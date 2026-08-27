using System;
using System.Collections.Generic;
using ChattyBones.Logic;

namespace ChattyBones.Tests
{
    /// <summary>
    /// Covers the owner-side choice of what to say, and never saying it twice running.
    /// </summary>
    /// <remarks>
    /// Two tests carry the weight here.
    /// <see cref="TheBroadcastSeedReproducesTheLineWithoutAnyOfOurState"/> is the
    /// multiplayer contract written down - it does exactly what a receiving client
    /// does and checks it lands on the same words. And
    /// <see cref="ItNeverRepeatsImmediatelyForAnySeedAtAll"/> sweeps thousands of
    /// starting points rather than trusting one, because the previous design passed
    /// a single-seed version of this very test while failing about one time in 256.
    ///
    /// Every Random is constructed with a literal so a failure is the same failure
    /// tomorrow.
    /// </remarks>
    public class LineChooserTests
    {
        private const string Cowardly = "cowardly";

        private static LineTokens Tokens()
        {
            return new LineTokens(target: "Greydwarf", player: "Dan", name: "Rattles", companion: "Bjorn");
        }

        private static LinePack Pack(params string[] idleLines)
        {
            return new LinePack.Builder()
                .Add(Cowardly, ChatterEvent.Idle, idleLines)
                .Build();
        }

        [Fact]
        public void APackWithNothingForThisEventSaysNothing()
        {
            LineChooser chooser = new();

            Assert.False(chooser.TryChoose(
                Pack("hmm"), Cowardly, ChatterEvent.Died, Tokens(), new Random(1), out _, out _));
        }

        [Fact]
        public void ASingleLineIsChosenAndRendered()
        {
            LinePack pack = new LinePack.Builder()
                .Add(Cowardly, ChatterEvent.TargetAcquired, "Oh gods, a {target}...")
                .Build();
            LineChooser chooser = new();

            Assert.True(chooser.TryChoose(
                pack, Cowardly, ChatterEvent.TargetAcquired, Tokens(), new Random(1),
                out _, out string line));

            Assert.Equal("Oh gods, a Greydwarf...", line);
        }

        [Fact]
        public void ItNeverRepeatsImmediatelyForAnySeedAtAll()
        {
            // The one promise this class makes. It is structural now - we walk the
            // group and take the first usable line that is not the last one said - so
            // it should hold for every starting point rather than most of them.
            //
            // The sweep is the whole point. The previous design rolled seeds and gave
            // up after eight tries, which failed roughly 1 in 256 and happily passed a
            // single-Random version of this test.
            LinePack pack = Pack("a", "b");
            int repeats = 0;

            for (int sweep = 0; sweep < 3000; sweep++)
            {
                LineChooser chooser = new();
                Random random = new(sweep);
                string previous = null;

                for (int i = 0; i < 10; i++)
                {
                    Assert.True(chooser.TryChoose(
                        pack, Cowardly, ChatterEvent.Idle, Tokens(), random, out _, out string line));

                    if (line == previous)
                    {
                        repeats++;
                    }

                    previous = line;
                }
            }

            Assert.Equal(0, repeats);
        }

        [Fact]
        public void AGroupWithOneLineRepeatsRatherThanFallingSilent()
        {
            // The only case where a repeat is allowed, because the alternative is a
            // skeleton that answers once and is then mute forever.
            LinePack pack = Pack("only one");
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
        public void OneUsableLineAmongManyIsAlwaysFound()
        {
            // Nine lines want a {target} we have not got and one does not. Walking the
            // group finds it every time; the old rejection sampling could miss it on
            // all eight rolls and leave the skeleton silent for no reason a player
            // could ever work out.
            LinePack pack = Pack(
                "A {target}!", "Another {target}.", "{target} again", "More {target}",
                "My bones are itchy.",
                "{target}!", "Ugh, {target}", "{target} once more", "Still {target}", "{target} yet again");

            LineTokens noTarget = new(target: null, player: "Dan", name: "Rattles");

            for (int sweep = 0; sweep < 500; sweep++)
            {
                LineChooser chooser = new();

                Assert.True(chooser.TryChoose(
                    pack, Cowardly, ChatterEvent.Idle, noTarget, new Random(sweep), out _, out string line));
                Assert.Equal("My bones are itchy.", line);
            }
        }

        [Fact]
        public void AGroupWeCanNeverFillInSaysNothing()
        {
            LinePack pack = Pack("A {target}!", "Another {target}!");
            LineChooser chooser = new();
            LineTokens noTarget = new(target: null, player: "Dan", name: "Rattles");

            Assert.False(chooser.TryChoose(
                pack, Cowardly, ChatterEvent.Idle, noTarget, new Random(5), out _, out _));
        }

        [Fact]
        public void OneChooserSpeakingForSeveralSkeletonsStillDoesNotRepeat()
        {
            // Phase 4 must share a single chooser across the squad rather than giving
            // each skeleton its own, because hearing the same line twice running is
            // just as tiresome when two different skeletons say it. Nothing in the
            // signature enforces that - TryChoose takes no speaker - so this records
            // the requirement rather than proving it.
            LinePack pack = Pack("a", "b");
            LineChooser shared = new();
            Random random = new(2);

            Assert.True(shared.TryChoose(
                pack, Cowardly, ChatterEvent.Idle, Tokens(), random, out _, out string first));
            Assert.True(shared.TryChoose(
                pack, Cowardly, ChatterEvent.Idle, Tokens(), random, out _, out string second));

            Assert.NotEqual(first, second);
        }

        [Fact]
        public void EveryLineInAGroupIsReachable()
        {
            // Catches a walk that can only ever start in one place, which would make
            // most of a pack unreachable without anything looking broken.
            LinePack pack = Pack("a", "b", "c", "d", "e");
            HashSet<string> seen = [];

            for (int sweep = 0; sweep < 200; sweep++)
            {
                LineChooser chooser = new();

                Assert.True(chooser.TryChoose(
                    pack, Cowardly, ChatterEvent.Idle, Tokens(), new Random(sweep), out _, out string line));
                _ = seen.Add(line);
            }

            Assert.Equal(5, seen.Count);
        }

        [Fact]
        public void TheBroadcastSeedReproducesTheLineWithoutAnyOfOurState()
        {
            // This is the contract the whole multiplayer design rests on. We choose a
            // line using state nobody else has, then hand over only a number. A client
            // with the same pack and none of our history has to arrive at the same
            // words - so we check by doing exactly what such a client would do.
            LinePack pack = new LinePack.Builder()
                .Add(Cowardly, ChatterEvent.TargetAcquired,
                    "A {target}!", "Not another {target}.", "{name} sees a {target}.", "Ach, {companion}, a {target}!")
                .Build();
            LineChooser chooser = new();
            Random random = new(99);

            for (int i = 0; i < 200; i++)
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
            // int, and the Utterance constructor now refuses anything that would not
            // fit. A chooser producing values too large would work perfectly in single
            // player and throw the first time somebody joined.
            LinePack pack = Pack("a", "b", "c");
            LineChooser chooser = new();
            Random random = new(21);

            for (int i = 0; i < 200; i++)
            {
                Assert.True(chooser.TryChoose(
                    pack, Cowardly, ChatterEvent.Idle, Tokens(), random, out int seed, out _));

                Assert.InRange(seed, 0, Utterance.MaxSeed);

                Utterance sent = new(1, ChatterEvent.Idle, seed, 0);
                Assert.True(Utterance.TryUnpack(sent.Pack(), 0, out Utterance got));
                Assert.Equal(seed, got.Seed);
            }
        }

        [Fact]
        public void TheSeedIsNotJustTheIndex()
        {
            // We send one of the many seeds that fold to the chosen index rather than
            // the index itself. Nothing breaks if we sent the index, but a pack with
            // three lines would then forever put 0, 1 and 2 on the wire, which is a
            // needlessly legible thing to be broadcasting.
            LinePack pack = Pack("a", "b", "c");
            LineChooser chooser = new();
            Random random = new(31);
            bool sawSomethingBigger = false;

            for (int i = 0; i < 100; i++)
            {
                Assert.True(chooser.TryChoose(
                    pack, Cowardly, ChatterEvent.Idle, Tokens(), random, out int seed, out _));

                if (seed > 2)
                {
                    sawSomethingBigger = true;
                }
            }

            Assert.True(sawSomethingBigger);
        }
    }
}
