using System;
using ChattyBones.Logic;
using HarmonyLib;
using UnityEngine;

namespace ChattyBones.Patches
{
    /// <summary>Notices you making something to eat or drink.</summary>
    /// <remarks>
    /// Two hooks for one event, because the game has two unrelated ways of turning
    /// ingredients into food and they share nothing.
    ///
    /// The cauldron is not a cooking station in the code's sense at all - it is a
    /// <c>CraftingStation</c>, and making a sausage runs the same path as forging a
    /// sword. The food preparation table and the mead ketill are
    /// <c>StationExtension</c>s hanging off it rather than stations of their own, so
    /// all three arrive here together and none of them needs naming.
    ///
    /// The cooking rack and the stone oven are the other family, <c>CookingStation</c>,
    /// and are deliberately not hooked. Putting raw meat on a rack is a chore rather
    /// than a moment, and the interesting half of that surface - burning it - is the
    /// only thing in this area that is an edge rather than an event. It can have its
    /// own change if it turns out to be wanted.
    /// </remarks>
    internal static class Cooking
    {
        /// <summary>How near the local player something has to be to be worth remarking on.</summary>
        /// <remarks>
        /// The fermenter tap runs on whoever owns the barrel, which in single player is
        /// you for every barrel currently loaded - so without this a squad reacts to a
        /// barrel finishing three zones away. This is the same mistake AtHome made
        /// twice, and it is cheaper to remember than to rediscover.
        ///
        /// Twenty metres because the skeletons are following you, so the question is
        /// really "could they see it happen".
        /// </remarks>
        private const float NearbyMetres = 20f;

        /// <summary>Is this close enough to the player for the squad to have noticed?</summary>
        /// <returns>True when it is within <see cref="NearbyMetres"/>.</returns>
        /// <param name="what">Whatever did the thing.</param>
        internal static bool Nearby(Component what)
        {
            return what != null
                && Player.m_localPlayer != null
                && Vector3.Distance(what.transform.position, Player.m_localPlayer.transform.position)
                    < NearbyMetres;
        }

        /// <summary>Offer it to the squad, and let the budget decide.</summary>
        /// <param name="item">What was made, already localized, or null.</param>
        internal static void React(string item)
        {
            if (!ModConfig.Enabled.Value || ChatterComponent.All.Count == 0)
            {
                return;
            }

            _ = Chatter.SpeakAny(
                ChatterEvent.PlayerCooked,
                subject: 0,
                targetName: null,
                companion: null,
                details: new LineDetails(item: item));
        }
    }

    /// <summary>Reacts to you cooking at a cauldron.</summary>
    /// <remarks>
    /// <c>m_craftingSkill</c> is how we tell a cauldron from a forge, and it is
    /// vanilla's own answer rather than a list of prefab names we would have to keep:
    /// every station declares the skill it trains, and DoCrafting already reads it to
    /// decide the craft bonus. Cooking food raises Cooking, so the filter agrees with
    /// the game by construction and picks up any station a future update tags the same
    /// way.
    ///
    /// The value lives in serialized prefab data rather than in code, so it cannot be
    /// checked by decompiling - it was confirmed in play instead.
    ///
    /// A postfix, which is worth a word because DoCrafting has a dozen early returns
    /// and a postfix runs after all of them alike. It is sound anyway: DoCrafting has
    /// exactly one caller, the craft timer, which only starts from a craft button that
    /// is enabled when the recipe is affordable - and the one late failure worth
    /// worrying about, no room in your inventory, is itself one of those early returns
    /// rather than something that goes wrong at the end. So reaching the postfix means
    /// the craft happened.
    /// </remarks>
    [HarmonyPatch(typeof(InventoryGui), "DoCrafting")]
    internal static class CauldronCraftPatch
    {
        /// <summary>
        /// Catch everything. An exception escaping here lands in the middle of
        /// vanilla's crafting, which is not ours to break.
        /// </summary>
        /// <param name="___m_craftRecipe">The recipe just made.</param>
        private static void Postfix(Recipe ___m_craftRecipe)
        {
            try
            {
                if (Player.m_localPlayer == null)
                {
                    return;
                }

                CraftingStation station = Player.m_localPlayer.GetCurrentCraftingStation();

                if (station == null || station.m_craftingSkill != Skills.SkillType.Cooking)
                {
                    return;
                }

                Cooking.React(Doings.NameOf(___m_craftRecipe?.m_item?.m_itemData));
            }
            catch (Exception e)
            {
                ChattyBonesPlugin.Log.LogWarning("ChattyBones stumbled over some cooking: " + e);
            }
        }
    }

    /// <summary>Reacts to you drawing mead off a fermenter.</summary>
    /// <remarks>
    /// <c>DelayedTap</c> rather than <c>Interact</c>, and the delay is the point. Interact
    /// only sends RPC_Tap; by the time this runs the conversion has been resolved, so
    /// <c>m_to</c> is the finished mead rather than the base that went in - which is
    /// the name worth saying. It also fires with the spawn effect, so the remark
    /// arrives as the bottles appear rather than as you press the key.
    ///
    /// Only the tap. Loading a barrel and the days of waiting afterwards are not
    /// moments, and a skeleton narrating fermentation would be a skeleton you mute.
    /// </remarks>
    [HarmonyPatch(typeof(Fermenter), "DelayedTap")]
    internal static class FermenterTapPatch
    {
        /// <summary>
        /// Catch everything, for the same reason the cauldron does.
        /// </summary>
        /// <param name="__instance">The barrel being tapped.</param>
        /// <param name="___m_delayedTapItem">What was fermenting, as a prefab name.</param>
        private static void Postfix(Fermenter __instance, string ___m_delayedTapItem)
        {
            try
            {
                if (!Cooking.Nearby(__instance))
                {
                    return;
                }

                Fermenter.ItemConversion conversion = __instance.m_conversion.Find(
                    c => c?.m_from != null && c.m_from.gameObject.name == ___m_delayedTapItem);

                Cooking.React(Doings.NameOf(conversion?.m_to?.m_itemData));
            }
            catch (Exception e)
            {
                ChattyBonesPlugin.Log.LogWarning("ChattyBones stumbled over a fermenter: " + e);
            }
        }
    }
}
