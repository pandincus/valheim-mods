using FishQualityBonus;

namespace FishQualityBonusTests;

public class PickQualityTests
{
    // Index = quality, value = how many of that quality are in the bag.
    // Index 0 exists because vanilla's own scan starts at quality 0.
    private static readonly int[] OneSmallOneBig = { 0, 1, 0, 0, 1, 0 };

    [Fact]
    public void SmallestFirstSpendsTheWorstFishThatWillDo()
    {
        Assert.Equal(1, BonusRules.PickQuality(OneSmallOneBig, needed: 1, largestFirst: false));
    }

    [Fact]
    public void LargestFirstSpendsTheBestFish()
    {
        Assert.Equal(4, BonusRules.PickQuality(OneSmallOneBig, needed: 1, largestFirst: true));
    }

    [Fact]
    public void SkipsTiersThatCannotCoverTheWholeCraft()
    {
        // Crafting 2 at once: quality 1 has only a single fish, so the
        // smallest-first scan has to fall through to quality 4.
        int[] counts = { 0, 1, 0, 0, 3, 0 };

        Assert.Equal(4, BonusRules.PickQuality(counts, needed: 2, largestFirst: false));
    }

    [Fact]
    public void AcceptsATierHoldingExactlyEnough()
    {
        int[] counts = { 0, 0, 2, 0 };

        Assert.Equal(2, BonusRules.PickQuality(counts, needed: 2, largestFirst: false));
    }

    [Fact]
    public void ReturnsNoQualityWhenNoSingleTierIsEnough()
    {
        // Two fish in total, but spread across tiers - vanilla would happily
        // mix them, so we decline and let it.
        int[] counts = { 0, 1, 1, 0 };

        Assert.Equal(BonusRules.NoQuality, BonusRules.PickQuality(counts, needed: 2, largestFirst: false));
    }

    [Fact]
    public void ReturnsNoQualityWhenTheBagIsEmpty()
    {
        Assert.Equal(BonusRules.NoQuality, BonusRules.PickQuality(new[] { 0, 0, 0 }, 1, false));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ReturnsNoQualityWhenNothingIsNeeded(int needed)
    {
        Assert.Equal(BonusRules.NoQuality, BonusRules.PickQuality(OneSmallOneBig, needed, false));
    }

    [Fact]
    public void HandlesMissingInputWithoutThrowing()
    {
        Assert.Equal(BonusRules.NoQuality, BonusRules.PickQuality(null, 1, false));
        Assert.Equal(BonusRules.NoQuality, BonusRules.PickQuality(new int[0], 1, false));
    }
}
