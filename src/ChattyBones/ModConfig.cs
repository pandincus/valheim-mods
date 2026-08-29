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
    /// Settings live here; the skeletons' lines do not. Those are in
    /// ChattyBones.lines.yaml next to this file - see <see cref="PackFile"/>. A
    /// BepInEx .cfg value cannot contain a newline, so a pack kept in here would have
    /// to be minified onto one line, and a pack is something players swap whole,
    /// which wants to be a file you can hand somebody rather than a section of your
    /// own config.
    /// </remarks>
    internal static class ModConfig
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<BubbleStyle> Bubble;
        internal static ConfigEntry<float> DialoguePanelSeconds;
        internal static ConfigEntry<float> DialoguePanelCullDistance;
        internal static ConfigEntry<float> TextHeight;
        internal static ConfigEntry<string> TextColor;

        internal static ConfigEntry<float> MinGapSeconds;
        internal static ConfigEntry<float> PreemptGapSeconds;
        internal static ConfigEntry<float> SpeakerCooldownSeconds;
        internal static ConfigEntry<float> SquadEchoWindowSeconds;
        internal static ConfigEntry<float> IdleSeconds;
        internal static ConfigEntry<float> SummonGreetingSeconds;
        internal static ConfigEntry<float> HurtFraction;
        internal static ConfigEntry<float> BigHitFraction;

        internal static ConfigEntry<bool> LogChatter;

        /// <summary>
        /// Declare every setting. Called once from <see cref="ChattyBonesPlugin.Awake"/>.
        /// </summary>
        /// <param name="cfg">The plugin's config file, handed to us by BepInEx.</param>
        /// <remarks>
        /// Binding either reads the player's existing value or writes the default, so
        /// the .cfg on disk always ends up complete. Bind order is the order settings
        /// appear in that file, so related ones are bound together.
        ///
        /// The numbers carry an AcceptableValueRange, which does two jobs: it stops a
        /// value that would break the feature quietly - 0 seconds leaves a dialogue
        /// panel up until the skeleton dies - and it is what makes ConfigurationManager
        /// draw a slider rather than a text box.
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
                new ConfigDescription(
                    "How long a DialoguePanel line stays up. Ignored by FloatingText, which " +
                    "uses Valheim's own chat timeout.",
                    new AcceptableValueRange<float>(1f, 30f)));

            DialoguePanelCullDistance = cfg.Bind(
                "Appearance", "DialoguePanelCullDistance", 20f,
                new ConfigDescription(
                    "How far away you can be and still see a DialoguePanel line, in meters. " +
                    "Ignored by FloatingText.",
                    new AcceptableValueRange<float>(5f, 100f)));

            TextHeight = cfg.Bind(
                "Appearance", "TextHeight", 0.3f,
                new ConfigDescription(
                    "Extra height above the skeleton's head, in meters, so the line clears " +
                    "the name label. 0.3 sits just clear of it; by 1.0 the text looks " +
                    "detached from whoever said it. 0 puts it exactly where Valheim puts a " +
                    "player's chat, which lands right on the name.",
                    new AcceptableValueRange<float>(0f, 2f)));

            TextColor = cfg.Bind(
                "Appearance", "TextColor", "",
                "One color for everything the skeletons say, as a hex code like #C8FFC8. " +
                "Leave it empty - which is the default - and the line pack decides " +
                "instead, which is usually what you want: packs color by event, so a " +
                "death cry reads as bad news before you have read a word of it. Set this " +
                "to override the pack entirely. Accepts #RGB, #RRGGBB and #RRGGBBAA, and " +
                "anything that is not a hex code is ignored with a note in the log.");

            MinGapSeconds = cfg.Bind(
                "Chatter", "MinGapSeconds", 2.5f,
                new ConfigDescription(
                    "How long the whole squad stays quiet after any one of them speaks. This " +
                    "is the main dial for how talkative they are: raise it if five skeletons " +
                    "feel like a crowd, lower it if they feel asleep.",
                    new AcceptableValueRange<float>(0f, 30f)));

            PreemptGapSeconds = cfg.Bind(
                "Chatter", "PreemptGapSeconds", 0.5f,
                new ConfigDescription(
                    "How long something important waits before cutting in on something " +
                    "trivial. Without a gap a death cry can land in the same frame as the " +
                    "idle mutter it interrupts, and two lines at once is two lines nobody reads.",
                    new AcceptableValueRange<float>(0f, 10f)));

            SpeakerCooldownSeconds = cfg.Bind(
                "Chatter", "SpeakerCooldownSeconds", 8f,
                new ConfigDescription(
                    "How long one skeleton waits before speaking again. Much longer than " +
                    "MinGapSeconds on purpose - the squad keeps a conversation going while " +
                    "each individual stays fairly quiet, which reads as several people rather " +
                    "than one person with a lot to say.",
                    new AcceptableValueRange<float>(0f, 120f)));

            SquadEchoWindowSeconds = cfg.Bind(
                "Chatter", "SquadEchoWindowSeconds", 6f,
                new ConfigDescription(
                    "How long one remark about a thing stops the others remarking on it too. " +
                    "Send five skeletons at one greydwarf and all five notice it inside the " +
                    "same second, so without this you get five near-identical lines at once.",
                    new AcceptableValueRange<float>(0f, 60f)));

            IdleSeconds = cfg.Bind(
                "Chatter", "IdleSeconds", 45f,
                new ConfigDescription(
                    "Roughly how often a skeleton with nothing to do says something anyway. " +
                    "Scattered by a quarter either way, so a squad summoned together does not " +
                    "get bored together.",
                    new AcceptableValueRange<float>(5f, 600f)));

            SummonGreetingSeconds = cfg.Bind(
                "Chatter", "SummonGreetingSeconds", 5f,
                new ConfigDescription(
                    "How new a skeleton has to be to greet you. Skeletons are rebuilt every " +
                    "time you walk back into their area, so this is what separates being " +
                    "raised from being reloaded. Only raise it if greetings are being missed " +
                    "on a slow machine.",
                    new AcceptableValueRange<float>(1f, 60f)));

            HurtFraction = cfg.Bind(
                "Chatter", "HurtFraction", 0.15f,
                new ConfigDescription(
                    "How big a hit has to be before a skeleton mentions it, as a share of its " +
                    "own maximum health. At 0.15 it complains about anything taking a seventh " +
                    "of it; at 0.01 it complains about everything.",
                    new AcceptableValueRange<float>(0.01f, 1f)));

            BigHitFraction = cfg.Bind(
                "Chatter", "BigHitFraction", 0.35f,
                new ConfigDescription(
                    "How hard you have to hit something before the squad is impressed, as a " +
                    "share of THAT CREATURE'S maximum health - not of the damage you usually " +
                    "do. Kills are handled separately, so this is only about a swing that did " +
                    "not quite finish the job.\n" +
                    "Being a share of the victim's health has an awkward consequence worth " +
                    "knowing before you tune it: the tougher the enemy, the harder it is to " +
                    "impress anybody. A hit that takes half a greydwarf usually kills it " +
                    "outright and is counted as a kill instead, while the same hit is a small " +
                    "fraction of a troll and says nothing at all. So if you never hear these, " +
                    "lower it - and expect the lines to come from mid-sized enemies rather " +
                    "than from your best swings.",
                    new AcceptableValueRange<float>(0.01f, 1f)));

            LogChatter = cfg.Bind(
                "Debug", "LogChatter", false,
                "Write a line to BepInEx/LogOutput.log every time a skeleton was about to say " +
                "something, and what came of it - said, or turned down and by which check. " +
                "Also records every time one loses sight of its target, with how stale that " +
                "sighting was, which is what decides whether the kill gets mentioned at all.\n" +
                "For working out why the squad is quieter than you expect. A refusal looks " +
                "exactly like silence from outside, so without this there is nothing to go " +
                "on. Off by default, and it is a lot of log.");
        }
    }
}
