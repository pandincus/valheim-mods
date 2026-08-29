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
    /// We measure the health actually lost rather than reading the damage off the
    /// hit, and that is the whole reason there is a prefix here. A postfix runs
    /// whichever way the method returned, and RPC_Damage has half a dozen ways out -
    /// not the owner, already dead, dodged, teleporting, PVP off. Trusting the hit
    /// meant a skeleton shouting "watch it!" at a blow you cleanly dodge-rolled.
    /// Comparing health before and against after covers every one of those exits
    /// without our having to keep a copy of the list, and it is the truer number
    /// anyway: RPC_Damage applies difficulty and backstab modifiers to the hit after
    /// we would have read it.
    /// </remarks>
    [HarmonyPatch(typeof(Character), "RPC_Damage")]
    internal static class CharacterDamagedPatch
    {
        /// <summary>Remember the health we are about to lose some of.</summary>
        /// <param name="__instance">Whoever is being hit.</param>
        /// <param name="__state">Handed to the postfix by Harmony. Per call, so nesting is safe.</param>
        private static void Prefix(Character __instance, out float __state)
        {
            __state = __instance == null ? 0f : __instance.GetHealth();
        }

        private static void Postfix(Character __instance, float __state)
        {
            if (!ModConfig.Enabled.Value || __instance == null)
            {
                return;
            }

            float max = __instance.GetMaxHealth();
            float lost = __state - __instance.GetHealth();

            if (max <= 0f || lost <= 0f || lost / max < ModConfig.HurtFraction.Value)
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
            // Health rather than IsDead(): Character.IsDead() returns a flat false and
            // only Player overrides it, so asking a skeleton whether it is dead always
            // says no. CheckDeath does not run until the next fixed update, so at this
            // point a killing blow shows up only as the health being gone.
            if (victim == null || __instance.GetHealth() <= 0f)
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
    /// A prefix, and it has to be. The last thing OnDeath does is
    /// <c>ZNetScene.instance.Destroy(gameObject)</c>, which calls ResetZDO and leaves
    /// the ZNetView with no ZDO at all - so from a postfix, a dying skeleton fails
    /// its own ownership check and cannot say a word. I wrote this as a postfix and
    /// left a comment claiming Died worked; it never once fired in game. Running
    /// first also means the victim's prefab hash is still readable, which is what
    /// PlayerGotAKill needs for its subject.
    ///
    /// OnDeath is reached from CheckDeath, which sits inside an owner check, so it
    /// fires on whoever owns the *victim*. That is your own machine for your
    /// skeletons; it is often somebody else's for the things you kill, which is why a
    /// skeleton's own kills are polled in <see cref="ChatterComponent.Sweep"/> rather
    /// than hooked here.
    /// </remarks>
    [HarmonyPatch(typeof(Character), "OnDeath")]
    internal static class CharacterDeathPatch
    {
        private static void Prefix(Character __instance)
        {
            if (!ModConfig.Enabled.Value || __instance == null)
            {
                return;
            }

            ChatterComponent ours = __instance.GetComponent<ChatterComponent>();
            if (ours != null)
            {
                Mourn(__instance, ours);
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

        /// <summary>Last words, and somebody noticing them.</summary>
        /// <param name="fallen">The skeleton that has just died.</param>
        /// <param name="speaker">Its own chatter component.</param>
        /// <remarks>
        /// The squad only answers a death cry that was actually said, which is what
        /// keeps a wipe to one exchange rather than four skeletons saying "oh no" over
        /// each other.
        ///
        /// Passing the fallen skeleton as well as its name is not redundant: the name
        /// fills the token, and the reference is how SpeakAny knows not to ask the
        /// dying skeleton to mourn itself. It would otherwise happily do so - Unity
        /// defers the destroy, so it is still in the registry for the rest of the frame.
        /// </remarks>
        private static void Mourn(Character fallen, ChatterComponent speaker)
        {
            if (!Chatter.TrySpeak(speaker, ChatterEvent.Died, subject: 0, targetName: null, companion: null))
            {
                return;
            }

            _ = Chatter.SpeakAny(
                ChatterEvent.CompanionDied,
                subject: 0,
                targetName: null,
                companion: fallen,
                companionName: Summons.NameOf(fallen));
        }
    }
}
