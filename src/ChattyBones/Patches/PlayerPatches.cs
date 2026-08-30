using System;
using ChattyBones.Logic;
using HarmonyLib;

namespace ChattyBones.Patches
{
    /// <summary>Names the things you pick up, eat and get better at.</summary>
    internal static class Doings
    {
        /// <summary>An item's own name, localized.</summary>
        /// <returns>Something like "Grilled Neck Tail", or null when it has not got one.</returns>
        /// <param name="item">The item to name.</param>
        /// <remarks>
        /// Null rather than empty, so LineTokens passes a line over rather than
        /// rendering a hole - the same reason as Hits.NameOf.
        /// </remarks>
        internal static string NameOf(ItemDrop.ItemData item)
        {
            if (item?.m_shared == null)
            {
                return null;
            }

            string name = Localization.instance.Localize(item.m_shared.m_name);

            return string.IsNullOrEmpty(name) ? null : name;
        }

        /// <summary>What to call a skill in a line.</summary>
        /// <returns>The localized name, e.g. "Blocking", or null.</returns>
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

            string name = Localization.instance.Localize("$skill_" + skill.ToString().ToLower());

            return string.IsNullOrEmpty(name) ? null : name;
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
    /// with the item data already in hand and already loaded, and only when the
    /// picker is a player.
    ///
    /// Nothing filters what is worth mentioning, and that is the design rather than
    /// an omission. A first version sorted items into notable and not - trophies,
    /// anything with a coin value, anything that does not stack - and it was wrong
    /// twice over. Wrong in detail, because it counted trophies as treasure when a
    /// chest of eleven greydwarf trophies is what most players actually have. And
    /// wrong in kind, because the budget is already a rate limiter and a far better
    /// one: Looted sits one rank above Idle, so it can only speak when nothing else
    /// is happening, and the squad gap decides how often that is.
    ///
    /// So every pickup is a candidate and almost none of them get through. The lines
    /// are written to work for whatever it happens to catch, which is usually
    /// something dull - and a skeleton being unimpressed by your fortieth piece of
    /// wood is funnier than one enthusing about a ruby.
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
    /// The game puts its own small message top-left when this fires, so the squad is
    /// talking over something rather than filling a silence. It survives that because
    /// a HUD number and somebody congratulating you are different registers - but it
    /// is the one event in the set where that is true, and worth remembering if it
    /// ever reads as redundant in play.
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

                _ = Chatter.SpeakAny(
                    ChatterEvent.PlayerSkilledUp,
                    subject: 0,
                    targetName: null,
                    companion: null,
                    details: new LineDetails(skill: Doings.NameOf(skill)));
            }
            catch (Exception e)
            {
                ChattyBonesPlugin.Log.LogWarning("ChattyBones stumbled over a skill: " + e);
            }
        }
    }
}
