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
    /// reach it with AccessTools once at startup rather than per line - which means
    /// a game update that moves it shows up as a warning while the game is still
    /// loading, instead of as an exception mid-fight.
    ///
    /// <c>Chat.SetNpcText</c> is public and safe, but draws the Haldor dialogue
    /// panel and sits at a fixed offset instead of tracking the head. I kept it as
    /// the fallback anyway - a mod that looks wrong is a far better outcome than one
    /// that throws - and <see cref="ModConfig.Bubble"/> can force it.
    ///
    /// Both are local UI: nothing here reaches another player's screen.
    /// </remarks>
    internal static class Speech
    {
        /// <summary>Mixed into the sender id so ours are unlikely to land on a player's.</summary>
        /// <remarks>
        /// Chat keys its bubbles by sender, and a real one is a platform user id. A
        /// collision would mean a skeleton stealing a player's bubble for a few
        /// seconds - not serious, but free to avoid.
        ///
        /// (The bytes spell CHATTY. That is not doing anything, but it did amuse me.)
        /// </remarks>
        private const long SenderSalt = 0x43_48_41_54_54_59L;

        /// <summary>Name of the empty child we hang the text from.</summary>
        private const string AnchorName = "ChattyBonesSpeechAnchor";

        private static MethodInfo _addInworldText;
        private static bool _resolved;

        /// <summary>Last colour we validated, so a bad one is only complained about once.</summary>
        private static string _checkedColour;
        private static string _colourTag;

        /// <summary>Find the private method once, and warn if it has moved.</summary>
        /// <remarks>
        /// Called from <see cref="ChattyBonesPlugin.Awake"/>. The warning is worth
        /// having: otherwise "why do my skeletons look like Haldor" has no visible
        /// cause anywhere.
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

            string coloured = Colourise(line);

            if (ModConfig.Bubble.Value == BubbleStyle.FloatingText && TryFloatingText(chat, speaker, coloured))
            {
                return;
            }

            ShowPanel(chat, speaker, speakerName, coloured);
        }

        /// <summary>Wrap the line in a TextMeshPro colour tag, if one is configured.</summary>
        /// <returns>The line, possibly wrapped. Unchanged when no colour is set.</returns>
        /// <param name="line">The finished text.</param>
        /// <remarks>
        /// Both places we draw into are TextMeshProUGUI, so rich text works. It
        /// survives because we call AddInworldText directly and skip
        /// OnNewChatMessage, which is what strips angle brackets out of player chat.
        ///
        /// A bad hex code reaches the screen as literal text - "#GGG" would appear
        /// over a skeleton's head as &lt;color=#GGG&gt; - so it is checked once per
        /// distinct value and complained about in the log instead.
        /// </remarks>
        private static string Colourise(string line)
        {
            string wanted = ModConfig.TextColour.Value;

            if (string.IsNullOrWhiteSpace(wanted))
            {
                return line;
            }

            if (wanted != _checkedColour)
            {
                _checkedColour = wanted;
                _colourTag = TryBuildTag(wanted);
            }

            return _colourTag == null ? line : _colourTag + line + "</color>";
        }

        /// <summary>Turn a configured hex code into an opening colour tag.</summary>
        /// <returns>The opening tag, or null if it was not a hex colour.</returns>
        /// <param name="wanted">Whatever the player typed.</param>
        private static string TryBuildTag(string wanted)
        {
            string hex = wanted.Trim().TrimStart('#');
            bool lengthOk = hex.Length is 3 or 6 or 8;

            for (int i = 0; lengthOk && i < hex.Length; i++)
            {
                if (!Uri.IsHexDigit(hex[i]))
                {
                    lengthOk = false;
                }
            }

            if (!lengthOk)
            {
                ChattyBonesPlugin.Log.LogWarning(
                    "TextColour '" + wanted + "' is not a hex code like #C8FFC8, so it is being ignored.");
                return null;
            }

            return "<color=#" + hex + ">";
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
        /// A failure here disables the path rather than retrying every line. If the
        /// invoke throws once it will throw every time, and the dialogue panel still
        /// draws something.
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
                    [AnchorFor(speaker), SenderIdFor(speaker), speaker.GetHeadPoint(), Talker.Type.Normal, new UserInfo(), line]);

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
        /// 1.5m is a guess at skeleton head height. Unlike the floating text this is
        /// placed once and never re-evaluated, so there is nothing to measure it
        /// against.
        /// </remarks>
        private static void ShowPanel(Chat chat, Character speaker, string speakerName, string line)
        {
            chat.SetNpcText(
                speaker.gameObject,
                Vector3.up * 1.5f,
                ModConfig.DialoguePanelCullDistance.Value,
                ModConfig.DialoguePanelSeconds.Value,
                speakerName ?? string.Empty,
                line,
                large: false);
        }

        /// <summary>Which object the text should hang from.</summary>
        /// <returns>An empty child above the skeleton's head, or the skeleton itself.</returns>
        /// <param name="speaker">Whoever is talking.</param>
        /// <remarks>
        /// The position we pass to AddInworldText is thrown away. UpdateWorldTexts
        /// recomputes it every frame, and for anything with a Character on it that
        /// means <c>GetHeadPoint() + 0.3</c> - which lands on top of the name label.
        ///
        /// The way out is the other branch of that same line: an object *without* a
        /// Character is drawn at its own transform position instead. So I hang the
        /// text on an empty child parented above the head. It still follows the
        /// skeleton, because the child moves with it, and we choose the height.
        ///
        /// Parented to the root rather than the head bone, so the text does not bob
        /// with the walk animation. Skeletons only rotate about Y, so a straight-up
        /// local offset stays straight up.
        /// </remarks>
        private static GameObject AnchorFor(Character speaker)
        {
            float extra = ModConfig.TextHeight.Value;
            if (extra <= 0f)
            {
                return speaker.gameObject;
            }

            Transform existing = speaker.transform.Find(AnchorName);
            GameObject anchor;

            if (existing == null)
            {
                anchor = new GameObject(AnchorName);
                anchor.transform.SetParent(speaker.transform, worldPositionStays: false);
            }
            else
            {
                anchor = existing.gameObject;
            }

            // Re-measured every time, so typing a new TextHeight in ConfigurationManager
            // moves the text on the next line rather than the next summon.
            float headHeight = speaker.GetHeadPoint().y - speaker.transform.position.y;
            anchor.transform.localPosition = new Vector3(0f, headHeight + extra, 0f);

            return anchor;
        }

        /// <summary>A stable id for this skeleton, for Chat to key its bubble by.</summary>
        /// <returns>The same value every time for the same skeleton.</returns>
        /// <param name="speaker">Whoever is talking.</param>
        /// <remarks>
        /// Chat replaces an existing bubble when the sender matches. Per skeleton that
        /// is exactly right - a new line supersedes the old one. Shared across the
        /// squad it would mean five skeletons taking turns wiping out each other's.
        ///
        /// A ZDOID is a (user, counter) pair and does not fit in a long, so this is a
        /// mix rather than a packing. 1099511628211 is the FNV-1a prime; nothing
        /// depends on that beyond it being large and odd. Collisions only matter
        /// between two skeletons alive at the same moment, of which there are a
        /// handful.
        /// </remarks>
        private static long SenderIdFor(Character speaker)
        {
            ZDOID id = speaker.GetZDOID();

            return unchecked((id.UserID * 1099511628211L) ^ id.ID ^ SenderSalt);
        }
    }
}
