using System;
using System.Collections.Generic;

namespace ChattyBones.Logic
{
    /// <summary>
    /// Picks what a skeleton actually says, and never says it twice running.
    /// </summary>
    /// <remarks>
    /// Only the client that owns a skeleton runs this. Everyone else takes the seed
    /// it settled on and does a plain <see cref="LinePack.TryPick"/> with no state
    /// of their own, which is the arrangement that keeps two players seeing the same
    /// line.
    ///
    /// That arrangement is the reason the "don't repeat yourself" memory lives here
    /// rather than at the point of picking a line, and it is worth spelling out
    /// because the obvious design is wrong. Suppose every client kept its own note
    /// of what it had heard and skipped a line that matched. Two clients hold
    /// different notes, because they see different subsets of what happens - you
    /// have been stood next to the squad for a minute, your friend just ran over
    /// from the next biome and a ZDO only replicates to clients with the zone
    /// loaded. The same seed arrives at both, you skip the line it lands on and
    /// slide to the next one, your friend does not, and now the same skeleton is
    /// saying two different things on two screens. Which is precisely what sending a
    /// seed was meant to avoid.
    ///
    /// So instead the owner does the avoiding when it *chooses*, and broadcasts a
    /// seed that reproduces its choice. No-repeat is judged from the point of view
    /// of the player actually stood there watching, which is the right vantage point
    /// anyway. In single player, where the owner is the only viewer, it is exactly
    /// correct.
    ///
    /// One of these serves the whole squad. Hearing "My bones are itchy" twice
    /// running is just as tiresome when two different skeletons say it, so Phase 4
    /// must share a single chooser rather than giving each skeleton its own.
    ///
    /// This used to remember the last several lines and roll seeds until it found an
    /// unheard one. That was more code, one more config knob, and worse: with eight
    /// rolls there was a small chance of every roll landing on the line just said,
    /// so the one promise the class made held about 99.7% of the time. Asking the
    /// pack how many lines there are and walking them costs less and makes the
    /// promise structural. If it ever feels repetitive with a real pack in real play,
    /// dealing from a shuffled deck is the natural next step - but that is a
    /// judgement to make against actual gameplay, not a hunch.
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

        /// <summary>Choose a line, and the seed that reproduces it anywhere else.</summary>
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
        /// <param name="seed">The seed to broadcast, so others reach this same line.</param>
        /// <param name="line">The finished line, tokens filled in.</param>
        /// <remarks>
        /// We start at a random offset and walk the whole group from there, taking
        /// the first line that renders and is not the one just said. Every line is
        /// examined at most once, so this cannot fail through bad luck the way
        /// rolling seeds could: if any usable line exists, we find it.
        ///
        /// That matters most for a group where, say, one line in ten is renderable
        /// and the other nine want a {target} we have not got. Rolling ten times and
        /// missing was entirely possible, and the skeleton would go silent for no
        /// reason a player could ever work out.
        ///
        /// Repeating is allowed only as a last resort, when the line just said is the
        /// single usable one in the group. Falling silent would be worse.
        /// </remarks>
        internal bool TryChoose(
            LinePack pack,
            string personality,
            ChatterEvent kind,
            LineTokens tokens,
            Random random,
            out int seed,
            out string line)
        {
            seed = 0;
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
                seed = SeedFor(index, count, random);
                line = rendered;
                return true;
            }

            if (repeatIndex < 0)
            {
                return false;
            }

            seed = SeedFor(repeatIndex, count, random);
            line = repeatLine;
            return true;
        }

        /// <summary>Find a seed that any client will fold back to this index.</summary>
        /// <returns>A value in 0..<see cref="Utterance.MaxSeed"/> whose remainder by <paramref name="count"/> is <paramref name="index"/>.</returns>
        /// <param name="index">The line we chose, within its group.</param>
        /// <param name="count">How many lines the group holds.</param>
        /// <param name="random">Used to vary which of the many valid seeds we send.</param>
        /// <remarks>
        /// Receiving clients compute <c>seed % theirCount</c>, so we cannot simply
        /// send the index - we send a number that lands on it. Any of
        /// <c>index, index + count, index + 2*count...</c> will do, and we pick among
        /// them at random so the value on the wire is not trivially the index. That
        /// costs nothing and means a pack with three lines is not forever sending
        /// 0, 1 and 2.
        ///
        /// A client whose pack has a *different* number of lines lands somewhere else
        /// entirely, which is the intended behaviour - it gets a sensible line out of
        /// its own file rather than an index that means nothing there.
        ///
        /// The guard is for a group with more than 65,536 lines in it, where no seed
        /// can encode every index. Mirroring degrades to "a line" rather than "the
        /// same line", which seems a reasonable thing to do for a pack that large.
        /// </remarks>
        private static int SeedFor(int index, int count, Random random)
        {
            int cycles = (Utterance.MaxSeed + 1) / count;

            return cycles <= 0
                ? index & Utterance.MaxSeed
                : index + (count * random.Next(0, cycles));
        }
    }
}
