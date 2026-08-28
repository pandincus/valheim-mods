using BepInEx.Configuration;

namespace ChattyBones
{
    /// <summary>How a line is drawn over a skeleton's head.</summary>
    internal enum BubbleStyle
    {
        /// <summary>The chat text that follows the head. The default.</summary>
        FloatingText,

        /// <summary>The trader dialogue box. Safe, but sits still.</summary>
        DialoguePanel,
    }

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
        internal static ConfigEntry<BubbleStyle> Bubble;
        internal static ConfigEntry<float> DialoguePanelSeconds;
        internal static ConfigEntry<float> DialoguePanelCullDistance;
        internal static ConfigEntry<float> TextHeight;
        internal static ConfigEntry<string> TextColour;

        /// <summary>
        /// Declare every setting. Called once from <see cref="ChattyBonesPlugin.Awake"/>.
        /// </summary>
        /// <param name="cfg">The plugin's config file, handed to us by BepInEx.</param>
        /// <remarks>
        /// Binding either reads the player's existing value or writes the default, so
        /// the .cfg on disk always ends up complete. Bind order is the order settings
        /// appear in that file, so related ones are bound together.
        /// </remarks>
        internal static void Init(ConfigFile cfg)
        {
            Enabled = cfg.Bind(
                "General", "Enabled", true,
                "Master switch. Turn this off and your skeletons shut up completely - no " +
                "speech, no idle chatter, nothing. Safe to flip while you are playing.");

            Bubble = cfg.Bind(
                "Appearance", "BubbleStyle", BubbleStyle.FloatingText,
                "How a line is drawn. FloatingText is the chat text that follows the " +
                "skeleton's head, and is the default. DialoguePanel is the box the " +
                "traders use - it does not follow the skeleton around, but it is built " +
                "on the parts of the game that mods are meant to use, so it is there if " +
                "a Valheim update ever breaks the other one.");

            DialoguePanelSeconds = cfg.Bind(
                "Appearance", "DialoguePanelSeconds", 5f,
                "How long a DialoguePanel line stays up. Ignored by FloatingText, which " +
                "uses Valheim's own chat timeout.");

            DialoguePanelCullDistance = cfg.Bind(
                "Appearance", "DialoguePanelCullDistance", 20f,
                "How far away you can be and still see a DialoguePanel line, in metres. " +
                "Ignored by FloatingText.");

            TextHeight = cfg.Bind(
                "Appearance", "TextHeight", 0.3f,
                "Extra height above the skeleton's head, in metres, so the line clears " +
                "the name label. 0.3 sits just clear of it; by 1.0 the text looks " +
                "detached from whoever said it. 0 puts it exactly where Valheim puts a " +
                "player's chat, which lands right on the name.");

            TextColour = cfg.Bind(
                "Appearance", "TextColour", "",
                "Colour for skeleton speech, as a hex code like #C8FFC8. Leave empty for " +
                "Valheim's usual white. Accepts #RGB, #RRGGBB and #RRGGBBAA. Anything " +
                "that is not a hex code is ignored, with a note in the log.");
        }
    }
}
