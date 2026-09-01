using System.Collections.Generic;
using ChattyBones.Logic;
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
                "[event] - list the summoned skeletons nearby, with the group each would draw from",
                Who,
                isCheat: false,
                isNetwork: false,
                onlyServer: false,
                isSecret: true);

            _ = new Terminal.ConsoleCommand(
                "cb_tokens",
                "what each event promises, against what it has actually supplied",
                Tokens,
                isCheat: false,
                isNetwork: false,
                onlyServer: false,
                isSecret: true);
        }

        /// <summary>Show what the hooks have really been handing over.</summary>
        /// <param name="args">Ignored.</param>
        /// <remarks>
        /// The one thing no test can check: the tests hold the table and the pack
        /// header's grid together, and neither of them can see whether a call site
        /// still passes what it used to. Delete a token from a hook and every test
        /// stays green while the lines using it are quietly skipped in game.
        ///
        /// A command rather than a warning, and that was decided the hard way. The
        /// earlier version logged the disagreement by itself, and could not tell a
        /// hook that had stopped passing something from a token that simply never
        /// exists for that event - a lone skeleton's Idle has no companion to name,
        /// and plenty of things that kill a skeleton are not holding a weapon. Both
        /// read as drift, both were reported as drift, and both are documented in the
        /// pack header as normal. Asking a person, who knows what they were just
        /// doing, is the version that works.
        /// </remarks>
        private static void Tokens(Terminal.ConsoleEventArgs args)
        {
            IReadOnlyList<string> report = EventTokens.Report();

            for (int i = 0; i < report.Count; i++)
            {
                args.Context.AddString(report[i]);
            }
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

            string name = Summons.NameOf(skeleton) ?? "It";
            Drew drew = Speech.Say(skeleton, line);

            args.Context.AddString(drew == Drew.Nothing
                ? name + " said nothing - check Enabled, and whether the world has finished loading."
                : name + " says (" + drew + "): " + line);
        }

        /// <summary>List nearby summons, so "nothing happened" can be told from "nothing is there".</summary>
        /// <param name="args">An event to ask about, or nothing for Idle.</param>
        /// <remarks>
        /// Reports the contexts each skeleton satisfies and the group it would actually
        /// draw from, because the tie-break between two matching groups is silent by
        /// design - the one written first wins and nothing is said about the other. The
        /// group named here is the answer to "why am I not hearing the lines I wrote".
        /// </remarks>
        private static void Who(Terminal.ConsoleEventArgs args)
        {
            if (Player.m_localPlayer == null)
            {
                args.Context.AddString("No player yet.");
                return;
            }

            // An event can be named to ask about that one instead. Idle is the default
            // because it is where context groups actually get written, and printing all
            // thirty-one per skeleton would bury the answer.
            ChatterEvent asking = ChatterEvent.Idle;

            string wanted = args.ArgsAll == null ? string.Empty : args.ArgsAll.Trim();

            if (wanted.Length > 0)
            {
                if (EventKey.TryParse(wanted, out EventKey named, out string problem))
                {
                    asking = named.Kind;
                }
                else
                {
                    args.Context.AddString(problem + ". Showing Idle.");
                }
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

                IReadOnlyList<string> contexts = Contexts.For(all[i]);

                args.Context.AddString(
                    (Summons.NameOf(all[i]) ?? "(unnamed)")
                    + " - " + Mathf.RoundToInt(Vector3.Distance(me, all[i].transform.position)) + "m"
                    + " - " + PersonalityOf(all[i])
                    + " - " + (contexts == null || contexts.Count == 0
                        ? "no context"
                        : string.Join(", ", contexts)));

                ChatterComponent chatter = all[i].GetComponent<ChatterComponent>();
                string choice = chatter == null
                    ? null
                    : Chatter.DescribeChoice(chatter.Personality, asking, contexts);

                args.Context.AddString(
                    "    " + asking + " -> " + (choice ?? "nothing it could say"));
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
