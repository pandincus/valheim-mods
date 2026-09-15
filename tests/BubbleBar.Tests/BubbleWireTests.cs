using BubbleBar.Logic;

namespace BubbleBar.Tests
{
    public class BubbleWireTests
    {
        [Fact]
        public void ACharacterThatHasPublishedNothingReadsAsNoBarrier()
        {
            // Both keys absent, which a ZDO reports as zero. This is every character in
            // the world that has never cast a barrier, so it is the common case rather
            // than a defensive one.
            Assert.False(BubbleWire.Read(fraction: 0f, expiry: 0L, now: 1234L).IsPresent);
        }

        [Fact]
        public void ALiveBarrierReadsWhatWasPublished()
        {
            ShieldReading reading = BubbleWire.Read(fraction: 0.4f, expiry: 1300L, now: 1234L);

            Assert.True(reading.IsPresent);
            Assert.Equal(0.4f, reading.Fraction, 5);
        }

        [Fact]
        public void AnExpiredBarrierIsDroppedEvenThoughAFractionIsStillPublished()
        {
            // The orphan case, and the reason expiry is on the wire at all. If a
            // creature's ZDO ownership moves, the new owner has no SE_Shield to publish
            // from - SEMan is a plain C# list on the client that had it - so the last
            // fraction written would sit there for ever. The deadline retires it.
            Assert.False(BubbleWire.Read(fraction: 0.9f, expiry: 1200L, now: 1234L).IsPresent);
        }

        [Fact]
        public void AnEmptyBarrierStillReadsAsPresentWhileItsDeadlineHolds()
        {
            // Expiry is the "is there one" flag, not the fraction, so a barrier at zero
            // draws an empty bar for the frame before it pops rather than vanishing.
            ShieldReading reading = BubbleWire.Read(fraction: 0f, expiry: 1300L, now: 1234L);

            Assert.True(reading.IsPresent);
            Assert.Equal(0f, reading.Fraction);
        }

        [Fact]
        public void AFractionOutsideItsRangeIsClamped()
        {
            Assert.Equal(1f, BubbleWire.Read(1.5f, 1300L, 1234L).Fraction);
            Assert.False(BubbleWire.Read(-0.2f, 1300L, 1234L).IsPresent);
            Assert.False(BubbleWire.Read(float.NaN, 1300L, 1234L).IsPresent);
        }

        [Fact]
        public void ASmallChangeIsNotWorthAWrite()
        {
            // A 400-point barrier moving by one point is 0.0025, below the 1/256 floor.
            Assert.False(BubbleWire.ShouldWrite(published: 0.5f, current: 0.5f - 0.0025f));
        }

        [Fact]
        public void AChangeBigEnoughToSeeIsWritten()
        {
            Assert.True(BubbleWire.ShouldWrite(published: 0.5f, current: 0.4f));
            Assert.True(BubbleWire.ShouldWrite(published: 0.4f, current: 0.5f));
        }

        [Fact]
        public void ReachingZeroIsAlwaysWritten()
        {
            // Whatever the epsilon says. It is the last value anyone sees before the
            // barrier breaks, and rounding it away leaves a sliver of bar on screen.
            Assert.True(BubbleWire.ShouldWrite(published: 0.001f, current: 0f));
        }

        [Fact]
        public void ZeroIsNotWrittenTwice()
        {
            Assert.False(BubbleWire.ShouldWrite(published: 0f, current: 0f));
        }

        [Fact]
        public void ADeadlineThatHasOnlyJitteredIsNotRepublished()
        {
            // now + remaining is very nearly constant while a barrier runs down, because both
            // halves move at the same rate. What is left is the difference between network time
            // and the status effect's own clock, which is far inside half a second.
            Assert.False(BubbleWire.ShouldWriteExpiry(published: 1_300_000L, current: 1_300_200L));
            Assert.False(BubbleWire.ShouldWriteExpiry(published: 1_300_000L, current: 1_299_800L));
        }

        [Fact]
        public void ARefreshMovesTheDeadlineFarEnoughToRepublish()
        {
            // A recast throws it forward by the barrier's whole duration.
            Assert.True(BubbleWire.ShouldWriteExpiry(published: 1_300_000L, current: 1_330_000L));
        }

        [Fact]
        public void TheFirstDeadlineIsAlwaysPublished()
        {
            // A character that has never published reads 0, which is further than half a second
            // from any real deadline - so the first write needs no special case.
            Assert.True(BubbleWire.ShouldWriteExpiry(published: 0L, current: 1_300_000L));
        }

        [Theory]
        // An ordinary world, and one that has been played for about four hundred days. The
        // second is the point: the deadline used to be a float, and past roughly 33.5 million
        // seconds a float's spacing exceeds two, so "now plus a couple of seconds" rounded
        // straight back to "now". Milliseconds in a long are exact at any age.
        [InlineData(1_000_000L)]
        [InlineData(34_000_000_000L)]
        public void ABarrierRunningDownIsNotRepublishedAtAnyWorldAge(long startMillis)
        {
            // Walk a thirty-second barrier down in tenth-of-a-second sweeps. The deadline is
            // now + remaining, so it should sit still; what moves it is the two clocks
            // disagreeing by up to a frame, which must stay inside the threshold.
            long published = startMillis + 30_000L;
            int writes = 0;

            for (int step = 1; step <= 300; step++)
            {
                long now = startMillis + (step * 100L);
                float remaining = 30f - (step * 0.1f) + (step % 3 == 0 ? 0.016f : -0.016f);
                long expiry = now + (long)(remaining * 1000f);

                if (BubbleWire.ShouldWriteExpiry(published, expiry))
                {
                    published = expiry;
                    writes++;
                }
            }

            Assert.Equal(0, writes);
        }

        [Theory]
        [InlineData(1_000_000L)]
        [InlineData(34_000_000_000L)]
        public void AShortGraceWindowStaysAheadOfTheClockAtAnyWorldAge(long nowMillis)
        {
            // The grace window is two seconds, and it is the whole reason a barrier with no
            // duration is visible to anyone at all. As a float this silently became a deadline
            // in the past on a long-lived server, and the barrier vanished from every remote
            // screen with nothing in any log.
            long expiry = nowMillis + 2_000L;

            Assert.True(expiry > nowMillis);
            Assert.True(BubbleWire.Read(fraction: 0.5f, expiry: expiry, now: nowMillis).IsPresent);
        }

        [Fact]
        public void TheThresholdIsWhatDecidesARepublish()
        {
            // Guards the walk above from going vacuous. It once asserted a quantity that
            // cancelled to a constant, so it passed with the threshold set to almost anything;
            // these two pin the boundary itself.
            Assert.False(BubbleWire.ShouldWriteExpiry(1000L, 1000L + BubbleWire.ExpiryDriftMillis));
            Assert.True(BubbleWire.ShouldWriteExpiry(1000L, 1000L + BubbleWire.ExpiryDriftMillis + 1L));
        }
    }
}
