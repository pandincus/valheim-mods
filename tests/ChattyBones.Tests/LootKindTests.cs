using ChattyBones.Logic;

namespace ChattyBones.Tests
{
    /// <summary>
    /// Covers telling treasure from firewood.
    /// </summary>
    /// <remarks>
    /// Every case below is a real item, because the four tests are only defensible as
    /// a set if they sort the actual contents of a Valheim inventory - the rule is
    /// "any one of these is enough", so what matters is which item trips which.
    ///
    /// The numbers are what the game files say: a max stack of 50 for materials and 1
    /// for anything you wield or wear, a coin value that is 0 for everything you can
    /// chop down, and a quality that stays 1 until you upgrade something.
    /// </remarks>
    public class LootKindTests
    {
        [Fact]
        public void FirewoodIsNotWorthMentioning()
        {
            // Wood, stone, resin, feathers - the hundreds of pickups a session is
            // actually made of, and the reason the filter exists at all.
            Assert.False(LootKind.IsNotable(trophy: false, coinValue: 0, maxStackSize: 50, quality: 1));
        }

        [Fact]
        public void ATrophyIsAlwaysWorthMentioning()
        {
            // You went and got it, and the game files trophies as their own type.
            Assert.True(LootKind.IsNotable(trophy: true, coinValue: 0, maxStackSize: 20, quality: 1));
        }

        [Fact]
        public void AnythingATraderWouldPayForCounts()
        {
            // Coins, amber, rubies. A coin value is how the game already separates
            // treasure from material, so this needs no notion of treasure of its own.
            Assert.True(LootKind.IsNotable(trophy: false, coinValue: 20, maxStackSize: 100, quality: 1));
        }

        [Fact]
        public void AnythingThatDoesNotStackCounts()
        {
            // Valheim stacks its materials fifty deep and its swords not at all, so a
            // max stack of one is a good proxy for an object rather than some stuff.
            Assert.True(LootKind.IsNotable(trophy: false, coinValue: 0, maxStackSize: 1, quality: 1));
        }

        [Fact]
        public void AnUpgradedItemCounts()
        {
            // Quality above one means somebody put work into it, so picking it back up
            // is not the same as finding a fresh one.
            Assert.True(LootKind.IsNotable(trophy: false, coinValue: 0, maxStackSize: 10, quality: 3));
        }

        [Fact]
        public void ANonsenseStackSizeStillReadsAsAnObject()
        {
            // A max stack of zero should not be possible, and a mod that manages it
            // should get "notable" rather than an argument. The test exists because
            // <= reads as a typo for == unless somebody says otherwise.
            Assert.True(LootKind.IsNotable(trophy: false, coinValue: 0, maxStackSize: 0, quality: 1));
        }
    }
}
