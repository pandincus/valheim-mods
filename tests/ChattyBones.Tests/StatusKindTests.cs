using ChattyBones.Logic;

namespace ChattyBones.Tests
{
    /// <summary>
    /// Covers telling a blessing from an affliction.
    /// </summary>
    /// <remarks>
    /// This table is the whole feature. Valheim has no flag saying whether an effect
    /// is good or bad - StatusEffect.m_attributes is about cold resistance and
    /// sailing - so the subclass name is the only signal there is, and getting an
    /// entry wrong means a skeleton thanking you for setting it on fire.
    /// </remarks>
    public class StatusKindTests
    {
        [Theory]
        [InlineData("SE_Burning")]
        [InlineData("SE_Frost")]
        [InlineData("SE_Poison")]
        [InlineData("SE_Wet")]
        [InlineData("SE_Smoke")]
        [InlineData("SE_Puke")]
        [InlineData("SE_Harpooned")]
        public void TheOnesThatHurtAreRecognized(string typeName)
        {
            Assert.True(StatusKind.IsHarmful(typeName));
        }

        [Theory]
        [InlineData("SE_Shield")]
        [InlineData("SE_Rested")]
        [InlineData("SE_Cozy")]
        [InlineData("SE_Stats")]
        [InlineData("SE_HealthUpgrade")]
        [InlineData("SE_Demister")]
        [InlineData("SE_Finder")]
        public void TheOnesThatHelpAreNot(string typeName)
        {
            // SE_Shield is the one this mod was built around - the Staff of Protection
            // does apply to summons, and "Much obliged" is the reason Buffed exists.
            Assert.False(StatusKind.IsHarmful(typeName));
        }

        [Theory]
        [InlineData("SE_SomeModAddedThis")]
        [InlineData("")]
        [InlineData(null)]
        public void AnythingUnknownIsTreatedAsABuff(string typeName)
        {
            // The safer of the two wrong answers. Thanking somebody for a modded
            // effect we do not recognize is merely odd; screaming about a shield
            // would be worse, and a modded effect still gets Buffed lines.
            Assert.False(StatusKind.IsHarmful(typeName));
        }

        [Fact]
        public void MatchingIsExactRatherThanFuzzy()
        {
            // Guards against a "starts with SE_" or "contains Burn" shortcut creeping
            // in later, which would drag unrelated effects in with it.
            Assert.False(StatusKind.IsHarmful("SE_Burning_Extra"));
            Assert.False(StatusKind.IsHarmful("Burning"));
            Assert.False(StatusKind.IsHarmful("se_burning"));
        }
    }
}
