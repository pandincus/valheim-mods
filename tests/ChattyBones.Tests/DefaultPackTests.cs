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
        public void TheShippedPackParsesWithNothingToComplainAbout()
        {
            // This is the one test that covers the .yaml file itself rather than the
            // code around it. The same text is parsed at startup and written into the
            // player's config folder, so a mistake in it is a mistake in the mod - and
            // a warning in somebody's log about a file they have not touched is a
            // particularly poor first impression.
            Assert.True(
                PackReader.TryRead(DefaultPack.Yaml, out LinePack pack, out IReadOnlyList<string> problems),
                "The shipped pack does not parse.");

            Assert.Empty(problems);
            Assert.False(pack.IsEmpty);
        }

        [Fact]
        public void TheShippedPackColorsBadNewsAndGoodNewsDifferently()
        {
            // Three colors, and it matters that they are distinguishable rather than
            // what they are - the actual shades need eyeballing over grass and snow,
            // which no test is going to do.
            LinePack pack = DefaultPack.Build();

            string normal = pack.Colors.TagFor(ChatterEvent.Idle);
            string alarm = pack.Colors.TagFor(ChatterEvent.Died);
            string triumph = pack.Colors.TagFor(ChatterEvent.Killed);

            Assert.NotNull(normal);
            Assert.NotEqual(normal, alarm);
            Assert.NotEqual(normal, triumph);
            Assert.NotEqual(alarm, triumph);

            // The player being hurt is alarming for the same reason a skeleton dying
            // is, and the pack should not have to be read to know that.
            Assert.Equal(alarm, pack.Colors.TagFor(ChatterEvent.PlayerHurt));
            Assert.Equal(triumph, pack.Colors.TagFor(ChatterEvent.PlayerGotAKill));
        }

        [Fact]
        public void EveryLineInTheShippedPackIsDoubleQuoted()
        {
            // The pack header makes this the house rule, and it is the only rule that
            // catches the mistakes YAML *accepts*: an unquoted "You are # 1" is stored
            // as "You are" and nothing complains. Parsing cleanly is not enough here.
            string[] lines = DefaultPack.Yaml.Split('\n');

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].TrimEnd('\r');
                string trimmed = line.TrimStart();

                if (!trimmed.StartsWith("- ", StringComparison.Ordinal))
                {
                    continue;
                }

                string dialogue = trimmed[2..].Trim();

                Assert.True(
                    dialogue.StartsWith("\"", StringComparison.Ordinal)
                    && dialogue.EndsWith("\"", StringComparison.Ordinal),
                    "Line " + (i + 1) + " of the shipped pack is not double-quoted: " + trimmed);
            }
        }

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
        public void EveryPersonalitySoundsLikeItselfSomewhere(string personality)
        {
            // Asserting that a personality can react to every event proves nothing:
            // TryGetGroup falls back to the shared group, which EveryEventHasSomething
            // ToSay already covers, so it comes back true whatever the personality
            // contains - including nothing at all. What is worth checking is that each
            // one has lines of its own somewhere, or it is a name with no character.
            LinePack pack = DefaultPack.Build();
            int own = 0;

            foreach (ChatterEvent kind in Enum.GetValues(typeof(ChatterEvent)))
            {
                _ = pack.TryGetGroup(LinePack.SharedPersonality, kind, out IReadOnlyList<string> shared);

                if (pack.TryGetGroup(personality, kind, out IReadOnlyList<string> lines)
                    && !ReferenceEquals(lines, shared))
                {
                    own++;
                }
            }

            Assert.True(own > 0, personality + " has no lines of its own for any event.");
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
        /// still - the three events where one skeleton is reacting to another - though
        /// supplying it on Idle so they can rib each other is the best content idea
        /// anyone has had for this mod.
        ///
        /// CompanionKilled gets both, and is the only event that does: the line can
        /// name the killer and what it killed in the same breath.
        /// </remarks>
        private static LineTokens TokensFor(ChatterEvent kind)
        {
            bool hasTarget = kind is ChatterEvent.TargetAcquired
                or ChatterEvent.Killed
                or ChatterEvent.CompanionKilled
                or ChatterEvent.PlayerLandedABigHit
                or ChatterEvent.PlayerGotAKill;

            // Idle is in the list, and that is the point of it being there - a
            // skeleton with nobody to talk to falls back to its plain idle lines.
            bool hasCompanion = kind is ChatterEvent.CompanionHurt
                or ChatterEvent.CompanionKilled
                or ChatterEvent.CompanionDied
                or ChatterEvent.CompanionSummoned
                or ChatterEvent.Idle;

            // The events that come from a HitData, and so can describe the blow.
            bool hasHit = kind is ChatterEvent.Hurt
                or ChatterEvent.PlayerHurt
                or ChatterEvent.CompanionHurt
                or ChatterEvent.PlayerLandedABigHit;

            bool hasStatus = kind is ChatterEvent.Buffed or ChatterEvent.Afflicted;

            return new LineTokens(
                target: hasTarget ? "Greydwarf" : null,
                player: "Ragnar",
                name: "Botvid",
                companion: hasCompanion ? "Gunnar" : null,
                details: new LineDetails(
                    weapon: hasHit ? "Mistwalker" : null,
                    weaponType: hasHit ? "sword" : null,
                    damage: hasHit ? "slash" : null,
                    status: hasStatus ? "Burning" : null));
        }
    }
}
