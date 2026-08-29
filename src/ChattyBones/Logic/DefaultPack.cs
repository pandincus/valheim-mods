using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace ChattyBones.Logic
{
    /// <summary>
    /// The pack the mod ships with, baked into the DLL.
    /// </summary>
    /// <remarks>
    /// This is the same text three times over: it is what gets parsed when the
    /// player has no pack of their own, it is what gets written to the config folder
    /// on first run, and it is what the reference copy alongside is refreshed from.
    /// Keeping one copy of it means the file a player opens is exactly the pack they
    /// are hearing, rather than a description of it that has drifted.
    ///
    /// It is an embedded resource rather than a string in this file so that it stays
    /// a real .yaml on disk while it is being edited - indentation you can see, and
    /// an editor that will tell you when you have broken it. Its logical name is
    /// pinned in both csproj files; the src one says why.
    /// </remarks>
    internal static class DefaultPack
    {
        /// <summary>What the resource is called, in both assemblies that carry it.</summary>
        internal const string ResourceName = "ChattyBones.lines.yaml";

        /// <summary>The shipped pack file, verbatim.</summary>
        internal static string Yaml { get; } = ReadResource();

        /// <summary>Parse the built-in pack.</summary>
        /// <returns>A pack with four personalities and a shared fallback.</returns>
        /// <remarks>
        /// Parsed on each call. It happens once at startup and a dozen times in the
        /// tests, so nothing is paying for it.
        /// </remarks>
        internal static LinePack Build()
        {
            if (!PackReader.TryRead(Yaml, out LinePack pack, out IReadOnlyList<string> problems))
            {
                // Not a player's mistake and not recoverable - the pack baked into the
                // DLL does not parse, which means the build is broken. DefaultPackTests
                // reads this same resource, so it should never get out of the repo.
                throw new InvalidDataException(
                    "The built-in ChattyBones pack does not parse: " + string.Join("; ", problems));
            }

            return pack;
        }

        /// <summary>Pull the pack file out of the assembly.</summary>
        /// <returns>Its contents.</returns>
        private static string ReadResource()
        {
            Assembly assembly = typeof(DefaultPack).Assembly;

            using Stream stream = assembly.GetManifestResourceStream(ResourceName)
                ?? throw new InvalidDataException(
                    "ChattyBones was built without its built-in pack. Expected an embedded resource named "
                    + ResourceName + " in " + assembly.GetName().Name + ".");

            using StreamReader reader = new(stream);

            return reader.ReadToEnd();
        }
    }
}
