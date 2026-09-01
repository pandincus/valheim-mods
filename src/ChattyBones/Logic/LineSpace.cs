using System;
using System.Collections.Generic;

namespace ChattyBones.Logic
{
    /// <summary>Every line one personality could reach for one event, numbered once.</summary>
    /// <remarks>
    /// This is the piece that lets a skeleton pick a line by where it is standing while
    /// everybody else's game lands on the same line without asking where anything is.
    ///
    /// The owner resolves the context, picks a group, and sends an index. A listener
    /// gets only the number - so the list it counts against has to be decided by the
    /// personality and the event alone. That rules out numbering each group separately,
    /// because *which* group applies is exactly the thing a listener cannot work out.
    ///
    /// So every line the pair could ever reach is numbered together - the personality's
    /// groups first, then the shared ones - and the context picks a window into that
    /// numbering rather than a list of its own.
    ///
    /// **Within a band the numbering is the order the pack wrote them in**, not sorted.
    /// Sorting was the first answer and it was worse: a pack author looking at their own
    /// file expects the group higher up the page to be the one that wins, and there is
    /// no reason to take that away from them. File order is exactly as reproducible,
    /// because everybody parses the same file and a YAML mapping comes back in document
    /// order - which is pinned by <c>YamlOrderTests</c>, since nothing in the type name
    /// promises it.
    ///
    /// The plain group is not part of that tie-break. Which group *applies* is decided
    /// by specificity - a context group beats a plain one wherever either sits in the
    /// file - and file order only settles ties between two context groups that both
    /// match. Two orders, doing two different jobs.
    ///
    /// A cowardly skeleton with two Swamp lines and three plain ones therefore numbers
    /// its Swamp lines 0 and 1 whether or not it is in the Swamp. That is the cost: the
    /// numbering is wider than any one group. With <see cref="Utterance.MaxLineRef"/> at
    /// 65535 against groups of tens, there is no crowding to worry about.
    /// </remarks>
    internal sealed class LineSpace
    {
        private readonly string[] _all;
        private readonly Group[] _groups;

        /// <summary>Wrap a finished numbering.</summary>
        /// <param name="all">Every line, in flat order.</param>
        /// <param name="groups">Where each group sits in it, in the same order.</param>
        private LineSpace(string[] all, Group[] groups)
        {
            _all = all;
            _groups = groups;
        }

        /// <summary>Every line, in the order a line ref counts against.</summary>
        internal IReadOnlyList<string> All => _all;

        /// <summary>Each group in the numbering, in the order it was written.</summary>
        /// <remarks>
        /// For the rules that have to look at groups nobody is currently standing in -
        /// a one-line Swamp group shadows the lines beside it just as surely as a
        /// one-line plain group does, and is invisible to anything that only inspects
        /// the window in force right now.
        /// </remarks>
        internal IReadOnlyList<Group> Groups => _groups;

        /// <summary>How many lines the numbering holds.</summary>
        internal int Count => _all.Length;

        /// <summary>Build the numbering for one personality and one event.</summary>
        /// <returns>The space, or null when neither the personality nor the shared lines have anything.</returns>
        /// <param name="own">The personality's groups, context to lines, or null.</param>
        /// <param name="shared">The shared groups, context to lines, or null.</param>
        /// <remarks>
        /// Pass the same dictionary as both arguments and it is used once, not twice -
        /// which is what a skeleton whose personality *is* the shared one needs, and
        /// what an unknown personality falls into by having no groups of its own.
        /// </remarks>
        internal static LineSpace Build(
            IReadOnlyList<KeyValuePair<string, string[]>> own,
            IReadOnlyList<KeyValuePair<string, string[]>> shared)
        {
            List<string> all = [];
            List<Group> groups = [];

            AddBand(own, personal: true, all, groups);

            if (!ReferenceEquals(own, shared))
            {
                AddBand(shared, personal: false, all, groups);
            }

            return all.Count == 0 ? null : new LineSpace([.. all], [.. groups]);
        }

        /// <summary>Append one personality's groups to the numbering being built.</summary>
        /// <param name="from">The groups in the order the pack wrote them, or null.</param>
        /// <param name="personal">True for the skeleton's own personality, false for the shared lines.</param>
        /// <param name="all">The flat list being built.</param>
        /// <param name="groups">The windows being built.</param>
        /// <remarks>
        /// Straight through in the order given, plain group and all. Reordering here
        /// would be the sorted version by another name, and specificity is applied when
        /// a group is *selected* rather than when it is numbered.
        /// </remarks>
        private static void AddBand(
            IReadOnlyList<KeyValuePair<string, string[]>> from,
            bool personal,
            List<string> all,
            List<Group> groups)
        {
            if (from == null)
            {
                return;
            }

            for (int i = 0; i < from.Count; i++)
            {
                string context = string.IsNullOrEmpty(from[i].Key) ? null : from[i].Key;
                Append(from[i].Value, context, personal, all, groups);
            }
        }

        /// <summary>Put one group's lines into the numbering and record where they went.</summary>
        /// <param name="lines">The group.</param>
        /// <param name="context">Its context, or null for the plain group.</param>
        /// <param name="personal">Whether it belongs to the skeleton's own personality.</param>
        /// <param name="all">The flat list being built.</param>
        /// <param name="groups">The windows being built.</param>
        private static void Append(
            string[] lines,
            string context,
            bool personal,
            List<string> all,
            List<Group> groups)
        {
            if (lines == null || lines.Length == 0)
            {
                return;
            }

            groups.Add(new Group(context, personal, all.Count, lines.Length));
            all.AddRange(lines);
        }

        /// <summary>The key a plain group is filed under.</summary>
        /// <remarks>
        /// A dictionary cannot take a null key, and "" cannot collide with a real
        /// context because <see cref="EventKey"/> refuses an empty one.
        /// </remarks>
        internal const string PlainKey = "";

        /// <summary>Pick the window a skeleton in these contexts should draw from.</summary>
        /// <returns>True always, for a space that exists at all - it is built non-empty.</returns>
        /// <param name="contexts">
        /// The contexts this skeleton currently satisfies, as "name=value". Null or
        /// empty is fine and simply reaches the plain groups.
        /// </param>
        /// <param name="offset">Where the chosen group starts in <see cref="All"/>.</param>
        /// <param name="length">How many lines it holds.</param>
        /// <remarks>
        /// Most specific wins, and personality outranks context. A cowardly skeleton in
        /// the Swamp with no Swamp lines of its own uses its own plain lines rather than
        /// the shared Swamp ones - which writes better people at the cost of writing
        /// slightly worse places, and the four personalities are why the squad reads as
        /// a group rather than as narrators.
        ///
        /// Within a band, two context groups that both match are settled by whichever
        /// the pack wrote first. With more than one context that is no longer a corner
        /// case - a skeleton in the Swamp at night satisfies two at once every time it
        /// speaks - so the rule now decides something on most utterances rather than
        /// none. It stays file order because an author can see it and change it by
        /// moving a group up the page, which is not true of any ranking between the
        /// context names themselves.
        /// </remarks>
        internal bool TrySelect(IReadOnlyList<string> contexts, out int offset, out int length)
        {
            if (TryBand(contexts, personal: true, out offset, out length)
                || TryPlain(personal: true, out offset, out length)
                || TryBand(contexts, personal: false, out offset, out length)
                || TryPlain(personal: false, out offset, out length))
            {
                return true;
            }

            // Unreachable for a space built by Build, which is never empty. Falling back
            // to the whole numbering rather than returning false keeps a caller that
            // gets here saying something instead of going silent.
            offset = 0;
            length = _all.Length;
            return _all.Length > 0;
        }

        /// <summary>Find a context group in one band.</summary>
        /// <returns>True when one of the contexts has a group here.</returns>
        /// <param name="contexts">What the skeleton satisfies.</param>
        /// <param name="personal">Which band to look in.</param>
        /// <param name="offset">Where it starts.</param>
        /// <param name="length">How long it is.</param>
        private bool TryBand(IReadOnlyList<string> contexts, bool personal, out int offset, out int length)
        {
            offset = 0;
            length = 0;

            if (contexts == null || contexts.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < _groups.Length; i++)
            {
                Group group = _groups[i];

                if (group.Personal != personal || group.Context == null)
                {
                    continue;
                }

                // Hand-rolled rather than LINQ or a set: this runs per utterance, and a
                // skeleton satisfies a handful of contexts at a time, never many.
                for (int c = 0; c < contexts.Count; c++)
                {
                    if (string.Equals(contexts[c], group.Context, StringComparison.Ordinal))
                    {
                        offset = group.Offset;
                        length = group.Length;
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>Find the plain group in one band.</summary>
        /// <returns>True when that band has one.</returns>
        /// <param name="personal">Which band to look in.</param>
        /// <param name="offset">Where it starts.</param>
        /// <param name="length">How long it is.</param>
        private bool TryPlain(bool personal, out int offset, out int length)
        {
            offset = 0;
            length = 0;

            for (int i = 0; i < _groups.Length; i++)
            {
                Group group = _groups[i];

                if (group.Personal == personal && group.Context == null)
                {
                    offset = group.Offset;
                    length = group.Length;
                    return true;
                }
            }

            return false;
        }

        /// <summary>Every context any group in this space is tagged with.</summary>
        /// <returns>The contexts, which may be empty.</returns>
        /// <remarks>
        /// For the check at load that a pack's context values name things the game
        /// actually has. A biome spelled "Swamps" parses perfectly and then never
        /// matches anything, which is the silent failure this mod keeps paying for.
        /// </remarks>
        internal IEnumerable<string> Contexts()
        {
            for (int i = 0; i < _groups.Length; i++)
            {
                if (_groups[i].Context != null)
                {
                    yield return _groups[i].Context;
                }
            }
        }

        /// <summary>Where one group sits in the numbering.</summary>
        internal readonly struct Group
        {
            /// <summary>Record one group's place.</summary>
            /// <param name="context">Its context, or null for the plain group.</param>
            /// <param name="personal">Whether it is the skeleton's own personality's.</param>
            /// <param name="offset">Where it starts in the flat list.</param>
            /// <param name="length">How many lines it holds.</param>
            internal Group(string context, bool personal, int offset, int length)
            {
                Context = context;
                Personal = personal;
                Offset = offset;
                Length = length;
            }

            /// <summary>The context, or null for the plain group.</summary>
            internal string Context { get; }

            /// <summary>Whether this is the skeleton's own personality's group.</summary>
            internal bool Personal { get; }

            /// <summary>Where it starts in the flat list.</summary>
            internal int Offset { get; }

            /// <summary>How many lines it holds.</summary>
            internal int Length { get; }
        }
    }
}
