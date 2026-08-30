using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using ChattyBones.Logic;

namespace ChattyBones
{
    /// <summary>
    /// The pack as a file in the config folder: writing it, reading it, and
    /// noticing when it changes.
    /// </summary>
    /// <remarks>
    /// Two files. <see cref="FileName"/> is the player's, written once if absent and
    /// never touched again; beside it a copy of what shipped, rewritten every launch,
    /// so a pack edited into a corner can always be compared against one that works.
    /// </remarks>
    internal static class PackFile
    {
        /// <summary>The pack the player edits.</summary>
        internal const string FileName = "ChattyBones.lines.yaml";

        /// <summary>The untouched copy of what we shipped, for reference.</summary>
        internal const string ReferenceFileName = "ChattyBones.lines.default.yaml";

        /// <summary>How long to wait after a change before reading the file.</summary>
        /// <remarks>
        /// A typical editor save writes a temp file, deletes the original and renames -
        /// three or four events in quick succession, and reading after the first gets
        /// half a file. Wait for the flurry to stop instead.
        /// </remarks>
        private const float SettleSeconds = 0.5f;

        /// <summary>At most this many complaints per load, so a badly broken file cannot flood the log.</summary>
        private const int MaxReported = 10;

        private static FileSystemWatcher _watcher;

        /// <summary>Set by the watcher's thread, read by Unity's. Volatile for that reason alone.</summary>
        private static volatile bool _changed;

        /// <summary>Counts down to a reload. Only ever touched from the main thread.</summary>
        private static float _settle;

        /// <summary>Where the player's pack lives.</summary>
        internal static string Location => Path.Combine(Paths.ConfigPath, FileName);

        /// <summary>Where the shipped copy is kept.</summary>
        private static string ReferenceLocation => Path.Combine(Paths.ConfigPath, ReferenceFileName);

        /// <summary>Put the files in place, read the pack, and start watching it.</summary>
        /// <returns>The player's pack, or the built-in one if theirs could not be read.</returns>
        internal static LinePack Load()
        {
            WriteFilesIfNeeded();

            LinePack pack = Read();

            // Only worth saying when there was a file to fail at. If writing it failed,
            // that has already been complained about in its own words.
            if (pack == null && File.Exists(Location))
            {
                ChattyBonesPlugin.Log.LogWarning(
                    FileName + " could not be used, so the lines built into the mod are being used instead.");
            }

            StartWatching();

            return pack ?? DefaultPack.Build();
        }

        /// <summary>Has the file changed and settled since we last looked?</summary>
        /// <returns>True on the one frame a reload is due.</returns>
        /// <param name="dt">Seconds since the last frame.</param>
        /// <remarks>
        /// A second change during the countdown restarts it, which is what makes the
        /// wait cover a whole save rather than the first event of one.
        /// </remarks>
        internal static bool ShouldReload(float dt)
        {
            if (_changed)
            {
                _changed = false;
                _settle = SettleSeconds;
            }

            if (_settle <= 0f)
            {
                return false;
            }

            _settle -= dt;

            return _settle <= 0f;
        }

        /// <summary>Read the pack again after the file changed.</summary>
        /// <returns>The new pack, or null to carry on with the one already loaded.</returns>
        /// <remarks>
        /// Null rather than the built-in pack: you are mid-edit, and the kind thing is
        /// to leave the skeletons saying what they said a minute ago.
        /// </remarks>
        internal static LinePack Reload()
        {
            LinePack pack = Read();

            if (pack == null)
            {
                ChattyBonesPlugin.Log.LogWarning(
                    FileName + " could not be used, so the skeletons are carrying on with the pack they had.");
            }

            return pack;
        }

        /// <summary>Stop watching, on the way out.</summary>
        internal static void StopWatching()
        {
            _watcher?.Dispose();
            _watcher = null;
        }

        /// <summary>Write the player's pack if they have not got one, and refresh the reference copy.</summary>
        private static void WriteFilesIfNeeded()
        {
            try
            {
                if (!File.Exists(Location))
                {
                    File.WriteAllText(Location, DefaultPack.Yaml);
                    ChattyBonesPlugin.Log.LogInfo("Wrote a starting line pack to " + Location + ".");
                }

                File.WriteAllText(ReferenceLocation, DefaultPack.Yaml);
            }
            catch (Exception e)
            {
                // Says nothing about what happens next, because either write can throw
                // and a failed reference copy still leaves the player's own pack
                // loading fine on the next line.
                ChattyBonesPlugin.Log.LogWarning(
                    "Could not write to " + Paths.ConfigPath + ": " + e.Message);
            }
        }

        /// <summary>Read and parse the player's pack.</summary>
        /// <returns>The pack, or null if there was nothing usable in the file.</returns>
        private static LinePack Read()
        {
            string yaml;

            if (!File.Exists(Location))
            {
                // Writing it has already been complained about in its own words.
                return null;
            }

            try
            {
                yaml = File.ReadAllText(Location);
            }
            catch (Exception e)
            {
                ChattyBonesPlugin.Log.LogWarning(
                    "Could not read " + FileName + ": " + e.Message + " Save the file again to have another go.");

                return null;
            }

            _ = PackReader.TryRead(yaml, out LinePack pack, out IReadOnlyList<string> problems);

            Report(problems);

            // What a failure costs depends on who asked, so the callers say so.
            return pack;
        }

        /// <summary>Put whatever the reader complained about in the log.</summary>
        /// <param name="problems">What it found, possibly none.</param>
        private static void Report(IReadOnlyList<string> problems)
        {
            int shown = Math.Min(problems.Count, MaxReported);

            for (int i = 0; i < shown; i++)
            {
                ChattyBonesPlugin.Log.LogWarning(FileName + ", " + problems[i]);
            }

            if (problems.Count > shown)
            {
                ChattyBonesPlugin.Log.LogWarning(
                    FileName + " has " + (problems.Count - shown) + " more problems, not listed.");
            }
        }

        /// <summary>Watch the player's pack for edits.</summary>
        /// <remarks>
        /// Wrapped because a watcher can fail for reasons outside the game entirely -
        /// a config folder on a network share, or a machine out of watch handles.
        /// </remarks>
        private static void StartWatching()
        {
            StopWatching();

            try
            {
                _watcher = new FileSystemWatcher(Paths.ConfigPath, FileName)
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                };

                _watcher.Changed += (_, _) => _changed = true;
                _watcher.Created += (_, _) => _changed = true;
                _watcher.Renamed += (_, _) => _changed = true;

                _watcher.EnableRaisingEvents = true;
            }
            catch (Exception e)
            {
                _watcher = null;

                ChattyBonesPlugin.Log.LogWarning(
                    "Could not watch " + FileName + " for changes, so edits to it need a restart: " + e.Message);
            }
        }
    }
}
