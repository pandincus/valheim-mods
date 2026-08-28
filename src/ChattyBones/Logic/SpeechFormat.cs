using System;

namespace ChattyBones.Logic
{
    /// <summary>The string and number work behind drawing a line, with no game types.</summary>
    /// <remarks>
    /// This lives here rather than next to the drawing code because none of it needs
    /// a running Valheim - it is hex parsing, string wrapping and a hash. Keeping it
    /// on this side of the fence means the awkward cases get tests instead of a
    /// shrug.
    /// </remarks>
    internal static class SpeechFormat
    {
        /// <summary>Turn a configured hex code into an opening TextMeshPro colour tag.</summary>
        /// <returns>False when there is no usable colour, which includes "none set".</returns>
        /// <param name="configured">Whatever the player typed. Null and blank are fine.</param>
        /// <param name="tag">The opening tag, or null.</param>
        /// <remarks>
        /// Accepts 3, 6 and 8 hex digits, with or without a leading #. TMP also
        /// understands 4-digit #RGBA, which we deliberately do not - it is easy to
        /// type by accident when you meant 3 or 6, and silently getting a
        /// transparent bubble is a worse outcome than being told the code is wrong.
        /// </remarks>
        internal static bool TryColourTag(string configured, out string tag)
        {
            tag = null;

            if (string.IsNullOrWhiteSpace(configured))
            {
                return false;
            }

            string hex = configured.Trim().TrimStart('#');
            if (hex.Length is not (3 or 6 or 8))
            {
                return false;
            }

            for (int i = 0; i < hex.Length; i++)
            {
                if (!Uri.IsHexDigit(hex[i]))
                {
                    return false;
                }
            }

            tag = "<color=#" + hex + ">";
            return true;
        }

        /// <summary>Wrap a line in a colour tag, if there is one.</summary>
        /// <returns>The wrapped line, or the original when there is no tag.</returns>
        /// <param name="line">The finished text.</param>
        /// <param name="tag">An opening tag from <see cref="TryColourTag"/>, or null.</param>
        /// <remarks>
        /// Angle brackets already in the line are left alone. That is deliberate:
        /// Valheim strips them from player chat in OnNewChatMessage, which we skip, so
        /// a pack author who wants their own markup can have it.
        /// </remarks>
        internal static string Wrap(string line, string tag)
        {
            return tag == null || line == null ? line : tag + line + "</color>";
        }

        /// <summary>A stable id for a skeleton, for Chat to key its bubble by.</summary>
        /// <returns>The same value every time for the same pair, and never 0.</returns>
        /// <param name="userId">The user half of a ZDOID - who created the object.</param>
        /// <param name="zdoId">The counter half, unique per creator.</param>
        /// <remarks>
        /// Chat replaces an existing bubble when the sender matches. Per skeleton that
        /// is exactly right - a new line supersedes the old one. Shared across the
        /// squad it would mean five skeletons taking turns wiping out each other's.
        ///
        /// A ZDOID does not fit in a long, so this is a mix rather than a packing.
        /// Multiplying the user half by a large odd number spreads it across all 64
        /// bits while the counter only touches the low 32, so two skeletons from the
        /// same creator can never collide. 1099511628211 is the FNV-1a prime; nothing
        /// depends on that beyond it being large and odd.
        ///
        /// Never 0, because a ZDOID of (0, 0) - which a creature with no valid
        /// ZNetView gives back - would otherwise be a real-looking sender id shared by
        /// every such creature.
        /// </remarks>
        internal static long SenderId(long userId, uint zdoId)
        {
            // The bytes spell CHATTY. It does nothing, but it did amuse me.
            const long salt = 0x43_48_41_54_54_59L;

            long mixed = unchecked((userId * 1099511628211L) ^ zdoId ^ salt);

            return mixed == 0L ? salt : mixed;
        }
    }

    /// <summary>Remembers the last colour we looked at, so a bad one is complained about once.</summary>
    /// <remarks>
    /// The player can retype the setting at any moment, and a bad hex code reaches
    /// the screen as literal text - "#GGG" appears over a skeleton's head as
    /// &lt;color=#GGG&gt;. So it wants a log line, and it wants exactly one of them
    /// rather than one per line spoken.
    /// </remarks>
    internal sealed class ColourTagCache
    {
        private string _seen;
        private string _tag;

        /// <summary>Get the tag for a configured value, evaluating it only when it changes.</summary>
        /// <returns>False when there is no colour to apply.</returns>
        /// <param name="configured">The current setting.</param>
        /// <param name="tag">The opening tag, or null.</param>
        /// <param name="newlyRejected">True exactly once per bad value, so the caller can log it.</param>
        internal bool TryTagFor(string configured, out string tag, out bool newlyRejected)
        {
            newlyRejected = false;

            if (string.IsNullOrWhiteSpace(configured))
            {
                tag = null;
                return false;
            }

            if (configured != _seen)
            {
                _seen = configured;
                newlyRejected = !SpeechFormat.TryColourTag(configured, out _tag);
            }

            tag = _tag;
            return tag != null;
        }
    }
}
