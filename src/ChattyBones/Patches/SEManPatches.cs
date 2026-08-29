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

        /// <summary>Which of the three status events this effect belongs to.</summary>
        /// <returns>Afflicted for an injury, Weather for the ambient ones, Buffed for the rest.</returns>
        /// <param name="effectName">The effect's asset name.</param>
        /// <remarks>
        /// Buffed is the fallback rather than a category, so an effect nobody has
        /// classified is thanked for. That is the gentlest of the three wrong answers.
        /// </remarks>
        private static ChatterEvent Kind(string effectName)
        {
            if (StatusKind.IsHarmful(effectName))
            {
                return ChatterEvent.Afflicted;
            }

            return StatusKind.IsWeather(effectName) ? ChatterEvent.Weather : ChatterEvent.Buffed;
        }

        /// <summary>What to call this effect in a line.</summary>
        /// <returns>Its localized name, or null when it has not got one.</returns>
        /// <param name="effect">The effect being added.</param>
        /// <remarks>
        /// Null rather than empty, because only null makes LineTokens refuse the line -
        /// an unnamed effect would otherwise render "Ah. . Wonderful."
        /// </remarks>
        private static string Named(StatusEffect effect)
        {
            string name = Localization.instance.Localize(effect.m_name);

            return string.IsNullOrEmpty(name) ? null : name;
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

            ChatterEvent kind = Kind(statusEffect.name);

            // The effect's name hash is a kind of thing rather than a particular one,
            // so it is a safe subject: two skeletons shielded by the same cast produce
            // one remark between them.
            _ = Chatter.TrySpeak(
                speaker,
                kind,
                statusEffect.NameHash(),
                targetName: null,
                companion: null,
                details: new LineDetails(status: Named(statusEffect)));
        }
    }
}
