using System.Collections.Generic;
using BubbleBar.Logic;
using UnityEngine;

namespace BubbleBar
{
    /// <summary>Console commands for seeing what the bars are reading.</summary>
    /// <remarks>
    /// Registering from Awake is safe: Terminal.commands is a static dictionary created once
    /// and never cleared, and InitTerminal guards itself with a flag.
    ///
    /// These are for us rather than for players, so they are marked secret and stay out of the
    /// tab-completion list.
    /// </remarks>
    internal static class DebugCommands
    {
        /// <summary>How far out to report on, in metres.</summary>
        private const float SearchRadius = 60f;

        /// <summary>Register the commands. Called once from Awake.</summary>
        internal static void Register()
        {
            _ = new Terminal.ConsoleCommand(
                "bb_shields",
                "every character nearby with a magic barrier, and where the reading came from",
                Report,
                isCheat: false,
                isNetwork: false,
                onlyServer: false,
                isSecret: true);
        }

        /// <summary>
        /// Print who has a barrier, what it reads, and why anything unreadable is unreadable.
        /// </summary>
        /// <param name="args">Ignored.</param>
        /// <remarks>
        /// The useful column is the last one, and it separates the two questions that get
        /// confused when a bar is missing: whether the character's own client is publishing
        /// anything, and whether we would draw it if it were.
        /// </remarks>
        private static void Report(Terminal.ConsoleEventArgs args)
        {
            Player me = Player.m_localPlayer;

            if (me == null)
            {
                args.Context.AddString("No player yet.");
                return;
            }

            args.Context.AddString(Describe(me, me, "you"));

            Vector3 here = me.transform.position;
            List<Character> all = Character.GetAllCharacters();
            int shown = 0;

            for (int i = 0; i < all.Count; i++)
            {
                Character c = all[i];

                if (c == null || ReferenceEquals(c, me))
                {
                    continue;
                }

                float away = Vector3.Distance(here, c.transform.position);

                if (away > SearchRadius)
                {
                    continue;
                }

                shown++;
                args.Context.AddString(Describe(me, c, Mathf.RoundToInt(away) + "m"));
            }

            if (shown == 0)
            {
                args.Context.AddString("Nothing else within " + Mathf.RoundToInt(SearchRadius) + "m.");
            }
        }

        /// <summary>One line about one character.</summary>
        /// <returns>Name, side, who owns it, and the barrier if we can see one.</returns>
        /// <param name="me">The local player, for the ally test.</param>
        /// <param name="c">Who to describe.</param>
        /// <param name="where">How to label the distance column.</param>
        private static string Describe(Player me, Character c, string where)
        {
            string name = string.IsNullOrEmpty(c.m_name) ? c.name : c.m_name;
            bool ally = Shields.IsAlly(me, c);
            bool valid = c.m_nview != null && c.m_nview.IsValid();
            bool ours = valid && c.m_nview.IsOwner();

            string line = name + " (" + where + ") - " + (ally ? "ally" : "enemy")
                + " - " + (!valid ? "no zdo" : ours ? "ours" : "owned elsewhere");

            if (!ours)
            {
                ShieldReading mirrored = valid ? Mirror.Read(c.m_nview.GetZDO()) : ShieldReading.None;

                if (!mirrored.IsPresent)
                {
                    return line + " - nothing published" + WhyNotDrawn(ally);
                }

                return line + " - mirrored " + Mathf.RoundToInt(mirrored.Fraction * 100f) + "%"
                    + WhyNotDrawn(ally);
            }

            if (!Shields.TryGetShield(c, out SE_Shield shield))
            {
                return line + " - no barrier";
            }

            ShieldReading reading = ShieldReading.For(shield.m_damage, shield.m_totalAbsorbDamage);

            // Left is what the bar draws, which clamps. Raw m_damage can exceed the capacity for
            // the frame between a killing hit and IsDone popping the effect, and printing that
            // difference unclamped would read as a negative barrier.
            float left = reading.Fraction * shield.m_totalAbsorbDamage;

            return line
                + " - barrier " + left.ToString("0")
                + "/" + shield.m_totalAbsorbDamage.ToString("0")
                + " (" + Mathf.RoundToInt(reading.Fraction * 100f) + "%)"
                + ", " + shield.GetRemaningTime().ToString("0") + "s left";
        }

        /// <summary>Why a barrier that exists might still not be on screen.</summary>
        /// <returns>A parenthetical, or an empty string when it would be drawn.</returns>
        /// <param name="ally">Whether this character counts as friendly.</param>
        /// <remarks>
        /// Both switches matter and they fail identically from the outside, which is exactly the
        /// confusion this command exists to settle.
        /// </remarks>
        private static string WhyNotDrawn(bool ally)
        {
            if (!ally)
            {
                return " (not drawn, enemies never get a bar)";
            }

            if (!ModConfig.ShowOthersBarriers.Value)
            {
                return " (not drawn, ShowOthersBarriers is off)";
            }

            if (!ModConfig.ShowAllyBars.Value)
            {
                return " (not drawn, ShowAllyBars is off)";
            }

            return string.Empty;
        }
    }
}
