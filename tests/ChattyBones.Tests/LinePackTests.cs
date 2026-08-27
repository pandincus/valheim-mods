using System.Collections.Generic;
using ChattyBones.Logic;

namespace ChattyBones.Tests
{
    /// <summary>
    /// Covers choosing a line, and the fallback that keeps half-written packs working.
    /// </summary>
    /// <remarks>
    /// The one to read first is the pair about the same seed giving the same line,
    /// and a negative seed not throwing. Both are really tests about multiplayer:
    /// the seed arrives from somebody else's machine, and everything downstream
    /// assumes two clients with the same pack agree.
    /// </remarks>
    public class LinePackTests
    {
        private const string Cowardly = "cowardly";
        private const string Boastful = "boastful";

        [Fact]
        public void APersonalitySaysItsOwnLines()
        {
            LinePack pack = new LinePackBuilder()
                .Add(Cowardly, ChatterEvent.TargetAcquired, "Oh gods, a {target}...")
                .Build();

            Assert.True(pack.TryPick(Cowardly, ChatterEvent.TargetAcquired, 0, out string line));
            Assert.Equal("Oh gods, a {target}...", line);
        }

        [Fact]
        public void TheSameSeedAlwaysGivesTheSameLine()
        {
            // This is the property the whole multiplayer design rests on. Two clients
            // never compare notes; they are handed the same seed and are expected to
            // arrive at the same line on their own.
            LinePack pack = new LinePackBuilder()
                .Add(Cowardly, ChatterEvent.Idle, "a", "b", "c", "d", "e")
                .Build();

            for (int seed = 0; seed < 200; seed++)
            {
                Assert.True(pack.TryPick(Cowardly, ChatterEvent.Idle, seed, out string first));
                Assert.True(pack.TryPick(Cowardly, ChatterEvent.Idle, seed, out string second));
                Assert.Equal(first, second);
            }
        }

        [Fact]
        public void DifferentSeedsReachEveryLineEventually()
        {
            // Not a distribution test - just enough to catch a fold that can only
            // ever land on one line, which would make every skeleton a broken record.
            LinePack pack = new LinePackBuilder()
                .Add(Cowardly, ChatterEvent.Idle, "a", "b", "c")
                .Build();

            HashSet<string> seen = [];
            for (int seed = 0; seed < 30; seed++)
            {
                Assert.True(pack.TryPick(Cowardly, ChatterEvent.Idle, seed, out string line));
                _ = seen.Add(line);
            }

            Assert.Equal(3, seen.Count);
        }

        [Fact]
        public void ANegativeSeedIsFineRatherThanFatal()
        {
            // A seed is whatever another client wrote into a ZDO. C# gives a negative
            // result for the remainder of a negative number, and a negative array
            // index throws, so this would be an exception in the middle of a fight on
            // somebody else's machine.
            LinePack pack = new LinePackBuilder()
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
            LinePack pack = new LinePackBuilder()
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
            LinePack pack = new LinePackBuilder()
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
            LinePack pack = new LinePackBuilder()
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
            LinePack pack = new LinePackBuilder()
                .Add(Cowardly, ChatterEvent.Idle, "hmm")
                .Build();

            Assert.False(pack.TryPick("nonexistent", ChatterEvent.Idle, 0, out _));
        }

        [Fact]
        public void AMissingPersonalityStillReachesTheSharedLines()
        {
            LinePack pack = new LinePackBuilder()
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
            LinePack pack = new LinePackBuilder()
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
            LinePack pack = new LinePackBuilder()
                .Add("zealous", ChatterEvent.Idle, "z")
                .Add(Cowardly, ChatterEvent.Idle, "c")
                .Add(Boastful, ChatterEvent.Idle, "b")
                .Build();

            Assert.Equal(["boastful", "cowardly", "zealous"], pack.Personalities);
        }

        [Fact]
        public void TheSharedPersonalityIsNotSomethingYouCanBe()
        {
            LinePack pack = new LinePackBuilder()
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
            LinePack pack = new LinePackBuilder()
                .Add(Cowardly, ChatterEvent.Idle, "real", "", "   ", null)
                .Build();

            for (int seed = 0; seed < 20; seed++)
            {
                Assert.True(pack.TryPick(Cowardly, ChatterEvent.Idle, seed, out string line));
                Assert.Equal("real", line);
            }
        }

        [Fact]
        public void APersonalityWithOnlyBlanksDoesNotExist()
        {
            LinePack pack = new LinePackBuilder()
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
            LinePack pack = new LinePackBuilder()
                .Add(Cowardly, ChatterEvent.Idle, "first")
                .Add(Cowardly, ChatterEvent.Idle, "second")
                .Build();

            HashSet<string> seen = [];
            for (int seed = 0; seed < 20; seed++)
            {
                Assert.True(pack.TryPick(Cowardly, ChatterEvent.Idle, seed, out string line));
                _ = seen.Add(line);
            }

            Assert.Equal(2, seen.Count);
        }

        [Fact]
        public void AnEmptyPackIsUsableRatherThanBroken()
        {
            // What we hand out if the player's file is missing or unreadable. Every
            // skeleton is simply mute, and nothing anywhere has to special-case null.
            LinePack pack = new LinePackBuilder().Build();

            Assert.Empty(pack.Personalities);
            Assert.False(pack.TryPick(Cowardly, ChatterEvent.Idle, 0, out _));
        }
    }
}
