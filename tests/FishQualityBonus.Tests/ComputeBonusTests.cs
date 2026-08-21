using FishQualityBonus;

namespace FishQualityBonusTests;

public class ComputeBonusTests
{
    // The three tables on the wiki's Raw Fish page, in full. Vanilla's Fish
    // (raw) recipe makes 1 at a multiplier of 3, so total = 1 + ComputeBonus.
    // If this ever fails, our formula has drifted from the game's.
    [Theory]
    // Perch, Pike, Tetra, Trollfish - species tier +0
    [InlineData(1, 0, 1)]
    [InlineData(2, 0, 4)]
    [InlineData(3, 0, 7)]
    [InlineData(4, 0, 10)]
    [InlineData(5, 0, 13)]
    // Tuna, Coral Cod, Giant Herring, Grouper - species tier +1
    [InlineData(1, 1, 2)]
    [InlineData(2, 1, 5)]
    [InlineData(3, 1, 8)]
    [InlineData(4, 1, 11)]
    [InlineData(5, 1, 14)]
    // Pufferfish, Anglerfish, Magma Fish, Northern Salmon - species tier +2
    [InlineData(1, 2, 3)]
    [InlineData(2, 2, 6)]
    [InlineData(3, 2, 9)]
    [InlineData(4, 2, 12)]
    [InlineData(5, 2, 15)]
    public void ReproducesTheVanillaRawFishTables(int quality, int speciesExtra, int expectedTotal)
    {
        int total = 1 + BonusRules.ComputeBonus(quality, recipeAmount: 1, perQualityLevel: 3, speciesExtra);

        Assert.Equal(expectedTotal, total);
    }

    [Fact]
    public void QualityOneEarnsNoScaledBonus()
    {
        Assert.Equal(0, BonusRules.ComputeBonus(1, recipeAmount: 1, perQualityLevel: 3, speciesExtra: 0));
    }

    [Fact]
    public void SpeciesBonusIsFlatAndAppliesEvenAtQualityOne()
    {
        // Vanilla does not scale this term by quality - that is the whole
        // reason UseSpeciesBonus is a separate setting.
        Assert.Equal(2, BonusRules.ComputeBonus(1, recipeAmount: 1, perQualityLevel: 3, speciesExtra: 2));
    }

    [Fact]
    public void ScalesWithTheRecipeOutputAmount()
    {
        // A recipe making 2 at a time gets twice the per-level bonus.
        Assert.Equal(12, BonusRules.ComputeBonus(3, recipeAmount: 2, perQualityLevel: 3, speciesExtra: 0));
    }

    [Theory]
    [InlineData(1, 4)]
    [InlineData(3, 12)]
    [InlineData(10, 40)]
    public void HonoursThePerLevelMultiplier(int perQualityLevel, int expected)
    {
        Assert.Equal(expected, BonusRules.ComputeBonus(5, recipeAmount: 1, perQualityLevel, speciesExtra: 0));
    }

    [Fact]
    public void QualityBelowOneIsClampedAndDoesNotCancelTheSpeciesBonus()
    {
        // Vanilla scans quality tiers from 0, so a 0 can reach us. Without the
        // clamp, (0 - 1) would produce a negative term that swallowed the
        // species bonus and silently returned nothing.
        Assert.Equal(2, BonusRules.ComputeBonus(0, recipeAmount: 1, perQualityLevel: 3, speciesExtra: 2));
    }

    [Fact]
    public void NeverReturnsANegativeBonus()
    {
        Assert.Equal(0, BonusRules.ComputeBonus(0, recipeAmount: -5, perQualityLevel: 3, speciesExtra: -9));
    }
}
