using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ChattyBones
{
    /// <summary>How a line gets drawn over a skeleton's head.</summary>
    /// <remarks>
    /// Two ways to do this, and we prefer the private one.
    ///
    /// <c>Chat.AddInworldText</c> draws the floating chat text that follows a
    /// character's head, which is exactly the look we want. It is private, so we
    /// reach it with AccessTools once at startup. Reflection is an advantage here
    /// rather than a cost: we learn at load time whether a game update has moved it,
    /// instead of throwing in the middle of a fight.
    ///
    /// <c>Chat.SetNpcText</c> is public and safe, but draws the Haldor dialogue
    /// panel and sits at a fixed offset instead of tracking the head. It is the
    /// fallback, and <see cref="ModConfig.Bubble"/> can force it.
    ///
    /// Both are local UI. Nothing here reaches another player's screen - that is
    /// Phase 6's problem.
    /// </remarks>
    internal static class Speech
    {
        /// <summary>Mixed into the sender id so ours cannot look like a player's.</summary>
        /// <remarks>
        /// Chat keys its bubbles by sender, and a real one is a platform user id. A
        /// collision would mean a skeleton stealing a player's bubble for a few
        /// seconds - not serious, but free to avoid.
        /// </remarks>
        private const long SenderSalt = 0x43_48_41_54_54_59L;

        private static MethodInfo _addInworldText;
        private static bool _resolved;

        /// <summary>Find the private method once, and say in the log what we found.</summary>
        /// <remarks>
        /// Called from <see cref="ChattyBonesPlugin.Awake"/>. The log line matters:
        /// "why do my skeletons look like Haldor" is otherwise a mystery, and this
        /// turns it into one grep.
        /// </remarks>
        internal static void Resolve()
        {
            _resolved = true;

            _addInworldText = AccessTools.Method(
                typeof(Chat),
                "AddInworldText",
                [typeof(GameObject), typeof(long), typeof(Vector3), typeof(Talker.Type), typeof(UserInfo), typeof(string)]);

            if (_addInworldText == null)
            {
                ChattyBonesPlugin.Log.LogWarning(
                    "Chat.AddInworldText not found - a game update has probably moved it. " +
                    "Falling back to the dialogue panel, which works but looks like Haldor.");
            }
        }

        /// <summary>Make a skeleton say something.</summary>
        /// <param name="speaker">Whoever is talking.</param>
        /// <param name="speakerName">Its name, used only by the dialogue panel.</param>
        /// <param name="line">Finished text, tokens already filled in.</param>
        /// <remarks>
        /// Silently does nothing when there is no Chat yet, which is every frame
        /// before the world loads.
        /// </remarks>
        internal static void Say(Character speaker, string speakerName, string line)
        {
            if (speaker == null || string.IsNullOrEmpty(line))
            {
                return;
            }

            Chat chat = Chat.instance;
            if (chat == null)
            {
                return;
            }

            if (!_resolved)
            {
                Resolve();
            }

            if (ModConfig.Bubble.Value == BubbleStyle.FloatingText && TryFloatingText(chat, speaker, line))
            {
                return;
            }

            ShowPanel(chat, speaker, speakerName, line);
        }

        /// <summary>Draw the floating chat text, if we can.</summary>
        /// <returns>False if the method is missing or threw, so the caller falls back.</returns>
        /// <param name="chat">The live Chat instance.</param>
        /// <param name="speaker">Whoever is talking.</param>
        /// <param name="line">Finished text.</param>
        /// <remarks>
        /// Talker.Type.Normal on purpose. Chat only prefixes the speaker's name for
        /// Shout and Ping, and Shout also uppercases and colours the text yellow -
        /// so Normal gives a plain white line, which is what a bubble should be. A
        /// pack that wants the name in the text can use {name}.
        ///
        /// A failure here disables the path rather than retrying every line. If it
        /// broke once it will break every time, and a per-line exception in a Harmony
        /// hook is a good way to make the whole mod look broken.
        /// </remarks>
        private static bool TryFloatingText(Chat chat, Character speaker, string line)
        {
            if (_addInworldText == null)
            {
                return false;
            }

            try
            {
                _ = _addInworldText.Invoke(
                    chat,
                    [speaker.gameObject, SenderIdFor(speaker), speaker.GetHeadPoint(), Talker.Type.Normal, new UserInfo(), line]);

                return true;
            }
            catch (Exception e)
            {
                ChattyBonesPlugin.Log.LogWarning("Chat.AddInworldText threw, falling back to the dialogue panel from now on: " + e.Message);
                _addInworldText = null;
                return false;
            }
        }

        /// <summary>Draw the dialogue panel instead.</summary>
        /// <param name="chat">The live Chat instance.</param>
        /// <param name="speaker">Whoever is talking.</param>
        /// <param name="speakerName">Shown as the panel's topic.</param>
        /// <param name="line">Finished text.</param>
        /// <remarks>
        /// The offset is a guess at head height, because unlike the floating text
        /// this one is placed once and does not follow the skeleton.
        /// </remarks>
        private static void ShowPanel(Chat chat, Character speaker, string speakerName, string line)
        {
            chat.SetNpcText(
                speaker.gameObject,
                Vector3.up * 1.5f,
                ModConfig.PanelCullDistance.Value,
                ModConfig.PanelSeconds.Value,
                speakerName ?? string.Empty,
                line,
                large: false);
        }

        /// <summary>A stable id for this skeleton, for Chat to key its bubble by.</summary>
        /// <param name="speaker">Whoever is talking.</param>
        /// <returns>The same value every time for the same skeleton.</returns>
        /// <remarks>
        /// Chat replaces an existing bubble when the sender matches, which is what we
        /// want per skeleton and emphatically not what we want across the squad.
        ///
        /// A ZDOID is a (user, counter) pair and does not fit in a long, so this is a
        /// mix rather than a packing. Collisions only matter between two skeletons
        /// alive at the same moment, of which there are a handful.
        /// </remarks>
        private static long SenderIdFor(Character speaker)
        {
            ZDOID id = speaker.GetZDOID();

            return unchecked((id.UserID * 1099511628211L) ^ id.ID ^ SenderSalt);
        }
    }
}
