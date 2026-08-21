using HarmonyLib;

namespace FishQualityBonus
{
    /// <summary>
    /// The one place we react to the game's item and recipe database being built.
    ///
    /// Valheim builds ObjectDB twice: once for the main menu, and again when a world loads.
    /// We patch both entry points and watch the recipe count, so a second and fuller
    /// database still gets picked up.
    /// </summary>
    [HarmonyPatch]
    internal static class ObjectDbHooks
    {
        private static int _lastRecipeCount = -1;

        // Tracked apart from _lastRecipeCount on purpose. The report can decline
        // to write (the settings turn it off), and if we counted that as done we
        // would never try again for the rest of the session.
        private static int _lastReportedCount = -1;

        [HarmonyPostfix]
        [HarmonyPatch(typeof(ObjectDB), "Awake")]
        private static void AfterAwake(ObjectDB __instance) => Refresh(__instance);

        [HarmonyPostfix]
        [HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.CopyOtherDB))]
        private static void AfterCopyOtherDB(ObjectDB __instance) => Refresh(__instance);

        /// <summary>
        /// Rebuild whatever depends on the recipe list, and write the report if it is due.
        /// </summary>
        /// <param name="db">The database that was just built or copied. Both hooks land here.</param>
        /// <remarks>
        /// Practically speaking, this runs a couple of times per session and does real work
        /// once, since the main-menu database already holds all 365 recipes and the world
        /// load hands us the same count.
        ///
        /// Known limitation: mods that add recipes *after* this point (Jotunn and friends
        /// do) are missed until the next world load. Nothing in vanilla does that, and it
        /// sorts itself out on reload, so we live with it for now.
        /// </remarks>
        private static void Refresh(ObjectDB db)
        {
            if (db?.m_recipes == null || db.m_recipes.Count == 0) return;
            int recipeCount = db.m_recipes.Count;

            if (recipeCount != _lastRecipeCount)
            {
                _lastRecipeCount = recipeCount;

                // Built no matter what the settings say. It is cheap, and it has
                // to be ready in case the player switches Enabled on mid-session.
                SpeciesBonusTable.Build(db);
            }

            // Only counts as reported if it actually wrote something, so a
            // report turned off at the menu can still appear on world load.
            if (recipeCount != _lastReportedCount && FishRecipeReport.Write(db))
            {
                _lastReportedCount = recipeCount;
            }
        }
    }
}
