using System.Collections.Generic;
using UnityEngine;

namespace PassThroughSkeletts
{
    /// <summary>Makes summoned skeletons and players stop colliding with each other.</summary>
    /// <remarks>
    /// Valheim has no script that shoves characters apart. The pushing you feel is plain
    /// physics contact between two colliders (its own pushback code is for knockback from
    /// hits and nothing else). So the whole mod is one Unity call, <c>Physics.IgnoreCollision</c>,
    /// which switches off contact for one specific pair and leaves both bodies solid to
    /// everything else. Valheim itself uses it when you climb into a bed or onto a ship.
    ///
    /// Raycasts and overlap queries are not affected, which is what keeps everything else
    /// working: you can still hover over a Skelett to pet or rename it, and a Skelett
    /// can still hit and be hit.
    /// </remarks>
    internal static class Passthrough
    {
        /// <summary>
        /// Reused by every call, so the sweep does not allocate an array per Skelett ten times
        /// a second. Nothing else touches it, and Update is single threaded.
        /// </summary>
        private static readonly List<Collider> Colliders = [];

        /// <summary>Is this one of the skeletons a Dead Raiser summons?</summary>
        /// <returns>True for a creature that was summoned rather than tamed.</returns>
        /// <param name="character">Any creature.</param>
        /// <remarks>
        /// The same test ChattyBones uses, for the same reasons: a summon is a creature whose
        /// prefab says it will unsummon (it wanders too far, or you log out), and a tamed
        /// animal does not have that. It is a fact about the prefab, so it is true from the
        /// moment the Skelett exists and nothing at runtime can clear it. "Tamed and following
        /// somebody" is the obvious test and is wrong, because a Skelett told to stay stops
        /// following.
        ///
        /// Practically speaking (in vanilla Valheim) that is the Dead Raiser's skeletons and
        /// nothing else. Theoretically any summon with that behaviour qualifies, which is
        /// what I would want from a future one anyway.
        /// </remarks>
        internal static bool IsSummoned(Character character)
        {
            if (character == null)
            {
                return false;
            }

            Tameable tameable = character.GetComponent<Tameable>();

            return tameable != null
                && (tameable.m_unsummonDistance > 0f || tameable.m_unsummonOnOwnerLogoutSeconds > 0f);
        }

        /// <summary>
        /// Put every Skelett and every player into the wanted state, ignoring each other or not.
        /// </summary>
        /// <param name="ignore">
        /// True to make them pass through each other. False puts back anything we ignored,
        /// which is what turning <see cref="ModConfig.Enabled"/> off needs.
        /// </param>
        /// <remarks>
        /// Idempotent, and deliberately stateless: it asks Unity whether each pair is already
        /// ignoring each other and only changes the ones that are not. I considered keeping a
        /// list of the pairs we had done, and decided against it. Unity drops an ignore when a
        /// collider is disabled or destroyed, so a list would drift out of step with the
        /// truth the moment a Skelett is unloaded and comes back, and asking Unity cannot
        /// disagree with Unity.
        ///
        /// Every player is included, remote ones as well as the local one. Your client is the
        /// one simulating a Skelett you own, and a remote player's avatar is a physics body
        /// there too, so leaving them out would let somebody else's player shove your
        /// Skelett about on your screen.
        ///
        /// All of a Skelett's colliders are paired, not just its main capsule, in case it
        /// carries a second solid one. Hit detection is not harmed: it does not go through
        /// contacts.
        /// </remarks>
        internal static void Sweep(bool ignore)
        {
            List<Player> players = Player.GetAllPlayers();

            if (players.Count == 0)
            {
                return;
            }

            List<Character> all = Character.GetAllCharacters();

            for (int i = 0; i < all.Count; i++)
            {
                Character skelett = all[i];

                if (!IsSummoned(skelett))
                {
                    continue;
                }

                skelett.GetComponentsInChildren(true, Colliders);

                for (int p = 0; p < players.Count; p++)
                {
                    Collider body = players[p] == null ? null : players[p].GetCollider();

                    if (!Usable(body))
                    {
                        continue;
                    }

                    for (int c = 0; c < Colliders.Count; c++)
                    {
                        Collider theirs = Colliders[c];

                        if (Usable(theirs) && Physics.GetIgnoreCollision(body, theirs) != ignore)
                        {
                            Physics.IgnoreCollision(body, theirs, ignore);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// How many of a Skelett's pairs with players are currently being ignored, for
        /// <c>pts_status</c>.
        /// </summary>
        /// <param name="skelett">One of ours.</param>
        /// <param name="ignored">Pairs Unity is ignoring.</param>
        /// <param name="total">Pairs we could ignore: its colliders times the players.</param>
        internal static void Tally(Character skelett, out int ignored, out int total)
        {
            ignored = 0;
            total = 0;

            skelett.GetComponentsInChildren(true, Colliders);
            List<Player> players = Player.GetAllPlayers();

            for (int p = 0; p < players.Count; p++)
            {
                Collider body = players[p] == null ? null : players[p].GetCollider();

                if (!Usable(body))
                {
                    continue;
                }

                for (int c = 0; c < Colliders.Count; c++)
                {
                    if (!Usable(Colliders[c]))
                    {
                        continue;
                    }

                    total++;

                    if (Physics.GetIgnoreCollision(body, Colliders[c]))
                    {
                        ignored++;
                    }
                }
            }
        }

        /// <summary>Can Unity be asked to ignore this collider?</summary>
        /// <remarks>
        /// Unity complains, in the log and once per call, if you give IgnoreCollision a
        /// collider that is disabled or sits on an inactive object. A Skelett that is asleep
        /// or still spawning can have such colliders, and we would be asking every tenth of a
        /// second, so skip them and pick them up on a later sweep once they are live. Unity's
        /// == is the right test for the null half: a destroyed collider counts as absent.
        /// </remarks>
        private static bool Usable(Collider collider)
        {
            return collider != null && collider.enabled && collider.gameObject.activeInHierarchy;
        }
    }
}
