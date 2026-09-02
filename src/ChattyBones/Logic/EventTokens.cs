using System;
using System.Collections.Generic;

namespace ChattyBones.Logic
{
    /// <summary>The tokens describing what happened, as a set rather than four bools.</summary>
    /// <remarks>
    /// Name and player are missing on purpose. Every event has both, so a flag for
    /// them would only ever be set and the interesting comparison is about the rest.
    /// </remarks>
    [Flags]
    internal enum TokenSet
    {
        /// <summary>Nothing beyond the skeleton's own name and yours.</summary>
        None = 0,

        /// <summary>{target} - the creature the remark is about.</summary>
        Target = 1,

        /// <summary>{companion} - another of your skeletons.</summary>
        Companion = 2,

        /// <summary>{ally} - a particular other player, named by the event.</summary>
        /// <remarks>
        /// Out of numerical order, and it has to be: these are the bits of a packed
        /// set, so slotting a new flag in beside its relatives would renumber every
        /// one above it. The same argument as ChatterEvent's, for the same reason.
        /// </remarks>
        Ally = 512,

        /// <summary>{weapon} - the weapon's own name.</summary>
        Weapon = 4,

        /// <summary>{weapontype} - what kind of weapon it was.</summary>
        WeaponType = 8,

        /// <summary>{damage} - the dominant damage type.</summary>
        Damage = 16,

        /// <summary>{status} - a status effect's name.</summary>
        Status = 32,

        /// <summary>{biome} - where the skeleton is standing.</summary>
        Biome = 64,

        /// <summary>{item} - something eaten or picked up.</summary>
        Item = 128,

        /// <summary>{skill} - the skill that went up.</summary>
        Skill = 256,
    }

    /// <summary>Which events promise which tokens, and what the hooks have actually supplied.</summary>
    /// <remarks>
    /// Three statements of this have to agree: the call sites that pass the values,
    /// this table, and the grid in the pack header that tells authors what they can
    /// write. The tests hold the last two together, and <see cref="Report"/> is how
    /// you check the first while playing.
    ///
    /// Only under-promising is caught statically - a token this table forgets makes a
    /// shipped line unrenderable and two tests fail. Over-promising in both this table
    /// and the grid is caught by neither, and that is the edit somebody makes when
    /// adding an event, because they touch both together.
    ///
    /// An earlier version tried to report the disagreement by itself, as a warning in
    /// the log. Two reviews and a run of the real code killed it: a promised token can
    /// be legitimately absent for the whole session, and the case the pack header names
    /// as normal - a killer that was not a Humanoid leaving no {weapon} - fires
    /// reliably in ordinary play.
    /// Counting firings first only moved the false accusation later, because nothing
    /// in a count separates "the hook stopped passing it" from "the game never
    /// produces one here". So the judgement is left to a person, who has the context
    /// to make it, and this only keeps the notes.
    /// </remarks>
    internal static class EventTokens
    {
        /// <summary>What an event undertakes to supply.</summary>
        /// <returns>The tokens a pack author can rely on for this event.</returns>
        /// <param name="kind">The event being fired.</param>
        /// <remarks>
        /// Grouped rather than listed per event because the grouping is the reasoning:
        /// a live blow can be described in full, a kill knows only what the killer was
        /// holding, and everything else is about people.
        ///
        /// **Companion and Ally mean something stronger here than the rest do.** Both
        /// can be filled on any event at all - a skeleton standing next to another one
        /// can name it in an idle mutter or in a line about you being hurt - so listing
        /// them would be pointless if the flag only meant "can be filled". What the
        /// flag means for those two is that the event supplies a *particular* person,
        /// the one it is about, and that Chatter must therefore not fall back to
        /// whoever happens to be nearby.
        ///
        /// That distinction is doing real work. Without it, a CompanionDied whose hook
        /// stopped handing over the fallen skeleton would quietly name a living one -
        /// "Oh no, Bjorn!" with Bjorn standing right there - instead of passing the
        /// line over and being noticed.
        /// </remarks>
        internal static TokenSet PromisedFor(ChatterEvent kind)
        {
            TokenSet set = TokenSet.None;

            // PlayerParried and PlayerStaggered name whoever swung, which they read
            // off the blow; StaggeredIt names what you knocked about. PlayerDodged is
            // not here because RPC_HitWhileDodging is told nothing about the attacker.
            if (kind is ChatterEvent.TargetAcquired
                or ChatterEvent.Killed
                or ChatterEvent.CompanionKilled
                or ChatterEvent.PlayerLandedABigHit
                or ChatterEvent.PlayerGotAKill
                or ChatterEvent.PlayerParried
                or ChatterEvent.PlayerStaggered
                or ChatterEvent.StaggeredIt)
            {
                set |= TokenSet.Target;
            }

            // Idle used to be in this list and is deliberately not any more. It was
            // here because its hook passes a random other skeleton, which is now what
            // every event gets for free - so listing it claimed something stronger than
            // was true and made the flag mean two things at once.
            if (kind is ChatterEvent.CompanionHurt
                or ChatterEvent.CompanionKilled
                or ChatterEvent.CompanionDied
                or ChatterEvent.CompanionSummoned)
            {
                set |= TokenSet.Companion;
            }

            // A blow caught as it lands, so it can be described in full.
            if (kind is ChatterEvent.Hurt
                or ChatterEvent.PlayerHurt
                or ChatterEvent.CompanionHurt
                or ChatterEvent.PlayerLandedABigHit)
            {
                set |= TokenSet.Weapon | TokenSet.WeaponType | TokenSet.Damage;
            }

            // A kill or a death knows only what the killer was holding. m_lastHit is
            // sitting right there on the body and is a trap - RPC_Damage has already
            // lifted the fire, poison and spirit off it by then, so its damage is
            // incomplete in exactly the way the prefix read exists to avoid.
            if (kind is ChatterEvent.Killed
                or ChatterEvent.CompanionKilled
                or ChatterEvent.Died
                or ChatterEvent.PlayerGotAKill)
            {
                set |= TokenSet.Weapon | TokenSet.WeaponType;
            }

            if (kind is ChatterEvent.Buffed or ChatterEvent.Afflicted or ChatterEvent.Weather)
            {
                set |= TokenSet.Status;
            }

            // Dawn and Nightfall are handed the biome by the method they hook, so it
            // costs nothing there and lets a sunrise line know what it is coming up
            // over.
            if (kind is ChatterEvent.BiomeChanged or ChatterEvent.Dawn or ChatterEvent.Nightfall)
            {
                set |= TokenSet.Biome;
            }

            // The same question either way - what was it - so one token serves both.
            if (kind is ChatterEvent.Looted or ChatterEvent.PlayerAte or ChatterEvent.PlayerCooked)
            {
                set |= TokenSet.Item;
            }

            if (kind == ChatterEvent.PlayerSkilledUp)
            {
                set |= TokenSet.Skill;
            }

            if (kind == ChatterEvent.AllyArrived)
            {
                set |= TokenSet.Ally;
            }

            return set;
        }

        /// <summary>Whether a token may be filled in from whoever is standing about.</summary>
        /// <returns>True when the event does not name somebody itself.</returns>
        /// <param name="kind">The event being fired.</param>
        /// <param name="token">
        /// <see cref="TokenSet.Companion"/> or <see cref="TokenSet.Ally"/>. Asking about
        /// a detail token answers true and means nothing - nothing fills a weapon in
        /// from its surroundings.
        /// </param>
        /// <remarks>
        /// The rule <see cref="PromisedFor"/> exists to state, given a name so that the
        /// call site reads as the rule rather than as a bit test - and so that a test
        /// can hold it, which a branch buried in a Unity class cannot be.
        ///
        /// Getting this backwards is the expensive mistake: filling in where the event
        /// named somebody would have CompanionDied mourn a skeleton that is still
        /// standing, quietly and with the right grammar.
        /// </remarks>
        internal static bool ShouldFillIn(ChatterEvent kind, TokenSet token)
        {
            return (PromisedFor(kind) & token) == 0;
        }

        /// <summary>What a call site actually handed over.</summary>
        /// <returns>The tokens with a value in them.</returns>
        /// <param name="target">The localized creature name, or null.</param>
        /// <param name="companion">The companion's name, or null.</param>
        /// <param name="ally">The other player's name, or null.</param>
        /// <param name="details">Everything describing the event itself.</param>
        internal static TokenSet SuppliedBy(
            string target, string companion, string ally, LineDetails details)
        {
            TokenSet set = TokenSet.None;

            if (target != null) { set |= TokenSet.Target; }
            if (companion != null) { set |= TokenSet.Companion; }
            if (ally != null) { set |= TokenSet.Ally; }
            if (details.Weapon != null) { set |= TokenSet.Weapon; }
            if (details.WeaponType != null) { set |= TokenSet.WeaponType; }
            if (details.Damage != null) { set |= TokenSet.Damage; }
            if (details.Status != null) { set |= TokenSet.Status; }
            if (details.Biome != null) { set |= TokenSet.Biome; }
            if (details.Item != null) { set |= TokenSet.Item; }
            if (details.Skill != null) { set |= TokenSet.Skill; }

            return set;
        }

        /// <summary>Every token that has actually turned up for each event this session.</summary>
        /// <remarks>
        /// Sized from the highest value rather than the number of members. ChatterEvent
        /// is implicitly numbered today so the two agree, but its own documentation
        /// argues for pinning the numbers, since they travel in a packed int - and
        /// sizing by count would then leave the newest events off the end.
        ///
        /// Every read of the length below goes through this array rather than through
        /// the field that sized it, so moving these declarations around cannot produce
        /// a bounds check that passes over an array of zero.
        /// </remarks>
        private static readonly TokenSet[] Seen = new TokenSet[HighestEvent() + 1];

        /// <summary>Note what a hook handed over, so <see cref="Report"/> can say later.</summary>
        /// <param name="kind">The event being fired.</param>
        /// <param name="target">The localized creature name, or null.</param>
        /// <param name="companion">The companion's name, or null.</param>
        /// <param name="ally">The other player's name, or null.</param>
        /// <param name="details">Everything describing the event itself.</param>
        /// <remarks>
        /// One array write, so it runs whether or not logging is switched on - the
        /// point of the report is to be able to ask after something looked wrong,
        /// which is too late to start collecting.
        /// </remarks>
        internal static void Note(
            ChatterEvent kind, string target, string companion, string ally, LineDetails details)
        {
            int i = (int)kind;

            if (i >= 0 && i < Seen.Length)
            {
                Seen[i] |= SuppliedBy(target, companion, ally, details);
            }
        }

        /// <summary>What each event promises against what it has been seen to supply.</summary>
        /// <returns>One line per event, plus a note on how to read a gap.</returns>
        /// <remarks>
        /// Read by a person, on demand, which is the whole design. A gap here is a
        /// question rather than a defect: {companion} missing from Idle means either
        /// the hook stopped passing it or you have only ever had one skeleton out, and
        /// nothing in the data tells the two apart. Somebody who knows what they were
        /// just doing can tell instantly, which is why this replaced a warning that
        /// guessed.
        ///
        /// An event that has never fired shows nothing supplied, which looks alarming
        /// and is not, so it says so.
        /// </remarks>
        internal static IReadOnlyList<string> Report()
        {
            List<string> lines = [];

            foreach (ChatterEvent kind in Enum.GetValues(typeof(ChatterEvent)))
            {
                TokenSet promised = PromisedFor(kind);
                if (promised == TokenSet.None)
                {
                    continue;
                }

                int i = (int)kind;
                TokenSet seen = i >= 0 && i < Seen.Length ? Seen[i] : TokenSet.None;
                TokenSet missing = promised & ~seen;
                TokenSet extra = seen & ~promised;

                string line = kind + ": promises " + Names(promised);

                if (missing != TokenSet.None)
                {
                    line += ", never seen " + Names(missing);
                }

                if (extra != TokenSet.None)
                {
                    line += ", supplies unpromised " + Names(extra);
                }

                lines.Add(line);
            }

            lines.Add("A token never seen is a question, not a fault - it may simply "
                + "not exist for that event yet, or the event may not have fired.");

            return lines;
        }

        /// <summary>Forget what has been seen, so a test can start from nothing.</summary>
        /// <remarks>
        /// The state above is static and deliberately lives for the session, which is
        /// right in game and useless in a test suite where one case would poison the
        /// next. Not called by the mod.
        /// </remarks>
        internal static void Forget()
        {
            for (int i = 0; i < Seen.Length; i++)
            {
                Seen[i] = TokenSet.None;
            }
        }

        /// <summary>Spell a set out the way a pack author would write it.</summary>
        /// <returns>Brace-wrapped token names, comma separated, e.g. "{weapon}, {damage}".</returns>
        /// <param name="set">The tokens to name.</param>
        private static string Names(TokenSet set)
        {
            string names = null;

            foreach (TokenSet one in AllTokens)
            {
                if ((set & one) == 0)
                {
                    continue;
                }

                string name = "{" + one.ToString().ToLowerInvariant() + "}";
                names = names == null ? name : names + ", " + name;
            }

            return names ?? "nothing";
        }

        /// <summary>The largest value in the event enum.</summary>
        /// <returns>The highest ordinal, so the array can be sized to hold it.</returns>
        private static int HighestEvent()
        {
            int highest = 0;

            foreach (object value in Enum.GetValues(typeof(ChatterEvent)))
            {
                int one = (int)value;
                if (one > highest)
                {
                    highest = one;
                }
            }

            return highest;
        }

        /// <summary>Every flag except None, smallest bit first.</summary>
        /// <remarks>
        /// Derived rather than hand-listed, so a flag added to TokenSet cannot quietly
        /// drop out of every message it appears in.
        /// </remarks>
        private static readonly TokenSet[] AllTokens = BuildAllTokens();

        /// <summary>Collect the real flags out of the enum.</summary>
        /// <returns>One entry per token.</returns>
        private static TokenSet[] BuildAllTokens()
        {
            List<TokenSet> all = [];

            foreach (object value in Enum.GetValues(typeof(TokenSet)))
            {
                TokenSet one = (TokenSet)value;
                if (one != TokenSet.None)
                {
                    all.Add(one);
                }
            }

            return [.. all];
        }
    }
}
