using System;
using System.Collections.Generic;

namespace ChattyBones.Logic
{
    /// <summary>
    /// Picks what a skeleton actually says, and never says it twice running.
    /// </summary>
    /// <remarks>
    /// Only the client owning a skeleton runs this; everyone else takes the line ref
    /// and does a stateless <see cref="LinePack.TryPick"/>.
    ///
    /// The no-repeat memory has to live here rather than in the pack. Clients see
    /// different subsets of what happens - a ZDO only replicates to clients with the
    /// zone loaded - so if each kept its own "heard lately" list they would skip
    /// different lines and the same skeleton would say two different things on two
    /// screens. The owner avoids repeats when it *chooses*; everyone else just looks
    /// the line ref up.
    ///
    /// One chooser serves the whole squad. Hearing a line twice running is just as
    /// tiresome from two different skeletons.
    /// </remarks>
    internal sealed class LineChooser
    {
        /// <summary>The last thing anybody said, or null before anything has been.</summary>
        /// <remarks>
        /// The template rather than the rendered text. "Get lost, {target}!" said
        /// about a greydwarf and then about a seeker is the same joke twice, and it
        /// should feel like one.
        /// </remarks>
        private string _lastSaid;

        /// <summary>Choose a line, and the line ref that reproduces it anywhere else.</summary>
        /// <returns>
        /// False when this skeleton has nothing it can say, and the caller should
        /// drop the whole thing without committing anything to the budget. Two ways
        /// that happens, and neither is an error: the pack has no lines for this
        /// personality and event, or every line it does have wants a token we cannot
        /// fill.
        /// </returns>
        /// <param name="pack">The owner's own pack. Other clients may well have a different one.</param>
        /// <param name="personality">Which character is speaking.</param>
        /// <param name="kind">What happened.</param>
        /// <param name="tokens">
        /// What we know at this moment. Used to skip a line we could not render - a
        /// {target} in an idle line, say - rather than discovering that after we have
        /// already told everybody to say it.
        /// </param>
        /// <param name="random">
        /// Where the starting point comes from. Passed in rather than owned so the
        /// tests can hand over a seeded Random and get the same answers every run.
        /// </param>
        /// <param name="lineRef">The line ref to broadcast, so others reach this same line.</param>
        /// <param name="line">The finished line, tokens filled in.</param>
        /// <remarks>
        /// Start at a random offset, walk the group, take the first line that renders
        /// and is not the one just said. Every line is examined at most once, so if a
        /// usable line exists we find it - which matters for a group where only one
        /// line in ten can be rendered right now.
        ///
        /// Repeating is allowed only when the line just said is the single usable one.
        /// Falling silent would be worse.
        /// </remarks>
        internal bool TryChoose(
            LinePack pack,
            string personality,
            ChatterEvent kind,
            LineTokens tokens,
            Random random,
            out int lineRef,
            out string line)
        {
            lineRef = 0;
            line = null;

            if (!pack.TryGetGroup(personality, kind, out IReadOnlyList<string> lines))
            {
                return false;
            }

            int count = lines.Count;
            int start = random.Next(0, count);

            string repeatLine = null;
            int repeatIndex = -1;

            for (int offset = 0; offset < count; offset++)
            {
                int index = (start + offset) % count;
                string template = lines[index];

                if (!tokens.TryRender(template, out string rendered))
                {
                    continue;
                }

                if (template == _lastSaid)
                {
                    // Hold on to it in case it turns out to be the only thing we can
                    // say, but keep looking first.
                    if (repeatIndex < 0)
                    {
                        repeatLine = rendered;
                        repeatIndex = index;
                    }

                    continue;
                }

                _lastSaid = template;
                lineRef = LineRefFor(index, count, random);
                line = rendered;
                return true;
            }

            if (repeatIndex < 0)
            {
                return false;
            }

            lineRef = LineRefFor(repeatIndex, count, random);
            line = repeatLine;
            return true;
        }

        /// <summary>Find a line ref that any client will fold back to this index.</summary>
        /// <returns>A value in 0..<see cref="Utterance.MaxLineRef"/> whose remainder by <paramref name="count"/> is <paramref name="index"/>.</returns>
        /// <param name="index">The line we chose, within its group.</param>
        /// <param name="count">How many lines the group holds.</param>
        /// <param name="random">Used to vary which of the many valid line refs we send.</param>
        /// <remarks>
        /// Any of <c>index, index + count, index + 2*count...</c> would do, and we
        /// take one at random. Sending the bare index would work too, but a listener
        /// with a bigger pack than ours would only ever reach its first few lines:
        /// index 2 of our 3 is <c>2 % 10 = 2</c> in their 10, every time.
        ///
        /// The guard covers a group bigger than the whole line-ref range, where no
        /// value can reach every index. Mirroring degrades to "a line" rather than
        /// "the same line".
        /// </remarks>
        private static int LineRefFor(int index, int count, Random random)
        {
            int cycles = (Utterance.MaxLineRef + 1) / count;

            return cycles <= 0
                ? index & Utterance.MaxLineRef
                : index + (count * random.Next(0, cycles));
        }
    }
}
