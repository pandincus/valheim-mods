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
    /// This class holds no state and remembers nothing.
    /// <see cref="TryPick"/> is a pure function of the pack, the personality, the
    /// event and a seed, and that is the whole point: the client that owns a
    /// skeleton broadcasts the seed, every other client looks it up in whatever pack
    /// it happens to have, and two players running the same pack land on the same
    /// line without ever having compared notes. Two players running *different*
    /// packs each get something sensible out of their own file, which is the reason
    /// we send a seed rather than a line number.
    ///
    /// Not knowing about YAML is also deliberate. A pack is built by calling
    /// <see cref="LinePackBuilder.Add"/>, and reading the file is somebody else's
    /// job - which keeps this testable without a file on disk, and keeps the
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

        /// <summary>Every personality in the pack, in a stable order.</summary>
        /// <remarks>
        /// Sorted, and that matters more than it looks. Assigning a personality to a
        /// newly summoned skeleton means choosing an index into this list, and if the
        /// order depended on what a Dictionary felt like that day, the same index
        /// would mean different personalities on different clients - or on the same
        /// client after a restart.
        ///
        /// <see cref="SharedPersonality"/> is excluded, because it is a fallback
        /// rather than a character. Nobody should be summoned as "common".
        /// </remarks>
        internal IList<string> Personalities { get; }

        internal LinePack(
            Dictionary<string, Dictionary<ChatterEvent, string[]>> byPersonality,
            IList<string> personalities)
        {
            _byPersonality = byPersonality;
            Personalities = personalities;
        }

        /// <summary>Choose the line a given seed points at.</summary>
        /// <returns>
        /// False when there is nothing to say, in which case the skeleton stays quiet.
        /// That happens when neither the personality nor
        /// <see cref="SharedPersonality"/> has any lines for this event, and it is a
        /// perfectly ordinary situation rather than an error - a pack author is
        /// allowed to decide that nobody comments on being unsummoned.
        /// </returns>
        /// <param name="personality">Which character is speaking.</param>
        /// <param name="kind">What just happened.</param>
        /// <param name="seed">
        /// Any number at all. It gets folded down to an index, so the caller does not
        /// have to know how many lines exist - which is just as well, because the
        /// client that chose the seed may have a different pack to the one reading it.
        /// </param>
        /// <param name="template">The raw line, tokens unfilled. See <see cref="LineTokens"/>.</param>
        /// <remarks>
        /// Falling back to <see cref="SharedPersonality"/> is per event, not per
        /// personality. A cowardly skeleton with its own idle lines but no death
        /// lines uses its own idle lines and the shared death ones, which is the
        /// behaviour you would want when filling a pack in gradually.
        /// </remarks>
        internal bool TryPick(string personality, ChatterEvent kind, int seed, out string template)
        {
            template = null;

            if (!TryGetLines(personality, kind, out string[] lines)
                && !TryGetLines(SharedPersonality, kind, out lines))
            {
                return false;
            }

            // Modulo of a negative seed is negative in C#, and a negative index
            // throws. Seeds reaching us from another client are whatever that client
            // put in a ZDO, so this is not a theoretical worry.
            int index = (int)((uint)seed % (uint)lines.Length);
            template = lines[index];
            return true;
        }

        /// <summary>Look up one personality's lines for one event.</summary>
        /// <param name="personality">Which character. A name we have never heard of is fine, and finds nothing.</param>
        /// <param name="kind">What happened.</param>
        /// <param name="lines">The lines, guaranteed non-empty when we return true.</param>
        /// <returns>True when there is at least one line to choose from.</returns>
        /// <remarks>
        /// The builder drops empty groups, so anything in the dictionary has content.
        /// That saves <see cref="TryPick"/> from having to tell "no lines" apart from
        /// "an empty list of lines", which would otherwise be two ways of saying the
        /// same thing and one of them would eventually divide by zero.
        /// </remarks>
        private bool TryGetLines(string personality, ChatterEvent kind, out string[] lines)
        {
            lines = null;

            return personality != null
                && _byPersonality.TryGetValue(personality, out Dictionary<ChatterEvent, string[]> byEvent)
                && byEvent.TryGetValue(kind, out lines);
        }
    }

    /// <summary>
    /// Assembles a <see cref="LinePack"/> one group of lines at a time.
    /// </summary>
    /// <remarks>
    /// The real mod will drive this from a YAML file. The tests drive it by hand,
    /// which is exactly why the pack does not read files itself - a test that needs
    /// four lines can just say so in four lines.
    /// </remarks>
    internal sealed class LinePackBuilder
    {
        private readonly Dictionary<string, Dictionary<ChatterEvent, List<string>>> _lines = [];

        /// <summary>Add some lines for one personality reacting to one event.</summary>
        /// <returns>This builder, so calls can be chained.</returns>
        /// <param name="personality">
        /// The character speaking, or <see cref="LinePack.SharedPersonality"/> for
        /// lines anyone may fall back on.
        /// </param>
        /// <param name="kind">What the lines are a reaction to.</param>
        /// <param name="lines">
        /// The lines themselves, tokens and all. Nulls and blanks are skipped rather
        /// than rejected - a hand-edited file will eventually contain a stray empty
        /// entry, and throwing the whole pack away over one is not a kindness.
        /// </param>
        /// <remarks>
        /// Calling this twice for the same personality and event adds to that group
        /// rather than replacing it, so a pack file can list a personality in more
        /// than one place without one half quietly winning.
        /// </remarks>
        internal LinePackBuilder Add(string personality, ChatterEvent kind, params string[] lines)
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

        /// <summary>Freeze what has been added into a pack.</summary>
        /// <returns>
        /// A pack holding only the groups that ended up with lines in them. A
        /// personality whose every line was blank does not appear at all, and will
        /// not turn up in <see cref="LinePack.Personalities"/> to be assigned to some
        /// unfortunate skeleton who then never speaks.
        /// </returns>
        /// <remarks>
        /// The lists become arrays here. Nothing after this point ever adds a line,
        /// and an array is the cheaper thing to index into over and over.
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

                if (entry.Key != LinePack.SharedPersonality)
                {
                    personalities.Add(entry.Key);
                }
            }

            // See the note on LinePack.Personalities - a stable order is what lets a
            // stored personality index mean the same thing on every client and after
            // every restart.
            personalities.Sort(StringComparer.Ordinal);

            return new LinePack(byPersonality, personalities);
        }
    }
}
