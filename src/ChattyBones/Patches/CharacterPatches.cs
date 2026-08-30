using System;
using ChattyBones.Logic;
using HarmonyLib;

namespace ChattyBones.Patches
{
    /// <summary>Reacts to somebody taking a hit - a skeleton, you, or something you hit.</summary>
    /// <remarks>
    /// Everything about damage is decided here, from one number: the health actually
    /// lost. A prefix takes a reading, the postfix takes another, and the difference
    /// is the only thing either of them trusts.
    ///
    /// That is worth the two patches. RPC_Damage has six ways to return without
    /// applying anything - not the owner, already dead, dodged, teleporting, in a
    /// cutscene, PVP off - and a postfix runs whichever way it left, so reading the
    /// hit produced lines about blows that were cleanly dodge-rolled. Subtracting
    /// covers every exit without our keeping a copy of the list.
    ///
    /// It is also the only honest number. Between the attacker calling Damage and the
    /// health being written, RPC_Damage applies a stagger crit, a backstab bonus,
    /// resistances, armor and the difficulty scaling - so the value on the HitData is
    /// not what lands. Reading it early meant a blow that tested as non-fatal killed
    /// anyway, and the squad admired the swing instead of the kill.
    ///
    /// The cost is that this only fires where we own the victim, which for your own
    /// skeletons and yourself is always. Somebody else's greydwarf resolves its damage
    /// on their machine and we hear nothing - the same limit PlayerGotAKill has, and
    /// worth having them agree rather than each covering a different half.
    /// </remarks>
    [HarmonyPatch(typeof(Character), "RPC_Damage")]
    internal static class CharacterDamagedPatch
    {
        /// <summary>Read the health, and the damage, before vanilla gets at either.</summary>
        /// <param name="__instance">Whoever is being hit.</param>
        /// <param name="hit">The blow, while its numbers are still all there.</param>
        /// <param name="__state">Handed to the postfix by Harmony. An IL local, so it is per call and nesting is safe.</param>
        /// <remarks>
        /// The damage has to be copied here rather than read in the postfix.
        /// RPC_Damage lifts the fire, poison and spirit numbers off the hit, zeroes
        /// those three fields, applies the rest, and hands the originals to
        /// AddFireDamage and friends - Character.cs:1985. It never puts them back, and
        /// HitData is a class, so by the time a postfix looks, a surtling fireball is
        /// a hit with no damage on it at all. Frost and lightning survive, being
        /// consumed after rather than before.
        ///
        /// A struct copy and a float read, and deliberately nothing else. A prefix
        /// that throws does not merely lose a line: Harmony does not wrap a patch
        /// body, so the exception escapes RPC_Damage and the blow is never applied at
        /// all - a chat mod quietly making a creature invulnerable. Everything that
        /// can throw, cost anything, or wants the Enabled switch checked first waits
        /// for the postfix.
        ///
        /// These numbers are the blow as it was thrown, before the victim's
        /// resistances and armour are applied to it. That is the right sense for
        /// {damage}: a fire sword is a fire sword whoever it lands on. The share of
        /// health that decides whether anybody speaks is measured from health, not
        /// from here.
        /// </remarks>
        private static void Prefix(Character __instance, HitData hit, out Reading __state)
        {
            __state = new Reading(
                __instance == null ? 0f : __instance.GetHealth(),
                hit == null ? default : hit.m_damage);
        }

        /// <summary>What the prefix saw, for the postfix to compare against.</summary>
        /// <param name="health">Health before the blow landed.</param>
        /// <param name="damage">The blow's numbers, before vanilla consumed three of them.</param>
        internal readonly struct Reading(float health, HitData.DamageTypes damage)
        {
            /// <summary>Health before the blow landed.</summary>
            internal float Health { get; } = health;

            /// <summary>What the blow was made of.</summary>
            internal HitData.DamageTypes Damage { get; } = damage;
        }

        /// <summary>
        /// Catch everything. Harmony does not wrap a patch body, so anything escaping
        /// here lands in the middle of vanilla's damage handling.
        /// </summary>
        /// <param name="__instance">Whoever was hit.</param>
        /// <param name="hit">The blow, for working out who threw it.</param>
        /// <param name="__state">What the prefix read.</param>
        private static void Postfix(Character __instance, HitData hit, Reading __state)
        {
            try
            {
                React(__instance, hit, __state);
            }
            catch (Exception e)
            {
                ChattyBonesPlugin.Log.LogWarning("ChattyBones stumbled over a hit: " + e);
            }

            try
            {
                // Last, and that is the point. The block and stagger hooks run early
                // inside RPC_Damage, so anything they said where they were detected
                // would beat the events they are ranked below - see Blow.
                Blow.Flush(__instance);
            }
            catch (Exception e)
            {
                ChattyBonesPlugin.Log.LogWarning("ChattyBones stumbled over a blow: " + e);
            }
        }

        /// <summary>Work out whether this hit is worth saying anything about.</summary>
        /// <param name="victim">Whoever was hit.</param>
        /// <param name="hit">The blow.</param>
        /// <param name="seen">What the prefix read.</param>
        private static void React(Character victim, HitData hit, Reading seen)
        {
            if (!ModConfig.Enabled.Value || victim == null)
            {
                return;
            }

            float max = victim.GetMaxHealth();
            float lost = seen.Health - victim.GetHealth();

            if (max <= 0f || lost <= 0f)
            {
                return;
            }

            // Health rather than IsDead(): Character.IsDead() returns a flat false and
            // only Player overrides it, so asking a skeleton whether it is dead always
            // says no. CheckDeath does not run until the next fixed update, so at this
            // point a killing blow shows up only as the health being gone.
            bool fatal = victim.GetHealth() <= 0f;

            if (victim.IsPlayer())
            {
                if (victim == Player.m_localPlayer && lost / max >= ModConfig.HurtFraction.Value)
                {
                    _ = Chatter.SpeakAny(
                        ChatterEvent.PlayerHurt,
                        subject: 0,
                        targetName: null,
                        companion: null,
                        details: Hits.Of(hit, seen.Damage));
                }

                return;
            }

            ChatterComponent ours = victim.GetComponent<ChatterComponent>();
            if (ours != null)
            {
                TheySufferedIt(victim, ours, Hits.Of(hit, seen.Damage), lost / max, fatal);
                return;
            }

            WeDealtIt(victim, hit, seen.Damage, lost / max, fatal);
        }

        /// <summary>One of ours was hurt.</summary>
        /// <param name="victim">The skeleton.</param>
        /// <param name="ours">Its chatter component.</param>
        /// <param name="details">What the blow was made of.</param>
        /// <param name="share">How much of its health went, as a fraction.</param>
        /// <param name="fatal">Whether that was the last of it.</param>
        private static void TheySufferedIt(Character victim, ChatterComponent ours, LineDetails details, float share, bool fatal)
        {
            // A fatal blow gets last words instead of a complaint about the ribs.
            if (fatal || share < ModConfig.HurtFraction.Value)
            {
                return;
            }

            if (Chatter.TrySpeak(ours, ChatterEvent.Hurt, subject: 0, targetName: null, companion: null, details: details))
            {
                return;
            }

            // It could not speak for itself - usually its own cooldown - so somebody
            // else gets to notice. The subject is the skeleton *prefab* rather than
            // this particular skeleton, which is what keeps the budget's subject map
            // bounded and, handily, also stops five of them piling on at once.
            _ = Chatter.SpeakAny(
                ChatterEvent.CompanionHurt,
                Summons.PrefabOf(victim),
                targetName: null,
                companion: victim,
                details: details);
        }

        /// <summary>Something that is not one of ours was hurt - possibly by you.</summary>
        /// <param name="victim">Whatever took the hit.</param>
        /// <param name="hit">The blow, for the attacker.</param>
        /// <param name="damage">Its numbers, as the prefix copied them.</param>
        /// <param name="share">How much of its health went, as a fraction.</param>
        /// <param name="fatal">Whether that was the last of it.</param>
        /// <remarks>
        /// The attacker lookup resolves a ZDOID, so it goes last of the cheap tests -
        /// and describing the blow, which resolves it again, goes after that.
        /// </remarks>
        private static void WeDealtIt(Character victim, HitData hit, HitData.DamageTypes damage, float share, bool fatal)
        {
            // A kill is PlayerGotAKill's to talk about, and it has better lines for it.
            if (fatal || hit == null || share < ModConfig.BigHitFraction.Value)
            {
                return;
            }

            if (Player.m_localPlayer == null || !hit.HaveAttacker() || hit.GetAttacker() != Player.m_localPlayer)
            {
                return;
            }

            // Held rather than said, for the same reason the combat hooks are. This
            // runs before Blow.Flush, and anything that speaks first takes the moment
            // whatever its rank - so saying it here would put a big hit at 40 ahead of
            // a stagger at 42, inverting the table that ranks them. Routed through Blow
            // the two are compared properly.
            Blow.Note(
                victim,
                ChatterEvent.PlayerLandedABigHit,
                Summons.PrefabOf(victim),
                Summons.CreatureName(victim),
                Hits.Of(hit, damage));
        }
    }

    /// <summary>Last words, and congratulations.</summary>
    /// <remarks>
    /// A prefix, and it has to be. The last thing OnDeath does is
    /// <c>ZNetScene.instance.Destroy(gameObject)</c>, which calls ResetZDO and leaves
    /// the ZNetView with no ZDO at all - so from a postfix a dying skeleton fails its
    /// own ownership check and cannot say a word. Running first also means the
    /// victim's prefab hash is still readable, which is what PlayerGotAKill needs for
    /// its subject.
    ///
    /// OnDeath is reached only from CheckDeath, which sits inside an owner check, so
    /// this fires on whoever owns the victim. Your skeletons are yours, so their
    /// deaths are always ours to narrate. The things you kill often are not, which is
    /// why a skeleton's own kills are polled in <see cref="ChatterComponent.Sweep"/>
    /// rather than hooked, and why PlayerGotAKill can miss a kill on somebody else's
    /// greydwarf. Note that a *player* dying never arrives here at all - Player
    /// overrides OnDeath and does not call base.
    /// </remarks>
    [HarmonyPatch(typeof(Character), "OnDeath")]
    internal static class CharacterDeathPatch
    {
        /// <summary>
        /// Catch everything. An exception escaping this one is the worst of the lot:
        /// OnDeath would never reach its own Destroy, so the creature is never
        /// removed - and CheckDeath re-tests its health every fixed update, so it
        /// would throw again forever.
        /// </summary>
        /// <param name="__instance">Whoever just died.</param>
        private static void Prefix(Character __instance)
        {
            try
            {
                React(__instance);
            }
            catch (Exception e)
            {
                ChattyBonesPlugin.Log.LogWarning("ChattyBones stumbled over a death: " + e);
            }
        }

        /// <summary>Decide who, if anyone, has something to say about this death.</summary>
        /// <param name="dead">Whoever just died.</param>
        private static void React(Character dead)
        {
            if (!ModConfig.Enabled.Value || dead == null)
            {
                return;
            }

            ChatterComponent ours = dead.GetComponent<ChatterComponent>();
            if (ours != null)
            {
                Mourn(dead, ours);
                return;
            }

            HitData last = dead.m_lastHit;
            if (last == null || Player.m_localPlayer == null || last.GetAttacker() != Player.m_localPlayer)
            {
                return;
            }

            _ = Chatter.SpeakAny(
                ChatterEvent.PlayerGotAKill,
                Summons.PrefabOf(dead),
                Summons.CreatureName(dead),
                companion: null,
                details: Hits.WieldedBy(Player.m_localPlayer));
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
            // The killer's hands, not fallen.m_lastHit: by now RPC_Damage has stripped
            // the fire, poison and spirit off that hit - see the Prefix remarks above.
            if (!Chatter.TrySpeak(
                speaker,
                ChatterEvent.Died,
                subject: 0,
                targetName: null,
                companion: null,
                details: Hits.WieldedBy(fallen.m_lastHit?.GetAttacker())))
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
