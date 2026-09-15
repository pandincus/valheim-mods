using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace BubbleBar
{
    /// <summary>
    /// Entry point to the mod. BepInEx finds this class by the attribute below,
    /// attaches it to a hidden GameObject, and calls Awake once while the game
    /// is starting up.
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInProcess("valheim.exe")]
    public class BubbleBarPlugin : BaseUnityPlugin
    {
        // The GUID is the mod's identity: BepInEx names the config file after
        // it, and other mods would depend on it by this string. We should treat
        // modifying this in the future as a breaking change.
        public const string PluginGuid = "pandincus.bubblebar";
        public const string PluginName = "BubbleBar";
        // Keep in step with <Version> in BubbleBar.csproj.
        public const string PluginVersion = "0.1.0";

        internal static ManualLogSource Log;

        private Harmony _harmony;

        /// <summary>
        /// Set up the config and apply our patches. BepInEx calls this once, during
        /// startup, before the game has loaded anything interesting.
        /// </summary>
        /// <remarks>
        /// We patch unconditionally rather than checking <see cref="ModConfig.Enabled"/>
        /// here, and let the patches themselves fall through when it is off. That costs a
        /// branch whenever a barrier is cast or an item tooltip carrying one is drawn, and it
        /// buys a kill switch you can flip mid-game in ConfigurationManager (F1) rather than
        /// one that needs a restart.
        /// </remarks>
        private void Awake()
        {
            Log = Logger;

            // Both of these are wrapped for the same reason the patch loop below is. An
            // exception escaping Awake skips everything after it, so a bad config value or a
            // console command that would not register would leave the mod loaded, unpatched
            // and completely silent - which reads as a mod that has no effect rather than one
            // that crashed.
            try
            {
                ModConfig.Init(Config);
                DebugCommands.Register();
            }
            catch (System.Exception e)
            {
                Log.LogError(
                    PluginName + " could not set itself up, so it will do nothing this session. "
                    + e);

                return;
            }

            _harmony = new Harmony(PluginGuid);

            // A class at a time rather than PatchAll. PatchAll stops at the first class
            // that throws and leaves every later one unapplied, so one moved target
            // silently costs a set of unrelated behaviour with nothing saying which -
            // and the exception escapes Awake, which takes the rest of startup with it.
            // Valheim 1.0 moved three of ChattyBones' targets at once, so this is not
            // hypothetical. There is only one patch class here today and the loop still
            // earns its place, because the cost of adding it later is remembering to.
            foreach (System.Type type in AccessTools.GetTypesFromAssembly(
                         System.Reflection.Assembly.GetExecutingAssembly()))
            {
                try
                {
                    _harmony.CreateClassProcessor(type).Patch();
                }
                catch (System.Exception e)
                {
                    Log.LogError(
                        PluginName + " could not apply " + type.Name + ", so what it carries "
                        + "will not happen. The usual cause is a Valheim update having moved "
                        + "what it hooks. " + e);
                }
            }

            Log.LogInfo(PluginName + " v" + PluginVersion + " loaded.");
        }

        /// <summary>
        /// Draw the bars. BepInEx plugins are MonoBehaviours, so this is an ordinary Unity
        /// Update running once a frame.
        /// </summary>
        /// <remarks>
        /// Doing this from here rather than from a patch on <c>Hud</c> or <c>EnemyHud</c> is
        /// what keeps the mod down to a single Harmony patch. See <see cref="Bars"/>.
        ///
        /// On most frames this is a couple of null checks and a walk over the handful of
        /// status effects on each visible character.
        /// </remarks>
        private void Update()
        {
            // A world change destroys every HUD object we cloned into. Unity's == is the right
            // test here rather than ReferenceEquals: we are asking whether the HUD still
            // exists, not whether it is the same object as before.
            if (Hud.instance == null)
            {
                Bars.Reset();

                // A fresh HUD is a clean slate, so a drawing fault that was really about the
                // last world does not cost you the bars for the whole session.
                _drawing = true;
            }
            else if (_drawing)
            {
                try
                {
                    Bars.Tick();
                }
                catch (System.Exception e)
                {
                    // A throw here repeats every frame, which buries the log and the reason
                    // with it. Stop drawing, and sweep away anything already on screen - a bar
                    // left frozen over an ally is worse than no bar.
                    _drawing = false;

                    Log.LogError(
                        PluginName + " could not draw the barrier bars, so they are off until "
                        + "you next load a world. The refill and the multiplayer half are "
                        + "unaffected. " + e);

                    try
                    {
                        Bars.Hide();
                    }
                    catch (System.Exception)
                    {
                        // Already reported; there is nothing useful to add about a sweep that
                        // failed while cleaning up after a failure.
                    }
                }
            }

            // Deliberately outside the HUD check and its own try. Publishing is about the
            // characters we own, not about anything on our screen, and the two used to share a
            // catch - so one bad frame of drawing took our barrier off every other player's
            // screen for the rest of the session.
            if (!_publishing)
            {
                return;
            }

            try
            {
                Mirror.Publish(Time.deltaTime);
            }
            catch (System.Exception e)
            {
                _publishing = false;

                Log.LogError(
                    PluginName + " could not publish your barrier, so other players will not "
                    + "see it this session. Your own bars are unaffected. " + e);
            }
        }

        /// <summary>False once drawing has thrown, until the next world.</summary>
        private bool _drawing = true;

        /// <summary>False once publishing has thrown.</summary>
        private bool _publishing = true;

        /// <summary>
        /// Take our patches back off on the way out, so we leave the game as we found it.
        /// </summary>
        /// <remarks>
        /// Practically speaking this only matters when something reloads plugins at
        /// runtime, since a normal quit tears the whole process down anyway.
        /// </remarks>
        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }
    }
}
