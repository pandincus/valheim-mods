using ChattyBones.Logic;

namespace ChattyBones.Tests
{
    /// <summary>
    /// Covers the rules that stop five skeletons talking over each other.
    /// </summary>
    /// <remarks>
    /// Every test hands the budget its own timestamps, so "wait eight seconds" is
    /// just a bigger number and the whole file runs instantly. The settings below
    /// use round numbers rather than the real defaults - if a test says 3 seconds
    /// have passed, you should not have to go and look up whether that is enough.
    /// </remarks>
    public class ChatterBudgetTests
    {
        private const long Alice = 1001;
        private const long Bob = 1002;
        private const long Carol = 1003;

        /// <summary>A greydwarf, as far as any of these tests are concerned.</summary>
        private const int Greydwarf = 555;
        private const int Seeker = 777;

        /// <summary>Nothing in particular - what Hurt and Idle pass as their subject.</summary>
        private const int NoSubject = 0;

        private static ChatterSettings Settings()
        {
            return new()
            {
                MinGapSeconds = 2f,
                PreemptGapSeconds = 0.5f,
                SpeakerCooldownSeconds = 10f,
                SquadEchoWindowSeconds = 6f,
            };
        }

        private static ChatterBudget Budget()
        {
            return new(Settings());
        }

        [Fact]
        public void TheFirstThingAnyoneSaysIsAlwaysAllowed()
        {
            ChatterBudget budget = Budget();

            Assert.True(budget.TryClaim(Alice, ChatterEvent.Idle, NoSubject, 0f));
        }

        [Fact]
        public void NobodyElseSpeaksInsideTheGlobalGap()
        {
            ChatterBudget budget = Budget();
            Assert.True(budget.TryClaim(Alice, ChatterEvent.Idle, NoSubject, 0f));

            // Bob has said nothing at all, so only the squad-wide gap is stopping him.
            Assert.False(budget.TryClaim(Bob, ChatterEvent.Idle, NoSubject, 1.9f));
        }

        [Fact]
        public void SomebodyElseSpeaksOnceTheGlobalGapHasPassed()
        {
            ChatterBudget budget = Budget();
            Assert.True(budget.TryClaim(Alice, ChatterEvent.Idle, NoSubject, 0f));

            Assert.True(budget.TryClaim(Bob, ChatterEvent.Idle, NoSubject, 2f));
        }

        [Fact]
        public void OneSkeletonStaysQuietForMuchLongerThanTheSquadDoes()
        {
            ChatterBudget budget = Budget();
            Assert.True(budget.TryClaim(Alice, ChatterEvent.Idle, NoSubject, 0f));

            // The squad gap (2s) is long gone, but Alice's own cooldown is 10s.
            // This is the effect we want: the group keeps chatting, any one member
            // of it does not.
            Assert.False(budget.TryClaim(Alice, ChatterEvent.Idle, NoSubject, 5f));
            Assert.True(budget.TryClaim(Alice, ChatterEvent.Idle, NoSubject, 10f));
        }

        [Fact]
        public void OnlyOneSkeletonCallsOutAGivenEnemy()
        {
            ChatterBudget budget = Budget();

            // All three charge the same greydwarf at once, which is exactly what a
            // squad does. Bob is refused for the echo even though 3s is past the
            // squad gap and he has never spoken.
            Assert.True(budget.TryClaim(Alice, ChatterEvent.TargetAcquired, Greydwarf, 0f));
            Assert.False(budget.TryClaim(Bob, ChatterEvent.TargetAcquired, Greydwarf, 3f));
            Assert.False(budget.TryClaim(Carol, ChatterEvent.TargetAcquired, Greydwarf, 5f));
        }

        [Fact]
        public void TheSameEnemyIsWorthMentioningAgainMuchLater()
        {
            ChatterBudget budget = Budget();
            Assert.True(budget.TryClaim(Alice, ChatterEvent.TargetAcquired, Greydwarf, 0f));

            Assert.True(budget.TryClaim(Bob, ChatterEvent.TargetAcquired, Greydwarf, 6f));
        }

        [Fact]
        public void ADifferentEnemyIsWorthItsOwnRemark()
        {
            ChatterBudget budget = Budget();
            Assert.True(budget.TryClaim(Alice, ChatterEvent.TargetAcquired, Greydwarf, 0f));

            Assert.True(budget.TryClaim(Bob, ChatterEvent.TargetAcquired, Seeker, 2f));
        }

        [Fact]
        public void CallingAnEnemyOutDoesNotSilenceKillingIt()
        {
            ChatterBudget budget = Budget();
            Assert.True(budget.TryClaim(Alice, ChatterEvent.TargetAcquired, Greydwarf, 0f));

            // Same subject, different event. "A greydwarf!" and "Got him!" are two
            // different remarks and both deserve to land.
            Assert.True(budget.TryClaim(Bob, ChatterEvent.Killed, Greydwarf, 2f));
        }

        [Fact]
        public void EventsWithNoSubjectSkipTheEchoCheckEntirely()
        {
            ChatterBudget budget = Budget();

            // Two skeletons taking a hit are two separate yelps. If the echo check
            // ran on subject 0 it would treat these as the same thing and swallow
            // the second one.
            Assert.True(budget.TryClaim(Alice, ChatterEvent.Hurt, NoSubject, 0f));
            Assert.True(budget.TryClaim(Bob, ChatterEvent.Hurt, NoSubject, 2f));
        }

        [Fact]
        public void AnEventThePlayerSwitchedOffNeverGetsThrough()
        {
            ChatterSettings settings = Settings();
            _ = settings.DisabledEvents.Add(ChatterEvent.TargetAcquired);
            ChatterBudget budget = new(settings);

            Assert.False(budget.TryClaim(Alice, ChatterEvent.TargetAcquired, Greydwarf, 0f));

            // And switching one event off leaves the rest alone.
            Assert.True(budget.TryClaim(Alice, ChatterEvent.Hurt, NoSubject, 0f));
        }

        [Fact]
        public void SomethingImportantInterruptsIdleChatter()
        {
            ChatterBudget budget = Budget();
            Assert.True(budget.TryClaim(Alice, ChatterEvent.Idle, NoSubject, 0f));

            // 1s is inside the 2s squad gap, so ordinary chatter would be refused -
            // but dying outranks muttering, and 1s clears the half-second floor.
            Assert.True(budget.TryClaim(Bob, ChatterEvent.Died, NoSubject, 1f));
        }

        [Fact]
        public void EvenSomethingImportantWaitsABeat()
        {
            ChatterBudget budget = Budget();
            Assert.True(budget.TryClaim(Alice, ChatterEvent.Idle, NoSubject, 0f));

            // Two lines appearing together are two lines nobody reads, so the
            // barge-in still respects PreemptGapSeconds.
            Assert.False(budget.TryClaim(Bob, ChatterEvent.Died, NoSubject, 0.2f));
        }

        [Fact]
        public void MutteringDoesNotInterruptSomethingImportant()
        {
            ChatterBudget budget = Budget();
            Assert.True(budget.TryClaim(Alice, ChatterEvent.Died, NoSubject, 0f));

            Assert.False(budget.TryClaim(Bob, ChatterEvent.Idle, NoSubject, 1f));
        }

        [Fact]
        public void TwoSkeletonsDyingTogetherGiveOneSetOfLastWords()
        {
            ChatterBudget budget = Budget();
            Assert.True(budget.TryClaim(Alice, ChatterEvent.Died, NoSubject, 0f));

            // Equal priority is not *higher* priority, so Bob does not get to barge
            // in. Overlapping death cries would be noise rather than drama.
            Assert.False(budget.TryClaim(Bob, ChatterEvent.Died, NoSubject, 1f));
        }

        [Fact]
        public void BeingRefusedDoesNotCountAsHavingSpoken()
        {
            ChatterBudget budget = Budget();
            Assert.True(budget.TryClaim(Alice, ChatterEvent.Idle, NoSubject, 0f));

            // Bob is refused here, and that refusal must not start Bob's own cooldown
            // or push the squad gap out. Otherwise a squad that keeps trying to talk
            // would keep talking itself out of ever being allowed to.
            Assert.False(budget.TryClaim(Bob, ChatterEvent.Idle, NoSubject, 1f));
            Assert.True(budget.TryClaim(Bob, ChatterEvent.Idle, NoSubject, 2f));
        }

        [Fact]
        public void ANegativeSubjectDoesNotBleedIntoAnotherEvent()
        {
            ChatterBudget budget = Budget();

            // Prefab hashes are happily negative, and the subject key packs the
            // event above the subject in a long. Without casting to uint first, the
            // sign bits of a negative hash flood the event half of the key and two
            // different events collide. Both of these should be allowed.
            const int negativeHash = -12345;

            Assert.True(budget.TryClaim(Alice, ChatterEvent.TargetAcquired, negativeHash, 0f));
            Assert.True(budget.TryClaim(Bob, ChatterEvent.Killed, negativeHash, 2f));
        }

        [Fact]
        public void ALongQuietSpellPutsEverythingBackToNormal()
        {
            ChatterBudget budget = Budget();
            Assert.True(budget.TryClaim(Alice, ChatterEvent.TargetAcquired, Greydwarf, 0f));

            // Well past every window. This also walks over the pruning, so it fails
            // if throwing old bookkeeping away ever changes an answer.
            Assert.True(budget.TryClaim(Alice, ChatterEvent.TargetAcquired, Greydwarf, 1000f));
        }

        [Fact]
        public void PruningSurvivesASteadyStreamOfDifferentEnemies()
        {
            ChatterBudget budget = Budget();

            // Fifty different creatures, comfortably spaced. Nothing here is really
            // asserting on the pruning itself - the point is that a long session
            // does not quietly change how the rules behave.
            for (int i = 0; i < 50; i++)
            {
                Assert.True(budget.TryClaim(Alice, ChatterEvent.TargetAcquired, 9000 + i, i * 20f));
            }
        }
    }
}
