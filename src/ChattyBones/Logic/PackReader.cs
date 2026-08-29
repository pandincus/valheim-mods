using System;
using System.Collections.Generic;
using System.IO;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace ChattyBones.Logic
{
    /// <summary>
    /// Turns the text of a pack file into a <see cref="LinePack"/>.
    /// </summary>
    /// <remarks>
    /// Nothing throws, and a mistake costs only itself: an unknown event name costs
    /// that event, a bad hex code costs that color. Only a file with no lines at all
    /// comes back false. The exception is getting the document to parse - a duplicate
    /// key takes the whole pack, and that is YamlDotNet's call rather than ours.
    ///
    /// Read as nodes rather than deserialized into classes: it costs a page of
    /// walking and buys node.Start.Line, so "line 84: 'Kiled' is not an event" is
    /// sayable.
    /// </remarks>
    internal static class PackReader
    {
        /// <summary>The palette entry every event uses unless it names another.</summary>
        private const string DefaultColorName = "normal";

        /// <summary>Read a pack file.</summary>
        /// <returns>
        /// False when nothing usable came out - the document would not parse, or it
        /// parsed and contained no lines. The caller should keep whatever pack it is
        /// already using.
        /// </returns>
        /// <param name="yaml">The file's contents.</param>
        /// <param name="pack">The pack, or null when we return false.</param>
        /// <param name="problems">
        /// Everything worth telling the player about, each prefixed with a line
        /// number. Always set, and often non-empty even on success.
        /// </param>
        internal static bool TryRead(string yaml, out LinePack pack, out IReadOnlyList<string> problems)
        {
            List<string> found = [];
            problems = found;
            pack = null;

            YamlMappingNode root;

            try
            {
                // An empty YamlStream, not a list. YamlStream is enumerable over its
                // documents, so the style rules ask for a collection expression here.
                YamlStream stream = [];
                stream.Load(new StringReader(yaml ?? string.Empty));

                if (stream.Documents.Count == 0)
                {
                    found.Add("the pack file is empty.");
                    return false;
                }

                if (stream.Documents.Count > 1)
                {
                    // A "---" starts a second document, and we only ever read the
                    // first. Silently dropping half a pack is the worst answer here,
                    // because everything still works and nothing says why.
                    found.Add(At(stream.Documents[1].RootNode)
                        + "everything from the '---' onwards is being ignored. A pack is one document, "
                        + "so use a comment to separate sections rather than '---'.");
                }

                root = stream.Documents[0].RootNode as YamlMappingNode;
            }
            catch (Exception e)
            {
                // Not just YamlException. Leave a mapping key off but keep its colon -
                // "  :" alone on a line, which is what deleting a personality name
                // leaves behind - and YamlNode.ParseNode throws a bare
                // ArgumentException. Escaping here reaches Plugin.Awake before Harmony
                // has patched anything, so one stray colon left the whole mod inert.
                found.Add(e is YamlException y
                    ? "line " + y.Start.Line + ": " + y.Message
                    : "the pack could not be read: " + e.Message
                        + " A key with nothing before its colon will do this.");

                Suggest(yaml, found);
                return false;
            }

            if (root == null)
            {
                found.Add("the pack should be a list of sections, starting with 'lines:'.");
                return false;
            }

            LinePack.Builder builder = new();

            foreach (KeyValuePair<YamlNode, YamlNode> section in root)
            {
                string name = NameOf(section.Key);

                switch (name)
                {
                    case "lines":
                        ReadLines(section.Value, builder, found);
                        break;

                    case "colors":
                        ReadColors(section.Value, builder, found);
                        break;

                    default:
                        found.Add(At(section.Key) + "'" + name + "' is not a section ChattyBones knows about. "
                            + "The two it reads are 'lines' and 'colors'.");
                        break;
                }
            }

            LinePack built = builder.Build();

            if (built.IsEmpty)
            {
                found.Add("the pack has no lines in it, so nobody would have anything to say.");
                return false;
            }

            pack = built;
            return true;
        }

        /// <summary>Read the lines section: personality, then event, then the lines.</summary>
        /// <param name="node">Whatever followed 'lines:'.</param>
        /// <param name="builder">The pack being assembled.</param>
        /// <param name="problems">Where to record anything wrong.</param>
        private static void ReadLines(YamlNode node, LinePack.Builder builder, List<string> problems)
        {
            if (node is not YamlMappingNode byPersonality)
            {
                problems.Add(At(node) + "'lines' should be a list of personalities, each with events under it.");
                return;
            }

            foreach (KeyValuePair<YamlNode, YamlNode> personality in byPersonality)
            {
                // A personality has to be a plain name, because a skeleton stores its
                // position in the sorted list of them. Letting a list or a mapping in
                // as a personality called "[ a, b ]" would sort somewhere among the
                // real ones and shift every skeleton already summoned.
                if (personality.Key is not YamlScalarNode)
                {
                    problems.Add(At(personality.Key) + "A personality has to be a plain name.");
                    continue;
                }

                string who = NameOf(personality.Key);

                // Fails silently otherwise: "Common" is accepted as an ordinary
                // personality, and every event that was relying on the shared group
                // for its lines simply goes quiet.
                if (who != LinePack.SharedPersonality
                    && string.Equals(who, LinePack.SharedPersonality, StringComparison.OrdinalIgnoreCase))
                {
                    problems.Add(At(personality.Key) + "'" + who + "' is being read as an ordinary personality. "
                        + "The shared group everyone falls back on is spelled '" + LinePack.SharedPersonality
                        + "', in lower case.");
                }

                if (personality.Value is not YamlMappingNode byEvent)
                {
                    problems.Add(At(personality.Value) + "'" + who + "' should be a list of events, each with lines under it.");
                    continue;
                }

                foreach (KeyValuePair<YamlNode, YamlNode> group in byEvent)
                {
                    string what = NameOf(group.Key);

                    if (!TryEvent(what, out ChatterEvent kind))
                    {
                        problems.Add(At(group.Key) + "'" + what + "' is not an event. "
                            + "The fifteen there are is listed in the comments at the top of the pack.");
                        continue;
                    }

                    if (group.Value is not YamlSequenceNode lines)
                    {
                        problems.Add(At(group.Value) + who + "/" + what + " should be a list of lines, each starting with a dash.");
                        continue;
                    }

                    foreach (YamlNode line in lines)
                    {
                        if (line is YamlScalarNode scalar)
                        {
                            _ = builder.Add(who, kind, scalar.Value);
                        }
                        else
                        {
                            problems.Add(At(line) + "A line in " + who + "/" + what + " is not a piece of text.");
                        }
                    }
                }
            }
        }

        /// <summary>Read the colors section: a named palette, and which events use which.</summary>
        /// <param name="node">Whatever followed 'colors:'.</param>
        /// <param name="builder">The pack being assembled.</param>
        /// <param name="problems">Where to record anything wrong.</param>
        /// <remarks>
        /// The events half is put aside and read after the loop rather than during it,
        /// because it refers to palette entries by name and a pack author is entitled
        /// to write the two sections in either order.
        /// </remarks>
        private static void ReadColors(YamlNode node, LinePack.Builder builder, List<string> problems)
        {
            if (node is not YamlMappingNode map)
            {
                problems.Add(At(node) + "'colors' should hold 'palette' and 'events'.");
                return;
            }

            Dictionary<string, string> palette = [];
            YamlNode events = null;

            foreach (KeyValuePair<YamlNode, YamlNode> entry in map)
            {
                string name = NameOf(entry.Key);

                switch (name)
                {
                    case "palette":
                        ReadPalette(entry.Value, palette, problems);
                        break;

                    case "events":
                        events = entry.Value;
                        break;

                    default:
                        problems.Add(At(entry.Key) + "'" + name + "' does not belong under 'colors'. "
                            + "The two that do are 'palette' and 'events'.");
                        break;
                }
            }

            if (palette.TryGetValue(DefaultColorName, out string fallback))
            {
                _ = builder.SetDefaultColor(fallback);
            }

            if (events != null)
            {
                ReadEventColors(events, palette, builder, problems);
            }
        }

        /// <summary>Read the named colors themselves.</summary>
        /// <param name="node">Whatever followed 'palette:'.</param>
        /// <param name="palette">Filled in with name to hex code.</param>
        /// <param name="problems">Where to record anything wrong.</param>
        private static void ReadPalette(YamlNode node, Dictionary<string, string> palette, List<string> problems)
        {
            if (node is not YamlMappingNode map)
            {
                problems.Add(At(node) + "'palette' should be a list of names with a hex code against each.");
                return;
            }

            foreach (KeyValuePair<YamlNode, YamlNode> entry in map)
            {
                string name = NameOf(entry.Key);
                string hex = NameOf(entry.Value);

                if (!SpeechFormat.TryColorTag(hex, out _))
                {
                    problems.Add(At(entry.Value) + "'" + hex + "' is not a hex color like \"#F0A9A0\", so '"
                        + name + "' is being ignored.");
                    continue;
                }

                palette[name] = hex;
            }
        }

        /// <summary>Read which events depart from the default color.</summary>
        /// <param name="node">Whatever followed 'events:'.</param>
        /// <param name="palette">The colors available, by name.</param>
        /// <param name="builder">The pack being assembled.</param>
        /// <param name="problems">Where to record anything wrong.</param>
        private static void ReadEventColors(
            YamlNode node,
            Dictionary<string, string> palette,
            LinePack.Builder builder,
            List<string> problems)
        {
            if (node is not YamlMappingNode map)
            {
                problems.Add(At(node) + "'events' under 'colors' should be a list of events with a color name against each.");
                return;
            }

            foreach (KeyValuePair<YamlNode, YamlNode> entry in map)
            {
                string what = NameOf(entry.Key);
                string color = NameOf(entry.Value);

                if (!TryEvent(what, out ChatterEvent kind))
                {
                    problems.Add(At(entry.Key) + "'" + what + "' is not an event, so it cannot be given a color.");
                    continue;
                }

                if (!palette.TryGetValue(color, out string hex))
                {
                    problems.Add(At(entry.Value) + "'" + color + "' is not one of the colors in the palette, so "
                        + what + " is being left the usual color.");
                    continue;
                }

                _ = builder.SetColor(kind, hex);
            }
        }

        /// <summary>Add a hint about the mistakes that break a pack most often.</summary>
        /// <param name="yaml">The file we could not read.</param>
        /// <param name="problems">Where to add the hint, if there is one to give.</param>
        /// <remarks>
        /// Only reached once the document has already failed, so a wrong guess costs
        /// one log line on a file that was broken anyway. That is what lets it be a
        /// blunt scan - but only up to a point, because the hint is appended after the
        /// parser's own complaint and therefore reads as the more specific diagnosis.
        /// So each check below only fires on something that is a mistake in every
        /// context, and legal-but-unusual openers like a comment or a block scalar are
        /// deliberately not flagged.
        /// </remarks>
        private static void Suggest(string yaml, List<string> problems)
        {
            if (string.IsNullOrEmpty(yaml))
            {
                return;
            }

            string[] lines = yaml.Split('\n');

            // One hint of each kind. The first is almost always the cause, and a wall
            // of near-identical suggestions is its own kind of unhelpful.
            bool saidQuote = false;
            bool saidTab = false;

            for (int i = 0; i < lines.Length && !(saidQuote && saidTab); i++)
            {
                string line = lines[i];

                if (!saidTab && IndentedWithATab(line))
                {
                    // YamlDotNet reports a tab at the line the enclosing mapping opened
                    // on, which for a pack is line 1 wherever the tab actually is.
                    problems.Add("line " + (i + 1) + ": this line is indented with a tab. "
                        + "YAML only accepts spaces, and the position it reports for this is not the tab's.");

                    saidTab = true;
                }

                if (!saidQuote && TryDialogueMistake(line, out string advice))
                {
                    problems.Add("line " + (i + 1) + ": " + advice);
                    saidQuote = true;
                }
            }
        }

        /// <summary>Is this line indented with a tab?</summary>
        /// <returns>True when the leading whitespace contains one.</returns>
        /// <param name="line">One raw line of the file.</param>
        private static bool IndentedWithATab(string line)
        {
            for (int i = 0; i < line.Length; i++)
            {
                if (line[i] == '\t')
                {
                    return true;
                }

                if (line[i] != ' ')
                {
                    return false;
                }
            }

            return false;
        }

        /// <summary>Does this line hand a piece of dialogue to YAML as syntax?</summary>
        /// <returns>True when there is something specific to say about it.</returns>
        /// <param name="line">One raw line of the file.</param>
        /// <param name="advice">What to tell the player, when we return true.</param>
        /// <remarks>
        /// Kept to the characters that cannot be anything but a mistake in a line of
        /// dialogue. A leading <c>#</c>, <c>&amp;</c>, <c>&gt;</c> or <c>|</c> is left
        /// alone even though each can break a file, because each is also legal - and
        /// commenting a line out mid-edit is exactly what somebody is doing at the
        /// moment their file breaks for an unrelated reason. Telling them to quote
        /// their comment would be worse than saying nothing.
        ///
        /// A closed single-quoted line is skipped for the same reason. An unclosed one
        /// - <c>'Tis but a scratch!</c> - is not, and is the case worth catching.
        /// </remarks>
        private static bool TryDialogueMistake(string line, out string advice)
        {
            advice = null;

            int i = 0;
            while (i < line.Length && (line[i] == ' ' || line[i] == '\t'))
            {
                i++;
            }

            if (i >= line.Length || line[i] != '-')
            {
                return false;
            }

            i++;
            while (i < line.Length && (line[i] == ' ' || line[i] == '\t'))
            {
                i++;
            }

            if (i >= line.Length)
            {
                return false;
            }

            // Index arithmetic rather than Substring and a [^1] index. The style rules
            // rewrite both into forms that want System.Index, which the net10 test
            // project has and net472 does not - and both projects compile this file.
            int last = line.Length - 1;
            while (last > i && (line[last] == '\r' || line[last] == ' '))
            {
                last--;
            }

            char opener = line[i];

            // Inside double quotes a backslash starts an escape, so the one style the
            // pack tells everybody to use is also the only one where a backslash bites:
            // "\o/" refuses the file, and "\_" quietly becomes a non-breaking space.
            if (opener == '"' && line.IndexOf('\\', i) >= 0)
            {
                advice = "a backslash inside \"double quotes\" starts an escape rather than "
                    + "being a backslash. Write it twice, or leave the line unquoted.";

                return true;
            }

            if (opener == '\'' && last > i && line[last] == '\'')
            {
                return false;
            }

            if (opener is not ('{' or '[' or '\'' or '*' or '%' or '@' or '`'))
            {
                return false;
            }

            advice = "this line starts with '" + opener
                + "', which YAML reads as the start of something rather than as words. "
                + "Wrap the line in \"double quotes\" and it will be read as text.";

            return true;
        }

        /// <summary>Match a name in the file against one of our events.</summary>
        /// <returns>True when the name is exactly one of them.</returns>
        /// <param name="name">Whatever the file said.</param>
        /// <param name="kind">The event, when we return true.</param>
        /// <remarks>
        /// The IsDefined call is doing real work. Enum.TryParse on its own accepts a
        /// number as well as a name, so a pack with "3:" under events would quietly
        /// color Buffed rather than being told that 3 is not an event.
        ///
        /// Case sensitive, like the tokens are, so "summoned" is reported rather than
        /// accepted. Loose matching is cheap only while nothing else is nearly the
        /// same word, and Summoned and CompanionSummoned already exist.
        /// </remarks>
        private static bool TryEvent(string name, out ChatterEvent kind)
        {
            kind = default;

            return !string.IsNullOrEmpty(name)
                && Enum.IsDefined(typeof(ChatterEvent), name)
                && Enum.TryParse(name, out kind);
        }

        /// <summary>The text of a node, for nodes we expect to be plain text.</summary>
        /// <returns>The scalar's value, or a readable stand-in for anything else.</returns>
        /// <param name="node">The node to name.</param>
        private static string NameOf(YamlNode node)
        {
            return node is YamlScalarNode scalar ? scalar.Value : node?.ToString() ?? string.Empty;
        }

        /// <summary>Where in the file something is, to prefix a complaint with.</summary>
        /// <returns>Something like "line 84: ".</returns>
        /// <param name="node">The node being complained about.</param>
        private static string At(YamlNode node)
        {
            return "line " + node.Start.Line + ": ";
        }
    }
}
