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
    /// unsummon behaviour rather than reading s_follow. That distinction matters
    /// more here than anywhere: s_follow is set by a routed RPC, so it is still
    /// empty at Awake - a skeleton would fail the test at the exact moment it was
    /// summoned and pass it on every zone reload afterwards.
    /// </remarks>
    [HarmonyPatch(typeof(Tameable), "Awake")]
    internal static class TameableAwakePatch
    {
        private static void Postfix(Tameable __instance)
        {
            Character character = __instance.m_character;

            if (!Summons.IsSummoned(character) || character.GetComponent<ChatterComponent>() != null)
            {
                return;
            }

            _ = character.gameObject.AddComponent<ChatterComponent>();
        }
    }

    /// <summary>Lets a skeleton say something on its way out.</summary>
    /// <remarks>
    /// A prefix rather than a postfix, because RPC_UnSummon destroys the GameObject
    /// on the owner and a line drawn after that has nothing left to hang on.
    ///
    /// It is only half a win even so: the floating text is parented to the skeleton,
    /// so on the owner's screen the parting line goes down with it and is visible
    /// for a frame at best. Other clients keep the object a moment longer and do
    /// rather better. Making this actually readable needs the text to outlive its
    /// speaker, which is a change to <see cref="Speech"/> rather than to the hook.
    /// </remarks>
    [HarmonyPatch(typeof(Tameable), "RPC_UnSummon")]
    internal static class TameableUnSummonPatch
    {
        private static void Prefix(Tameable __instance)
        {
            Character character = __instance.m_character;
            if (character == null)
            {
                return;
            }

            ChatterComponent speaker = character.GetComponent<ChatterComponent>();
            if (speaker == null)
            {
                return;
            }

            _ = Chatter.TrySpeak(speaker, ChatterEvent.Unsummoned, subject: 0, targetName: null, companion: null);
        }
    }
}
