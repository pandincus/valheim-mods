using System;
using System.Collections.Generic;
using System.Linq;
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
        public void NoLineGluesAnArticleToAWeaponSkill()
        {
            // "That is what a {weaponskill} is for." renders as "a Swords". No rendering
            // test can catch it: the fixture below supplies a placeholder, and the real
            // vocabulary is the game's own skill names, which live in Unity assets out of
            // reach of anything here.
            //
            // This has now been introduced twice, so it gets a rule rather than a fix.
            foreach (string raw in DefaultPack.Yaml.Split('\n'))
            {
                string line = raw.TrimEnd('\r');

                Assert.False(
                    Regex.IsMatch(line, @"\b[Aa]n? \{weapon(skill)?\}"),
                    "An article glued to a weapon token, which reads as \"a axe\": " + line.Trim());
            }
        }

        [Fact]
        public void NoWeaponTokenSitsOnABlowComingIn()
        {
            // Both weapon tokens are read from the attacker's hands, and a creature with
            // no real weapon reports "sword" - so a skeleton torn apart by a greydwarf
            // announced it had been "done in by the sword". That shipped in common/Died,
            // where no personality shadowed it, and survived every test here.
            //
            // These four events still promise both tokens and that is not a
            // contradiction: the grid says what the hooks can fill, this says where it
            // reads well. They are the only place in the pack where the two differ.
            string[] incoming = ["Hurt", "PlayerHurt", "CompanionHurt", "Died"];
            HashSet<string> seen = [];
            string current = null;

            foreach (string raw in DefaultPack.Yaml.Split('\n'))
            {
                string line = raw.TrimEnd('\r');

                if (Regex.IsMatch(line, @"^  [A-Za-z]+:\s*(#.*)?$"))
                {
                    current = null;
                    continue;
                }

                // The trailing comment is not decoration. Without it a "Died:  # blows
                // coming in" fails to match, current keeps whatever event came before,
                // and the offending line is quietly checked against the wrong one - so
                // the case this test was written for passes.
                Match key = Regex.Match(line, @"^    ([A-Za-z]+)(\[[^\]]*\])?:\s*(#.*)?$");
                if (key.Success)
                {
                    current = key.Groups[1].Value;
                    _ = seen.Add(current);
                    continue;
                }

                if (current == null || !line.TrimStart().StartsWith("- ", StringComparison.Ordinal))
                {
                    continue;
                }

                Assert.False(
                    Array.IndexOf(incoming, current) >= 0 && Regex.IsMatch(line, @"\{weapon(skill)?\}"),
                    "A weapon token on a blow coming in, which names whatever hit us and "
                    + "reads as \"done in by the sword\": " + current + " " + line.Trim());
            }

            // Scanning by hand means a reindent, or a shape this does not recognise,
            // would leave it walking the file and examining nothing while still passing.
            foreach (string kind in incoming)
            {
                Assert.Contains(kind, seen);
            }
        }

        [Fact]
        public void TheBareDamageTokenNeverTakesAnArticle()
        {
            // {damage} is one of eight damage-type names, and "Blunt" and "Pierce" are
            // not nouns you can put "the" in front of - so "Mind the {damage}, {player}."
            // reads in play as "Mind the Pierce". Fifteen lines shipped that way at once
            // and passed everything, because the fixture supplies a placeholder rather
            // than any of the real values.
            //
            // Attributive is the fix and stays legal, so "the {damage} damage" and "the
            // {damage} hit" are both fine and only the bare token is refused - hence any
            // following word rather than a list of the nouns I happened to think of.
            // Dialogue only: the header explains the rule by quoting the wrong version.
            foreach (string raw in DefaultPack.Yaml.Split('\n'))
            {
                string line = raw.TrimEnd('\r');

                if (!line.TrimStart().StartsWith("- ", StringComparison.Ordinal))
                {
                    continue;
                }

                Assert.False(
                    Regex.IsMatch(line, @"\b[Tt]he \{damage\}(?! \w)"),
                    "An article on the bare damage token, which reads as \"the Pierce\": "
                    + line.Trim());
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
            // the lookups fall back to the shared group, which EveryEventHasSomething
            // ToSay already covers, so they come back true whatever the personality
            // contains - including nothing at all. What is worth checking is that each
            // one has lines of its own somewhere, or it is a name with no character.
            LinePack pack = DefaultPack.Build();
            int own = 0;

            foreach (ChatterEvent kind in Enum.GetValues(typeof(ChatterEvent)))
            {
                if (pack.HasOwnLines(personality, kind))
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
            // A personality group *shadows* the shared one rather than adding to it -
            // selection picks one window and stays in it - so a group with one line in
            // it means that personality says that line and nothing else, for that
            // event, forever. The shared lines beside it become unreachable.
            //
            // Easy to write and impossible to see on the page, which is why it is a
            // rule: six of these went in at once, and the review caught them rather
            // than a test. The pack header's own advice is that a group of three
            // audibly cycles.
            //
            // Every group of the personality's is checked, not only the one in force
            // with no context resolved. A one-line Idle[biome=Swamp] shadows exactly
            // the same way and would otherwise be invisible here - which is the second
            // way to make this mistake that the context work introduced.
            LinePack pack = DefaultPack.Build();

            foreach (ChatterEvent kind in Enum.GetValues(typeof(ChatterEvent)))
            {
                if (!pack.HasOwnLines(personality, kind)
                    || !pack.TryGetSpace(personality, kind, out LineSpace space))
                {
                    continue;
                }

                foreach (LineSpace.Group group in space.Groups)
                {
                    if (!group.Personal || group.Length > 1)
                    {
                        continue;
                    }

                    string where = group.Context == null ? "" : "[" + group.Context + "]";

                    Assert.Fail(
                        personality + "/" + kind + where + " has one line of its own, so it will say \""
                        + space.All[group.Offset] + "\" every time and never reach anything beside it.");
                }
            }
        }

        /// <summary>How rare each context is, rarest first.</summary>
        /// <remarks>
        /// Two matching context groups are settled by file order and nothing warns about
        /// the loser, so the order the pack writes its groups in is a content decision
        /// rather than tidiness. This is the decision: rarest condition first.
        ///
        /// <c>home=yes</c> is the rarest thing a skeleton can be - it wants resting, a
        /// roof, a fire, a base and nothing hunting you all at once - so it should be
        /// heard whenever it holds. <c>biome</c> is next, because where you are says more
        /// than what hour it is, and <c>time</c> after it because a quarter of every day
        /// satisfies it.
        ///
        /// <c>home=no</c> is last, and it is the reason this ranks whole contexts rather
        /// than just their names. A value can invert a context's rarity: home resolves to
        /// one or the other on every real utterance, so <c>home=no</c> is true nearly
        /// always and a group tagged with it sits above its neighbours as a silent kill
        /// switch. Ranking by the name alone approved exactly that, in the same commit
        /// that added a test warning about it.
        /// </remarks>
        private static readonly string[] RarestFirst = ["home=yes", "biome", "time", "home=no"];

        /// <summary>Where a context sits in the ranking.</summary>
        /// <returns>Its position, or -1 for a context the rule has no opinion about.</returns>
        /// <param name="context">The context a group is tagged with, as "name=value".</param>
        /// <remarks>
        /// The whole context first, so a value that changes the answer can say so, then
        /// the bare name for the ones where every value is equally rare - any biome is as
        /// likely to be true as any other, and so is any time band.
        /// </remarks>
        private static int RankOf(string context)
        {
            int exact = Array.IndexOf(RarestFirst, context);

            return exact >= 0 ? exact : Array.IndexOf(RarestFirst, NameOf(context));
        }

        [Fact]
        public void ContextGroupsAreWrittenRarestFirst()
        {
            // The first version of this checked biome against everything else, and only
            // for Idle. Both limits were real: it passed while cowardly and dutiful had
            // their home groups below time, which silenced the home lines every night -
            // the quarter of the day they were written for - and it would not have looked
            // at a context group on any other event at all.
            LinePack pack = DefaultPack.Build();

            foreach (string personality in pack.Personalities)
            {
                foreach (ChatterEvent kind in Enum.GetValues(typeof(ChatterEvent)))
                {
                    if (!pack.TryGetSpace(personality, kind, out LineSpace space))
                    {
                        continue;
                    }

                    CheckOneEvent(personality, kind, space);
                }
            }
        }

        /// <summary>Walk one personality's groups for one event and check their order.</summary>
        /// <param name="personality">Whose lines these are.</param>
        /// <param name="kind">The event.</param>
        /// <param name="space">Its numbering, groups in the order the pack wrote them.</param>
        private static void CheckOneEvent(string personality, ChatterEvent kind, LineSpace space)
        {
            int previous = -1;
            string previousContext = null;

            foreach (LineSpace.Group group in space.Groups)
            {
                // Only the personality's own band. File order decides within a band -
                // TrySelect exhausts the personal groups before it touches the shared
                // ones - so a shared group below a personal one is not a shadowing.
                if (!group.Personal || group.Context == null)
                {
                    continue;
                }

                int rank = RankOf(group.Context);

                Assert.True(
                    rank >= 0,
                    personality + "/" + kind + "[" + group.Context + "] uses a context this rule"
                    + " has no opinion about. Add it to RarestFirst and decide where it goes.");

                Assert.True(
                    rank >= previous,
                    personality + "/" + kind + " writes [" + group.Context + "] below ["
                    + previousContext + "], so it is unreachable whenever both match.");

                previous = rank;
                previousContext = group.Context;
            }
        }

        /// <summary>The name half of a "name=value" context.</summary>
        /// <returns>The part before the =.</returns>
        /// <param name="context">The context a group is tagged with.</param>
        private static string NameOf(string context)
        {
            int equals = context.IndexOf('=');
            return equals < 0 ? context : context[..equals];
        }

        [Fact]
        public void NoSharedContextGroupIsWrittenWhereNobodyCanReachIt()
        {
            // Personality beats context, so a personality with plain lines of its own
            // for an event never falls through to a shared group tagged for where it is
            // standing. If every personality has plain lines for that event, a shared
            // context group is unreachable - it reads perfectly and nothing can ever
            // say it.
            //
            // Eight lines shipped that way before a review caught it, which is why the
            // rule is here rather than in somebody's head. The atmosphere belongs in
            // the personalities; common is the boring baseline.
            LinePack pack = DefaultPack.Build();

            if (!pack.TryGetSpace(LinePack.SharedPersonality, ChatterEvent.Idle, out _))
            {
                return;
            }

            foreach (ChatterEvent kind in Enum.GetValues(typeof(ChatterEvent)))
            {
                if (!pack.TryGetSpace(LinePack.SharedPersonality, kind, out LineSpace shared))
                {
                    continue;
                }

                bool anyoneFallsThrough = false;

                for (int i = 0; !anyoneFallsThrough && i < pack.Personalities.Count; i++)
                {
                    anyoneFallsThrough = !pack.HasOwnLines(pack.Personalities[i], kind);
                }

                if (anyoneFallsThrough)
                {
                    continue;
                }

                foreach (LineSpace.Group group in shared.Groups)
                {
                    Assert.True(
                        group.Context == null,
                        "common/" + kind + "[" + group.Context + "] can never be reached: every "
                        + "personality has its own plain " + kind + " lines, and those win. "
                        + "Put these lines in the personalities instead.");
                }
            }
        }

        [Fact]
        public void EveryLineRendersWithTheTokensItsEventActuallyGets()
        {
            LinePack pack = DefaultPack.Build();
            List<string> personalities = [.. pack.Personalities, LinePack.SharedPersonality];

            foreach (ChatterEvent kind in Enum.GetValues(typeof(ChatterEvent)))
            {
                LineTokens tokens = AnythingFor(kind);

                foreach (string personality in personalities)
                {
                    // The whole numbering rather than the group in force, because with
                    // no context resolved the window is only the plain group - which
                    // would leave every Idle[biome=...] line unchecked, and a token an
                    // event never supplies makes a line silently unsayable.
                    if (!pack.TryGetSpace(personality, kind, out LineSpace space))
                    {
                        continue;
                    }

                    foreach (string template in space.All)
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
        public void ThePackHeadersTimeValuesMatchTheBandsTheCodeProduces()
        {
            // The header's vocabulary row is a hand-copy of TimeOfDay.All - the fourth
            // one, and the only one a test can reach that is not derived from the list.
            // A band renamed in code and not here would leave the file telling authors to
            // write a value that is refused at load, which reads as the mod being broken
            // rather than the docs being stale.
            Match row = Regex.Match(DefaultPack.Yaml, @"^#   time=\s+(.*)$", RegexOptions.Multiline);

            Assert.True(row.Success, "the header has no 'time=' vocabulary row any more");

            string[] listed = row.Groups[1].Value.Split(
                [' '], StringSplitOptions.RemoveEmptyEntries);

            Assert.Equal(TimeOfDay.All.OrderBy(band => band, StringComparer.Ordinal), listed.OrderBy(band => band, StringComparer.Ordinal));
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
                    @"^#   (\w+)\s+.*?([T.])  ([C.])  ([A.])  ([W.])  ([K.])  ([D.])  ([S.])  ([B.])  ([I.])  ([L.])$");

                if (match.Success)
                {
                    // The header is aligned to the character, and a row that drifts
                    // still matches the pattern - the leading .*? absorbs it. So the
                    // width is asserted rather than assumed, or the one thing a reader
                    // checks this grid for by eye can rot unnoticed.
                    Assert.Equal(83, match.Value.Length);

                    rows[match.Groups[1].Value] = string.Concat(
                        match.Groups[2].Value, match.Groups[3].Value, match.Groups[4].Value,
                        match.Groups[5].Value, match.Groups[6].Value, match.Groups[7].Value,
                        match.Groups[8].Value, match.Groups[9].Value, match.Groups[10].Value,
                        match.Groups[11].Value);
                }
            }

            Assert.Equal(Enum.GetValues(typeof(ChatterEvent)).Length, rows.Count);

            foreach (ChatterEvent kind in Enum.GetValues(typeof(ChatterEvent)))
            {
                LineTokens tokens = TokensFor(kind);

                string expected = string.Concat(
                    Mark('T', tokens.TryRender("{target}", out _)),
                    Mark('C', tokens.TryRender("{companion}", out _)),
                    Mark('A', tokens.TryRender("{ally}", out _)),
                    Mark('W', tokens.TryRender("{weapon}", out _)),
                    Mark('K', tokens.TryRender("{weaponskill}", out _)),
                    Mark('D', tokens.TryRender("{damage}", out _)),
                    Mark('S', tokens.TryRender("{status}", out _)),
                    Mark('B', tokens.TryRender("{biome}", out _)),
                    Mark('I', tokens.TryRender("{item}", out _)),
                    Mark('L', tokens.TryRender("{skill}", out _)));

                Assert.True(rows.ContainsKey(kind.ToString()), "No row in the pack header for " + kind + ".");
                Assert.Equal(expected, rows[kind.ToString()]);
            }
        }

        /// <summary>Everything a line in this event could possibly be given.</summary>
        /// <returns>The promised tokens, plus the two that any event can fill.</returns>
        /// <param name="kind">The event being fired.</param>
        /// <remarks>
        /// A different question from <see cref="TokensFor"/>, and the two must not be
        /// merged. That one asks what the event *guarantees*, which is what the pack
        /// header's grid states. This asks what a line could ever be handed, which is
        /// what decides whether a line in the shipped pack is sayable at all -
        /// {companion} and {ally} are filled from whoever is standing about on every
        /// event, so a line using either is fine anywhere and the grid still should not
        /// claim it.
        /// </remarks>
        private static LineTokens AnythingFor(ChatterEvent kind)
        {
            LineTokens promised = TokensFor(kind);

            return new LineTokens(
                target: promised.Target,
                player: promised.Player,
                name: promised.Name,
                companion: promised.Companion ?? "Gunnar",
                ally: promised.Ally ?? "Sigrid",
                details: promised.Details);
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
                ally: Fill(promised, TokenSet.Ally, "Sigrid"),
                details: new LineDetails(
                    weapon: Fill(promised, TokenSet.Weapon, "Mistwalker"),
                    weaponSkill: Fill(promised, TokenSet.WeaponSkill, "sword"),
                    damage: Fill(promised, TokenSet.Damage, "slash"),
                    status: Fill(promised, TokenSet.Status, "Burning"),
                    biome: Fill(promised, TokenSet.Biome, "Black Forest"),
                    item: Fill(promised, TokenSet.Item, "Carrot Soup"),
                    skill: Fill(promised, TokenSet.Skill, "Blocking")));
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
