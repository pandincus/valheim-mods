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
            if (!budget.CanClaim(speaker, kind, subject, now, out _))
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
        public void ADisabledEventNeverGetsThrough()
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

            // Subject 0, because that is what the hook actually passes - being hit is
            // not "about" a kind of creature the way a target is. So the squad echo is
            // not what holds this down; the squad gap is, and this is the test that it
            // does. An earlier version passed a creature here and proved the echo
            // window instead, which no caller ever reaches.
            Assert.True(Speak(budget, Alice, ChatterEvent.PlayerHurt, NoSubject, 0f));

            Assert.False(Speak(budget, Bob, ChatterEvent.PlayerHurt, NoSubject, 0.5f));
        }

        /// <summary>Can <paramref name="barger"/> interrupt <paramref name="sitting"/>, on rank alone?</summary>
        /// <returns>True if the barger got through.</returns>
        /// <param name="barger">The event trying to cut in.</param>
        /// <param name="sitting">The event already said.</param>
        /// <remarks>
        /// A fresh budget each time, two different speakers so no per-speaker cooldown
        /// is involved, and no subject so the squad echo stays out of it.
        ///
        /// 1.5 seconds sits in exactly one window: inside MinGapSeconds, so rank is
        /// what decides, and past the answer window, so an event that *answers* the one
        /// already said does not come back true for that reason instead. At 1.0 it
        /// does, and NoTwoEventsShareARank reports a tie between Died and CompanionDied
        /// that is not a tie.
        /// </remarks>
        private static bool CanInterrupt(ChatterEvent barger, ChatterEvent sitting)
        {
            ChatterBudget budget = Budget();
            Assert.True(Speak(budget, Alice, sitting, NoSubject, 0f));

            return Speak(budget, Bob, barger, NoSubject, 1.5f);
        }

        [Fact]
        public void RemarkingOnTheWeatherNeverTalksOverAnything()
        {
            // Weather may interrupt an idle mutter and nothing else. That is the point.
            Assert.True(CanInterrupt(ChatterEvent.Weather, ChatterEvent.Idle));
            Assert.False(CanInterrupt(ChatterEvent.Weather, ChatterEvent.Killed));
            Assert.False(CanInterrupt(ChatterEvent.Weather, ChatterEvent.TargetAcquired));
            Assert.False(CanInterrupt(ChatterEvent.Weather, ChatterEvent.CompanionSummoned));
        }

        [Fact]
        public void TheTextureOfAFightNeverTalksOverTheFightItself()
        {
            // These three fire several times an encounter, which is what makes them
            // worth having and also what makes them dangerous. Ranked any higher and
            // a squad would spend a troll fight admiring your footwork instead of
            // calling out what is happening.
            ChatterEvent[] texture =
            [
                ChatterEvent.PlayerDodged,
                ChatterEvent.PlayerParried,
                ChatterEvent.StaggeredIt,
            ];

            foreach (ChatterEvent kind in texture)
            {
                Assert.False(
                    CanInterrupt(kind, ChatterEvent.TargetAcquired),
                    kind + " should not have cut in on TargetAcquired");

                Assert.False(
                    CanInterrupt(kind, ChatterEvent.Killed),
                    kind + " should not have cut in on a kill");

                // But they are still worth more than an idle mutter, or nobody would
                // ever hear one.
                Assert.True(
                    CanInterrupt(kind, ChatterEvent.Idle),
                    kind + " should have been able to cut in on Idle");
            }
        }

        [Fact]
        public void SkillBeatsBruteForceAmongTheCombatRemarks()
        {
            // The ordering inside the cluster, and it is a judgement rather than a
            // fact: turning a blow is harder than landing one, and rolling clear of it
            // is harder still. Pinned so that adding a fifth combat event has to think
            // about where it goes rather than landing on a free number.
            Assert.True(CanInterrupt(ChatterEvent.PlayerDodged, ChatterEvent.PlayerParried));
            Assert.True(CanInterrupt(ChatterEvent.PlayerParried, ChatterEvent.StaggeredIt));
            Assert.True(CanInterrupt(ChatterEvent.StaggeredIt, ChatterEvent.PlayerLandedABigHit));

            Assert.False(CanInterrupt(ChatterEvent.PlayerLandedABigHit, ChatterEvent.StaggeredIt));
        }

        [Fact]
        public void BeingKnockedAboutIsAboutYouRatherThanAboutTheFight()
        {
            // PlayerStaggered sits with the events about you, not with the combat
            // texture, because it means you are losing. It gives way to you actually
            // being hurt - the same moment, told better - and beats a skeleton being
            // scratched.
            Assert.False(CanInterrupt(ChatterEvent.PlayerStaggered, ChatterEvent.PlayerHurt));
            Assert.True(CanInterrupt(ChatterEvent.PlayerStaggered, ChatterEvent.CompanionHurt));

            // And it is emphatically not one of the three above, which are good news.
            Assert.True(CanInterrupt(ChatterEvent.PlayerStaggered, ChatterEvent.PlayerParried));
        }

        [Fact]
        public void PickingThingsUpSitsRightAtTheBottom()
        {
            // Looted sits one rank above Idle, which is what lets it fire on every
            // pickup with no filter at all: it can interrupt an idle mutter and
            // nothing else, so a stick getting through costs one mutter.
            Assert.True(CanInterrupt(ChatterEvent.Looted, ChatterEvent.Idle));
            Assert.False(CanInterrupt(ChatterEvent.Looted, ChatterEvent.Weather));
            Assert.False(CanInterrupt(ChatterEvent.Looted, ChatterEvent.TargetAcquired));
        }

        [Fact]
        public void TheSmallTalkGoesInThreeTiers()
        {
            // The bottom of the table is a dozen events with nothing urgent in it, so
            // the ordering inside it is a judgement rather than a fact - which is
            // exactly why it needs pinning. A review found four of these orderings
            // could be swapped with every test still passing.
            //
            // Coarsest first: what the squad is, then what you are doing, then what
            // the world is doing around you.
            ChatterEvent[] descending =
            [
                ChatterEvent.Buffed,
                ChatterEvent.Summoned,
                ChatterEvent.CompanionSummoned,
                ChatterEvent.PlayerSkilledUp,
                ChatterEvent.PlayerAte,
                ChatterEvent.AtHome,
                ChatterEvent.BiomeChanged,
                ChatterEvent.Dawn,
                ChatterEvent.Nightfall,
                ChatterEvent.Weather,
                ChatterEvent.Looted,
                ChatterEvent.Idle,
            ];

            for (int i = 0; i + 1 < descending.Length; i++)
            {
                Assert.True(
                    CanInterrupt(descending[i], descending[i + 1]),
                    descending[i] + " should outrank " + descending[i + 1]);

                Assert.False(
                    CanInterrupt(descending[i + 1], descending[i]),
                    descending[i + 1] + " should not outrank " + descending[i]);
            }
        }

        [Fact]
        public void ASquadmateArrivingBeatsYourLunch()
        {
            // Called out on its own because it was wrong. CompanionSummoned sat below
            // PlayerAte, AtHome and BiomeChanged while the comment above it said
            // arriving somewhere ranks below anything that happens to a person - so a
            // squad welcoming a newcomer lost to you eating a carrot.
            Assert.True(CanInterrupt(ChatterEvent.CompanionSummoned, ChatterEvent.PlayerAte));
            Assert.True(CanInterrupt(ChatterEvent.CompanionSummoned, ChatterEvent.AtHome));
            Assert.True(CanInterrupt(ChatterEvent.CompanionSummoned, ChatterEvent.BiomeChanged));
        }

        [Fact]
        public void TheSmallTalkBandStillHasRoomToGrow()
        {
            // The table's own convention is that the gaps are wide so there is room to
            // slot something in later. Adding nine events in one branch consumed every
            // free integer between Idle and Summoned, and the next addition would have
            // had to renumber a neighbour - which is how two events come to share a
            // rank and quietly stop being able to interrupt each other.
            //
            // Asserting the gap rather than the numbers, so respacing again is free.
            ChatterEvent[] band =
            [
                ChatterEvent.Idle, ChatterEvent.Looted, ChatterEvent.Weather,
                ChatterEvent.Nightfall, ChatterEvent.Dawn, ChatterEvent.BiomeChanged,
                ChatterEvent.AtHome, ChatterEvent.PlayerAte, ChatterEvent.PlayerSkilledUp,
                ChatterEvent.CompanionSummoned, ChatterEvent.Summoned, ChatterEvent.Buffed,
            ];

            foreach (ChatterEvent kind in band)
            {
                foreach (ChatterEvent other in band)
                {
                    if (kind != other)
                    {
                        Assert.True(
                            ChatterBudget.PriorityOf(kind) != ChatterBudget.PriorityOf(other) + 1,
                            kind + " sits immediately above " + other + " with no room between them.");
                    }
                }
            }
        }

        [Fact]
        public void NoneOfTheSmallTalkTalksOverAFight()
        {
            Assert.False(CanInterrupt(ChatterEvent.PlayerSkilledUp, ChatterEvent.TargetAcquired));
            Assert.False(CanInterrupt(ChatterEvent.PlayerAte, ChatterEvent.Hurt));
            Assert.False(CanInterrupt(ChatterEvent.AtHome, ChatterEvent.Killed));
        }

        [Fact]
        public void ARaidAnnouncingItselfBeatsTheFightItBrings()
        {
            // The reason Raid is ranked oddly high for something this rare. It arrives
            // and immediately supplies things to fight, so at any lower rank the squad
            // would announce the first greydwarf and never the raid - and "something is
            // coming" is the better line and the one that only gets said once.
            Assert.True(CanInterrupt(ChatterEvent.Raid, ChatterEvent.TargetAcquired));
            Assert.True(CanInterrupt(ChatterEvent.Raid, ChatterEvent.PlayerGotAKill));

            // But not over somebody actually being hurt. A warning about what is
            // coming loses to what has already arrived.
            Assert.False(CanInterrupt(ChatterEvent.Raid, ChatterEvent.PlayerHurt));
        }

        [Fact]
        public void SurvivingARaidIsSaidIntoAQuietField()
        {
            // RaidEnded deliberately does not inherit Raid's standing. It fires when
            // the fighting has stopped, so it has nothing to compete with and does not
            // need to win anything.
            Assert.False(CanInterrupt(ChatterEvent.RaidEnded, ChatterEvent.TargetAcquired));
            Assert.True(CanInterrupt(ChatterEvent.RaidEnded, ChatterEvent.Idle));
        }

        [Fact]
        public void TravellingRemarksNeverTalkOverAFight()
        {
            // All four fire while you are going somewhere, which is when the squad has
            // least to say - and they must stay out of the way when that changes.
            ChatterEvent[] travelling =
            [
                ChatterEvent.BiomeChanged,
                ChatterEvent.AtHome,
                ChatterEvent.Dawn,
                ChatterEvent.Nightfall,
            ];

            foreach (ChatterEvent kind in travelling)
            {
                Assert.False(
                    CanInterrupt(kind, ChatterEvent.TargetAcquired),
                    kind + " should not have cut in on TargetAcquired");

                Assert.False(
                    CanInterrupt(kind, ChatterEvent.Hurt),
                    kind + " should not have cut in on an injury");

                Assert.True(
                    CanInterrupt(kind, ChatterEvent.Idle),
                    kind + " should have been able to cut in on Idle");
            }
        }

        [Fact]
        public void ArrivingSomewhereOutranksTheSkyDoingItsUsualThing()
        {
            // A twenty-minute day cycle means Dawn and Nightfall come round on their
            // own; crossing into the Plains does not. So the sky sits below the ground
            // underfoot, and both sit above the weather.
            Assert.True(CanInterrupt(ChatterEvent.BiomeChanged, ChatterEvent.Dawn));
            Assert.True(CanInterrupt(ChatterEvent.AtHome, ChatterEvent.Nightfall));
            Assert.True(CanInterrupt(ChatterEvent.Dawn, ChatterEvent.Weather));
        }

        [Fact]
        public void SayingHelloOutranksSmallTalkAndNothingElse()
        {
            // A greeting goes off if it is not said now, so it beats the housekeeping
            // events it would otherwise queue behind. It stays under everything in a
            // fight because a skeleton breaking off from a greydwarf to say hail reads
            // as a bug rather than as manners.
            Assert.True(CanInterrupt(ChatterEvent.Visitor, ChatterEvent.Buffed));
            Assert.True(CanInterrupt(ChatterEvent.Visitor, ChatterEvent.Summoned));
            Assert.True(CanInterrupt(ChatterEvent.RaidEnded, ChatterEvent.Visitor));
            Assert.True(CanInterrupt(ChatterEvent.Hurt, ChatterEvent.Visitor));
        }

        [Fact]
        public void CatchingFireOutranksTheBlowThatCausedIt()
        {
            // Uniqueness alone left this rank free to move either way. Why it is above
            // Hurt is in ChatterBudget.
            Assert.True(CanInterrupt(ChatterEvent.Afflicted, ChatterEvent.Hurt));
        }

        [Fact]
        public void CatchingFireDoesNotTalkOverSomethingWorse()
        {
            // The other side of it. A skeleton on fire must not drown out you being
            // mauled or a companion going down.
            Assert.False(CanInterrupt(ChatterEvent.Afflicted, ChatterEvent.PlayerHurt));
            Assert.False(CanInterrupt(ChatterEvent.Afflicted, ChatterEvent.CompanionDied));
            Assert.False(CanInterrupt(ChatterEvent.Afflicted, ChatterEvent.Died));
        }

        [Fact]
        public void HowAFightEndedOutranksNoticingItStarted()
        {
            // One Fact rather than a Theory with InlineData, because ChatterEvent is
            // internal and xUnit needs the test method public - a public method cannot
            // take an internal parameter.
            ChatterEvent[] outcomes =
            [
                ChatterEvent.PlayerGotAKill,
                ChatterEvent.Killed,
                ChatterEvent.CompanionKilled,
            ];

            foreach (ChatterEvent outcome in outcomes)
            {
                AnOutcomeBeatsAnAnnouncement(outcome);
            }
        }

        /// <summary>Both directions of one outcome against TargetAcquired.</summary>
        /// <param name="outcome">The result event that should win.</param>
        private static void AnOutcomeBeatsAnAnnouncement(ChatterEvent outcome)
        {
            // This is a fix pinned in place, not a preference. TargetAcquired used to
            // outrank all three of these, and because a fight is usually over inside
            // MinGapSeconds, the kill could not preempt the announcement that preceded
            // it - so it was dropped outright while the next target announcement went
            // through at the higher rank. Watching a squad in the Black Forest gave
            // three "there's a greydwarf" and never once a result.
            Assert.True(
                CanInterrupt(outcome, ChatterEvent.TargetAcquired),
                outcome + " should have been able to cut in on TargetAcquired");

            // And not the other way round, or we have simply moved the problem.
            Assert.False(
                CanInterrupt(ChatterEvent.TargetAcquired, outcome),
                "TargetAcquired should not have cut in on " + outcome);
        }

        [Fact]
        public void LastWordsAreNotHeldBackByTheSpeakersOwnCooldown()
        {
            // A skeleton that called out a target and then died four seconds later
            // used to go quietly. The cooldown is checked before priority is even
            // looked at, so Died outranking everything did not help it at all - and
            // in a fight, a skeleton that has been silent for a full eight seconds
            // beforehand is nearly none of them.
            ChatterBudget budget = Budget();
            Assert.True(Speak(budget, Alice, ChatterEvent.TargetAcquired, Greydwarf, 0f));

            Assert.True(Speak(budget, Alice, ChatterEvent.Died, NoSubject, 4f));
        }

        [Fact]
        public void BeingUnsummonedIsAlsoWorthTheLastWord()
        {
            ChatterBudget budget = Budget();
            Assert.True(Speak(budget, Alice, ChatterEvent.TargetAcquired, Greydwarf, 0f));

            Assert.True(Speak(budget, Alice, ChatterEvent.Unsummoned, NoSubject, 4f));
        }

        [Fact]
        public void OnlyTheTerminalEventsSkipTheCooldown()
        {
            // The control for the two above. Being badly hurt is urgent and outranks
            // most things, and it still waits its turn - otherwise the exemption has
            // quietly grown into "important events ignore the cooldown", which is the
            // whole rule gone.
            ChatterBudget budget = Budget();
            Assert.True(Speak(budget, Alice, ChatterEvent.TargetAcquired, Greydwarf, 0f));

            Assert.False(Speak(budget, Alice, ChatterEvent.Hurt, NoSubject, 4f));
        }

        [Fact]
        public void ADeathCryStillLeavesABeatOfQuiet()
        {
            // The exemption is only about the speaker's own cooldown. Committing a
            // death still spends the squad gap, so the survivors do not talk over it.
            ChatterBudget budget = Budget();
            Assert.True(Speak(budget, Alice, ChatterEvent.Died, NoSubject, 0f));

            Assert.False(Speak(budget, Bob, ChatterEvent.Idle, NoSubject, 1f));
        }

        [Fact]
        public void AnAnswerLandsInTheSameBreathAsWhatItAnswers()
        {
            // The gaps space out subjects of conversation, not utterances. A death cry
            // and somebody reacting to it are one moment between two skeletons, so the
            // second does not wait - you do not pause before saying "oh no" when
            // somebody drops something.
            ChatterBudget budget = Budget();
            Assert.True(Speak(budget, Alice, ChatterEvent.Died, NoSubject, 0f));

            Assert.True(Speak(budget, Bob, ChatterEvent.CompanionDied, NoSubject, 0f));
        }

        [Fact]
        public void AMomentGetsOneAnswerAndNoMore()
        {
            // Otherwise a squad wipe is four skeletons saying "oh no" over each other,
            // which is the wall of text this whole class exists to prevent.
            ChatterBudget budget = Budget();
            Assert.True(Speak(budget, Alice, ChatterEvent.Died, NoSubject, 0f));
            Assert.True(Speak(budget, Bob, ChatterEvent.CompanionDied, NoSubject, 0f));

            Assert.False(Speak(budget, Carol, ChatterEvent.CompanionDied, NoSubject, 0f));
        }

        [Fact]
        public void AnAnswerOnlySkipsTheGapForTheMomentItActuallyAnswers()
        {
            // The exemption is tied to what is being talked about, not to the event
            // being a companion one. Nobody has died here, so there is nothing to
            // answer and it queues like anything else.
            ChatterBudget budget = Budget();
            Assert.True(Speak(budget, Alice, ChatterEvent.TargetAcquired, Greydwarf, 0f));

            Assert.False(Speak(budget, Bob, ChatterEvent.CompanionDied, NoSubject, 0.1f));
        }

        [Fact]
        public void AnAnswerGoesStaleRatherThanWaitingForAGap()
        {
            // A reply arriving well after the thing it replies to is worse than no
            // reply, so the exemption expires instead of being banked.
            ChatterBudget budget = Budget();
            Assert.True(Speak(budget, Alice, ChatterEvent.Died, NoSubject, 0f));

            Assert.False(Speak(budget, Bob, ChatterEvent.CompanionDied, NoSubject, 1.5f));
        }

        [Fact]
        public void AnsweringDoesNotOpenAMomentOfItsOwn()
        {
            // If a reply started a fresh moment, a second reply could answer the first
            // and the squad would talk itself in a circle.
            ChatterBudget budget = Budget();
            Assert.True(Speak(budget, Alice, ChatterEvent.Died, NoSubject, 0f));
            Assert.True(Speak(budget, Bob, ChatterEvent.CompanionDied, NoSubject, 0f));

            // PlayerHurt rather than Idle, and the choice is the test. Idle is ranked
            // 10 and is refused whatever the standing priority is, so it passed while
            // the bar was being wrongly lowered to the answer's own rank. PlayerHurt
            // is 110 - above CompanionDied's 105 and below Died's 130 - so it lands
            // exactly in the gap the fault opened up and fails if it comes back.
            Assert.False(Speak(budget, Carol, ChatterEvent.PlayerHurt, NoSubject, 1f));
        }

        [Fact]
        public void AnsweringADeathDoesNotLetTheNextDeathCascade()
        {
            // The measured version of the fault above. Four skeletons dying half a
            // second apart, each death answered, produced seven lines in a second and
            // a half: the answer lowered the bar to 105, the next Died beat that, and
            // round it went. Two deaths is enough to catch it.
            ChatterBudget budget = Budget();
            Assert.True(Speak(budget, Alice, ChatterEvent.Died, NoSubject, 0f));
            Assert.True(Speak(budget, Bob, ChatterEvent.CompanionDied, NoSubject, 0f));

            Assert.False(Speak(budget, Carol, ChatterEvent.Died, NoSubject, 0.5f));
        }

        [Fact]
        public void ASquadRaisedTogetherGivesOneWelcomeBetweenThem()
        {
            // The newcomer introduces itself and an existing skeleton welcomes it -
            // that pair is one moment, so the welcome does not wait. A *second*
            // newcomer arriving in the same breath is refused, because a greeting
            // cannot barge in on a greeting: same event, same rank, and barging in
            // wants strictly higher. The tie is what holds a batch down, not the
            // answer machinery.
            ChatterBudget budget = Budget();
            Assert.True(Speak(budget, Alice, ChatterEvent.Summoned, NoSubject, 0f));
            Assert.True(Speak(budget, Bob, ChatterEvent.CompanionSummoned, NoSubject, 0f));

            Assert.False(Speak(budget, Carol, ChatterEvent.Summoned, NoSubject, 0.1f));
        }

        [Fact]
        public void NoTwoEventsShareARank()
        {
            // Ties are invisible and awkward: barging in needs a *strictly* higher
            // rank, so two events on the same number can never interrupt each other
            // in either direction. You would only notice by wondering why a death cry
            // went missing, months later.
            //
            // Reached entirely through behavior rather than by reflecting on the
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

            Assert.True(budget.CanClaim(Alice, ChatterEvent.Idle, NoSubject, 0f, out _));
            Assert.True(budget.CanClaim(Alice, ChatterEvent.Idle, NoSubject, 0f, out _));
            Assert.True(budget.CanClaim(Bob, ChatterEvent.Idle, NoSubject, 0f, out _));

            // Nothing was recorded, so a real claim a moment later is still allowed.
            Assert.True(Speak(budget, Bob, ChatterEvent.Idle, NoSubject, 0f));

            // And now it has been.
            Assert.False(budget.CanClaim(Alice, ChatterEvent.Idle, NoSubject, 0.5f, out _));
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

            Assert.True(budget.CanClaim(Alice, ChatterEvent.TargetAcquired, Greydwarf, 0f, out _));
            Assert.True(budget.CanClaim(Bob, ChatterEvent.TargetAcquired, Greydwarf, 0f, out _));

            // Both were told yes about the same greydwarf. Commit both and the echo
            // window has been defeated - two skeletons announce the same enemy.
            budget.Commit(Alice, ChatterEvent.TargetAcquired, Greydwarf, 0f);
            budget.Commit(Bob, ChatterEvent.TargetAcquired, Greydwarf, 0f);

            // Done properly - ask, resolve, then ask again - the second one is refused.
            ChatterBudget careful = Budget();
            Assert.True(careful.CanClaim(Alice, ChatterEvent.TargetAcquired, Greydwarf, 0f, out _));
            careful.Commit(Alice, ChatterEvent.TargetAcquired, Greydwarf, 0f);
            Assert.False(careful.CanClaim(Bob, ChatterEvent.TargetAcquired, Greydwarf, 0f, out _));
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
                Assert.True(budget.CanClaim(Alice, ChatterEvent.Buffed, NoSubject, i * 0.1f, out _));
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
            Assert.False(budget.CanClaim(Alice, ChatterEvent.Idle, NoSubject, 5f, out _));

            budget.Settings = Settings(speakerCooldownSeconds: 1f);

            Assert.True(budget.CanClaim(Alice, ChatterEvent.Idle, NoSubject, 5f, out _));
        }

        [Fact]
        public void RaisingACooldownMidSessionIsHonoredForSkeletonsThatAlreadySpoke()
        {
            // This is the test that a pruning pass would fail, which is one of two
            // reasons there no longer is one. Dropping entries against the window as
            // it stood at the time meant a setting the player had just raised was
            // quietly ignored for anyone who had already spoken.
            ChatterBudget budget = Budget();
            Assert.True(Speak(budget, Alice, ChatterEvent.Idle, NoSubject, 0f));

            budget.Settings = Settings(speakerCooldownSeconds: 60f);

            Assert.False(budget.CanClaim(Alice, ChatterEvent.Idle, NoSubject, 30f, out _));
            Assert.True(budget.CanClaim(Alice, ChatterEvent.Idle, NoSubject, 61f, out _));
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
            // behavior: if somebody adds pruning back, it should be a deliberate act
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

    }
}
