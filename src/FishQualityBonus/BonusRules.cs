using System;
using System.Collections.Generic;

namespace FishQualityBonus
{
    /// <summary>
    /// Everything about a recipe that decides whether it earns a bonus.
    ///
    /// HasOutput is the one to check first. When it is false the recipe was null or had no
    /// output item, and none of the other fields were filled in - they are C# defaults
    /// rather than answers. Nothing reads them in that case, because
    /// <see cref="BonusRules.IneligibleReason(RecipeFacts)"/> tests HasOutput before
    /// anything else, so keep that check first if you ever reorder them.
    /// </summary>
    internal struct RecipeFacts
    {
        public bool HasOutput;
        public bool RequireOnlyOneIngredient;
        public bool OutputIsEquipment;
        public bool IsMead;
        public bool MeadsIncluded;
        public bool ExplicitlyExcluded;
        public int FishRequirementCount;
    }

    /// <summary>
    /// Plain class with no Unity/BepInEx/Valheim dependencies, so that we can extract
    /// out the decision-making logic and test it.
    /// </summary>
    internal static class BonusRules
    {
        /// <summary>What PickQuality returns when no tier can cover the craft.</summary>
        internal const int NoQuality = -1;

        /// <summary>
        /// Picks which quality of fish to spend, given how many of each you have.
        /// </summary>
        /// <returns>
        /// The quality, or <see cref="BonusRules.NoQuality"/> if none is found that satisfies
        /// the requirements.
        /// </returns> 
        /// <param name="countsByQuality">
        /// How many fish of each quality are in the player's inventory. The index is the quality, so
        /// countsByQuality[2] is the number of quality-2 fish. Index 0 is quality 0,
        /// which no real fish has, but we keep the slot so index and quality always match,
        /// and because vanilla scans from 0 as well.
        /// </param>
        /// <param name="needed">
        /// How many fish this whole craft will eat: the recipe's requirement times the
        /// craft multiplier. Example: MeadBaseBugRepellent wants 3 fish, so multi-crafting five
        /// of them needs 15. We don't split across two qualities, so if you have 2 quality-2 and
        /// 2 quality-3, we return NoQuality. If you have 3 quality-2 and 2 quality-3, we'll use the
        /// lower quality, since that's all you actually have that satisfies.
        /// </param>
        /// <param name="largestFirst">
        /// Whether to choose the largest quality fish first for this recipe, or the smallest.
        /// True = largest. See <see cref="ModConfig.FishToSpend"/>.
        /// </param>
        internal static int PickQuality(IList<int> countsByQuality, int needed, bool largestFirst)
        {
            if (countsByQuality == null || countsByQuality.Count == 0 || needed <= 0) return NoQuality;

            int last = countsByQuality.Count - 1;
            for (int i = 0; i <= last; i++)
            {
                int quality = largestFirst ? last - i : i;
                if (countsByQuality[quality] >= needed) return quality;
            }
            return NoQuality;
        }

        /// <summary>
        /// Compute how many extra items one craft earns, on top of what the
        /// recipe normally makes.
        /// </summary>
        /// <returns>
        /// The number of extra items. Never negative, and 0 when nothing applies.
        /// Example: a quality-2 anglerfish in Fish 'n' Bread at the default
        /// settings returns 5, so you get 6 meals instead of 1.
        /// </returns>
        /// <param name="quality">
        /// The quality of the fish being spent, from <see cref="PickQuality"/>.
        /// A quality-1 fish earns nothing from this part. Anything below 1 is
        /// treated as 1, because vanilla counts qualities from 0 and we don't
        /// want a negative eating the species bonus.
        /// </param>
        /// <param name="recipeAmount">
        /// What the recipe normally makes in one craft (Recipe.m_amount). Fish
        /// 'n' Bread makes 1, so the size bonus scales off 1. This is per craft,
        /// not per multi-craft - the caller multiplies afterwards.
        /// </param>
        /// <param name="perQualityLevel">
        /// How much each quality level above 1 is worth, as a multiple of
        /// recipeAmount. See <see cref="ModConfig.BonusPerQualityLevel"/>.
        /// </param>
        /// <param name="speciesExtra">
        /// The flat bonus for the species of fish; e.g. anglerfish is +2.
        /// This is completely independent of quality.
        /// Pass 0 to leave it out, which is what we do when UseSpeciesBonus is off.
        /// See <see cref="SpeciesBonusTable"/>.
        /// Also see https://valheim.fandom.com/wiki/Raw_Fish.
        /// </param>
        internal static int ComputeBonus(int quality, int recipeAmount, int perQualityLevel, int speciesExtra)
        {
            // Though no fish with quality 0 exists, the vanilla code counts fish from quality 0,
            // so a 0 can theoretically reach us. We safeguard this here to clamp down to 1.
            if (quality < 1) quality = 1;
            if (recipeAmount < 0) recipeAmount = 0;

            int scaled = (quality - 1) * recipeAmount * perQualityLevel;
            return Math.Max(0, scaled + Math.Max(0, speciesExtra));
        }

        /// <summary>
        /// Decide whether a recipe earns a bonus at all.
        /// </summary>
        /// <returns>
        /// Null if the recipe qualifies, otherwise a short reason why it doesn't.
        /// The reason is written for debugging only: <see cref="FishRecipeReport"/> which
        /// prints it straight into the log next to each recipe.
        ///
        /// Only the first failing reason comes back, so the order of the checks
        /// below decides what you see. The fishing hat violates two rules (it is
        /// equipment, and it wants more than one fish); we report the equipment one
        /// because it explains more.
        /// </returns>
        /// <param name="facts">
        /// Descriptive 'facts' about the craft, built by <see cref="FishBonus.Describe(Recipe)"/>.
        /// Note that two of these fields are config settings rather than facts purely about
        /// the recipe - MeadsIncluded and ExplicitlyExcluded - so the answer can
        /// change when the player edits the config.
        /// </param>
        internal static string IneligibleReason(RecipeFacts facts)
        {
            if (!facts.HasOutput) return "no output item";

            // The flag reads oddly here because it describes a requirements rule
            // ("any one of these ingredients will do"), not scaling.
            // But looking at the recipes that exist in-game today using this flag,
            // it only applies to FishRaw. So we treat this as an indicator that only
            // one of many possible ingredients qualifies, and the game will already scale the output.
            // In theory, other recipes might come out in the future (or from other mods) that use this,
            // and we'll skip those, too. If there is ever a case where the game changes and this
            // flag is no longer appropriate, we can modify!
            if (facts.RequireOnlyOneIngredient) return "vanilla already scales this by ingredient quality";

            if (facts.OutputIsEquipment) return "output is equipment";
            if (facts.IsMead && !facts.MeadsIncluded) return "mead, and IncludeMeadRecipes is off";
            if (facts.ExplicitlyExcluded) return "listed in ExcludedRecipes";
            if (facts.FishRequirementCount == 0) return "uses no fish";

            // We don't support recipes that use more than one kind of fish, so we'd fall back to
            // vanilla if that's the case
            // We could always change this in the future, but we'd need to decide where to draw the line
            // on what kind of recipes to support, and there's none that I care about in the game currently
            if (facts.FishRequirementCount > 1) return "uses " + facts.FishRequirementCount + " different fish";

            return null;
        }

        /// <summary>
        /// Fold the species bonuses we read out of the game into one lookup.
        /// </summary>
        /// <returns>
        /// A map of fish name to bonus. Fish with no bonus (perch, pike, tetra, trollfish)
        /// are left out entirely, so the count is 8 in vanilla rather than 12.
        /// </returns>
        /// <param name="entries">
        /// One entry per fish requirement found on a single-ingredient recipe:
        /// the fish's name, and the bonus that recipe gives it.
        /// 
        /// Pratically speaking (in vanilla Valheim), all entries only come from one
        /// recipe (FishRaw), one per fish. Theoretically there could be more, so
        /// this function can handle additional recipes that pop up in the future.
        /// 
        /// We dedupe and resolve using the following rules (again, practically not needed today):
        /// 1. Drop zeroes
        /// 2. Dedupe by taking the bigger number when a fish shows up twice
        /// 
        /// This should also handle if other mods add other fish-related recipes. Hopefully!
        /// 
        /// See <see cref="SpeciesBonusTable.Build"/> for where these bonuses come from.
        /// </param>
        internal static Dictionary<string, int> BuildSpeciesTable(IEnumerable<KeyValuePair<string, int>> entries)
        {
            var table = new Dictionary<string, int>();
            if (entries == null) return table;

            foreach (KeyValuePair<string, int> entry in entries)
            {
                if (string.IsNullOrEmpty(entry.Key) || entry.Value <= 0) continue;

                int existing;
                if (table.TryGetValue(entry.Key, out existing) && existing >= entry.Value) continue;
                table[entry.Key] = entry.Value;
            }
            return table;
        }

        /// <summary>
        /// Split the ExcludedRecipes setting into the names it lists.
        /// </summary>
        /// <returns>
        /// The prefab names to skip, or an empty set if the setting is blank.
        /// Matching is exact and case-sensitive, since these are prefab ids
        /// rather than anything the player types from memory.
        /// </returns>
        /// <param name="raw">
        /// The setting as typed in BepInEx Config by the player, comma-separated.
        /// Spaces around each name are trimmed and empty entries ignored,
        /// so "A, ,B," gives you A and B. See <see cref="ModConfig.ExcludedRecipes"/>.
        /// </param>
        internal static HashSet<string> ParseExclusions(string raw)
        {
            var set = new HashSet<string>();
            if (string.IsNullOrEmpty(raw)) return set;

            foreach (string part in raw.Split(','))
            {
                string trimmed = part.Trim();
                if (trimmed.Length > 0) set.Add(trimmed);
            }
            return set;
        }
    }
}
