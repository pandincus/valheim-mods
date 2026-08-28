using System.Collections.Generic;

namespace ChattyBones.Logic
{
    /// <summary>
    /// How talkative a squad is. All configurable.
    /// </summary>
    /// <remarks>
    /// Immutable: to change a setting, build a new one and assign
    /// <see cref="ChatterBudget.Settings"/>. BepInEx raises SettingChanged off
    /// Unity's main thread, so swapping one reference is the difference between a
    /// reader seeing the old settings or the new ones, and seeing a half-built set.
    ///
    /// The defaults are a starting guess. Five skeletons is a lot of mouths.
    /// </remarks>
    internal sealed class ChatterSettings
    {
        private readonly HashSet<ChatterEvent> _disabled;

        /// <summary>Build a set of settings. Name only what you are changing.</summary>
        /// <param name="minGapSeconds">How long the whole squad stays quiet after any one of them speaks.</param>
        /// <param name="preemptGapSeconds">
        /// How long an important line waits before interrupting a less important one.
        /// Without it a death cry could land in the same frame as the idle mutter it
        /// is cutting off, and two bits of text appearing together are two bits of
        /// text nobody reads.
        /// </param>
        /// <param name="speakerCooldownSeconds">
        /// How long one skeleton waits before speaking again. Much longer than the
        /// squad gap, so the group keeps a conversation going while any individual
        /// stays fairly quiet - which reads as several people rather than one person
        /// with a lot to say.
        /// </param>
        /// <param name="squadEchoWindowSeconds">
        /// How long one remark about a thing stops everyone else remarking on it.
        /// Send five skeletons at one greydwarf and all five acquire it inside the
        /// same second, so without this you get five near-identical lines at once.
        /// </param>
        /// <param name="disabledEvents">Events the player has switched off, or null for none. Copied, not held.</param>
        internal ChatterSettings(
            float minGapSeconds = 2.5f,
            float preemptGapSeconds = 0.5f,
            float speakerCooldownSeconds = 8f,
            float squadEchoWindowSeconds = 6f,
            IEnumerable<ChatterEvent> disabledEvents = null)
        {
            MinGapSeconds = minGapSeconds;
            PreemptGapSeconds = preemptGapSeconds;
            SpeakerCooldownSeconds = speakerCooldownSeconds;
            SquadEchoWindowSeconds = squadEchoWindowSeconds;
            _disabled = disabledEvents == null ? [] : [.. disabledEvents];
        }

        /// <summary>How long the whole squad stays quiet after any one of them speaks.</summary>
        internal float MinGapSeconds { get; }

        /// <summary>How long an important line waits before interrupting a less important one.</summary>
        internal float PreemptGapSeconds { get; }

        /// <summary>How long one skeleton waits before it is allowed to speak again.</summary>
        internal float SpeakerCooldownSeconds { get; }

        /// <summary>How long one remark about a thing stops everyone else remarking on it.</summary>
        internal float SquadEchoWindowSeconds { get; }

        /// <summary>Has the player switched this event off?</summary>
        /// <param name="kind">The event to check.</param>
        /// <returns>True if nobody should react to it at all.</returns>
        internal bool IsDisabled(ChatterEvent kind)
        {
            return _disabled.Contains(kind);
        }
    }
}
