using System;
using System.Collections.Generic;
using ChattyBones.Logic;

namespace ChattyBones
{
    /// <summary>Works out where a skeleton is, for the pack to filter lines on.</summary>
    /// <remarks>
    /// One context so far. Each new one is a case here and a name in
    /// <see cref="EventKey"/>; everything between - the parsing, the fallback chain,
    /// the line numbering - is indifferent to which context is in play, which is what
    /// makes them arrive one at a time rather than all at once.
    ///
    /// Resolved from the *subject's* position rather than the player's. The remark is
    /// about the skeleton, so the skeleton is what the question is about - and a
    /// squad strung out across a border gets each member answering for itself, which
    /// is the better reading of "It's very open out here" anyway.
    ///
    /// Only the owner ever calls this. Everyone else is handed a number and folds it
    /// against the same numbering, which is the arrangement that lets contexts a
    /// remote client could not possibly evaluate exist at all.
    /// </remarks>
    internal static class Contexts
    {
        /// <summary>The context list handed to a skeleton in each biome, built once each.</summary>
        /// <remarks>
        /// Cached because the lists are immutable, one per biome, and a squad standing
        /// in one place asks the same question every time it speaks. Sharing the array
        /// is safe precisely because nothing downstream writes to it.
        /// </remarks>
        private static readonly Dictionary<Heightmap.Biome, string[]> Cached = [];

        /// <summary>What contexts this character currently satisfies.</summary>
        /// <returns>The contexts as "name=value", or null when there are none.</returns>
        /// <param name="subject">The skeleton about to say something.</param>
        /// <remarks>
        /// Null rather than an empty list for "nowhere in particular", because that is
        /// what <see cref="LineSpace.TrySelect"/> reads as "use the plain groups" - and
        /// a caller that gets it wrong gets the plain groups anyway.
        ///
        /// <c>Biome.None</c> is what Heightmap answers while a zone is still loading,
        /// which is every portal trip and every login. Returning it would spell a
        /// context nothing can match; returning null falls back properly.
        /// </remarks>
        internal static IReadOnlyList<string> For(Character subject)
        {
            if (subject == null)
            {
                return null;
            }

            Heightmap.Biome biome = Heightmap.FindBiome(subject.transform.position);

            if (biome == Heightmap.Biome.None)
            {
                return null;
            }

            if (!Cached.TryGetValue(biome, out string[] contexts))
            {
                contexts = ["biome=" + biome];
                Cached[biome] = contexts;
            }

            return contexts;
        }

        /// <summary>Which of a pack's contexts name something the game does not have.</summary>
        /// <returns>The unusable ones, ready to be complained about. Empty for a clean pack.</returns>
        /// <param name="pack">The pack just loaded.</param>
        /// <remarks>
        /// <see cref="EventKey"/> can tell that <c>biome</c> is a context this version
        /// understands, but not that <c>Swamps</c> is not a biome - the list of those
        /// is a Unity type and the Logic half cannot see one. So a misspelled value
        /// parses perfectly, matches nothing, and the group it tags is silent forever
        /// with no symptom at all. That failure has cost this mod a session already,
        /// under a different name, and is why it is worth a pass at load.
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

                if (what == "biome" && !Enum.IsDefined(typeof(Heightmap.Biome), value))
                {
                    bad.Add(context);
                }
            }

            return bad;
        }
    }
}
