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
    /// Two files land next to each other. <see cref="FileName"/> is the player's,
    /// written once if it is not there and never touched again. Beside it sits a
    /// copy of what the mod shipped with, rewritten every launch, so that "what did
    /// the original say?" and "what is new in this version?" are answerable without
    /// unpacking the DLL - and so that a pack edited into a corner can be started
    /// over from a file that is definitely right.
    /// </remarks>
    internal static class PackFile
    {
        /// <summary>The pack the player edits.</summary>
        internal const string FileName = "ChattyBones.lines.yaml";

        /// <summary>The untouched copy of what we shipped, for reference.</summary>
        private const string ReferenceFileName = "ChattyBones.lines.default.yaml";

        /// <summary>How long to wait after a change before reading the file.</summary>
        /// <remarks>
        /// Editors do not save a file once. A typical save writes a temporary file,
        /// deletes the original and renames, which is three or four events in quick
        /// succession - and reading after the first one gets a half-written file at
        /// best. Waiting for the flurry to stop is both simpler and more reliable than
        /// trying to interpret which event means "finished".
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
        /// <remarks>
        /// Never comes back empty-handed. A pack that will not parse is a thing to
        /// complain about in the log and carry on from, not a reason to have the whole
        /// squad stand there silently while the player wonders what they broke.
        /// </remarks>
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
        /// The watcher raises its events on a thread of its own, and touching Unity
        /// from there is a crash rather than a bug you get to read about. So it sets a
        /// flag and nothing else; the countdown and the reload both happen here, on
        /// the frame loop that calls this.
        ///
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
        /// Keeping the pack already in use is the point of returning null rather than
        /// falling back to the built-in one. You are mid-edit, you have just saved
        /// something with the indentation wrong, and the kind thing to do is leave the
        /// skeletons saying what they said a minute ago while the log tells you which
        /// line to look at.
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
                // Deliberately does not say what happens next, because at this point
                // we do not know. Either write can throw, and a reference copy that
                // could not be refreshed - somebody marked it read-only to stop it
                // being overwritten, which the file itself rather invites - leaves the
                // player's own pack loading perfectly on the next line.
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
                // Only reachable when writing it failed, which has already been
                // complained about. Saying so a second time in different words would
                // read like two separate things had gone wrong.
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

            // What a failure costs depends on who asked, so the callers say so rather
            // than this one guessing. At startup it means the built-in lines; on a
            // reload it means the pack already in use.
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
        /// Wrapped because a watcher is one of the few things here that can fail for
        /// reasons entirely outside the game - a config folder on a network share, or
        /// a machine that has run out of them. Losing hot-reload is a small loss;
        /// failing to load over it would not be.
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
