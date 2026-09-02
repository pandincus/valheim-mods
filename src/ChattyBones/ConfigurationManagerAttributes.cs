namespace ChattyBones
{
    /// <summary>Hints for ConfigurationManager's settings panel, if the player has it.</summary>
    /// <remarks>
    /// ConfigurationManager finds this by <b>type name alone</b> - it looks through a
    /// ConfigDescription's tags for an object whose type is called exactly
    /// ConfigurationManagerAttributes and copies across whatever names it recognises. So
    /// there is no assembly to reference and nothing to install: a player without
    /// ConfigurationManager has an object in a tag array that nobody ever reads.
    ///
    /// Which means the two rules here are worth knowing. The name cannot change, and
    /// these have to be public properties on a public class - a member the panel cannot
    /// see is not an error anywhere, it is a setting that quietly ignores the hint.
    ///
    /// Only the two we use are declared. The panel supports rather more - a custom
    /// drawer, Browsable, ReadOnly, a display name - and any of them can be added here
    /// as a property when there is a reason to.
    /// </remarks>
    public sealed class ConfigurationManagerAttributes
    {
        /// <summary>Hide this until the player ticks Advanced at the top of the window.</summary>
        public bool? IsAdvanced { get; set; }

        /// <summary>
        /// Where this sits in its section. Higher comes first, and everything left at
        /// zero falls back to alphabetical order by key.
        /// </summary>
        /// <remarks>
        /// Worth setting on every entry rather than only on the ones being moved,
        /// because alphabetical is a genuinely bad default here: it puts BigHitFraction
        /// at the top of Chatter, above the dial that governs half the section, and
        /// sorts HearOthers below the two settings it gates.
        /// </remarks>
        public int? Order { get; set; }
    }
}
