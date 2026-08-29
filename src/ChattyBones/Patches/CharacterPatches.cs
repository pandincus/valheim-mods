using ChattyBones.Logic;
using HarmonyLib;

namespace ChattyBones.Patches
{
    /// <summary>Reacts to somebody taking a hit - a skeleton, or you.</summary>
    /// <remarks>
    /// RPC_Damage rather than Damage, because this is where the damage actually lands.
    /// It runs on the victim's owner, which for your skeletons and for you is your own
    /// machine, so no networking is involved in noticing any of it.
    ///
    /// A postfix runs whether or not the method returned early, and RPC_Damage has
    /// half a dozen ways out - not the owner, already dead, dodged, PVP off. So the
    /// checks below are not belt and braces; without them a skeleton comments on
    /// damage that was never applied.
    /// </remarks>
    [HarmonyPatch(typeof(Character), "RPC_Damage")]
    internal static class CharacterDamagedPatch
    {
        private static void Postfix(Character __instance, HitData hit)
        {
            if (!ModConfig.Enabled.Value || __instance == null || hit == null)
            {
                return;
            }

            float max = __instance.GetMaxHealth();
            if (max <= 0f || hit.GetTotalDamage() / max < ModConfig.HurtFraction.Value)
            {
                return;
            }

            if (__instance.IsPlayer())
            {
                if (__instance == Player.m_localPlayer)
                {
                    _ = Chatter.SpeakAny(ChatterEvent.PlayerHurt, subject: 0, targetName: null, companion: null);
                }

                return;
            }

            ChatterComponent victim = __instance.GetComponent<ChatterComponent>();

            // A fatal blow gets last words instead of a complaint about the ribs.
            if (victim == null || __instance.IsDead())
            {
                return;
            }

            if (Chatter.TrySpeak(victim, ChatterEvent.Hurt, subject: 0, targetName: null, companion: null))
            {
                return;
            }

            // It could not speak for itself - usually its own cooldown - so somebody
            // else gets to notice. The subject is the skeleton *prefab* rather than
            // this particular skeleton, which is what keeps the budget's subject map
            // bounded and, handily, also stops five of them piling on at once.
            _ = Chatter.SpeakAny(
                ChatterEvent.CompanionHurt,
                Summons.PrefabOf(__instance),
                targetName: null,
                companion: __instance);
        }
    }

    /// <summary>Reacts to you hitting something very hard.</summary>
    /// <remarks>
    /// Damage rather than RPC_Damage, and a prefix rather than a postfix. The
    /// attacking client is the one that builds the HitData and calls Damage, which is
    /// what then sends the RPC - so this runs on your machine with the number already
    /// in hand, whoever owns the thing you hit.
    ///
    /// The checks are ordered by what they cost. This runs on every blow anyone
    /// lands, and hit.GetAttacker() resolves a ZDOID, so it goes last.
    /// </remarks>
    [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
    internal static class CharacterDamagePatch
    {
        private static void Prefix(Character __instance, HitData hit)
        {
            if (!ModConfig.Enabled.Value
                || __instance == null
                || hit == null
                || Player.m_localPlayer == null
                || __instance.IsPlayer())
            {
                return;
            }

            float damage = hit.GetTotalDamage();
            float max = __instance.GetMaxHealth();

            if (max <= 0f || damage / max < ModConfig.BigHitFraction.Value)
            {
                return;
            }

            // A blow that finishes the job is a kill, and PlayerGotAKill has its own
            // lines. Without this the two fire together and you get both at once.
            if (damage >= __instance.GetHealth())
            {
                return;
            }

            if (!hit.HaveAttacker() || hit.GetAttacker() != Player.m_localPlayer)
            {
                return;
            }

            _ = Chatter.SpeakAny(
                ChatterEvent.PlayerLandedABigHit,
                Summons.PrefabOf(__instance),
                Summons.CreatureName(__instance),
                companion: null);
        }
    }

    /// <summary>Last words, and congratulations.</summary>
    /// <remarks>
    /// OnDeath is protected virtual on Character and nothing between here and a
    /// skeleton overrides it - Humanoid does not - so patching the base reaches them.
    ///
    /// Note this is only half of the death story. OnDeath is reached from CheckDeath,
    /// which sits inside an owner check, so it fires on whoever owns the *victim*.
    /// That is your own machine for your skeletons, which is why Died works here; it
    /// is often somebody else's for the things you kill, which is why a skeleton's own
    /// kills are polled in <see cref="ChatterComponent.Sweep"/> rather than hooked.
    /// </remarks>
    [HarmonyPatch(typeof(Character), "OnDeath")]
    internal static class CharacterDeathPatch
    {
        private static void Postfix(Character __instance)
        {
            if (!ModConfig.Enabled.Value || __instance == null)
            {
                return;
            }

            ChatterComponent ours = __instance.GetComponent<ChatterComponent>();
            if (ours != null)
            {
                _ = Chatter.TrySpeak(ours, ChatterEvent.Died, subject: 0, targetName: null, companion: null);
                return;
            }

            if (__instance.IsPlayer())
            {
                return;
            }

            HitData last = __instance.m_lastHit;
            if (last == null || Player.m_localPlayer == null || last.GetAttacker() != Player.m_localPlayer)
            {
                return;
            }

            _ = Chatter.SpeakAny(
                ChatterEvent.PlayerGotAKill,
                Summons.PrefabOf(__instance),
                Summons.CreatureName(__instance),
                companion: null);
        }
    }
}
