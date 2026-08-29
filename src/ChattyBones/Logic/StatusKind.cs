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
        /// through a hit rather than by name. Cold and Freezing are absent on purpose:
        /// skeletons do not feel the cold, and a line about it would only ever fire
        /// for a player's effects.
        ///
        /// Wet is also absent, and that one is a judgement call. It is by far the most
        /// frequently acquired - any water at all - and Afflicted outranks the kill
        /// events, so a skeleton wading into a swamp would talk over its own victories
        /// for the sake of mentioning it is damp.
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

        /// <summary>Is this effect a bad thing to have happened?</summary>
        /// <returns>True for the effects that hurt, sting or stick.</returns>
        /// <param name="effectName">The effect's asset name, e.g. "Burning".</param>
        /// <remarks>
        /// Anything unrecognised comes back false and is treated as a buff, which is
        /// the safer of the two wrong answers.
        /// </remarks>
        internal static bool IsHarmful(string effectName)
        {
            if (string.IsNullOrEmpty(effectName))
            {
                return false;
            }

            for (int i = 0; i < Harmful.Length; i++)
            {
                if (Harmful[i] == effectName)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
