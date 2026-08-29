using System;
using System.Collections.Generic;
using ChattyBones.Logic;

namespace ChattyBones.Tests
{
    /// <summary>
    /// Covers the pack the mod ships with, and the contract between it and the hooks.
    /// </summary>
    /// <remarks>
    /// The one worth reading is <see cref="EveryLineRendersWithTheTokensItsEventActuallyGets"/>.
    /// A pack and the code that fires it can disagree silently: put a {target} in an
    /// idle line and LineTokens refuses it, correctly, and the only symptom is a
    /// skeleton that never idles. The table in that test is the hooks' side of the
    /// bargain written down where a failing build can see it.
    /// </remarks>
    public class DefaultPackTests
    {
        [Fact]
        public void EveryEventHasSomethingToSay()
        {
            // The shared group is the backstop - a personality is allowed to have
            // nothing for an event, but then it falls back to here. If this ever fails,
            // some event is silent for everybody and looks exactly like a broken hook.
            LinePack pack = DefaultPack.Build();

            foreach (ChatterEvent kind in Enum.GetValues(typeof(ChatterEvent)))
            {
                Assert.True(
                    pack.TryGetGroup(LinePack.SharedPersonality, kind, out IReadOnlyList<string> lines),
                    "No shared lines for " + kind + ".");

                Assert.NotEmpty(lines);
            }
        }

        [Fact]
        public void TheFourPersonalitiesAreThereAndCommonIsNot()
        {
            LinePack pack = DefaultPack.Build();

            // Sorted, because a personality is stored as an index into this list and a
            // reordering would silently repoint every skeleton already in a save.
            Assert.Equal(["boastful", "cowardly", "dutiful", "veteran"], pack.Personalities);
        }

        [Theory]
        [InlineData("cowardly")]
        [InlineData("boastful")]
        [InlineData("dutiful")]
        [InlineData("veteran")]
        public void EveryPersonalityCanReactToEveryEvent(string personality)
        {
            LinePack pack = DefaultPack.Build();

            foreach (ChatterEvent kind in Enum.GetValues(typeof(ChatterEvent)))
            {
                Assert.True(
                    pack.TryGetGroup(personality, kind, out _),
                    personality + " has nothing for " + kind + ", not even by fallback.");
            }
        }

        [Fact]
        public void EveryLineRendersWithTheTokensItsEventActuallyGets()
        {
            LinePack pack = DefaultPack.Build();
            List<string> personalities = [.. pack.Personalities, LinePack.SharedPersonality];

            foreach (ChatterEvent kind in Enum.GetValues(typeof(ChatterEvent)))
            {
                LineTokens tokens = TokensFor(kind);

                foreach (string personality in personalities)
                {
                    if (!pack.TryGetGroup(personality, kind, out IReadOnlyList<string> lines))
                    {
                        continue;
                    }

                    foreach (string template in lines)
                    {
                        Assert.True(
                            tokens.TryRender(template, out _),
                            personality + "/" + kind + " cannot render \"" + template
                            + "\" - it wants a token that event is never given.");
                    }
                }
            }
        }

        /// <summary>What the hooks actually supply for each event.</summary>
        /// <returns>Tokens with a value for what is available and null for what is not.</returns>
        /// <param name="kind">The event being fired.</param>
        /// <remarks>
        /// Name and player are always known: one is the skeleton doing the talking and
        /// the other is whoever is playing.
        ///
        /// Target is only there when the event is about a creature, which rules out the
        /// ones that are about the skeleton itself or about you. Companion is narrower
        /// still - today only CompanionHurt passes one, though supplying it on Idle so
        /// they can rib each other is the best content idea anyone has had for this mod.
        /// </remarks>
        private static LineTokens TokensFor(ChatterEvent kind)
        {
            bool hasTarget = kind is ChatterEvent.TargetAcquired
                or ChatterEvent.Killed
                or ChatterEvent.PlayerLandedABigHit
                or ChatterEvent.PlayerGotAKill;

            bool hasCompanion = kind is ChatterEvent.CompanionHurt;

            return new LineTokens(
                target: hasTarget ? "Greydwarf" : null,
                player: "Ragnar",
                name: "Botvid",
                companion: hasCompanion ? "Gunnar" : null);
        }
    }
}
