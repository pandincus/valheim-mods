using System.Collections.Generic;

namespace FishQualityBonus
{
    /// <summary>
    /// Which fish a craft will spend, and at which qualities.
    /// </summary>
    /// <remarks>
    /// <see cref="TryPick"/> is the only way to build one, because the constructor is
    /// private. So a plan you are holding is always a plan the picker actually produced -
    /// it cannot claim fish the player does not have, or ignore the FishToSpend order.
    /// Everything else here is a question you can ask about the result.
    ///
    /// A struct cannot seal that completely: default(FishPlan) and new FishPlan() are
    /// always reachable in C# and no access modifier stops them. That is deliberate rather
    /// than a leak, because the zero value *is* a meaningful plan - the empty one - and
    /// TryPick already hands it back on failure. What the private constructor seals off is
    /// the case that would actually lie to us: a plan with arbitrary made-up contents.
    ///
    /// A class would seal construction completely, at the price of being nullable, and
    /// nullable reference types are off in this project. Reading a null plan from inside a
    /// Harmony patch that runs every frame would throw at 60Hz; reading the empty plan
    /// quietly pays no bonus. In a game mod that is the better way to be wrong.
    ///
    /// Inside it is an array indexed by quality, so [2] is the number of quality-2 fish to
    /// spend. Index 0 is kept even though no real fish has quality 0, so that index and
    /// quality always match, and because vanilla scans from 0 as well. That indexing is
    /// deliberate rather than incidental - it mirrors how the game itself walks qualities -
    /// but it is nobody else's business, hence the wrapper.
    ///
    /// This is a readonly struct, so wrapping the array costs no extra allocation: the
    /// array was being allocated anyway and the wrapper lives inline. Worth knowing on a
    /// path the crafting panel touches every frame.
    ///
    /// The totals are worked out once, in the constructor, so asking for them repeatedly
    /// is free. Both are guaranteed non-negative, which is why ComputeBonus no longer
    /// guards against a negative - a plan simply cannot express one.
    ///
    /// default(FishPlan) is the "spends nothing" plan: no fish, no points, and CountAt
    /// returns 0 for every quality. TryPick hands that back when it fails, so a caller
    /// that ignores the bool still can't read garbage.
    /// </remarks>
    internal readonly struct FishPlan
    {
        private readonly int[] _byQuality;
        private readonly int _totalFish;
        private readonly int _qualityPoints;

        /// <summary>
        /// Wrap a per-quality tally and total it up.
        /// </summary>
        /// <param name="byQuality">
        /// How many fish to take from each quality, indexed by quality. Never null - the
        /// only caller is <see cref="TryPick"/>, which passes an array it just allocated.
        /// Callers wanting an empty plan use default(FishPlan).
        /// </param>
        private FishPlan(int[] byQuality)
        {
            _byQuality = byQuality;

            int total = 0;
            int points = 0;
            for (int quality = 0; quality < byQuality.Length; quality++)
            {
                int count = byQuality[quality];
                if (count <= 0) continue;

                total += count;

                // Quality 0 contributes 0 rather than -1. Vanilla counts qualities from
                // 0, so a 0 can theoretically reach us, and one must not eat another
                // fish's contribution.
                int above = quality - 1;
                if (above > 0) points += count * above;
            }
            _totalFish = total;
            _qualityPoints = points;
        }

        /// <summary>How many fish this plan spends in total.</summary>
        internal int TotalFish => _totalFish;

        /// <summary>
        /// How far above quality 1 the fish in this plan are, added up.
        /// </summary>
        /// <remarks>
        /// The sum of (quality - 1) across every fish spent. Two quality-1 fish give 0, a
        /// quality-1 plus a quality-2 gives 1, and two quality-5 give 8.
        ///
        /// This is the numerator of the average, kept as a whole number so the bonus
        /// arithmetic never needs a float. See <see cref="BonusRules.ComputeBonus"/>.
        /// </remarks>
        internal int QualityPoints => _qualityPoints;

        /// <summary>
        /// The highest quality this plan can speak for, which is the fish's own max quality.
        /// </summary>
        /// <remarks>
        /// Written to be used as an inclusive loop bound - <c>for (q = 0; q &lt;= MaxQuality; q++)</c> -
        /// so an empty plan returns -1 and the loop simply doesn't run.
        /// </remarks>
        internal int MaxQuality => _byQuality == null ? -1 : _byQuality.Length - 1;

        /// <summary>
        /// How many fish of one quality this plan spends.
        /// </summary>
        /// <returns>The count, or 0 for a quality this plan says nothing about.</returns>
        /// <param name="quality">The quality to ask about. Out-of-range is safe, not an error.</param>
        internal int CountAt(int quality)
        {
            if (_byQuality == null || quality < 0 || quality >= _byQuality.Length) return 0;
            return _byQuality[quality];
        }

        /// <summary>
        /// Work out which fish to spend, and how many of each quality.
        /// </summary>
        /// <returns>
        /// We return true when the craft can, in fact, be crafted (the player has enough of
        /// the requirements in their inventory to fulfill).
        /// We return false otherwise (not enough fish in total, or the mod mixing is switched
        /// off and no single quality can support the craft on its own).
        /// When we return false, the caller falls back to vanilla logic.
        /// The out parameter 'plan' holds the plan of which fish to spend for this craft, and is only
        /// meaningful when we return true (otherwise it is the empty plan, which spends nothing).
        /// </returns>
        /// <param name="countsByQuality">
        /// How many fish of each quality are in the player's inventory. The index is the quality, so
        /// countsByQuality[2] is the number of quality-2 fish. Index 0 is quality 0,
        /// which no real fish has, but we keep the slot so index and quality always match,
        /// and because vanilla scans from 0 as well.
        /// </param>
        /// <param name="needed">
        /// How many fish this whole craft will eat: the recipe's requirement times the
        /// craft multiplier. Example: MeadBaseBugRepellent wants 3 fish, so multi-crafting
        /// five of them needs 15.
        /// </param>
        /// <param name="largestFirst">
        /// Whether to take the largest fish first, or the smallest.
        /// True = largest. See <see cref="ModConfig.FishToSpend"/>.
        /// </param>
        /// <param name="allowMixed">
        /// Whether a craft may draw on more than one quality at once.
        /// See <see cref="ModConfig.AllowMixedQualities"/>.
        /// </param>
        /// <param name="plan">
        /// Out parameter holding which fish to spend. See <see cref="FishPlan"/> for what you
        /// can ask it. The empty plan when we return false.
        /// </param>
        /// <remarks>
        /// The fill behavior pays attention to <see cref="ModConfig.FishToSpend"/>.
        /// For example: If "SmallestFirst" is picked, given one quality-1 fish and three quality-4
        /// for a two-fish craft, we take the quality-1 and THEN one quality-4.
        /// Vanilla - and this mod before 0.2.0 - would skip the small fish entirely
        /// and burn two quality-4, because both looked for a single quality that could
        /// fulfill the whole set of craft requirements by itself.
        /// </remarks>
        internal static bool TryPick(IList<int> countsByQuality, int needed, bool largestFirst,
                                     bool allowMixed, out FishPlan plan)
        {
            plan = default;
            if (countsByQuality == null || countsByQuality.Count == 0 || needed <= 0) return false;

            int last = countsByQuality.Count - 1;
            var taken = new int[countsByQuality.Count];
            int remaining = needed;

            for (int i = 0; i <= last; i++)
            {
                int quality = largestFirst ? last - i : i;
                int available = countsByQuality[quality];
                if (available <= 0) continue;

                // The mod has a toggle to turn off mixing, so we pay attention to that here
                if (!allowMixed)
                {
                    // Pre-0.2.0 behavior for this mod, and still what vanilla demands:
                    // one quality has to cover the whole craft or we keep out of it.
                    if (available < needed) continue;
                    taken[quality] = needed;
                    plan = new FishPlan(taken);
                    return true;
                }

                // Take some fish, subtract so we know what's left, and keep going
                // as we check further qualities in the desired order!
                int take = available < remaining ? available : remaining;
                taken[quality] = take;
                remaining -= take;
                if (remaining == 0)
                {
                    plan = new FishPlan(taken);
                    return true;
                }
            }

            // We were not able to satisfy the craft, so we return false and leave plan unset
            return false;
        }
    }
}
