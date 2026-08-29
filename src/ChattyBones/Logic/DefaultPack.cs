using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace ChattyBones.Logic
{
    /// <summary>
    /// The pack the mod ships with, baked into the DLL.
    /// </summary>
    /// <remarks>
    /// The same text three times over - parsed when the player has no pack, written
    /// to the config folder on first run, and refreshed into the reference copy - so
    /// the file a player opens is exactly the pack they are hearing.
    ///
    /// An embedded resource rather than a string here, so it stays a real .yaml while
    /// being edited. Its logical name is pinned in both csproj files; the src one
    /// says why.
    /// </remarks>
    internal static class DefaultPack
    {
        /// <summary>What the resource is called, in both assemblies that carry it.</summary>
        internal const string ResourceName = "ChattyBones.lines.yaml";

        /// <summary>The shipped pack file, verbatim.</summary>
        internal static string Yaml { get; } = ReadResource();

        /// <summary>Parse the built-in pack.</summary>
        /// <returns>The shipped pack, parsed.</returns>
        /// <remarks>Parsed on each call; it happens once at startup.</remarks>
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
