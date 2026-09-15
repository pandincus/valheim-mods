using System.Collections.Generic;
using BubbleBar.Logic;

namespace BubbleBar
{
    /// <summary>
    /// Everything that needs a <c>Character</c> to answer. Flattens the game's objects
    /// into a <see cref="ShieldReading"/> and hands that to the bars.
    /// </summary>
    /// <remarks>
    /// The split is the same one FishQualityBonus makes with <c>FishBonus</c>: game types
    /// stop here, and what crosses into <c>Logic/</c> is a pair of floats.
    /// </remarks>
    internal static class Shields
    {
        /// <summary>Find the barrier on a character, if it has one we can see.</summary>
        /// <returns>True when <paramref name="shield"/> was set.</returns>
        /// <param name="character">Who to look at.</param>
        /// <param name="shield">The barrier found, or null.</param>
        /// <remarks>
        /// Only the owner of a character runs its status effects at all, so on every other
        /// client this returns false however obvious the bubble looks. That is not a bug to
        /// work around here - it is what <see cref="Mirror"/> exists for, and
        /// <see cref="Read"/> is where the two sources are chosen between.
        /// </remarks>
        internal static bool TryGetShield(Character character, out SE_Shield shield)
        {
            shield = null;

            if (character == null || character.m_nview == null || !character.m_nview.IsValid()
                || !character.m_nview.IsOwner())
            {
                return false;
            }

            SEMan seman = character.GetSEMan();

            if (seman == null)
            {
                return false;
            }

            // A hand-rolled loop rather than LINQ, because this runs once per visible
            // character per frame. The list is a handful of entries.
            List<StatusEffect> effects = seman.GetStatusEffects();

            for (int i = 0; i < effects.Count; i++)
            {
                if (effects[i] is SE_Shield found)
                {
                    shield = found;
                    return true;
                }
            }

            return false;
        }

        /// <summary>How much of a character's barrier is left.</summary>
        /// <returns>The reading, or <see cref="ShieldReading.None"/> when there is none to see.</returns>
        /// <param name="character">Who to look at.</param>
        /// <remarks>
        /// Two sources, and which one applies is decided by who owns the character rather than
        /// by trying one and falling back. That distinction matters: a character we own with no
        /// barrier must read as None, not as whatever we last published about it - our own stale
        /// value would otherwise keep drawing a bar for the seconds before the deadline passed.
        /// </remarks>
        internal static ShieldReading Read(Character character)
        {
            if (TryGetShield(character, out SE_Shield shield))
            {
                return ShieldReading.For(shield.m_damage, shield.m_totalAbsorbDamage);
            }

            if (character == null || character.m_nview == null || !character.m_nview.IsValid()
                || character.m_nview.IsOwner())
            {
                return ShieldReading.None;
            }

            if (!ModConfig.ShowOthersBarriers.Value)
            {
                return ShieldReading.None;
            }

            return Mirror.Read(character.m_nview.GetZDO());
        }

        /// <summary>Whether we should draw a bar over this character at all.</summary>
        /// <returns>True for the player's own side.</returns>
        /// <param name="viewer">The local player.</param>
        /// <param name="character">Who is being drawn.</param>
        /// <remarks>
        /// <c>BaseAI.IsEnemy</c> is the same question <c>EnemyHud</c> asks when it picks
        /// between the red health bar and the friendly one, so a barrier bar appears on
        /// exactly the characters that already get a friendly-colored health bar.
        /// </remarks>
        internal static bool IsAlly(Player viewer, Character character)
        {
            if (character == null)
            {
                return false;
            }

            if (viewer == null)
            {
                return true;
            }

            return !BaseAI.IsEnemy(viewer, character);
        }
    }
}
