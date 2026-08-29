using System.Collections.Generic;
using ChattyBones.Logic;

namespace ChattyBones
{
    /// <summary>Turns a blow into the words a line can use to describe it.</summary>
    /// <remarks>
    /// The game half of the <c>{weapon}</c>, <c>{weapontype}</c> and <c>{damage}</c>
    /// tokens: everything here needs a HitData or a Humanoid, so none of it can live
    /// under Logic/. The deciding is next door - <see cref="DamageKind"/> picks the
    /// dominant damage type out of eleven numbers, and is tested against a table.
    /// </remarks>
    internal static class Hits
    {
        /// <summary>Describe a blow.</summary>
        /// <returns>What we could work out, with nulls for what we could not.</returns>
        /// <param name="hit">The blow, or null when there was not one.</param>
        internal static LineDetails Of(HitData hit)
        {
            if (hit == null)
            {
                return default;
            }

            return new LineDetails(
                weapon: WeaponName(hit),
                weaponType: TypeName(hit.m_skill),
                damage: DamageKind.Dominant(
                    hit.m_damage.m_blunt,
                    hit.m_damage.m_slash,
                    hit.m_damage.m_pierce,
                    hit.m_damage.m_fire,
                    hit.m_damage.m_frost,
                    hit.m_damage.m_lightning,
                    hit.m_damage.m_poison,
                    hit.m_damage.m_spirit));
        }

        /// <summary>What the attacker is holding, by its own name.</summary>
        /// <returns>Something like "Mistwalker", or null when we cannot tell.</returns>
        /// <param name="hit">The blow, for its attacker.</param>
        /// <remarks>
        /// The weapon in hand *now*, which is not quite the weapon that landed this
        /// hit - an arrow arrives long after the bow was drawn, a thrown spear leaves
        /// the hand entirely, and nothing stops a swap mid-swing. Good enough for a
        /// joke and not for anything else, which is why <see cref="TypeName"/> exists
        /// beside it and why the pack file says which of the two can lie.
        /// </remarks>
        private static string WeaponName(HitData hit)
        {
            if (!hit.HaveAttacker())
            {
                return null;
            }

            Character attacker = hit.GetAttacker();
            if (attacker == null || attacker is not Humanoid humanoid)
            {
                return null;
            }

            ItemDrop.ItemData weapon = humanoid.GetCurrentWeapon();

            return weapon?.m_shared == null
                ? null
                : Localization.instance.Localize(weapon.m_shared.m_name);
        }

        /// <summary>What kind of weapon landed the blow.</summary>
        /// <returns>A lower-case word, or null for skills that are not a weapon.</returns>
        /// <param name="skill">The skill riding on the hit.</param>
        /// <remarks>
        /// This one cannot be wrong: the skill travels on the HitData itself and is
        /// even serialized across the network, so it is always the thing that actually
        /// landed rather than whatever is in somebody's hands afterwards.
        ///
        /// Pickaxes and woodcutting are deliberately unnamed. They are tools, and a
        /// skeleton admiring your axework on a birch is not a line anybody wants.
        /// </remarks>
        private static string TypeName(Skills.SkillType skill)
        {
            return WeaponWords.TryGetValue(skill, out string word) ? word : null;
        }

        /// <summary>What to call each weapon skill in a line.</summary>
        /// <remarks>
        /// A lookup rather than a switch because SkillType has twenty-seven values -
        /// cooking and swimming among them - and the style rules want every arm of a
        /// switch spelled out. Eleven entries and a miss returning null says the same
        /// thing in a quarter of the space.
        ///
        /// Both magic skills answer "staff", which is what the player is holding.
        /// </remarks>
        private static readonly Dictionary<Skills.SkillType, string> WeaponWords = new()
        {
            [Skills.SkillType.Swords] = "sword",
            [Skills.SkillType.Knives] = "knife",
            [Skills.SkillType.Clubs] = "club",
            [Skills.SkillType.Polearms] = "polearm",
            [Skills.SkillType.Spears] = "spear",
            [Skills.SkillType.Axes] = "axe",
            [Skills.SkillType.Bows] = "bow",
            [Skills.SkillType.Crossbows] = "crossbow",
            [Skills.SkillType.ElementalMagic] = "staff",
            [Skills.SkillType.BloodMagic] = "staff",
            [Skills.SkillType.Unarmed] = "fists",
        };
    }
}
