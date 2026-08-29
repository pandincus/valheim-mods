using ChattyBones.Logic;

namespace ChattyBones.Tests
{
    /// <summary>
    /// Covers telling a blessing from an affliction.
    /// </summary>
    /// <remarks>
    /// This table is the whole feature, and getting an entry wrong means a skeleton
    /// thanking you for setting it on fire.
    /// </remarks>
    public class StatusKindTests
    {
        [Theory]
        [InlineData("Burning")]
        [InlineData("Spirit")]
        [InlineData("Frost")]
        [InlineData("Poison")]
        [InlineData("Smoked")]
        [InlineData("Puke")]
        [InlineData("Harpooned")]
        public void TheOnesThatHurtAreRecognized(string effectName)
        {
            Assert.True(StatusKind.IsHarmful(effectName));
        }

        [Theory]
        [InlineData("Tared")]
        [InlineData("Lightning")]
        [InlineData("Slimed")]
        public void TheOnesWithNoSubclassOfTheirOwnAreRecognizedToo(string effectName)
        {
            // The reason this keys on the asset name rather than the runtime type.
            // Tar, lightning and slime are plain StatusEffect or SE_Stats, so a
            // type-name check called all three of them buffs - and a skeleton wading
            // into a Plains tar pit said "Ooh, that's the stuff."
            Assert.True(StatusKind.IsHarmful(effectName));
        }

        [Theory]
        [InlineData("Shield")]
        [InlineData("Rested")]
        [InlineData("Shelter")]
        [InlineData("CampFire")]
        public void TheOnesThatHelpAreNot(string effectName)
        {
            // Shield is the one this mod was built around - the Staff of Protection
            // does apply to summons, and "Much obliged" is the reason Buffed exists.
            Assert.False(StatusKind.IsHarmful(effectName));
        }

        [Theory]
        [InlineData("Wet")]
        [InlineData("Cold")]
        [InlineData("Freezing")]
        public void TheWeatherIsNotAnInjury(string effectName)
        {
            // The distinction the Weather event exists for. Wet is acquired constantly
            // - any water at all, rain included - and Afflicted outranks the kill
            // events, so calling it harmful meant a skeleton wading into a swamp
            // talking over its own victories to mention it is damp.
            Assert.False(StatusKind.IsHarmful(effectName));
            Assert.True(StatusKind.IsWeather(effectName));
        }

        [Theory]
        [InlineData("Burning")]
        [InlineData("Shield")]
        [InlineData("SomeModAddedThis")]
        [InlineData(null)]
        public void EverythingElseIsNotTheWeather(string effectName)
        {
            Assert.False(StatusKind.IsWeather(effectName));
        }

        [Theory]
        [InlineData("SomeModAddedThis")]
        [InlineData("")]
        [InlineData(null)]
        public void AnythingUnknownIsTreatedAsABuff(string typeName)
        {
            Assert.False(StatusKind.IsHarmful(typeName));
        }

        [Fact]
        public void MatchingIsExactRatherThanFuzzy()
        {
            // Guards against a "starts with SE_" or "contains Burn" shortcut creeping
            // in later, which would drag unrelated effects in with it.
            Assert.False(StatusKind.IsHarmful("BurningExtra"));
            Assert.False(StatusKind.IsHarmful("Burn"));
            Assert.False(StatusKind.IsHarmful("burning"));
        }
    }
}
