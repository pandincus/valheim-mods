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
        /// <param name="contexts">
        /// Where this skeleton is, as the pack spells it, or null for nowhere in
        /// particular. Optional because the great majority of callers - and every test
        /// written before contexts existed - have no context to offer and want the
        /// plain groups, which is exactly what null gives them.
        /// </param>
        /// <remarks>
        /// Start at a random offset, walk the window, take the first line that renders
        /// and is not the one just said. Every line in both windows is examined at most
        /// once, so if a usable line exists we find it - which matters for a group where
        /// only one line in ten can be rendered right now.
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
            out string line,
            IReadOnlyList<string> contexts = null)
        {
            lineRef = 0;
            line = null;

            if (!pack.SelectBands(
                    personality, kind, contexts,
                    out LineSpace space,
                    out LineSpace.Window own,
                    out LineSpace.Window shared))
            {
                return false;
            }

            Order(own, shared, random, out LineSpace.Window first, out LineSpace.Window second);

            IReadOnlyList<string> all = space.All;
            int total = space.Count;

            string repeatLine = null;
            int repeatAt = -1;

            for (int band = 0; band < 2; band++)
            {
                LineSpace.Window window = band == 0 ? first : second;

                if (window.IsEmpty)
                {
                    continue;
                }

                int count = window.Length;
                int from = random.Next(0, count);

                for (int offset = 0; offset < count; offset++)
                {
                    // Walking the window the context chose, but numbering against the
                    // whole space - a listener folds the ref against every line this
                    // personality could reach for this event, because working out which
                    // window applied would mean resolving a context it may not see.
                    int index = window.Offset + ((from + offset) % count);
                    string template = all[index];

                    if (!tokens.TryRender(template, out string rendered))
                    {
                        continue;
                    }

                    if (template == _lastSaid)
                    {
                        // Hold on to it in case it turns out to be the only thing we can
                        // say, but keep looking first.
                        if (repeatAt < 0)
                        {
                            repeatLine = rendered;
                            repeatAt = index;
                        }

                        continue;
                    }

                    _lastSaid = template;
                    lineRef = LineRefFor(index, total, random);
                    line = rendered;
                    return true;
                }
            }

            if (repeatAt < 0)
            {
                return false;
            }

            lineRef = LineRefFor(repeatAt, total, random);
            line = repeatLine;
            return true;
        }

        /// <summary>How much of the time a skeleton with lines of its own uses them.</summary>
        /// <remarks>
        /// A share rather than a per-line weight, and that is the whole design. Weighting
        /// each personality line by some multiple would leave the *count* deciding how
        /// strongly a character comes through, so an author would still have to ask "have
        /// I written enough of these to drown out common?" - which is exactly the question
        /// that produced twenty-six groups quietly reducing their own variance. A share
        /// makes presence the thing that counts: two good cowardly lines are as dominant
        /// as eight.
        ///
        /// One caveat, and it only bites at exactly one line. The no-repeat rule below
        /// outranks this, so a personality with a *single* line of its own cannot say it
        /// twice running and reaches for the shared band instead - measured at 41% rather
        /// than 70%, against 69.9% for two lines and every count above. That is the rule
        /// rescuing a one-line group from being a broken record rather than a fault, but
        /// "about 70%" is not true down there and it is worth saying so.
        ///
        /// 0.7 is a starting guess, and deliberately a constant rather than a setting. A
        /// probability dial over line selection is hard to describe to a player and easy
        /// to set badly, and nothing yet says what the right number is.
        /// </remarks>
        private const double PersonalityShare = 0.7;

        /// <summary>Decide which band to try first, and keep the other as the follow-up.</summary>
        /// <param name="own">The personality's window.</param>
        /// <param name="shared">The shared window.</param>
        /// <param name="random">Where the roll comes from.</param>
        /// <param name="first">The band to walk first.</param>
        /// <param name="second">The band to fall through to, possibly empty.</param>
        /// <remarks>
        /// The personality gets <see cref="PersonalityShare"/> of the time, or its
        /// natural share of the lines when that is larger - so a well-stocked
        /// personality is not dragged *down* to 70% by two shared lines. Both directions
        /// of the trap are closed by that one max: writing few lines cannot cost you the
        /// shared ones, and writing many cannot be undone by them.
        ///
        /// Worked through: two cowardly lines against five common ones gives 0.7 rather
        /// than the natural 0.29, so each cowardly line is said about 35% of the time and
        /// each common one about 6%. Eight cowardly against two common gives the natural
        /// 0.8 instead, and the two common lines stay a tail at 10% each.
        ///
        /// Losing the roll is not losing the line. The band that goes second is still
        /// walked when the first has nothing renderable, so a token we cannot fill costs
        /// variety rather than silence.
        /// </remarks>
        private static void Order(
            LineSpace.Window own,
            LineSpace.Window shared,
            Random random,
            out LineSpace.Window first,
            out LineSpace.Window second)
        {
            if (own.IsEmpty || shared.IsEmpty)
            {
                first = own.IsEmpty ? shared : own;
                second = default;
                return;
            }

            double natural = (double)own.Length / (own.Length + shared.Length);
            bool personalityFirst = random.NextDouble() < Math.Max(PersonalityShare, natural);

            first = personalityFirst ? own : shared;
            second = personalityFirst ? shared : own;
        }

        /// <summary>Find a line ref that any client will fold back to this index.</summary>
        /// <returns>A value in 0..<see cref="Utterance.MaxLineRef"/> whose remainder by <paramref name="count"/> is <paramref name="index"/>.</returns>
        /// <param name="index">The line we chose, as a position in the whole numbering.</param>
        /// <param name="count">How many lines the whole numbering holds.</param>
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
