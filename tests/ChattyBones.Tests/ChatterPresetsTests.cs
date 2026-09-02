using ChattyBones.Logic;

namespace ChattyBones.Tests
{
    /// <summary>
    /// Covers the one word a player picks, and the four numbers it stands for.
    /// </summary>
    /// <remarks>
    /// The one worth reading is <see cref="SometimesIsExactlyWhatTheModShippedWith"/>.
    /// Presets arrived after the numbers did, so the level that is the default has to
    /// reproduce the old behaviour exactly - otherwise everybody's squad quietly
    /// changes character on upgrade, and nothing in the config would say why.
    /// </remarks>
    public class ChatterPresetsTests
    {
        [Fact]
        public void SometimesIsExactlyWhatTheModShippedWith()
        {
            Assert.True(ChatterPresets.TryGaps(ChatterAmount.Sometimes, out ChatterGaps gaps));

            Assert.Equal(2.5f, gaps.MinGapSeconds);
            Assert.Equal(8f, gaps.SpeakerCooldownSeconds);
            Assert.Equal(6f, gaps.SquadEchoWindowSeconds);

            Assert.True(ChatterPresets.TryIdleSeconds(ChatterAmount.Sometimes, out float idle));
            Assert.Equal(45f, idle);
        }

        // A loop rather than a Theory throughout this file: ChatterAmount is internal,
        // and a public test method taking one as a parameter will not compile.
        [Fact]
        public void TheTwoThatNameNoNumbersSaySo()
        {
            // Both mean "do not read these", for opposite reasons - one is switched off
            // and the other is deferring to the advanced settings. Neither can answer.
            foreach (ChatterAmount amount in new[] { ChatterAmount.Never, ChatterAmount.Custom })
            {
                Assert.False(ChatterPresets.TryGaps(amount, out _), amount + " named gaps");
                Assert.False(ChatterPresets.TryIdleSeconds(amount, out _), amount + " named an interval");
            }
        }

        [Fact]
        public void EveryAmountThatNamesOneAnswersBoth()
        {
            foreach (ChatterAmount amount in
                new[] { ChatterAmount.Rarely, ChatterAmount.Sometimes, ChatterAmount.Often, ChatterAmount.Always })
            {
                Assert.True(ChatterPresets.TryGaps(amount, out ChatterGaps gaps), amount + " named no gaps");
                Assert.True(ChatterPresets.TryIdleSeconds(amount, out float idle), amount + " named no interval");

                Assert.True(gaps.MinGapSeconds > 0f);
                Assert.True(gaps.SpeakerCooldownSeconds > 0f);
                Assert.True(gaps.SquadEchoWindowSeconds > 0f);
                Assert.True(idle > 0f);
            }
        }

        [Fact]
        public void AskingForMoreNeverGivesYouLess()
        {
            // The ladder is the whole promise a preset makes, and it is the one thing a
            // typo in the table could break while every other test stayed green.
            ChatterAmount[] louder =
                [ChatterAmount.Rarely, ChatterAmount.Sometimes, ChatterAmount.Often, ChatterAmount.Always];

            for (int i = 1; i < louder.Length; i++)
            {
                Assert.True(ChatterPresets.TryGaps(louder[i - 1], out ChatterGaps quieter));
                Assert.True(ChatterPresets.TryGaps(louder[i], out ChatterGaps chattier));

                Assert.True(
                    chattier.MinGapSeconds < quieter.MinGapSeconds,
                    louder[i] + " does not shorten the squad's gap against " + louder[i - 1]);
                Assert.True(
                    chattier.SpeakerCooldownSeconds < quieter.SpeakerCooldownSeconds,
                    louder[i] + " does not shorten the speaker cooldown against " + louder[i - 1]);
                Assert.True(
                    chattier.SquadEchoWindowSeconds < quieter.SquadEchoWindowSeconds,
                    louder[i] + " does not shorten the echo window against " + louder[i - 1]);

                Assert.True(ChatterPresets.TryIdleSeconds(louder[i - 1], out float slower));
                Assert.True(ChatterPresets.TryIdleSeconds(louder[i], out float faster));

                Assert.True(
                    faster < slower,
                    louder[i] + " does not idle more often than " + louder[i - 1]);
            }
        }

        [Fact]
        public void AnIndividualStaysQuieterThanTheSquad()
        {
            // The property that makes five skeletons read as five people rather than as
            // one with a lot to say. It has to survive every rung, not just the default.
            foreach (ChatterAmount amount in
                new[] { ChatterAmount.Rarely, ChatterAmount.Sometimes, ChatterAmount.Often, ChatterAmount.Always })
            {
                Assert.True(ChatterPresets.TryGaps(amount, out ChatterGaps gaps));

                Assert.True(
                    gaps.SpeakerCooldownSeconds > gaps.MinGapSeconds,
                    amount + " lets one skeleton speak as often as the whole squad");
            }
        }
    }
}
