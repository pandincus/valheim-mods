using UnityEngine;

namespace ChattyBones
{
    /// <summary>Console commands for looking at the render path without a fight.</summary>
    /// <remarks>
    /// Registering from Awake is safe: Terminal.commands is a static dictionary
    /// created once and never cleared, and InitTerminal guards itself with a flag.
    ///
    /// These are for us rather than for players, so they are marked secret and stay
    /// out of the tab-completion list.
    /// </remarks>
    internal static class DebugCommands
    {
        /// <summary>How far to look for a skeleton to talk at.</summary>
        private const float SearchRadius = 50f;

        /// <summary>Register the commands. Called once from Awake.</summary>
        internal static void Register()
        {
            _ = new Terminal.ConsoleCommand(
                "cb_say",
                "[text] - make the nearest summoned skeleton say something",
                Say,
                isCheat: false,
                isNetwork: false,
                onlyServer: false,
                isSecret: true);

            _ = new Terminal.ConsoleCommand(
                "cb_who",
                "list the summoned skeletons nearby",
                Who,
                isCheat: false,
                isNetwork: false,
                onlyServer: false,
                isSecret: true);
        }

        /// <summary>Make the nearest skeleton say whatever you typed.</summary>
        /// <param name="args">Everything after the command name becomes the line.</param>
        /// <remarks>
        /// The whole point of Phase 3: see text over a skull before any of the event
        /// hooks exist, so that when a line fails to appear later we already know the
        /// drawing half works.
        /// </remarks>
        private static void Say(Terminal.ConsoleEventArgs args)
        {
            if (Player.m_localPlayer == null)
            {
                args.Context.AddString("No player yet.");
                return;
            }

            string line = args.Length > 1 ? args.FullLine.Substring(args[0].Length).Trim() : "My bones are itchy.";

            if (!Summons.TryFindNearest(Player.m_localPlayer.transform.position, SearchRadius, out Character skeleton))
            {
                args.Context.AddString("No summoned skeleton within " + SearchRadius + "m.");
                return;
            }

            Speech.Say(skeleton, Summons.NameOf(skeleton), line);
            args.Context.AddString(Summons.NameOf(skeleton) + " says: " + line);
        }

        /// <summary>List nearby summons, so "nothing happened" can be told from "nothing is there".</summary>
        /// <param name="args">Ignored.</param>
        private static void Who(Terminal.ConsoleEventArgs args)
        {
            if (Player.m_localPlayer == null)
            {
                args.Context.AddString("No player yet.");
                return;
            }

            Vector3 me = Player.m_localPlayer.transform.position;
            int found = 0;

            System.Collections.Generic.List<Character> all = Character.GetAllCharacters();
            for (int i = 0; i < all.Count; i++)
            {
                if (!Summons.IsSummoned(all[i]))
                {
                    continue;
                }

                found++;
                args.Context.AddString(
                    Summons.NameOf(all[i]) + " - " + Mathf.RoundToInt(Vector3.Distance(me, all[i].transform.position)) + "m");
            }

            args.Context.AddString(found + " summoned skeleton(s) loaded.");
        }
    }
}
