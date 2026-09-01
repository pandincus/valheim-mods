using System;
using System.Collections.Generic;
using ChattyBones.Logic;

namespace ChattyBones
{
    /// <summary>Works out where a skeleton is, for the pack to filter lines on.</summary>
    /// <remarks>
    /// Each new context is a case here and a row in <see cref="EventKey"/>; everything
    /// between - the parsing, the fallback chain, the line numbering - is indifferent to
    /// which context is in play, which is what makes them arrive a couple at a time
    /// rather than all at once.
    ///
    /// <c>biome</c> is resolved from the *subject's* position rather than the player's.
    /// The remark is about the skeleton, so the skeleton is what the question is about -
    /// and a squad strung out across a border gets each member answering for itself,
    /// which is the better reading of "It's very open out here" anyway.
    ///
    /// <c>home</c> deliberately breaks that. It asks <c>Player.IsSafeInHome()</c>, which
    /// is a property of you rather than of the skeleton, so a skeleton left out in the
    /// woods says home lines while you are settled by the fire. That is the same answer
    /// the <c>AtHome</c> event already gives - it speaks for the whole squad off your
    /// state - and having the event and the context disagree about what "home" means
    /// would be much worse than having them agree on a slightly loose one.
    ///
    /// Only the owner ever calls this. Everyone else is handed a number and folds it
    /// against the same numbering, which is the arrangement that lets a context like
    /// <c>home</c> - which no other client could possibly evaluate - exist at all.
    /// </remarks>
    internal static class Contexts
    {
        /// <summary>The biome half of the answer, spelled once per biome.</summary>
        /// <remarks>
        /// The strings are cached rather than the finished list: caching whole answers
        /// would mean a dictionary entry per combination of three contexts, all to save
        /// one small allocation per utterance, and skeletons speak a few times a minute.
        /// </remarks>
        private static readonly Dictionary<Heightmap.Biome, string> BiomeNames = [];

        /// <summary>The time context for each band, spelled once.</summary>
        /// <remarks>
        /// Built from <see cref="TimeOfDay.All"/> rather than written out again. It was
        /// written out again, and a review pointed out that made three hand-copies of the
        /// same four words with nothing tying them together - and that this copy was read
        /// with a dictionary indexer on the per-utterance path, so a band added to one
        /// list and not this one would have thrown out of a skeleton trying to speak.
        /// </remarks>
        private static readonly Dictionary<string, string> TimeNames = BuildTimeNames();

        /// <summary>Spell out the context string for every band there is.</summary>
        /// <returns>Band name to "time=band".</returns>
        private static Dictionary<string, string> BuildTimeNames()
        {
            Dictionary<string, string> names = new(StringComparer.Ordinal);

            for (int i = 0; i < TimeOfDay.All.Length; i++)
            {
                names[TimeOfDay.All[i]] = "time=" + TimeOfDay.All[i];
            }

            return names;
        }

        /// <summary>What contexts this character currently satisfies.</summary>
        /// <returns>The contexts as "name=value", or null when there are none.</returns>
        /// <param name="subject">The skeleton about to say something.</param>
        /// <remarks>
        /// Null rather than an empty list for "nowhere in particular", because that is
        /// what <see cref="LineSpace.TrySelect"/> reads as "use the plain groups" - and
        /// a caller that gets it wrong gets the plain groups anyway.
        ///
        /// Each context is skipped on its own when the game cannot answer for it, rather
        /// than the whole answer being abandoned - a zone still loading has no biome, but
        /// the clock is perfectly well known.
        /// </remarks>
        internal static IReadOnlyList<string> For(Character subject)
        {
            if (subject == null)
            {
                return null;
            }

            List<string> contexts = [];

            AddBiome(subject, contexts);
            AddTime(contexts);
            AddHome(contexts);

            return contexts.Count == 0 ? null : contexts;
        }

        /// <summary>Say which biome the skeleton is standing in.</summary>
        /// <param name="subject">The skeleton.</param>
        /// <param name="contexts">The answer being built.</param>
        /// <remarks>
        /// <c>Biome.None</c> is what Heightmap answers while a zone is still loading,
        /// which is every portal trip and every login. Spelling it would give a context
        /// nothing can match; leaving it out falls back properly.
        /// </remarks>
        private static void AddBiome(Character subject, List<string> contexts)
        {
            Heightmap.Biome biome = Heightmap.FindBiome(subject.transform.position);

            if (biome == Heightmap.Biome.None)
            {
                return;
            }

            if (!BiomeNames.TryGetValue(biome, out string name))
            {
                name = "biome=" + biome;
                BiomeNames[biome] = name;
            }

            contexts.Add(name);
        }

        /// <summary>Say which quarter of the day it is.</summary>
        /// <param name="contexts">The answer being built.</param>
        private static void AddTime(List<string> contexts)
        {
            if (EnvMan.instance == null)
            {
                return;
            }

            contexts.Add(TimeNames[TimeOfDay.Band(EnvMan.instance.GetDayFraction())]);
        }

        /// <summary>Say whether you are properly settled at home.</summary>
        /// <param name="contexts">The answer being built.</param>
        /// <remarks>
        /// The same question the <c>AtHome</c> event asks, and deliberately without that
        /// event's hysteresis. The event needs it because it fires on a *transition* and
        /// would otherwise announce itself every time you stepped away from the fire.
        /// This is a *state*, and the worst a flicker can do is drop a skeleton back to
        /// its plain idle lines for one remark.
        ///
        /// Note that <c>m_safeInHome</c> includes <c>!IsSensed()</c>, so anything that
        /// notices you indoors turns this off for a second or two. That reads correctly -
        /// a skeleton with something creeping up on the hall is not having a cozy
        /// evening - and it is why the flicker is worth allowing rather than smoothing.
        /// </remarks>
        private static void AddHome(List<string> contexts)
        {
            if (Player.m_localPlayer == null)
            {
                return;
            }

            contexts.Add(Player.m_localPlayer.IsSafeInHome() ? "home=yes" : "home=no");
        }

        /// <summary>Which of a pack's contexts name something the game does not have.</summary>
        /// <returns>The unusable ones, ready to be complained about. Empty for a clean pack.</returns>
        /// <param name="pack">The pack just loaded.</param>
        /// <remarks>
        /// Only <c>biome</c> reaches here, and that is the whole reason the pass exists.
        /// <see cref="EventKey"/> checks a context's value against a written-out list as
        /// it parses, which catches <c>time=noon</c> against the line of the file it came
        /// from - but it cannot do that for biomes, because the list of those is a Unity
        /// type and the Logic half cannot see one. So a misspelled biome parses
        /// perfectly, matches nothing, and the group it tags is silent forever with no
        /// symptom at all. That failure has cost this mod a session already, under a
        /// different name.
        ///
        /// Reported rather than corrected. Guessing at what somebody meant is how a
        /// typo becomes a line nobody wrote.
        /// </remarks>
        internal static IReadOnlyList<string> Unusable(LinePack pack)
        {
            List<string> bad = [];

            if (pack == null)
            {
                return bad;
            }

            foreach (string context in pack.Contexts())
            {
                int equals = context.IndexOf('=');

                if (equals < 0)
                {
                    continue;
                }

                string what = context.Substring(0, equals);
                string value = context.Substring(equals + 1);

                // None and All are both excluded on purpose, and neither is a special
                // case so much as the same rule. Biome is a [Flags] enum, so it has two
                // named members that are not places: None is 0 and All is 0x37F. Enum
                // .IsDefined says yes to both - they are real members - while
                // Heightmap.FindBiome only ever answers with a single one, so a group
                // tagged with either can never fire. That is the exact thing this walk
                // exists to catch, and All is the likelier of the two to be typed,
                // because "All" is what somebody reaches for meaning "everywhere".
                //
                // The warning names both and points at a plain group, because "All" is
                // usually somebody reaching for "everywhere" and being told only that it
                // is wrong would not tell them what to write instead.
                if (what == "biome"
                    && (value == nameof(Heightmap.Biome.None)
                        || value == nameof(Heightmap.Biome.All)
                        || !Enum.IsDefined(typeof(Heightmap.Biome), value)))
                {
                    bad.Add(context);
                }
            }

            return bad;
        }
    }
}
