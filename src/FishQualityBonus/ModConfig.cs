using BepInEx.Configuration;

namespace FishQualityBonus
{
    /// <summary>Which fish a recipe should spend when you have several.</summary>
    public enum FishPreference
    {
        /// <summary>Spend the smallest first, which keeps your best catches.
        /// This is what vanilla already does for the Fish (raw) recipe.</summary>
        SmallestFirst,

        /// <summary>Always spend the largest quality fish you have in your inventory.</summary>
        LargestFirst,
    }

    /// <summary>
    /// Every setting the mod has. BepInEx writes these to
    /// BepInEx/config/pandincus.fishqualitybonus.cfg on first run, and
    /// ConfigurationManager (F1) edits them live - the code reads .Value every
    /// time, so changes take effect straight away with no restart.
    ///
    /// These descriptions are visible to players using the config manager.
    /// </summary>
    internal static class ModConfig
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<int> BonusPerQualityLevel;
        internal static ConfigEntry<FishPreference> FishToSpend;
        internal static ConfigEntry<bool> AllowMixedQualities;
        internal static ConfigEntry<bool> UseSpeciesBonus;
        internal static ConfigEntry<bool> IncludeMeadRecipes;
        internal static ConfigEntry<string> ExcludedRecipes;
        internal static ConfigEntry<bool> LogRecipeReport;

        /// <summary>
        /// Declare every setting. Called once from <see cref="FishQualityBonusPlugin.Awake"/>.
        /// </summary>
        /// <param name="cfg">
        /// The plugin's config file, handed to us by BepInEx. Binding a setting either reads
        /// the player's existing value or writes the default, so the .cfg on disk always ends
        /// up complete.
        ///
        /// The descriptions below are the player-facing help text: they show up as comments
        /// in the .cfg and as tooltips in ConfigurationManager (F1). Worth writing for a
        /// player rather than for us.
        /// </param>
        internal static void Init(ConfigFile cfg)
        {
            Enabled = cfg.Bind(
                "General", "Enabled", true,
                "Master switch. Disables all mod functionality. When disabled the mod should behave " +
                "exactly like vanilla. No side-effects, though we still capture information about " +
                "receipts into internal memory.");

            BonusPerQualityLevel = cfg.Bind(
                "Bonus", "BonusPerQualityLevel", 3,
                new ConfigDescription(
                    "How much higher-quality fish are worth. The formula is:\n" +
                    "    size bonus = (fishQuality - 1) * amount * thisValue\n" +
                    "where 'amount' is what the recipe normally makes. Fish 'n' Bread makes 1, " +
                    "so with this bonus set to the default of 3, a quality 1/2/3/4/5 fish gives " +
                    " you 1/4/7/10/13. That is the same scaling the game uses for its own Fish (raw) recipe.\n" +
                    "Note that UseSpeciesBonus is added on top and is not affected by this setting, " +
                    "so with both left at their defaults a quality-1 anglerfish still gives 3, " +
                    "and a quality-5 gives 15.\n" +
                    "When a craft spends fish of different sizes, 'fishQuality' is their average, " +
                    "rounded down.",
                    new AcceptableValueRange<int>(1, 10)));

            FishToSpend = cfg.Bind(
                "Bonus", "FishToSpend", FishPreference.SmallestFirst,
                "Which fish a recipe spends when you are carrying several qualities of the same " +
                "species. Vanilla takes whichever one you picked up first, no matter where it " +
                "sits in your inventory, so it is effectively random.\n" +
                "We work through your fish in this order and take them as we go, so " +
                "SmallestFirst really does save your best catches: a two-fish craft with one " +
                "small fish and three big ones spends the small one and only one big one.");

            AllowMixedQualities = cfg.Bind(
                "Bonus", "AllowMixedQualities", true,
                "Let a recipe draw on several qualities of the same fish at once.\n" +
                "Vanilla will not: it checks your biggest single-quality stack rather than your " +
                "total, so two trollfish of different sizes cannot brew a Troll Endurance mead " +
                "that needs two - even though the ingredient list shows 2 of 2 and looks happy. " +
                "With this on, the craft goes through and the payout is based on the average " +
                "size of the fish you spent.\n" +
                "This applies to the same recipes the bonus does, so ExcludedRecipes and " +
                "IncludeMeadRecipes still leave a recipe entirely to vanilla. Set false to keep " +
                "vanilla's rule and only change the payout.");

            UseSpeciesBonus = cfg.Bind(
                "Bonus", "UseSpeciesBonus", true,
                "Also pay out for what kind of fish it is, not just how big it was. Valheim " +
                "sorts its twelve fish into +0/+1/+2 tiers for the Fish (raw) recipe, and this " +
                "reuses those same numbers, read from the game at load. Anglerfish is a +2, so " +
                "Fish 'n' Bread gains 2 loaves. Vanilla does not scale this by quality, so it " +
                "applies to a quality-1 fish too. Set false if you want the bonus to come purely " +
                "from fish quality.");

            IncludeMeadRecipes = cfg.Bind(
                "Bonus", "IncludeMeadRecipes", true,
                "Whether mead bases brewed at the mead cauldron get the bonus as well as food.\n" +
                "Vanilla has three that involve fish: MeadBaseBugRepellent, MeadBaseStrength and MeadBaseSwimmer.\n" +
                "Set false to keep the mod to food recipes like Fish 'n' Bread. Equipment such " +
                "as the fishing hat is never affected either way.");

            ExcludedRecipes = cfg.Bind(
                "Bonus", "ExcludedRecipes", "",
                "Optional. A comma-separated list of recipes to leave alone, named by the " +
                "prefab that comes out of them. Use this when you want to skip one recipe " +
                "rather than a whole group. Example: MeadBaseStrength,MeadBaseSwimmer");

            LogRecipeReport = cfg.Bind(
                "Diagnostics", "LogRecipeReport", false,
                "Dump every fish and every fish-consuming recipe to the BepInEx log on load, " +
                "with a note on whether the bonus should apply and why. This is a development " +
                "aid and changes nothing in game.");
        }
    }
}
