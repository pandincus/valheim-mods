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
        /// <summary>
        /// Compute how many extra items one craft earns, on top of what the
        /// recipe normally makes.
        /// </summary>
        /// <returns>
        /// The number of extra items. Never negative, and 0 when nothing applies.
        /// Example: a quality-2 anglerfish in Fish 'n' Bread at the default
        /// settings returns 5, so you get 6 meals instead of 1.
        /// </returns>
        /// <param name="plan">
        /// The fish this craft will spend, from <see cref="TryPickFish"/>. We price it on
        /// the average size of those fish rather than on their total, so a recipe eating
        /// three fish is not worth three times one eating a single fish. The empty plan
        /// earns no size bonus at all.
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
        /// <remarks>
        /// The formula is the average of (quality - 1) across the fish spent, times the
        /// recipe amount, times the per-level setting:
        ///
        ///     size bonus = plan.QualityPoints * recipeAmount * perQualityLevel / plan.TotalFish
        ///
        /// Practically speaking, when every fish is the same quality this is exactly what
        /// the mod did before 0.2.0 and exactly what vanilla does for Fish (raw), because
        /// qualityPoints is then fishCount * (quality - 1) and the division cancels.
        /// Mixed qualities are the new case.
        ///
        /// Multiply-then-divide is deliberate: it keeps the precision that dividing first
        /// would throw away. The division is integer, so it floors, and the mod can never
        /// pay more for a mixed craft than for the same craft with every fish rounded up
        /// to the better quality. Worked example - two trollfish, one quality-1 and one
        /// quality-2, in a mead base that makes 1 at the default multiplier of 3:
        /// QualityPoints is 1, so 1 * 1 * 3 / 2 = 1, and you brew 2 instead of 1.
        /// Two quality-2 would give 3, and two quality-1 would give 0.
        ///
        /// There is no guard here against a negative quality total, because
        /// <see cref="FishPlan"/> cannot produce one. recipeAmount and speciesExtra still
        /// get clamped - those come from game data rather than from us.
        /// </remarks>
        internal static int ComputeBonus(FishPlan plan, int recipeAmount,
                                         int perQualityLevel, int speciesExtra)
        {
            if (recipeAmount < 0) recipeAmount = 0;

            int fishCount = plan.TotalFish;
            int scaled = fishCount > 0
                ? plan.QualityPoints * recipeAmount * perQualityLevel / fishCount
                : 0;

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
        /// <remarks>
        /// Since 0.2.0 this decides more than the payout. It also decides which recipes may
        /// be crafted from mixed qualities, because <see cref="FishBonus.CanCraftMixed"/>
        /// asks the same question. One notion of "a recipe this mod handles" is easier to
        /// explain than two, and it guarantees we never unblock a craft we would then
        /// decline to price - which would hand the player a mispriced payout.
        ///
        /// Note this does not cover consumption. <see cref="FishBonus.Choose"/> works from a
        /// requirement list rather than a Recipe, because that is all
        /// Player.ConsumeResources is given, so FishToSpend still steers which fish gets
        /// eaten by an ineligible recipe that happens to use exactly one. That costs
        /// nothing - the payout is vanilla's either way - and it is the older behaviour.
        /// </remarks>
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
