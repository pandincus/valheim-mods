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
    /// Whitespace-sensitive files that people edit by hand go wrong, so the whole
    /// design here is about going wrong usefully. Nothing throws, and every complaint
    /// carries the line it is about - which is the one thing that makes a YAML error
    /// actionable instead of infuriating.
    ///
    /// Once the document parses, a mistake costs only itself: an unknown event name
    /// costs that event, a bad hex code costs that colour, and the rest of the file
    /// is still a working pack. Only a file that yields no lines at all comes back
    /// false, on the reasoning that a squad missing two idle lines is much better
    /// than a silent one.
    ///
    /// Getting the document to parse at all is where that stops being true, and it
    /// is not ours to soften: YamlDotNet reads the whole file before handing us
    /// anything, so a duplicate key or a broken indent takes the pack with it. Those
    /// at least come with a position, and the caller keeps whatever pack it already
    /// had.
    ///
    /// I read the document as nodes rather than deserialising into classes. It costs
    /// a page of walking, and it buys the position of every entry - so "line 84:
    /// 'Kiled' is not an event" is sayable, where a deserialiser would have given us
    /// a missing property and no idea where.
    /// </remarks>
    internal static class PackReader
    {
        /// <summary>The palette entry every event uses unless it names another.</summary>
        internal const string DefaultColourName = "normal";

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
                    found.Add("The pack file is empty.");
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
            catch (YamlException e)
            {
                // The marks are on the exception but not in its message, which reads
                // "While parsing a block mapping, did not find expected key." on its
                // own - true, and useless without somewhere to look.
                found.Add("line " + e.Start.Line + ": " + e.Message);
                SuggestQuoting(yaml, found);
                return false;
            }
            catch (Exception e)
            {
                // YamlStream.Load does not confine itself to YamlException. Leave a
                // mapping key off and keep its colon - "  :" on a line of its own,
                // which is exactly what deleting a personality name leaves behind -
                // and YamlNode.ParseNode throws a bare ArgumentException instead.
                //
                // Anything escaping this method reaches Plugin.Awake, which is before
                // Harmony has patched anything, so one stray colon would leave the
                // whole mod loaded and inert for the session.
                found.Add("the pack could not be read: " + e.Message
                    + " A key with nothing before its colon will do this.");
                SuggestQuoting(yaml, found);
                return false;
            }

            if (root == null)
            {
                found.Add("The pack should be a list of sections, starting with 'lines:'.");
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

                    case "colours":
                        ReadColours(section.Value, builder, found);
                        break;

                    default:
                        found.Add(At(section.Key) + "'" + name + "' is not a section ChattyBones knows about. "
                            + "The two it reads are 'lines' and 'colours'.");
                        break;
                }
            }

            LinePack built = builder.Build();

            if (built.IsEmpty)
            {
                found.Add("The pack has no lines in it, so nobody would have anything to say.");
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

        /// <summary>Read the colours section: a named palette, and which events use which.</summary>
        /// <param name="node">Whatever followed 'colours:'.</param>
        /// <param name="builder">The pack being assembled.</param>
        /// <param name="problems">Where to record anything wrong.</param>
        /// <remarks>
        /// The events half is put aside and read after the loop rather than during it,
        /// because it refers to palette entries by name and a pack author is entitled
        /// to write the two sections in either order.
        /// </remarks>
        private static void ReadColours(YamlNode node, LinePack.Builder builder, List<string> problems)
        {
            if (node is not YamlMappingNode map)
            {
                problems.Add(At(node) + "'colours' should hold 'palette' and 'events'.");
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
                        problems.Add(At(entry.Key) + "'" + name + "' does not belong under 'colours'. "
                            + "The two that do are 'palette' and 'events'.");
                        break;
                }
            }

            if (palette.TryGetValue(DefaultColourName, out string fallback))
            {
                _ = builder.SetDefaultColour(fallback);
            }

            if (events != null)
            {
                ReadEventColours(events, palette, builder, problems);
            }
        }

        /// <summary>Read the named colours themselves.</summary>
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

                if (!SpeechFormat.TryColourTag(hex, out _))
                {
                    problems.Add(At(entry.Value) + "'" + hex + "' is not a hex colour like \"#F0A9A0\", so '"
                        + name + "' is being ignored.");
                    continue;
                }

                palette[name] = hex;
            }
        }

        /// <summary>Read which events depart from the default colour.</summary>
        /// <param name="node">Whatever followed 'events:'.</param>
        /// <param name="palette">The colours available, by name.</param>
        /// <param name="builder">The pack being assembled.</param>
        /// <param name="problems">Where to record anything wrong.</param>
        private static void ReadEventColours(
            YamlNode node,
            Dictionary<string, string> palette,
            LinePack.Builder builder,
            List<string> problems)
        {
            if (node is not YamlMappingNode map)
            {
                problems.Add(At(node) + "'events' under 'colours' should be a list of events with a colour name against each.");
                return;
            }

            foreach (KeyValuePair<YamlNode, YamlNode> entry in map)
            {
                string what = NameOf(entry.Key);
                string colour = NameOf(entry.Value);

                if (!TryEvent(what, out ChatterEvent kind))
                {
                    problems.Add(At(entry.Key) + "'" + what + "' is not an event, so it cannot be given a colour.");
                    continue;
                }

                if (!palette.TryGetValue(colour, out string hex))
                {
                    problems.Add(At(entry.Value) + "'" + colour + "' is not one of the colours in the palette, so "
                        + what + " is being left the usual colour.");
                    continue;
                }

                _ = builder.SetColour(kind, hex);
            }
        }

        /// <summary>Point at the mistake that breaks a pack more often than any other.</summary>
        /// <param name="yaml">The file we could not read.</param>
        /// <param name="problems">Where to add the hint, if there is one to give.</param>
        /// <remarks>
        /// Writing <c>- {player}! Watch it!</c> is the single most natural way to open
        /// a line and one of the fastest ways to break the file, because YAML reads a
        /// leading brace as the start of an object. The parser's own message is about
        /// block mappings and expected keys, which is accurate and no help at all to
        /// somebody writing jokes for a skeleton.
        ///
        /// Only reached when the document has already failed, so a wrong guess costs a
        /// line of log on a file that was broken anyway. That is what lets it be a
        /// blunt scan rather than a careful one - it does not have to be sure, only
        /// useful.
        /// </remarks>
        private static void SuggestQuoting(string yaml, List<string> problems)
        {
            if (string.IsNullOrEmpty(yaml))
            {
                return;
            }

            string[] lines = yaml.Split('\n');

            for (int i = 0; i < lines.Length; i++)
            {
                if (TryRiskyOpener(lines[i], out char opener))
                {
                    problems.Add("line " + (i + 1) + ": this line starts with '" + opener
                        + "', which YAML reads as the start of something rather than as words. "
                        + "Wrap the line in \"double quotes\" and it will be read as text.");

                    // One is enough. The first is almost always the cause, and a wall of
                    // near-identical hints is its own kind of unhelpful.
                    return;
                }
            }
        }

        /// <summary>Does this line hand a piece of dialogue to YAML as syntax?</summary>
        /// <returns>True when the line is a list item opening with a character YAML treats specially.</returns>
        /// <param name="line">One raw line of the file.</param>
        /// <param name="opener">The offending character, when we return true.</param>
        /// <remarks>
        /// A leading double quote is the one opener that is fine, because that is the
        /// fix. Everything listed here means something to YAML in that position:
        /// braces and brackets open collections, an apostrophe opens a quoted string,
        /// a star is a reference and an ampersand names one, angle and pipe open
        /// multi-line blocks, and a hash is a comment.
        /// </remarks>
        private static bool TryRiskyOpener(string line, out char opener)
        {
            opener = default;

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
            while (i < line.Length && line[i] == ' ')
            {
                i++;
            }

            if (i >= line.Length)
            {
                return false;
            }

            opener = line[i];

            return opener is '{' or '[' or '\'' or '*' or '&' or '>' or '|' or '%' or '@' or '`' or '#';
        }

        /// <summary>Match a name in the file against one of our events.</summary>
        /// <returns>True when the name is exactly one of them.</returns>
        /// <param name="name">Whatever the file said.</param>
        /// <param name="kind">The event, when we return true.</param>
        /// <remarks>
        /// The IsDefined call is doing real work. Enum.TryParse on its own accepts a
        /// number as well as a name, so a pack with "3:" under events would quietly
        /// colour Buffed rather than being told that 3 is not an event.
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
