using System;
using System.Collections.Generic;
using ChattyBones.Logic;

namespace ChattyBones.Tests
{
    /// <summary>
    /// Covers the table of which events promise which tokens, and the session report.
    /// </summary>
    /// <remarks>
    /// The table is pinned from the other side by
    /// <c>ThePackHeadersTokenGridMatchesWhatTheEventsActuallySupply</c>, which reads
    /// the shipped pack's header. What is here is the table itself, and the report
    /// that <c>cb_tokens</c> prints - which exists because no test can see whether a
    /// call site still passes what it used to.
    ///
    /// The seen-tokens memory is static and lives for the session by design, so it is
    /// cleared in the constructor rather than in each test. xUnit builds the class
    /// once per test, so the constructor is the one place that cannot be forgotten.
    /// </remarks>
    public class EventTokensTests
    {
        /// <summary>Start every test from a blank memory.</summary>
        public EventTokensTests()
        {
            EventTokens.Forget();
        }

        /// <summary>A hit with every detail on it, promised or not.</summary>
        /// <remarks>
        /// More than a live blow promises, deliberately. Since the weapon tokens became
        /// the player's alone an incoming hit promises only {damage}, so this now also
        /// exercises the supplies-what-it-did-not-promise half of the report.
        /// </remarks>
        private static LineDetails FullHit()
        {
            return new LineDetails(weapon: "Mistwalker", weaponSkill: "sword", damage: "slash");
        }

        /// <summary>The report line for one event.</summary>
        /// <returns>The line, or null when the event promises nothing and is omitted.</returns>
        /// <param name="kind">The event to look for.</param>
        private static string LineFor(ChatterEvent kind)
        {
            foreach (string line in EventTokens.Report())
            {
                if (line.StartsWith(kind + ":", StringComparison.Ordinal))
                {
                    return line;
                }
            }

            return null;
        }

        [Fact]
        public void AnEventNobodyHasFiredShowsEverythingUnseen()
        {
            // PlayerLandedABigHit rather than Hurt, which is what this used to use.
            // Hurt promises only {damage} now that the weapon tokens are the player's
            // alone, and one token cannot show that the two lists agree.
            string big = LineFor(ChatterEvent.PlayerLandedABigHit);

            Assert.NotNull(big);

            int split = big.IndexOf("never seen", StringComparison.Ordinal);
            Assert.True(split > 0, "the report should name what it has never seen");

            // Each token looked for on its own rather than as one run of text, so the
            // report stays free to order them however it likes.
            string promises = big[..split];
            string unseen = big[split..];

            foreach (string token in new[] { "{target}", "{weapon}", "{weaponskill}", "{damage}" })
            {
                Assert.Contains(token, promises, StringComparison.Ordinal);
                Assert.Contains(token, unseen, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void SupplyingATokenOnceIsRememberedForTheRestOfTheSession()
        {
            EventTokens.Note(ChatterEvent.Hurt, target: null, companion: null, ally: null, FullHit());

            string hurt = LineFor(ChatterEvent.Hurt);

            Assert.NotNull(hurt);
            Assert.DoesNotContain("never seen", hurt, StringComparison.Ordinal);
        }

        [Fact]
        public void APartialSupplyNamesOnlyWhatIsStillUnseen()
        {
            // The ordinary case the old warning could not handle: a hit with no
            // dominant damage type. The report says {damage} has not been seen and
            // leaves the judgement to whoever is reading, because nothing in the data
            // separates "the hook stopped passing it" from "no hit has had one yet".
            // PlayerLandedABigHit, because it is now the only kind of event that
            // promises a weapon at all. Asked of Hurt this would pass without
            // proving anything - {weapon} is not on its list, so of course the
            // report does not mention it.
            EventTokens.Note(
                ChatterEvent.PlayerLandedABigHit, "Greydwarf", null, null,
                new LineDetails(weapon: "Mistwalker", weaponSkill: "sword"));

            string big = LineFor(ChatterEvent.PlayerLandedABigHit);

            Assert.Contains("never seen {damage}", big, StringComparison.Ordinal);
            Assert.DoesNotContain("{weapon}", big[
                big.IndexOf("never seen", StringComparison.Ordinal)..], StringComparison.Ordinal);
        }

        [Fact]
        public void ATokenSuppliedButNotPromisedIsCalledOut()
        {
            // The direction a single sample settles: if a call site supplies it, it
            // supplies it every time, so one sighting is conclusive where an absence
            // never is. It means the grid under-claims and authors are being told not
            // to write a line that would work.
            //
            // Looted rather than Idle, which is what this used to use. Idle no longer
            // promises anything at all, so it has no row in the report to say this on.
            EventTokens.Note(
                ChatterEvent.Looted, target: null, companion: null, ally: null,
                new LineDetails(item: "$item_stone", status: "Burning"));

            string looted = LineFor(ChatterEvent.Looted);

            Assert.Contains("supplies unpromised {status}", looted, StringComparison.Ordinal);
            Assert.DoesNotContain("never seen", looted, StringComparison.Ordinal);
        }

        [Fact]
        public void EventsThatPromiseNothingAreLeftOutOfTheReport()
        {
            // Summoned is about nobody but the speaker, so it has no row to get wrong
            // and printing one would be noise in a report meant to be scanned.
            Assert.Equal(TokenSet.None, EventTokens.PromisedFor(ChatterEvent.Summoned));
            Assert.Null(LineFor(ChatterEvent.Summoned));
            Assert.Null(LineFor(ChatterEvent.PlayerDodged));
        }

        [Fact]
        public void TheReportSaysHowToReadAGap()
        {
            // Without this the report is a list of accusations. The whole reason it is
            // a command rather than a warning is that a gap is a question.
            IReadOnlyList<string> report = EventTokens.Report();

            Assert.Contains(
                report,
                line => line.Contains("question", StringComparison.Ordinal));
        }

        [Fact]
        public void EveryEventThatPromisesSomethingCanBeReportedOn()
        {
            // Teeth for the array sizing. ChatterEvent is contiguous today, so sizing
            // by member count and by highest value agree - but its own documentation
            // argues for pinning the numbers explicitly, and the moment one carries a
            // value above the count, a count-sized array would silently drop the
            // newest events. This walks every declared event and insists each one both
            // records and reports.
            foreach (ChatterEvent kind in Enum.GetValues(typeof(ChatterEvent)))
            {
                if (EventTokens.PromisedFor(kind) == TokenSet.None)
                {
                    continue;
                }

                EventTokens.Note(kind, "Greydwarf", "Gunnar", "Sigrid", FullHit());

                string line = LineFor(kind);
                Assert.True(line != null, kind + " promises tokens but has no row in the report.");
                Assert.DoesNotContain("never seen {target}", line, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void AnEventOutsideTheEnumIsIgnoredRatherThanThrowing()
        {
            // Unreachable from the mod, and the cost of being wrong is an
            // IndexOutOfRangeException thrown from inside a Harmony postfix.
            EventTokens.Note((ChatterEvent)999, "Greydwarf", "Gunnar", "Sigrid", FullHit());
            EventTokens.Note((ChatterEvent)(-1), null, null, null, details: default);
        }

        [Fact]
        public void TheTableStillAgreesWithTheGroupsItIsBuiltFrom()
        {
            Assert.Equal(TokenSet.Damage, EventTokens.PromisedFor(ChatterEvent.Hurt));

            Assert.Equal(
                TokenSet.Target | TokenSet.Companion,
                EventTokens.PromisedFor(ChatterEvent.CompanionKilled));

            Assert.Equal(
                TokenSet.Target | TokenSet.Weapon | TokenSet.WeaponSkill,
                EventTokens.PromisedFor(ChatterEvent.PlayerGotAKill));

            Assert.Equal(TokenSet.Ally, EventTokens.PromisedFor(ChatterEvent.AllyArrived));
            Assert.Equal(TokenSet.Status, EventTokens.PromisedFor(ChatterEvent.Afflicted));
            Assert.Equal(TokenSet.Target, EventTokens.PromisedFor(ChatterEvent.PlayerParried));
            Assert.Equal(TokenSet.None, EventTokens.PromisedFor(ChatterEvent.PlayerDodged));
        }

        [Fact]
        public void EveryCompanionEventPromisesTheCompanionToken()
        {
            // A new Companion* event dropped from the second group in PromisedFor is
            // the likeliest way this table goes wrong, and the naming convention is
            // strong enough to check against.
            foreach (ChatterEvent kind in Enum.GetValues(typeof(ChatterEvent)))
            {
                if (!kind.ToString().StartsWith("Companion", StringComparison.Ordinal))
                {
                    continue;
                }

                Assert.True(
                    (EventTokens.PromisedFor(kind) & TokenSet.Companion) != 0,
                    kind + " is a companion event but does not promise {companion}.");
            }
        }

        [Fact]
        public void OnlyTheArrivalEventNamesAParticularPlayer()
        {
            // {ally} can be written into any line at all - Chatter fills in whoever is
            // standing nearby - so the flag here means something narrower: this event
            // knows *which* player it is about, and the fallback must keep its hands
            // off. Exactly one event is in that position, and a second one appearing
            // without the fallback being taught about it would have a skeleton greeting
            // a bystander.
            foreach (ChatterEvent kind in Enum.GetValues(typeof(ChatterEvent)))
            {
                bool named = (EventTokens.PromisedFor(kind) & TokenSet.Ally) != 0;

                Assert.Equal(kind == ChatterEvent.AllyArrived, named);
            }
        }

        [Fact]
        public void SomebodyIsFilledInOnlyWhereTheEventDoesNotNameOne()
        {
            // The rule Chatter acts on when it decides whether to reach for whoever is
            // standing about. Getting it backwards is quiet and nasty: CompanionDied
            // would mourn a living skeleton, with the right grammar and the wrong name.
            Assert.False(EventTokens.ShouldFillIn(ChatterEvent.CompanionDied, TokenSet.Companion));
            Assert.False(EventTokens.ShouldFillIn(ChatterEvent.CompanionKilled, TokenSet.Companion));
            Assert.False(EventTokens.ShouldFillIn(ChatterEvent.AllyArrived, TokenSet.Ally));

            // And the other way, which is what lets a token go in any line at all.
            Assert.True(EventTokens.ShouldFillIn(ChatterEvent.Killed, TokenSet.Companion));
            Assert.True(EventTokens.ShouldFillIn(ChatterEvent.Idle, TokenSet.Companion));
            Assert.True(EventTokens.ShouldFillIn(ChatterEvent.Idle, TokenSet.Ally));
            Assert.True(EventTokens.ShouldFillIn(ChatterEvent.PlayerHurt, TokenSet.Ally));
        }

        [Fact]
        public void EveryEventEitherNamesSomebodyOrFillsThemIn()
        {
            // Belt and braces over the pair above: for both people tokens, every event
            // is on exactly one side of the rule. A third state would mean an event
            // that neither supplies a companion nor is allowed one, which is a line
            // that can never be said and nothing to say why.
            foreach (ChatterEvent kind in Enum.GetValues(typeof(ChatterEvent)))
            {
                foreach (TokenSet token in new[] { TokenSet.Companion, TokenSet.Ally })
                {
                    bool named = (EventTokens.PromisedFor(kind) & token) != 0;

                    Assert.Equal(!named, EventTokens.ShouldFillIn(kind, token));
                }
            }
        }

        [Fact]
        public void NoKillPromisesTheDamage()
        {
            // Not a restatement of the table for its own sake. m_lastHit is sitting
            // on the body and looks like the easy source, and RPC_Damage has already
            // emptied the fire, poison and spirit off it by then - so a kill that
            // claimed {damage} would be quietly reporting an incomplete hit.
            Assert.True((EventTokens.PromisedFor(ChatterEvent.Killed) & TokenSet.Damage) == 0);
            Assert.True((EventTokens.PromisedFor(ChatterEvent.PlayerGotAKill) & TokenSet.Damage) == 0);
        }

        [Fact]
        public void OnlyThePlayersOwnBlowsPromiseAWeapon()
        {
            // Valheim only describes a weapon honestly when a player is holding it: a
            // Skelett's sword is named "Dragur axe" in the data, and its bow reports
            // its skill as Swords. Both were seen in play. Hits refuses them for
            // anybody else, so no other event may promise them.
            const TokenSet weapon = TokenSet.Weapon | TokenSet.WeaponSkill;

            foreach (ChatterEvent kind in Enum.GetValues(typeof(ChatterEvent)))
            {
                bool mine = kind is ChatterEvent.PlayerLandedABigHit or ChatterEvent.PlayerGotAKill;

                Assert.Equal(mine, (EventTokens.PromisedFor(kind) & weapon) != 0);
            }
        }
    }
}
