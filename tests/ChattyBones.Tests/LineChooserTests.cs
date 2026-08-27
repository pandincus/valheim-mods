using System;
using System.Collections.Generic;
using ChattyBones.Logic;

namespace ChattyBones.Tests
{
    /// <summary>
    /// Covers the owner-side choice of what to say, and not saying it twice running.
    /// </summary>
    /// <remarks>
    /// The test that matters most is the last one, which checks that the seed we
    /// broadcast really does reproduce the line on a client that has none of our
    /// state. Everything else here is comfort; that one is the contract.
    ///
    /// Random is always constructed with a fixed value so a failure is the same
    /// failure tomorrow.
    /// </remarks>
    public class LineChooserTests
    {
        private const string Cowardly = "cowardly";

        private static LineTokens Tokens()
        {
            return new LineTokens("Greydwarf", "Dan", "Rattles");
        }

        [Fact]
        public void APackWithNothingForThisEventSaysNothing()
        {
            LinePack pack = new LinePackBuilder()
                .Add(Cowardly, ChatterEvent.Idle, "hmm")
                .Build();
            LineChooser chooser = new();

            Assert.False(chooser.TryChoose(
                pack, Cowardly, ChatterEvent.Died, Tokens(), new Random(1), out _, out _));
        }

        [Fact]
        public void ASingleLineIsChosenAndRendered()
        {
            LinePack pack = new LinePackBuilder()
                .Add(Cowardly, ChatterEvent.TargetAcquired, "Oh gods, a {target}...")
                .Build();
            LineChooser chooser = new();

            Assert.True(chooser.TryChoose(
                pack, Cowardly, ChatterEvent.TargetAcquired, Tokens(), new Random(1),
                out _, out string line));

            Assert.Equal("Oh gods, a Greydwarf...", line);
        }

        [Fact]
        public void ItAvoidsSayingTheSameThingTwiceRunning()
        {
            LinePack pack = new LinePackBuilder()
                .Add(Cowardly, ChatterEvent.Idle, "a", "b", "c", "d", "e", "f")
                .Build();
            LineChooser chooser = new();
            Random random = new(7);

            string previous = null;
            for (int i = 0; i < 20; i++)
            {
                Assert.True(chooser.TryChoose(
                    pack, Cowardly, ChatterEvent.Idle, Tokens(), random, out _, out string line));

                Assert.NotEqual(previous, line);
                previous = line;
            }
        }

        [Fact]
        public void AGroupWithOneLineRepeatsRatherThanFallingSilent()
        {
            // Every roll gives the only line there is, and it is always in memory
            // after the first go. Giving up and repeating is much better than a
            // skeleton that answers once and is then mute forever.
            LinePack pack = new LinePackBuilder()
                .Add(Cowardly, ChatterEvent.Idle, "only one")
                .Build();
            LineChooser chooser = new();
            Random random = new(3);

            for (int i = 0; i < 5; i++)
            {
                Assert.True(chooser.TryChoose(
                    pack, Cowardly, ChatterEvent.Idle, Tokens(), random, out _, out string line));
                Assert.Equal("only one", line);
            }
        }

        [Fact]
        public void ALineWeCannotFillInIsPassedOverForOneWeCan()
        {
            // A pack author put a {target} in an idle line. The skeleton should use
            // the other line rather than going quiet.
            LinePack pack = new LinePackBuilder()
                .Add(Cowardly, ChatterEvent.Idle, "Where did that {target} go?", "My bones are itchy.")
                .Build();
            LineChooser chooser = new();
            LineTokens noTarget = new(null, "Dan", "Rattles");
            Random random = new(11);

            for (int i = 0; i < 10; i++)
            {
                Assert.True(chooser.TryChoose(
                    pack, Cowardly, ChatterEvent.Idle, noTarget, random, out _, out string line));
                Assert.Equal("My bones are itchy.", line);
            }
        }

        [Fact]
        public void AGroupWeCanNeverFillInSaysNothing()
        {
            LinePack pack = new LinePackBuilder()
                .Add(Cowardly, ChatterEvent.Idle, "A {target}!", "Another {target}!")
                .Build();
            LineChooser chooser = new();
            LineTokens noTarget = new(null, "Dan", "Rattles");

            Assert.False(chooser.TryChoose(
                pack, Cowardly, ChatterEvent.Idle, noTarget, new Random(5), out _, out _));
        }

        [Fact]
        public void TheMemoryIsSharedAcrossTheWholeSquad()
        {
            // One chooser serves every skeleton, because hearing the same line twice
            // running is just as tiresome when two different skeletons say it.
            LinePack pack = new LinePackBuilder()
                .Add(Cowardly, ChatterEvent.Idle, "a", "b")
                .Build();
            LineChooser chooser = new();
            Random random = new(2);

            Assert.True(chooser.TryChoose(
                pack, Cowardly, ChatterEvent.Idle, Tokens(), random, out _, out string first));
            Assert.True(chooser.TryChoose(
                pack, Cowardly, ChatterEvent.Idle, Tokens(), random, out _, out string second));

            Assert.NotEqual(first, second);
        }

        [Fact]
        public void ForgettingIsBoundedSoOldLinesComeBack()
        {
            // Memory of 2 over a group of 3: line "a" has to become available again
            // once two others have been said, or the pack would run dry.
            LinePack pack = new LinePackBuilder()
                .Add(Cowardly, ChatterEvent.Idle, "a", "b", "c")
                .Build();
            LineChooser chooser = new(memory: 2);
            Random random = new(13);

            HashSet<string> seen = [];
            for (int i = 0; i < 30; i++)
            {
                Assert.True(chooser.TryChoose(
                    pack, Cowardly, ChatterEvent.Idle, Tokens(), random, out _, out string line));
                _ = seen.Add(line);
            }

            Assert.Equal(3, seen.Count);
        }

        [Fact]
        public void ZeroMemoryStillNeverRepeatsImmediately()
        {
            // The memory setting says how far back to steer away from. Not repeating
            // the line you just said is separate from that and holds whatever the
            // memory is set to, including off - it is the one promise this class makes
            // unconditionally.
            LinePack pack = new LinePackBuilder()
                .Add(Cowardly, ChatterEvent.Idle, "a", "b")
                .Build();
            LineChooser chooser = new(memory: 0);
            Random random = new(4);

            string previous = null;
            for (int i = 0; i < 10; i++)
            {
                Assert.True(chooser.TryChoose(
                    pack, Cowardly, ChatterEvent.Idle, Tokens(), random, out _, out string line));

                Assert.NotEqual(previous, line);
                previous = line;
            }
        }

        [Fact]
        public void TheBroadcastSeedReproducesTheLineWithoutAnyOfOurState()
        {
            // This is the contract the whole multiplayer design rests on. We choose a
            // line using memory nobody else has, then hand over only a number. A
            // client with the same pack and none of our history has to arrive at the
            // same words - so we check by doing exactly what such a client would do.
            LinePack pack = new LinePackBuilder()
                .Add(Cowardly, ChatterEvent.TargetAcquired, "A {target}!", "Not another {target}.", "{name} sees a {target}.")
                .Build();
            LineChooser chooser = new();
            Random random = new(99);

            for (int i = 0; i < 25; i++)
            {
                Assert.True(chooser.TryChoose(
                    pack, Cowardly, ChatterEvent.TargetAcquired, Tokens(), random,
                    out int seed, out string ours));

                // The receiving client: pick by seed, render, and nothing else.
                Assert.True(pack.TryPick(Cowardly, ChatterEvent.TargetAcquired, seed, out string template));
                Assert.True(Tokens().TryRender(template, out string theirs));

                Assert.Equal(ours, theirs);
            }
        }

        [Fact]
        public void TheSeedSurvivesBeingPackedIntoAnUtterance()
        {
            // The seed does not travel on its own; it goes into 16 bits of a packed
            // int. A chooser that produced values too large for that would work
            // perfectly in single player and quietly desync in multiplayer.
            LinePack pack = new LinePackBuilder()
                .Add(Cowardly, ChatterEvent.Idle, "a", "b", "c")
                .Build();
            LineChooser chooser = new();
            Random random = new(21);

            for (int i = 0; i < 50; i++)
            {
                Assert.True(chooser.TryChoose(
                    pack, Cowardly, ChatterEvent.Idle, Tokens(), random, out int seed, out _));

                Utterance sent = new(1, ChatterEvent.Idle, seed, 0);
                Assert.True(Utterance.TryUnpack(sent.Pack(), 0, out Utterance got));
                Assert.Equal(seed, got.Seed);
            }
        }
    }
}
