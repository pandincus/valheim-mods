using BubbleBar.Logic;

namespace BubbleBar.Tests
{
    public class ShieldReadingTests
    {
        [Fact]
        public void AnUntouchedBarrierReadsFull()
        {
            ShieldReading reading = ShieldReading.For(absorbed: 0f, capacity: 200f);

            Assert.True(reading.IsPresent);
            Assert.Equal(1f, reading.Fraction);
        }

        [Theory]
        [InlineData(50f, 0.75f)]
        [InlineData(100f, 0.5f)]
        [InlineData(190f, 0.05f)]
        public void APartlySpentBarrierReadsWhatIsLeft(float absorbed, float expected)
        {
            ShieldReading reading = ShieldReading.For(absorbed, capacity: 200f);

            Assert.True(reading.IsPresent);
            Assert.Equal(expected, reading.Fraction, 5);
        }

        [Fact]
        public void ABarrierSoakingMoreThanItsCapacityReadsEmptyRatherThanNegative()
        {
            // This is the ordinary state of a barrier about to break, not an edge case:
            // SE_Shield adds the whole hit to m_damage and only then asks whether the
            // total has gone past capacity. A 90-damage hit landing on a 200 barrier
            // that has already soaked 150 leaves m_damage at 240.
            ShieldReading reading = ShieldReading.For(absorbed: 240f, capacity: 200f);

            Assert.True(reading.IsPresent);
            Assert.Equal(0f, reading.Fraction);
        }

        [Fact]
        public void AnEmptyBarrierIsStillPresent()
        {
            // The frame between soaking everything and IsDone popping it. A bar that
            // disappears here reads as a bug rather than as a barrier breaking.
            ShieldReading reading = ShieldReading.For(absorbed: 200f, capacity: 200f);

            Assert.True(reading.IsPresent);
            Assert.Equal(0f, reading.Fraction);
        }

        [Fact]
        public void NoCapacityMeansNoBarrier()
        {
            // What a clone reads for the frame between Setup and SetLevel, since
            // m_totalAbsorbDamage is computed in SetLevel and starts at zero.
            Assert.False(ShieldReading.For(absorbed: 0f, capacity: 0f).IsPresent);
            Assert.False(ShieldReading.For(absorbed: 10f, capacity: -1f).IsPresent);
        }

        [Fact]
        public void TheDefaultValueIsNoBarrier()
        {
            Assert.False(default(ShieldReading).IsPresent);
            Assert.False(ShieldReading.None.IsPresent);
        }
    }
}
