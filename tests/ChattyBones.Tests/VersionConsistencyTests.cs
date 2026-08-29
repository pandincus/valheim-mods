using System;
using System.IO;
using System.Text.RegularExpressions;

namespace ChattyBones.Tests
{
    /// <summary>
    /// Guards the one release step that nothing else checks: the version number
    /// is written down in three places and they have to agree.
    /// </summary>
    /// <remarks>
    /// Practically speaking, two of the three are already covered - package.ps1
    /// refuses to run when the csproj and the manifest disagree. It cannot see
    /// Plugin.cs, though, because BepInPlugin needs a compile-time constant and
    /// that means a literal in the source. So the third one was a manual check,
    /// and manual checks get skipped on the release you are in a hurry for.
    ///
    /// Reading the files off disk is unusual for a unit test, but the thing under
    /// test genuinely is the files: there is no object that holds all three
    /// numbers at once, which is the whole problem.
    /// </remarks>
    public class VersionConsistencyTests
    {
        [Fact]
        public void CsprojPluginAndManifestAgreeOnTheVersion()
        {
            string mod = Path.Combine(RepoRoot(), "src", "ChattyBones");

            string csproj = Extract(
                Path.Combine(mod, "ChattyBones.csproj"),
                @"<Version>([^<]+)</Version>");
            string plugin = Extract(
                Path.Combine(mod, "Plugin.cs"),
                @"PluginVersion\s*=\s*""([^""]+)""");
            string manifest = Extract(
                Path.Combine(mod, "manifest.json"),
                @"""version_number""\s*:\s*""([^""]+)""");

            Assert.Equal(csproj, plugin);
            Assert.Equal(csproj, manifest);
        }

        [Fact]
        public void TheManifestAndTheTestsAgreeOnWhichYamlDotNetThisIs()
        {
            // The trap this exists for: the Thunderstore package is 16.3.1 and the
            // library inside it is 16.3.0, so the two numbers are *supposed* to differ
            // in the last digit. That makes an honest mistake indistinguishable from
            // the correct state by eye. Compare the major and minor, which do have to
            // match, and leave the patch digit alone.
            //
            // Worth having because the failure is the quiet kind: the mod compiles
            // against whatever DLL is in the profile while the tests keep exercising
            // whatever NuGet restores, so they can pass while the game breaks.
            string mod = Path.Combine(RepoRoot(), "src", "ChattyBones");

            string manifest = Extract(
                Path.Combine(mod, "manifest.json"),
                @"ValheimModding-YamlDotNet-(\d+\.\d+)\.\d+");
            string tests = Extract(
                Path.Combine(RepoRoot(), "tests", "ChattyBones.Tests", "ChattyBones.Tests.csproj"),
                @"PackageReference\s+Include=""YamlDotNet""\s+Version=""(\d+\.\d+)\.\d+""");

            Assert.Equal(manifest, tests);
        }

        /// <summary>Pull the first capture group out of a file, or fail saying which file.</summary>
        private static string Extract(string path, string pattern)
        {
            Assert.True(File.Exists(path), "Expected to find " + path);

            Match match = Regex.Match(File.ReadAllText(path), pattern);
            Assert.True(match.Success, "No version matching /" + pattern + "/ in " + path);

            return match.Groups[1].Value.Trim();
        }

        /// <summary>
        /// Walk up from the test assembly until we find the solution file.
        /// </summary>
        /// <remarks>
        /// The test runs out of bin/Debug/net10.0, and how deep that is below the
        /// repo depends on configuration and target framework. Searching for a
        /// landmark beats counting "../" and hoping.
        /// </remarks>
        private static string RepoRoot()
        {
            DirectoryInfo dir = new(AppContext.BaseDirectory);

            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "ValheimMods.sln")))
            {
                dir = dir.Parent;
            }

            Assert.True(dir != null, "Could not find ValheimMods.sln above " + AppContext.BaseDirectory);
            return dir.FullName;
        }
    }
}
