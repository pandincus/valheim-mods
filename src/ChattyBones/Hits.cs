using System.Collections.Generic;
using ChattyBones.Logic;

namespace ChattyBones
{
    /// <summary>Turns a blow into the words a line can use to describe it.</summary>
    /// <remarks>
    /// Needs a HitData and a Humanoid, so it cannot live under Logic/. The part that
    /// can is <see cref="DamageKind"/>.
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
        /// the hand entirely, and nothing stops a swap mid-swing. <see cref="TypeName"/>
        /// is the one that cannot be wrong.
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

            if (weapon?.m_shared == null)
            {
                return null;
            }

            // Empty rather than null is what an unnamed item gives back, and Localize
            // passes it straight through. A token has to be null to be refused, or the
            // line renders with a hole where the weapon should be.
            string name = Localization.instance.Localize(weapon.m_shared.m_name);

            return string.IsNullOrEmpty(name) ? null : name;
        }

        /// <summary>What kind of weapon landed the blow.</summary>
        /// <returns>A lower-case word, or null for skills that are not a weapon.</returns>
        /// <param name="skill">The skill riding on the hit.</param>
        /// <remarks>
        /// The skill rides on the HitData itself, so this is what actually landed
        /// rather than whatever is in somebody's hands afterwards. One caveat: a
        /// creature with no weapon falls back to an attack item whose skill defaults
        /// to Swords, so it is honest about players and a guess about monsters.
        ///
        /// Pickaxes and woodcutting are left out - they are tools, not weapons.
        /// </remarks>
        private static string TypeName(Skills.SkillType skill)
        {
            return WeaponWords.TryGetValue(skill, out string word) ? word : null;
        }

        /// <summary>What to call each weapon skill in a line.</summary>
        /// <remarks>A miss is null, which is most of the twenty-six.</remarks>
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
