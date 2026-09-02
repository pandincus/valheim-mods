using System.Text;

namespace ChattyBones.Logic
{
    /// <summary>
    /// How a <see cref="LineDetails"/> travels to the other players who can see the
    /// skeleton.
    /// </summary>
    /// <remarks>
    /// <see cref="Utterance"/> carries which line was said; this carries what it was
    /// said about. Two fields rather than one because the utterance is three small
    /// numbers packed tight and this is text, and squeezing text into an int is a job
    /// with no happy ending.
    ///
    /// Text, and not the finished words. Everything in a LineDetails is either a
    /// localization key the game already ships - "$item_sword_iron", "$se_burning" -
    /// or one of the mod's own English words like "sword". Both are the same on every
    /// machine, so each client localizes for itself and a German player reads
    /// "Nebelwandler" from the same broadcast that gave you "Mistwalker". That is the
    /// property <see cref="LineTokens"/> already relies on for {target}, arrived at a
    /// different way: a prefab hash there, a localization key here.
    ///
    /// I did weigh the tidier version, which is prefab hashes in two more int fields
    /// with the enums packed alongside. It is a third of the bytes and it needs
    /// ObjectDB lookups that can miss, an enum for the damage types, and a wire format
    /// that has to be revised every time an event learns a new token. This is a
    /// delimited string that costs about forty bytes an utterance and grows by adding
    /// a field to the end. At the rate a squad speaks, the bytes are not worth the
    /// ceremony.
    /// </remarks>
    internal static class DetailWire
    {
        /// <summary>What separates one field from the next.</summary>
        /// <remarks>
        /// Safe against everything that legitimately turns up in these fields -
        /// localization keys are identifiers and the mod's own words are from two
        /// fixed tables in <c>Hits</c> and <see cref="DamageKind"/>. <see cref="Pack"/>
        /// drops a field carrying one anyway rather than writing a record that reads
        /// back as a different shape.
        /// </remarks>
        private const char Separator = '|';

        /// <summary>Write the details out for the other clients.</summary>
        /// <returns>The record, or an empty string when there is nothing to say about it.</returns>
        /// <param name="details">What the hook worked out, holding keys rather than words.</param>
        /// <remarks>
        /// Empty rather than null, because it is going straight into a ZDO string
        /// field and a listener needs "the last thing said had no details" to overwrite
        /// "the thing before it did". A stale weapon name on an idle remark would be a
        /// strange thing to debug.
        ///
        /// A new field goes on the end, and the two halves then disagree harmlessly in
        /// both directions: an older reader stops at the fields it knows, and a newer
        /// one finds the tail missing and leaves those null. That is the same "quietly
        /// react to less" the packed int already has for an event it does not
        /// recognize - see <see cref="Utterance.TryUnpack"/>.
        /// </remarks>
        internal static string Pack(LineDetails details)
        {
            string[] parts =
            [
                details.Weapon,
                details.WeaponType,
                details.Damage,
                details.Status,
                details.Biome,
                details.Item,
                details.Skill,
            ];

            int last = -1;
            for (int i = 0; i < parts.Length; i++)
            {
                if (!Usable(parts[i]))
                {
                    parts[i] = null;
                    continue;
                }

                last = i;
            }

            if (last < 0)
            {
                return string.Empty;
            }

            StringBuilder sb = new(64);
            for (int i = 0; i <= last; i++)
            {
                if (i > 0)
                {
                    sb.Append(Separator);
                }

                sb.Append(parts[i]);
            }

            return sb.ToString();
        }

        /// <summary>Read a record another client wrote.</summary>
        /// <returns>False when there was nothing in the field, which is the usual case.</returns>
        /// <param name="packed">The string out of the ZDO. Null and empty are both fine.</param>
        /// <param name="details">What it said, with nulls for the fields it left out.</param>
        /// <remarks>
        /// Never throws and never rejects. Anything unrecognizable comes back as
        /// fields we have no value for, and a line wanting one of them is passed over
        /// by <see cref="LineTokens.TryRender"/> - so a garbled record costs a
        /// listener the odd line rather than an exception in the middle of a fight.
        /// </remarks>
        internal static bool TryUnpack(string packed, out LineDetails details)
        {
            details = default;

            if (string.IsNullOrEmpty(packed))
            {
                return false;
            }

            string[] parts = packed.Split(Separator);

            details = new LineDetails(
                weapon: At(parts, 0),
                weaponType: At(parts, 1),
                damage: At(parts, 2),
                status: At(parts, 3),
                biome: At(parts, 4),
                item: At(parts, 5),
                skill: At(parts, 6));

            return true;
        }

        /// <summary>One field out of a record, if it is there at all.</summary>
        /// <returns>The value, or null for a field past the end or left empty.</returns>
        /// <param name="parts">The split record.</param>
        /// <param name="index">Which field.</param>
        private static string At(string[] parts, int index)
        {
            if (index >= parts.Length || parts[index].Length == 0)
            {
                return null;
            }

            return parts[index];
        }

        /// <summary>Can this value go on the wire as it stands?</summary>
        /// <returns>False for nothing to send, and for anything that would break the record.</returns>
        /// <param name="value">One field.</param>
        private static bool Usable(string value)
        {
            return !string.IsNullOrEmpty(value) && value.IndexOf(Separator) < 0;
        }
    }
}
