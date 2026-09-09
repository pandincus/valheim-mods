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
    /// <see cref="TheBroadcastLineRefReproducesTheLineWithoutAnyOfOurState"/> is the
    /// multiplayer contract written down - it does exactly what a receiving client
    /// does and checks it lands on the same words. And
    /// <see cref="ItNeverRepeatsImmediatelyForAnyLineRefAtAll"/> sweeps thousands of
    /// starting points rather than trusting one, because the previous design passed
    /// a single-lineRef version of this very test while failing about one time in 256.
    ///
    /// Every Random is constructed with a literal so a failure is the same failure
    /// tomorrow.
    /// </remarks>
    public class LineChooserTests
    {
        private const string Cowardly = "cowardly";

        private static LineTokens Tokens()
        {
            return new LineTokens(target: "Greydwarf", player: "Ragnar", name: "Rattles", companion: "Bjorn");
        }

        private static LinePack Pack(params string[] idleLines)
        {
            return new LinePack.Builder()
                .Add(Cowardly, ChatterEvent.Idle, idleLines)
                .Build();
        }

        /// <summary>A pack where the personality and the shared lines both have something.</summary>
        /// <returns>The pack.</returns>
        /// <param name="own">The cowardly lines.</param>
        /// <param name="shared">The common lines.</param>
        private static LinePack Split(string[] own, string[] shared)
        {
            return new LinePack.Builder()
                .Add(Cowardly, ChatterEvent.Idle, own)
                .Add(LinePack.SharedPersonality, ChatterEvent.Idle, shared)
                .Build();
        }

        /// <summary>Say a lot, and report what came out.</summary>
        /// <returns>Every line said, in order.</returns>
        /// <param name="pack">The pack to draw from.</param>
        /// <param name="times">How many utterances to take.</param>
        /// <param name="seed">Fixed, so a failure is the same failure tomorrow.</param>
        private static List<string> Speak(LinePack pack, int times, int seed)
        {
            LineChooser chooser = new();
            Random random = new(seed);
            List<string> said = [];

            for (int i = 0; i < times; i++)
            {
                if (chooser.TryChoose(pack, Cowardly, ChatterEvent.Idle, Tokens(), random, out _, out string line))
                {
                    said.Add(line);
                }
            }

            return said;
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
        public void ItNeverRepeatsImmediatelyForAnyLineRefAtAll()
        {
            // The one promise this class makes. It is structural now - we walk the
            // group and take the first usable line that is not the last one said - so
            // it should hold for every starting point rather than most of them.
            //
            // The sweep is the whole point. The previous design rolled line refs and gave
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
        public void TheSameJokeAboutADifferentEnemyStillCountsAsARepeat()
        {
            // _lastSaid holds the template, not the rendered text. "Get lost,
            // {target}!" said about a greydwarf and then about a seeker is the same
            // joke twice, and should feel like one.
            //
            // Every other test in this file uses one fixed set of tokens for its whole
            // run, so template and rendered text are 1:1 and this distinction is
            // invisible - which is exactly how it went uncovered. Comparing rendered
            // text instead of templates passed the entire suite.
            LinePack pack = Pack("Get lost, {target}!", "My bones are itchy.");

            LineTokens greydwarf = new(target: "Greydwarf", player: "Ragnar", name: "Rattles");
            LineTokens seeker = new(target: "Seeker", player: "Ragnar", name: "Rattles");

            for (int sweep = 0; sweep < 50; sweep++)
            {
                LineChooser chooser = new();
                Random random = new(sweep);

                // Drive it to say the joke. With two lines, if the first pick is the
                // other one then the second pick has to be this.
                Assert.True(chooser.TryChoose(
                    pack, Cowardly, ChatterEvent.Idle, greydwarf, random, out _, out string said));

                if (said != "Get lost, Greydwarf!")
                {
                    Assert.True(chooser.TryChoose(
                        pack, Cowardly, ChatterEvent.Idle, greydwarf, random, out _, out said));
                }

                Assert.Equal("Get lost, Greydwarf!", said);

                // Now a different enemy. The words would differ, but the joke does not,
                // so we should get the other line.
                Assert.True(chooser.TryChoose(
                    pack, Cowardly, ChatterEvent.Idle, seeker, random, out _, out string next));

                Assert.Equal("My bones are itchy.", next);
            }
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

            LineTokens noTarget = new(target: null, player: "Ragnar", name: "Rattles");

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
            LineTokens noTarget = new(target: null, player: "Ragnar", name: "Rattles");

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
                seen.Add(line);
            }

            Assert.Equal(5, seen.Count);
        }

        [Fact]
        public void TheBroadcastLineRefReproducesTheLineWithoutAnyOfOurState()
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
                    out int lineRef, out string ours));

                // The receiving client: pick by lineRef, render, and nothing else.
                Assert.True(pack.TryPick(Cowardly, ChatterEvent.TargetAcquired, lineRef, out string template));
                Assert.True(Tokens().TryRender(template, out string theirs));

                Assert.Equal(ours, theirs);
            }
        }

        [Fact]
        public void APersonalityWithItsOwnLinesStillReachesTheSharedOnes()
        {
            // The behaviour this whole change exists for. Before it, two cowardly lines
            // *replaced* common's five rather than joining them, so writing two good
            // lines for an event left that skeleton with two - and twenty-six groups in
            // the shipped pack had quietly done exactly that.
            LinePack pack = Split(
                ["own one", "own two"],
                ["shared one", "shared two", "shared three", "shared four", "shared five"]);

            Assert.Equal(7, new HashSet<string>(Speak(pack, 2000, 20260908)).Count);
        }

        [Fact]
        public void ASmallPersonalityGroupStillDoesMostOfTheTalking()
        {
            // Merging must not wash the character out. Two own lines against five shared
            // ones is the case that would go worst if the bands were simply pooled: the
            // natural split hands 71% of the talking to common. The share puts it back.
            List<string> said = Speak(
                pack: Split(
                    ["own one", "own two"],
                    ["shared one", "shared two", "shared three", "shared four", "shared five"]),
                times: 4000,
                seed: 4711);

            double mine = said.FindAll(line => line.StartsWith("own", StringComparison.Ordinal)).Count
                / (double)said.Count;

            Assert.InRange(mine, 0.66, 0.74);
        }

        [Fact]
        public void AWellStockedPersonalityIsNotDraggedDownToTheShare()
        {
            // The other direction of the same trap, and why the rule takes a max rather
            // than the share flat. Eight own lines against two shared ones is naturally
            // 80% - forcing it to 70% would mean writing more lines made a personality
            // come through *less*, which is the thing we just finished fixing.
            //
            // On its own this does not prove what its name says: simply pooling the two
            // bands also gives 0.8 here. It is the pair that pins the rule down -
            // pooling scores 0.29 on the test above, and a flat 0.7 fails this one.
            List<string> said = Speak(
                pack: Split(
                    ["own 1", "own 2", "own 3", "own 4", "own 5", "own 6", "own 7", "own 8"],
                    ["shared one", "shared two"]),
                times: 4000,
                seed: 1301);

            double mine = said.FindAll(line => line.StartsWith("own", StringComparison.Ordinal)).Count
                / (double)said.Count;

            Assert.InRange(mine, 0.76, 0.84);
        }

        [Fact]
        public void LosingTheRollIsNotLosingTheLine()
        {
            // The band that goes second is still walked. Every own line here wants an
            // {ally} we have not got, so a roll that picks the personality must fall
            // through to the shared lines rather than going quiet - a token we cannot
            // fill should cost variety, never silence.
            List<string> said = Speak(
                pack: Split(
                    ["Stay close, {ally}.", "After you, {ally}."],
                    ["shared one", "shared two"]),
                times: 200,
                seed: 8);

            Assert.Equal(200, said.Count);
            Assert.All(said, line => Assert.StartsWith("shared", line, StringComparison.Ordinal));
        }

        [Fact]
        public void TheBroadcastLineRefStillReproducesTheLineAcrossBothBands()
        {
            // The contract that could actually have been broken by this. A listener folds
            // a bare number against the whole numbering and never learns which band we
            // drew from - so now that we draw from both, the ref has to keep landing on
            // the same words. It does because the numbering never changed: both bands
            // were always numbered in, they were merely unreachable.
            LinePack pack = Split(
                ["A {target}!", "Not another {target}."],
                ["{name} sees a {target}.", "Ach, {companion}, a {target}!", "Something moved."]);
            LineChooser chooser = new();
            Random random = new(2024);

            for (int i = 0; i < 500; i++)
            {
                Assert.True(chooser.TryChoose(
                    pack, Cowardly, ChatterEvent.Idle, Tokens(), random, out int lineRef, out string ours));

                Assert.True(pack.TryPick(Cowardly, ChatterEvent.Idle, lineRef, out string template));
                Assert.True(Tokens().TryRender(template, out string theirs));

                Assert.Equal(ours, theirs);
            }
        }

        [Fact]
        public void TheLineRefSurvivesBeingPackedIntoAnUtterance()
        {
            // The line ref does not travel on its own; it goes into 16 bits of a packed
            // int, and the Utterance constructor now refuses anything that would not
            // fit. A chooser producing values too large would work perfectly in single
            // player and throw the first time somebody joined.
            LinePack pack = Pack("a", "b", "c");
            LineChooser chooser = new();
            Random random = new(21);

            for (int i = 0; i < 200; i++)
            {
                Assert.True(chooser.TryChoose(
                    pack, Cowardly, ChatterEvent.Idle, Tokens(), random, out int lineRef, out _));

                Assert.InRange(lineRef, 0, Utterance.MaxLineRef);

                Utterance sent = new(1, ChatterEvent.Idle, lineRef, 0);
                Assert.True(Utterance.TryUnpack(sent.Pack(), 0, out Utterance got));
                Assert.Equal(lineRef, got.LineRef);
            }
        }

        [Fact]
        public void TheLineRefRoundTripsAtEveryGroupSizeNotJustConvenientOnes()
        {
            // LineRefFor is the one bit of genuinely new arithmetic here: given an index
            // and a count, produce a line ref that folds back to that index and still fits
            // in the 16 bits an Utterance allows. Testing it at one group size proves
            // very little - 65536 divides evenly by some counts and awkwardly by
            // others, and the interesting failures live at the edges.
            //
            // So: every size from 1 to 40, plus a few that divide badly, and for each
            // one check both halves of the contract.
            int[] sizes = [1, 2, 3, 7, 13, 17, 31, 33, 37, 40, 100, 999, 1000, 4095, 30000, 65535, 65536];

            foreach (int size in sizes)
            {
                string[] lines = new string[size];
                for (int i = 0; i < size; i++)
                {
                    lines[i] = "line " + i;
                }

                LinePack pack = Pack(lines);
                LineChooser chooser = new();
                Random random = new(size);

                for (int attempt = 0; attempt < 25; attempt++)
                {
                    Assert.True(chooser.TryChoose(
                        pack, Cowardly, ChatterEvent.Idle, Tokens(), random, out int lineRef, out string ours));

                    // Must survive the wire...
                    Assert.InRange(lineRef, 0, Utterance.MaxLineRef);

                    // ...and must reproduce the same words on a client with no state.
                    Assert.True(pack.TryPick(Cowardly, ChatterEvent.Idle, lineRef, out string template));
                    Assert.True(Tokens().TryRender(template, out string theirs));
                    Assert.Equal(ours, theirs);
                }
            }
        }

        [Fact]
        public void AnAbsurdlyLargeGroupDegradesRatherThanBreaking()
        {
            // More lines in one group than a 16-bit lineRef can address. No lineRef can
            // encode every index, so the promise that a remote client lands on the
            // *same* line genuinely cannot hold here - it gets a sensible line from
            // its own pack instead, which is the documented degradation.
            //
            // What must still hold is that we produce something, in range, without
            // throwing, because Utterance's constructor rejects a line ref above MaxLineRef.
            //
            // The size is deliberately far past the limit rather than one over it. My
            // first attempt at this test used MaxLineRef + 2, which reaches the branch
            // but only exercises the mask for a single index out of 65,537 - so
            // deleting the mask left every test green. At 200,000 roughly two thirds
            // of draws need it, and removing it fails here immediately.
            const int size = 200000;
            string[] lines = new string[size];
            for (int i = 0; i < size; i++)
            {
                lines[i] = "line " + i;
            }

            LinePack pack = Pack(lines);
            LineChooser chooser = new();
            Random random = new(5);

            for (int attempt = 0; attempt < 20; attempt++)
            {
                Assert.True(chooser.TryChoose(
                    pack, Cowardly, ChatterEvent.Idle, Tokens(), random, out int lineRef, out string line));

                Assert.InRange(lineRef, 0, Utterance.MaxLineRef);
                Assert.NotNull(line);

                // And it still packs, which is what would actually throw.
                Utterance sent = new(1, ChatterEvent.Idle, lineRef, 0);
                Assert.True(Utterance.TryUnpack(sent.Pack(), 0, out _));
            }
        }

        [Fact]
        public void TheLineRefIsNotJustTheIndex()
        {
            // We send one of the many line refs that fold to the chosen index rather than
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
                    pack, Cowardly, ChatterEvent.Idle, Tokens(), random, out int lineRef, out _));

                if (lineRef > 2)
                {
                    sawSomethingBigger = true;
                }
            }

            Assert.True(sawSomethingBigger);
        }
    }
}
