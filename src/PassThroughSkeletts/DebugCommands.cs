using System.Collections.Generic;
using UnityEngine;

namespace PassThroughSkeletts
{
    /// <summary>A console command for seeing what the sweep has done.</summary>
    /// <remarks>
    /// Registering from Awake is safe: Terminal.commands is a static dictionary created once
    /// and never cleared, and InitTerminal guards itself with a flag.
    ///
    /// This is for us rather than for players, so it is marked secret and stays out of the
    /// tab-completion list.
    /// </remarks>
    internal static class DebugCommands
    {
        /// <summary>How far out to report on, in metres.</summary>
        private const float SearchRadius = 60f;

        /// <summary>Register the command. Called once from Awake.</summary>
        internal static void Register()
        {
            _ = new Terminal.ConsoleCommand(
                "pts_status",
                "every summoned skeleton nearby, and how many of its collisions with players are off",
                Report,
                isCheat: false,
                isNetwork: false,
                onlyServer: false,
                isSecret: true);
        }

        /// <summary>Print each nearby Skelett and how many collision pairs are being ignored.</summary>
        /// <param name="args">Ignored.</param>
        /// <remarks>
        /// The two numbers that matter are the pair counts. "3/3" is working, and "0/3" with
        /// the mod enabled means the sweep is not reaching it. The owner column is there for the
        /// two-player test: a Skelett owned elsewhere is simulated on the other machine, and
        /// that is the case I most want to see behave.
        /// </remarks>
        private static void Report(Terminal.ConsoleEventArgs args)
        {
            Player me = Player.m_localPlayer;

            if (me == null)
            {
                args.Context.AddString("No player yet.");
                return;
            }

            args.Context.AddString(
                "Enabled: " + ModConfig.Enabled.Value + ", players known to this game: "
                + Player.GetAllPlayers().Count);

            Vector3 here = me.transform.position;
            List<Character> all = Character.GetAllCharacters();
            int shown = 0;

            for (int i = 0; i < all.Count; i++)
            {
                Character c = all[i];

                if (!Passthrough.IsSummoned(c))
                {
                    continue;
                }

                float away = Vector3.Distance(here, c.transform.position);

                if (away > SearchRadius)
                {
                    continue;
                }

                shown++;
                Passthrough.Tally(c, out int ignored, out int total);

                string name = string.IsNullOrEmpty(c.m_name) ? c.name : c.m_name;

                args.Context.AddString(
                    name + " (" + Mathf.RoundToInt(away) + "m) - " + (c.IsOwner() ? "ours" : "owned elsewhere")
                    + " - " + ignored + "/" + total + " pairs ignored");
            }

            if (shown == 0)
            {
                args.Context.AddString("No summoned Skeletts within " + Mathf.RoundToInt(SearchRadius) + "m.");
            }
        }
    }
}
