using BepInEx.Configuration;

namespace BubbleBar
{
    /// <summary>
    /// Every setting the mod has. BepInEx writes these to
    /// BepInEx/config/pandincus.bubblebar.cfg on first run, and
    /// ConfigurationManager (F1) edits them live - the code reads .Value every
    /// time, so changes take effect straight away with no restart.
    ///
    /// These descriptions are visible to players using the config manager.
    /// </summary>
    internal static class ModConfig
    {
        /// <summary>The bar's shipped color, and what an unreadable <see cref="BarColor"/> falls back to.</summary>
        internal const string DefaultBarColor = "#168DF5";

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> RestoreOnRefresh;
        internal static ConfigEntry<bool> ShowOwnBar;
        internal static ConfigEntry<bool> ShowAllyBars;
        internal static ConfigEntry<string> BarColor;
        internal static ConfigEntry<float> OwnBarOffsetX;
        internal static ConfigEntry<float> OwnBarOffsetY;
        internal static ConfigEntry<float> OwnBarWidth;
        internal static ConfigEntry<float> OwnBarThickness;
        internal static ConfigEntry<bool> ShowOwnBarNumber;
        internal static ConfigEntry<float> AllyBarOffsetY;
        internal static ConfigEntry<bool> ShowOthersBarriers;
        internal static ConfigEntry<bool> LogBarrierEvents;

        /// <summary>
        /// Declare every setting. Called once from <see cref="BubbleBarPlugin.Awake"/>.
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
        /// <remarks>
        /// The mod does two unrelated things - it changes what a refresh does, and it draws
        /// something that was never drawn - so they get a switch each rather than sharing
        /// one. Somebody who thinks the vanilla refresh is fine should still be able to see
        /// how much barrier is left, and somebody who wants the fix and no new UI should be
        /// able to have that too.
        /// </remarks>
        internal static void Init(ConfigFile cfg)
        {
            Enabled = cfg.Bind(
                "General", "Enabled", true,
                "Master switch. Turn this off and the mod does nothing at all - the barrier " +
                "behaves exactly as it does in vanilla, and nothing new is drawn.");

            RestoreOnRefresh = cfg.Bind(
                "Barrier", "RestoreOnRefresh", true,
                new ConfigDescription(
                    "With this on, recasting the Staff of Protection while a barrier is already up refills it.\n" +
                    "Vanilla only resets the timer: a barrier that has soaked 190 of its 200 " +
                    "damage keeps all 190 and pops on the next hit, however long it now says " +
                    "it will last. With this on, a recast puts it back to a full 200.\n" +
                    "Turn it off to keep vanilla's behavior."));

            ShowOwnBar = cfg.Bind(
                "Bars", "ShowOwnBar", true,
                "Show a bar on your own HUD while you have a magic barrier.");

            ShowAllyBars = cfg.Bind(
                "Bars", "ShowAllyBars", true,
                new ConfigDescription(
                    "Show a bar over allies who have a barrier, under the health bar you already " +
                    "see when you look at them. Never shown on enemies.\n" +
                    "Another player's barrier is only visible to you if they are running this mod " +
                    "too (their game is what publishes the number). Your own summons and tames " +
                    "work regardless, as long as you're nearby."));

            BarColor = cfg.Bind(
                "Bars", "BarColor", DefaultBarColor,
                "The color of the barrier bar, as a hex value. The unfilled part stays dark, " +
                "similar to eitr/stamina/adrenaline/health.");

            OwnBarWidth = cfg.Bind(
                "Bars", "OwnBarWidth", 128f,
                new ConfigDescription(
                    "How wide your own barrier bar is, in HUD units. 128 matches the eitr bar at " +
                    "100 maximum eitr.",
                    new AcceptableValueRange<float>(32f, 512f)));

            OwnBarThickness = cfg.Bind(
                "Bars", "OwnBarThickness", 1f,
                new ConfigDescription(
                    "How thick your own barrier bar is, as a multiplier. 1 matches the stamina " +
                    "and eitr bars it sits under. Raise it to be chunkier, lower to be thinner.",
                    new AcceptableValueRange<float>(0.4f, 3f)));

            ShowOwnBarNumber = cfg.Bind(
                "Bars", "ShowOwnBarNumber", true,
                "Print how much damage your barrier can still soak atop your own bar, the " +
                "way stamina and eitr print theirs. Ally bars never get a number.");

            // The bar is placed relative to the eitr bar, and the defaults were found by
            // nudging it in game until it sat under stamina and eitr. These offsets are for
            // anyone whose HUD is laid out differently, usually because another mod has moved
            // things around. They take effect live in ConfigurationManager, so no restart.
            OwnBarOffsetX = cfg.Bind(
                "Bars", "OwnBarOffsetX", 0f,
                new ConfigDescription(
                    "Sideways nudge for your own barrier bar, relative to the eitr bar.",
                    new AcceptableValueRange<float>(-400f, 400f)));

            OwnBarOffsetY = cfg.Bind(
                "Bars", "OwnBarOffsetY", -40f,
                new ConfigDescription(
                    "Vertical nudge for your own barrier bar, relative to the eitr bar. " +
                    "Negative is down.",
                    new AcceptableValueRange<float>(-400f, 400f)));

            AllyBarOffsetY = cfg.Bind(
                "Bars", "AllyBarOffsetY", -9f,
                new ConfigDescription(
                    "How far under an ally's health bar their barrier bar sits. Negative is down.",
                    new AcceptableValueRange<float>(-60f, 60f)));

            ShowOthersBarriers = cfg.Bind(
                "Multiplayer", "ShowOthersBarriers", true,
                new ConfigDescription(
                    "Draw bars for barriers on characters another player's game is running - " +
                    "other players, and their summons.\n" +
                    "Your own barrier and your own summons do not go through this; they are read " +
                    "directly and are unaffected. Turn it off if another player's bar ever looks " +
                    "wrong, without losing your own."));

            LogBarrierEvents = cfg.Bind(
                "Diagnostics", "LogBarrierEvents", false,
                "Write a line to the BepInEx log every time a barrier is actually refilled, " +
                "saying how much it had soaked and what it went back to. Meant for working out " +
                "whether the refill is doing what you think.\n" +
                "It says nothing about a barrier going up for the first time, since there is " +
                "nothing to restore, and nothing at all while RestoreOnRefresh is off.");
        }
    }
}
