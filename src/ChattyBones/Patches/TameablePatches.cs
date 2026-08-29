using System;
using ChattyBones.Logic;
using HarmonyLib;

namespace ChattyBones.Patches
{
    /// <summary>Puts a <see cref="ChatterComponent"/> on every summon as it wakes up.</summary>
    /// <remarks>
    /// Tameable.Awake is the right place because Tameable is the component that makes
    /// a creature one of ours in the first place, so anything with one is worth
    /// looking at and nothing without one ever is.
    ///
    /// The check is <see cref="Summons.IsSummoned"/>, which asks the prefab about
    /// unsummon behavior rather than reading s_follow. See the remarks there.
    /// </remarks>
    [HarmonyPatch(typeof(Tameable), "Awake")]
    internal static class TameableAwakePatch
    {
        private static void Postfix(Tameable __instance)
        {
            try
            {
                Character character = __instance.m_character;

                if (Summons.IsSummoned(character) && character.GetComponent<ChatterComponent>() == null)
                {
                    _ = character.gameObject.AddComponent<ChatterComponent>();
                }
            }
            catch (Exception e)
            {
                ChattyBonesPlugin.Log.LogWarning("ChattyBones could not attach to a summon: " + e);
            }
        }
    }

    /// <summary>Lets a skeleton say something on its way out.</summary>
    /// <remarks>
    /// A prefix rather than a postfix: RPC_UnSummon destroys the GameObject, and we
    /// want the skeleton still standing when we ask it for its name and its head.
    ///
    /// The line then outlives it, which I had assumed it would not. Chat keeps the
    /// position handed to AddInworldText and falls back to it the moment the object
    /// the text was anchored to stops existing, so a parting line hangs in the air
    /// where the skeleton was and drifts gently upward for the rest of its five
    /// seconds. See <see cref="Speech"/> for the rest of that.
    /// </remarks>
    [HarmonyPatch(typeof(Tameable), "RPC_UnSummon")]
    internal static class TameableUnSummonPatch
    {
        private static void Prefix(Tameable __instance)
        {
            try
            {
                Character character = __instance.m_character;
                ChatterComponent speaker = character == null ? null : character.GetComponent<ChatterComponent>();

                if (speaker != null)
                {
                    _ = Chatter.TrySpeak(speaker, ChatterEvent.Unsummoned, subject: 0, targetName: null, companion: null);
                }
            }
            catch (Exception e)
            {
                ChattyBonesPlugin.Log.LogWarning("ChattyBones stumbled over an unsummon: " + e);
            }
        }
    }
}
