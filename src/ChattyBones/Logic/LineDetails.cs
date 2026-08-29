namespace ChattyBones.Logic
{
    /// <summary>
    /// The extra things known about one particular event, for the tokens that
    /// describe it rather than the people in it.
    /// </summary>
    /// <remarks>
    /// Bundled rather than passed as four more arguments. <see cref="LineTokens"/>
    /// already warns that four strings in a row is easy to get subtly wrong; eight
    /// would be worse, and every one of these is absent on most events.
    ///
    /// Everything here is optional and usually null. A line asking for a token we do
    /// not have is passed over rather than rendered with a hole in it, so a pack can
    /// carry a flavoured variant beside a plain one and get the flavoured one only
    /// when it fits.
    /// </remarks>
    internal readonly struct LineDetails
    {
        /// <summary>Gather whatever this event happens to know.</summary>
        /// <param name="weapon">The weapon's own name, e.g. "Mistwalker".</param>
        /// <param name="weaponType">What kind of weapon it is, e.g. "sword".</param>
        /// <param name="damage">The dominant damage type, e.g. "fire".</param>
        /// <param name="status">A status effect's name, e.g. "Burning".</param>
        internal LineDetails(
            string weapon = null,
            string weaponType = null,
            string damage = null,
            string status = null)
        {
            Weapon = weapon;
            WeaponType = weaponType;
            Damage = damage;
            Status = status;
        }

        /// <summary>The weapon's own name, or null. Can name the wrong one - see Hits.WeaponName.</summary>
        internal string Weapon { get; }

        /// <summary>What kind of weapon it was, or null. Read off the hit, so always right.</summary>
        internal string WeaponType { get; }

        /// <summary>The dominant damage type, or null when nothing stood out.</summary>
        internal string Damage { get; }

        /// <summary>The status effect involved, already localized, or null.</summary>
        internal string Status { get; }
    }
}
