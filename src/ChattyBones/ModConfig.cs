using BepInEx.Configuration;

namespace ChattyBones
{
    /// <summary>
    /// Every setting the mod has. BepInEx writes these to
    /// BepInEx/config/pandincus.chattybones.cfg on first run, and
    /// ConfigurationManager (F1) edits them live - the code reads .Value every
    /// time, so changes take effect straight away with no restart.
    ///
    /// These descriptions are visible to players using the config manager.
    /// </summary>
    /// <remarks>
    /// Settings live here; the skeletons' actual lines do not. Those are a YAML
    /// file, read with the shared ValheimModding-YamlDotNet package.
    ///
    /// Two reasons, and neither is about YAML being fashionable. A BepInEx .cfg
    /// value cannot contain a newline, so a pack kept in here would have to be
    /// minified onto a single line - roughly 13KB of it - which is miserable both in
    /// a text editor and in ConfigurationManager. And a line pack is something
    /// players swap whole, which wants to be a file you can hand somebody rather
    /// than a section of your own config.
    ///
    /// YAML over JSON because a pack is a file people write by hand, and it can
    /// carry comments explaining the tokens and the events. JSON cannot, which for a
    /// "write your own gags" file is a real loss. See NOTES-ChattyBones.md.
    /// </remarks>
    internal static class ModConfig
    {
        internal static ConfigEntry<bool> Enabled;

        /// <summary>
        /// Declare every setting. Called once from <see cref="ChattyBonesPlugin.Awake"/>.
        /// </summary>
        /// <param name="cfg">
        /// The plugin's config file, handed to us by BepInEx. Binding a setting either reads
        /// the player's existing value or writes the default, so the .cfg on disk always ends
        /// up complete.
        ///
        /// The descriptions below are the player-facing help text: they show up as comments
        /// in the .cfg and as tooltips in ConfigurationManager (F1). Worth writing for a
        /// player rather than for us.
        /// </param>
        internal static void Init(ConfigFile cfg)
        {
            Enabled = cfg.Bind(
                "General", "Enabled", true,
                "Master switch. Turn this off and your skeletons shut up completely - no " +
                "speech, no idle chatter, nothing. Safe to flip while you are playing.");
        }
    }
}
