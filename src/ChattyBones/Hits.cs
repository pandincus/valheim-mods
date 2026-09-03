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
        /// <param name="hit">The blow, for its attacker and its skill.</param>
        /// <param name="damage">
        /// Its numbers, copied before vanilla consumed three of them. Not read off
        /// <paramref name="hit"/>, which no longer has them - see the prefix in
        /// CharacterPatches.
        /// </param>
        internal static LineDetails Of(HitData hit, HitData.DamageTypes damage)
        {
            if (hit == null)
            {
                return default;
            }

            return new LineDetails(
                weapon: WeaponName(hit),
                weaponSkill: SkillName(hit.m_skill),
                damage: DamageKind.Dominant(
                    damage.m_blunt,
                    damage.m_slash,
                    damage.m_pierce,
                    damage.m_fire,
                    damage.m_frost,
                    damage.m_lightning,
                    damage.m_poison,
                    damage.m_spirit));
        }

        /// <summary>Describe what somebody is holding, when there is no blow to read.</summary>
        /// <returns>The weapon and its kind, with no damage - nobody recorded a hit.</returns>
        /// <param name="character">Whoever is holding it.</param>
        /// <remarks>
        /// For the kill events, which are found by watching a target disappear rather
        /// than by catching a blow - so there is no HitData and never was one.
        ///
        /// The caveat that makes <see cref="WeaponName"/> unreliable does not apply
        /// here: a skeleton is handed one weapon when it is raised and never touches
        /// another, so what it is holding now is what it killed with.
        /// </remarks>
        internal static LineDetails WieldedBy(Character character)
        {
            if (character == null || character is not Humanoid humanoid)
            {
                return default;
            }

            ItemDrop.ItemData weapon = humanoid.GetCurrentWeapon();

            return weapon?.m_shared == null
                ? default
                : new LineDetails(weapon: NameOf(weapon), weaponSkill: SkillName(weapon.m_shared.m_skillType));
        }

        /// <summary>What the attacker is holding, by its own name.</summary>
        /// <returns>Something like "Mistwalker", or null when we cannot tell.</returns>
        /// <param name="hit">The blow, for its attacker.</param>
        /// <remarks>
        /// The weapon in hand *now*, which is not quite the weapon that landed this
        /// hit - an arrow arrives long after the bow was drawn, a thrown spear leaves
        /// the hand entirely, and nothing stops a swap mid-swing. <see cref="SkillName"/>
        /// is the more reliable of the two, though not exact either.
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

            return weapon?.m_shared == null ? null : NameOf(weapon);
        }

        /// <summary>An item's own name, as the key rather than the words.</summary>
        /// <returns>Something like "$item_sword_mistwalker", or null when it has not got one.</returns>
        /// <param name="item">The item to name.</param>
        /// <remarks>
        /// Unlocalized on purpose, and every other detail is the same. The key is what
        /// travels to the other players who can see the skeleton, so each of them
        /// resolves it in their own language - see <see cref="DetailWire"/>.
        /// <see cref="Mirror.Localize"/> turns it into words at the moment of speaking.
        ///
        /// Null rather than empty, so LineTokens passes the line over rather than
        /// rendering a hole - the same reason as SEManPatches.Named.
        /// </remarks>
        private static string NameOf(ItemDrop.ItemData item)
        {
            string name = item.m_shared.m_name;

            return string.IsNullOrEmpty(name) ? null : name;
        }

        /// <summary>Which weapon skill landed the blow.</summary>
        /// <returns>The game's name for it, or null for skills that are not a weapon.</returns>
        /// <param name="skill">The skill riding on the hit.</param>
        /// <remarks>
        /// The skill rides on the HitData itself, so this is what actually landed
        /// rather than whatever is in somebody's hands afterwards. One caveat: a
        /// creature with no weapon falls back to an attack item whose skill defaults
        /// to Swords, so it is honest about players and a guess about monsters.
        ///
        /// Pickaxes and woodcutting are left out - they are tools, not weapons.
        ///
        /// The name comes from <see cref="Patches.Doings.NameOf(Skills.SkillType)"/>
        /// rather than from a table here, so {weaponskill} and {skill} can never spell the
        /// same skill two different ways. A key spelled by hand would be a token that
        /// silently never renders, and none of this file is compiled by the tests.
        /// </remarks>
        private static string SkillName(Skills.SkillType skill)
        {
            return WeaponSkills.Contains(skill) ? Patches.Doings.NameOf(skill) : null;
        }

        /// <summary>The skills that mean somebody swung something.</summary>
        private static readonly HashSet<Skills.SkillType> WeaponSkills =
        [
            Skills.SkillType.Swords,
            Skills.SkillType.Knives,
            Skills.SkillType.Clubs,
            Skills.SkillType.Polearms,
            Skills.SkillType.Spears,
            Skills.SkillType.Axes,
            Skills.SkillType.Bows,
            Skills.SkillType.Crossbows,
            Skills.SkillType.ElementalMagic,
            Skills.SkillType.BloodMagic,
            Skills.SkillType.Unarmed,
        ];
    }
}
