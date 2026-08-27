using ChattyBones.Logic;

namespace ChattyBones.Tests
{
    /// <summary>
    /// Covers the packing that carries an utterance to other players.
    /// </summary>
    /// <remarks>
    /// Most of this is round-tripping, which sounds dull until you remember the
    /// value is going over a wire to a client that might be running a different
    /// build of the mod. The two tests I actually care about are the one about a
    /// counter above 127 (where the packed int goes negative) and the one about an
    /// unknown event, because both are the sort of thing that works perfectly in
    /// single player and falls over the first time somebody joins your world.
    /// </remarks>
    public class UtteranceTests
    {
        private const int Greydwarf = -1234567;
        private const int NoSubject = 0;

        [Fact]
        public void AnUtteranceSurvivesTheRoundTrip()
        {
            Utterance sent = new(7, ChatterEvent.TargetAcquired, 4242, Greydwarf);

            Assert.True(Utterance.TryUnpack(sent.Pack(), Greydwarf, out Utterance got));

            Assert.Equal(7, got.Counter);
            Assert.Equal(ChatterEvent.TargetAcquired, got.Kind);
            Assert.Equal(4242, got.Seed);
            Assert.Equal(Greydwarf, got.Subject);
        }

        [Fact]
        public void EveryEventSurvivesTheRoundTrip()
        {
            foreach (ChatterEvent kind in System.Enum.GetValues(typeof(ChatterEvent)))
            {
                Utterance sent = new(1, kind, 0, NoSubject);

                Assert.True(Utterance.TryUnpack(sent.Pack(), NoSubject, out Utterance got));
                Assert.Equal(kind, got.Kind);
            }
        }

        [Fact]
        public void ACounterPastHalfwaySurvivesEvenThoughThePackedValueGoesNegative()
        {
            // 200 sets the top bit of the top byte, so the packed int is negative.
            // A ZDO holds a signed int and does not mind.
            //
            // I originally wrote this expecting it to be the test that catches a
            // signed shift in TryUnpack, then checked by actually making that change,
            // and it passed anyway - masking each field afterwards discards the
            // smeared sign bits. So this covers the large-counter round trip, which
            // is worth having, and nothing more. Worth knowing before you lean on it.
            Utterance sent = new(200, ChatterEvent.Died, 65535, NoSubject);

            Assert.True(sent.Pack() < 0);
            Assert.True(Utterance.TryUnpack(sent.Pack(), NoSubject, out Utterance got));

            Assert.Equal(200, got.Counter);
            Assert.Equal(ChatterEvent.Died, got.Kind);
            Assert.Equal(65535, got.Seed);
        }

        [Fact]
        public void TheBiggestSeedWeAdvertiseActuallyFits()
        {
            Utterance sent = new(1, ChatterEvent.Idle, Utterance.MaxSeed, NoSubject);

            Assert.True(Utterance.TryUnpack(sent.Pack(), NoSubject, out Utterance got));
            Assert.Equal(Utterance.MaxSeed, got.Seed);
        }

        [Fact]
        public void AFieldNobodyHasWrittenIsNotAnUtterance()
        {
            // This is what every skeleton's ZDO field reads as until the first time
            // it says anything, so it is by far the most common thing we unpack.
            // Getting it wrong would have every skeleton greet you the moment it came
            // into range on any client.
            Assert.False(Utterance.TryUnpack(0, NoSubject, out _));
        }

        [Fact]
        public void AnEventFromANewerVersionIsIgnoredRatherThanGuessedAt()
        {
            // Someone on a later build tells us about event 99, which did not exist
            // when this copy was compiled. We should say nothing - not throw, and not
            // pick whatever line happens to live at that index.
            const int unknownEvent = 99;
            int packed = (1 << 24) | (unknownEvent << 16) | 5;

            Assert.False(Utterance.TryUnpack(packed, NoSubject, out _));
        }

        [Fact]
        public void ASubjectIsCarriedThroughUntouched()
        {
            // Prefab hashes are cheerfully negative and we never interpret them, so
            // whatever goes in comes out. int.MinValue is the nastiest one available.
            Assert.True(Utterance.TryUnpack(
                new Utterance(1, ChatterEvent.Killed, 0, NoSubject).Pack(),
                int.MinValue,
                out Utterance got));

            Assert.Equal(int.MinValue, got.Subject);
        }

        [Fact]
        public void TheCounterWalksUpAndWrapsPastZero()
        {
            Assert.Equal(1, Utterance.NextCounter(0));
            Assert.Equal(2, Utterance.NextCounter(1));
            Assert.Equal(255, Utterance.NextCounter(254));

            // The wrap skips 0, because 0 is how we recognise a field nobody has
            // written to. Coming back around to 1 is fine - anyone who saw the last
            // 1 has seen 254 utterances since.
            Assert.Equal(1, Utterance.NextCounter(255));
        }

        [Fact]
        public void EveryCounterInTheCycleStaysUnpackable()
        {
            // Walking the whole cycle rather than spot-checking, because an
            // off-by-one at the wrap would give a packed value of 0, which reads as
            // "never spoken" and would silence that skeleton until it happened to say
            // something else.
            int counter = 0;

            for (int i = 0; i < 512; i++)
            {
                counter = Utterance.NextCounter(counter);
                Utterance sent = new(counter, ChatterEvent.Hurt, i & 0xFFFF, NoSubject);

                Assert.NotEqual(0, sent.Pack());
                Assert.True(Utterance.TryUnpack(sent.Pack(), NoSubject, out Utterance got));
                Assert.Equal(counter, got.Counter);
            }
        }

        [Fact]
        public void ConsecutiveUtterancesAlwaysLookDifferent()
        {
            // The whole reason the counter exists: a watching client polls the field
            // and can only notice a change. The same skeleton saying the same kind of
            // thing about the same target twice running still has to produce a
            // different packed value, or the second one is invisible.
            Utterance first = new(4, ChatterEvent.TargetAcquired, 99, Greydwarf);
            Utterance second = new(Utterance.NextCounter(4), ChatterEvent.TargetAcquired, 99, Greydwarf);

            Assert.NotEqual(first.Pack(), second.Pack());
        }
    }
}
