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
                    new AcceptableValueRange<float>(1f, 30f), "Advanced"));

            DialoguePanelCullDistance = cfg.Bind(
                "Appearance", "DialoguePanelCullDistance", 20f,
                new ConfigDescription(
                    "How far away you can be and still see a DialoguePanel line, in meters. " +
                    "Ignored by FloatingText.",
                    new AcceptableValueRange<float>(5f, 100f), "Advanced"));

            TextHeight = cfg.Bind(
                "Appearance", "TextHeight", 0.3f,
                new ConfigDescription(
                    "Extra height above the skeleton's head, in meters, so the line clears " +
                    "the name label. 0.3 sits just clear of it; by 1.0 the text looks " +
                    "detached from whoever said it. 0 puts it exactly where Valheim puts a " +
                    "player's chat, which lands right on the name.",
                    new AcceptableValueRange<float>(0f, 2f), "Advanced"));

            TextColor = cfg.Bind(
                "Appearance", "TextColor", "",
                "One color for everything the skeletons say, as a hex code like #C8FFC8. " +
                "Leave it empty - which is the default - and the line pack decides " +
                "instead, which is usually what you want: packs color by event, so a " +
                "death cry reads as bad news before you have read a word of it. Set this " +
                "to override the pack entirely. Accepts #RGB, #RRGGBB and #RRGGBBAA, and " +
                "anything that is not a hex code is ignored with a note in the log.");

            ChatterFrequency = cfg.Bind(
                "Chatter", "ChatterFrequency", ChatterAmount.Sometimes,
                "How much the squad reacts to things - fights, weather, what you pick up, " +
                "each other. Sometimes is what the mod ships with.\n" +
                "Never leaves them reacting to nothing at all, which is not the same as " +
                "silence: they will still mutter to themselves if IdleChatter lets them. " +
                "Custom hands the decision to MinGapSeconds, SpeakerCooldownSeconds and " +
                "SquadEchoWindowSeconds in the advanced settings, which are ignored until " +
                "you pick it.");

            IdleChatter = cfg.Bind(
                "Chatter", "IdleChatter", ChatterAmount.Sometimes,
                "How often a skeleton with nothing happening says something anyway. Its own " +
                "dial rather than part of the one above, because they are different " +
                "complaints: a squad that talks over a fight and a squad that will not stop " +
                "musing on the weather want opposite answers.\n" +
                "Custom hands the decision to IdleSeconds in the advanced settings.");

            SilencedEvents = cfg.Bind(
                "Chatter", "SilencedEvents", "",
                new ConfigDescription(
                    "Events to switch off completely, separated by commas - for example " +
                    "\"Weather, PlayerAte\". Everything else carries on as normal. The names " +
                    "are the ones listed at the top of ChattyBones.lines.yaml, and anything " +
                    "that is not one of them is reported in the log and otherwise ignored.\n" +
                    "Deleting an event from your line pack does the same thing and is the " +
                    "better move if you are editing the pack anyway. This is for when you " +
                    "are not.",
                    null, "Advanced"));

            MinGapSeconds = cfg.Bind(
                "Chatter", "MinGapSeconds", 2.5f,
                new ConfigDescription(
                    "How long the whole squad stays quiet after any one of them speaks. This " +
                    "is the main dial for how talkative they are: raise it if five skeletons " +
                    "feel like a crowd, lower it if they feel asleep.\n" +
                    "Only used when ChatterFrequency is Custom.",
                    new AcceptableValueRange<float>(0f, 30f), "Advanced"));

            PreemptGapSeconds = cfg.Bind(
                "Chatter", "PreemptGapSeconds", 0.5f,
                new ConfigDescription(
                    "How long something important waits before cutting in on something " +
                    "trivial. Without a gap a death cry can land in the same frame as the " +
                    "idle mutter it interrupts, and two lines at once is two lines nobody reads.",
                    new AcceptableValueRange<float>(0f, 10f), "Advanced"));

            SpeakerCooldownSeconds = cfg.Bind(
                "Chatter", "SpeakerCooldownSeconds", 8f,
                new ConfigDescription(
                    "How long one skeleton waits before speaking again. Much longer than " +
                    "MinGapSeconds on purpose - the squad keeps a conversation going while " +
                    "each individual stays fairly quiet, which reads as several people rather " +
                    "than one person with a lot to say.\n" +
                    "Only used when ChatterFrequency is Custom.",
                    new AcceptableValueRange<float>(0f, 120f), "Advanced"));

            SquadEchoWindowSeconds = cfg.Bind(
                "Chatter", "SquadEchoWindowSeconds", 6f,
                new ConfigDescription(
                    "How long one remark about a thing stops the others remarking on it too. " +
                    "Send five skeletons at one greydwarf and all five notice it inside the " +
                    "same second, so without this you get five near-identical lines at once.\n" +
                    "Only used when ChatterFrequency is Custom.",
                    new AcceptableValueRange<float>(0f, 60f), "Advanced"));

            IdleSeconds = cfg.Bind(
                "Chatter", "IdleSeconds", 45f,
                new ConfigDescription(
                    "Roughly how often a skeleton with nothing to do says something anyway. " +
                    "Scattered by a quarter either way, so a squad summoned together does not " +
                    "get bored together.\n" +
                    "Only used when IdleChatter is Custom.",
                    new AcceptableValueRange<float>(5f, 600f), "Advanced"));

            SummonGreetingSeconds = cfg.Bind(
                "Chatter", "SummonGreetingSeconds", 5f,
                new ConfigDescription(
                    "How new a skeleton has to be to greet you. Skeletons are rebuilt every " +
                    "time you walk back into their area, so this is what separates being " +
                    "raised from being reloaded. Only raise it if greetings are being missed " +
                    "on a slow machine.",
                    new AcceptableValueRange<float>(1f, 60f), "Advanced"));

            HurtFraction = cfg.Bind(
                "Chatter", "HurtFraction", 0.15f,
                new ConfigDescription(
                    "How big a hit has to be before a skeleton mentions it, as a share of its " +
                    "own maximum health. At 0.15 it complains about anything taking a seventh " +
                    "of it; at 0.01 it complains about everything.",
                    new AcceptableValueRange<float>(0.01f, 1f), "Advanced"));

            BigHitFraction = cfg.Bind(
                "Chatter", "BigHitFraction", 0.15f,
                new ConfigDescription(
                    "How hard you have to hit something before the squad is impressed, as a " +
                    "share of THAT CREATURE'S maximum health - not of the damage you usually " +
                    "do. Kills are handled separately, so this is only about a swing that did " +
                    "not quite finish the job.\n" +
                    "Being a share of the victim's health has an awkward consequence worth " +
                    "knowing before you tune it: the tougher the enemy, the harder it is to " +
                    "impress anybody. At 0.15 a draugr with a hundred health notices anything " +
                    "taking fifteen of it, while a troll's six hundred wants ninety - a real " +
                    "two-handed swing - and a boss is out of reach at any setting you would " +
                    "want. A hit that takes half a greydwarf usually kills it outright and is " +
                    "counted as a kill instead. So the lines come from mid-sized enemies more " +
                    "than from your best ones, and lowering this is what brings the big " +
                    "creatures in at all.\n" +
                    "0.15 is deliberately generous. How often you actually hear it is decided " +
                    "by the gaps the squad keeps anyway, so this only decides what counts.",
                    new AcceptableValueRange<float>(0.01f, 1f), "Advanced"));

            HearOthers = cfg.Bind(
                "Multiplayer", "HearOthers", true,
                "Whether other players' skeletons talk on your screen. They only ever say " +
                "what their own player's game decided they say - this side does no choosing " +
                "at all - so switching it off is a statement about your screen rather than " +
                "about their squad.\n" +
                "Players without ChattyBones see nothing either way, and are told nothing: " +
                "the mod writes what it needs into fields the game already syncs, and an " +
                "unmodded client ignores a field it does not recognize.");

            AllyGreetingDistance = cfg.Bind(
                "Multiplayer", "AllyGreetingDistance", 15f,
                new ConfigDescription(
                    "How close another player has to come to one of your skeletons before it " +
                    "says hail, in metres. Measured from the skeletons rather than from you, " +
                    "so one told to hold position at home greets whoever walks past it. The " +
                    "same distance decides whether an ordinary line can use {ally}.\n" +
                    "Pairs with AllyGreetingForgetSeconds below: a large value here means a " +
                    "greeting from further off, not a greeting more often.",
                    new AcceptableValueRange<float>(2f, 60f), "Advanced"));

            AllyGreetingForgetSeconds = cfg.Bind(
                "Multiplayer", "AllyGreetingForgetSeconds", 60f,
                new ConfigDescription(
                    "How long another player has to stay out of range before the squad will " +
                    "greet them again, in seconds. This is the dial that decides whether a greeting " +
                    "is a welcome or a tic.\n" +
                    "The two of you playing side by side will cross that boundary far more " +
                    "often than you would think, so err high. A greeting you did not get is " +
                    "a small loss; one you get every few minutes from the same person is the " +
                    "kind of line you stop hearing.",
                    new AcceptableValueRange<float>(5f, 600f), "Advanced"));

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
