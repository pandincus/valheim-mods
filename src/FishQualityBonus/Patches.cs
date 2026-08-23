using HarmonyLib;

namespace FishQualityBonus
{
    /// <summary>
    /// We patch the recipe GetAmount functionality to hand out the extra fish-made items!
    ///
    /// Valheim already knows how to scale output by fish quality, but only for recipes
    /// flagged m_requireOnlyOneIngredient. That flag is a general Recipe feature meaning
    /// "takes one of any ingredient from a list"; of the recipes that consume a fish, only
    /// Fish (raw) uses it. And in vanilla, any recipe pairing a fish with something else,
    /// like Fish 'n' Bread, does not get that benefit. Hence the purpose of this mod:
    /// we fill that gap for the non-flagged recipes (so we don't double-count).
    /// </summary>
    [HarmonyPatch(typeof(Recipe), nameof(Recipe.GetAmount))]
    internal static class Patch_Recipe_GetAmount
    {
        /// <summary>
        /// Runs after vanilla's GetAmount and adds our bonus to whatever it worked out.
        /// </summary>
        /// <param name="__instance">
        /// Harmony's name for the Recipe whose method we're wrapping.
        /// </param>
        /// <param name="quality">
        /// The quality of the item being *crafted*, not of the fish. Always 1 for food.
        /// </param>
        /// <param name="craftMultiplier">
        /// 1 normally, N (5?) when multi-crafting. We have to abide by this so that we
        /// handle multi-crafting properly, though the label might be wrong in some cases.
        /// (see <see cref="Patch_InventoryGui_UpdateRecipe"/>).
        /// </param>
        /// <param name="__result">
        /// Harmony's name for the method's return value. Declared ref so we can add to it.
        /// </param>
        private static void Postfix(Recipe __instance, int quality, int craftMultiplier, ref int __result)
        {
            if (!ModConfig.Enabled.Value) return;
            if (__instance.m_requireOnlyOneIngredient) return;

            Player player = Player.m_localPlayer;
            if (player == null) return;

            FishChoice choice = FishBonus.Choose(player.GetInventory(), __instance.m_resources,
                                                 quality, craftMultiplier);
            int bonus = FishBonus.BonusFor(__instance, choice);
            if (bonus <= 0) return;

            // Vanilla returns amount * craftMultiplier, so our per-craft bonus
            // has to be multiplied the same way.
            __result += bonus * craftMultiplier;
        }
    }

    /// <summary>
    /// Decides which fish actually gets eaten.
    ///
    /// Vanilla calls RemoveItem with quality -1, which takes whichever fish you picked up
    /// first. Therefore we have to patch the consumption, or else we might charge you
    /// for a big fish and spend a small one, or the other way around.
    /// </summary>
    /// <remarks>
    /// This has to reach the same answer <see cref="Patch_Recipe_GetAmount"/> reached a
    /// moment earlier.
    /// 
    /// We could have gone a stateful route here, storing information after GetAmount, then using
    /// that data here. But adding state is messy; maybe something else happened in-between, the craft
    /// was somehow abandoned, etc. So we instead opt for a stateless approach, and compute the same
    /// answer again using the underlying <see cref="FishBonus.Choose(Inventory, Piece.Requirement[], int, int)"/>
    /// functionality. Therefore, both functions compute again from the inventory.
    /// 
    /// This SHOULD be safe. In what's present in vanilla code, DoCrafting calls GetAmount and then
    /// ConsumeResources with only one inventory change in between (AddItem for the crafted
    /// result, which is never a fish) so both patches read the same counts.
    /// </remarks>
    [HarmonyPatch(typeof(Player), nameof(Player.ConsumeResources))]
    internal static class Patch_Player_ConsumeResources
    {
        /// <summary>
        /// Runs instead of vanilla's ConsumeResources, but only for a fish recipe.
        /// </summary>
        /// <returns>
        /// True to let vanilla's version run as normal, false to skip it because we already
        /// did the work ourselves.
        /// </returns>
        /// <remarks>
        /// Replacing a method outright is the most invasive kind of Harmony patch, but this is narrow.
        /// We hand control back (return true) in three cases, which covers the majority of craft consumption:
        /// 1. The mod is switched off
        /// 2. The caller already picked a quality to consume
        /// 3. The recipe has no single fish in it (every other craft and building piece)
        ///
        /// We only take over for a recipe with exactly one kind of fish. Other mods patching
        /// this same method are unaffected outside that case.
        /// </remarks>
        /// <param name="__instance">Harmony's name for the Player doing the crafting.</param>
        /// <param name="requirements">The recipe's ingredient list.</param>
        /// <param name="qualityLevel">The quality of the item being crafted.</param>
        /// <param name="itemQuality">Which quality of ingredient to consume, or -1 for "any" (what Vanilla passes).</param>
        /// <param name="multiplier">1 normally, or 5 when multi-crafting.</param>
        private static bool Prefix(Player __instance, Piece.Requirement[] requirements,
                                   int qualityLevel, int itemQuality, int multiplier)
        {
            if (!ModConfig.Enabled.Value) return true;

            // The caller already picked a quality, so don't second-guess it.
            if (itemQuality >= 0) return true;

            Inventory inventory = __instance.GetInventory();
            FishChoice choice = FishBonus.Choose(inventory, requirements, qualityLevel, multiplier);
            if (choice == null) return true;   // no fish here, so let vanilla run

            // The same loop vanilla runs, except the fish is taken at the qualities
            // we based the payout on.
            foreach (Piece.Requirement req in requirements)
            {
                if (!req.m_resItem) continue;

                int amount = req.GetAmount(qualityLevel) * multiplier;
                if (amount <= 0) continue;

                string name = req.m_resItem.m_itemData.m_shared.m_name;
                if (!ReferenceEquals(req, choice.Requirement))
                {
                    inventory.RemoveItem(name, amount, itemQuality);
                    continue;
                }

                // One call per quality the plan draws on. Usually that is a single
                // call, exactly as before; a mixed craft makes one per size.
                for (int quality = 0; quality < choice.Plan.Length; quality++)
                {
                    if (choice.Plan[quality] > 0) inventory.RemoveItem(name, choice.Plan[quality], quality);
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Let a craft through when the only thing stopping it is that your fish are
    /// different sizes.
    ///
    /// This is the fix for the case that started the feature: two trollfish, one quality-1
    /// and one quality-2, and a Troll Endurance mead that wants two of them. Vanilla refuses,
    /// because its requirement check looks at your biggest single-quality stack rather than
    /// your total. The ingredient list disagrees with it and shows 2 of 2 in white, so the
    /// Craft button just sits there greyed out with nothing explaining why.
    ///
    /// See <see cref="FishBonus.CanCraftMixed"/> for the vanilla code and why it is written
    /// that way.
    /// </summary>
    [HarmonyPatch(typeof(Player), "HaveRequirementItems")]
    internal static class Patch_Player_HaveRequirementItems
    {
        /// <summary>
        /// Runs after vanilla's check and can only ever turn a no into a yes.
        /// </summary>
        /// <param name="__instance">Harmony's name for the Player doing the crafting.</param>
        /// <param name="piece">
        /// The recipe being checked. Vanilla's own parameter name, and it really is a
        /// Recipe rather than a Piece.
        /// </param>
        /// <param name="discover">
        /// True when the game is asking "should this recipe be visible at all", which is
        /// about materials the player has ever seen and never about how many they hold.
        /// That branch reads no quantities, so there is nothing here for us to fix.
        /// </param>
        /// <param name="qualityLevel">The quality of the item being crafted, not of the fish.</param>
        /// <param name="amount">1 normally, or the multi-craft amount when shift is held.</param>
        /// <param name="__result">
        /// Harmony's name for the return value. Declared ref so we can flip it.
        /// </param>
        /// <remarks>
        /// Patching the innermost check rather than HaveRequirements means the Craft button
        /// and the craft itself cannot disagree - every caller goes through here.
        ///
        /// A postfix that only fires on false is about as small as this patch can be. We
        /// never take craftability away, we never run when the mod is switched off, and a
        /// recipe we don't handle is left exactly as vanilla left it.
        /// </remarks>
        private static void Postfix(Player __instance, Recipe piece, bool discover,
                                    int qualityLevel, int amount, ref bool __result)
        {
            if (__result) return;                  // vanilla is happy, nothing to do
            if (!ModConfig.Enabled.Value) return;
            if (discover) return;

            if (FishBonus.CanCraftMixed(__instance.GetInventory(), piece, qualityLevel, amount))
            {
                __result = true;
            }
        }
    }

    /// <summary>
    /// Display the projected amount we'll get in the crafting panel.
    ///
    /// The label is built straight from Recipe.m_amount rather than from GetAmount, so
    /// without this you would receive the extra items with nothing on screen saying so.
    /// </summary>
    [HarmonyPatch(typeof(InventoryGui), "UpdateRecipe")]
    internal static class Patch_InventoryGui_UpdateRecipe
    {
        /// <summary>
        /// Runs after vanilla has written the recipe name, and rewrites it to show the real
        /// total. Only touches the label when there is actually a bonus to report.
        /// </summary>
        /// <param name="__instance">
        /// Harmony's name for the InventoryGui. We read m_selectedRecipe, m_recipeName and
        /// m_multiCraftAmount off it.
        /// </param>
        /// <param name="player">
        /// The player object so we can fetch inventory and choose the right fish for computing
        /// the bonus that they'll get (based on current settings).
        /// Note that you should be able to switch mod config on the fly (e.g. smallest first,
        /// largest first) and the label should update in real-time.
        /// </param>
        private static void Postfix(InventoryGui __instance, Player player)
        {
            if (!ModConfig.Enabled.Value) return;
            if (player == null || __instance.m_recipeName == null) return;

            Recipe recipe = __instance.m_selectedRecipe.Recipe;
            if (recipe == null || recipe.m_item == null) return;
            if (recipe.m_requireOnlyOneIngredient) return;

            // An upgrade rather than a craft, so nothing is multiplied.
            if (__instance.m_selectedRecipe.ItemData != null) return;

            // Determine if the player is holding down the craft-multiplier key (e.g. left-shift by default)
            int craftMultiplier = CurrentCraftMultiplier(__instance);

            FishChoice choice = FishBonus.Choose(player.GetInventory(), recipe.m_resources,
                                                 1, craftMultiplier);
            int bonus = FishBonus.BonusFor(recipe, choice);
            // If there's no bonus for this recipe, do nothing!
            if (bonus <= 0) return;

            // Otherwise, compute the bonus, multiply it out if needed, and display the new total!
            int total = (recipe.m_amount + bonus) * craftMultiplier;
            string name = Localization.instance.Localize(recipe.m_item.m_itemData.m_shared.m_name);
            __instance.m_recipeName.text = name + " x" + total;
        }

        /// <summary>
        /// Work out how many crafts the player is asking for at this exact moment.
        /// </summary>
        /// <returns>
        /// The multi-craft amount (5 in vanilla) while the multi-craft key is held down,
        /// otherwise 1.
        /// </returns>
        /// <param name="gui">The crafting panel, which is where the multi-craft amount lives.</param>
        /// <remarks>
        /// This duplicates InventoryGui.UpdateRecipe, which works the same thing out
        /// but doesn't provide a public accessor for it and the data is only kept in a local variable
        /// We also can't get this from the m_multiCrafting field, since it is  only set in OnCraftPressed
        /// and so describes the craft in progress, not whether the key is held right now.
        ///
        /// These strings used here (AltPlace, JoyLStick) are ZInput action names, not keys.
        /// AltPlace defaults to Left Shift and players can rebind it freely, so keybinds can't break this.
        /// Only Iron Gate renaming the action could, and an unknown name just returns false, so it would
        /// fail quietly.
        ///
        /// The impact of this failing is only in the label writing; we don't use this to compute anything
        /// critical about the recipe itself.
        /// </remarks>
        private static int CurrentCraftMultiplier(InventoryGui gui)
        {
            bool multiCrafting = ZInput.GetButton("AltPlace") || ZInput.GetButton("JoyLStick");
            return multiCrafting ? gui.m_multiCraftAmount : 1;
        }
    }
}
