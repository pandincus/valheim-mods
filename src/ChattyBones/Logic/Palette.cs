using System.Collections.Generic;

namespace ChattyBones.Logic
{
    /// <summary>
    /// What colour each kind of event is drawn in.
    /// </summary>
    /// <remarks>
    /// Keyed on the event, not on the speaker - the pack header says why, in the
    /// words a pack author needs. Hex codes become TextMeshPro tags here, once,
    /// rather than on every line spoken.
    /// </remarks>
    internal sealed class Palette
    {
        private readonly Dictionary<ChatterEvent, string> _tags;
        private readonly string _fallback;

        /// <summary>Turn a pack's colours into ready-to-use tags.</summary>
        /// <param name="fallbackHex">The colour for events not named below, or null for the game's own.</param>
        /// <param name="byEvent">Per-event colours, or null for none.</param>
        /// <remarks>
        /// Anything that is not a hex code is dropped and that event falls back. The
        /// reader has already checked the same values and named the line, so nothing
        /// is lost silently.
        /// </remarks>
        internal Palette(string fallbackHex, IReadOnlyDictionary<ChatterEvent, string> byEvent)
        {
            _ = SpeechFormat.TryColourTag(fallbackHex, out _fallback);
            _tags = [];

            if (byEvent == null)
            {
                return;
            }

            foreach (KeyValuePair<ChatterEvent, string> entry in byEvent)
            {
                if (SpeechFormat.TryColourTag(entry.Value, out string tag))
                {
                    _tags[entry.Key] = tag;
                }
            }
        }

        /// <summary>The opening colour tag for an event.</summary>
        /// <returns>A tag like <c>&lt;color=#F0A9A0&gt;</c>, or null to leave the line alone.</returns>
        /// <param name="kind">What is being reacted to.</param>
        internal string TagFor(ChatterEvent kind)
        {
            return _tags.TryGetValue(kind, out string tag) ? tag : _fallback;
        }
    }
}
