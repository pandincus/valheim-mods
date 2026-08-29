namespace ChattyBones.Logic
{
    /// <summary>
    /// Whether a status effect is something to be pleased about.
    /// </summary>
    /// <remarks>
    /// Valheim has no flag for this. <c>StatusEffect.m_attributes</c> looks like the
    /// answer and is not - its only members are ColdResistance, DoubleImpactDamage,
    /// SailingPower and TamingBoost, none of which is about harm. What there is
    /// instead is the subclass, and the vanilla set is small enough to name.
    ///
    /// Taking the type name as a string rather than the type keeps this on the
    /// Unity-free side of the fence, where the table can be read and tested.
    /// </remarks>
    internal static class StatusKind
    {
        /// <summary>The vanilla effects that mean something has gone wrong.</summary>
        /// <remarks>
        /// Burning covers fire and spirit damage over time; wet and smoke are the two
        /// that are more nuisance than injury, and are in because a skeleton
        /// grumbling about being damp is exactly the register this mod wants.
        /// </remarks>
        private static readonly string[] Harmful =
        [
            "SE_Burning",
            "SE_Frost",
            "SE_Poison",
            "SE_Wet",
            "SE_Smoke",
            "SE_Puke",
            "SE_Harpooned",
        ];

        /// <summary>Is this effect a bad thing to have happened?</summary>
        /// <returns>True for the effects that hurt, sting or annoy.</returns>
        /// <param name="typeName">The effect's runtime type name, e.g. "SE_Burning".</param>
        /// <remarks>
        /// Anything unrecognised comes back false and is treated as a buff, which is
        /// the safer of the two wrong answers: a modded effect being thanked for is
        /// merely odd, where a shield being screamed about would be worse. A modded
        /// effect that really is harmful can still be given lines - it just arrives as
        /// Buffed, and the pack can say something neutral there.
        /// </remarks>
        internal static bool IsHarmful(string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
            {
                return false;
            }

            for (int i = 0; i < Harmful.Length; i++)
            {
                if (Harmful[i] == typeName)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
