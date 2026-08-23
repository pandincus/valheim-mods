using FishQualityBonus;

namespace FishQualityBonusTests;

public class PickFishTests
{
    // Index = quality, value = how many of that quality are in the bag.
    // Index 0 exists because vanilla's own scan starts at quality 0.
    private static readonly int[] OneSmallOneBig = { 0, 1, 0, 0, 1, 0 };

    private static int[] Pick(int[] counts, int needed, bool largestFirst = false, bool allowMixed = true)
    {
        Assert.True(BonusRules.TryPickFish(counts, needed, largestFirst, allowMixed, out int[] plan));
        return plan;
    }

    private static void AssertCannotPick(int[] counts, int needed, bool largestFirst = false,
                                         bool allowMixed = true)
    {
        Assert.False(BonusRules.TryPickFish(counts, needed, largestFirst, allowMixed, out int[] plan));
        Assert.Null(plan);
    }

    [Fact]
    public void SmallestFirstSpendsTheWorstFishThatWillDo()
    {
        Assert.Equal(new[] { 0, 1, 0, 0, 0, 0 }, Pick(OneSmallOneBig, needed: 1));
    }

    [Fact]
    public void LargestFirstSpendsTheBestFish()
    {
        Assert.Equal(new[] { 0, 0, 0, 0, 1, 0 }, Pick(OneSmallOneBig, needed: 1, largestFirst: true));
    }

    [Fact]
    public void MixesQualitiesWhenNoSingleOneCanCoverTheCraft()
    {
        // The case that started all this: one quality-1 and one quality-2
        // trollfish, and a mead base that wants two of them. Vanilla refuses.
        int[] counts = { 0, 1, 1, 0 };

        Assert.Equal(new[] { 0, 1, 1, 0 }, Pick(counts, needed: 2));
    }

    [Fact]
    public void SmallestFirstSpendsTheSmallFishBeforeReachingForABigOne()
    {
        // One quality-1 and three quality-4, crafting 2 at once. The old
        // single-quality rule skipped the small fish entirely and burned two
        // quality-4, which is the opposite of what SmallestFirst promises.
        int[] counts = { 0, 1, 0, 0, 3, 0 };

        Assert.Equal(new[] { 0, 1, 0, 0, 1, 0 }, Pick(counts, needed: 2));
    }

    [Fact]
    public void LargestFirstWorksDownFromTheTop()
    {
        int[] counts = { 0, 5, 0, 1, 2, 0 };

        Assert.Equal(new[] { 0, 0, 0, 1, 2, 0 }, Pick(counts, needed: 3, largestFirst: true));
    }

    [Fact]
    public void TakesOnlyAsManyAsTheCraftNeeds()
    {
        int[] counts = { 0, 9, 0, 0 };

        Assert.Equal(new[] { 0, 2, 0, 0 }, Pick(counts, needed: 2));
    }

    [Fact]
    public void FailsWhenThereAreNotEnoughFishInTotal()
    {
        int[] counts = { 0, 1, 1, 0 };

        AssertCannotPick(counts, needed: 3);
    }

    [Fact]
    public void FailsWhenTheBagIsEmpty()
    {
        AssertCannotPick(new[] { 0, 0, 0 }, needed: 1);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void FailsWhenNothingIsNeeded(int needed)
    {
        AssertCannotPick(OneSmallOneBig, needed);
    }

    [Fact]
    public void HandlesMissingInputWithoutThrowing()
    {
        AssertCannotPick(null, 1);
        AssertCannotPick(new int[0], 1);
    }

    public class WithMixingSwitchedOff
    {
        [Fact]
        public void RequiresOneQualityToCoverTheWholeCraft()
        {
            int[] counts = { 0, 1, 1, 0 };

            Assert.False(BonusRules.TryPickFish(counts, needed: 2, largestFirst: false,
                                                allowMixed: false, out int[] plan));
            Assert.Null(plan);
        }

        [Fact]
        public void SkipsTiersThatCannotCoverTheWholeCraft()
        {
            // Pre-0.2.0 behaviour, kept so the setting really does restore it:
            // the lone quality-1 fish is passed over and two quality-4 are spent.
            int[] counts = { 0, 1, 0, 0, 3, 0 };

            Assert.True(BonusRules.TryPickFish(counts, needed: 2, largestFirst: false,
                                               allowMixed: false, out int[] plan));
            Assert.Equal(new[] { 0, 0, 0, 0, 2, 0 }, plan);
        }

        [Fact]
        public void AcceptsATierHoldingExactlyEnough()
        {
            int[] counts = { 0, 0, 2, 0 };

            Assert.True(BonusRules.TryPickFish(counts, needed: 2, largestFirst: false,
                                               allowMixed: false, out int[] plan));
            Assert.Equal(new[] { 0, 0, 2, 0 }, plan);
        }
    }

    public class Totals
    {
        [Fact]
        public void CountsEveryFishInThePlan()
        {
            Assert.Equal(4, BonusRules.TotalFish(new[] { 0, 1, 2, 0, 1 }));
        }

        [Fact]
        public void AddsUpHowFarAboveQualityOneTheFishAre()
        {
            // One quality-2 (worth 1) and two quality-5 (worth 4 each).
            Assert.Equal(9, BonusRules.QualityPoints(new[] { 0, 0, 1, 0, 0, 2 }));
        }

        [Fact]
        public void QualityOneFishAreWorthNothing()
        {
            Assert.Equal(0, BonusRules.QualityPoints(new[] { 0, 6, 0 }));
        }

        [Fact]
        public void QualityZeroDoesNotEatAnotherFishsContribution()
        {
            // No real fish has quality 0, but vanilla scans from 0 so one could
            // theoretically reach us. It must contribute 0, not -1.
            Assert.Equal(1, BonusRules.QualityPoints(new[] { 1, 0, 1 }));
        }

        [Fact]
        public void HandleANullPlanWithoutThrowing()
        {
            Assert.Equal(0, BonusRules.TotalFish(null));
            Assert.Equal(0, BonusRules.QualityPoints(null));
        }
    }
}
