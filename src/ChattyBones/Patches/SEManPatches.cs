using ChattyBones.Logic;
using HarmonyLib;

namespace ChattyBones.Patches
{
    /// <summary>Reacts to a skeleton picking up a status effect.</summary>
    /// <remarks>
    /// The Staff of Protection does apply to summons - confirmed in game - which is
    /// what makes this hook worth having rather than a curiosity.
    ///
    /// Of the several ways into SEMan this is the one that always runs. The int-hash
    /// overload only reaches the effect on the owner and otherwise sends an RPC, and
    /// Internal_AddStatusEffect just hands over to this one; both paths arrive here,
    /// so hooking here catches an effect however it was applied.
    ///
    /// Gated on the return value, which is null when the effect was already running
    /// or the character refused it. Without that, a shield being refreshed every few
    /// seconds reads as a fresh buff every time.
    /// </remarks>
    [HarmonyPatch(typeof(SEMan), nameof(SEMan.AddStatusEffect), typeof(StatusEffect), typeof(bool), typeof(int), typeof(float))]
    internal static class SEManAddStatusEffectPatch
    {
        private static void Postfix(SEMan __instance, StatusEffect statusEffect, StatusEffect __result)
        {
            if (!ModConfig.Enabled.Value || __result == null || __instance == null || statusEffect == null)
            {
                return;
            }

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

            // The effect's name hash is a kind of thing rather than a particular one,
            // so it is a safe subject: two skeletons shielded by the same cast produce
            // one remark between them.
            _ = Chatter.TrySpeak(
                speaker,
                ChatterEvent.Buffed,
                statusEffect.NameHash(),
                targetName: null,
                companion: null);
        }
    }
}
