using System.Collections.Generic;
using FishQualityBonus.Logic;

namespace FishQualityBonus.Tests
{
    public class SpeciesTableTests
    {
        private static KeyValuePair<string, int> Entry(string fish, int extra)
        {
            return new(fish, extra);
        }

        [Fact]
        public void KeepsOnlyTheSpeciesThatCarryABonus()
        {
            // Perch and pike are the +0 tier. Storing zeroes would just make the
            // report claim twelve bonuses when there are really only eight.
            Dictionary<string, int> table = BonusRules.BuildSpeciesTable(
            [
                Entry("$item_fish1", 0),
                Entry("$item_fish3", 1),
                Entry("$item_fish9", 2),
            ]);

            Assert.Equal(2, table.Count);
            Assert.False(table.ContainsKey("$item_fish1"));
            Assert.Equal(1, table["$item_fish3"]);
            Assert.Equal(2, table["$item_fish9"]);
        }

        [Fact]
        public void KeepsTheMostGenerousValueWhenRecipesDisagree()
        {
            // Hypothetical in vanilla - FishRaw is the only recipe we read from, and
            // it lists each fish once. This pins the behaviour for the case where a
            // mod adds a second single-ingredient fish recipe with a different tier.
            Dictionary<string, int> table = BonusRules.BuildSpeciesTable(
            [
                Entry("$item_fish9", 1),
                Entry("$item_fish9", 2),
                Entry("$item_fish9", 0),
            ]);

            Assert.Equal(2, table["$item_fish9"]);
        }

        [Fact]
        public void IgnoresBlankAndMissingKeys()
        {
            Dictionary<string, int> table = BonusRules.BuildSpeciesTable(
            [
                Entry(null, 2),
                Entry("", 2),
                Entry("$item_fish9", 2),
            ]);

            _ = Assert.Single(table);
        }

        [Fact]
        public void HandlesNoInputWithoutThrowing()
        {
            Assert.Empty(BonusRules.BuildSpeciesTable(null));
            Assert.Empty(BonusRules.BuildSpeciesTable([]));
        }

        [Theory]
        [InlineData(null, 0)]
        [InlineData("", 0)]
        [InlineData("   ", 0)]
        [InlineData("MeadBaseStrength", 1)]
        [InlineData("MeadBaseStrength,MeadBaseSwimmer", 2)]
        [InlineData("  MeadBaseStrength , MeadBaseSwimmer  ", 2)]
        [InlineData("MeadBaseStrength,,MeadBaseSwimmer,", 2)]
        public void ParsesTheExclusionList(string raw, int expectedCount)
        {
            Assert.Equal(expectedCount, BonusRules.ParseExclusions(raw).Count);
        }

        [Fact]
        public void ExclusionEntriesAreTrimmed()
        {
            Assert.Contains("MeadBaseStrength", BonusRules.ParseExclusions("  MeadBaseStrength  "));
        }
    }
}
