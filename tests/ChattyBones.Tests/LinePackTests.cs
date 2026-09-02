using System;
using System.Collections.Generic;
using ChattyBones.Logic;

namespace ChattyBones.Tests
{
    /// <summary>
    /// Covers choosing a line, and the fallback that keeps half-written packs working.
    /// </summary>
    /// <remarks>
    /// The one to read first is the pair about the same lineRef giving the same line,
    /// and a negative lineRef not throwing. Both are really tests about multiplayer:
    /// the line ref arrives from somebody else's machine, and everything downstream
    /// assumes two clients with the same pack agree.
    /// </remarks>
    public class LinePackTests
    {
        [Fact]
        public void APackMissingAnEventEntirelyReportsIt()
        {
            // The case that made this worth having: a pack written before an event
            // existed. The hook fires, the budget approves, the pack has nothing, and
            // the skeleton stays quiet - which is indistinguishable from a broken hook
            // unless something says so at load.
            LinePack.Builder builder = new();
            builder.Add(LinePack.SharedPersonality, ChatterEvent.Idle, "Quiet, isn't it.");

            LinePack pack = builder.Build();
            IReadOnlyList<ChatterEvent> missing = pack.EventsWithNoLines();

            Assert.DoesNotContain(ChatterEvent.Idle, missing);
            Assert.Contains(ChatterEvent.PlayerParried, missing);
            Assert.Equal(Enum.GetValues(typeof(ChatterEvent)).Length - 1, missing.Count);
        }

        [Fact]
        public void AnEventCoveredByOnlyOnePersonalityIsNotReportedMissing()
        {
            // Leaving an event to one personality is odd but not silent, and leaving
            // it to the shared lines is the documented way to fill a pack in
            // gradually. Warning about either would be warning about the normal case.
            LinePack.Builder builder = new();
            builder.Add("veteran", ChatterEvent.PlayerDodged, "Lucky.");

            Assert.DoesNotContain(ChatterEvent.PlayerDodged, builder.Build().EventsWithNoLines());
        }

        [Fact]
        public void TheShippedPackIsMissingNothing()
        {
            // The one that would have caught this before it reached a live session.
            Assert.Empty(DefaultPack.Build().EventsWithNoLines());
        }

        private const string Cowardly = "cowardly";
        private const string Boastful = "boastful";

        [Fact]
        public void APersonalitySaysItsOwnLines()
        {
            LinePack pack = new LinePack.Builder()
                .Add(Cowardly, ChatterEvent.TargetAcquired, "Oh gods, a {target}...")
                .Build();

            Assert.True(pack.TryPick(Cowardly, ChatterEvent.TargetAcquired, 0, out string line));
            Assert.Equal("Oh gods, a {target}...", line);
        }

        [Fact]
        public void TheSameLineRefAlwaysGivesTheSameLine()
        {
            // This is the property the whole multiplayer design rests on. Two clients
            // never compare notes; they are handed the same lineRef and are expected to
            // arrive at the same line on their own.
            LinePack pack = new LinePack.Builder()
                .Add(Cowardly, ChatterEvent.Idle, "a", "b", "c", "d", "e")
                .Build();

            for (int lineRef = 0; lineRef < 200; lineRef++)
            {
                Assert.True(pack.TryPick(Cowardly, ChatterEvent.Idle, lineRef, out string first));
                Assert.True(pack.TryPick(Cowardly, ChatterEvent.Idle, lineRef, out string second));
                Assert.Equal(first, second);
            }
        }

        [Fact]
        public void DifferentLineRefsReachEveryLineEventually()
        {
            // Not a distribution test - just enough to catch a fold that can only
            // ever land on one line, which would make every skeleton a broken record.
            LinePack pack = new LinePack.Builder()
                .Add(Cowardly, ChatterEvent.Idle, "a", "b", "c")
                .Build();

            HashSet<string> seen = [];
            for (int lineRef = 0; lineRef < 30; lineRef++)
            {
                Assert.True(pack.TryPick(Cowardly, ChatterEvent.Idle, lineRef, out string line));
                seen.Add(line);
            }

            Assert.Equal(3, seen.Count);
        }

        [Fact]
        public void ANegativeLineRefIsFineRatherThanFatal()
        {
            // A line ref is whatever another client wrote into a ZDO. C# gives a negative
            // result for the remainder of a negative number, and a negative array
            // index throws, so this would be an exception in the middle of a fight on
            // somebody else's machine.
            LinePack pack = new LinePack.Builder()
                .Add(Cowardly, ChatterEvent.Idle, "a", "b", "c")
                .Build();

            Assert.True(pack.TryPick(Cowardly, ChatterEvent.Idle, -7, out string line));
            Assert.NotNull(line);

            Assert.True(pack.TryPick(Cowardly, ChatterEvent.Idle, int.MinValue, out line));
            Assert.NotNull(line);
        }

        [Fact]
        public void APersonalityWithNothingToSayFallsBackToTheSharedLines()
        {
            LinePack pack = new LinePack.Builder()
                .Add(Cowardly, ChatterEvent.Idle, "My bones are itchy.")
                .Add(LinePack.SharedPersonality, ChatterEvent.Died, "Ohh, that's it for me.")
                .Build();

            Assert.True(pack.TryPick(Cowardly, ChatterEvent.Died, 0, out string line));
            Assert.Equal("Ohh, that's it for me.", line);
        }

        [Fact]
        public void TheFallbackIsPerEventNotPerPersonality()
        {
            // A half-written personality uses its own lines where it has them and the
            // shared ones where it does not, rather than being all-or-nothing. That is
            // what makes filling a pack in gradually pleasant.
            LinePack pack = new LinePack.Builder()
                .Add(Cowardly, ChatterEvent.Idle, "mine")
                .Add(LinePack.SharedPersonality, ChatterEvent.Idle, "shared idle")
                .Add(LinePack.SharedPersonality, ChatterEvent.Died, "shared death")
                .Build();

            Assert.True(pack.TryPick(Cowardly, ChatterEvent.Idle, 0, out string idle));
            Assert.Equal("mine", idle);

            Assert.True(pack.TryPick(Cowardly, ChatterEvent.Died, 0, out string died));
            Assert.Equal("shared death", died);
        }

        [Fact]
        public void NothingToSayIsAnOrdinaryAnswer()
        {
            LinePack pack = new LinePack.Builder()
                .Add(Cowardly, ChatterEvent.Idle, "hmm")
                .Build();

            // No lines for this event anywhere, and no shared personality either. The
            // skeleton simply says nothing, which a pack author is entitled to want.
            Assert.False(pack.TryPick(Cowardly, ChatterEvent.Unsummoned, 0, out string line));
            Assert.Null(line);
        }

        [Fact]
        public void APersonalityNobodyDefinedSaysNothing()
        {
            LinePack pack = new LinePack.Builder()
                .Add(Cowardly, ChatterEvent.Idle, "hmm")
                .Build();

            Assert.False(pack.TryPick("nonexistent", ChatterEvent.Idle, 0, out _));
        }

        [Fact]
        public void AMissingPersonalityStillReachesTheSharedLines()
        {
            LinePack pack = new LinePack.Builder()
                .Add(LinePack.SharedPersonality, ChatterEvent.Idle, "shared")
                .Build();

            // Covers a skeleton whose stored personality no longer exists, which is
            // exactly what happens when a player edits a pack and removes one while
            // their skeletons are still standing around.
            Assert.True(pack.TryPick("deleted-personality", ChatterEvent.Idle, 0, out string line));
            Assert.Equal("shared", line);
        }

        [Fact]
        public void ANullPersonalityIsHandledRatherThanThrowing()
        {
            LinePack pack = new LinePack.Builder()
                .Add(LinePack.SharedPersonality, ChatterEvent.Idle, "shared")
                .Build();

            Assert.True(pack.TryPick(null, ChatterEvent.Idle, 0, out string line));
            Assert.Equal("shared", line);
        }

        [Fact]
        public void PersonalitiesComeBackInAStableOrder()
        {
            // Assigning a personality means choosing an index into this list, so the
            // order has to be the same on every client and after every restart. Added
            // deliberately out of order here.
            LinePack pack = new LinePack.Builder()
                .Add("zealous", ChatterEvent.Idle, "z")
                .Add(Cowardly, ChatterEvent.Idle, "c")
                .Add(Boastful, ChatterEvent.Idle, "b")
                .Build();

            Assert.Equal(["boastful", "cowardly", "zealous"], pack.Personalities);
        }

        [Fact]
        public void TheSharedPersonalityIsNotSomethingYouCanBe()
        {
            LinePack pack = new LinePack.Builder()
                .Add(Cowardly, ChatterEvent.Idle, "c")
                .Add(LinePack.SharedPersonality, ChatterEvent.Idle, "shared")
                .Build();

            // It is a fallback, not a character. A skeleton assigned "common" would
            // have no voice of its own at all.
            Assert.Equal(["cowardly"], pack.Personalities);
        }

        [Fact]
        public void BlankLinesAreSkippedRatherThanKept()
        {
            // A hand-edited file will eventually have a stray empty entry in it.
            // Keeping it would give a skeleton that occasionally says nothing at all
            // while using up its turn, which looks exactly like a bug.
            LinePack pack = new LinePack.Builder()
                .Add(Cowardly, ChatterEvent.Idle, "real", "", "   ", null)
                .Build();

            for (int lineRef = 0; lineRef < 20; lineRef++)
            {
                Assert.True(pack.TryPick(Cowardly, ChatterEvent.Idle, lineRef, out string line));
                Assert.Equal("real", line);
            }
        }

        [Fact]
        public void APersonalityWithOnlyBlanksDoesNotExist()
        {
            LinePack pack = new LinePack.Builder()
                .Add(Cowardly, ChatterEvent.Idle, "real")
                .Add(Boastful, ChatterEvent.Idle, "", "  ")
                .Build();

            // Otherwise it could be assigned to a skeleton that then never speaks.
            Assert.Equal(["cowardly"], pack.Personalities);
            Assert.False(pack.TryPick(Boastful, ChatterEvent.Idle, 0, out _));
        }

        [Fact]
        public void AddingTheSameGroupTwiceGathersRatherThanReplaces()
        {
            // A pack file is allowed to mention a personality in more than one place,
            // and losing the first half silently would be a miserable thing to debug.
            LinePack pack = new LinePack.Builder()
                .Add(Cowardly, ChatterEvent.Idle, "first")
                .Add(Cowardly, ChatterEvent.Idle, "second")
                .Build();

            HashSet<string> seen = [];
            for (int lineRef = 0; lineRef < 20; lineRef++)
            {
                Assert.True(pack.TryPick(Cowardly, ChatterEvent.Idle, lineRef, out string line));
                seen.Add(line);
            }

            Assert.Equal(2, seen.Count);
        }

        [Fact]
        public void AListenerLandsOnTheSameLineTheOwnerChose()
        {
            // The whole point of mirroring a line ref rather than an index: two clients
            // with the same pack fold the same number to the same line, and neither has
            // to know anything about the other's file.
            LinePack.Builder builder = new();
            builder.Add(Cowardly, ChatterEvent.Idle, "One.", "Two.", "Three.");

            LinePack pack = builder.Build();
            LineTokens tokens = Everything();

            for (int lineRef = 0; lineRef < 30; lineRef++)
            {
                Assert.True(pack.TryPick(Cowardly, ChatterEvent.Idle, lineRef, out string chosen));
                Assert.True(pack.TryPickRenderable(Cowardly, ChatterEvent.Idle, lineRef, tokens, out string heard));

                Assert.Equal(chosen, heard);
            }
        }

        [Fact]
        public void ALineTheListenerCannotRenderSlidesOnToTheNextOne()
        {
            // The decision this method exists to implement. The owner had a {weapon} in
            // hand and we do not, so rather than going silent we take the next line
            // along. Two players read different bubbles over the same skeleton, which
            // the design already accepts for two different packs - and silence is the
            // failure this mod has paid for over and over.
            LinePack.Builder builder = new();
            builder.Add(Cowardly, ChatterEvent.Killed, "Down it goes, by the {weapon}.", "Down it goes.");

            LinePack pack = builder.Build();
            LineTokens missing = new(target: null, player: "Ragnar", name: "Botvid");

            Assert.True(pack.TryPickRenderable(Cowardly, ChatterEvent.Killed, 0, missing, out string line));

            Assert.Equal("Down it goes.", line);
        }

        [Fact]
        public void AListenerWithNothingItCanRenderStaysQuiet()
        {
            // The one case where silence is right: every line in the space wants
            // something this client could not work out. Walking further would only go
            // round the same lines again.
            LinePack.Builder builder = new();
            builder.Add(Cowardly, ChatterEvent.Killed, "Got the {target}!", "{target} is down.");

            LinePack pack = builder.Build();
            LineTokens missing = new(target: null, player: "Ragnar", name: "Botvid");

            Assert.False(pack.TryPickRenderable(Cowardly, ChatterEvent.Killed, 0, missing, out string line));
            Assert.Null(line);
        }

        [Fact]
        public void AListenerReachesTheContextGroupsToo()
        {
            // A listener never resolves a context - it has no idea whether the skeleton
            // is in the Swamp - so it walks the whole numbering. The owner's line ref is
            // counted against exactly that same list, which is what makes the two agree
            // without the listener asking a single question about where anybody is.
            LinePack.Builder builder = new();
            builder.Add(Cowardly, Key("Idle[biome=Swamp]"), "I do not like it here.");
            builder.Add(Cowardly, ChatterEvent.Idle, "Hmm.");

            LinePack pack = builder.Build();
            LineTokens tokens = Everything();

            Assert.True(pack.TryPickRenderable(Cowardly, ChatterEvent.Idle, 0, tokens, out string first));
            Assert.True(pack.TryPickRenderable(Cowardly, ChatterEvent.Idle, 1, tokens, out string second));

            Assert.Equal("I do not like it here.", first);
            Assert.Equal("Hmm.", second);
        }

        [Fact]
        public void ANegativeLineRefFromAnotherClientDoesNotThrow()
        {
            // A packed counter above 127 sets the top bit, so line refs arriving from
            // another machine really are negative, and C# gives a negative remainder
            // for a negative operand. TryPick has the same test for the same reason.
            LinePack.Builder builder = new();
            builder.Add(Cowardly, ChatterEvent.Idle, "One.", "Two.");

            LinePack pack = builder.Build();

            Assert.True(pack.TryPickRenderable(Cowardly, ChatterEvent.Idle, -7, Everything(), out _));
            Assert.True(pack.TryPickRenderable(Cowardly, ChatterEvent.Idle, int.MinValue, Everything(), out _));
        }

        /// <summary>Parse a tagged event key, failing the test if it will not.</summary>
        /// <returns>The key.</returns>
        /// <param name="text">Something like "Idle[biome=Swamp]".</param>
        private static EventKey Key(string text)
        {
            Assert.True(EventKey.TryParse(text, out EventKey key, out string problem), problem);

            return key;
        }

        /// <summary>Tokens with a value for everything, so only the walk is under test.</summary>
        /// <returns>A full set.</returns>
        private static LineTokens Everything()
        {
            return new LineTokens(
                target: "Greydwarf",
                player: "Ragnar",
                name: "Botvid",
                companion: "Gunnar",
                ally: "Sigrid",
                details: new LineDetails(
                    weapon: "Mistwalker",
                    weaponType: "sword",
                    damage: "slash",
                    status: "Burning",
                    biome: "Black Forest",
                    item: "Carrot Soup",
                    skill: "Blocking"));
        }

        [Fact]
        public void AnEmptyPackIsUsableRatherThanBroken()
        {
            // What we hand out if the player's file is missing or unreadable. Every
            // skeleton is simply mute, and nothing anywhere has to special-case null.
            LinePack pack = new LinePack.Builder().Build();

            Assert.Empty(pack.Personalities);
            Assert.False(pack.TryPick(Cowardly, ChatterEvent.Idle, 0, out _));
        }
    }
}
