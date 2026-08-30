using System;
using System.Collections.Generic;

namespace ChattyBones.Logic
{
    /// <summary>
    /// Everything the skeletons are able to say, and the rule for choosing one.
    /// </summary>
    /// <remarks>
    /// Lines are grouped by personality and then by event, so a cowardly skeleton
    /// and a boastful one react to the same greydwarf quite differently.
    ///
    /// No state, and <see cref="TryPick"/> is a pure function. The client owning a
    /// skeleton broadcasts a line ref; everyone else folds it against their own
    /// pack. Same pack, same line, with nobody comparing notes.
    ///
    /// <see cref="Builder"/> is the only way to make one, so a pack in memory always
    /// has no empty groups - which <see cref="TryPick"/> would divide by - and a
    /// personality list in a stable order.
    ///
    /// Not knowing about YAML is also deliberate. Reading the file is somebody
    /// else's job, which keeps this testable without a file on disk and keeps the
    /// question of what a malformed pack does out of here entirely.
    /// </remarks>
    internal sealed class LinePack
    {
        /// <summary>
        /// The personality consulted when a skeleton's own has nothing to say.
        /// </summary>
        /// <remarks>
        /// Without this, writing a pack means filling in every event for every
        /// personality before anything works, and forgetting one corner gives you a
        /// skeleton that is mysteriously silent in one situation. With it, an author
        /// can write the lines they had a good idea for and let the rest fall back.
        ///
        /// It is an ordinary personality with a reserved name, so a pack that never
        /// mentions "common" simply never falls back, and one that puts all its lines
        /// there gives every skeleton the same voice. Both seem like reasonable
        /// things to want.
        /// </remarks>
        internal const string SharedPersonality = "common";

        private readonly Dictionary<string, Dictionary<ChatterEvent, string[]>> _byPersonality;

        /// <summary>Wrap what the builder assembled.</summary>
        /// <param name="byPersonality">Personality to event to lines. Every group non-empty.</param>
        /// <param name="personalities">The personality types, sorted, without the shared fallback.</param>
        /// <param name="colors">What color each event is drawn in.</param>
        /// <remarks>
        /// Private, and reachable only from <see cref="Builder.Build"/>, which is
        /// what lets everything downstream stop checking for empty groups.
        /// </remarks>
        private LinePack(
            Dictionary<string, Dictionary<ChatterEvent, string[]>> byPersonality,
            IReadOnlyList<string> personalities,
            Palette colors)
        {
            _byPersonality = byPersonality;
            Personalities = personalities;
            Colors = colors;
        }

        /// <summary>What color each event is drawn in.</summary>
        /// <remarks>
        /// Here rather than alongside, so that reloading a pack swaps the lines and
        /// the colors in a single assignment. Two fields updated one after the other
        /// would leave a window - short, but during a fight - where a skeleton says a
        /// new line in the old pack's color.
        /// </remarks>
        internal Palette Colors { get; }

        /// <summary>Every personality in the pack, in a stable order.</summary>
        /// <remarks>
        /// Sorted, and that matters more than it looks. Assigning a personality to a
        /// newly summoned skeleton means choosing an index into this list, and if the
        /// order depended on what a Dictionary felt like that day, the same index
        /// would mean different personalities on different clients - or on the same
        /// client after a restart.
        ///
        /// Read-only so a caller cannot Sort it out from under us.
        ///
        /// <see cref="SharedPersonality"/> is excluded: it is a fallback rather than
        /// a personality type, and nothing should be summoned as "common".
        /// </remarks>
        internal IReadOnlyList<string> Personalities { get; }

        /// <summary>Is there nothing in here at all?</summary>
        /// <remarks>
        /// Not the same question as having no personalities - a pack of nothing but
        /// <see cref="SharedPersonality"/> lines has none and works fine. This asks
        /// whether the squad would be mute.
        /// </remarks>
        internal bool IsEmpty => _byPersonality.Count == 0;

        /// <summary>Find the lines available for one personality and event.</summary>
        /// <returns>
        /// False when there is nothing to say, in which case the skeleton stays quiet.
        /// That happens when neither the personality nor
        /// <see cref="SharedPersonality"/> has any lines for this event, and it is a
        /// perfectly ordinary situation rather than an error - a pack author is
        /// allowed to decide that nobody comments on being unsummoned.
        /// </returns>
        /// <param name="personality">Which personality type is speaking. Null and unknown names are both fine, and fall back.</param>
        /// <param name="kind">What just happened.</param>
        /// <param name="lines">The group, never empty when we return true.</param>
        /// <remarks>
        /// Exposed for <see cref="LineChooser"/>, which needs to know how many lines
        /// there are so that it can walk them deliberately rather than rolling dice
        /// and hoping. That is what lets "never say the same thing twice running" be
        /// a guarantee rather than a very likely outcome.
        ///
        /// Falling back to <see cref="SharedPersonality"/> is per event, not per
        /// personality. A cowardly skeleton with its own idle lines but no death
        /// lines uses its own idle lines and the shared death ones, which is the
        /// behavior you would want when filling a pack in gradually.
        /// </remarks>
        internal bool TryGetGroup(string personality, ChatterEvent kind, out IReadOnlyList<string> lines)
        {
            if (TryGetLines(personality, kind, out string[] own))
            {
                lines = own;
                return true;
            }

            if (TryGetLines(SharedPersonality, kind, out string[] shared))
            {
                lines = shared;
                return true;
            }

            lines = null;
            return false;
        }

        /// <summary>Which events this pack has nothing at all to say about.</summary>
        /// <returns>The uncovered events, in enum order. Empty for a pack covering everything.</returns>
        /// <remarks>
        /// For the warning at load. When the mod gains an event, every pack written
        /// before it goes quiet for that event and there is no symptom - the hook
        /// fires, the budget approves, and the pack has no line, which looks exactly
        /// like a hook that does not work. That is how the combat events landed: they
        /// were correct, and silent for anyone who had ever touched their file.
        ///
        /// A refreshed copy of the shipped pack is written alongside on every launch,
        /// which is the fix in principle and useless in practice, because nobody
        /// diffs four hundred lines of YAML against a file they have edited.
        ///
        /// Only events nothing covers are reported. A personality that leaves an event
        /// to the shared lines is the documented way to write a pack gradually, so
        /// counting that as missing would warn about the normal case.
        /// </remarks>
        internal IReadOnlyList<ChatterEvent> EventsWithNoLines()
        {
            List<ChatterEvent> missing = [];

            foreach (ChatterEvent kind in Enum.GetValues(typeof(ChatterEvent)))
            {
                bool covered = TryGetLines(SharedPersonality, kind, out _);

                for (int i = 0; !covered && i < Personalities.Count; i++)
                {
                    covered = TryGetLines(Personalities[i], kind, out _);
                }

                if (!covered)
                {
                    missing.Add(kind);
                }
            }

            return missing;
        }

        /// <summary>Choose the line a given line ref points at.</summary>
        /// <returns>False when there is nothing to say. See <see cref="TryGetGroup"/>.</returns>
        /// <param name="personality">Which personality type is speaking.</param>
        /// <param name="kind">What just happened.</param>
        /// <param name="lineRef">
        /// Any number at all. It gets folded down to an index, so the caller does not
        /// have to know how many lines exist - which is just as well, because the
        /// client that chose the line ref may have a different pack to the one reading it.
        /// </param>
        /// <param name="template">The raw line, tokens unfilled. See <see cref="LineTokens"/>.</param>
        /// <remarks>
        /// This is what a client that did *not* choose the line runs: a line ref arrives
        /// over the network and this turns it into words, with no state involved.
        /// </remarks>
        internal bool TryPick(string personality, ChatterEvent kind, int lineRef, out string template)
        {
            if (!TryGetGroup(personality, kind, out IReadOnlyList<string> lines))
            {
                template = null;
                return false;
            }

            // Modulo of a negative line ref is negative in C#, and a negative index
            // throws. LineRefs reaching us from another client are whatever that client
            // put in a ZDO, so this is not a theoretical worry.
            template = lines[(int)((uint)lineRef % (uint)lines.Count)];
            return true;
        }

        /// <summary>Look up one personality's lines for one event, with no fallback.</summary>
        /// <param name="personality">Which personality type. An unknown name is fine, and finds nothing.</param>
        /// <param name="kind">What happened.</param>
        /// <param name="lines">
        /// The lines, guaranteed non-empty when we return true, and null when false.
        /// Null rather than an empty list because the bool is the contract - a caller
        /// that checks it never sees this at all.
        /// </param>
        /// <returns>True when there is at least one line to choose from.</returns>
        /// <remarks>
        /// The builder drops empty groups, so anything in the dictionary has content.
        /// That saves callers from having to tell "no lines" apart from "an empty list
        /// of lines", which would otherwise be two ways of saying the same thing and
        /// one of them would eventually divide by zero.
        /// </remarks>
        private bool TryGetLines(string personality, ChatterEvent kind, out string[] lines)
        {
            lines = null;

            return personality != null
                && _byPersonality.TryGetValue(personality, out Dictionary<ChatterEvent, string[]> byEvent)
                && byEvent.TryGetValue(kind, out lines);
        }

        /// <summary>
        /// Assembles a <see cref="LinePack"/> one group of lines at a time.
        /// </summary>
        /// <remarks>
        /// Nested so that it can reach the private constructor, which is the whole
        /// point - if it sat alongside as its own class, the constructor would have
        /// to be internal and anybody could build a pack that breaks the guarantees
        /// the pack's own comments promise.
        ///
        /// The real mod drives this from a YAML file, via PackReader. The tests drive it by hand,
        /// which is exactly why the pack does not read files itself - a test that
        /// needs four lines can just say so in four lines.
        /// </remarks>
        internal sealed class Builder
        {
            private readonly Dictionary<string, Dictionary<ChatterEvent, List<string>>> _lines = [];
            private readonly Dictionary<ChatterEvent, string> _colors = [];
            private string _fallbackColor;

            /// <summary>Add some lines for one personality reacting to one event.</summary>
            /// <returns>This builder, so calls can be chained.</returns>
            /// <param name="personality">
            /// The personality type speaking, or <see cref="SharedPersonality"/> for
            /// lines anyone may fall back on.
            /// </param>
            /// <param name="kind">What the lines are a reaction to.</param>
            /// <param name="lines">
            /// The lines themselves, tokens and all. Nulls and blanks are skipped
            /// rather than rejected - a hand-edited file will eventually contain a
            /// stray empty entry, and throwing the whole pack away over one is not a
            /// kindness.
            /// </param>
            /// <remarks>
            /// Calling this twice for the same personality and event adds to that
            /// group rather than replacing it. No pack file can reach that - YAML
            /// refuses a duplicate key outright - so it is really a convenience for
            /// the tests, which build groups up a line at a time.
            /// </remarks>
            internal Builder Add(string personality, ChatterEvent kind, params string[] lines)
            {
                if (string.IsNullOrWhiteSpace(personality) || lines == null)
                {
                    return this;
                }

                if (!_lines.TryGetValue(personality, out Dictionary<ChatterEvent, List<string>> byEvent))
                {
                    byEvent = [];
                    _lines[personality] = byEvent;
                }

                if (!byEvent.TryGetValue(kind, out List<string> group))
                {
                    group = [];
                    byEvent[kind] = group;
                }

                for (int i = 0; i < lines.Length; i++)
                {
                    if (!string.IsNullOrWhiteSpace(lines[i]))
                    {
                        group.Add(lines[i]);
                    }
                }

                return this;
            }

            /// <summary>Set the color for events that do not name one of their own.</summary>
            /// <returns>This builder, so calls can be chained.</returns>
            /// <param name="hex">A hex code like #E8E4DC, or null for Valheim's usual white.</param>
            internal Builder SetDefaultColor(string hex)
            {
                _fallbackColor = hex;
                return this;
            }

            /// <summary>Set the color for one event.</summary>
            /// <returns>This builder, so calls can be chained.</returns>
            /// <param name="kind">The event to color.</param>
            /// <param name="hex">A hex code like #F0A9A0.</param>
            internal Builder SetColor(ChatterEvent kind, string hex)
            {
                _colors[kind] = hex;
                return this;
            }

            /// <summary>Freeze what has been added into a pack.</summary>
            /// <returns>
            /// A pack holding only the groups that ended up with lines in them. A
            /// personality whose every line was blank does not appear at all, and
            /// will not turn up in <see cref="Personalities"/> to be assigned to some
            /// unfortunate skeleton who then never speaks.
            /// </returns>
            /// <remarks>
            /// The lists become arrays here. Nothing after this point ever adds a
            /// line, and an array is the cheaper thing to index into over and over.
            /// </remarks>
            internal LinePack Build()
            {
                Dictionary<string, Dictionary<ChatterEvent, string[]>> byPersonality = [];
                List<string> personalities = [];

                foreach (KeyValuePair<string, Dictionary<ChatterEvent, List<string>>> entry in _lines)
                {
                    Dictionary<ChatterEvent, string[]> byEvent = [];

                    foreach (KeyValuePair<ChatterEvent, List<string>> group in entry.Value)
                    {
                        if (group.Value.Count > 0)
                        {
                            byEvent[group.Key] = [.. group.Value];
                        }
                    }

                    if (byEvent.Count == 0)
                    {
                        continue;
                    }

                    byPersonality[entry.Key] = byEvent;

                    if (entry.Key != SharedPersonality)
                    {
                        personalities.Add(entry.Key);
                    }
                }

                // See the note on Personalities - a stable order is what lets a stored
                // personality index mean the same thing on every client and after
                // every restart.
                personalities.Sort(StringComparer.Ordinal);

                return new LinePack(byPersonality, personalities, new Palette(_fallbackColor, _colors));
            }
        }
    }
}
