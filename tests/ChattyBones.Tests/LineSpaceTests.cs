using System;
using System.Collections.Generic;
using ChattyBones.Logic;

namespace ChattyBones.Tests
{
    /// <summary>
    /// Covers the numbering that lets one client pick a line by where it is standing
    /// and every other client land on the same line without asking.
    /// </summary>
    /// <remarks>
    /// The property worth protecting is the round trip, and it is the last test here:
    /// a speaker resolves a context, chooses, and sends a number; a listener that
    /// resolves nothing at all folds that number and gets the same words. Everything
    /// above it is the machinery that has to hold for that to be true.
    /// </remarks>
    public class LineSpaceTests
    {
        private const string Cowardly = "cowardly";
        private const string Swamp = "biome=Swamp";
        private const string Meadows = "biome=Meadows";

        private static readonly string[] InSwamp = [Swamp];
        private static readonly string[] InMeadows = [Meadows];

        private static EventKey Key(string text)
        {
            Assert.True(EventKey.TryParse(text, out EventKey key, out string problem), problem);
            return key;
        }

        /// <summary>A pack with lines in every band, so precedence has something to choose between.</summary>
        private static LinePack FourBands()
        {
            return new LinePack.Builder()
                .Add(Cowardly, Key("Idle[biome=Swamp]"), "own swamp")
                .Add(Cowardly, Key("Idle"), "own plain a", "own plain b")
                .Add(LinePack.SharedPersonality, Key("Idle[biome=Swamp]"), "shared swamp")
                .Add(LinePack.SharedPersonality, Key("Idle"), "shared plain")
                .Build();
        }

        [Fact]
        public void TheNumberingHoldsEveryLineTheyCouldReach()
        {
            LinePack pack = FourBands();

            Assert.True(pack.TryGetSpace(Cowardly, ChatterEvent.Idle, out LineSpace space));

            // The personality's groups first, then the shared ones - and every line is
            // in there whether or not anybody is standing anywhere near a swamp.
            Assert.Equal(
                ["own swamp", "own plain a", "own plain b", "shared swamp", "shared plain"],
                space.All);
        }

        [Fact]
        public void TheNumberingFollowsThePackRatherThanTheAlphabet()
        {
            // Sorting was the first answer here. File order is the one that lets an
            // author decide a tie by moving a group up the page.
            LinePack pack = new LinePack.Builder()
                .Add(Cowardly, Key("Idle[biome=Swamp]"), "swamp")
                .Add(Cowardly, Key("Idle[biome=Meadows]"), "meadows")
                .Build();

            Assert.True(pack.TryGetSpace(Cowardly, ChatterEvent.Idle, out LineSpace space));
            Assert.Equal(["swamp", "meadows"], space.All);
        }

        [Fact]
        public void AContextGroupBeatsThePlainOne()
        {
            Assert.True(FourBands().TryGetGroup(Cowardly, ChatterEvent.Idle, out IReadOnlyList<string> lines, InSwamp));
            Assert.Equal(["own swamp"], lines);
        }

        [Fact]
        public void ThePersonalityBeatsTheSharedContextGroup()
        {
            // Standing in the Meadows with no Meadows lines of its own, a cowardly
            // skeleton uses its own plain lines rather than the shared swamp ones.
            // Better people at the cost of slightly worse places, which is the trade
            // the four personalities exist to win.
            Assert.True(FourBands().TryGetGroup(Cowardly, ChatterEvent.Idle, out IReadOnlyList<string> lines, InMeadows));
            Assert.Equal(["own plain a", "own plain b"], lines);
        }

        [Fact]
        public void TheSharedContextGroupIsReachedWhenThePersonalityHasNothingPlain()
        {
            LinePack pack = new LinePack.Builder()
                .Add(Cowardly, Key("Idle[biome=Meadows]"), "own meadows")
                .Add(LinePack.SharedPersonality, Key("Idle[biome=Swamp]"), "shared swamp")
                .Add(LinePack.SharedPersonality, Key("Idle"), "shared plain")
                .Build();

            Assert.True(pack.TryGetGroup(Cowardly, ChatterEvent.Idle, out IReadOnlyList<string> lines, InSwamp));
            Assert.Equal(["shared swamp"], lines);
        }

        [Fact]
        public void NoContextAtAllReachesThePlainGroup()
        {
            // What a skeleton gets while a zone is still loading, which is every portal
            // trip and every login.
            Assert.True(FourBands().TryGetGroup(Cowardly, ChatterEvent.Idle, out IReadOnlyList<string> lines, null));
            Assert.Equal(["own plain a", "own plain b"], lines);
        }

        [Fact]
        public void ThePlainGroupWinsWhereverThePackWroteIt()
        {
            // File order settles ties between context groups. It does not override
            // specificity: a plain group written first still loses to a matching
            // context group written after it.
            LinePack pack = new LinePack.Builder()
                .Add(Cowardly, Key("Idle"), "plain")
                .Add(Cowardly, Key("Idle[biome=Swamp]"), "swamp")
                .Build();

            Assert.True(pack.TryGetGroup(Cowardly, ChatterEvent.Idle, out IReadOnlyList<string> lines, InSwamp));
            Assert.Equal(["swamp"], lines);
        }

        [Fact]
        public void APersonalityThatIsTheSharedOneIsNotCountedTwice()
        {
            LinePack pack = new LinePack.Builder()
                .Add(LinePack.SharedPersonality, Key("Idle"), "a", "b")
                .Build();

            Assert.True(pack.TryGetSpace(LinePack.SharedPersonality, ChatterEvent.Idle, out LineSpace space));
            Assert.Equal(["a", "b"], space.All);
        }

        [Fact]
        public void AnUnknownPersonalityCountsAgainstTheSharedLines()
        {
            // The listener has to agree with the speaker about the numbering, and it
            // only knows the personality - so a name neither of them has lines for has
            // to land in the same place on both.
            LinePack pack = FourBands();

            Assert.True(pack.TryGetSpace("nobody", ChatterEvent.Idle, out LineSpace space));
            Assert.Equal(["shared swamp", "shared plain"], space.All);
        }

        [Fact]
        public void ThePackReportsTheContextsItUses()
        {
            IReadOnlyList<string> contexts = FourBands().Contexts();

            Assert.Contains(Swamp, contexts);
            Assert.Single(contexts);
        }

        [Fact]
        public void AOneLineContextGroupIsVisibleAsAGroup()
        {
            // The rule against a personality shadowing everything with a single line
            // has to see groups nobody is currently standing in, or a one-line swamp
            // group slips past it.
            Assert.True(FourBands().TryGetSpace(Cowardly, ChatterEvent.Idle, out LineSpace space));

            int shadowing = 0;

            foreach (LineSpace.Group group in space.Groups)
            {
                if (group.Personal && group.Length == 1)
                {
                    shadowing++;
                }
            }

            Assert.Equal(1, shadowing);
        }

        [Fact]
        public void ASpeakerInAContextAndAListenerInNoneReachTheSameLine()
        {
            // The whole point, in one test. The speaker resolves a context and picks a
            // line; the listener is handed nothing but the number and never asks where
            // anybody is - and both say the same words.
            LinePack pack = FourBands();
            _ = new LineChooser();
            LineTokens tokens = new(target: null, player: "Ragnar", name: "Rattles", companion: null);

            foreach (string[] where in new[] { InSwamp, InMeadows, null })
            {
                // A fresh chooser each time: its one-deep memory is about not repeating
                // itself, and here it would just be noise between the two halves.
                LineChooser chooser = new();

                Assert.True(chooser.TryChoose(
                    pack, Cowardly, ChatterEvent.Idle, tokens, new Random(7),
                    out int lineRef, out string said, where));

                Assert.True(pack.TryPick(Cowardly, ChatterEvent.Idle, lineRef, out string heard));
                Assert.Equal(said, heard);
            }
        }

        [Fact]
        public void ASentLineRefSurvivesAnyNumberOfFoldings()
        {
            // A line ref is deliberately not the bare index - see LineRefFor - so the
            // property that matters is the remainder, not the value.
            LinePack pack = FourBands();
            _ = new LineChooser();
            LineTokens tokens = new(target: null, player: "Ragnar", name: "Rattles", companion: null);

            Assert.True(pack.TryGetSpace(Cowardly, ChatterEvent.Idle, out LineSpace space));

            for (int seed = 0; seed < 50; seed++)
            {
                LineChooser chooser = new();

                Assert.True(chooser.TryChoose(
                    pack, Cowardly, ChatterEvent.Idle, tokens, new Random(seed),
                    out int lineRef, out string said, InSwamp));

                Assert.True(lineRef >= 0);
                Assert.Equal(said, space.All[lineRef % space.Count]);
            }
        }
    }
}
