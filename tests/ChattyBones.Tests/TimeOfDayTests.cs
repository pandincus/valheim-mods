using System.Collections.Generic;
using ChattyBones.Logic;

namespace ChattyBones.Tests
{
    /// <summary>
    /// Covers where each quarter of the day starts and stops.
    /// </summary>
    /// <remarks>
    /// Short, and the only reason it exists is that the boundaries are borrowed from
    /// vanilla rather than chosen. If a game update moves <c>CalculateAfternoon</c> off
    /// 0.5 the mod is quietly disagreeing with the sky, and a failing test naming the
    /// number is a much better way to find that out than a line about the evening
    /// arriving at noon.
    ///
    /// Note the fractions stop at 0.99 rather than 1.0. Both ends of the range are the
    /// same instant - midnight - and only one of them can be "night", so the table gives
    /// 1.0 to evening. <c>GetDayFraction</c> comes out of a <c>Mathf.Repeat</c> and does
    /// not land there in practice, and pinning it would be pinning the wrong end of the
    /// day.
    /// </remarks>
    public class TimeOfDayTests
    {
        [Theory]
        [InlineData(0.00f, "night")]
        [InlineData(0.10f, "night")]
        [InlineData(0.30f, "morning")]
        [InlineData(0.49f, "morning")]
        [InlineData(0.60f, "afternoon")]
        [InlineData(0.80f, "evening")]
        [InlineData(0.99f, "evening")]
        public void EachQuarterOfTheDayHasAName(float fraction, string expected)
        {
            Assert.Equal(expected, TimeOfDay.Band(fraction));
        }

        [Theory]
        [InlineData(0.25f, "morning")]
        [InlineData(0.50f, "afternoon")]
        [InlineData(0.75f, "evening")]
        public void ABoundaryBelongsToTheBandThatStartsThere(float fraction, string expected)
        {
            Assert.Equal(expected, TimeOfDay.Band(fraction));
        }

        [Theory]
        [InlineData(0.25f)]
        [InlineData(0.40f)]
        [InlineData(0.50f)]
        [InlineData(0.74f)]
        public void MorningAndAfternoonAgreeWithVanillasOwnDay(float fraction)
        {
            // CalculateDay is 0.25 <= f <= 0.75. Everything strictly inside that span has
            // to be one of the two daylight bands - the closed upper end is left out on
            // purpose, because vanilla calls 0.75 afternoon *and* night and we give it to
            // evening. TimeOfDay's own remark covers that overlap; this checks the part
            // where the game has one answer rather than two. This is the half of the borrowing that matters:
            // splitting the night in two is ours to decide, but calling something
            // "morning" while the game has the sun down would be a plain contradiction.
            string band = TimeOfDay.Band(fraction);

            Assert.True(band is "morning" or "afternoon", fraction + " gave " + band);
        }

        [Theory]
        [InlineData(0.00f)]
        [InlineData(0.24f)]
        [InlineData(0.75f)]
        [InlineData(0.99f)]
        public void EveningAndNightAgreeWithVanillasOwnNight(float fraction)
        {
            // The other half. CalculateNight is f <= 0.25 || f >= 0.75 - closed at both
            // ends - so both of our dark bands have to sit inside it.
            string band = TimeOfDay.Band(fraction);

            Assert.True(band is "evening" or "night", fraction + " gave " + band);
        }


        [Fact]
        public void EveryBandIsAValueAPackCanActuallyWrite()
        {
            // The join between the bands and the pack's vocabulary. EventKey takes its
            // "time" values from TimeOfDay.All, and the resolver spells its contexts from
            // the same list - so a fifth band cannot leave either behind. This asserts the
            // half a test can reach; the resolver lives outside Logic and is a dictionary
            // built from the list rather than a second copy of it.
            foreach (string band in TimeOfDay.All)
            {
                Assert.True(
                    EventKey.TryParse("Idle[time=" + band + "]", out EventKey key, out string problem),
                    problem);

                Assert.Equal("time=" + band, key.Context);
            }
        }

        [Fact]
        public void EveryBandIsReachable()
        {
            // Band is a chain of ranges, so a mistyped boundary can make one of them
            // unreachable without any single case noticing. Sweeping the day proves each
            // name is actually produced by something.
            HashSet<string> seen = [];

            for (int i = 0; i <= 1000; i++)
            {
                _ = seen.Add(TimeOfDay.Band(i / 1000f));
            }

            Assert.Equal(TimeOfDay.All.Length, seen.Count);
            Assert.All(TimeOfDay.All, band => Assert.Contains(band, seen));
        }
    }
}
