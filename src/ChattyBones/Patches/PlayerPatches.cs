using System;
using ChattyBones.Logic;
using HarmonyLib;

namespace ChattyBones.Patches
{
    /// <summary>Names the things you pick up, eat and get better at.</summary>
    internal static class Doings
    {
        /// <summary>An item's own name, as the key rather than the words.</summary>
        /// <returns>Something like "$item_necktailgrilled", or null when it has not got one.</returns>
        /// <param name="item">The item to name.</param>
        /// <remarks>
        /// Unlocalized, like every other detail, so it can travel and be resolved in
        /// the reader's own language - see <see cref="Mirror.Localize"/>.
        ///
        /// Null rather than empty, so LineTokens passes a line over rather than
        /// rendering a hole - the same reason as Hits.NameOf.
        /// </remarks>
        internal static string NameOf(ItemDrop.ItemData item)
        {
            if (item?.m_shared == null)
            {
                return null;
            }

            string name = item.m_shared.m_name;

            return string.IsNullOrEmpty(name) ? null : name;
        }

        /// <summary>What to call a skill in a line.</summary>
        /// <returns>Its key, e.g. "$skill_blocking", or null.</returns>
        /// <param name="skill">The skill that went up.</param>
        /// <remarks>
        /// Vanilla's own spelling, taken from the message Skills.RaiseSkill puts on
        /// screen one line away from the call we hook: the enum name lower-cased
        /// behind a <c>$skill_</c> prefix.
        /// </remarks>
        internal static string NameOf(Skills.SkillType skill)
        {
            if (skill == Skills.SkillType.None)
            {
                return null;
            }

            return "$skill_" + skill.ToString().ToLowerInvariant();
        }
    }

    /// <summary>Reacts to you picking anything up.</summary>
    /// <remarks>
    /// Hooked on ShowPickupMessage rather than on Pickup itself, and that is worth
    /// explaining because Pickup looks like the obvious place. By the time Pickup
    /// returns it has already called <c>ZNetScene.instance.Destroy(go)</c>, so a
    /// postfix asking the GameObject for its ItemDrop gets a destroyed component -
    /// the same trap that made Character.OnDeath unusable as a postfix in phase 4.
    ///
    /// ShowPickupMessage sidesteps it entirely: vanilla calls it from inside Pickup
    /// with the item data already in hand and already loaded, and only when the picker
    /// is a player.
    ///
    /// It has a second caller, though, and it is not a pickup: StoreGui.BuySelectedItem
    /// uses the same message to tell you what you just bought. "Finders keepers" about
    /// something you paid Haldor for is wrong, so the shop being open is a guard.
    ///
    /// Nothing filters what is worth mentioning. Every pickup is a candidate and the
    /// budget decides: at rank 12 Looted only speaks when the field is quiet, so
    /// emptying a chest of forty things produces about one remark. The lines are
    /// written for whatever that happens to catch, which is usually something dull -
    /// a skeleton unimpressed by your fortieth piece of wood is funnier than one
    /// enthusing about a ruby, and needs no notion of what a ruby is.
    /// </remarks>
    [HarmonyPatch(typeof(Character), "ShowPickupMessage")]
    internal static class CharacterPickupPatch
    {
        /// <summary>
        /// Catch everything. Harmony does not wrap a patch body, and an exception here
        /// would land in the middle of vanilla telling you what you just picked up.
        /// </summary>
        /// <param name="__instance">Whoever picked it up.</param>
        /// <param name="item">What they picked up.</param>
        private static void Postfix(Character __instance, ItemDrop.ItemData item)
        {
            try
            {
                React(__instance, item);
            }
            catch (Exception e)
            {
                ChattyBonesPlugin.Log.LogWarning("ChattyBones stumbled over a pickup: " + e);
            }
        }

        /// <summary>Offer it to the squad, and let the budget decide.</summary>
        /// <param name="picker">Whoever picked it up.</param>
        /// <param name="item">What they picked up.</param>
        private static void React(Character picker, ItemDrop.ItemData item)
        {
            if (!ModConfig.Enabled.Value || item?.m_shared == null)
            {
                return;
            }

            if (Player.m_localPlayer == null || picker != Player.m_localPlayer)
            {
                return;
            }

            if (StoreGui.IsVisible())
            {
                return;
            }

            // Auto-pickup routes through here, so this is the busiest path the mod
            // has. Localizing the name before asking whether anybody can speak would
            // do that work on every stick and stone - see the ordering note on
            // Chatter.TrySpeak, which this is the one place that could get wrong.
            if (ChatterComponent.All.Count == 0)
            {
                return;
            }

            _ = Chatter.SpeakAny(
                ChatterEvent.Looted,
                subject: 0,
                targetName: null,
                companion: null,
                details: new LineDetails(item: Doings.NameOf(item)));
        }
    }

    /// <summary>Reacts to you eating something.</summary>
    /// <remarks>
    /// Self-pacing, which is why it needs no threshold: three food slots on roughly
    /// twenty-minute timers means it comes round on the game's schedule.
    ///
    /// The return value is the whole guard. EatFood is called whenever you try, and
    /// refuses when the slots are full or the food is already at its cap - so
    /// speaking on a false would congratulate you for a meal you did not have.
    /// </remarks>
    [HarmonyPatch(typeof(Player), "EatFood")]
    internal static class PlayerEatPatch
    {
        /// <summary>
        /// Catch everything, so a failure here cannot stop vanilla feeding you.
        /// </summary>
        /// <param name="__instance">Whoever is eating.</param>
        /// <param name="item">What they ate.</param>
        /// <param name="__result">False when the food was refused.</param>
        private static void Postfix(Player __instance, ItemDrop.ItemData item, bool __result)
        {
            try
            {
                if (!ModConfig.Enabled.Value || !__result || __instance == null)
                {
                    return;
                }

                if (Player.m_localPlayer == null || __instance != Player.m_localPlayer)
                {
                    return;
                }

                _ = Chatter.SpeakAny(
                    ChatterEvent.PlayerAte,
                    subject: 0,
                    targetName: null,
                    companion: null,
                    details: new LineDetails(item: Doings.NameOf(item)));
            }
            catch (Exception e)
            {
                ChattyBonesPlugin.Log.LogWarning("ChattyBones stumbled over a meal: " + e);
            }
        }
    }

    /// <summary>Reacts to you getting better at something.</summary>
    /// <remarks>
    /// Records through <see cref="Blow"/> rather than speaking, and that is not
    /// optional. Blocking is raised from inside <c>Humanoid.BlockAttack</c>, which
    /// RPC_Damage calls - so a parry that levels Blocking fires this mid-blow, and
    /// speaking there would take the moment from the parry, the injury and the kill
    /// alike, at rank 19. Blocking levels on your first block, so it is not a corner
    /// case. Recording lets the rank decide instead, which is what the ranks are for.
    ///
    /// A level-up outside a fight has no blow to end it, and is picked up by
    /// <see cref="Blow.FlushStale"/> on the next tick.
    ///
    /// The game shows its own message when a skill goes up, so the squad is talking
    /// over something rather than filling a silence - centre-screen for the first
    /// level of a skill, top-left after that. It survives because a number and a
    /// compliment are different registers, and it is the only event here of which
    /// that is true.
    /// </remarks>
    [HarmonyPatch(typeof(Player), "OnSkillLevelup")]
    internal static class PlayerSkillPatch
    {
        /// <summary>
        /// Catch everything. Harmony does not wrap a patch body, and this one sits in
        /// the middle of vanilla spawning the level-up effect.
        /// </summary>
        /// <param name="__instance">Whoever levelled.</param>
        /// <param name="skill">Which skill went up.</param>
        private static void Postfix(Player __instance, Skills.SkillType skill)
        {
            try
            {
                if (!ModConfig.Enabled.Value || __instance == null)
                {
                    return;
                }

                if (Player.m_localPlayer == null || __instance != Player.m_localPlayer)
                {
                    return;
                }

                Blow.Note(
                    __instance,
                    ChatterEvent.PlayerSkilledUp,
                    subject: 0,
                    targetName: null,
                    new LineDetails(skill: Doings.NameOf(skill)));
            }
            catch (Exception e)
            {
                ChattyBonesPlugin.Log.LogWarning("ChattyBones stumbled over a skill: " + e);
            }
        }
    }
}
