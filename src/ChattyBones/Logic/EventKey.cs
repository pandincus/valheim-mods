using System;
using System.Collections.Generic;

namespace ChattyBones.Logic
{
    /// <summary>An event name from a pack file, with the context it was tagged with.</summary>
    /// <remarks>
    /// A pack writes <c>Idle</c> for lines that suit anywhere and <c>Idle[biome=Swamp]</c>
    /// for lines that only suit the Swamp. This is the thing that tells those apart.
    ///
    /// The suffix won over the two obvious alternatives for the same reason: a nested
    /// <c>when:</c> block separates related lines by half a file, and a marker inside the
    /// line puts markup in the one place the format has worked hard to keep as plain
    /// prose. A suffix keeps the conditional group next to the ordinary one, which is
    /// where somebody writing them wants it.
    ///
    /// One context per key, deliberately. <c>Idle[biome=Swamp,time=night]</c> is a
    /// reasonable thing to want and it is refused, because "most specific wins" has no
    /// honest answer for whether a biome beats a time - and a rule nobody can predict is
    /// worse than a feature nobody has.
    /// </remarks>
    internal readonly struct EventKey : IEquatable<EventKey>
    {
        /// <summary>The context names a pack may filter on, and the values each takes.</summary>
        /// <remarks>
        /// Each new context is a row here plus a resolver on the mod side, which is what
        /// makes them additive - the parsing, the fallback chain and the line numbering
        /// are all indifferent to which context is in play.
        ///
        /// An unknown name is refused rather than ignored. A pack that never fires is
        /// the failure this project keeps paying for, and a typo like
        /// <c>Idle[bioem=Swamp]</c> is exactly that failure with no symptom - so it is
        /// reported at load, where somebody can still see it.
        ///
        /// A null value set means "this half cannot check it". That is only
        /// <c>biome</c>, whose spellings are a Unity enum this assembly deliberately
        /// cannot see; the mod side catches those at load instead. Everywhere else the
        /// vocabulary is a handful of plain words, and checking them here is worth
        /// doing because a parse failure is reported against the line of the file it
        /// came from - so <c>Idle[time=noon]</c> is refused at line 412 and told what
        /// would have worked, rather than warned about after the whole file has been
        /// read.
        /// </remarks>
        private static readonly Dictionary<string, HashSet<string>> KnownContexts = new(StringComparer.Ordinal)
        {
            ["biome"] = null,
            ["home"] = ["yes", "no"],
            ["time"] = new HashSet<string>(TimeOfDay.All, StringComparer.Ordinal),
        };

        /// <summary>Build a key. Private so that <see cref="TryParse"/> is the only way in.</summary>
        /// <param name="kind">The event.</param>
        /// <param name="context">The context, already normalized, or null.</param>
        private EventKey(ChatterEvent kind, string context)
        {
            Kind = kind;
            Context = context;
        }

        /// <summary>A key for lines that suit anywhere.</summary>
        /// <returns>The key for that event's plain group.</returns>
        /// <param name="kind">The event.</param>
        internal static EventKey Plain(ChatterEvent kind)
        {
            return new EventKey(kind, null);
        }

        /// <summary>The event these lines are a reaction to.</summary>
        internal ChatterEvent Kind { get; }

        /// <summary>
        /// The context, as <c>name=value</c>, or null for a group that suits anywhere.
        /// </summary>
        /// <remarks>
        /// Held as the joined string rather than split in two, because every use of it
        /// is a comparison: the resolver hands over the contexts a skeleton currently
        /// satisfies, and a group applies when its context is one of them. Splitting
        /// would mean re-joining at every one of those comparisons.
        /// </remarks>
        internal string Context { get; }

        /// <summary>Whether these lines suit anywhere, rather than one context.</summary>
        internal bool IsPlain => Context == null;

        /// <summary>Read an event key as a pack file writes it.</summary>
        /// <returns>True when the text is a usable key.</returns>
        /// <param name="text">Whatever the file said, e.g. "Idle" or "Idle[biome=Swamp]".</param>
        /// <param name="key">The parsed key, when we return true.</param>
        /// <param name="problem">
        /// What was wrong with it, when we return false. Written to be read by whoever
        /// edited the file rather than by whoever wrote this, and never null on failure.
        /// </param>
        /// <remarks>
        /// Whitespace inside the brackets is trimmed, so <c>Idle[biome = Swamp]</c>
        /// works. That is a kindness to hand-editors and costs nothing.
        ///
        /// The value is *not* checked against the biomes the game actually has, because
        /// that list is a Unity type and this half of the mod cannot see one. The mod
        /// side checks it at load instead, against the contexts
        /// <see cref="LineSpace"/> reports.
        /// </remarks>
        internal static bool TryParse(string text, out EventKey key, out string problem)
        {
            key = default;
            problem = null;

            string name = text?.Trim();

            if (string.IsNullOrEmpty(name))
            {
                problem = "an event name is missing";
                return false;
            }

            int open = name.IndexOf('[');

            if (open < 0)
            {
                if (!TryEvent(name, out ChatterEvent plain, out problem))
                {
                    return false;
                }

                key = new EventKey(plain, null);
                return true;
            }

            if (name[name.Length - 1] != ']')
            {
                problem = "'" + name + "' opens a [ that is never closed";
                return false;
            }

            string before = name.Substring(0, open).Trim();
            string inside = name.Substring(open + 1, name.Length - open - 2).Trim();

            if (!TryEvent(before, out ChatterEvent kind, out problem))
            {
                return false;
            }

            if (!TryContext(inside, out string context, out problem))
            {
                return false;
            }

            key = new EventKey(kind, context);
            return true;
        }

        /// <summary>Match a name against one of our events.</summary>
        /// <returns>True when the name is exactly one of them.</returns>
        /// <param name="name">The part before any bracket.</param>
        /// <param name="kind">The event, when we return true.</param>
        /// <param name="problem">Why not, when we return false.</param>
        /// <remarks>
        /// The IsDefined call is doing real work, for the same reason it does in
        /// PackReader: Enum.TryParse on its own accepts a number as well as a name, so
        /// "3" would quietly become an event rather than being reported.
        /// </remarks>
        private static bool TryEvent(string name, out ChatterEvent kind, out string problem)
        {
            kind = default;
            problem = null;

            if (string.IsNullOrEmpty(name)
                || !Enum.IsDefined(typeof(ChatterEvent), name)
                || !Enum.TryParse(name, out kind))
            {
                problem = "'" + name + "' is not one of the events";
                return false;
            }

            return true;
        }

        /// <summary>Read the part between the brackets.</summary>
        /// <returns>True when it is a single known context with a value.</returns>
        /// <param name="inside">The text between [ and ], already trimmed.</param>
        /// <param name="context">The normalized "name=value", when we return true.</param>
        /// <param name="problem">Why not, when we return false.</param>
        private static bool TryContext(string inside, out string context, out string problem)
        {
            context = null;
            problem = null;

            if (inside.Length == 0)
            {
                problem = "the [] is empty - it wants something like [biome=Swamp]";
                return false;
            }

            if (inside.IndexOf(',') >= 0)
            {
                problem = "'" + inside + "' asks for two contexts at once, which is not supported"
                    + " - write one of them, or a plain group";
                return false;
            }

            int equals = inside.IndexOf('=');

            if (equals < 0)
            {
                problem = "'" + inside + "' is missing an = - it wants something like [biome=Swamp]";
                return false;
            }

            string what = inside.Substring(0, equals).Trim();
            string value = inside.Substring(equals + 1).Trim();

            if (!KnownContexts.TryGetValue(what, out HashSet<string> allowed))
            {
                problem = "'" + what + "' is not a context this version understands"
                    + " (it knows: " + string.Join(", ", Sorted(KnownContexts.Keys)) + ")";
                return false;
            }

            if (value.Length == 0)
            {
                problem = "'" + what + "' has no value after the =";
                return false;
            }

            if (allowed != null && !allowed.Contains(value))
            {
                problem = "'" + value + "' is not one of the values '" + what + "' takes"
                    + " (it takes: " + string.Join(", ", Sorted(allowed)) + ")";
                return false;
            }

            context = what + "=" + value;
            return true;
        }

        /// <summary>Names in a stable order, for a message.</summary>
        /// <returns>The names, sorted.</returns>
        /// <param name="names">The names to sort.</param>
        /// <remarks>
        /// Sorted rather than written out in file order, because this is the one place
        /// the reader is scanning for a word they expected to find and did not.
        /// </remarks>
        private static string[] Sorted(ICollection<string> names)
        {
            string[] all = new string[names.Count];
            names.CopyTo(all, 0);
            Array.Sort(all, StringComparer.Ordinal);
            return all;
        }

        /// <summary>Whether two keys mean the same group.</summary>
        /// <returns>True when the event and the context both match.</returns>
        /// <param name="other">The key to compare with.</param>
        public bool Equals(EventKey other)
        {
            return Kind == other.Kind && string.Equals(Context, other.Context, StringComparison.Ordinal);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return obj is EventKey other && Equals(other);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return ((int)Kind * 397) ^ (Context == null ? 0 : StringComparer.Ordinal.GetHashCode(Context));
        }

        /// <summary>How the key would be written in a pack.</summary>
        /// <returns>"Idle" or "Idle[biome=Swamp]".</returns>
        public override string ToString()
        {
            return IsPlain ? Kind.ToString() : Kind + "[" + Context + "]";
        }
    }
}
