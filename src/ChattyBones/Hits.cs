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

            Character attacker = hit.HaveAttacker() ? hit.GetAttacker() : null;
            bool believable = Describable(attacker);

            return new LineDetails(
                weapon: believable ? WeaponName(attacker) : null,
                weaponSkill: believable ? SkillName(hit.m_skill) : null,
                damage: Dominant(damage));
        }

        /// <summary>Describe what a blow was made of, and deliberately nothing else.</summary>
        /// <returns>The damage type alone.</returns>
        /// <param name="damage">Its numbers, copied before vanilla consumed three of them.</param>
        /// <remarks>
        /// For the events about a blow coming *in* - Hurt, PlayerHurt, CompanionHurt -
        /// which promise the damage type and nothing more. <see cref="Of"/> would also
        /// hand over the weapon whenever the attacker happens to be a player, which is
        /// PvP and nothing else; no line may legally use it there, so all it would do is
        /// supply a token the event never promised and set cb_tokens complaining about
        /// drift it cannot act on.
        /// </remarks>
        internal static LineDetails DamageOf(HitData.DamageTypes damage)
        {
            return new LineDetails(damage: Dominant(damage));
        }

        /// <summary>Which of the eight damage types was the biggest.</summary>
        /// <returns>The game's name for it, or null when the blow was all zeroes.</returns>
        /// <param name="damage">The blow's numbers.</param>
        private static string Dominant(HitData.DamageTypes damage)
        {
            return DamageKind.Dominant(
                damage.m_blunt,
                damage.m_slash,
                damage.m_pierce,
                damage.m_fire,
                damage.m_frost,
                damage.m_lightning,
                damage.m_poison,
                damage.m_spirit);
        }

        /// <summary>Describe what somebody is holding, when there is no blow to read.</summary>
        /// <returns>The weapon and its kind, with no damage - nobody recorded a hit.</returns>
        /// <param name="character">Whoever is holding it.</param>
        /// <remarks>
        /// For <see cref="ChatterEvent.PlayerGotAKill"/>, which is the only caller left
        /// now that <see cref="Describable"/> refuses everybody but a player - the death
        /// hook hands us the victim rather than the blow that finished it, so there is no
        /// HitData to read.
        ///
        /// It carries the same approximation <see cref="WeaponName"/> does, and for the
        /// same reason - more so, if anything, since a player swaps weapons constantly.
        /// Shoot something with a bow, switch to a sword as it closes, and the arrow that
        /// lands reports the sword.
        ///
        /// <see cref="Blame"/> widened that window a good deal, and knowingly: a kill can
        /// now be credited up to fifteen seconds after the blow that earned it, because
        /// that is how long a creature may take to burn down. The weapon named is still
        /// the one in hand at the moment it dies. Worth knowing before writing a line that
        /// leans hard on the weapon being the one that did it.
        /// </remarks>
        internal static LineDetails WieldedBy(Character character)
        {
            // Unity's == first, and it is load-bearing: the type tests that follow are
            // plain C#, and a *destroyed* Player passes both of them - so without this
            // we reach GetCurrentWeapon on a dead object and throw.
            if (character == null || character is not Humanoid humanoid || !Describable(character))
            {
                return default;
            }

            ItemDrop.ItemData weapon = humanoid.GetCurrentWeapon();

            return weapon?.m_shared == null
                ? default
                : new LineDetails(weapon: NameOf(weapon), weaponSkill: SkillName(weapon.m_shared.m_skillType));
        }

        /// <summary>Will the game describe this one's weapon honestly?</summary>
        /// <returns>True only for a player.</returns>
        /// <param name="wielder">Whoever is holding it. Null is fine.</param>
        /// <remarks>
        /// Only a player, and it is worth saying why so plainly, because the obvious
        /// reading is that we are being precious about monsters. We are not - Valheim
        /// fills these fields in for player equipment and leaves them as they came for
        /// a creature's, and both of them lie.
        ///
        /// The name is a hand-typed leftover: a Skelett's sword is called "Dragur axe",
        /// as a raw string rather than a $key, so it neither reads right nor
        /// translates. And the skill is worse, because it looks plausible -
        /// ItemDrop's SharedData declares m_skillType = Swords as its initializer
        /// rather than None, so an untagged item reports Swords for ever. A Skelett's
        /// *bow* reports Swords. That one is not a judgement call: a bow is not a
        /// sword, and the field is simply empty.
        ///
        /// Both were seen in play, and cb_gear prints the evidence if it is ever worth
        /// re-checking - if Iron Gate tidies these up, this whole gate can go.
        /// </remarks>
        private static bool Describable(Character wielder)
        {
            return wielder is Player;
        }

        /// <summary>What the attacker is holding, by its own name.</summary>
        /// <returns>Something like "Mistwalker", or null when we cannot tell.</returns>
        /// <param name="attacker">Whoever swung, or null if the blow named nobody.</param>
        /// <remarks>
        /// The weapon in hand *now*, which is not quite the weapon that landed this
        /// hit - an arrow arrives long after the bow was drawn, a thrown spear leaves
        /// the hand entirely, and nothing stops a swap mid-swing.
        /// </remarks>
        private static string WeaponName(Character attacker)
        {
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
        /// <param name="skill">The skill riding on the hit, or on the item in hand.</param>
        /// <remarks>
        /// Only ever reached for a player - see <see cref="Describable"/>, which is
        /// where the interesting half of this now lives.
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
