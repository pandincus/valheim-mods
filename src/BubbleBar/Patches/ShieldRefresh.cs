using HarmonyLib;

namespace BubbleBar.Patches
{
    /// <summary>
    /// Refill the magic barrier when it is recast, instead of only resetting its timer.
    /// </summary>
    /// <remarks>
    /// The vanilla behaviour, in <c>Character.Damage</c> - the Staff of Protection
    /// applies the barrier by hitting you with its own AoE, so a recast arrives as a
    /// hit carrying the same status effect:
    ///
    /// <code>
    /// StatusEffect statusEffect = m_seman.GetStatusEffect(hit.m_statusEffectHash);
    /// if (statusEffect == null) { statusEffect = m_seman.AddStatusEffect(...); }
    /// else { statusEffect.ResetTime(); statusEffect.SetLevel(...); }
    /// </code>
    ///
    /// <c>ResetTime</c> zeroes the clock and nothing else. <c>SE_Shield.SetLevel</c>
    /// recomputes the *capacity* - <c>m_totalAbsorbDamage</c> - but never touches
    /// <c>m_damage</c>, the running total of what the barrier has soaked, which is what
    /// <c>IsDone</c> compares against to decide it has broken. So a barrier of 200 that
    /// has already taken 190 comes back with a fresh full duration and 10 points of
    /// actual protection, and pops on the next hit that lands.
    ///
    /// I hooked SetLevel rather than the refresh branch itself for two reasons. It is
    /// the one place all three refresh paths meet - the branch above, and both of
    /// SEMan's <c>resetTime: true</c> paths - so one postfix covers the lot. And it is
    /// a two-parameter virtual on a leaf class, which is about as stable a target as
    /// this game offers: Valheim 1.0 appended a <c>short variant</c> to
    /// <c>SEMan.AddStatusEffect</c> and broke every patch that had pinned its
    /// signature, and that is a method we would otherwise have wanted.
    /// </remarks>
    [HarmonyPatch(typeof(SE_Shield), nameof(SE_Shield.SetLevel))]
    internal static class ShieldRefresh
    {
        /// <summary>Clear the damage the barrier has soaked, so it comes back whole.</summary>
        /// <param name="__instance">The barrier being applied or refreshed.</param>
        /// <remarks>
        /// This also runs on the six calls that are not refreshes, and all of them are
        /// harmless. A brand new barrier is a fresh <c>Clone()</c> whose <c>m_damage</c> is
        /// already zero. The other five are <c>ItemDrop</c> building item tooltips, which call
        /// SetLevel on the shared ScriptableObject - and that asset is never the thing taking
        /// hits, because <c>SEMan</c> only ever adds a <c>Clone()</c> to the list
        /// <c>OnDamaged</c> walks, so its <c>m_damage</c> is permanently zero.
        /// </remarks>
        private static void Postfix(SE_Shield __instance)
        {
            if (!ModConfig.Enabled.Value || !ModConfig.RestoreOnRefresh.Value)
            {
                return;
            }

            float soaked = __instance.m_damage;

            __instance.m_damage = 0f;

            // Only a call that actually restored something is worth a line. The two no-op
            // callers above would otherwise write one every time an item tooltip is drawn,
            // which is often enough to bury the refreshes we are trying to watch.
            if (!ModConfig.LogBarrierEvents.Value || soaked <= 0f)
            {
                return;
            }

            Character who = __instance.m_character;

            BubbleBarPlugin.Log.LogInfo(
                "Barrier refreshed on " + (who == null ? "(nobody)" : who.m_name)
                + ": had soaked " + soaked.ToString("0") + " of "
                + __instance.m_totalAbsorbDamage.ToString("0") + ", now back to full.");
        }
    }
}
