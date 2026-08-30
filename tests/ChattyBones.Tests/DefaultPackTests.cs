using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
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
        public void NoLineGluesAnArticleToAWeaponType()
        {
            // "That is what a {weapontype} is for." reads fine until somebody swings
            // an axe, and "a fists" is worse. No rendering test can catch it: the
            // fixture below renders {weapontype} as "sword", which is the one word in
            // the vocabulary under which every such line happens to work - and the
            // vocabulary itself lives in the Unity half, out of reach.
            //
            // This has now been introduced twice, so it gets a rule rather than a fix.
            foreach (string raw in DefaultPack.Yaml.Split('\n'))
            {
                string line = raw.TrimEnd('\r');

                Assert.False(
                    Regex.IsMatch(line, @"\b[Aa]n? \{weapon(type)?\}"),
                    "An article glued to a weapon token, which reads as \"a axe\": " + line.Trim());
            }
        }

        [Fact]
        public void NoLineDropsAStatusTokenIntoTheMiddleOfASentence()
        {
            // The game names a status effect for its status bar, so {status} always
            // arrives capitalized - "Wet", "Burning", "Tarred". Sentence-initial that
            // is exactly right, and "Hey, my bones are Wet." looks like a typo. Seen in
            // a live session on three shipped lines, which is why it is a rule: 5d is a
            // whole pass of writing new ones, and this reads fine on the page.
            //
            // Comment lines are skipped deliberately - the pack header demonstrates the
            // mistake in order to warn about it.
            foreach (string raw in DefaultPack.Yaml.Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                if (line.TrimStart().StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                Assert.False(
                    Regex.IsMatch(line, @"[\p{L}\d,]\s*\{status\}"),
                    "A status token mid-sentence, which reads as \"my bones are Wet\": " + line.Trim());
            }
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

        [Theory]
        [InlineData("cowardly")]
        [InlineData("boastful")]
        [InlineData("dutiful")]
        [InlineData("veteran")]
        public void NoPersonalityOverridesTheSharedLinesWithASingleOne(string personality)
        {
            // A personality group *replaces* the shared one rather than adding to it -
            // TryGetGroup returns one or the other and never merges - so a group with
            // one line in it means that personality says that line and nothing else,
            // for that event, forever. The shared lines beside it become unreachable.
            //
            // Easy to write and impossible to see on the page, which is why it is a
            // rule: six of these went in at once, and the review caught them rather
            // than a test. The pack header's own advice is that a group of three
            // audibly cycles.
            LinePack pack = DefaultPack.Build();

            foreach (ChatterEvent kind in Enum.GetValues(typeof(ChatterEvent)))
            {
                _ = pack.TryGetGroup(LinePack.SharedPersonality, kind, out IReadOnlyList<string> shared);

                if (!pack.TryGetGroup(personality, kind, out IReadOnlyList<string> lines)
                    || ReferenceEquals(lines, shared))
                {
                    continue;
                }

                Assert.True(
                    lines.Count > 1,
                    personality + "/" + kind + " has one line of its own, so it will say \""
                    + lines[0] + "\" every time and never reach the " + (shared?.Count ?? 0)
                    + " shared ones.");
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

        [Fact]
        public void ThePackHeadersTokenGridMatchesWhatTheEventsActuallySupply()
        {
            // A row promising a token the event never supplies means lines that
            // silently never fire, and the log says nothing about it. This pins the
            // grid against EventTokens; the call sites are the hop neither this nor
            // TokensFor can see, and only the cb_tokens report watches those.
            Dictionary<string, string> rows = [];

            foreach (string raw in DefaultPack.Yaml.Split('\n'))
            {
                Match match = Regex.Match(
                    raw.TrimEnd('\r'),
                    @"^#   (\w+)\s+.*?([T.])  ([C.])  ([W.])  ([K.])  ([D.])  ([S.])  ([B.])$");

                if (match.Success)
                {
                    rows[match.Groups[1].Value] = string.Concat(
                        match.Groups[2].Value, match.Groups[3].Value, match.Groups[4].Value,
                        match.Groups[5].Value, match.Groups[6].Value, match.Groups[7].Value,
                        match.Groups[8].Value);
                }
            }

            Assert.Equal(Enum.GetValues(typeof(ChatterEvent)).Length, rows.Count);

            foreach (ChatterEvent kind in Enum.GetValues(typeof(ChatterEvent)))
            {
                LineTokens tokens = TokensFor(kind);

                string expected = string.Concat(
                    Mark('T', tokens.TryRender("{target}", out _)),
                    Mark('C', tokens.TryRender("{companion}", out _)),
                    Mark('W', tokens.TryRender("{weapon}", out _)),
                    Mark('K', tokens.TryRender("{weapontype}", out _)),
                    Mark('D', tokens.TryRender("{damage}", out _)),
                    Mark('S', tokens.TryRender("{status}", out _)),
                    Mark('B', tokens.TryRender("{biome}", out _)));

                Assert.True(rows.ContainsKey(kind.ToString()), "No row in the pack header for " + kind + ".");
                Assert.Equal(expected, rows[kind.ToString()]);
            }
        }

        /// <summary>One cell of the grid.</summary>
        /// <returns>The token's letter when it is supplied, a dot when it is not.</returns>
        /// <param name="letter">The column's letter.</param>
        /// <param name="supplied">Whether the event fills that token in.</param>
        private static string Mark(char letter, bool supplied)
        {
            return supplied ? letter.ToString() : ".";
        }

        /// <summary>What the hooks actually supply for each event.</summary>
        /// <returns>Tokens with a value for what is available and null for what is not.</returns>
        /// <param name="kind">The event being fired.</param>
        /// <remarks>
        /// Name and player are always known: one is the skeleton doing the talking and
        /// the other is whoever is playing. Everything else comes from
        /// <see cref="EventTokens.PromisedFor"/> rather than being restated here, so
        /// the table has one home and these tests check the pack against it instead of
        /// against a copy of it.
        /// </remarks>
        private static LineTokens TokensFor(ChatterEvent kind)
        {
            TokenSet promised = EventTokens.PromisedFor(kind);

            return new LineTokens(
                target: Fill(promised, TokenSet.Target, "Greydwarf"),
                player: "Ragnar",
                name: "Botvid",
                companion: Fill(promised, TokenSet.Companion, "Gunnar"),
                details: new LineDetails(
                    weapon: Fill(promised, TokenSet.Weapon, "Mistwalker"),
                    weaponType: Fill(promised, TokenSet.WeaponType, "sword"),
                    damage: Fill(promised, TokenSet.Damage, "slash"),
                    status: Fill(promised, TokenSet.Status, "Burning"),
                    biome: Fill(promised, TokenSet.Biome, "Black Forest")));
        }

        /// <summary>A fixture value when the event promises that token, null when it does not.</summary>
        /// <returns>The value, or null.</returns>
        /// <param name="promised">What the event undertakes to supply.</param>
        /// <param name="one">The token being asked about.</param>
        /// <param name="value">What to use when it is supplied.</param>
        private static string Fill(TokenSet promised, TokenSet one, string value)
        {
            return (promised & one) == 0 ? null : value;
        }
    }
}
