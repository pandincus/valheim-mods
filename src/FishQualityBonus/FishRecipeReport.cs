using System.Text;
using UnityEngine;

namespace FishQualityBonus
{
    /// <summary>
    /// Purely for diagnostics, and this class has no impact on the mod's actual 'effects'.
    /// This report dumps every fish and every recipe that eats one to the BepInEx log,
    /// so we can read the game's real data instead of guessing at it.
    /// 
    /// Off by default in <see cref="ModConfig"/> 
    ///
    /// The line to look at is the "-->" under each recipe: it says whether the
    /// bonus should apply, and if not, why not. The rest is the raw data those
    /// decisions come from.
    /// </summary>
    internal static class FishRecipeReport
    {
        /// <summary>
        /// Write the report to the BepInEx log, if the settings ask for it.
        /// </summary>
        /// <returns>
        /// True if we actually wrote something, false if the settings said not to.
        ///
        /// <see cref="ObjectDbHooks"/> needs to tell those apart. If a declined report
        /// counted as done, switching LogRecipeReport on partway through a session would
        /// never produce anything.
        /// </returns>
        /// <param name="db">
        /// The game's item and recipe database. We only read from it here.
        /// </param>
        internal static bool Write(ObjectDB db)
        {
            // Enabled is a full kill switch, so with it off we don't even log.
            if (!ModConfig.Enabled.Value || !ModConfig.LogRecipeReport.Value) return false;

            StringBuilder sb = new();
            _ = sb.AppendLine("===== FishQualityBonus report (" + db.m_recipes.Count + " recipes, " +
                          SpeciesBonusTable.Count + " species bonuses derived) =====");

            // Every fish, with the tier we scraped for it. If these all read +0
            // then the scrape found nothing and UseSpeciesBonus is doing nothing.
            _ = sb.AppendLine("-- Fish items --");
            foreach (GameObject go in db.m_items)
            {
                if (go == null) continue;
                ItemDrop drop = go.GetComponent<ItemDrop>();
                ItemDrop.ItemData.SharedData shared = drop?.m_itemData?.m_shared;
                if (shared == null || shared.m_itemType != ItemDrop.ItemData.ItemType.Fish) continue;
                _ = sb.AppendLine("   " + go.name.PadRight(24) + " maxQuality=" + shared.m_maxQuality +
                              " scaleWeightByQuality=" + shared.m_scaleWeightByQuality +
                              " speciesBonus=+" + SpeciesBonusTable.ExtraFor(shared));
            }

            // Every recipe the game already scales by ingredient quality, fish
            // or not. These are the ones we deliberately keep out of, so it is
            // worth being able to see the whole list rather than assuming it is
            // only Fish (raw) - m_requireOnlyOneIngredient is a general Recipe
            // feature and nothing stops another recipe using it.
            _ = sb.AppendLine("-- Recipes vanilla already scales (m_requireOnlyOneIngredient) --");
            int alreadyScaled = 0;
            foreach (Recipe recipe in db.m_recipes)
            {
                if (recipe == null || recipe.m_item == null || !recipe.m_requireOnlyOneIngredient) continue;
                alreadyScaled++;
                _ = sb.AppendLine("   " + recipe.m_item.gameObject.name.PadRight(24) +
                              " x" + recipe.m_amount +
                              "   QualityMult=" + recipe.m_qualityResultAmountMultiplier +
                              "   ingredientChoices=" + (recipe.m_resources == null ? 0 : recipe.m_resources.Length));
            }
            if (alreadyScaled == 0) _ = sb.AppendLine("   (none)");

            _ = sb.AppendLine("-- Recipes consuming a fish --");
            foreach (Recipe recipe in db.m_recipes)
            {
                if (recipe == null || recipe.m_item == null || recipe.m_resources == null) continue;

                bool usesFish = false;
                foreach (Piece.Requirement req in recipe.m_resources)
                {
                    ItemDrop.ItemData.SharedData shared = req?.m_resItem?.m_itemData?.m_shared;
                    if (shared != null && shared.m_itemType == ItemDrop.ItemData.ItemType.Fish)
                    {
                        usesFish = true;
                        break;
                    }
                }
                if (!usesFish) continue;

                string reason = FishBonus.IneligibleReason(recipe);
                _ = sb.AppendLine("   " + recipe.m_item.gameObject.name + " x" + recipe.m_amount +
                              "   OnlyOneIngredient=" + recipe.m_requireOnlyOneIngredient +
                              "   QualityMult=" + recipe.m_qualityResultAmountMultiplier +
                              "   Station=" + (recipe.m_craftingStation ? recipe.m_craftingStation.name : "(none)"));
                ItemDrop.ItemData.SharedData outShared = recipe.m_item.m_itemData.m_shared;
                _ = sb.AppendLine("        output: type=" + outShared.m_itemType +
                              " maxStackSize=" + outShared.m_maxStackSize +
                              " equipable=" + recipe.m_item.m_itemData.IsEquipable() +
                              " mead=" + FishBonus.IsMeadRecipe(recipe));
                _ = sb.AppendLine("        --> " + (reason == null ? "BONUS APPLIES" : "skipped: " + reason));
                foreach (Piece.Requirement req in recipe.m_resources)
                {
                    ItemDrop.ItemData.SharedData shared = req?.m_resItem?.m_itemData?.m_shared;
                    if (shared == null) continue;
                    _ = sb.AppendLine("        needs " + req.m_resItem.gameObject.name.PadRight(20) +
                                  " x" + req.m_amount +
                                  " (type=" + shared.m_itemType + ", maxQuality=" + shared.m_maxQuality +
                                  ", extraOnlyOne=" + req.m_extraAmountOnlyOneIngredient + ")");
                }
            }

            _ = sb.Append("===== end report =====");
            FishQualityBonusPlugin.Log.LogInfo(sb.ToString());
            return true;
        }
    }
}
