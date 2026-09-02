using System;
using System.Collections.Generic;

namespace ChattyBones.Logic
{
    /// <summary>Read a player's list of event names out of one config string.</summary>
    /// <remarks>
    /// A list rather than thirty-two switches, which stopped being a question once
    /// there were thirty-two of them. Deleting an event from the pack already does the
    /// same job, so this is a convenience - which is the argument for it being one
    /// entry a player can ignore rather than a section they have to scroll past.
    /// </remarks>
    internal static class EventList
    {
        /// <summary>Turn "Idle, Weather" into events, and say what could not be read.</summary>
        /// <returns>The events named, with anything unrecognised left out.</returns>
        /// <param name="text">What the player typed. Null and empty are both fine.</param>
        /// <param name="unknown">Whatever did not name an event, for the log.</param>
        /// <remarks>
        /// Case-insensitive, unlike the pack, and the difference is deliberate: a pack
        /// is authored content where <c>idle</c> for <c>Idle</c> is a mistake worth
        /// reporting, while this is a list somebody types into a settings box once.
        ///
        /// Digits are refused before parsing rather than after. Enum.TryParse accepts
        /// "9" and hands back the ninth event without complaint, so a player who wrote
        /// a number expecting it to mean nothing would silently switch off Idle.
        /// </remarks>
        internal static IReadOnlyList<ChatterEvent> Parse(string text, out IReadOnlyList<string> unknown)
        {
            List<ChatterEvent> found = [];
            List<string> bad = [];

            unknown = bad;

            if (string.IsNullOrWhiteSpace(text))
            {
                return found;
            }

            foreach (string piece in text.Split(','))
            {
                string name = piece.Trim();

                if (name.Length == 0)
                {
                    continue;
                }

                if (!LooksLikeAName(name)
                    || !Enum.TryParse(name, ignoreCase: true, out ChatterEvent kind)
                    || !Enum.IsDefined(typeof(ChatterEvent), kind))
                {
                    bad.Add(name);
                    continue;
                }

                if (!found.Contains(kind))
                {
                    found.Add(kind);
                }
            }

            return found;
        }

        /// <summary>Everything a player has switched off, by dial or by name.</summary>
        /// <returns>The events that must never be spoken about.</returns>
        /// <param name="reactions">The dial for everything that happens.</param>
        /// <param name="idle">The dial for saying something anyway.</param>
        /// <param name="named">Whatever <see cref="Parse"/> read out of the list.</param>
        /// <remarks>
        /// Two dials set to Never are the same thing as a long list, so they are folded
        /// in here rather than checked again wherever a line is about to be said.
        ///
        /// Reactions at Never spares Idle deliberately. Between them the two dials can
        /// then say "only mutter to yourselves" and "only speak when something happens",
        /// and neither is reachable any other way - the master switch turns off the lot.
        /// </remarks>
        internal static IReadOnlyList<ChatterEvent> Silenced(
            ChatterAmount reactions, ChatterAmount idle, IReadOnlyList<ChatterEvent> named)
        {
            List<ChatterEvent> off = named == null ? [] : [.. named];

            if (idle == ChatterAmount.Never && !off.Contains(ChatterEvent.Idle))
            {
                off.Add(ChatterEvent.Idle);
            }

            if (reactions == ChatterAmount.Never)
            {
                foreach (ChatterEvent kind in Enum.GetValues(typeof(ChatterEvent)))
                {
                    if (kind != ChatterEvent.Idle && !off.Contains(kind))
                    {
                        off.Add(kind);
                    }
                }
            }

            return off;
        }

        /// <summary>Whether this could be an event name at all.</summary>
        /// <returns>False for anything holding a digit or a sign.</returns>
        /// <param name="name">One trimmed piece of the player's list.</param>
        private static bool LooksLikeAName(string name)
        {
            for (int i = 0; i < name.Length; i++)
            {
                if (!char.IsLetter(name[i]))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
