using BepInEx;
using BepInEx.Logging;
using UnityEngine;

namespace PassThroughSkeletts
{
    /// <summary>
    /// Entry point to the mod. BepInEx finds this class by the attribute below,
    /// attaches it to a hidden GameObject, and calls Awake once while the game
    /// is starting up.
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInProcess("valheim.exe")]
    public class PassThroughSkelettsPlugin : BaseUnityPlugin
    {
        // The GUID is the mod's identity: BepInEx names the config file after
        // it, and other mods would depend on it by this string. We should treat
        // modifying this in the future as a breaking change.
        public const string PluginGuid = "pandincus.passthroughskeletts";
        public const string PluginName = "PassThroughSkeletts";
        // Keep in step with <Version> in PassThroughSkeletts.csproj.
        public const string PluginVersion = "0.1.0";

        /// <summary>How often to look for pairs that need ignoring, in seconds.</summary>
        /// <remarks>
        /// This is the longest a freshly summoned Skelett can shove you before it stops. A
        /// tenth of a second is a handful of frames, and cheap: a few Skeletts times a few
        /// players times a few colliders each.
        /// </remarks>
        private const float SweepInterval = 0.1f;

        internal static ManualLogSource Log;

        private float _sinceSweep;

        /// <summary>False once a sweep has thrown, until the next world.</summary>
        private bool _sweeping = true;

        /// <summary>
        /// Set up the config and the console command. BepInEx calls this once, during
        /// startup, before the game has loaded anything interesting.
        /// </summary>
        /// <remarks>
        /// There is no Harmony here at all. The game never has to be told anything: we ask
        /// Unity's physics to stop colliding two bodies, and it does. So there is also no
        /// patch that a Valheim update could move out from under us.
        ///
        /// Wrapped because an exception escaping Awake would leave the mod loaded and
        /// doing nothing, which looks exactly like a mod that has no effect rather than one
        /// that crashed.
        /// </remarks>
        private void Awake()
        {
            Log = Logger;

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

                _sweeping = false;
                return;
            }

            Log.LogInfo(PluginName + " v" + PluginVersion + " loaded.");
        }

        /// <summary>
        /// Keep every Skelett and every player ignoring each other. BepInEx plugins are
        /// MonoBehaviours, so this is an ordinary Unity Update.
        /// </summary>
        private void Update()
        {
            // Nothing to do at the main menu. It is also where a fault gets forgiven: a sweep
            // that threw because of something odd in one world should not cost you the mod
            // in the next.
            if (Player.m_localPlayer == null)
            {
                _sinceSweep = 0f;

                // If Awake failed, the config was never bound and there is nothing to read, so
                // forgiving a fault here must not switch sweeping back on.
                _sweeping = ModConfig.Enabled != null;
                return;
            }

            if (!_sweeping)
            {
                return;
            }

            _sinceSweep += Time.deltaTime;

            if (_sinceSweep < SweepInterval)
            {
                return;
            }

            _sinceSweep = 0f;

            try
            {
                Passthrough.Sweep(ModConfig.Enabled.Value);
            }
            catch (System.Exception e)
            {
                // A throw here would repeat ten times a second and bury the reason in the log.
                _sweeping = false;

                Log.LogError(
                    PluginName + " could not sweep for Skeletts, so it is off until you next "
                    + "load a world. " + e);
            }
        }
    }
}
