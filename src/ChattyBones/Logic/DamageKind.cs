namespace ChattyBones.Logic
{
    /// <summary>
    /// Which kind of damage a hit mostly was, as a word a line can use.
    /// </summary>
    /// <remarks>
    /// A hit almost always has more than one of these set - a fire sword does slash
    /// and fire together - so there is no "the" damage type, only a dominant one, and
    /// picking it is the whole job.
    ///
    /// Chop and pickaxe are not among the arguments: they are tool damage, not weapon
    /// damage. A hit that was mostly either still answers with whatever weapon damage
    /// it also did.
    /// </remarks>
    internal static class DamageKind
    {
        /// <summary>Name the damage type that did the most of the work.</summary>
        /// <returns>A lower-case word for the dominant type, or null when nothing stands out.</returns>
        /// <param name="blunt">Blunt damage.</param>
        /// <param name="slash">Slash damage.</param>
        /// <param name="pierce">Pierce damage.</param>
        /// <param name="fire">Fire damage.</param>
        /// <param name="frost">Frost damage.</param>
        /// <param name="lightning">Lightning damage.</param>
        /// <param name="poison">Poison damage.</param>
        /// <param name="spirit">Spirit damage.</param>
        /// <remarks>
        /// Ties go to the earlier argument, which puts the three physical types above
        /// the elemental ones. That is the right way round for the common case: a
        /// weapon with a fire enchant does most of its work as slash, and "nice slash
        /// hit" beats "nice fire hit" for describing a sword.
        /// </remarks>
        internal static string Dominant(
            float blunt,
            float slash,
            float pierce,
            float fire,
            float frost,
            float lightning,
            float poison,
            float spirit)
        {
            string best = null;
            float most = 0f;

            Consider("blunt", blunt, ref best, ref most);
            Consider("slash", slash, ref best, ref most);
            Consider("pierce", pierce, ref best, ref most);
            Consider("fire", fire, ref best, ref most);
            Consider("frost", frost, ref best, ref most);
            Consider("lightning", lightning, ref best, ref most);
            Consider("poison", poison, ref best, ref most);
            Consider("spirit", spirit, ref best, ref most);

            return best;
        }

        /// <summary>Take this type if it beats what we have so far.</summary>
        /// <param name="name">What to call it.</param>
        /// <param name="amount">How much of it there was.</param>
        /// <param name="best">The leader so far.</param>
        /// <param name="most">How much the leader did.</param>
        private static void Consider(string name, float amount, ref string best, ref float most)
        {
            if (amount > most)
            {
                best = name;
                most = amount;
            }
        }
    }
}
