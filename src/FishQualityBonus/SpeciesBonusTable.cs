using System.Collections.Generic;
using FishQualityBonus.Logic;

namespace FishQualityBonus
{
    /// <summary>
    /// How much each species of fish is worth on its own, read from the game at
    /// load rather than hard-coded out here.
    ///
    /// See https://valheim.fandom.com/wiki/Raw_Fish
    /// Valheim splits its (currently) twelve fish into three tiers worth +0, +1 and +2 raw
    /// fish. Anglerfish is a +2, for example.
    ///
    /// Valheim keeps those numbers in m_extraAmountOnlyOneIngredient on the
    /// Fish (raw) recipe, and only ever reads them for single-ingredient
    /// recipes.
    ///
    /// We read the same numbers back out and treat them as belonging to the
    /// species, so a recipe that eats an anglerfish can pay out like one.
    /// Taking them from the recipe means no hard-coded fish names, and any fish another
    /// mod adds some (or, if Valheim's upcoming 1.0 Deep North adds them),
    /// we SHOULD get the new values for free.
    /// </summary>
    internal static class SpeciesBonusTable
    {
        private static Dictionary<string, int> _extraByFish = new Dictionary<string, int>();

        /// <summary>How many species we found a bonus for. The report prints this.</summary>
        internal static int Count => _extraByFish.Count;

        /// <summary>
        /// Pulls the species tiers out of the game's recipe list. The rules for folding them
        /// together live in <see cref="BonusRules.BuildSpeciesTable(IEnumerable{KeyValuePair{string, int}})"/>
        /// so they can be unit-tested on their own.
        /// </summary>
        /// <param name="db">
        /// The game's item and recipe database, already filled in. We read it and never write to it.
        ///
        /// Practically speaking (in vanilla Valheim), we walk all 365 recipes and exactly one
        /// survives the check below: FishRaw. Scanning for it rather than naming it keeps us
        /// honest if that ever changes, and it only costs one pass when a world loads.
        ///
        /// Called from <see cref="ObjectDbHooks"/>, which also decides when this is worth
        /// redoing.
        /// </param>
        internal static void Build(ObjectDB db)
        {
            var entries = new List<KeyValuePair<string, int>>();

            if (db?.m_recipes != null)
            {
                foreach (Recipe recipe in db.m_recipes)
                {
                    // Only single-ingredient recipes fill this field in. On
                    // every other recipe it is left at 0 and never read.
                    if (recipe == null || !recipe.m_requireOnlyOneIngredient || recipe.m_resources == null) continue;

                    foreach (Piece.Requirement req in recipe.m_resources)
                    {
                        ItemDrop.ItemData.SharedData shared = req?.m_resItem?.m_itemData?.m_shared;
                        if (shared == null || shared.m_itemType != ItemDrop.ItemData.ItemType.Fish) continue;

                        entries.Add(new KeyValuePair<string, int>(shared.m_name, req.m_extraAmountOnlyOneIngredient));
                    }
                }
            }

            _extraByFish = BonusRules.BuildSpeciesTable(entries);
        }

        /// <summary>
        /// Look up the flat bonus for one species of fish.
        /// </summary>
        /// <returns>
        /// The bonus, or 0 for a fish we found nothing for. Perch, pike, tetra and trollfish
        /// are all genuinely 0, so a 0 here is a real answer rather than a lookup failure.
        /// </returns>
        /// <param name="fish">
        /// The fish's shared data. We key on m_shared.m_name (the translation key, e.g.
        /// "$item_fish9") because that is what the recipe requirements give us, and it is
        /// stable across languages. Null-safe, and returns 0.
        /// </param>
        internal static int ExtraFor(ItemDrop.ItemData.SharedData fish)
        {
            if (fish == null) return 0;
            return _extraByFish.TryGetValue(fish.m_name, out int extra) ? extra : 0;
        }
    }
}
