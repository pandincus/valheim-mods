using System.Collections.Generic;
using UnityEngine;

namespace ChattyBones
{
    /// <summary>Finds the skeletons this mod cares about.</summary>
    /// <remarks>
    /// The obvious test is "tamed, and following somebody" - a Tameable whose ZDO
    /// carries the summoner's name in <c>s_follow</c>, which is how SpawnAbility
    /// marks one. I used that first and it is wrong twice over.
    ///
    /// It matches too much: any tamed follower qualifies, so a wolf you have told to
    /// heel would start reciting skeleton lines.
    ///
    /// And it matches too little, in two ways that both go unnoticed. RPC_Command
    /// blanks <c>s_follow</c> when you toggle a creature to "stay", so a skeleton
    /// holding position would go permanently mute. And <c>Tameable.Command</c> is a
    /// routed RPC, so the field is still empty during Awake - the field is false at
    /// exactly the moment a skeleton is summoned, and true on every zone reload
    /// afterwards, which is precisely backwards for a greeting.
    ///
    /// So we ask the prefab instead. Unsummon behaviour is what a summon has and a
    /// tamed animal does not: it wanders too far and vanishes, or you log out and it
    /// vanishes. That is a fact about the prefab, available synchronously, and
    /// nothing at runtime can clear it.
    /// </remarks>
    internal static class Summons
    {
        /// <summary>Is this one of ours?</summary>
        /// <returns>True for a creature that was summoned rather than tamed.</returns>
        /// <param name="character">Any creature. Null is fine.</param>
        /// <remarks>
        /// The ZNetView is fetched before anything else is asked, and that order
        /// matters: <c>Character.IsTamed</c> reaches into <c>m_nview</c> itself, and
        /// <c>Character.Awake</c> registers the character before assigning it. Since
        /// component Awake order within a GameObject is undefined, a hook on some
        /// other component's Awake can reach a Character whose nview is still null.
        /// </remarks>
        internal static bool IsSummoned(Character character)
        {
            if (character == null)
            {
                return false;
            }

            ZNetView view = character.GetComponent<ZNetView>();
            if (view == null || !view.IsValid())
            {
                return false;
            }

            Tameable tameable = character.GetComponent<Tameable>();

            return tameable != null
                && (tameable.m_unsummonDistance > 0f || tameable.m_unsummonOnOwnerLogoutSeconds > 0f);
        }

        /// <summary>The summoned skeleton nearest a point, if there is one in range.</summary>
        /// <returns>True if we found one.</returns>
        /// <param name="point">Usually the player.</param>
        /// <param name="maxDistance">How far to look.</param>
        /// <param name="found">The nearest one, or null.</param>
        /// <remarks>
        /// Walks <c>Character.GetAllCharacters()</c>, which is every loaded creature.
        /// Fine for a console command and much too slow for anything regular - when
        /// the skeletons start reacting on their own they should keep a list of
        /// themselves rather than have us search for them.
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
        ///
        /// Not free, mind: two ZDO reads, a filter pass, and a ZDO write when we are
        /// the owner and the name has no recorded author. Worth calling only when
        /// something is actually going to show the name.
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

        /// <summary>The prefab hash of a creature.</summary>
        /// <returns>The hash, or 0 if there is no live ZDO to read it from.</returns>
        /// <param name="character">Any creature. Null is fine.</param>
        /// <remarks>
        /// This is what the budget wants as a subject and what other clients want in
        /// order to name the thing themselves. It identifies a *kind* of creature -
        /// every greydwarf in the world shares one - which is the property that keeps
        /// the budget's subject map from growing without bound.
        /// </remarks>
        internal static int PrefabOf(Character character)
        {
            if (character == null)
            {
                return 0;
            }

            ZNetView view = character.GetComponent<ZNetView>();

            return view == null || !view.IsValid() ? 0 : view.GetZDO().GetPrefab();
        }

        /// <summary>What to call a creature inside a line.</summary>
        /// <returns>Its localised name, or null when it has not got one.</returns>
        /// <param name="character">Any creature. Null is fine.</param>
        /// <remarks>
        /// Localised on the machine that is going to read it, which is the point of
        /// sending prefab hashes between clients rather than words: a German player
        /// reads "Grauzwerg" where you read "Greydwarf", from the same broadcast.
        ///
        /// Localization lives in assembly_guiutils rather than assembly_valheim, which
        /// is worth knowing before going to look for it.
        /// </remarks>
        internal static string CreatureName(Character character)
        {
            if (character == null || string.IsNullOrEmpty(character.m_name))
            {
                return null;
            }

            return Localization.instance == null
                ? character.m_name
                : Localization.instance.Localize(character.m_name);
        }
    }
}
