using System.Collections.Generic;
using System.IO;
using YamlDotNet.RepresentationModel;

namespace ChattyBones.Tests
{
    /// <summary>
    /// Pins the one thing about YamlDotNet that the line numbering depends on.
    /// </summary>
    /// <remarks>
    /// Context groups are numbered in the order the pack writes them, so that a pack
    /// author can decide which of two groups wins by putting it higher up. That only
    /// works if reading the file gives the groups back in the order they were written,
    /// and nothing in the type name promises it - <c>YamlMappingNode</c> reads like a
    /// dictionary, and a dictionary makes no such promise.
    ///
    /// It does preserve document order, and this is here so that a YamlDotNet upgrade
    /// which quietly stopped doing so would fail a test rather than desync two
    /// players' line refs - a symptom nobody would trace back to a package bump.
    /// </remarks>
    public class YamlOrderTests
    {
        [Fact]
        public void AMappingComesBackInTheOrderItWasWritten()
        {
            YamlStream stream = [];
            stream.Load(new StringReader("zeta: 1\nalpha: 2\nmiddle: 3\nbeta: 4\n"));

            YamlMappingNode map = (YamlMappingNode)stream.Documents[0].RootNode;
            List<string> seen = [];

            foreach (KeyValuePair<YamlNode, YamlNode> entry in map)
            {
                seen.Add(((YamlScalarNode)entry.Key).Value);
            }

            // Deliberately not alphabetical, so a sorted or hashed implementation fails.
            Assert.Equal(["zeta", "alpha", "middle", "beta"], seen);
        }
    }
}
