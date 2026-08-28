using System.Collections.Generic;
using UnityEngine;

namespace ChattyBones
{
    /// <summary>Finds the skeletons this mod cares about.</summary>
    /// <remarks>
    /// A Dead Raiser skeleton is a tamed creature with a Tameable whose ZDO carries
    /// the summoner's name in <c>s_follow</c> - which is how SpawnAbility marks one
    /// when it calls <c>Tameable.Command</c>. No prefab names involved, so it keeps
    /// working if Iron Gate renames anything. It also matches any other tamed
    /// follower - a wolf you have told to heel counts - which has not been worth
    /// excluding yet, since Dead Raiser skeletons are what people are summoning.
    /// </remarks>
    internal static class Summons
    {
        /// <summary>Is this one of ours?</summary>
        /// <returns>True for a tamed creature that is following somebody.</returns>
        /// <param name="character">Any creature.</param>
        internal static bool IsSummoned(Character character)
        {
            if (character == null || !character.IsTamed())
            {
                return false;
            }

            ZNetView view = character.GetComponent<ZNetView>();
            if (view == null || !view.IsValid())
            {
                return false;
            }

            return character.GetComponent<Tameable>() != null
                && !string.IsNullOrEmpty(view.GetZDO().GetString(ZDOVars.s_follow));
        }

        /// <summary>The summoned skeleton nearest a point, if there is one in range.</summary>
        /// <returns>True if we found one.</returns>
        /// <param name="point">Usually the player.</param>
        /// <param name="maxDistance">How far to look.</param>
        /// <param name="found">The nearest one, or null.</param>
        /// <remarks>
        /// Walks <c>Character.GetAllCharacters()</c>, which is every loaded creature -
        /// fine for a console command, and not something to do every frame.
        /// </remarks>
        internal static bool TryFindNearest(Vector3 point, float maxDistance, out Character found)
        {
            found = null;
            float best = maxDistance * maxDistance;

            List<Character> all = Character.GetAllCharacters();
            for (int i = 0; i < all.Count; i++)
            {
                Character candidate = all[i];
                if (!IsSummoned(candidate))
                {
                    continue;
                }

                float distance = (candidate.transform.position - point).sqrMagnitude;
                if (distance < best)
                {
                    best = distance;
                    found = candidate;
                }
            }

            return found != null;
        }

        /// <summary>What to call this skeleton.</summary>
        /// <returns>Its given name, or its creature name if it has not got one.</returns>
        /// <param name="character">One of ours.</param>
        /// <remarks>
        /// Skeletons arrive named and players can rename them, both of which live in
        /// the ZDO's tamed-name field and sync to everyone. Tameable.GetHoverName
        /// already does the fallback and the UGC filtering, so we use it rather than
        /// reading the field ourselves.
        /// </remarks>
        internal static string NameOf(Character character)
        {
            if (character == null)
            {
                return string.Empty;
            }

            Tameable tameable = character.GetComponent<Tameable>();

            return tameable == null ? string.Empty : tameable.GetHoverName();
        }
    }
}
