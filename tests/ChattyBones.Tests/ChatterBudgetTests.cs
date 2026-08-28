using System.Collections.Generic;
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

        /// <summary>Round numbers rather than the real defaults.</summary>
        /// <remarks>
        /// If a test says three seconds have passed, you should not have to go and
        /// look up whether that is enough.
        /// </remarks>
        private static ChatterSettings Settings(
            float speakerCooldownSeconds = 10f,
            IEnumerable<ChatterEvent> disabledEvents = null)
        {
            return new ChatterSettings(
                minGapSeconds: 2f,
                preemptGapSeconds: 0.5f,
                speakerCooldownSeconds: speakerCooldownSeconds,
                squadEchoWindowSeconds: 6f,
                disabledEvents: disabledEvents);
        }

        private static ChatterBudget Budget()
        {
            return new(Settings());
        }

        /// <summary>Ask, and book it if the answer is yes.</summary>
        /// <remarks>
        /// Almost every test below wants "did this skeleton get to speak", which is
        /// both halves of the real caller's job. The tests that care about the two
        /// halves being separate call CanClaim and Commit directly.
        /// </remarks>
        private static bool Speak(ChatterBudget budget, long speaker, ChatterEvent kind, int subject, float now)
        {
            if (!budget.CanClaim(speaker, kind, subject, now))
            {
                return false;
            }

            budget.Commit(speaker, kind, subject, now);
            return true;
        }

        [Fact]
        public void TheFirstThingAnyoneSaysIsAlwaysAllowed()
        {
            ChatterBudget budget = Budget();

            Assert.True(Speak(budget, Alice, ChatterEvent.Idle, NoSubject, 0f));
        }

        [Fact]
        public void NobodyElseSpeaksInsideTheGlobalGap()
        {
            ChatterBudget budget = Budget();
            Assert.True(Speak(budget, Alice, ChatterEvent.Idle, NoSubject, 0f));

            // Bob has said nothing at all, so only the squad-wide gap is stopping him.
            Assert.False(Speak(budget, Bob, ChatterEvent.Idle, NoSubject, 1.9f));
        }

        [Fact]
        public void SomebodyElseSpeaksOnceTheGlobalGapHasPassed()
        {
            ChatterBudget budget = Budget();
            Assert.True(Speak(budget, Alice, ChatterEvent.Idle, NoSubject, 0f));

            Assert.True(Speak(budget, Bob, ChatterEvent.Idle, NoSubject, 2f));
        }

        [Fact]
        public void OneSkeletonStaysQuietForMuchLongerThanTheSquadDoes()
        {
            ChatterBudget budget = Budget();
            Assert.True(Speak(budget, Alice, ChatterEvent.Idle, NoSubject, 0f));

            // The squad gap (2s) is long gone, but Alice's own cooldown is 10s.
            // This is the effect we want: the group keeps chatting, any one member
            // of it does not.
            Assert.False(Speak(budget, Alice, ChatterEvent.Idle, NoSubject, 5f));
            Assert.True(Speak(budget, Alice, ChatterEvent.Idle, NoSubject, 10f));
        }

        [Fact]
        public void OnlyOneSkeletonCallsOutAGivenEnemy()
        {
            ChatterBudget budget = Budget();

            // All three charge the same greydwarf at once, which is exactly what a
            // squad does. Bob is refused for the echo even though 3s is past the
            // squad gap and he has never spoken.
            Assert.True(Speak(budget, Alice, ChatterEvent.TargetAcquired, Greydwarf, 0f));
            Assert.False(Speak(budget, Bob, ChatterEvent.TargetAcquired, Greydwarf, 3f));
            Assert.False(Speak(budget, Carol, ChatterEvent.TargetAcquired, Greydwarf, 5f));
        }

        [Fact]
        public void TheSameEnemyIsWorthMentioningAgainMuchLater()
        {
            ChatterBudget budget = Budget();
            Assert.True(Speak(budget, Alice, ChatterEvent.TargetAcquired, Greydwarf, 0f));

            Assert.True(Speak(budget, Bob, ChatterEvent.TargetAcquired, Greydwarf, 6f));
        }

        [Fact]
        public void ADifferentEnemyIsWorthItsOwnRemark()
        {
            ChatterBudget budget = Budget();
            Assert.True(Speak(budget, Alice, ChatterEvent.TargetAcquired, Greydwarf, 0f));

            Assert.True(Speak(budget, Bob, ChatterEvent.TargetAcquired, Seeker, 2f));
        }

        [Fact]
        public void CallingAnEnemyOutDoesNotSilenceKillingIt()
        {
            ChatterBudget budget = Budget();
            Assert.True(Speak(budget, Alice, ChatterEvent.TargetAcquired, Greydwarf, 0f));

            // Same subject, different event. "A greydwarf!" and "Got him!" are two
            // different remarks and both deserve to land.
            Assert.True(Speak(budget, Bob, ChatterEvent.Killed, Greydwarf, 2f));
        }

        [Fact]
        public void EventsWithNoSubjectSkipTheEchoCheckEntirely()
        {
            ChatterBudget budget = Budget();

            // Two skeletons taking a hit are two separate yelps. If the echo check
            // ran on subject 0 it would treat these as the same thing and swallow
            // the second one.
            Assert.True(Speak(budget, Alice, ChatterEvent.Hurt, NoSubject, 0f));
            Assert.True(Speak(budget, Bob, ChatterEvent.Hurt, NoSubject, 2f));
        }

        [Fact]
        public void AnEventThePlayerSwitchedOffNeverGetsThrough()
        {
            ChatterBudget budget = new(Settings(disabledEvents: [ChatterEvent.TargetAcquired]));

            Assert.False(Speak(budget, Alice, ChatterEvent.TargetAcquired, Greydwarf, 0f));

            // And switching one event off leaves the rest alone.
            Assert.True(Speak(budget, Alice, ChatterEvent.Hurt, NoSubject, 0f));
        }

        [Fact]
        public void SomethingImportantInterruptsIdleChatter()
        {
            ChatterBudget budget = Budget();
            Assert.True(Speak(budget, Alice, ChatterEvent.Idle, NoSubject, 0f));

            // 1s is inside the 2s squad gap, so ordinary chatter would be refused -
            // but dying outranks muttering, and 1s clears the half-second floor.
            Assert.True(Speak(budget, Bob, ChatterEvent.Died, NoSubject, 1f));
        }

        [Fact]
        public void EvenSomethingImportantWaitsABeat()
        {
            ChatterBudget budget = Budget();
            Assert.True(Speak(budget, Alice, ChatterEvent.Idle, NoSubject, 0f));

            // Two lines appearing together are two lines nobody reads, so the
            // barge-in still respects PreemptGapSeconds.
            Assert.False(Speak(budget, Bob, ChatterEvent.Died, NoSubject, 0.2f));
        }

        [Fact]
        public void MutteringDoesNotInterruptSomethingImportant()
        {
            ChatterBudget budget = Budget();
            Assert.True(Speak(budget, Alice, ChatterEvent.Died, NoSubject, 0f));

            Assert.False(Speak(budget, Bob, ChatterEvent.Idle, NoSubject, 1f));
        }

        [Fact]
        public void TwoSkeletonsDyingTogetherGiveOneSetOfLastWords()
        {
            ChatterBudget budget = Budget();
            Assert.True(Speak(budget, Alice, ChatterEvent.Died, NoSubject, 0f));

            // Equal priority is not *higher* priority, so Bob does not get to barge
            // in. Overlapping death cries would be noise rather than drama.
            Assert.False(Speak(budget, Bob, ChatterEvent.Died, NoSubject, 1f));
        }

        [Fact]
        public void YourInjuriesOutrankTheirs()
        {
            ChatterBudget budget = Budget();
            Assert.True(Speak(budget, Alice, ChatterEvent.Hurt, NoSubject, 0f));

            // A skeleton is being chewed on and so are you. Yours is the one worth
            // hearing about, so it gets to cut in.
            Assert.True(Speak(budget, Bob, ChatterEvent.PlayerHurt, Greydwarf, 1f));
        }

        [Fact]
        public void ACompanionsInjuriesDoNotOutrankYourOwn()
        {
            ChatterBudget budget = Budget();
            Assert.True(Speak(budget, Alice, ChatterEvent.PlayerHurt, Greydwarf, 0f));

            Assert.False(Speak(budget, Bob, ChatterEvent.CompanionHurt, NoSubject, 1f));
        }

        [Fact]
        public void OnlyOneSkeletonCommentsOnYouGettingHit()
        {
            ChatterBudget budget = Budget();

            // One hit on you, five skeletons that all noticed. The squad echo already
            // handles this: pass whatever hit you as the subject and it collapses to
            // a single remark, exactly as it does for a shared target.
            Assert.True(Speak(budget, Alice, ChatterEvent.PlayerHurt, Greydwarf, 0f));
            Assert.False(Speak(budget, Bob, ChatterEvent.PlayerHurt, Greydwarf, 3f));
            Assert.False(Speak(budget, Carol, ChatterEvent.PlayerHurt, Greydwarf, 5f));
        }

        /// <summary>Can <paramref name="barger"/> interrupt <paramref name="sitting"/>?</summary>
        /// <remarks>
        /// A fresh budget each time, two different speakers so no per-speaker cooldown
        /// is involved, and no subject so the squad echo stays out of it. The second
        /// claim lands inside the squad gap but past the barge-in floor, so the only
        /// thing that can decide it is which event outranks the other.
        /// </remarks>
        private static bool CanInterrupt(ChatterEvent barger, ChatterEvent sitting)
        {
            ChatterBudget budget = Budget();
            Assert.True(Speak(budget, Alice, sitting, NoSubject, 0f));

            return Speak(budget, Bob, barger, NoSubject, 1f);
        }

        [Fact]
        public void NoTwoEventsShareARank()
        {
            // Ties are invisible and awkward: barging in needs a *strictly* higher
            // rank, so two events on the same number can never interrupt each other
            // in either direction. You would only notice by wondering why a death cry
            // went missing, months later.
            //
            // Reached entirely through behaviour rather than by reflecting on the
            // private table, so it keeps working if the ranks are ever moved into
            // config.
            ChatterEvent[] all = (ChatterEvent[])System.Enum.GetValues(typeof(ChatterEvent));

            foreach (ChatterEvent a in all)
            {
                foreach (ChatterEvent b in all)
                {
                    if (a.Equals(b))
                    {
                        continue;
                    }

                    // Exactly one direction must work. Both would be nonsense, and
                    // neither means they are tied.
                    Assert.True(
                        CanInterrupt(a, b) != CanInterrupt(b, a),
                        a + " and " + b + " appear to share a rank");
                }
            }
        }

        [Fact]
        public void DyingOutranksAbsolutelyEverything()
        {
            ChatterEvent[] all = (ChatterEvent[])System.Enum.GetValues(typeof(ChatterEvent));

            foreach (ChatterEvent kind in all)
            {
                if (kind.Equals(ChatterEvent.Died))
                {
                    continue;
                }

                Assert.True(
                    CanInterrupt(ChatterEvent.Died, kind),
                    "a death cry should have cut in on " + kind);
            }
        }

        [Fact]
        public void AskingDoesNotBookTheSlot()
        {
            // The reason CanClaim and Commit are separate calls. The caller cannot
            // know there is anything to *say* until after it has asked - the pack may
            // have no lines for that personality and event, or every line may want a
            // {target} we have not got. If asking booked the slot, that silent event
            // would have burned the squad's gap, the skeleton's cooldown and an echo
            // lock, for a line nobody heard.
            ChatterBudget budget = Budget();

            Assert.True(budget.CanClaim(Alice, ChatterEvent.Idle, NoSubject, 0f));
            Assert.True(budget.CanClaim(Alice, ChatterEvent.Idle, NoSubject, 0f));
            Assert.True(budget.CanClaim(Bob, ChatterEvent.Idle, NoSubject, 0f));

            // Nothing was recorded, so a real claim a moment later is still allowed.
            Assert.True(Speak(budget, Bob, ChatterEvent.Idle, NoSubject, 0f));

            // And now it has been.
            Assert.False(budget.CanClaim(Alice, ChatterEvent.Idle, NoSubject, 0.5f));
        }

        [Fact]
        public void AskingForTwoSkeletonsBeforeCommittingEitherDefeatsTheEchoWindow()
        {
            // The flip side of AskingDoesNotBookTheSlot, and the trap the split
            // creates. Nothing here can stop a caller doing this, so the test exists
            // to make the hazard visible rather than to prevent it - the rule lives in
            // CanClaim's remarks: resolve one claim before asking about the next.
            //
            // It matters because the TargetAcquired poll runs over the whole squad
            // several times a second, so "collect everyone whose target changed, ask
            // for each, then say them all" is the obvious shape and is wrong.
            ChatterBudget budget = Budget();

            Assert.True(budget.CanClaim(Alice, ChatterEvent.TargetAcquired, Greydwarf, 0f));
            Assert.True(budget.CanClaim(Bob, ChatterEvent.TargetAcquired, Greydwarf, 0f));

            // Both were told yes about the same greydwarf. Commit both and the echo
            // window has been defeated - two skeletons announce the same enemy.
            budget.Commit(Alice, ChatterEvent.TargetAcquired, Greydwarf, 0f);
            budget.Commit(Bob, ChatterEvent.TargetAcquired, Greydwarf, 0f);

            // Done properly - ask, resolve, then ask again - the second one is refused.
            ChatterBudget careful = Budget();
            Assert.True(careful.CanClaim(Alice, ChatterEvent.TargetAcquired, Greydwarf, 0f));
            careful.Commit(Alice, ChatterEvent.TargetAcquired, Greydwarf, 0f);
            Assert.False(careful.CanClaim(Bob, ChatterEvent.TargetAcquired, Greydwarf, 0f));
        }

        [Fact]
        public void AnEventWithNothingToSayCostsTheSquadNothing()
        {
            // The scenario the split is for, spelled out: a half-written pack, which
            // the shared-personality fallback deliberately invites. Three events fire,
            // none of them produces a line, and the squad is no quieter for it.
            ChatterBudget budget = Budget();

            for (int i = 0; i < 3; i++)
            {
                Assert.True(budget.CanClaim(Alice, ChatterEvent.Buffed, NoSubject, i * 0.1f));
                // ...LineChooser returns false here, so no Commit.
            }

            Assert.True(Speak(budget, Alice, ChatterEvent.Idle, NoSubject, 0.4f));
        }

        [Fact]
        public void ChangingASettingTakesEffectOnTheVeryNextQuestion()
        {
            // Settings are swapped wholesale rather than edited in place, because
            // BepInEx raises SettingChanged off the main thread and a half-rebuilt set
            // would be read mid-frame. Swapping one reference is atomic.
            ChatterBudget budget = Budget();
            Assert.True(Speak(budget, Alice, ChatterEvent.Idle, NoSubject, 0f));

            // Alice's cooldown is 10s, so she is normally refused here.
            Assert.False(budget.CanClaim(Alice, ChatterEvent.Idle, NoSubject, 5f));

            budget.Settings = Settings(speakerCooldownSeconds: 1f);

            Assert.True(budget.CanClaim(Alice, ChatterEvent.Idle, NoSubject, 5f));
        }

        [Fact]
        public void RaisingACooldownMidSessionIsHonouredForSkeletonsThatAlreadySpoke()
        {
            // This is the test that a pruning pass would fail, which is one of two
            // reasons there no longer is one. Dropping entries against the window as
            // it stood at the time meant a setting the player had just raised was
            // quietly ignored for anyone who had already spoken.
            ChatterBudget budget = Budget();
            Assert.True(Speak(budget, Alice, ChatterEvent.Idle, NoSubject, 0f));

            budget.Settings = Settings(speakerCooldownSeconds: 60f);

            Assert.False(budget.CanClaim(Alice, ChatterEvent.Idle, NoSubject, 30f));
            Assert.True(budget.CanClaim(Alice, ChatterEvent.Idle, NoSubject, 61f));
        }

        [Fact]
        public void WeRememberEverythingForTheWholeSessionOnPurpose()
        {
            // The other reason there is no pruning. Both maps are tiny - one small
            // entry per skeleton ever summoned, and one per distinct (event, creature)
            // pair, which the game itself bounds. Forgetting cost correctness under
            // live settings and bought a few kilobytes.
            //
            // This is a regression guard for that decision rather than a test of
            // behaviour: if somebody adds pruning back, it should be a deliberate act
            // that fails here first.
            ChatterBudget budget = Budget();

            for (int i = 0; i < 50; i++)
            {
                Assert.True(Speak(budget, 5000 + i, ChatterEvent.TargetAcquired, 9000 + i, i * 100f));
            }

            Assert.Equal(50, budget.TrackedSpeakers);
            Assert.Equal(50, budget.TrackedSubjects);
        }

        [Fact]
        public void BeingRefusedDoesNotCountAsHavingSpoken()
        {
            ChatterBudget budget = Budget();
            Assert.True(Speak(budget, Alice, ChatterEvent.Idle, NoSubject, 0f));

            // Bob is refused here, and that refusal must not start Bob's own cooldown
            // or push the squad gap out. Otherwise a squad that keeps trying to talk
            // would keep talking itself out of ever being allowed to.
            Assert.False(Speak(budget, Bob, ChatterEvent.Idle, NoSubject, 1f));
            Assert.True(Speak(budget, Bob, ChatterEvent.Idle, NoSubject, 2f));
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

            Assert.True(Speak(budget, Alice, ChatterEvent.TargetAcquired, negativeHash, 0f));
            Assert.True(Speak(budget, Bob, ChatterEvent.Killed, negativeHash, 2f));
        }

        [Fact]
        public void ALongQuietSpellPutsEverythingBackToNormal()
        {
            ChatterBudget budget = Budget();
            Assert.True(Speak(budget, Alice, ChatterEvent.TargetAcquired, Greydwarf, 0f));

            // Well past every window, so every check should have forgotten about the
            // first remark by now.
            Assert.True(Speak(budget, Alice, ChatterEvent.TargetAcquired, Greydwarf, 1000f));
        }

        [Fact]
        public void ASteadyStreamOfDifferentEnemiesKeepsWorking()
        {
            ChatterBudget budget = Budget();

            // Fifty different creatures, comfortably spaced so nothing should refuse.
            // A weak test on its own - the point is that a long session does not
            // quietly change how the rules behave.
            for (int i = 0; i < 50; i++)
            {
                Assert.True(Speak(budget, Alice, ChatterEvent.TargetAcquired, 9000 + i, i * 20f));
            }
        }
    }
}
