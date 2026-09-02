using System;
using System.Collections.Generic;

namespace ChattyBones.Logic
{
    /// <summary>
    /// Everything the skeletons are able to say, and the rule for choosing one.
    /// </summary>
    /// <remarks>
    /// Lines are grouped by personality and then by event, so a cowardly skeleton
    /// and a boastful one react to the same greydwarf quite differently. An event may
    /// also carry a context - <c>Idle[biome=Swamp]</c> - so the same skeleton reacts
    /// differently to standing in different places.
    ///
    /// No state, and <see cref="TryPick"/> is a pure function. The client owning a
    /// skeleton broadcasts a line ref; everyone else folds it against their own
    /// pack. Same pack, same line, with nobody comparing notes - and, since context
    /// arrived, with nobody but the owner even asking where the skeleton is. What
    /// makes that work is <see cref="LineSpace"/>, which numbers every line a
    /// personality could reach for an event as one list.
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

        private readonly Dictionary<string, Dictionary<ChatterEvent, LineSpace>> _spaces;

        /// <summary>Wrap what the builder assembled.</summary>
        /// <param name="spaces">Personality to event to its numbering. Every space non-empty.</param>
        /// <param name="personalities">The personality types, sorted, without the shared fallback.</param>
        /// <param name="colors">What color each event is drawn in.</param>
        /// <remarks>
        /// Private, and reachable only from <see cref="Builder.Build"/>, which is
        /// what lets everything downstream stop checking for empty groups.
        /// </remarks>
        private LinePack(
            Dictionary<string, Dictionary<ChatterEvent, LineSpace>> spaces,
            IReadOnlyList<string> personalities,
            Palette colors)
        {
            _spaces = spaces;
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
        internal bool IsEmpty => _spaces.Count == 0;

        /// <summary>Find the numbering one personality counts against for one event.</summary>
        /// <returns>
        /// False when there is nothing to say, in which case the skeleton stays quiet.
        /// That happens when neither the personality nor
        /// <see cref="SharedPersonality"/> has any lines for this event, and it is a
        /// perfectly ordinary situation rather than an error - a pack author is
        /// allowed to decide that nobody comments on being unsummoned.
        /// </returns>
        /// <param name="personality">Which personality type is speaking. Null and unknown names are both fine, and fall back.</param>
        /// <param name="kind">What just happened.</param>
        /// <param name="space">The numbering, never empty when we return true.</param>
        /// <remarks>
        /// Takes no context, and that is the whole point of it. A client that did not
        /// choose the line has to reach the same numbering knowing only who is speaking
        /// and what happened, because working out anything more would mean resolving a
        /// context it may have no way to see.
        ///
        /// Falling back to <see cref="SharedPersonality"/> is per event, not per
        /// personality. A cowardly skeleton with its own idle lines but no death
        /// lines uses its own idle lines and the shared death ones, which is the
        /// behavior you would want when filling a pack in gradually.
        /// </remarks>
        internal bool TryGetSpace(string personality, ChatterEvent kind, out LineSpace space)
        {
            space = null;

            if (personality != null
                && _spaces.TryGetValue(personality, out Dictionary<ChatterEvent, LineSpace> own)
                && own.TryGetValue(kind, out space))
            {
                return true;
            }

            return _spaces.TryGetValue(SharedPersonality, out Dictionary<ChatterEvent, LineSpace> shared)
                && shared.TryGetValue(kind, out space);
        }

        /// <summary>Does this personality write any of its own lines for this event?</summary>
        /// <returns>False when it leaves the event entirely to the shared lines.</returns>
        /// <param name="personality">The personality type to ask about.</param>
        /// <param name="kind">The event.</param>
        /// <remarks>
        /// Worth having as a question of its own because it cannot be inferred from
        /// what the other methods hand back. They all fall back to the shared lines and
        /// answer the same either way, which is right for saying something and useless
        /// for asking whose lines they were.
        ///
        /// Comparing the returned lists used to serve instead. That stopped working the
        /// moment a space merged the two, and it stopped *loudly* only because someone
        /// looked - a reference comparison against a freshly built list is silently
        /// false forever, so the tests relying on it passed while checking nothing.
        /// </remarks>
        internal bool HasOwnLines(string personality, ChatterEvent kind)
        {
            return personality != null
                && personality != SharedPersonality
                && _spaces.TryGetValue(personality, out Dictionary<ChatterEvent, LineSpace> own)
                && own.ContainsKey(kind);
        }

        /// <summary>Find the lines a skeleton in these contexts should draw from.</summary>
        /// <returns>False when there is nothing to say. See <see cref="TryGetSpace"/>.</returns>
        /// <param name="personality">Which personality type is speaking.</param>
        /// <param name="kind">What just happened.</param>
        /// <param name="contexts">What the skeleton currently satisfies, or null for none.</param>
        /// <param name="space">The whole numbering, which the chosen window is part of.</param>
        /// <param name="offset">Where the window starts in it.</param>
        /// <param name="length">How many lines the window holds.</param>
        /// <remarks>
        /// The window is what may be *said*; the space is what the line ref counts
        /// against. Handing back both is the whole shape of the thing - a caller that
        /// only had the window would have no way to name its choice in a way anybody
        /// else could follow.
        /// </remarks>
        internal bool TrySelect(
            string personality,
            ChatterEvent kind,
            IReadOnlyList<string> contexts,
            out LineSpace space,
            out int offset,
            out int length)
        {
            offset = 0;
            length = 0;

            return TryGetSpace(personality, kind, out space)
                && space.TrySelect(contexts, out offset, out length);
        }

        /// <summary>The lines a skeleton in these contexts would draw from, as a list.</summary>
        /// <returns>False when there is nothing to say.</returns>
        /// <param name="personality">Which personality type is speaking.</param>
        /// <param name="kind">What just happened.</param>
        /// <param name="lines">The window, never empty when we return true.</param>
        /// <param name="contexts">What the skeleton satisfies, or null for the plain groups.</param>
        /// <remarks>
        /// A convenience over <see cref="TrySelect"/> for tests and diagnostics, which
        /// want to look at a group rather than walk one. It copies, so the chooser does
        /// not use it - that runs per utterance and has no reason to allocate.
        /// </remarks>
        internal bool TryGetGroup(
            string personality,
            ChatterEvent kind,
            out IReadOnlyList<string> lines,
            IReadOnlyList<string> contexts = null)
        {
            lines = null;

            if (!TrySelect(personality, kind, contexts, out LineSpace space, out int offset, out int length))
            {
                return false;
            }

            string[] window = new string[length];

            for (int i = 0; i < length; i++)
            {
                window[i] = space.All[offset + i];
            }

            lines = window;
            return true;
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
                if (!TryGetSpace(null, kind, out _))
                {
                    bool covered = false;

                    for (int i = 0; !covered && i < Personalities.Count; i++)
                    {
                        covered = TryGetSpace(Personalities[i], kind, out _);
                    }

                    if (!covered)
                    {
                        missing.Add(kind);
                    }
                }
            }

            return missing;
        }

        /// <summary>Every context any group in the pack is tagged with.</summary>
        /// <returns>The contexts as "name=value", each once, in no particular order.</returns>
        /// <remarks>
        /// For the check at load that the values name things the game actually has.
        /// <see cref="EventKey"/> can tell that <c>biome</c> is a context this version
        /// understands, but not that <c>Swamps</c> is not a biome - the list of those
        /// is a Unity type, and this half of the mod cannot see one.
        ///
        /// So a misspelled value parses perfectly and then matches nothing, which is
        /// the silent failure this mod keeps paying for. The mod side walks these
        /// against the real enum and says so.
        /// </remarks>
        internal IReadOnlyList<string> Contexts()
        {
            HashSet<string> seen = [];
            List<string> all = [];

            foreach (KeyValuePair<string, Dictionary<ChatterEvent, LineSpace>> byPersonality in _spaces)
            {
                foreach (KeyValuePair<ChatterEvent, LineSpace> byEvent in byPersonality.Value)
                {
                    foreach (string context in byEvent.Value.Contexts())
                    {
                        if (seen.Add(context))
                        {
                            all.Add(context);
                        }
                    }
                }
            }

            return all;
        }

        /// <summary>Choose the line a given line ref points at.</summary>
        /// <returns>False when there is nothing to say. See <see cref="TryGetSpace"/>.</returns>
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
        /// over the network and this turns it into words, with no state involved and no
        /// question asked about where anybody is standing.
        /// </remarks>
        internal bool TryPick(string personality, ChatterEvent kind, int lineRef, out string template)
        {
            if (!TryGetSpace(personality, kind, out LineSpace space))
            {
                template = null;
                return false;
            }

            // Modulo of a negative line ref is negative in C#, and a negative index
            // throws. LineRefs reaching us from another client are whatever that client
            // put in a ZDO, so this is not a theoretical worry.
            template = space.All[(int)((uint)lineRef % (uint)space.Count)];
            return true;
        }

        /// <summary>Choose a line a line ref points at, and that we can actually say.</summary>
        /// <returns>False when nothing in the whole space renders. See <see cref="TryGetSpace"/>.</returns>
        /// <param name="personality">Which personality type is speaking.</param>
        /// <param name="kind">What just happened.</param>
        /// <param name="lineRef">The number off the wire. Folded, exactly as <see cref="TryPick"/> folds it.</param>
        /// <param name="tokens">What this client was able to work out about the event.</param>
        /// <param name="line">The finished line, tokens filled in.</param>
        /// <remarks>
        /// What a listening client runs. It starts where the line ref points and walks
        /// on until something renders, which is the same walk
        /// <see cref="LineChooser.TryChoose"/> does, minus the no-repeat memory - a
        /// listener has no business having one, for the reason that class explains.
        ///
        /// **Walking on rather than falling silent is a decision, and it is worth being
        /// clear about what it costs.** Two players watching the same skeleton can read
        /// different bubbles: the owner had a {weapon} to hand and the listener did not,
        /// so the listener slid on to the next line. The mod already accepts that for
        /// two players with different packs - mirroring a line ref rather than an index
        /// is precisely the decision that a listener resolves against its own file - so
        /// this is the same trade in a smaller place. Set against it, silence is the
        /// failure this mod has paid for over and over, and a skeleton that visibly
        /// reacts to nothing while its owner hears it chattering is the worse of the
        /// two bugs by a distance.
        ///
        /// It walks the whole space rather than a context window, because a listener
        /// never resolves a context - see <see cref="LineSpace"/>.
        /// </remarks>
        internal bool TryPickRenderable(
            string personality, ChatterEvent kind, int lineRef, LineTokens tokens, out string line)
        {
            line = null;

            if (!TryGetSpace(personality, kind, out LineSpace space))
            {
                return false;
            }

            IReadOnlyList<string> all = space.All;
            int count = space.Count;
            int from = (int)((uint)lineRef % (uint)count);

            for (int offset = 0; offset < count; offset++)
            {
                if (tokens.TryRender(all[(from + offset) % count], out line))
                {
                    return true;
                }
            }

            line = null;
            return false;
        }

        /// <summary>
        /// Assembles a <see cref="LinePack"/> one group of lines at a time.
        /// </summary>
        /// <remarks>
        /// Nested so that it can reach the private constructor, which is the whole
        /// point - if it sat alongside as its own class, the constructor would have
        /// to be internal and anybody could build a pack that breaks the guarantees
        /// everything downstream relies on.
        /// </remarks>
        internal sealed class Builder
        {
            /// <summary>Personality, then event, then its groups in the order they arrived.</summary>
            /// <remarks>
            /// A list rather than a dictionary at the innermost level, because the order
            /// groups are added in is the order they get numbered in, and that is a
            /// promise made to pack authors: the group higher up your file is the one
            /// that wins a tie. A Dictionary would hand that decision to a hash.
            /// </remarks>
            private readonly Dictionary<string, Dictionary<ChatterEvent, List<KeyValuePair<string, List<string>>>>> _lines = [];

            private readonly Dictionary<ChatterEvent, string> _colors = [];
            private string _fallbackColor;

            /// <summary>Add some lines for one personality reacting to one event, anywhere.</summary>
            /// <returns>This builder, so calls can be chained.</returns>
            /// <param name="personality">
            /// The personality type speaking, or <see cref="SharedPersonality"/> for
            /// lines anyone may fall back on.
            /// </param>
            /// <param name="kind">What the lines are a reaction to.</param>
            /// <param name="lines">The lines themselves, tokens and all.</param>
            internal Builder Add(string personality, ChatterEvent kind, params string[] lines)
            {
                return Add(personality, EventKey.Plain(kind), lines);
            }

            /// <summary>Add some lines for one personality, event and context.</summary>
            /// <returns>This builder, so calls can be chained.</returns>
            /// <param name="personality">
            /// The personality type speaking, or <see cref="SharedPersonality"/> for
            /// lines anyone may fall back on.
            /// </param>
            /// <param name="key">What the lines are a reaction to, and where.</param>
            /// <param name="lines">
            /// The lines themselves, tokens and all. Nulls and blanks are skipped
            /// rather than rejected - a hand-edited file will eventually contain a
            /// stray empty entry, and throwing the whole pack away over one is not a
            /// kindness.
            /// </param>
            /// <remarks>
            /// Calling this twice for the same personality, event and context adds to
            /// that group rather than replacing it. No pack file can reach that - YAML
            /// refuses a duplicate key outright - so it is really a convenience for
            /// the tests, which build groups up a line at a time.
            /// </remarks>
            internal Builder Add(string personality, EventKey key, params string[] lines)
            {
                if (string.IsNullOrWhiteSpace(personality) || lines == null)
                {
                    return this;
                }

                if (!_lines.TryGetValue(personality, out Dictionary<ChatterEvent, List<KeyValuePair<string, List<string>>>> byEvent))
                {
                    byEvent = [];
                    _lines[personality] = byEvent;
                }

                if (!byEvent.TryGetValue(key.Kind, out List<KeyValuePair<string, List<string>>> groups))
                {
                    groups = [];
                    byEvent[key.Kind] = groups;
                }

                string context = key.Context ?? LineSpace.PlainKey;
                List<string> group = null;

                for (int i = 0; i < groups.Count; i++)
                {
                    if (string.Equals(groups[i].Key, context, StringComparison.Ordinal))
                    {
                        group = groups[i].Value;
                        break;
                    }
                }

                if (group == null)
                {
                    group = [];
                    groups.Add(new KeyValuePair<string, List<string>>(context, group));
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

            /// <summary>Set the color one event is drawn in.</summary>
            /// <returns>This builder, so calls can be chained.</returns>
            /// <param name="kind">The event.</param>
            /// <param name="hex">A hex code like #E8E4DC.</param>
            internal Builder SetColor(ChatterEvent kind, string hex)
            {
                if (!string.IsNullOrWhiteSpace(hex))
                {
                    _colors[kind] = hex;
                }

                return this;
            }

            /// <summary>Turn everything added so far into a pack.</summary>
            /// <returns>
            /// A pack with no empty groups and a stable personality order. Possibly an
            /// empty one, if nothing usable was ever added.
            /// </returns>
            /// <remarks>
            /// This is where the numbering is worked out, once, rather than per
            /// utterance - a space is a couple of arrays and there are only ever a few
            /// dozen of them.
            ///
            /// A space is built for every personality that has anything for an event,
            /// plus one per event for the shared lines. A known personality with nothing
            /// for some event has no space of its own and falls back to the shared one
            /// in <see cref="TryGetSpace"/>, which is the same list the sender used.
            /// </remarks>
            internal LinePack Build()
            {
                Dictionary<ChatterEvent, List<KeyValuePair<string, string[]>>> shared = Frozen(SharedPersonality);

                Dictionary<string, Dictionary<ChatterEvent, LineSpace>> spaces = [];
                List<string> personalities = [];

                foreach (KeyValuePair<string, Dictionary<ChatterEvent, List<KeyValuePair<string, List<string>>>>> entry in _lines)
                {
                    Dictionary<ChatterEvent, LineSpace> byEvent = [];

                    foreach (KeyValuePair<ChatterEvent, List<KeyValuePair<string, List<string>>>> group in entry.Value)
                    {
                        List<KeyValuePair<string, string[]>> own = Freeze(group.Value);

                        if (own.Count == 0)
                        {
                            continue;
                        }

                        _ = shared.TryGetValue(group.Key, out List<KeyValuePair<string, string[]>> fallback);

                        LineSpace space = entry.Key == SharedPersonality
                            ? LineSpace.Build(own, own)
                            : LineSpace.Build(own, fallback);

                        if (space != null)
                        {
                            byEvent[group.Key] = space;
                        }
                    }

                    if (byEvent.Count == 0)
                    {
                        continue;
                    }

                    spaces[entry.Key] = byEvent;

                    if (entry.Key != SharedPersonality)
                    {
                        personalities.Add(entry.Key);
                    }
                }

                // See the note on Personalities - a stable order is what lets a stored
                // personality index mean the same thing on every client and after
                // every restart.
                personalities.Sort(StringComparer.Ordinal);

                return new LinePack(spaces, personalities, new Palette(_fallbackColor, _colors));
            }

            /// <summary>One personality's groups, per event, with the lists turned into arrays.</summary>
            /// <returns>Event to that event's groups in order. Empty when the personality has nothing.</returns>
            /// <param name="personality">Whose groups to take.</param>
            /// <remarks>
            /// Taken once and reused for every personality's fallback, rather than
            /// rebuilt per personality - the shared lines are the same lines each time.
            /// </remarks>
            private Dictionary<ChatterEvent, List<KeyValuePair<string, string[]>>> Frozen(string personality)
            {
                Dictionary<ChatterEvent, List<KeyValuePair<string, string[]>>> frozen = [];

                if (!_lines.TryGetValue(personality, out Dictionary<ChatterEvent, List<KeyValuePair<string, List<string>>>> byEvent))
                {
                    return frozen;
                }

                foreach (KeyValuePair<ChatterEvent, List<KeyValuePair<string, List<string>>>> group in byEvent)
                {
                    List<KeyValuePair<string, string[]>> lines = Freeze(group.Value);

                    if (lines.Count > 0)
                    {
                        frozen[group.Key] = lines;
                    }
                }

                return frozen;
            }

            /// <summary>Turn one event's groups into arrays, dropping any that ended up empty.</summary>
            /// <returns>The groups, in the order they were added.</returns>
            /// <param name="groups">The groups as the builder holds them.</param>
            private static List<KeyValuePair<string, string[]>> Freeze(
                List<KeyValuePair<string, List<string>>> groups)
            {
                List<KeyValuePair<string, string[]>> frozen = [];

                for (int i = 0; i < groups.Count; i++)
                {
                    if (groups[i].Value.Count > 0)
                    {
                        frozen.Add(new KeyValuePair<string, string[]>(groups[i].Key, [.. groups[i].Value]));
                    }
                }

                return frozen;
            }
        }
    }
}
