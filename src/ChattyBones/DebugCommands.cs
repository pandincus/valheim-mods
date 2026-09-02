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
                "cb_mirror",
                "draw the nearest skeleton's own broadcast back at you, the way another player would see it",
                Broadcast,
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

        /// <summary>Read a skeleton's broadcast and draw it the way a listener would.</summary>
        /// <param name="args">Ignored.</param>
        /// <remarks>
        /// Testing the mirrored path properly needs two machines, and this is what gets
        /// most of the way there on one. Everything after the ZDO is exercised: reading
        /// the packed int back, unpacking the detail record, turning a prefab hash and
        /// a ZDOID into names, folding the line ref against the pack, and walking on
        /// when the line will not render. Only the replication itself is untested, and
        /// that is the one part vanilla is doing rather than us.
        ///
        /// Aimed at your own skeleton on purpose, which means it draws over one you can
        /// already hear. That is the point: say something, then run this, and the two
        /// lines should agree - or differ in a way you can explain by a token this side
        /// could not work out.
        /// </remarks>
        private static void Broadcast(Terminal.ConsoleEventArgs args)
        {
            if (Player.m_localPlayer == null)
            {
                args.Context.AddString("No player yet.");
                return;
            }

            if (!Summons.TryFindNearest(Player.m_localPlayer.transform.position, SearchRadius, out Character skeleton))
            {
                args.Context.AddString("No summoned skeleton within " + SearchRadius + "m.");
                return;
            }

            string name = Summons.NameOf(skeleton) ?? "It";
            ChatterComponent chatter = skeleton.GetComponent<ChatterComponent>();

            if (chatter == null)
            {
                args.Context.AddString(name + " has no chatter component.");
                return;
            }

            if (!chatter.TryReadBroadcast(
                    out Utterance said, out LineDetails details, out ZDOID companion, out ZDOID ally))
            {
                args.Context.AddString(name + " has not said anything yet - nothing to mirror.");
                return;
            }

            args.Context.AddString(
                name + " is broadcasting " + said.Kind + " #" + said.Counter
                + ", line ref " + said.LineRef
                + ", about " + (said.Subject == 0 ? "nothing" : Mirror.CreatureName(said.Subject) ?? "an unknown prefab")
                + ", to " + Who(companion, ally));

            args.Context.AddString("    details: " + Describe(details));

            Chatter.Hear(chatter, said, details, companion, ally);
        }

        /// <summary>Name whoever an utterance was addressed to.</summary>
        /// <returns>The people it named, or a note that it named none.</returns>
        /// <param name="companion">The skeleton field.</param>
        /// <param name="ally">The player field.</param>
        private static string Who(ZDOID companion, ZDOID ally)
        {
            List<string> named = [];

            if (companion != ZDOID.None)
            {
                named.Add("companion " + (Mirror.NameFor(companion) ?? "not loaded here"));
            }

            if (ally != ZDOID.None)
            {
                named.Add("ally " + (Mirror.NameFor(ally) ?? "not loaded here"));
            }

            return named.Count == 0 ? "nobody" : string.Join(", ", named);
        }

        /// <summary>Spell a detail record out for the console.</summary>
        /// <returns>The fields that have something in them, or a note that none do.</returns>
        /// <param name="details">Straight off the wire, so keys rather than words.</param>
        private static string Describe(LineDetails details)
        {
            List<string> had = [];

            if (details.Weapon != null) { had.Add("weapon=" + details.Weapon); }
            if (details.WeaponType != null) { had.Add("weapontype=" + details.WeaponType); }
            if (details.Damage != null) { had.Add("damage=" + details.Damage); }
            if (details.Status != null) { had.Add("status=" + details.Status); }
            if (details.Biome != null) { had.Add("biome=" + details.Biome); }
            if (details.Item != null) { had.Add("item=" + details.Item); }
            if (details.Skill != null) { had.Add("skill=" + details.Skill); }

            return had.Count == 0 ? "none" : string.Join(", ", had);
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
