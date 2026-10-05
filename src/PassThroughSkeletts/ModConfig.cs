using BepInEx.Configuration;

namespace PassThroughSkeletts
{
    /// <summary>
    /// Every setting the mod has. BepInEx writes these to
    /// BepInEx/config/pandincus.passthroughskeletts.cfg on first run, and
    /// ConfigurationManager (F1) edits them live - the code reads .Value every
    /// time, so changes take effect straight away with no restart.
    /// </summary>
    internal static class ModConfig
    {
        internal static ConfigEntry<bool> Enabled;

        /// <summary>
        /// Declare every setting. Called once from <see cref="PassThroughSkelettsPlugin.Awake"/>.
        /// </summary>
        /// <param name="cfg">The plugin's config file, handed to us by BepInEx.</param>
        /// <remarks>
        /// One setting is all there is, on purpose. Who it applies to (every Skelett, every
        /// player) is not something I could think of a reason to vary, and a dial nobody
        /// would turn is just one more thing to explain.
        /// </remarks>
        internal static void Init(ConfigFile cfg)
        {
            Enabled = cfg.Bind(
                "General", "Enabled", true,
                "Master switch. Turn this off and Skeletts go back to being solid: you are " +
                "blocked and pushed by them exactly as in vanilla. It takes effect within a " +
                "fraction of a second, no restart needed.");
        }
    }
}
