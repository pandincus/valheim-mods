using System;
using ChattyBones.Logic;
using HarmonyLib;
using UnityEngine;

namespace ChattyBones.Patches
{
    /// <summary>What a blow turned out to be worth remarking on, held until the blow is over.</summary>
    /// <remarks>
    /// The combat events all fire from inside <c>RPC_Damage</c>, and they fire early:
    /// the block and stagger hooks run before the status effect is applied, before the
    /// health is read back, and a fixed update before the victim is found to be dead.
    /// Speaking from where they are detected therefore beat every event they were
    /// ranked below - a stagger at 42 would take the moment and the kill at 80 would
    /// be refused 0.02s later, which is the phase 4 defect all over again.
    ///
    /// Ranking cannot fix that on its own. <see cref="ChatterBudget"/> only lets one
    /// event interrupt another after PreemptGapSeconds, a twentieth of which has
    /// elapsed by the time the next thing happens, so inside one blow it is first
    /// past the post rather than highest rank.
    ///
    /// So the hooks record here and <see cref="CharacterDamagedPatch"/> speaks at the
    /// end of the blow, by which point everything else has had its turn and the budget
    /// is comparing the texture of the fight against what actually happened in it.
    ///
    /// The record is keyed by victim, and that is load-bearing rather than tidy.
    /// A successful melee block ends with <c>attacker.Damage(hitData)</c> for the
    /// deflection push, and a routed RPC aimed at the local peer is dispatched inline
    /// - so in single player, where you own everything, every block runs a second
    /// RPC_Damage inside the first. Keyed by victim, the inner blow cannot consume the
    /// outer one's record, and there is no depth counter to leak if vanilla throws
    /// between a prefix and a postfix.
    /// </remarks>
    internal static class Blow
    {
        /// <summary>Who the pending remark is about, or null when there is not one.</summary>
        private static Character _victim;

        /// <summary>What happened to them.</summary>
        private static ChatterEvent _kind;

        /// <summary>A prefab hash for whatever it concerned, or 0.</summary>
        private static int _subject;

        /// <summary>That thing's name, already localized, or null.</summary>
        private static string _targetName;

        /// <summary>What is known about the blow itself, for the events that describe it.</summary>
        private static LineDetails _details;

        /// <summary>Which frame it was recorded in, so a stale record cannot speak later.</summary>
        private static int _frame;

        /// <summary>Whoever had their own stagger bar filled during the blow in progress.</summary>
        /// <remarks>
        /// The other half of vanilla's perfect-block gate. It computes
        /// <c>flag3 = HaveStamina() &amp;&amp; !staggeredBySelf</c>, and the second term is
        /// AddStaggerDamage's return from inside BlockAttack - which our stagger hook
        /// sees and the block hook, running afterwards, otherwise cannot. Without it a
        /// block vanilla refused outright still reads as a parry.
        /// </remarks>
        private static Character _selfStaggered;

        /// <summary>Note that this character's own bar filled while absorbing a blow.</summary>
        /// <param name="victim">Whoever was staggered.</param>
        internal static void NoteSelfStagger(Character victim)
        {
            _selfStaggered = victim;
            _frame = Time.frameCount;
        }

        /// <summary>Was this character staggered by the blow it is about to be asked about?</summary>
        /// <returns>True when its own bar filled during this blow.</returns>
        /// <param name="victim">Whoever might have been.</param>
        internal static bool WasSelfStaggered(Character victim)
        {
            return victim != null
                && ReferenceEquals(victim, _selfStaggered)
                && _frame == Time.frameCount;
        }

        /// <summary>Set aside something worth saying once this blow has finished landing.</summary>
        /// <param name="victim">Who it happened to.</param>
        /// <param name="kind">What happened.</param>
        /// <param name="subject">A prefab hash for what it concerned, or 0.</param>
        /// <param name="targetName">That thing's localized name, or null.</param>
        /// <param name="details">What is known about the blow, for the events that use it.</param>
        /// <remarks>
        /// Two texture events can land on one blow - a parry that also staggers you
        /// through the shield - so the higher rank wins rather than the later call.
        /// </remarks>
        internal static void Note(
            Character victim,
            ChatterEvent kind,
            int subject,
            string targetName,
            LineDetails details = default)
        {
            if (victim == null)
            {
                return;
            }

            if (ReferenceEquals(victim, _victim)
                && _frame == Time.frameCount
                && ChatterBudget.PriorityOf(kind) <= ChatterBudget.PriorityOf(_kind))
            {
                return;
            }

            _victim = victim;
            _kind = kind;
            _subject = subject;
            _targetName = targetName;
            _details = details;
            _frame = Time.frameCount;
        }

        /// <summary>Say anything left over from a frame that has already finished.</summary>
        /// <remarks>
        /// Most records are consumed by the RPC_Damage postfix that ends the blow they
        /// belong to. Not all of them: a skill can go up during a blow *or* while you
        /// are chopping a tree, and the hook cannot tell which. So it always records,
        /// and anything still sitting here a frame later had no blow to end it and is
        /// said now.
        ///
        /// A tick late is imperceptible for the events that reach this - nobody can
        /// tell a level-up remark arrived a sixtieth of a second after the level.
        /// </remarks>
        internal static void FlushStale()
        {
            if (_victim == null || _frame == Time.frameCount)
            {
                return;
            }

            Flush(_victim);
        }

        /// <summary>Say whatever was set aside for this victim, if it is still worth saying.</summary>
        /// <param name="victim">Whoever the finished blow landed on.</param>
        /// <remarks>
        /// A death takes precedence and is not ours to announce: the kill and death
        /// events have better lines and fire from OnDeath a moment later, so a blow
        /// that finished somebody off leaves without a word. That is the rule
        /// PlayerLandedABigHit already follows.
        ///
        /// The frame check bounds how stale a record can be. If vanilla throws between
        /// a prefix and a postfix the pending remark is simply dropped, rather than
        /// being attached to whatever gets hit next.
        /// </remarks>
        internal static void Flush(Character victim)
        {
            if (victim == null || !ReferenceEquals(victim, _victim))
            {
                return;
            }

            ChatterEvent kind = _kind;
            int subject = _subject;
            string targetName = _targetName;
            LineDetails details = _details;

            // One frame of grace rather than none. The blow that recorded this ends in
            // the same frame, and FlushStale deliberately picks records up in the next
            // one - so anything older than that was orphaned by an exception and is
            // dropped rather than attached to whatever is happening now.
            bool fresh = Time.frameCount - _frame <= 1;

            _victim = null;
            _targetName = null;
            _details = default;
            _selfStaggered = null;

            if (fresh && victim.GetHealth() > 0f)
            {
                _ = Chatter.SpeakAny(kind, subject, targetName, companion: null, details: details);
            }
        }
    }

    /// <summary>Reacts to a parry - a block timed well enough that the game pays double for it.</summary>
    /// <remarks>
    /// Only the parry, because a skeleton that praises every raised shield is one you
    /// stop listening to. Vanilla already draws the line: <c>m_timedBlockBonus &gt; 1f</c>
    /// with a block raised inside a quarter of a second is the same test that decides
    /// whether Blocking earns 2f or 1f, further down the same method we patch.
    /// Borrowing it means the mod agrees with the game about what counted.
    ///
    /// It cannot be folded into <see cref="CharacterStaggerPatch"/>, which was the
    /// first thing I tried, and the reason is stronger than it first looked. A perfect
    /// block calls <c>attacker.Stagger(-hit.m_dir)</c> directly, which bypasses
    /// AddStaggerDamage entirely - so the stagger hook never sees a parry at all. On
    /// top of that it only happens when <c>attacker.m_staggerWhenBlocked</c> is set,
    /// and Stagger takes a direction with no attacker on it, so there would be nobody
    /// to credit even if it did.
    ///
    /// BlockAttack is reached only from RPC_Damage, which returns early for a
    /// non-owner, so this runs on whoever owns the blocker. Your own parries are
    /// always your machine's.
    /// </remarks>
    [HarmonyPatch(typeof(Humanoid), "BlockAttack")]
    internal static class HumanoidBlockPatch
    {
        /// <summary>
        /// Catch everything. Harmony does not wrap a patch body, so anything escaping
        /// here lands in the middle of vanilla's damage handling.
        /// </summary>
        /// <param name="__instance">Whoever raised the shield.</param>
        /// <param name="hit">The blow being turned.</param>
        /// <param name="__result">False when the block did not happen at all.</param>
        /// <param name="___m_leftItem">What is being blocked with, when it is not the weapon.</param>
        /// <param name="___m_blockTimer">How long the block has been held.</param>
        /// <remarks>
        /// GetCurrentBlocker is private and is simply the off-hand item or the current
        /// weapon, so reproducing it costs less than reflecting for it.
        ///
        /// BlockAttack never writes m_blockTimer - only UpdateBlock does - so reading
        /// it after the fact is reading what the method itself read. Its initial value
        /// is 9999f rather than the -1f UpdateBlock later writes for "not blocking",
        /// which changes nothing here: both fail the quarter-second test.
        /// </remarks>
        private static void Postfix(
            Humanoid __instance,
            HitData hit,
            bool __result,
            ItemDrop.ItemData ___m_leftItem,
            float ___m_blockTimer)
        {
            try
            {
                React(__instance, hit, __result, ___m_leftItem, ___m_blockTimer);
            }
            catch (Exception e)
            {
                ChattyBonesPlugin.Log.LogWarning("ChattyBones stumbled over a block: " + e);
            }
        }

        /// <summary>Decide whether that block was worth remarking on.</summary>
        /// <param name="blocker">Whoever raised the shield.</param>
        /// <param name="hit">The blow being turned.</param>
        /// <param name="blocked">Whether a block happened at all.</param>
        /// <param name="leftItem">The off-hand item, or null when blocking with the weapon.</param>
        /// <param name="blockTimer">How long the block has been held.</param>
        /// <remarks>
        /// Both halves of a local vanilla computes and we cannot see:
        /// <c>flag3 = HaveStamina() &amp;&amp; !staggeredBySelf</c>, which gates the
        /// perfect-block flash, the adrenaline and the counter-stagger. The second half
        /// comes from <see cref="Blow.WasSelfStaggered"/>, because our stagger hook runs
        /// inside BlockAttack and sees the value this postfix cannot.
        ///
        /// The stamina half is close rather than exact, and only ever errs quiet. A
        /// perfect block drains stamina a second time before we get here, so parrying
        /// on the last sliver of it can read as empty and go uncongratulated. Costing a
        /// line is the acceptable direction for that to be wrong in.
        ///
        /// An attacker is required because vanilla requires one - the flash, the
        /// adrenaline and the counter-stagger are all inside <c>if ((bool)attacker)</c>,
        /// so a well-timed block of a trap rewards nothing and is not a parry.
        /// </remarks>
        private static void React(
            Humanoid blocker,
            HitData hit,
            bool blocked,
            ItemDrop.ItemData leftItem,
            float blockTimer)
        {
            if (!ModConfig.Enabled.Value || !blocked || blocker == null)
            {
                return;
            }

            if (Player.m_localPlayer == null || blocker != Player.m_localPlayer)
            {
                return;
            }

            if (hit == null || !hit.HaveAttacker() || !blocker.HaveStamina())
            {
                return;
            }

            if (Blow.WasSelfStaggered(blocker))
            {
                return;
            }

            if (!WasTimed(blocker, leftItem, blockTimer))
            {
                return;
            }

            Character attacker = hit.GetAttacker();

            Blow.Note(
                blocker,
                ChatterEvent.PlayerParried,
                Summons.PrefabOf(attacker),
                Summons.CreatureName(attacker));
        }

        /// <summary>Was that a parry rather than an ordinary block?</summary>
        /// <returns>True when the blocker can parry at all and was raised in time.</returns>
        /// <param name="blocker">Whoever raised it.</param>
        /// <param name="leftItem">The off-hand item, or null when blocking with the weapon.</param>
        /// <param name="blockTimer">How long the block has been held.</param>
        /// <remarks>
        /// The bonus check is not redundant with the timing one. Vanilla ANDs the two,
        /// so an item whose m_timedBlockBonus is exactly 1 cannot parry however well
        /// it is timed - and without this, every such block inside the window would be
        /// congratulated as a parry. Which items those are is authored in the prefabs
        /// rather than in the assembly, so this reproduces vanilla's rule rather than
        /// naming any particular shield.
        /// </remarks>
        private static bool WasTimed(Humanoid blocker, ItemDrop.ItemData leftItem, float blockTimer)
        {
            ItemDrop.ItemData used = leftItem ?? blocker.GetCurrentWeapon();

            return used?.m_shared != null
                && used.m_shared.m_timedBlockBonus > 1f
                && blockTimer != -1f
                && blockTimer < 0.25f;
        }
    }

    /// <summary>Reacts to somebody being knocked off balance, in either direction.</summary>
    /// <remarks>
    /// One hook, two events, and the only place with the attacker in hand - vanilla's
    /// own <c>Stagger(Vector3)</c> takes a direction and nothing more, while this
    /// method already does the <c>hit.GetAttacker()</c> lookup for its own purposes.
    ///
    /// What the return value means took a review to get right, and the comment used to
    /// say the opposite. It is not "this blow tipped the bar over" - it is "the bar is
    /// at or over full". Vanilla clamps m_staggerDamage to the threshold rather than
    /// resetting it, and UpdateStagger bleeds it off over a full five seconds, so every
    /// blow inside that window re-satisfies the test and returns true again. The
    /// animation looks right because RPC_Stagger is separately guarded on
    /// <c>!IsStaggering()</c>; the return value is not.
    ///
    /// So the edge has to be found rather than taken on trust: the bar has to have
    /// been below the threshold before this blow and at it afterwards. That covers
    /// three cases - repeat blows on a target already staggered, the same blocked blow
    /// arriving twice (BlockAttack staggers the blocker, then lets the full damage
    /// through to ApplyDamage, which staggers it again), and a burning tick, which adds
    /// no stagger damage at all and would otherwise report a stagger with nobody to
    /// name once per damage interval.
    ///
    /// Testing for an increase rather than a crossing is not enough, which a review
    /// caught: UpdateStagger bleeds the bar off at a fifth of the threshold per second,
    /// so one fixed step after a stagger it sits a fraction below full and the next
    /// blow pushes it back up. That is an increase and it is not a new stagger.
    ///
    /// Reached from ApplyDamage rather than RPC_Damage directly, which matters: the
    /// status-effect ticks reach ApplyDamage too. They all run under an owner check, so
    /// this still only fires on whoever owns the victim - the same limit
    /// PlayerGotAKill has, and worth having the two agree.
    /// </remarks>
    [HarmonyPatch(typeof(Character), "AddStaggerDamage")]
    internal static class CharacterStaggerPatch
    {
        /// <summary>Read the stagger bar before vanilla adds to it.</summary>
        /// <param name="___m_staggerDamage">How full the bar is.</param>
        /// <param name="__state">Handed to the postfix by Harmony. An IL local, so it is per call.</param>
        /// <remarks>
        /// A single float read, and deliberately nothing else. A prefix that throws
        /// does not merely lose a line - Harmony does not wrap a patch body, so it
        /// escapes into vanilla's damage handling and the blow is never applied.
        /// </remarks>
        private static void Prefix(float ___m_staggerDamage, out float __state)
        {
            __state = ___m_staggerDamage;
        }

        /// <summary>
        /// Catch everything. Harmony does not wrap a patch body, so anything escaping
        /// here lands in the middle of vanilla's damage handling.
        /// </summary>
        /// <param name="__instance">Whoever took the stagger damage.</param>
        /// <param name="hit">The blow, or null when it came from absorbing a block.</param>
        /// <param name="__result">Whether the bar is at or over full.</param>
        /// <param name="__state">How full it was before.</param>
        /// <remarks>
        /// The threshold is GetMaxHealth() times m_staggerDamageFactor, which is what
        /// vanilla's private GetStaggerTreshold returns - both are public, so there is
        /// nothing to reflect for.
        /// </remarks>
        private static void Postfix(
            Character __instance,
            HitData hit,
            bool __result,
            float __state)
        {
            try
            {
                bool crossed = __result
                    && __instance != null
                    && __state < __instance.GetMaxHealth() * __instance.m_staggerDamageFactor;

                React(__instance, hit, crossed);
            }
            catch (Exception e)
            {
                ChattyBonesPlugin.Log.LogWarning("ChattyBones stumbled over a stagger: " + e);
            }
        }

        /// <summary>Work out who was knocked about, and by whom.</summary>
        /// <param name="victim">Whoever lost their footing.</param>
        /// <param name="hit">The blow, or null.</param>
        /// <param name="staggered">Whether this blow is what actually tipped the bar over.</param>
        /// <remarks>
        /// A null hit is not a defect to guard against - it is how vanilla calls this
        /// from inside BlockAttack, to charge the blocker for absorbing a blow. So a
        /// stagger with no hit on it means you were staggered through your own shield,
        /// which is worth a line and simply has nobody to name.
        /// </remarks>
        private static void React(Character victim, HitData hit, bool staggered)
        {
            if (!ModConfig.Enabled.Value || !staggered || victim == null || Player.m_localPlayer == null)
            {
                return;
            }

            Character attacker = hit != null && hit.HaveAttacker() ? hit.GetAttacker() : null;

            // A null hit means BlockAttack charging the blocker for absorbing a blow,
            // which is the term vanilla folds into its perfect-block gate.
            if (hit == null)
            {
                Blow.NoteSelfStagger(victim);
            }

            if (victim == Player.m_localPlayer)
            {
                Blow.Note(
                    victim,
                    ChatterEvent.PlayerStaggered,
                    Summons.PrefabOf(attacker),
                    Summons.CreatureName(attacker));

                return;
            }

            // Not "did a player do this" - specifically you. Somebody else's blow
            // staggering a troll is their squad's line to say, not ours.
            if (attacker == Player.m_localPlayer)
            {
                Blow.Note(
                    victim,
                    ChatterEvent.StaggeredIt,
                    Summons.PrefabOf(victim),
                    Summons.CreatureName(victim));
            }
        }
    }

    /// <summary>Reacts to a dodge that actually turned a blow.</summary>
    /// <remarks>
    /// This is the game's own perfect dodge rather than our reading of one.
    /// RPC_HitWhileDodging is what spawns the effect, refunds the stamina, adds the
    /// adrenaline and raises the Dodge skill - so hooking it congratulates you at
    /// exactly the moment the game rewards you, and never for a roll into open air.
    ///
    /// Vanilla guards the body with <c>m_beenHitWhileDodging</c>, so it counts once
    /// per roll however many blows land inside the window - which is why the prefix
    /// reads that flag rather than the postfix simply firing. It is reached from both
    /// Attack and Projectile, so an arrow dodged counts the same as an axe.
    ///
    /// This one speaks where it is detected rather than going through
    /// <see cref="Blow"/>, and can: a dodged hit never enters RPC_Damage at all -
    /// Attack and Projectile call HitWhileDodging and then skip the Damage call
    /// entirely - so there is no later event in the same blow for it to talk over.
    /// </remarks>
    [HarmonyPatch(typeof(Player), "RPC_HitWhileDodging")]
    internal static class PlayerDodgePatch
    {
        /// <summary>Note whether this call is the one that will count.</summary>
        /// <param name="___m_beenHitWhileDodging">Vanilla's own once-per-roll guard, before it is set.</param>
        /// <param name="__state">Handed to the postfix by Harmony. An IL local, so it is per call.</param>
        /// <remarks>
        /// A single bool read, and deliberately nothing else - a prefix that throws
        /// escapes into vanilla's own handler and the dodge silently stops paying out.
        ///
        /// It has to be read here because the postfix would always see true: the method
        /// sets the flag on the way through, so by then a four-hit swing rolled through
        /// looks like four separate dodges.
        /// </remarks>
        private static void Prefix(bool ___m_beenHitWhileDodging, out bool __state)
        {
            __state = ___m_beenHitWhileDodging;
        }

        /// <summary>
        /// Catch everything. Harmony does not wrap a patch body, so anything escaping
        /// here lands in the middle of vanilla's dodge handling.
        /// </summary>
        /// <param name="__instance">Whoever rolled.</param>
        /// <param name="__state">Whether the roll had already been counted before this call.</param>
        private static void Postfix(Player __instance, bool __state)
        {
            try
            {
                React(__instance, __state);
            }
            catch (Exception e)
            {
                ChattyBonesPlugin.Log.LogWarning("ChattyBones stumbled over a dodge: " + e);
            }
        }

        /// <summary>Decide whether to say anything about that roll.</summary>
        /// <param name="dodger">Whoever rolled.</param>
        /// <param name="alreadyCounted">Whether an earlier blow in the same roll had already counted.</param>
        private static void React(Player dodger, bool alreadyCounted)
        {
            if (!ModConfig.Enabled.Value || alreadyCounted || dodger == null)
            {
                return;
            }

            if (Player.m_localPlayer == null || dodger != Player.m_localPlayer)
            {
                return;
            }

            // Nothing to name. RPC_HitWhileDodging is told the sender and no more, so
            // whatever it was you rolled away from does not reach us.
            _ = Chatter.SpeakAny(
                ChatterEvent.PlayerDodged,
                subject: 0,
                targetName: null,
                companion: null);
        }
    }
}
