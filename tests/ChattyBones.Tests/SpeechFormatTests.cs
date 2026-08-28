using System.Collections.Generic;
using ChattyBones.Logic;

namespace ChattyBones.Tests
{
    /// <summary>Covers the hex parsing, the wrapping and the sender id.</summary>
    /// <remarks>
    /// All of this used to sit next to the drawing code where nothing could reach it,
    /// on the grounds that the render layer needs a running game. Most of it does.
    /// None of this does.
    /// </remarks>
    public class SpeechFormatTests
    {
        [Theory]
        [InlineData("#C8FFC8", "<color=#C8FFC8>")]
        [InlineData("C8FFC8", "<color=#C8FFC8>")]
        [InlineData("  #fff  ", "<color=#fff>")]
        [InlineData("#FFFFFFAA", "<color=#FFFFFFAA>")]
        public void AHexCodeBecomesAColourTag(string configured, string expected)
        {
            Assert.True(SpeechFormat.TryColourTag(configured, out string tag));
            Assert.Equal(expected, tag);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("#GGGGGG")]
        [InlineData("green")]
        [InlineData("#FFFFF")]
        public void AnythingElseIsRefused(string configured)
        {
            Assert.False(SpeechFormat.TryColourTag(configured, out string tag));
            Assert.Null(tag);
        }

        [Fact]
        public void FourHexDigitsAreRefusedOnPurpose()
        {
            // TMP would accept #RGBA, so this is a decision rather than an oversight.
            // Four digits is easy to type when you meant three or six, and the result
            // would be a silently transparent bubble - much worse than being told the
            // code is wrong.
            Assert.False(SpeechFormat.TryColourTag("#FFF8", out _));
        }

        [Fact]
        public void AWrappedLineKeepsItsOwnAngleBrackets()
        {
            // Valheim strips these from player chat in OnNewChatMessage, which we skip,
            // so a pack author who wants their own markup can have it.
            string wrapped = SpeechFormat.Wrap("a <b>bold</b> claim", "<color=#FFF>");

            Assert.Equal("<color=#FFF>a <b>bold</b> claim</color>", wrapped);
        }

        [Fact]
        public void NoTagMeansTheLineComesBackUntouched()
        {
            string line = "My bones are itchy.";

            Assert.Same(line, SpeechFormat.Wrap(line, null));
        }

        [Fact]
        public void TheSameSkeletonAlwaysGetsTheSameSenderId()
        {
            Assert.Equal(SpeechFormat.SenderId(12345L, 67U), SpeechFormat.SenderId(12345L, 67U));
        }

        [Fact]
        public void OneCreatorsSkeletonsNeverCollide()
        {
            // Chat keys bubbles by sender, so a collision means two skeletons fighting
            // over one bubble. The counter only touches the low 32 bits, so within a
            // single creator this should be exactly injective.
            HashSet<long> seen = [];

            for (uint i = 0; i < 10000; i++)
            {
                Assert.True(seen.Add(SpeechFormat.SenderId(76561198000000000L, i)));
            }
        }

        [Fact]
        public void TwoCreatorsDoNotCollideEither()
        {
            HashSet<long> seen = [];

            for (uint i = 0; i < 2000; i++)
            {
                Assert.True(seen.Add(SpeechFormat.SenderId(76561198000000000L, i)));
                Assert.True(seen.Add(SpeechFormat.SenderId(76561198000000001L, i)));
            }
        }

        [Fact]
        public void AnInvalidZdoDoesNotProduceASenderIdOfZero()
        {
            // A creature with no valid ZNetView gives back ZDOID.None, which is (0, 0).
            // Without a guard that maps to a real-looking id shared by every such
            // creature, so they would all fight over one bubble.
            Assert.NotEqual(0L, SpeechFormat.SenderId(0L, 0U));
        }

        [Fact]
        public void ABadColourIsComplainedAboutOnceRatherThanEveryLine()
        {
            ColourTagCache cache = new();
            int rejections = 0;

            for (int i = 0; i < 5; i++)
            {
                _ = cache.TryTagFor("#GGGGGG", out _, out bool rejected);
                if (rejected)
                {
                    rejections++;
                }
            }

            Assert.Equal(1, rejections);
        }

        [Fact]
        public void ChangingTheColourReEvaluatesIt()
        {
            ColourTagCache cache = new();

            Assert.True(cache.TryTagFor("#FFFFFF", out string white, out _));
            Assert.Equal("<color=#FFFFFF>", white);

            Assert.False(cache.TryTagFor("#GGGGGG", out _, out bool rejected));
            Assert.True(rejected);

            Assert.True(cache.TryTagFor("#C8FFC8", out string green, out _));
            Assert.Equal("<color=#C8FFC8>", green);
        }

        [Fact]
        public void ClearingTheSettingDoesNotDisturbTheCache()
        {
            // Blank short-circuits before the cache is consulted, so going bad, blank,
            // bad again still only complains once. Pinning that deliberately, because
            // it is a consequence of the early return rather than something anyone
            // designed.
            ColourTagCache cache = new();

            _ = cache.TryTagFor("#GGGGGG", out _, out bool first);
            _ = cache.TryTagFor("", out _, out bool blank);
            _ = cache.TryTagFor("#GGGGGG", out _, out bool again);

            Assert.True(first);
            Assert.False(blank);
            Assert.False(again);
        }

        [Fact]
        public void NoColourSetIsNotARejection()
        {
            ColourTagCache cache = new();

            Assert.False(cache.TryTagFor("", out string tag, out bool rejected));
            Assert.Null(tag);
            Assert.False(rejected);
        }
    }
}
