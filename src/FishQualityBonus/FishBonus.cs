using System.Collections.Generic;

namespace FishQualityBonus
{
    /// <summary>The fish a craft is about to spend, and at which qualities.</summary>
    internal sealed class FishChoice
    {
        public Piece.Requirement Requirement;

        /// <summary>
        /// Which fish to spend, and at which qualities. Usually one quality; more than
        /// one when the craft is mixing sizes.
        /// </summary>
        public FishPlan Plan;

        public int TotalNeeded;
    }

    /// <summary>
    /// This class acts as the bridge between the game's objects and the plain logic in BonusRules.
    /// 
    /// This just turns a Recipe or an Inventory into ordinary numbers and hands
    /// them over, so that the decisions themselves stay separate and testable.
    /// Every method is conversion; take arguments and config, and we return the internal type.
    /// 
    /// The one piece of state is the parsed ExcludedRecipes cache,
    /// which we rebuild whenever that setting's string changes.
    /// </summary>
    internal static class FishBonus
    {
        private static HashSet<string> _excluded = new HashSet<string>();
        private static string _excludedRaw;

        /// <summary>
        /// Compute which fish a craft should spend, and at which quality.
        /// </summary>
        /// <returns>
        /// The fish and qualities to spend, or null when we should keep out of it entirely.
        /// We exit in three cases, and vanilla then handles the craft as usual:
        /// 1. The recipe uses no fish at all (not relevant to our mod!)
        /// 2. The recipe uses more than one kind of fish (see <see cref="TryGetSingleFishRequirement"/>)
        /// 3. There aren't enough fish to cover the craft - which, with AllowMixedQualities
        ///    off, means no single quality has enough on its own
        /// </returns>
        /// <param name="inventory">
        /// The player's inventory (read-only here). Consuming happens in <see cref="Patch_Player_ConsumeResources"/>.
        /// </param>
        /// <param name="requirements">
        /// Everything the recipe needs (Recipe.m_resources), including non-fish requirements.
        /// We pick the single fish out of it and ignore other ingredients.
        /// </param>
        /// <param name="qualityLevel">
        /// The quality of the item being *crafted*, not of the fish. Food is always 1;
        /// this only goes above 1 when upgrading equipment, which we never touch here.
        /// Passed through to Piece.Requirement.GetAmount, which vanilla uses to charge
        /// alternative amounts for higher upgrade tiers.
        /// </param>
        /// <param name="multiplier">
        /// How many crafts at once: 1 normally, or 5 when multi-crafting (hold Left Shift).
        /// </param>
        internal static FishChoice Choose(Inventory inventory, Piece.Requirement[] requirements,
                                          int qualityLevel, int multiplier)
        {
            if (inventory == null || requirements == null) return null;

            if (!TryGetSingleFishRequirement(requirements, out Piece.Requirement fishReq)) return null;

            int needed = fishReq.GetAmount(qualityLevel) * multiplier;
            if (needed <= 0) return null;

            int[] counts = CountByQuality(inventory, fishReq);

            bool largestFirst = ModConfig.FishToSpend.Value == FishPreference.LargestFirst;
            if (!BonusRules.TryPickFish(counts, needed, largestFirst,
                                        ModConfig.AllowMixedQualities.Value, out FishPlan plan))
            {
                return null;
            }

            return new FishChoice
            {
                Requirement = fishReq,
                Plan = plan,
                TotalNeeded = needed,
            };
        }

        /// <summary>
        /// Count how many fish of each quality the player is carrying.
        /// </summary>
        /// <returns>
        /// An array indexed by quality, so [2] is the number of quality-2 fish. Index 0 is
        /// kept even though no real fish has quality 0, so that index and quality always
        /// match - and because vanilla scans from 0 as well.
        /// </returns>
        /// <param name="inventory">The player's inventory, read only.</param>
        /// <param name="fishReq">The recipe's fish requirement, which tells us what to count.</param>
        private static int[] CountByQuality(Inventory inventory, Piece.Requirement fishReq)
        {
            ItemDrop.ItemData.SharedData fish = fishReq.m_resItem.m_itemData.m_shared;

            int maxQuality = fish.m_maxQuality < 1 ? 1 : fish.m_maxQuality;
            var counts = new int[maxQuality + 1];
            for (int quality = 0; quality <= maxQuality; quality++)
            {
                counts[quality] = inventory.CountItems(fish.m_name, quality);
            }
            return counts;
        }

        /// <summary>
        /// Decide whether a recipe earns a bonus at all.
        /// </summary>
        /// <returns>
        /// Null if the recipe qualifies, otherwise a short reason why it doesn't.
        /// See <see cref="BonusRules.IneligibleReason(RecipeFacts)"/> for the actual rules.
        /// </returns>
        /// <param name="recipe">
        /// The recipe to judge. Kept separate from <see cref="Choose"/> on purpose, so the
        /// diagnostic report can explain every recipe in the game without needing an
        /// inventory to look at.
        /// </param>
        internal static string IneligibleReason(Recipe recipe)
        {
            return BonusRules.IneligibleReason(Describe(recipe));
        }

        /// <summary>
        /// Work out how many extra items this craft earns.
        /// </summary>
        /// <returns>
        /// The extra items for one craft, or 0 if the recipe doesn't qualify or no fish
        /// was chosen. The caller multiplies this out for multi-crafting.
        /// </returns>
        /// <param name="recipe">The recipe being crafted.</param>
        /// <param name="choice">
        /// The fish we settled on, from <see cref="Choose"/>. Null means no bonus.
        /// </param>
        internal static int BonusFor(Recipe recipe, FishChoice choice)
        {
            if (choice == null) return 0;
            if (IneligibleReason(recipe) != null) return 0;

            int speciesExtra = ModConfig.UseSpeciesBonus.Value
                ? SpeciesBonusTable.ExtraFor(choice.Requirement.m_resItem.m_itemData.m_shared)
                : 0;

            return BonusRules.ComputeBonus(choice.Plan, recipe.m_amount,
                                           ModConfig.BonusPerQualityLevel.Value, speciesExtra);
        }

        /// <summary>
        /// Decide whether a craft vanilla has just refused should be allowed after all,
        /// because the player has enough fish once you stop insisting they are all the
        /// same size.
        /// </summary>
        /// <returns>
        /// True only when every requirement is covered, counting the fish across all
        /// qualities and everything else exactly the way vanilla does. False whenever
        /// we are not sure, so vanilla's refusal stands.
        /// </returns>
        /// <param name="inventory">The player's inventory, read only.</param>
        /// <param name="recipe">The recipe whose Craft button vanilla has just greyed out.</param>
        /// <param name="qualityLevel">The quality of the item being crafted, not of the fish.</param>
        /// <param name="multiplier">1 normally, or the multi-craft amount when shift is held.</param>
        /// <remarks>
        /// Vanilla's own gate is Player.HaveRequirementItems, and for each requirement it
        /// takes the *largest single-quality stack* you own rather than the total:
        ///
        ///     for (int j = 1; j &lt; maxQuality + 1; j++)
        ///         num3 = CountItems(name, j);
        ///         if (num3 &gt; num2) num2 = num3;      // max, never a sum
        ///
        /// Every ordinary crafting material has m_maxQuality 1, so that loop runs once and
        /// the maximum is the total - which is why the rule is invisible everywhere except
        /// on fish. It comes from GetFirstRequiredItem, where a single covering quality
        /// genuinely is required, because that path reads the output's scaling off one
        /// representative item. Recipes that don't use m_requireOnlyOneIngredient inherited
        /// the restriction without ever needing it.
        ///
        /// Consumption was always able to cope: ConsumeResources removes at quality -1, and
        /// Inventory.RemoveItem then walks straight across stacks of different qualities. So
        /// this really is only a gate, and it disagrees with the ingredient list the player
        /// is looking at - InventoryGui.SetupRequirement counts every quality, so the tile
        /// reads 2 of 2 in white while the Craft button stays dead.
        ///
        /// We copy vanilla's rule for the non-fish requirements rather than summing those
        /// too. Keeping the change to fish is the whole point, and any modded ingredient
        /// with real quality tiers keeps whatever behaviour its own author expects.
        /// </remarks>
        internal static bool CanCraftMixed(Inventory inventory, Recipe recipe,
                                           int qualityLevel, int multiplier)
        {
            if (!ModConfig.AllowMixedQualities.Value) return false;
            if (inventory == null || recipe?.m_resources == null) return false;

            // Cheapest check that rules out almost everything goes first. This runs for
            // every recipe the player knows each time the crafting panel rebuilds its list,
            // and 361 of vanilla's 365 recipes leave here. IneligibleReason is the pricier
            // question - it reaches for the crafting station's name, which is a Unity
            // property call - so only the handful of fish recipes ever ask it.
            if (!TryGetSingleFishRequirement(recipe.m_resources, out Piece.Requirement fishReq)) return false;

            // Same notion of "a recipe we handle" as the payout, so the mod never unblocks
            // a craft it would then decline to price.
            if (IneligibleReason(recipe) != null) return false;

            foreach (Piece.Requirement req in recipe.m_resources)
            {
                if (!req.m_resItem) continue;

                int needed = req.GetAmount(qualityLevel) * multiplier;
                if (needed <= 0) continue;

                // CountItems with no quality argument counts every quality, which is the
                // whole point here - and is exactly the number the ingredient list has
                // been showing the player all along.
                int have = ReferenceEquals(req, fishReq)
                    ? inventory.CountItems(req.m_resItem.m_itemData.m_shared.m_name)
                    : LargestStackByQuality(inventory, req);

                if (have < needed) return false;
            }

            return true;
        }

        /// <summary>
        /// Count an ingredient the way vanilla's requirement check does.
        /// </summary>
        /// <returns>
        /// The size of the biggest single-quality holding, which for anything with one
        /// quality level is simply how many you have.
        /// </returns>
        /// <param name="inventory">The player's inventory, read only.</param>
        /// <param name="req">The ingredient to count.</param>
        /// <remarks>
        /// Deliberately reproduces Player.HaveRequirementItems, including its quirk, so
        /// that a requirement we are not trying to change is judged by exactly the rule
        /// that just judged it. Note vanilla starts this scan at quality 1, not 0.
        /// </remarks>
        private static int LargestStackByQuality(Inventory inventory, Piece.Requirement req)
        {
            ItemDrop.ItemData.SharedData shared = req.m_resItem.m_itemData.m_shared;

            int largest = 0;
            for (int quality = 1; quality <= shared.m_maxQuality; quality++)
            {
                int count = inventory.CountItems(shared.m_name, quality);
                if (count > largest) largest = count;
            }
            return largest;
        }

        /// <summary>
        /// Spot a mead base, which is anything brewed at the mead cauldron.
        /// </summary>
        /// <returns>
        /// True if the recipe is crafted at the mead cauldron, fish in it or not. Sorting
        /// out which of those we actually care about happens elsewhere.
        /// </returns>
        /// <param name="recipe">The recipe to check.</param>
        /// <remarks>
        /// "MeadCauldron" is the only game string we hard-code anywhere in the mod, so it
        /// is worth saying why that is safe:
        /// 1. It matches the *prefab* name, not the display name. CraftingStation.m_name is a
        ///    translation key ("$piece_cauldron"), but Unity's .name is the prefab id and is
        ///    identical in every language, so this works for players in any locale.
        /// 2. Prefab names are baked into save files - ZNetScene looks pieces up by
        ///    name.GetStableHashCode() - so renaming one would break every cauldron already
        ///    placed in every world. Iron Gate really can't change it.
        /// 3. OrdinalIgnoreCase rather than ToLower(), because it ignores the player's locale
        ///    and its casing rules.
        /// </remarks>
        internal static bool IsMeadRecipe(Recipe recipe)
        {
            CraftingStation station = recipe?.m_craftingStation;
            return station != null &&
                   station.name.IndexOf("MeadCauldron", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Boil a Recipe down to the plain facts BonusRules can judge.
        /// </summary>
        /// <returns>
        /// The facts. A null or output-less recipe comes back with HasOutput false and
        /// everything else at its default, which BonusRules reports as "no output item".
        /// </returns>
        /// <param name="recipe">The recipe to describe.</param>
        private static RecipeFacts Describe(Recipe recipe)
        {
            var facts = new RecipeFacts();
            if (recipe == null || recipe.m_item == null) return facts;   // HasOutput stays false

            facts.HasOutput = true;
            facts.RequireOnlyOneIngredient = recipe.m_requireOnlyOneIngredient;
            // IsEquipable is the game code's own check, and it
            // covers weapons, armour, tools, shields, torches and trinkets.
            facts.OutputIsEquipment = recipe.m_item.m_itemData.IsEquipable();
            facts.IsMead = IsMeadRecipe(recipe);
            facts.MeadsIncluded = ModConfig.IncludeMeadRecipes.Value;
            facts.ExplicitlyExcluded = IsExcluded(recipe.m_item.gameObject.name);
            facts.FishRequirementCount = CountFishRequirements(recipe.m_resources);
            return facts;
        }

        /// <summary>
        /// Count how many different kinds of fish a recipe asks for.
        /// </summary>
        /// <returns>
        /// The number of fish ingredients. Fish 'n' Bread gives 1, the fishing hat gives 12,
        /// and anything with no fish gives 0.
        /// </returns>
        /// <param name="requirements">The recipe's ingredient list.</param>
        private static int CountFishRequirements(Piece.Requirement[] requirements)
        {
            if (requirements == null) return 0;

            // Deliberately imperative rather than requirements.Count(IsFish). Counting
            // through IEnumerable allocates an enumerator each call, and this runs every
            // frame the crafting panel is open.
            int count = 0;
            foreach (Piece.Requirement req in requirements)
            {
                if (IsFish(req)) count++;
            }
            return count;
        }

        /// <summary>
        /// Find the one fish a recipe needs.
        /// </summary>
        /// <returns>
        /// True only when the recipe asks for exactly one kind of fish. False covers both
        /// "no fish at all" and "more than one kind". The specific reason should not matter
        /// to the caller, since we'll just want to fall back to vanilla crafting in that case.
        ///
        /// The "more than one" case is on purpose: for example, the fishing hat
        /// wants one of all twelve species, and "pay out based on the fish" has no single answer there.
        /// (It also just wouldn't make any sense in that case)
        /// </returns>
        /// <param name="requirements">The recipe's ingredient list.</param>
        /// <param name="fish">
        /// Out parameter that will hold the single fish ingredient when we return true,
        /// and null otherwise.
        /// </param>
        private static bool TryGetSingleFishRequirement(Piece.Requirement[] requirements,
                                                        out Piece.Requirement fish)
        {
            fish = null;
            foreach (Piece.Requirement req in requirements)
            {
                if (!IsFish(req)) continue;

                // Case 2: More than one kind of fish used in this recipe
                if (fish != null)
                {
                    // If fish was already set to non-null, that means _this_ 'req' fish is the
                    // SECOND fish we've found. This recipe isn't for us, so we clear out the fish param
                    // and return fasle
                    fish = null;
                    return false;
                }
                fish = req;
            }
            // Case 1: No Fish at all in this recipe
            return fish != null;
        }

        /// <summary>
        /// Is this ingredient a fish?
        /// </summary>
        /// <returns>
        /// True for anything of ItemType.Fish. We ask the game rather than matching names,
        /// so fish added by other mods (or by a future Valheim update) count automatically.
        /// </returns>
        /// <param name="req">The ingredient to check. Null-safe, and returns false.</param>
        private static bool IsFish(Piece.Requirement req)
        {
            ItemDrop.ItemData.SharedData shared = req?.m_resItem?.m_itemData?.m_shared;
            return shared != null && shared.m_itemType == ItemDrop.ItemData.ItemType.Fish;
        }

        /// <summary>
        /// Has the player told us to leave this recipe alone?
        /// </summary>
        /// <returns>
        /// True if the name appears in ExcludedRecipes. Matching is exact and
        /// case-sensitive, since these are prefab ids.
        /// Prefab ids can be found easily online, such as here:
        /// https://valheim-modding.github.io/Jotunn/data/objects/recipe-list.html
        /// </returns>
        /// <param name="prefabName">
        /// The prefab name of whatever the recipe produces, e.g. "MeadBaseStrength".
        ///
        /// We only re-parse the setting when its string actually changes, because this runs
        /// every frame the crafting panel is open. ConfigurationManager (F1) edits settings
        /// live, so we can't just parse it once at startup and forget about it.
        /// </param>
        private static bool IsExcluded(string prefabName)
        {
            string raw = ModConfig.ExcludedRecipes.Value ?? string.Empty;
            if (raw != _excludedRaw)
            {
                _excludedRaw = raw;
                _excluded = BonusRules.ParseExclusions(raw);
            }
            return _excluded.Contains(prefabName);
        }
    }
}
