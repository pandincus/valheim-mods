using System;
using System.Reflection;
using ChattyBones.Logic;
using HarmonyLib;
using UnityEngine;

namespace ChattyBones
{
    /// <summary>What actually appeared, if anything.</summary>
    /// <remarks>
    /// Returned so the debug commands can tell "the line drew" from "the line was
    /// refused", which is the entire question they exist to answer.
    /// </remarks>
    internal enum Drew
    {
        /// <summary>Nothing was drawn.</summary>
        Nothing,

        /// <summary>The floating chat text that follows the head.</summary>
        FloatingText,

        /// <summary>The trader-style dialogue panel.</summary>
        DialoguePanel,
    }

    /// <summary>How a line gets drawn over a skeleton's head.</summary>
    /// <remarks>
    /// Two ways to do this, and I prefer the private one.
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
        /// <summary>Name of the empty child we hang the text from.</summary>
        private const string AnchorName = "ChattyBonesSpeechAnchor";

        private static readonly ColourTagCache Colours = new();

        private static MethodInfo _addInworldText;

        /// <summary>Set when the invoke has thrown, so we stop trying it.</summary>
        /// <remarks>
        /// Deliberately separate from <see cref="_addInworldText"/> being null, which
        /// means "never found it". Two different reasons to be on the panel, and
        /// conflating them in one field is how a later "re-resolve on config reload"
        /// would quietly resurrect a path that was killed on purpose.
        /// </remarks>
        private static bool _floatingTextDisabled;

        /// <summary>Find the private method once, and warn if it has moved.</summary>
        /// <remarks>
        /// Called from <see cref="ChattyBonesPlugin.Awake"/>. The warning is worth
        /// having: otherwise "why do my skeletons look like Haldor" has no visible
        /// cause anywhere.
        /// </remarks>
        internal static void Resolve()
        {
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
        /// <returns>Which of the two drew it, or <see cref="Drew.Nothing"/>.</returns>
        /// <param name="speaker">Whoever is talking.</param>
        /// <param name="line">Finished text, tokens already filled in.</param>
        /// <param name="packTag">The colour the pack asked for this event, or null.</param>
        /// <remarks>
        /// This is the one door every caller comes through, which makes it the right
        /// place for the catch. The event hooks sit inside vanilla damage and status
        /// handling, and an exception escaping one of those does not stay our problem
        /// for long.
        ///
        /// Nothing drawn is an ordinary answer rather than a failure: there is no Chat
        /// before the world loads, and the player may simply have switched the mod off.
        /// </remarks>
        internal static Drew Say(Character speaker, string line, string packTag = null)
        {
            if (!ModConfig.Enabled.Value || speaker == null || string.IsNullOrEmpty(line))
            {
                return Drew.Nothing;
            }

            Chat chat = Chat.instance;
            if (chat == null)
            {
                return Drew.Nothing;
            }

            try
            {
                string coloured = Colourise(line, packTag);

                return ModConfig.Bubble.Value == BubbleStyle.FloatingText && TryFloatingText(chat, speaker, coloured)
                    ? Drew.FloatingText
                    : ShowPanel(chat, speaker, coloured);
            }
            catch (Exception e)
            {
                ChattyBonesPlugin.Log.LogWarning("Could not draw a line for a skeleton: " + e);
                return Drew.Nothing;
            }
        }

        /// <summary>Wrap the line in a colour tag, if anything has an opinion about one.</summary>
        /// <returns>The line, possibly wrapped.</returns>
        /// <param name="line">The finished text.</param>
        /// <param name="packTag">What the pack wants for this event, or null.</param>
        /// <remarks>
        /// The config wins over the pack. A palette is a statement about a pack, so it
        /// is the right default; TextColour is the escape hatch for somebody who wants
        /// one colour and no argument.
        /// </remarks>
        private static string Colourise(string line, string packTag)
        {
            string configured = ModConfig.TextColour.Value;

            if (Colours.TryTagFor(configured, out string tag, out bool newlyRejected))
            {
                return SpeechFormat.Wrap(line, tag);
            }

            if (newlyRejected)
            {
                ChattyBonesPlugin.Log.LogWarning(
                    "TextColour '" + configured + "' is not a hex code like #C8FFC8, so it is being ignored.");
            }

            return SpeechFormat.Wrap(line, packTag);
        }

        /// <summary>Draw the floating chat text, if we can.</summary>
        /// <returns>False if the method is missing or has already thrown once.</returns>
        /// <param name="chat">The live Chat instance.</param>
        /// <param name="speaker">Whoever is talking.</param>
        /// <param name="line">Finished text.</param>
        /// <remarks>
        /// Talker.Type.Normal on purpose. Chat only prefixes the speaker's name for
        /// Shout and Ping, and Shout also uppercases and colours the text yellow - so
        /// Normal gives a plain white line, which is what a bubble should be. A pack
        /// that wants the name in the text can use {name}.
        ///
        /// The arguments are built before the try, and that placement is the point.
        /// Everything in there can fail for reasons local to one skeleton - a creature
        /// with no mapped head bone has a null m_head, and a speaker destroyed this
        /// frame makes Transform.Find throw - and none of that means the reflection is
        /// broken. Inside the try, each would kill the good path for the rest of the
        /// session. Outside it, they reach <see cref="Say"/>'s catch, cost one line,
        /// and we carry on.
        /// </remarks>
        private static bool TryFloatingText(Chat chat, Character speaker, string line)
        {
            if (_addInworldText == null || _floatingTextDisabled)
            {
                return false;
            }

            object[] arguments =
            [
                AnchorFor(speaker),
                SenderIdFor(speaker),
                speaker.GetHeadPoint(),
                Talker.Type.Normal,
                new UserInfo(),
                line,
            ];

            try
            {
                _ = _addInworldText.Invoke(chat, arguments);
                return true;
            }
            catch (Exception e)
            {
                // Invoke wraps whatever the method threw, and the wrapper's own message
                // is boilerplate, so unwrap it or the log line explains nothing.
                _floatingTextDisabled = true;
                ChattyBonesPlugin.Log.LogWarning(
                    "Chat.AddInworldText threw, so the dialogue panel takes over from here: " + (e.InnerException ?? e));

                return false;
            }
        }

        /// <summary>Draw the dialogue panel instead.</summary>
        /// <returns>Always <see cref="Drew.DialoguePanel"/>.</returns>
        /// <param name="chat">The live Chat instance.</param>
        /// <param name="speaker">Whoever is talking.</param>
        /// <param name="line">Finished text.</param>
        /// <remarks>
        /// 1.5m is a guess at skeleton head height. Unlike the floating text this is
        /// placed once and never re-evaluated, so there is nothing to measure it
        /// against.
        ///
        /// The name is resolved here rather than passed in, because it costs two ZDO
        /// reads and a filter pass, and the path everybody actually uses never wants it.
        /// </remarks>
        private static Drew ShowPanel(Chat chat, Character speaker, string line)
        {
            chat.SetNpcText(
                speaker.gameObject,
                Vector3.up * 1.5f,
                ModConfig.DialoguePanelCullDistance.Value,
                ModConfig.DialoguePanelSeconds.Value,
                Summons.NameOf(speaker) ?? string.Empty,
                line,
                large: false);

            return Drew.DialoguePanel;
        }

        /// <summary>Which object the text should hang from.</summary>
        /// <returns>An empty child above the skeleton's head, or the skeleton itself.</returns>
        /// <param name="speaker">Whoever is talking.</param>
        /// <remarks>
        /// The position we pass to AddInworldText is thrown away for as long as the
        /// object it is anchored to exists. UpdateWorldTexts recomputes it every
        /// frame, and for anything with a Character on it that means
        /// <c>GetHeadPoint() + 0.3</c> - which lands on top of the name label.
        ///
        /// Only for as long as it exists, mind, and that turns out to matter. Once
        /// the anchor is destroyed, Chat falls back to the position we passed and
        /// keeps drawing there - so a skeleton's last words survive it, hanging where
        /// it fell. That makes <c>GetHeadPoint()</c> the right thing to send even
        /// though it is ignored almost every time it is sent.
        ///
        /// The way out is the other branch of that same line: an object *without* a
        /// Character is drawn at its own transform position instead. So I hang the
        /// text on an empty child parented above the head. It still follows the
        /// skeleton, because the child moves with it, and we choose the height - on
        /// top of Chat's own 0.3, which it adds either way.
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

            // Re-measured every time, so a new TextHeight in ConfigurationManager moves
            // the text on the next line rather than the next summon.
            float headHeight = speaker.GetHeadPoint().y - speaker.transform.position.y;
            anchor.transform.localPosition = new Vector3(0f, headHeight + extra, 0f);

            return anchor;
        }

        /// <summary>A stable id for this skeleton, for Chat to key its bubble by.</summary>
        /// <returns>The same value every time for the same skeleton.</returns>
        /// <param name="speaker">Whoever is talking.</param>
        private static long SenderIdFor(Character speaker)
        {
            ZDOID id = speaker.GetZDOID();

            return SpeechFormat.SenderId(id.UserID, id.ID);
        }
    }
}
