using System.Collections.Generic;
using UnityEngine;

namespace ChattyBones
{
    /// <summary>Remembers who last landed a blow somebody could be blamed for.</summary>
    /// <remarks>
    /// A kill is credited from <c>Character.m_lastHit</c>, which is vanilla's own answer
    /// and is right nearly all the time. The exception is what this exists for:
    /// <c>m_lastHit</c> is not "the blow that mattered", it is the last ApplyDamage that
    /// got past the 0.1 threshold, and several very ordinary damage sources build a bare
    /// HitData with nobody attached - burning, poison, fall damage, Ashlands heat, lava.
    ///
    /// That would be a curiosity if those only came from the world, and they do not.
    /// Character.Damage lifts the fire, poison and spirit off an incoming blow and hands
    /// them to a status effect, which then ticks with no attacker at all - so a fire
    /// arrow that finishes a greydwarf leaves m_lastHit naming nobody. Before this, every
    /// such kill went completely unremarked: no Killed, no CompanionKilled, and no
    /// PlayerGotAKill either. Poison arrows, fire arrows, and anything at all that burns
    /// to death in the Ashlands.
    ///
    /// So we keep the last blow that *did* name somebody and fall back to it. The hook is
    /// Character.RPC_Damage, which is where an attributed blow arrives; the tick damage
    /// goes straight to ApplyDamage and never comes through here, which is exactly the
    /// filter we want rather than something we had to write.
    ///
    /// Kept small on purpose. Entries are dropped when the death that wanted them
    /// arrives, and anything nobody came back for goes stale on its own - so this holds
    /// what is currently being fought and not a record of the afternoon.
    /// </remarks>
    internal static class Blame
    {
        /// <summary>How long a blow still counts as the reason something died.</summary>
        /// <remarks>
        /// Generous, because the gap it covers is a creature burning down over several
        /// seconds after you stopped shooting it. Too short and the fire kills we are
        /// here to catch fall through anyway; too long and a greydwarf that wandered off,
        /// healed, and drowned a minute later gets pinned on you. Fifteen seconds is
        /// longer than a burn and much shorter than a grudge.
        /// </remarks>
        private const float RememberSeconds = 15f;

        /// <summary>How many we tolerate before looking for stale ones.</summary>
        /// <remarks>
        /// A trigger for tidying rather than a cap: nothing is thrown away for being
        /// numerous, only for being old. So an ordinary fight never walks the dictionary
        /// at all, and a big one - a raid, or the Ashlands, where you may own most of
        /// what is nearby - walks it on every blow and can sit above this number for as
        /// long as the fighting lasts. That is a few dozen entries and a few dozen
        /// comparisons, which is nothing next to the damage handling it rides on; it is
        /// bounded by how many creatures can be loaded at once rather than by anything
        /// here, and <see cref="Clear"/> empties it when the world goes away.
        /// </remarks>
        private const int Crowd = 64;

        private static readonly Dictionary<ZDOID, Entry> Recent = [];

        /// <summary>Reused so tidying up does not allocate on a combat path.</summary>
        private static readonly List<ZDOID> Stale = [];

        /// <summary>Note that somebody hit somebody else.</summary>
        /// <param name="victim">Whoever was hit.</param>
        /// <param name="hit">The blow, for its attacker.</param>
        internal static void Note(Character victim, HitData hit)
        {
            if (victim == null || hit == null || !hit.HaveAttacker())
            {
                return;
            }

            // Unity's ==, because the question is whether there is an attacker to name
            // rather than which one it is.
            Character attacker = hit.GetAttacker();
            if (attacker == null)
            {
                return;
            }

            ZDOID id = victim.GetZDOID();
            if (id == ZDOID.None)
            {
                return;
            }

            Tidy();

            Recent[id] = new Entry(attacker, Time.time);
        }

        /// <summary>Who last hit this one, if anybody did and it was recent.</summary>
        /// <returns>The attacker, or null if there is nothing worth blaming.</returns>
        /// <param name="victim">Whoever just died.</param>
        /// <remarks>
        /// Always forgets, whether or not it answers. The caller is a death, so there is
        /// no second chance to ask and nothing left to remember it for.
        /// </remarks>
        internal static Character Take(Character victim)
        {
            if (victim == null)
            {
                return null;
            }

            ZDOID id = victim.GetZDOID();

            if (id == ZDOID.None || !Recent.TryGetValue(id, out Entry entry))
            {
                return null;
            }

            _ = Recent.Remove(id);

            // Unity's == again: an attacker that has since been destroyed is no more use
            // to us than one that was never named, and asking it anything would throw.
            return Time.time - entry.At > RememberSeconds || entry.Attacker == null
                ? null
                : entry.Attacker;
        }

        /// <summary>Forget everything. For a world change, where every id is about to mean nothing.</summary>
        internal static void Clear()
        {
            Recent.Clear();
        }

        /// <summary>Drop anything too old to be the reason for a death.</summary>
        private static void Tidy()
        {
            if (Recent.Count < Crowd)
            {
                return;
            }

            float now = Time.time;

            Stale.Clear();

            foreach (KeyValuePair<ZDOID, Entry> entry in Recent)
            {
                if (now - entry.Value.At > RememberSeconds)
                {
                    Stale.Add(entry.Key);
                }
            }

            for (int i = 0; i < Stale.Count; i++)
            {
                _ = Recent.Remove(Stale[i]);
            }

            Stale.Clear();
        }

        /// <summary>One remembered blow.</summary>
        private readonly struct Entry
        {
            /// <summary>Record a blow.</summary>
            /// <param name="attacker">Who swung it.</param>
            /// <param name="at">When, in game time.</param>
            internal Entry(Character attacker, float at)
            {
                Attacker = attacker;
                At = at;
            }

            /// <summary>Who swung it. May have been destroyed since.</summary>
            internal Character Attacker { get; }

            /// <summary>When, in game time.</summary>
            internal float At { get; }
        }
    }
}
