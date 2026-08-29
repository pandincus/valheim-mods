namespace ChattyBones.Logic
{
    /// <summary>
    /// Whether a status effect is something to be pleased about.
    /// </summary>
    /// <remarks>
    /// Valheim has no flag for this. <c>StatusEffect.m_attributes</c> looks like the
    /// answer and is not - its only members are ColdResistance, DoubleImpactDamage,
    /// SailingPower and TamingBoost, none of which is about harm.
    ///
    /// So the effect's own asset name is the signal, which is what the game itself
    /// keys on: <c>SEMan.s_statusEffectTared</c> is <c>"Tared".GetStableHashCode()</c>
    /// and the rest follow the same pattern. Names rather than subclasses, because
    /// several of the nastiest have no subclass at all - tar, lightning and slime are
    /// plain StatusEffect or SE_Stats, so a type-name check called them buffs.
    /// </remarks>
    internal static class StatusKind
    {
        /// <summary>The vanilla effects that mean something has gone wrong.</summary>
        /// <remarks>
        /// Taken from the names <c>SEMan</c> hashes, plus Slimed, which is applied
        /// through a hit rather than by name. The ambient ones are next door in
        /// <see cref="IsWeather"/> - they are not injuries and must not be ranked
        /// like them.
        /// </remarks>
        private static readonly string[] Harmful =
        [
            "Burning",
            "Spirit",
            "Frost",
            "Poison",
            "Lightning",
            "Smoked",
            "Tared",
            "Slimed",
            "Puke",
            "Harpooned",
        ];

        /// <summary>Which of the three status events an effect belongs to.</summary>
        /// <returns>Afflicted for an injury, Weather for the ambient ones, Buffed for the rest.</returns>
        /// <param name="effectName">The effect's asset name, e.g. "Burning".</param>
        /// <remarks>
        /// Here rather than at the hook so it can be tested: the branch is the point of
        /// the two lists, and it lived in the Unity half where nothing could reach it.
        /// </remarks>
        internal static ChatterEvent EventFor(string effectName)
        {
            if (IsHarmful(effectName))
            {
                return ChatterEvent.Afflicted;
            }

            return IsWeather(effectName) ? ChatterEvent.Weather : ChatterEvent.Buffed;
        }

        /// <summary>The effects that are just the weather being unpleasant.</summary>
        /// <remarks>
        /// Practically speaking this is Wet and only Wet: it is applied in
        /// Character.UpdateWater, so anything can get it, and it is by far the most
        /// frequently acquired effect in the game - any water at all, rain included.
        /// Cold and Freezing are Player-only, so no vanilla skeleton reaches them.
        /// Insurance against a mod that changes that, at a cost of two strings.
        /// </remarks>
        private static readonly string[] Ambient =
        [
            "Wet",
            "Cold",
            "Freezing",
        ];

        /// <summary>Is this the weather rather than an injury?</summary>
        /// <returns>True for the ambient effects, which get their own quiet event.</returns>
        /// <param name="effectName">The effect's asset name, e.g. "Wet".</param>
        internal static bool IsWeather(string effectName)
        {
            return Contains(Ambient, effectName);
        }

        /// <summary>Is this effect a bad thing to have happened?</summary>
        /// <returns>True for the effects that hurt, sting or stick.</returns>
        /// <param name="effectName">The effect's asset name, e.g. "Burning".</param>
        /// <remarks>
        /// Anything unrecognised comes back false and is treated as a buff, which is
        /// the safer of the two wrong answers.
        /// </remarks>
        internal static bool IsHarmful(string effectName)
        {
            return Contains(Harmful, effectName);
        }

        /// <summary>Exact, case-sensitive membership.</summary>
        /// <returns>True when the name is in the list.</returns>
        /// <param name="names">The list to look in.</param>
        /// <param name="effectName">What to look for. Null and empty are fine, and find nothing.</param>
        private static bool Contains(string[] names, string effectName)
        {
            if (string.IsNullOrEmpty(effectName))
            {
                return false;
            }

            for (int i = 0; i < names.Length; i++)
            {
                if (names[i] == effectName)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
