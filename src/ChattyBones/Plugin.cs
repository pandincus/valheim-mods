using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace ChattyBones
{
    /// <summary>
    /// Entry point to the mod. BepInEx finds this class by the attribute below,
    /// attaches it to a hidden GameObject, and calls Awake once while the game
    /// is starting up.
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInProcess("valheim.exe")]
    public class ChattyBonesPlugin : BaseUnityPlugin
    {
        // The GUID is the mod's identity: BepInEx names the config file after
        // it, and other mods would depend on it by this string. We should treat
        // modifying this in the future as a breaking change.
        public const string PluginGuid = "pandincus.chattybones";
        public const string PluginName = "ChattyBones";
        // Keep in step with <Version> in ChattyBones.csproj.
        public const string PluginVersion = "0.1.0";

        internal static ManualLogSource Log;

        private Harmony _harmony;

        /// <summary>False when Awake gave up, which is what stops Update running on a half-built mod.</summary>
        private bool _started;

        /// <summary>
        /// Set up the config and apply our patches. BepInEx calls this once, during
        /// startup, before the game has loaded anything interesting.
        /// </summary>
        /// <remarks>
        /// We patch unconditionally rather than checking <see cref="ModConfig.Enabled"/>
        /// here, and let the patches themselves fall through when it is off. That
        /// costs a branch on a path that is already cheap, and it buys a kill
        /// switch you can flip mid-game in ConfigurationManager (F1) rather than
        /// one that needs a restart. Skeletons going quiet is exactly the kind of
        /// thing you want to try without leaving the world.
        ///
        /// Nothing here touches game data. The game has not built ObjectDB or any
        /// scene yet, and no skeleton exists to talk to.
        /// </remarks>
        private void Awake()
        {
            Log = Logger;
            ModConfig.Init(Config);
            Speech.Resolve();

            // Init reads a file and parses YAML, so unlike everything else here it can
            // fail for reasons outside the mod - and an exception escaping Awake would
            // stop before PatchAll below, leaving us loaded, unpatched and silent with
            // only a raw stack trace to go on. Missing YamlDotNet is the likely cause
            // and it fails exactly this way, so name it.
            try
            {
                Chatter.Init();
            }
            catch (System.Exception e)
            {
                Log.LogError(
                    PluginName + " could not start, so your skeletons will stay quiet this session. "
                    + "The usual cause is a missing YamlDotNet - this mod needs the "
                    + "ValheimModding-YamlDotNet package, which a mod manager installs for you. " + e);

                return;
            }

            DebugCommands.Register();

            // BepInEx raises this off the main thread, which is exactly why
            // RefreshSettings builds a whole new settings object rather than editing
            // the one in use. See the note on ChatterSettings.
            Config.SettingChanged += (_, _) => Chatter.RefreshSettings();

            // PatchAll finds every [HarmonyPatch] class in this assembly and
            // applies it - everything under Patches/.
            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll();

            _started = true;

            Log.LogInfo(PluginName + " v" + PluginVersion + " loaded.");
        }

        /// <summary>
        /// Drive the squad's sweep. BepInEx plugins are MonoBehaviours, so this is an
        /// ordinary Unity Update running once a frame.
        /// </summary>
        /// <remarks>
        /// One sweep for the whole squad rather than an Update on each skeleton. The
        /// budget's rule is that a claim must be resolved before the next one is made,
        /// and that is easy to honour in a loop we control and awkward in a set of
        /// components Unity calls in an order of its own choosing.
        ///
        /// <see cref="Chatter.Tick"/> is a decrement and a comparison on most frames;
        /// the actual work happens four times a second.
        /// </remarks>
        private void Update()
        {
            if (!_started)
            {
                return;
            }

            Chatter.Tick(Time.deltaTime);
        }

        /// <summary>
        /// Take our patches back off on the way out, so we leave the game as we found it.
        /// </summary>
        /// <remarks>
        /// Practically speaking this only matters when something reloads plugins at
        /// runtime, since a normal quit tears the whole process down anyway. The
        /// watcher on the line pack is a real OS handle rather than something the
        /// garbage collector will get round to, so it is worth letting go of properly.
        /// </remarks>
        private void OnDestroy()
        {
            PackFile.StopWatching();
            _harmony?.UnpatchSelf();
        }
    }
}
