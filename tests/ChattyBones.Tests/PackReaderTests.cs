using System.Collections.Generic;
using ChattyBones.Logic;

namespace ChattyBones.Tests
{
    /// <summary>
    /// Covers reading a pack file, and mostly covers reading a broken one.
    /// </summary>
    /// <remarks>
    /// The pack is the one file this mod actively invites players to edit by hand,
    /// in a format where a stray space is a syntax error. So the tests worth reading
    /// here are the ones about what a mistake costs: a bad event name costs that
    /// event, a bad colour costs that colour, and only a file with nothing usable in
    /// it at all costs the pack.
    ///
    /// Line numbers are asserted on rather than just "something was reported",
    /// because a complaint with no position in it is barely better than silence when
    /// you are staring at two hundred lines of YAML.
    /// </remarks>
    public class PackReaderTests
    {
        [Fact]
        public void ReadsPersonalitiesEventsAndLines()
        {
            const string yaml = """
                lines:
                  cowardly:
                    Idle:
                      - Can we go home?
                      - It's very open out here.
                    Summoned:
                      - Do I have to?
                  boastful:
                    Idle:
                      - They'll sing about me, you know.
                """;

            Assert.True(PackReader.TryRead(yaml, out LinePack pack, out IReadOnlyList<string> problems));
            Assert.Empty(problems);
            Assert.Equal(["boastful", "cowardly"], pack.Personalities);

            Assert.True(pack.TryGetGroup("cowardly", ChatterEvent.Idle, out IReadOnlyList<string> idle));
            Assert.Equal(["Can we go home?", "It's very open out here."], idle);
        }

        [Fact]
        public void APackOfNothingButCommonLinesIsPerfectlyValid()
        {
            // No personalities at all is a reasonable pack to write - every skeleton
            // falls back to the shared group and they all sound alike. It must not be
            // mistaken for an empty pack, which is the one thing we refuse.
            const string yaml = """
                lines:
                  common:
                    Idle:
                      - Hmm.
                """;

            Assert.True(PackReader.TryRead(yaml, out LinePack pack, out _));
            Assert.Empty(pack.Personalities);
            Assert.False(pack.IsEmpty);
            Assert.True(pack.TryGetGroup("anyone at all", ChatterEvent.Idle, out _));
        }

        [Fact]
        public void AnUnknownEventCostsThatEventAndNothingElse()
        {
            const string yaml = """
                lines:
                  cowardly:
                    Kiled:
                      - Did I do that?
                    Idle:
                      - Can we go home?
                """;

            Assert.True(PackReader.TryRead(yaml, out LinePack pack, out IReadOnlyList<string> problems));

            string complaint = Assert.Single(problems);
            Assert.Contains("line 3", complaint);
            Assert.Contains("Kiled", complaint);

            // The good half of the file survived, which is the whole policy.
            Assert.True(pack.TryGetGroup("cowardly", ChatterEvent.Idle, out _));
        }

        [Fact]
        public void EventNamesAreCaseSensitive()
        {
            // Deliberate, and the same rule the tokens follow. Summoned and
            // CompanionSummoned are nearly the same word, and loose matching is only
            // cheap while nothing is close to anything else.
            const string yaml = """
                lines:
                  common:
                    summoned:
                      - Up we get.
                    Idle:
                      - Hmm.
                """;

            Assert.True(PackReader.TryRead(yaml, out LinePack pack, out IReadOnlyList<string> problems));
            Assert.Contains("summoned", Assert.Single(problems));
            Assert.False(pack.TryGetGroup("common", ChatterEvent.Summoned, out _));
        }

        [Fact]
        public void ANumberIsNotAnEvent()
        {
            // Enum.TryParse takes "3" and hands back the third event, so without the
            // IsDefined check in TryEvent this would silently fill in Buffed. Worth a
            // test because the failure is invisible: the pack works, just not the way
            // it reads.
            const string yaml = """
                lines:
                  common:
                    3:
                      - Ooh, that's the stuff.
                    Idle:
                      - Hmm.
                """;

            Assert.True(PackReader.TryRead(yaml, out LinePack pack, out IReadOnlyList<string> problems));
            Assert.Contains("line 3", Assert.Single(problems));
            Assert.False(pack.TryGetGroup("common", ChatterEvent.Buffed, out _));
        }

        [Fact]
        public void BrokenIndentationIsRefusedWithAPosition()
        {
            const string yaml = """
                lines:
                  common:
                    Idle:
                      - Hmm.
                     - Anyone else cold?
                """;

            Assert.False(PackReader.TryRead(yaml, out LinePack pack, out IReadOnlyList<string> problems));
            Assert.Null(pack);

            // The position has to come from the exception's marks. YamlDotNet's own
            // message is "While parsing a block mapping, did not find expected key."
            // and says nothing about where, which on its own is no help at all.
            Assert.Contains("line 5", Assert.Single(problems));
        }

        [Fact]
        public void AKeyWithNothingBeforeItsColonIsRefusedRatherThanThrown()
        {
            // The one that got away. YamlStream.Load throws a bare ArgumentException
            // for this rather than a YamlException, so a narrow catch let it escape
            // TryRead, out of PackFile, out of Chatter.Init and out of Plugin.Awake -
            // which happens before Harmony patches anything, so one stray colon left
            // the whole mod loaded and permanently silent.
            const string yaml = """
                lines:
                  :
                    Idle:
                      - Hmm.
                """;

            Assert.False(PackReader.TryRead(yaml, out LinePack pack, out IReadOnlyList<string> problems));
            Assert.Null(pack);
            Assert.NotEmpty(problems);
        }

        [Fact]
        public void EverythingAfterADocumentSeparatorIsReported()
        {
            // Only the first document is read, so the rest is lost either way. What
            // matters is that it is not lost silently - the pack still works, which is
            // exactly why nobody would think to look.
            const string yaml = """
                lines:
                  common:
                    Idle:
                      - Hmm.
                ---
                lines:
                  cowardly:
                    Idle:
                      - Can we go home?
                """;

            Assert.True(PackReader.TryRead(yaml, out LinePack pack, out IReadOnlyList<string> problems));
            Assert.Contains("---", Assert.Single(problems));
            Assert.Empty(pack.Personalities);
        }

        [Fact]
        public void TheSameNameTwiceIsRefusedWithAPosition()
        {
            // Costs the whole file, which is worse than the rest of this class's
            // policy and is not ours to soften - YamlDotNet reads the document before
            // handing us anything. The position had better be right, then.
            const string yaml = """
                lines:
                  common:
                    Idle:
                      - Hmm.
                    Idle:
                      - Anyone else cold?
                """;

            Assert.False(PackReader.TryRead(yaml, out LinePack pack, out IReadOnlyList<string> problems));
            Assert.Null(pack);
            Assert.Contains("line 5", Assert.Single(problems));
        }

        [Fact]
        public void APersonalityHasToBeAPlainName()
        {
            // A skeleton stores a position in the sorted list of personalities, so a
            // junk entry does not merely sit there being useless - it sorts among the
            // real ones and shifts everybody already summoned.
            const string yaml = """
                lines:
                  ? [a, b]
                  :
                    Idle:
                      - Hmm.
                  common:
                    Idle:
                      - Anyone else cold?
                """;

            Assert.True(PackReader.TryRead(yaml, out LinePack pack, out IReadOnlyList<string> problems));
            Assert.NotEmpty(problems);
            Assert.Empty(pack.Personalities);
        }

        [Fact]
        public void AnEventWithNoLinesUnderItIsReported()
        {
            // The state a file is in halfway through being edited, so it wants to say
            // something better than nothing.
            const string yaml = """
                lines:
                  common:
                    Idle:
                    Summoned:
                      - Up we get.
                """;

            Assert.True(PackReader.TryRead(yaml, out LinePack pack, out IReadOnlyList<string> problems));
            Assert.Contains("line 3", Assert.Single(problems));
            Assert.True(pack.TryGetGroup("common", ChatterEvent.Summoned, out _));
        }

        [Theory]
        [InlineData("{player}! Watch it!", '{')]
        [InlineData("'Tis but a scratch!", '\'')]
        [InlineData("*rattles bones*", '*')]
        public void ALineOpeningWithYamlSyntaxIsNamedAndTheFixGiven(string dialogue, char opener)
        {
            // The most likely mistake in the whole format, and YamlDotNet's own message
            // for it talks about block mappings and expected keys - accurate, and no
            // help at all to somebody writing jokes for a skeleton.
            string yaml = "lines:\n  common:\n    Idle:\n      - " + dialogue + "\n";

            Assert.False(PackReader.TryRead(yaml, out _, out IReadOnlyList<string> problems));

            string hint = Assert.Single(problems, p => p.Contains("double quotes"));
            Assert.Contains("line 4", hint);
            Assert.Contains("'" + opener + "'", hint);
        }

        [Fact]
        public void QuotingThatLineIsActuallyTheFix()
        {
            // The hint has to be true, or it is worse than saying nothing. Same line,
            // wrapped, and it must now parse and come out verbatim.
            const string yaml = """
                lines:
                  common:
                    Idle:
                      - "{player}! Watch it!"
                """;

            Assert.True(PackReader.TryRead(yaml, out LinePack pack, out IReadOnlyList<string> problems));
            Assert.Empty(problems);

            Assert.True(pack.TryGetGroup("common", ChatterEvent.Idle, out IReadOnlyList<string> lines));
            Assert.Equal(["{player}! Watch it!"], lines);
        }

        [Fact]
        public void NoQuotingHintIsOfferedForAFileThatBrokeSomeOtherWay()
        {
            // The scan is blunt on purpose, but it should still stay quiet when nothing
            // in the file looks like this - otherwise it is noise on every failure.
            const string yaml = """
                lines:
                  common:
                    Idle:
                      - "Hmm."
                      - "Anyone else cold?"
                    Idle:
                      - "Nice weather for it."
                """;

            Assert.False(PackReader.TryRead(yaml, out _, out IReadOnlyList<string> problems));
            Assert.DoesNotContain(problems, p => p.Contains("double quotes"));
        }

        [Fact]
        public void AnEmptyFileIsRefused()
        {
            Assert.False(PackReader.TryRead("", out LinePack pack, out IReadOnlyList<string> problems));
            Assert.Null(pack);
            Assert.NotEmpty(problems);
        }

        [Fact]
        public void AFileWithNoLinesInItIsRefused()
        {
            // Parses cleanly and is still useless. Handing this back would leave the
            // whole squad mute with nothing in the log to explain it.
            const string yaml = """
                colours:
                  palette:
                    normal: "#E8E4DC"
                """;

            Assert.False(PackReader.TryRead(yaml, out LinePack pack, out IReadOnlyList<string> problems));
            Assert.Null(pack);
            Assert.NotEmpty(problems);
        }

        [Fact]
        public void AnUnknownSectionIsReportedAndTheRestIsRead()
        {
            const string yaml = """
                personalities:
                  - cowardly
                lines:
                  common:
                    Idle:
                      - Hmm.
                """;

            Assert.True(PackReader.TryRead(yaml, out LinePack pack, out IReadOnlyList<string> problems));
            Assert.Contains("personalities", Assert.Single(problems));
            Assert.True(pack.TryGetGroup("common", ChatterEvent.Idle, out _));
        }

        [Fact]
        public void LinesHaveToBeAList()
        {
            const string yaml = """
                lines:
                  common:
                    Idle: Hmm.
                    Summoned:
                      - Up we get.
                """;

            Assert.True(PackReader.TryRead(yaml, out LinePack pack, out IReadOnlyList<string> problems));

            // The line number, not just the event name - "Idle" appears in the message
            // only because it interpolates the group it is about, so asserting on it
            // alone would pass for a complaint about something else entirely.
            Assert.Contains("line 3", Assert.Single(problems));
            Assert.True(pack.TryGetGroup("common", ChatterEvent.Summoned, out _));
        }

        [Fact]
        public void ColoursReachTheEventsThatNameThem()
        {
            const string yaml = """
                colours:
                  palette:
                    normal: "#E8E4DC"
                    alarm: "#F0A9A0"
                  events:
                    Died: alarm
                lines:
                  common:
                    Idle:
                      - Hmm.
                """;

            Assert.True(PackReader.TryRead(yaml, out LinePack pack, out IReadOnlyList<string> problems));
            Assert.Empty(problems);

            Assert.Equal("<color=#F0A9A0>", pack.Colours.TagFor(ChatterEvent.Died));

            // Everything the events list does not mention takes "normal".
            Assert.Equal("<color=#E8E4DC>", pack.Colours.TagFor(ChatterEvent.Idle));
        }

        [Fact]
        public void ThePaletteCanBeWrittenAfterTheEventsThatUseIt()
        {
            // The reader puts the events half aside and resolves it once the whole
            // section has been read, because a pack author is entitled to write these
            // two in whichever order reads better to them.
            const string yaml = """
                colours:
                  events:
                    Died: alarm
                  palette:
                    alarm: "#F0A9A0"
                lines:
                  common:
                    Idle:
                      - Hmm.
                """;

            Assert.True(PackReader.TryRead(yaml, out LinePack pack, out IReadOnlyList<string> problems));
            Assert.Empty(problems);
            Assert.Equal("<color=#F0A9A0>", pack.Colours.TagFor(ChatterEvent.Died));
        }

        [Fact]
        public void NoColoursAtAllMeansTheGameDrawsItsUsualWhite()
        {
            const string yaml = """
                lines:
                  common:
                    Idle:
                      - Hmm.
                """;

            Assert.True(PackReader.TryRead(yaml, out LinePack pack, out _));
            Assert.Null(pack.Colours.TagFor(ChatterEvent.Idle));
        }

        [Fact]
        public void ABadHexCodeCostsThatColourAndIsReported()
        {
            const string yaml = """
                colours:
                  palette:
                    normal: "#E8E4DC"
                    alarm: reddish
                  events:
                    Died: alarm
                lines:
                  common:
                    Idle:
                      - Hmm.
                """;

            Assert.True(PackReader.TryRead(yaml, out LinePack pack, out IReadOnlyList<string> problems));

            // Two complaints, and both are useful: the colour is not a hex code, and
            // as a result the event asking for it cannot be given one.
            Assert.Equal(2, problems.Count);
            Assert.Contains("line 4", problems[0]);
            Assert.Contains("line 6", problems[1]);

            // Died falls back to normal rather than to nothing.
            Assert.Equal("<color=#E8E4DC>", pack.Colours.TagFor(ChatterEvent.Died));
        }

        [Fact]
        public void AColourNameNothingDefinesIsReported()
        {
            const string yaml = """
                colours:
                  palette:
                    normal: "#E8E4DC"
                  events:
                    Died: alrm
                lines:
                  common:
                    Idle:
                      - Hmm.
                """;

            Assert.True(PackReader.TryRead(yaml, out LinePack pack, out IReadOnlyList<string> problems));
            Assert.Contains("alrm", Assert.Single(problems));
            Assert.Equal("<color=#E8E4DC>", pack.Colours.TagFor(ChatterEvent.Died));
        }

        [Fact]
        public void BlankLinesAreSkippedRatherThanSaid()
        {
            // A hand-edited list picks up an empty entry sooner or later, and a
            // skeleton silently opening its mouth to say nothing is a worse outcome
            // than one line quietly not existing.
            const string yaml = """
                lines:
                  common:
                    Idle:
                      - Hmm.
                      - ""
                      - "   "
                """;

            Assert.True(PackReader.TryRead(yaml, out LinePack pack, out _));
            Assert.True(pack.TryGetGroup("common", ChatterEvent.Idle, out IReadOnlyList<string> lines));
            Assert.Equal(["Hmm."], lines);
        }

        [Fact]
        public void TheRootHasToBeSections()
        {
            Assert.False(PackReader.TryRead("- cowardly\n- boastful\n", out LinePack pack, out IReadOnlyList<string> problems));
            Assert.Null(pack);
            Assert.NotEmpty(problems);
        }

        [Fact]
        public void CommentsAndTokensSurviveTheTrip()
        {
            // The pack we ship is mostly comments, and the lines in it are mostly
            // braces. Neither should need any thought from a reader of this file, but
            // both would be a very silly thing to find broken in game.
            const string yaml = """
                # Everything a skeleton can say.
                lines:
                  common:
                    PlayerHurt:
                      # Quoted because a line starting with { is a mapping otherwise.
                      - "{player}! Watch it!"
                    CompanionKilled:
                      - "{companion} got the {target}."
                """;

            Assert.True(PackReader.TryRead(yaml, out LinePack pack, out IReadOnlyList<string> problems));
            Assert.Empty(problems);

            Assert.True(pack.TryGetGroup("common", ChatterEvent.PlayerHurt, out IReadOnlyList<string> hurt));
            Assert.Equal(["{player}! Watch it!"], hurt);

            Assert.True(pack.TryGetGroup("common", ChatterEvent.CompanionKilled, out IReadOnlyList<string> killed));
            Assert.Equal(["{companion} got the {target}."], killed);
        }
    }
}
