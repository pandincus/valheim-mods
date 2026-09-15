namespace BubbleBar.Logic
{
    /// <summary>
    /// The two values a barrier's owner publishes so other clients can draw it.
    /// </summary>
    /// <remarks>
    /// Only the owner of a character runs its status effects at all -
    /// <c>Character.CustomFixedUpdate</c> calls <c>m_seman.Update</c> inside
    /// <c>if (zDO.IsOwner())</c> - so on every other machine the SE_Shield object
    /// simply does not exist. The bubble everyone can see is the effect's start
    /// effect, a separate networked prefab, and it carries none of the numbers.
    ///
    /// Vanilla has exactly this problem with health and solves it exactly this
    /// way: <c>Character.SetHealth</c> writes <c>ZDOVars.s_health</c> when it owns
    /// the ZDO and <c>GetHealth</c> reads it back on every client. The health bar
    /// over another player is a mirrored float. So is this.
    ///
    /// Two keys rather than one, and the second is the interesting one. Ownership
    /// can move - a creature whose owner walks out of range is claimed by whoever
    /// is left - and SEMan is a plain C# list on the owning client, so the new
    /// owner has no SE_Shield and nothing left to publish. Without a second value
    /// the last fraction written would sit in the ZDO for ever and we would draw a
    /// barrier that stopped existing minutes ago. Publishing when it *expires*
    /// closes that off for free, because a barrier always does: the reader drops
    /// anything whose deadline has passed, so an orphaned value fades on schedule
    /// instead of lingering.
    /// </remarks>
    public static class BubbleWire
    {
        /// <summary>ZDO key holding how much of the barrier is left, 0 to 1.</summary>
        /// <remarks>
        /// The names are here and the hashing is not, because <c>GetStableHashCode</c>
        /// lives in assembly_utils and this half of the mod compiles without the game.
        /// The mod half hashes these once at startup.
        /// </remarks>
        public const string FractionKey = "BubbleBar.fraction";

        /// <summary>
        /// ZDO key holding the network time at which the barrier runs out, in whole
        /// milliseconds.
        /// </summary>
        /// <remarks>
        /// A long rather than a float, and that is not fussiness. Network time is the world's
        /// total played seconds, saved and restored, so on a long-lived server it grows without
        /// bound - and past about 33.5 million seconds a float's spacing exceeds two, at which
        /// point <c>now + GraceSeconds</c> rounds straight back to <c>now</c> and a barrier
        /// with no duration becomes invisible to every remote client. The same coarseness makes
        /// the deadline oscillate between two representable values, re-sending the ZDO to every
        /// peer twice a second. Milliseconds in a long are exact for any world that will ever
        /// exist, and cost the same four bytes to think about.
        /// </remarks>
        public const string ExpiryKey = "BubbleBar.expiry";

        /// <summary>
        /// Smallest change in fraction worth spending a ZDO write on.
        /// </summary>
        /// <remarks>
        /// A barrier's fraction only moves when it is hit, so writes are already rare -
        /// this is about a flurry rather than a steady drip. A bar drawn 60 pixels wide
        /// cannot show 1/256th of itself, so a hit that moves a 400-point barrier by
        /// less than about 1.6 points has nothing to say.
        /// </remarks>
        public const float WriteEpsilon = 1f / 256f;

        /// <summary>
        /// How far the deadline has to move, in milliseconds, before it is worth republishing.
        /// </summary>
        /// <remarks>
        /// The deadline is <c>now + remaining</c>, and those two move in opposite directions at
        /// the same rate, so a barrier running normally has a deadline that barely changes - it
        /// only jitters, because network time and the status effect's own clock advance from
        /// different sources. Half a second is far wider than that jitter and far narrower than
        /// a refresh, which throws the deadline forward by the barrier's whole duration.
        ///
        /// Without this the value is rewritten on every sweep. Vanilla bumps a ZDO's data
        /// revision whenever a stored value actually differs, and a revision bump is what sends
        /// it to every peer in range, so jitter alone would replicate ten times a second for as
        /// long as anybody had a barrier up.
        /// </remarks>
        public const long ExpiryDriftMillis = 500L;

        /// <summary>
        /// Turn a pair of published values into a reading.
        /// </summary>
        /// <returns>
        /// What to draw, or <see cref="ShieldReading.None"/> when the barrier has
        /// expired or was never published.
        /// </returns>
        /// <param name="fraction">The published <see cref="FractionKey"/> value.</param>
        /// <param name="expiry">The published <see cref="ExpiryKey"/> value, in milliseconds.</param>
        /// <param name="now">Network time now, in milliseconds.</param>
        /// <remarks>
        /// Expiry doubles as the "is there one at all" flag, which is why a fraction of
        /// zero still reads as present while the deadline holds. That matters for one
        /// frame at the end of a barrier's life: it has soaked everything and is about
        /// to pop, and an empty bar says that better than no bar does.
        ///
        /// A character with none of our keys set reads 0 for both, and 0 is never
        /// ahead of the clock, so it falls out as None without a special case.
        /// </remarks>
        public static ShieldReading Read(float fraction, long expiry, long now)
        {
            if (expiry <= now)
            {
                return ShieldReading.None;
            }

            return ShieldReading.FromFraction(fraction);
        }

        /// <summary>
        /// Decide whether a new fraction is different enough to publish.
        /// </summary>
        /// <returns>True when the value should go into the ZDO.</returns>
        /// <param name="published">What we last wrote.</param>
        /// <param name="current">What the barrier reads now.</param>
        /// <remarks>
        /// Vanilla's <c>SetHealth</c> gained the same guard in 1.0 - it compares
        /// against an <c>m_lastHealth</c> field before writing - so this is the
        /// house style rather than an optimization I invented.
        ///
        /// Reaching 0 always writes, whatever the epsilon says. It is the last value
        /// anyone sees before the barrier goes, and rounding it away would leave a
        /// sliver of bar on screen at the moment it breaks.
        /// </remarks>
        public static bool ShouldWrite(float published, float current)
        {
            if (current <= 0f)
            {
                return published > 0f;
            }

            float moved = published > current ? published - current : current - published;

            return moved >= WriteEpsilon;
        }

        /// <summary>
        /// Decide whether the deadline has moved enough to be worth publishing again.
        /// </summary>
        /// <returns>True when the value should go into the ZDO.</returns>
        /// <param name="published">The deadline we last wrote, in milliseconds.</param>
        /// <param name="current">The deadline the barrier implies now, in milliseconds.</param>
        /// <remarks>
        /// See <see cref="ExpiryDriftMillis"/> for why the threshold is so much coarser than
        /// the one on the fraction. A barrier that has never been published reads 0 here, which
        /// is always further than half a second from a real deadline, so the first write needs
        /// no special case.
        /// </remarks>
        public static bool ShouldWriteExpiry(long published, long current)
        {
            long moved = published > current ? published - current : current - published;

            return moved > ExpiryDriftMillis;
        }
    }
}
