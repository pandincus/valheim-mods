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
            try
            {
                React(__instance, statusEffect, __result);
            }
            catch (System.Exception e)
            {
                ChattyBonesPlugin.Log.LogWarning("ChattyBones stumbled over a status effect: " + e);
            }
        }

        /// <summary>Have the skeleton thank you for the shield, or complain about the fire.</summary>
        /// <param name="seman">The status effect manager the effect was added to.</param>
        /// <param name="statusEffect">The effect being added.</param>
        /// <param name="added">What AddStatusEffect returned. Null when nothing was added.</param>
        private static void React(SEMan seman, StatusEffect statusEffect, StatusEffect added)
        {
            if (!ModConfig.Enabled.Value || added == null || seman == null || statusEffect == null)
            {
                return;
            }

            Character character = seman.m_character;
            if (character == null)
            {
                return;
            }

            ChatterComponent speaker = character.GetComponent<ChatterComponent>();
            if (speaker == null)
            {
                return;
            }

            // Valheim has no flag for good versus bad - StatusEffect.m_attributes is
            // about cold resistance and sailing - so the subclass is the signal, and
            // StatusKind keeps the list. Anything unrecognised arrives as Buffed,
            // which is the gentler of the two wrong answers.
            bool harmful = StatusKind.IsHarmful(statusEffect.GetType().Name);

            // The effect's name hash is a kind of thing rather than a particular one,
            // so it is a safe subject: two skeletons shielded by the same cast produce
            // one remark between them.
            _ = Chatter.TrySpeak(
                speaker,
                harmful ? ChatterEvent.Afflicted : ChatterEvent.Buffed,
                statusEffect.NameHash(),
                targetName: null,
                companion: null,
                details: new LineDetails(status: Localization.instance.Localize(statusEffect.m_name)));
        }
    }
}
