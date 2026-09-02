using BepInEx.Configuration;
using ChattyBones.Logic;

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

        internal static ConfigEntry<ChatterAmount> ChatterFrequency;
        internal static ConfigEntry<ChatterAmount> IdleChatter;
        internal static ConfigEntry<string> SilencedEvents;

        internal static ConfigEntry<float> MinGapSeconds;
        internal static ConfigEntry<float> PreemptGapSeconds;
        internal static ConfigEntry<float> SpeakerCooldownSeconds;
        internal static ConfigEntry<float> SquadEchoWindowSeconds;
        internal static ConfigEntry<float> IdleSeconds;
        internal static ConfigEntry<float> SummonGreetingSeconds;
        internal static ConfigEntry<float> HurtFraction;
        internal static ConfigEntry<float> BigHitFraction;

        internal static ConfigEntry<bool> HearOthers;
        internal static ConfigEntry<float> AllyGreetingDistance;
        internal static ConfigEntry<float> AllyGreetingForgetSeconds;

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
                new ConfigDescription(
                    "Master switch. Off means complete silence. Safe to flip while playing.",
                    null, At(100)));

            Bubble = cfg.Bind(
                "Appearance", "BubbleStyle", BubbleStyle.FloatingText,
                new ConfigDescription(
                    "How a line is drawn. FloatingText follows the skeleton's head. " +
                    "DialoguePanel is the trader's box - it sits still, but is the safer one " +
                    "if a game update breaks the other.",
                    null, At(100)));

            DialoguePanelSeconds = cfg.Bind(
                "Appearance", "DialoguePanelSeconds", 5f,
                new ConfigDescription(
                    "How long a DialoguePanel line stays up. Ignored by FloatingText.",
                    new AcceptableValueRange<float>(1f, 30f), Adv(70)));

            DialoguePanelCullDistance = cfg.Bind(
                "Appearance", "DialoguePanelCullDistance", 20f,
                new ConfigDescription(
                    "How far away you can still see a DialoguePanel line, in meters. Ignored " +
                    "by FloatingText.",
                    new AcceptableValueRange<float>(5f, 100f), Adv(60)));

            TextHeight = cfg.Bind(
                "Appearance", "TextHeight", 0.3f,
                new ConfigDescription(
                    "Height above the skeleton's head, in meters. 0.3 clears the name label; " +
                    "by 1.0 the text looks detached.",
                    new AcceptableValueRange<float>(0f, 2f), Adv(80)));

            TextColor = cfg.Bind(
                "Appearance", "TextColor", "",
                new ConfigDescription(
                    "One color for everything, as a hex code like #C8FFC8. Leave it empty and " +
                    "the line pack colors by event instead.",
                    null, At(90)));

            ChatterFrequency = cfg.Bind(
                "Chatter", "ChatterFrequency", ChatterAmount.Often,
                new ConfigDescription(
                    "How much the squad reacts to things. Never still leaves idle chatter " +
                    "running, and this caps that too. Custom uses the numbers below.",
                    null, At(100)));

            IdleChatter = cfg.Bind(
                "Chatter", "IdleChatter", ChatterAmount.Sometimes,
                new ConfigDescription(
                    "How often they mutter when nothing is happening. Custom uses IdleSeconds " +
                    "below.",
                    null, At(90)));

            SilencedEvents = cfg.Bind(
                "Chatter", "SilencedEvents", "",
                new ConfigDescription(
                    "Events to switch off, separated by commas - for example \"Weather, " +
                    "PlayerAte\". The names are listed at the top of " +
                    "ChattyBones.lines.default.yaml.",
                    null, Adv(80)));

            MinGapSeconds = cfg.Bind(
                "Chatter", "MinGapSeconds", 1.5f,
                new ConfigDescription(
                    "How long the whole squad stays quiet after one of them speaks. Set by " +
                    "ChatterFrequency; editing it switches that to Custom.",
                    new AcceptableValueRange<float>(0f, 30f), Adv(95)));

            PreemptGapSeconds = cfg.Bind(
                "Chatter", "PreemptGapSeconds", 0.5f,
                new ConfigDescription(
                    "How long something important waits before cutting in on something " +
                    "trivial.",
                    new AcceptableValueRange<float>(0f, 10f), Adv(70)));

            SpeakerCooldownSeconds = cfg.Bind(
                "Chatter", "SpeakerCooldownSeconds", 5f,
                new ConfigDescription(
                    "How long one skeleton waits before speaking again. Set by " +
                    "ChatterFrequency; editing it switches that to Custom.",
                    new AcceptableValueRange<float>(0f, 120f), Adv(94)));

            SquadEchoWindowSeconds = cfg.Bind(
                "Chatter", "SquadEchoWindowSeconds", 4f,
                new ConfigDescription(
                    "How long one remark about a thing stops the others repeating it. Set by " +
                    "ChatterFrequency; editing it switches that to Custom.",
                    new AcceptableValueRange<float>(0f, 60f), Adv(93)));

            IdleSeconds = cfg.Bind(
                "Chatter", "IdleSeconds", 45f,
                new ConfigDescription(
                    "Roughly how often a bored skeleton says something anyway. Set by " +
                    "IdleChatter; editing it switches that to Custom.",
                    new AcceptableValueRange<float>(5f, 600f), Adv(89)));

            SummonGreetingSeconds = cfg.Bind(
                "Chatter", "SummonGreetingSeconds", 5f,
                new ConfigDescription(
                    "How new a skeleton has to be to greet you. Only raise it if greetings " +
                    "are being missed.",
                    new AcceptableValueRange<float>(1f, 60f), Adv(60)));

            HurtFraction = cfg.Bind(
                "Chatter", "HurtFraction", 0.15f,
                new ConfigDescription(
                    "How big a hit has to be before a skeleton mentions it, as a share of its " +
                    "own health.",
                    new AcceptableValueRange<float>(0.01f, 1f), Adv(50)));

            BigHitFraction = cfg.Bind(
                "Chatter", "BigHitFraction", 0.15f,
                new ConfigDescription(
                    "How hard you have to hit something before the squad is impressed, as a " +
                    "share of THAT creature's health. Kills count separately. Tough enemies " +
                    "are harder to impress, so lower it if you never hear these.",
                    new AcceptableValueRange<float>(0.01f, 1f), Adv(40)));

            HearOthers = cfg.Bind(
                "Multiplayer", "HearOthers", true,
                new ConfigDescription(
                    "Whether other players' skeletons talk on your screen. ChatterFrequency " +
                    "and SilencedEvents apply to them too. Players without the mod see " +
                    "nothing either way.",
                    null, At(100)));

            AllyGreetingDistance = cfg.Bind(
                "Multiplayer", "AllyGreetingDistance", 15f,
                new ConfigDescription(
                    "How close another player has to come before your skeletons say hail, in " +
                    "meters. Measured from the skeletons rather than from you.",
                    new AcceptableValueRange<float>(2f, 60f), Adv(90)));

            AllyGreetingForgetSeconds = cfg.Bind(
                "Multiplayer", "AllyGreetingForgetSeconds", 60f,
                new ConfigDescription(
                    "How long somebody has to stay out of range before being greeted again. " +
                    "Err high - you cross that boundary more often than you would think.",
                    new AcceptableValueRange<float>(5f, 600f), Adv(80)));

            LogChatter = cfg.Bind(
                "Debug", "LogChatter", false,
                new ConfigDescription(
                    "Log every line the squad was about to say and what came of it, for " +
                    "working out why they are quiet. It is a lot of log.",
                    null, At(100)));
        }

        /// <summary>Keep the dials and the numbers telling the same story.</summary>
        /// <remarks>
        /// Called once, after <see cref="Init"/>. Picking a preset writes its numbers
        /// into the advanced settings, so a player watching the panel sees the sliders
        /// move and a player reading the .cfg never finds a value that is not in force.
        /// Moving one of those sliders by hand sets the dial to Custom, which is the
        /// same bargain every graphics menu makes and the reason nobody has to be told
        /// about it.
        ///
        /// At startup the dial wins. A hand-edited number under a named preset cannot
        /// be told apart from a hand-edited dial, and the dial is the one the player is
        /// more likely to have meant - it is also the only reading under which the file
        /// never displays a number that does nothing.
        ///
        /// The guard is load-bearing rather than defensive: writing a number raises
        /// SettingChanged, which is the very thing that would flip the dial to Custom.
        /// </remarks>
        internal static void Wire()
        {
            ChatterFrequency.SettingChanged += (_, _) => PushReactions();
            IdleChatter.SettingChanged += (_, _) => PushIdle();

            MinGapSeconds.SettingChanged += (_, _) => NoticeReactionsEdited();
            SpeakerCooldownSeconds.SettingChanged += (_, _) => NoticeReactionsEdited();
            SquadEchoWindowSeconds.SettingChanged += (_, _) => NoticeReactionsEdited();
            IdleSeconds.SettingChanged += (_, _) => NoticeIdleEdited();

            PushReactions();
            PushIdle();
        }

        /// <summary>Whether we are the ones moving a value right now.</summary>
        private static bool _pushing;

        /// <summary>Write the reactions preset into the three numbers it stands for.</summary>
        private static void PushReactions()
        {
            if (_pushing || !ChatterPresets.TryGaps(ChatterFrequency.Value, out ChatterGaps gaps))
            {
                return;
            }

            _pushing = true;
            try
            {
                MinGapSeconds.Value = gaps.MinGapSeconds;
                SpeakerCooldownSeconds.Value = gaps.SpeakerCooldownSeconds;
                SquadEchoWindowSeconds.Value = gaps.SquadEchoWindowSeconds;
            }
            finally
            {
                _pushing = false;
            }
        }

        /// <summary>Write the idle preset into the one number it stands for.</summary>
        private static void PushIdle()
        {
            if (_pushing || !ChatterPresets.TryIdleSeconds(IdleChatter.Value, out float seconds))
            {
                return;
            }

            _pushing = true;
            try
            {
                IdleSeconds.Value = seconds;
            }
            finally
            {
                _pushing = false;
            }
        }

        /// <summary>Move the reactions dial to Custom when the numbers no longer match it.</summary>
        private static void NoticeReactionsEdited()
        {
            if (_pushing || !ChatterPresets.TryGaps(ChatterFrequency.Value, out ChatterGaps gaps))
            {
                return;
            }

            if (MinGapSeconds.Value != gaps.MinGapSeconds
                || SpeakerCooldownSeconds.Value != gaps.SpeakerCooldownSeconds
                || SquadEchoWindowSeconds.Value != gaps.SquadEchoWindowSeconds)
            {
                ChatterFrequency.Value = ChatterAmount.Custom;
            }
        }

        /// <summary>Move the idle dial to Custom when its number no longer matches it.</summary>
        private static void NoticeIdleEdited()
        {
            if (_pushing || !ChatterPresets.TryIdleSeconds(IdleChatter.Value, out float seconds))
            {
                return;
            }

            if (IdleSeconds.Value != seconds)
            {
                IdleChatter.Value = ChatterAmount.Custom;
            }
        }

        /// <summary>Where a visible setting sits in its section.</summary>
        /// <returns>A tag for ConfigurationManager, ignored by everything else.</returns>
        /// <param name="order">Higher comes first.</param>
        private static ConfigurationManagerAttributes At(int order)
        {
            return new ConfigurationManagerAttributes { Order = order };
        }

        /// <summary>Where a setting sits, and that it is hidden until Advanced is ticked.</summary>
        /// <returns>A tag for ConfigurationManager, ignored by everything else.</returns>
        /// <param name="order">Higher comes first.</param>
        private static ConfigurationManagerAttributes Adv(int order)
        {
            return new ConfigurationManagerAttributes { IsAdvanced = true, Order = order };
        }
    }
}
