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
    /// sword. The food preparation table and the mead ketill are stations in their own
    /// right, and all three tag their recipes with the Cooking skill - so asking the
    /// recipe which skill it trains picks up all of them without naming any.
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
        /// <param name="item">What was made, or null.</param>
        /// <remarks>
        /// Takes the item rather than its name so that the localizing happens on this
        /// side of the guard. Handing a caller a string to build means the caller does
        /// that work whether or not anybody is listening, and both call sites got it
        /// wrong the same way - which is a sign the parameter was the wrong shape
        /// rather than that the call sites were careless.
        /// </remarks>
        internal static void React(ItemDrop.ItemData item)
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
                details: new LineDetails(item: Doings.NameOf(item)));
        }
    }

    /// <summary>Reacts to you cooking at a cauldron.</summary>
    /// <remarks>
    /// <c>m_craftingSkill</c> is how we tell a cauldron from a forge, and it is
    /// vanilla's own answer rather than a list of prefab names we would have to keep:
    /// every station declares the skill it trains, and picks up any station a future
    /// update tags the same way.
    ///
    /// Read off the *recipe's* station rather than the one the player has open, and
    /// that distinction is the whole guard. A recipe needing no station is craftable
    /// anywhere and is listed alongside the food, while the open cauldron stays the
    /// player's current station until they close the panel - so asking the player
    /// meant arrows crafted at the cauldron read as cooking. Vanilla settles it the
    /// same way a few lines further down DoCrafting, where the skill it raises comes
    /// off the recipe.
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
                CraftingStation station = ___m_craftRecipe?.m_craftingStation;

                if (station == null || station.m_craftingSkill != Skills.SkillType.Cooking)
                {
                    return;
                }

                Cooking.React(___m_craftRecipe.m_item?.m_itemData);
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

                Cooking.React(conversion?.m_to?.m_itemData);
            }
            catch (Exception e)
            {
                ChattyBonesPlugin.Log.LogWarning("ChattyBones stumbled over a fermenter: " + e);
            }
        }
    }
}
