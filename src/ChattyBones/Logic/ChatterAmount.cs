namespace ChattyBones.Logic
{
    /// <summary>How much a player wants to hear, as one word instead of four numbers.</summary>
    /// <remarks>
    /// Ordered least to most, because that is the order a dropdown should offer them
    /// in. <see cref="Custom"/> sits at the end as the odd one out: it
    /// names no amount at all, it says to go and read the numbers instead.
    /// </remarks>
    internal enum ChatterAmount
    {
        /// <summary>Not at all.</summary>
        Never,

        /// <summary>Now and then.</summary>
        Rarely,

        /// <summary>The amount the mod shipped with.</summary>
        Sometimes,

        /// <summary>Chattier.</summary>
        Often,

        /// <summary>As much as the squad has to say.</summary>
        Always,

        /// <summary>Whatever the advanced settings say.</summary>
        Custom,
    }

    /// <summary>The three gaps that decide how often the squad is allowed to speak.</summary>
    /// <param name="minGapSeconds">How long the whole squad waits after any one of them speaks.</param>
    /// <param name="speakerCooldownSeconds">How long one skeleton waits before speaking again.</param>
    /// <param name="squadEchoWindowSeconds">How long one remark stops the others repeating it.</param>
    internal readonly struct ChatterGaps(
        float minGapSeconds, float speakerCooldownSeconds, float squadEchoWindowSeconds)
    {
        /// <summary>How long the whole squad waits after any one of them speaks.</summary>
        internal float MinGapSeconds { get; } = minGapSeconds;

        /// <summary>How long one skeleton waits before speaking again.</summary>
        internal float SpeakerCooldownSeconds { get; } = speakerCooldownSeconds;

        /// <summary>How long one remark about a thing stops the others repeating it.</summary>
        internal float SquadEchoWindowSeconds { get; } = squadEchoWindowSeconds;
    }

    /// <summary>Turn a chosen amount into the numbers the budget actually runs on.</summary>
    /// <remarks>
    /// A preset wins outright rather than scaling the advanced numbers, and that is the
    /// decision worth knowing here. Scaling keeps both live and reads well right up
    /// until somebody sets MinGapSeconds to 2.5, watches the squad behave like 5, and
    /// has nothing on screen telling them why. So the numbers are consulted only under
    /// <see cref="ChatterAmount.Custom"/>, and every advanced description says so.
    ///
    /// <see cref="ChatterAmount.Sometimes"/> is exactly what the mod shipped with -
    /// 2.5, 8 and 6, and 45 seconds of idling. A player upgrading into presets should
    /// not be able to hear the difference, and a test holds that.
    /// </remarks>
    internal static class ChatterPresets
    {
        /// <summary>The gaps for one amount.</summary>
        /// <returns>False for Never and Custom, which name no gaps.</returns>
        /// <param name="amount">What the player picked.</param>
        /// <param name="gaps">The three numbers, when there are three numbers.</param>
        /// <remarks>
        /// The echo window moves with the rest, which is not obvious - it is a dedup
        /// rule rather than a frequency. It belongs here because a player asking for
        /// more chatter is asking to hear a second skeleton react to the same
        /// greydwarf, and holding the window still would refuse them exactly that.
        /// </remarks>
        internal static bool TryGaps(ChatterAmount amount, out ChatterGaps gaps)
        {
            switch (amount)
            {
                case ChatterAmount.Rarely:
                    gaps = new ChatterGaps(6f, 20f, 12f);
                    return true;

                case ChatterAmount.Sometimes:
                    gaps = new ChatterGaps(2.5f, 8f, 6f);
                    return true;

                case ChatterAmount.Often:
                    gaps = new ChatterGaps(1.5f, 5f, 4f);
                    return true;

                case ChatterAmount.Always:
                    gaps = new ChatterGaps(0.75f, 3f, 2f);
                    return true;

                case ChatterAmount.Never:
                case ChatterAmount.Custom:
                default:
                    gaps = default;
                    return false;
            }
        }

        /// <summary>How long a skeleton with nothing to do waits before saying something.</summary>
        /// <returns>False for Never and Custom.</returns>
        /// <param name="amount">What the player picked.</param>
        /// <param name="seconds">The idle interval, when there is one.</param>
        internal static bool TryIdleSeconds(ChatterAmount amount, out float seconds)
        {
            switch (amount)
            {
                case ChatterAmount.Rarely:
                    seconds = 120f;
                    return true;

                case ChatterAmount.Sometimes:
                    seconds = 45f;
                    return true;

                case ChatterAmount.Often:
                    seconds = 22f;
                    return true;

                case ChatterAmount.Always:
                    seconds = 10f;
                    return true;

                case ChatterAmount.Never:
                case ChatterAmount.Custom:
                default:
                    seconds = 0f;
                    return false;
            }
        }
    }
}
