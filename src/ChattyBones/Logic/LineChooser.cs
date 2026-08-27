using System;
using System.Collections.Generic;

namespace ChattyBones.Logic
{
    /// <summary>
    /// Picks what a skeleton actually says, and remembers enough not to repeat itself.
    /// </summary>
    /// <remarks>
    /// Only the client that owns a skeleton runs this. Everyone else takes the seed
    /// it settled on and does a plain <see cref="LinePack.TryPick"/> with no state
    /// of their own, which is the arrangement that keeps two players seeing the same
    /// line.
    ///
    /// That arrangement is the reason the "don't repeat yourself" memory lives here
    /// rather than at the point of picking a line, and it is worth spelling out
    /// because the obvious design is wrong. Suppose every client kept its own list
    /// of what it had heard lately and skipped a line that was on it. Two clients
    /// have different lists, because they see different subsets of what happens -
    /// you have been stood next to the squad for a minute, your friend just ran over
    /// from the next biome and a ZDO only replicates to clients with the zone
    /// loaded. The same seed arrives at both, you skip the line it lands on and slide
    /// to the next one, your friend does not, and now the same skeleton is saying
    /// two different things on two screens. Which is precisely what sending a seed
    /// was meant to avoid.
    ///
    /// So instead the owner does the avoiding when it *chooses* the seed: roll one,
    /// see which line that gives in its own pack, roll again if it has heard that
    /// one lately. Whatever it settles on is broadcast, and every client - the owner
    /// included - does the same stateless lookup. No-repeat still works, judged from
    /// the point of view of the player actually stood there watching, which is the
    /// right vantage point anyway. In single player, where the owner is the only
    /// viewer, it is exactly correct.
    ///
    /// Your friend might occasionally hear a line you heard two minutes ago. They
    /// have never heard it, so it is not a repeat for them.
    /// </remarks>
    internal sealed class LineChooser
    {
        /// <summary>The last few lines anyone said, newest last.</summary>
        /// <remarks>
        /// Templates rather than indices, and shared across the whole squad rather
        /// than kept per skeleton. Both follow from asking what the *player* would
        /// find repetitive: hearing "My bones are itchy" twice running is just as
        /// tiresome when it comes from two different skeletons, and a line reached
        /// through <see cref="LinePack.SharedPersonality"/> is the same line however
        /// many personalities can reach it.
        /// </remarks>
        private readonly Queue<string> _recent = new();

        private readonly int _memory;
        private readonly int _attempts;

        /// <summary>The very last thing said, which we work hardest to avoid repeating.</summary>
        /// <remarks>
        /// <see cref="_recent"/> already holds this, so why keep it twice? Because
        /// the two get treated differently when we run out of attempts. Saying
        /// something you heard four lines ago is barely noticeable; saying the same
        /// thing twice in a row is the exact effect this class exists to prevent, and
        /// it is worth giving up a little variety elsewhere to guarantee it.
        ///
        /// A test caught this. With a memory of 5 over a group of 6 lines, nearly
        /// every roll lands on something remembered, so we reach the fallback
        /// constantly - and the fallback used to be "whatever we rolled last", which
        /// perfectly happily handed back the line we had just said.
        /// </remarks>
        private string _lastSaid;

        /// <summary>Build a chooser.</summary>
        /// <param name="memory">
        /// How many recent lines to steer away from. Small on purpose - this is
        /// "don't say that again straight away", not "work through the whole pack
        /// before repeating". Set it near the size of a group and every roll starts
        /// failing, at which point you are just paying for attempts.
        /// </param>
        /// <param name="attempts">
        /// How many seeds to try before giving up and repeating something. There has
        /// to be a limit: a group with one line in it can never produce anything
        /// unheard, and neither can a group where every line is already in memory.
        /// Repeating is much better than saying nothing.
        /// </param>
        internal LineChooser(int memory = 5, int attempts = 8)
        {
            _memory = memory < 0 ? 0 : memory;
            _attempts = attempts < 1 ? 1 : attempts;
        }

        /// <summary>Choose a line, and the seed that reproduces it anywhere else.</summary>
        /// <returns>
        /// False when this skeleton has nothing it can say, and the caller should
        /// drop the whole thing. Two ways that happens, and neither is an error:
        /// the pack has no lines for this personality and event, or every line it
        /// does have wants a token we cannot fill.
        /// </returns>
        /// <param name="pack">The owner's own pack. Other clients may well have a different one.</param>
        /// <param name="personality">Which character is speaking.</param>
        /// <param name="kind">What happened.</param>
        /// <param name="tokens">
        /// What we know at this moment. Used to reject a line we could not render -
        /// a {target} in an idle line, say - rather than discovering that after we
        /// have already told everybody to say it.
        /// </param>
        /// <param name="random">
        /// Where seeds come from. Passed in rather than owned so the tests can hand
        /// over a seeded Random and get the same answers every run.
        /// </param>
        /// <param name="seed">The seed to broadcast, so others reach this same line.</param>
        /// <param name="line">The finished line, tokens filled in.</param>
        /// <remarks>
        /// Note what gets remembered: the template, not the rendered text. "Get lost,
        /// {target}!" said about a greydwarf and then about a seeker is the same joke
        /// twice, and it should feel like one.
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

            // Two grades of fallback, used only if every attempt turns up something
            // we have heard lately. "Stale" is a line from further back, which is
            // barely noticeable. "Repeat" is the line we said last, which is the one
            // thing we really do not want, and is taken only when nothing else came up.
            string staleTemplate = null;
            string staleLine = null;
            int staleSeed = 0;

            string repeatTemplate = null;
            string repeatLine = null;
            int repeatSeed = 0;

            for (int attempt = 0; attempt < _attempts; attempt++)
            {
                int candidate = random.Next(0, Utterance.MaxSeed + 1);

                if (!pack.TryPick(personality, kind, candidate, out string template))
                {
                    // Nothing for this personality and event at all, and another roll
                    // will not change that.
                    return false;
                }

                if (!tokens.TryRender(template, out string rendered))
                {
                    // This particular line wants something we do not have. A different
                    // line in the same group might not, so keep rolling - but do not
                    // let it become the fallback, because we cannot say it.
                    continue;
                }

                // The _lastSaid half is what makes "never twice in a row" hold even
                // with the memory turned off. When the memory is on, _recent already
                // contains it and this costs nothing.
                if (template != _lastSaid && !_recent.Contains(template))
                {
                    Remember(template);
                    seed = candidate;
                    line = rendered;
                    return true;
                }

                // Heard it lately. Hold on to it in case every remaining roll is also
                // something we have heard, because repeating beats falling silent -
                // but file it by how bad a repeat it would be.
                if (template == _lastSaid)
                {
                    repeatTemplate = template;
                    repeatLine = rendered;
                    repeatSeed = candidate;
                }
                else if (staleTemplate == null)
                {
                    staleTemplate = template;
                    staleLine = rendered;
                    staleSeed = candidate;
                }
            }

            if (staleTemplate != null)
            {
                Remember(staleTemplate);
                seed = staleSeed;
                line = staleLine;
                return true;
            }

            if (repeatTemplate != null)
            {
                Remember(repeatTemplate);
                seed = repeatSeed;
                line = repeatLine;
                return true;
            }

            return false;
        }

        /// <summary>Note that a line has just been used, dropping the oldest if we are full.</summary>
        /// <param name="template">The raw line, before tokens were filled in.</param>
        private void Remember(string template)
        {
            // Tracked even when the memory is switched off entirely, so that "never
            // twice in a row" holds regardless of how the memory is configured. It is
            // the one promise this class makes unconditionally.
            _lastSaid = template;

            if (_memory == 0)
            {
                return;
            }

            _recent.Enqueue(template);

            while (_recent.Count > _memory)
            {
                _ = _recent.Dequeue();
            }
        }
    }
}
