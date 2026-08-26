using FishQualityBonus.Logic;

namespace FishQualityBonus.Tests
{
    public class EligibilityTests
    {
        /// <summary>A recipe shaped like Fish 'n' Bread: one fish, plus dough.</summary>
        private static RecipeFacts Eligible()
        {
            return new()
            {
                HasOutput = true,
                RequireOnlyOneIngredient = false,
                OutputIsEquipment = false,
                IsMead = false,
                MeadsIncluded = true,
                ExplicitlyExcluded = false,
                FishRequirementCount = 1,
            };
        }

        [Fact]
        public void FishAndBreadShapedRecipeIsEligible()
        {
            Assert.Null(BonusRules.IneligibleReason(Eligible()));
        }

        [Fact]
        public void RecipeWithNoOutputIsRejected()
        {
            RecipeFacts facts = Eligible();
            facts.HasOutput = false;

            Assert.Equal("no output item", BonusRules.IneligibleReason(facts));
        }

        [Fact]
        public void SingleIngredientRecipesAreLeftToVanilla()
        {
            // Fish (raw) already scales itself; touching it would double-dip.
            RecipeFacts facts = Eligible();
            facts.RequireOnlyOneIngredient = true;

            Assert.Equal("vanilla already scales this by ingredient quality", BonusRules.IneligibleReason(facts));
        }

        [Fact]
        public void EquipmentIsRejected()
        {
            // The fishing hat consumes fish but must never be duplicated.
            RecipeFacts facts = Eligible();
            facts.OutputIsEquipment = true;

            Assert.Equal("output is equipment", BonusRules.IneligibleReason(facts));
        }

        [Fact]
        public void MeadIsRejectedOnlyWhenTheSettingIsOff()
        {
            RecipeFacts facts = Eligible();
            facts.IsMead = true;

            facts.MeadsIncluded = true;
            Assert.Null(BonusRules.IneligibleReason(facts));

            facts.MeadsIncluded = false;
            Assert.Equal("mead, and IncludeMeadRecipes is off", BonusRules.IneligibleReason(facts));
        }

        [Fact]
        public void ExplicitExclusionIsRespected()
        {
            RecipeFacts facts = Eligible();
            facts.ExplicitlyExcluded = true;

            Assert.Equal("listed in ExcludedRecipes", BonusRules.IneligibleReason(facts));
        }

        [Fact]
        public void RecipeWithoutFishIsRejected()
        {
            RecipeFacts facts = Eligible();
            facts.FishRequirementCount = 0;

            Assert.Equal("uses no fish", BonusRules.IneligibleReason(facts));
        }

        [Fact]
        public void RecipeWantingSeveralSpeciesIsRejected()
        {
            // The fishing hat asks for one of each of the twelve, so "scale the
            // output by the fish" has no single answer.
            RecipeFacts facts = Eligible();
            facts.FishRequirementCount = 12;

            Assert.Equal("uses 12 different fish", BonusRules.IneligibleReason(facts));
        }

        [Fact]
        public void EquipmentIsReportedAheadOfTheFishCount()
        {
            // The fishing hat trips both rules. The equipment reason is the more
            // useful one to show in the diagnostic report.
            RecipeFacts facts = Eligible();
            facts.OutputIsEquipment = true;
            facts.FishRequirementCount = 12;

            Assert.Equal("output is equipment", BonusRules.IneligibleReason(facts));
        }
    }
}
