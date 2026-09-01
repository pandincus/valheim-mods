namespace ChattyBones.Logic
{
    /// <summary>
    /// Which quarter of the day it is, for the <c>time</c> context.
    /// </summary>
    /// <remarks>
    /// Here rather than beside the resolver so the boundaries can be tested. The
    /// resolver's own job is one call to <c>EnvMan.GetDayFraction()</c>; the part worth
    /// pinning is where each band starts.
    ///
    /// The quarters are not a division I invented. Vanilla cuts the day at exactly these
    /// points, though its own bands overlap at them: <c>CalculateNight</c> is
    /// <c>f &lt;= 0.25 || f &gt;= 0.75</c> and <c>CalculateDay</c> is <c>0.25 &lt;= f &lt;= 0.75</c>,
    /// both ends closed, so the game calls 0.25 night *and* day and calls 0.75 afternoon
    /// *and* night. Ours pick one of vanilla's two answers at each of those points rather
    /// than disagreeing with it, and agree outright everywhere in between. The only thing
    /// added is splitting its long night in two at midnight. A fraction of 0.5 is noon and
    /// 0.0 is midnight, which is what makes the split land where you would want it:
    /// evening is sunset to midnight, night is midnight to dawn.
    ///
    /// I did weigh using <c>IsNight</c> and <c>IsAfternoon</c> directly instead. They
    /// are public statics and it would have been less code, but they give three bands
    /// rather than four and there is no <c>IsEvening</c> to borrow - so the fraction has
    /// to be read anyway, and reading it once is tidier than reading it once and two
    /// booleans besides.
    /// </remarks>
    internal static class TimeOfDay
    {
        /// <summary>The four band names, in the order the day runs.</summary>
        /// <remarks>
        /// The one place these words are written down. <see cref="EventKey"/> takes the
        /// pack's vocabulary from here and the resolver spells its contexts from here, so
        /// adding a fifth band cannot leave either of them behind - which it could when
        /// each kept its own copy, and the resolver's copy was a dictionary indexer on the
        /// per-utterance path, so a missed band would have thrown rather than gone quiet.
        /// </remarks>
        internal static readonly string[] All = ["morning", "afternoon", "evening", "night"];

        /// <summary>Name the quarter of the day a fraction falls in.</summary>
        /// <returns>One of "morning", "afternoon", "evening" or "night".</returns>
        /// <param name="dayFraction">Where the day has got to, 0 at midnight and 0.5 at noon.</param>
        /// <remarks>
        /// Boundaries belong to the band that starts there, so exactly 0.25 is morning
        /// and exactly 0.75 is evening.
        ///
        /// There is no range check, and there was one until a review pointed out it could
        /// not change a single answer: anything below 0 is already below 0.25 and anything
        /// above 1 already falls past the last test, so clamping first gave the same word
        /// every time. The table is total - every float lands in exactly one band - and
        /// practically speaking <c>GetDayFraction</c> hands over a <c>Mathf.Repeat</c>
        /// result that was never outside 0 to 1 anyway.
        /// </remarks>
        internal static string Band(float dayFraction)
        {
            if (dayFraction < 0.25f)
            {
                return "night";
            }

            if (dayFraction < 0.5f)
            {
                return "morning";
            }

            return dayFraction < 0.75f ? "afternoon" : "evening";
        }
    }
}
