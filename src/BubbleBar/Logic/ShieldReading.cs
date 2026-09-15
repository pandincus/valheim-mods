namespace BubbleBar.Logic
{
    /// <summary>
    /// How much of a magic barrier is left, as a fraction.
    /// </summary>
    /// <remarks>
    /// A reading comes from one of two places and the bar cannot tell them apart,
    /// which is the point of having a single type for both. When we own the
    /// character we read <c>SE_Shield</c>'s own fields and build one with
    /// <see cref="For"/>; when somebody else owns it we read the float its client
    /// published and build one with <c>BubbleWire.Read</c>.
    ///
    /// I kept this to a fraction rather than carrying the absolute numbers around.
    /// The absolute pair is only knowable locally, so putting it in the shared type
    /// would mean a field that is populated half the time - and a bar does not want
    /// it anyway. If we ever show "150/200" over your own head, that reads
    /// SE_Shield directly at the point of drawing rather than travelling here.
    ///
    /// The default value is "no barrier", which is what a character without one
    /// should read as, and what a ZDO with none of our keys set decodes to.
    /// </remarks>
    public readonly struct ShieldReading
    {
        /// <summary>No barrier: what a character reads when it has none.</summary>
        public static readonly ShieldReading None = default;

        private ShieldReading(float fraction)
        {
            IsPresent = true;
            Fraction = fraction;
        }

        /// <summary>True when there is a barrier to draw at all.</summary>
        /// <remarks>
        /// Distinct from <c>Fraction &gt; 0</c> on purpose. A barrier that has soaked
        /// exactly its capacity is present, at zero, for the moment before
        /// <c>SE_Shield.IsDone</c> pops it - and a bar that vanishes a frame early
        /// looks like a bug rather than like a barrier breaking.
        /// </remarks>
        public bool IsPresent { get; }

        /// <summary>How much is left, from 0 (spent) to 1 (untouched).</summary>
        public float Fraction { get; }

        /// <summary>
        /// Build a reading from the two numbers SE_Shield keeps.
        /// </summary>
        /// <returns>
        /// The reading, or <see cref="None"/> when there is no capacity to divide by.
        /// </returns>
        /// <param name="absorbed">Damage soaked so far - SE_Shield's <c>m_damage</c>.</param>
        /// <param name="capacity">The total it can soak - <c>m_totalAbsorbDamage</c>.</param>
        /// <remarks>
        /// A capacity of zero means no barrier rather than a barrier that breaks on
        /// the first hit, because that is what an unset field looks like: SE_Shield
        /// computes <c>m_totalAbsorbDamage</c> in SetLevel, so a clone that has not
        /// been levelled yet reads zero for a frame.
        ///
        /// Absorbed is clamped both ways. It arrives above capacity routinely - a
        /// barrier of 200 that takes a 90-damage hit at 150 soaked reads 240, which
        /// is exactly the state that makes IsDone pop it - and the bar wants 0 there,
        /// not a negative fraction.
        /// </remarks>
        public static ShieldReading For(float absorbed, float capacity)
        {
            if (capacity <= 0f)
            {
                return None;
            }

            float remaining = capacity - absorbed;

            if (remaining <= 0f)
            {
                return new ShieldReading(0f);
            }

            return new ShieldReading(remaining >= capacity ? 1f : remaining / capacity);
        }

        /// <summary>Build a reading straight from a fraction, for the mirrored path.</summary>
        /// <returns>The reading, or <see cref="None"/> when the fraction is not a real one.</returns>
        /// <param name="fraction">How much is left, 0 to 1.</param>
        internal static ShieldReading FromFraction(float fraction)
        {
            if (float.IsNaN(fraction) || fraction < 0f)
            {
                return None;
            }

            return new ShieldReading(fraction > 1f ? 1f : fraction);
        }
    }
}
