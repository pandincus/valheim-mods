using FishQualityBonus;

namespace FishQualityBonusTests;

public class PickFishTests
{
    // Index = quality, value = how many of that quality are in the bag.
    // Index 0 exists because vanilla's own scan starts at quality 0.
    private static readonly int[] OneSmallOneBig = { 0, 1, 0, 0, 1, 0 };

    private static int[] Pick(int[] counts, int needed, bool largestFirst = false, bool allowMixed = true)
    {
        Assert.True(FishPlan.TryPick(counts, needed, largestFirst, allowMixed, out FishPlan plan));
        return Spread(plan);
    }

    private static void AssertCannotPick(int[] counts, int needed, bool largestFirst = false,
                                         bool allowMixed = true)
    {
        Assert.False(FishPlan.TryPick(counts, needed, largestFirst, allowMixed, out FishPlan plan));
        Assert.Equal(0, plan.TotalFish);
    }

    /// <summary>
    /// Read a plan back out as a per-quality array, so a test can assert the whole shape
    /// in one go. Deliberately goes through CountAt rather than reaching inside, so these
    /// tests exercise the same surface the mod uses.
    /// </summary>
    private static int[] Spread(FishPlan plan)
    {
        var counts = new int[plan.MaxQuality + 1];
        for (int quality = 0; quality <= plan.MaxQuality; quality++)
        {
            counts[quality] = plan.CountAt(quality);
        }
        return counts;
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

            Assert.False(FishPlan.TryPick(counts, needed: 2, largestFirst: false,
                                          allowMixed: false, out FishPlan plan));
            Assert.Equal(0, plan.TotalFish);
        }

        [Fact]
        public void SkipsTiersThatCannotCoverTheWholeCraft()
        {
            // Pre-0.2.0 behaviour, kept so the setting really does restore it:
            // the lone quality-1 fish is passed over and two quality-4 are spent.
            int[] counts = { 0, 1, 0, 0, 3, 0 };

            Assert.True(FishPlan.TryPick(counts, needed: 2, largestFirst: false,
                                         allowMixed: false, out FishPlan plan));
            Assert.Equal(new[] { 0, 0, 0, 0, 2, 0 }, Spread(plan));
        }

        [Fact]
        public void AcceptsATierHoldingExactlyEnough()
        {
            int[] counts = { 0, 0, 2, 0 };

            Assert.True(FishPlan.TryPick(counts, needed: 2, largestFirst: false,
                                         allowMixed: false, out FishPlan plan));
            Assert.Equal(new[] { 0, 0, 2, 0 }, Spread(plan));
        }
    }

    public class Totals
    {
        /// <summary>
        /// A plan that spends exactly the fish described. The constructor is private, so
        /// tests build plans the same way the mod does - by asking TryPick for everything
        /// in a bag, which the greedy fill then takes in full.
        /// </summary>
        private static FishPlan PlanOf(params int[] countsByQuality)
        {
            int needed = 0;
            foreach (int count in countsByQuality) needed += count;

            Assert.True(FishPlan.TryPick(countsByQuality, needed, largestFirst: false,
                                         allowMixed: true, out FishPlan plan));
            return plan;
        }

        [Fact]
        public void CountsEveryFishInThePlan()
        {
            Assert.Equal(4, PlanOf(0, 1, 2, 0, 1).TotalFish);
        }

        [Fact]
        public void AddsUpHowFarAboveQualityOneTheFishAre()
        {
            // One quality-2 (worth 1) and two quality-5 (worth 4 each).
            Assert.Equal(9, PlanOf(0, 0, 1, 0, 0, 2).QualityPoints);
        }

        [Fact]
        public void QualityOneFishAreWorthNothing()
        {
            Assert.Equal(0, PlanOf(0, 6, 0).QualityPoints);
        }

        [Fact]
        public void QualityZeroDoesNotEatAnotherFishsContribution()
        {
            // No real fish has quality 0, but vanilla scans from 0 so one could
            // theoretically reach us. It must contribute 0, not -1.
            Assert.Equal(1, PlanOf(1, 0, 1).QualityPoints);
        }

        [Fact]
        public void ReportsTheHighestQualityItCanSpeakFor()
        {
            Assert.Equal(5, PlanOf(0, 1, 0, 0, 0, 1).MaxQuality);
        }

        [Fact]
        public void AnsweringAboutAQualityOutsideThePlanIsSafe()
        {
            FishPlan plan = PlanOf(0, 2, 0);

            Assert.Equal(2, plan.CountAt(1));
            Assert.Equal(0, plan.CountAt(99));
            Assert.Equal(0, plan.CountAt(-1));
        }

        [Fact]
        public void TheEmptyPlanSpendsNothingAndDoesNotThrow()
        {
            // default(FishPlan) is what TryPick hands back when it fails, and it is the
            // one plan you can build without going through TryPick - a struct always has
            // its zero value. Reading it has to be harmless.
            FishPlan empty = default;

            Assert.Equal(0, empty.TotalFish);
            Assert.Equal(0, empty.QualityPoints);
            Assert.Equal(0, empty.CountAt(3));
            // -1 so that a `for (q = 0; q <= MaxQuality; q++)` loop simply doesn't run.
            Assert.Equal(-1, empty.MaxQuality);
        }
    }
}
