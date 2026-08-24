using FishQualityBonus;

namespace FishQualityBonusTests;

public class ComputeBonusTests
{
    /// <summary>One fish of the given quality, which is what most crafts spend.</summary>
    private static int OneFish(int quality, int recipeAmount, int perQualityLevel, int speciesExtra)
        => BonusRules.ComputeBonus(PlanOf((quality, 1)), recipeAmount, perQualityLevel, speciesExtra);

    /// <summary>
    /// A plan spending exactly the given fish, e.g. PlanOf((1, 1), (2, 1)) for one
    /// quality-1 and one quality-2.
    /// </summary>
    /// <remarks>
    /// FishPlan's constructor is private, so this goes through TryPick with a bag holding
    /// precisely what the plan should spend - the greedy fill then takes all of it. That
    /// couples these arithmetic tests to the picker, which is the point: we only ever
    /// price plans the picker can actually produce.
    /// </remarks>
    private static FishPlan PlanOf(params (int Quality, int Count)[] fish)
    {
        int maxQuality = 0;
        foreach ((int quality, _) in fish)
        {
            if (quality > maxQuality) maxQuality = quality;
        }

        var counts = new int[maxQuality + 1];
        int needed = 0;
        foreach ((int quality, int count) in fish)
        {
            counts[quality] += count;
            needed += count;
        }

        Assert.True(FishPlan.TryPick(counts, needed, largestFirst: false,
                                     allowMixed: true, out FishPlan plan));
        return plan;
    }

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
        int total = 1 + OneFish(quality, recipeAmount: 1, perQualityLevel: 3, speciesExtra);

        Assert.Equal(expectedTotal, total);
    }

    [Fact]
    public void QualityOneEarnsNoScaledBonus()
    {
        Assert.Equal(0, OneFish(1, recipeAmount: 1, perQualityLevel: 3, speciesExtra: 0));
    }

    [Fact]
    public void SpeciesBonusIsFlatAndAppliesEvenAtQualityOne()
    {
        // Vanilla does not scale this term by quality - that is the whole
        // reason UseSpeciesBonus is a separate setting.
        Assert.Equal(2, OneFish(1, recipeAmount: 1, perQualityLevel: 3, speciesExtra: 2));
    }

    [Fact]
    public void ScalesWithTheRecipeOutputAmount()
    {
        // A recipe making 2 at a time gets twice the per-level bonus.
        Assert.Equal(12, OneFish(3, recipeAmount: 2, perQualityLevel: 3, speciesExtra: 0));
    }

    [Theory]
    [InlineData(1, 4)]
    [InlineData(3, 12)]
    [InlineData(10, 40)]
    public void HonoursThePerLevelMultiplier(int perQualityLevel, int expected)
    {
        Assert.Equal(expected, OneFish(5, recipeAmount: 1, perQualityLevel, speciesExtra: 0));
    }

    [Fact]
    public void NeverReturnsANegativeBonus()
    {
        // recipeAmount and speciesExtra come from game data rather than from us,
        // so they are still worth clamping. The quality term needs no clamp: every
        // fish counts as at least quality 1, so TotalQuality - TotalFish cannot go
        // negative.
        Assert.Equal(0, BonusRules.ComputeBonus(PlanOf((1, 1)),
                                                recipeAmount: -5, perQualityLevel: 3, speciesExtra: -9));
    }

    [Fact]
    public void TheEmptyPlanEarnsNoSizeBonus()
    {
        // Defensive: a plan is never empty by the time it reaches here, and
        // dividing by its fish count would throw if it were.
        Assert.Equal(0, BonusRules.ComputeBonus(default, recipeAmount: 1,
                                                perQualityLevel: 3, speciesExtra: 0));
    }

    public class MixedQualities
    {
        [Fact]
        public void PaysTheAverageOfTheFishSpent()
        {
            // The case that prompted the feature: one quality-1 and one quality-2
            // trollfish for a mead base that makes 1. Average is 1.5, so the bonus
            // is 1 and you brew 2. Trollfish is a +0 species, nothing on top.
            Assert.Equal(1, BonusRules.ComputeBonus(PlanOf((1, 1), (2, 1)),
                                                    recipeAmount: 1, perQualityLevel: 3, speciesExtra: 0));
        }

        [Fact]
        public void SitsBetweenTheTwoUniformCrafts()
        {
            // Two quality-1 give 0 and two quality-2 give 3, so the mixed pair
            // has to land somewhere in between.
            int twoSmall = BonusRules.ComputeBonus(PlanOf((1, 2)), 1, perQualityLevel: 3, speciesExtra: 0);
            int mixed = BonusRules.ComputeBonus(PlanOf((1, 1), (2, 1)), 1, perQualityLevel: 3, speciesExtra: 0);
            int twoBig = BonusRules.ComputeBonus(PlanOf((2, 2)), 1, perQualityLevel: 3, speciesExtra: 0);

            Assert.Equal(0, twoSmall);
            Assert.Equal(3, twoBig);
            Assert.InRange(mixed, twoSmall, twoBig);
        }

        [Fact]
        public void MatchesTheUniformResultWhenEveryFishIsTheSameSize()
        {
            // Three quality-4 fish: 3 points each. This has to agree with the
            // one-fish answer exactly, or the mod's payout changed at 0.2.0 for
            // crafts that were never mixed.
            int threeFish = BonusRules.ComputeBonus(PlanOf((4, 3)),
                                                    recipeAmount: 2, perQualityLevel: 3, speciesExtra: 1);
            int oneFish = BonusRules.ComputeBonus(PlanOf((4, 1)),
                                                  recipeAmount: 2, perQualityLevel: 3, speciesExtra: 1);

            Assert.Equal(oneFish, threeFish);
        }

        [Fact]
        public void RoundsDownRatherThanPayingForFishYouDidNotSpend()
        {
            // Two quality-1 and one quality-2 across a three-fish craft: one
            // point spread over three fish, so a third of a level. The mod pays
            // nothing rather than rounding a partial level up into a whole item.
            Assert.Equal(0, BonusRules.ComputeBonus(PlanOf((1, 2), (2, 1)),
                                                    recipeAmount: 1, perQualityLevel: 1, speciesExtra: 0));
        }

        [Fact]
        public void NeverBeatsTheSameCraftWithEveryFishRoundedUp()
        {
            // Floor, not round: a mixed craft must not out-earn the uniform
            // craft at the better quality.
            int mixed = BonusRules.ComputeBonus(PlanOf((3, 1), (5, 1)),
                                                recipeAmount: 1, perQualityLevel: 3, speciesExtra: 0);
            int uniformHigher = BonusRules.ComputeBonus(PlanOf((4, 2)),
                                                        recipeAmount: 1, perQualityLevel: 3, speciesExtra: 0);

            Assert.True(mixed <= uniformHigher);
        }
    }
}
