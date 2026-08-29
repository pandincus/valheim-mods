using System.Collections.Generic;
using UnityEngine;

namespace ChattyBones
{
    /// <summary>Console commands for exercising the render path without picking a fight.</summary>
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
        /// Being able to make a skeleton talk on demand means that when a line fails
        /// to appear later, the drawing half is already ruled out. Which is also why
        /// it reports *which* path drew: "nothing appeared" and "the panel appeared
        /// off-screen" look identical from the chair.
        /// </remarks>
        private static void Say(Terminal.ConsoleEventArgs args)
        {
            if (Player.m_localPlayer == null)
            {
                args.Context.AddString("No player yet.");
                return;
            }

            // ArgsAll is everything after the command token. Length > 1 only promises
            // a space, not text after it, so a trailing space would otherwise "say" an
            // empty line and still report it as spoken.
            string typed = args.ArgsAll == null ? string.Empty : args.ArgsAll.Trim();
            string line = typed.Length > 0 ? typed : "My bones are itchy.";

            if (!Summons.TryFindNearest(Player.m_localPlayer.transform.position, SearchRadius, out Character skeleton))
            {
                args.Context.AddString("No summoned skeleton within " + SearchRadius + "m.");
                return;
            }

            string name = Summons.NameOf(skeleton);
            Drew drew = Speech.Say(skeleton, line);

            args.Context.AddString(drew == Drew.Nothing
                ? name + " said nothing - check Enabled, and whether the world has finished loading."
                : name + " says (" + drew + "): " + line);
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

            List<Character> all = Character.GetAllCharacters();
            for (int i = 0; i < all.Count; i++)
            {
                if (!Summons.IsSummoned(all[i]))
                {
                    continue;
                }

                found++;
                args.Context.AddString(
                    Summons.NameOf(all[i])
                    + " - " + Mathf.RoundToInt(Vector3.Distance(me, all[i].transform.position)) + "m"
                    + " - " + PersonalityOf(all[i]));
            }

            args.Context.AddString(
                found + " summoned skeleton(s) loaded. Style: " + ModConfig.Bubble.Value
                + ", enabled: " + ModConfig.Enabled.Value);
        }

        /// <summary>Which personality a skeleton is playing.</summary>
        /// <returns>The personality name, or a reason we cannot say.</returns>
        /// <param name="character">One of ours.</param>
        /// <remarks>
        /// Otherwise the only way to know is to wait for it to say something
        /// characteristic, which is slow when you are trying to check that a squad
        /// came out varied rather than all the same.
        ///
        /// Reading this assigns one if the skeleton has not got there yet, which is a
        /// side effect worth knowing about in a command that otherwise only looks. It
        /// is the same assignment its first line would have made a moment later, so
        /// nothing is changed except when - and on a skeleton somebody else owns we
        /// have no business assigning anything, so that answers "unassigned" instead.
        /// </remarks>
        private static string PersonalityOf(Character character)
        {
            ChatterComponent chatter = character.GetComponent<ChatterComponent>();

            return chatter == null
                ? "no chatter component"
                : chatter.Personality ?? "unassigned";
        }
    }
}
