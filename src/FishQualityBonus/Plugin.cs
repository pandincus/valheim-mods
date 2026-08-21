using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace FishQualityBonus
{
    /// <summary>
    /// Entry point to the mod. BepInEx finds this class by the attribute below,
    /// attaches it to a hidden GameObject, and calls Awake once while the game
    /// is starting up.
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInProcess("valheim.exe")]
    public class FishQualityBonusPlugin : BaseUnityPlugin
    {
        // The GUID is the mod's identity: BepInEx names the config file after
        // it, and other mods would depend on it by this string. We should treat
        // modifying this in the future as a breaking change.
        public const string PluginGuid = "pandincus.fishqualitybonus";
        public const string PluginName = "FishQualityBonus";
        // Keep in step with <Version> in FishQualityBonus.csproj.
        public const string PluginVersion = "0.1.0";

        internal static ManualLogSource Log;

        private Harmony _harmony;

        /// <summary>
        /// Set up the config and apply our patches. BepInEx calls this once, during
        /// startup, before the game has loaded anything interesting.
        ///
        /// Nothing here reads game data - ObjectDB isn't built yet. Anything that needs
        /// recipes or items waits for <see cref="ObjectDbHooks"/> instead.
        /// </summary>
        private void Awake()
        {
            Log = Logger;
            ModConfig.Init(Config);

            // PatchAll finds every [HarmonyPatch] class in this assembly and
            // applies it, so Patches.cs and ObjectDbHooks.cs hook themselves up.
            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll();

            Log.LogInfo(PluginName + " v" + PluginVersion + " loaded.");
        }

        /// <summary>
        /// Take our patches back off on the way out, so we leave the game as we found it.
        ///
        /// Practically speaking this only matters when something reloads plugins at
        /// runtime, since a normal quit tears the whole process down anyway.
        /// </summary>
        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }
    }
}
