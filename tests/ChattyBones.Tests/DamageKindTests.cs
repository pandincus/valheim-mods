using ChattyBones.Logic;

namespace ChattyBones.Tests
{
    /// <summary>
    /// Covers naming the damage type a hit was mostly made of.
    /// </summary>
    /// <remarks>
    /// Nearly every real hit sets more than one of these - a fire sword does slash
    /// and fire together - so the interesting cases are all about which one wins,
    /// not about reading a single number back.
    /// </remarks>
    public class DamageKindTests
    {
        /// <summary>Call it with only one type set, the way a plain weapon lands.</summary>
        /// <returns>Whatever the dominant type came out as.</returns>
        /// <param name="which">Which of the eight to set.</param>
        /// <param name="amount">How much of it.</param>
        private static string Only(string which, float amount = 10f)
        {
            return DamageKind.Dominant(
                blunt: which == "blunt" ? amount : 0f,
                slash: which == "slash" ? amount : 0f,
                pierce: which == "pierce" ? amount : 0f,
                fire: which == "fire" ? amount : 0f,
                frost: which == "frost" ? amount : 0f,
                lightning: which == "lightning" ? amount : 0f,
                poison: which == "poison" ? amount : 0f,
                spirit: which == "spirit" ? amount : 0f);
        }

        [Theory]
        [InlineData("blunt")]
        [InlineData("slash")]
        [InlineData("pierce")]
        [InlineData("fire")]
        [InlineData("frost")]
        [InlineData("lightning")]
        [InlineData("poison")]
        [InlineData("spirit")]
        public void EachTypeCanBeNamed(string type)
        {
            // The word is what the caller asks for; the answer is the key vanilla
            // uses for it on an item tooltip, so it reaches a player in their language.
            Assert.Equal("$inventory_" + type, Only(type));
        }

        [Fact]
        public void TheBiggestNumberWins()
        {
            // A frostner-ish hit: mostly blunt, some frost.
            Assert.Equal(
                "$inventory_blunt",
                DamageKind.Dominant(
                    blunt: 40f, slash: 0f, pierce: 0f, fire: 0f,
                    frost: 15f, lightning: 0f, poison: 0f, spirit: 0f));
        }

        [Fact]
        public void AnEnchantDoesNotStealTheDescription()
        {
            // The case the tie-break exists for. A fire sword is a sword, and "nice
            // slash hit" describes it better than "nice fire hit" would.
            Assert.Equal(
                "$inventory_slash",
                DamageKind.Dominant(
                    blunt: 0f, slash: 30f, pierce: 0f, fire: 30f,
                    frost: 0f, lightning: 0f, poison: 0f, spirit: 0f));
        }

        [Fact]
        public void NothingAtAllHasNoName()
        {
            // Commoner than it looks - a hit can be pure stagger, or have everything
            // absorbed before it reaches us.
            Assert.Null(
                DamageKind.Dominant(
                    blunt: 0f, slash: 0f, pierce: 0f, fire: 0f,
                    frost: 0f, lightning: 0f, poison: 0f, spirit: 0f));
        }

        [Fact]
        public void NegativeDamageIsNotADamageType()
        {
            // Resistances are applied before this, and nothing stops the result going
            // below zero. A hit that healed you is not a "blunt" hit.
            Assert.Null(
                DamageKind.Dominant(
                    blunt: -5f, slash: 0f, pierce: 0f, fire: 0f,
                    frost: 0f, lightning: 0f, poison: 0f, spirit: 0f));
        }

        [Fact]
        public void ATinyAmountStillCounts()
        {
            // No threshold, deliberately. If the only thing a hit did was a sliver of
            // poison, poison is what it was.
            Assert.Equal("$inventory_poison", Only("poison", 0.01f));
        }
    }
}
