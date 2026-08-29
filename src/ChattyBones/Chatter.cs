using System.Collections.Generic;
using ChattyBones.Logic;
using UnityEngine;

namespace ChattyBones
{
    /// <summary>
    /// The squad's shared voice: what it may say, and whether it may say it now.
    /// </summary>
    /// <remarks>
    /// One budget, one chooser and one pack for every skeleton you have out. Both
    /// pieces of memory only work that way round - a per-skeleton chooser would let
    /// five of them say the same line in turn, and a per-skeleton budget would let
    /// them all shout at once, which is the thing the budget exists to stop.
    ///
    /// The hooks do not talk to <see cref="Speech"/> directly. Everything goes
    /// through <see cref="TrySpeak"/>, so the ask-then-book ordering is written down
    /// once here rather than trusted to a dozen call sites.
    /// </remarks>
    internal static class Chatter
    {
        private static ChatterBudget _budget;
        private static LineChooser _chooser;
        private static LinePack _pack;
        // Qualified because UnityEngine.Random is also in scope here. LineChooser
        // wants the System one - it is handed a seeded instance in the tests.
        private static System.Random _random;

        /// <summary>Seconds of game time left to run before the sweep fires again.</summary>
        private static float _untilSweep;

        /// <summary>How often we look at what the skeletons are doing.</summary>
        /// <remarks>
        /// Four times a second. Target changes and kills have no event to hang off -
        /// see <see cref="ChatterEvent.Killed"/> - so they are found by looking, and
        /// this is the rate at which we look. Fast enough that a line lands while the
        /// moment is still the moment, slow enough that the cost is a handful of
        /// field reads per skeleton per quarter second.
        /// </remarks>
        private const float SweepSeconds = 0.25f;

        /// <summary>Build the pack and the budget. Called once from Awake.</summary>
        internal static void Init()
        {
            _pack = DefaultPack.Build();
            _chooser = new LineChooser();
            _budget = new ChatterBudget(SettingsFromConfig());
            _random = new System.Random();

            ChattyBonesPlugin.Log.LogInfo(
                "Line pack loaded with " + _pack.Personalities.Count + " personalities.");
        }

        /// <summary>The personalities a skeleton can be assigned, in a stable order.</summary>
        internal static IReadOnlyList<string> Personalities => _pack.Personalities;

        /// <summary>Read the current config into a fresh settings object.</summary>
        /// <returns>Settings matching what the player has set right now.</returns>
        /// <remarks>
        /// A new object every time rather than editing the one in force, because
        /// BepInEx raises SettingChanged off the main thread - see the note on
        /// <see cref="ChatterSettings"/>.
        /// </remarks>
        private static ChatterSettings SettingsFromConfig()
        {
            return new ChatterSettings(
                minGapSeconds: ModConfig.MinGapSeconds.Value,
                preemptGapSeconds: ModConfig.PreemptGapSeconds.Value,
                speakerCooldownSeconds: ModConfig.SpeakerCooldownSeconds.Value,
                squadEchoWindowSeconds: ModConfig.SquadEchoWindowSeconds.Value);
        }

        /// <summary>Pick up a config change without a restart.</summary>
        internal static void RefreshSettings()
        {
            if (_budget != null)
            {
                _budget.Settings = SettingsFromConfig();
            }
        }

        /// <summary>Have a skeleton react to something, if it is allowed to and has words for it.</summary>
        /// <returns>True if a line actually appeared on screen.</returns>
        /// <param name="speaker">Which of ours is reacting.</param>
        /// <param name="kind">What happened.</param>
        /// <param name="subject">
        /// A prefab hash for whatever the remark is about, or 0 when it is not about
        /// anything. Never an instance id - see <see cref="ChatterBudget.CanClaim"/>.
        /// </param>
        /// <param name="targetName">
        /// Already localised, and resolved by the caller rather than in here. Killed
        /// is why: by the time a skeleton gets to gloat, the thing it killed has often
        /// been destroyed and replaced with a ragdoll, so the name has to be taken
        /// while there is still something to take it from.
        /// </param>
        /// <param name="companion">Another of your skeletons, for lines about each other.</param>
        /// <remarks>
        /// Three ways to come back false, and all three are ordinary: the budget said
        /// no, the pack had nothing sayable, or the world is not in a state to draw.
        /// Nothing is queued for later in any of those cases, deliberately - see
        /// <see cref="ChatterBudget.CanClaim"/>.
        ///
        /// The order below is the whole point of the method. Asking books nothing, so
        /// a caller that asked on behalf of two skeletons before resolving either
        /// would get two yeses and blow through the squad gap. Here, one call is one
        /// resolved claim.
        ///
        /// Committing last, after <see cref="Speech.Say"/> has confirmed something was
        /// drawn, means a line that failed to render does not spend the squad's quiet
        /// time. The skeleton simply stays eligible.
        /// </remarks>
        internal static bool TrySpeak(
            ChatterComponent speaker,
            ChatterEvent kind,
            int subject,
            string targetName,
            Character companion)
        {
            if (!ModConfig.Enabled.Value || speaker == null || _budget == null)
            {
                return false;
            }

            // The ownership test lives here rather than in each hook, because a Harmony
            // postfix runs whichever way the patched method returned. RPC_Damage bails
            // out early on a non-owner, but our postfix does not - so on a shared world
            // one skeleton being hit reaches this code once per player who can see it,
            // and all but one of those has to come to nothing.
            if (!speaker.IsOwned)
            {
                return false;
            }

            Character character = speaker.Character;
            if (character == null)
            {
                return false;
            }

            long speakerId = speaker.SpeakerId;
            float now = Time.time;

            if (!_budget.CanClaim(speakerId, kind, subject, now))
            {
                return false;
            }

            // Built only once the budget has said yes. Summons.NameOf costs two ZDO
            // reads and a filter pass, and the refusal path is much the busier one.
            LineTokens tokens = new(
                target: targetName,
                player: Player.m_localPlayer == null ? null : Player.m_localPlayer.GetPlayerName(),
                name: Summons.NameOf(character),
                companion: companion == null ? null : Summons.NameOf(companion));

            if (!_chooser.TryChoose(_pack, speaker.Personality, kind, tokens, _random, out int lineRef, out string line))
            {
                return false;
            }

            if (Speech.Say(character, line) == Drew.Nothing)
            {
                return false;
            }

            _budget.Commit(speakerId, kind, subject, now);
            speaker.OnSpoke(kind, lineRef, subject);

            return true;
        }

        /// <summary>Let whichever skeleton is willing react to something that happened nearby.</summary>
        /// <returns>True if one of them spoke.</returns>
        /// <param name="kind">What happened.</param>
        /// <param name="subject">A prefab hash, or 0.</param>
        /// <param name="targetName">Already localised, or null.</param>
        /// <param name="companion">The skeleton the remark is about, for CompanionHurt. Never the speaker.</param>
        /// <remarks>
        /// Used by the events that happen to you or to the world rather than to one
        /// particular skeleton - you took a hit, you landed one, somebody's colleague
        /// got clobbered. Somebody should say something and it does not much matter who.
        ///
        /// We ask each in turn and stop at the first yes, which is the ask-then-book
        /// rule again: asking books nothing, so asking all five first and then picking
        /// one would have got five yeses and taught us nothing about which of them was
        /// actually free.
        ///
        /// The starting point rotates. Without it the loop always begins at the same
        /// end of the list, and since the speaker cooldown is the usual reason for a
        /// refusal, the skeleton you summoned first would do a noticeably large share
        /// of the talking.
        /// </remarks>
        internal static bool SpeakAny(ChatterEvent kind, int subject, string targetName, Character companion)
        {
            List<ChatterComponent> squad = ChatterComponent.All;
            int count = squad.Count;
            if (count == 0)
            {
                return false;
            }

            // Masked to stay positive. int.MaxValue is about four hours of asking at
            // the sweep rate, and a negative remainder is a negative index.
            int start = (_nextSpeaker++ & int.MaxValue) % count;

            for (int offset = 0; offset < count; offset++)
            {
                ChatterComponent speaker = squad[(start + offset) % count];

                if (companion != null && speaker.Character == companion)
                {
                    // Nobody commiserates with themselves.
                    continue;
                }

                if (TrySpeak(speaker, kind, subject, targetName, companion))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Rotates so the same skeleton is not always asked first. Wraps harmlessly.</summary>
        private static int _nextSpeaker;

        /// <summary>Look at what the squad is doing, and let one of them comment.</summary>
        /// <param name="dt">Seconds since the last frame.</param>
        /// <remarks>
        /// Driven from the plugin's Update rather than from a MonoBehaviour on each
        /// skeleton. One loop in a known order is what makes the ask-then-book rule
        /// easy to keep: with an Update per skeleton, the order is Unity's, and five
        /// of them would each ask before any of them had booked.
        ///
        /// The sweep looks for the two things that have no event to hook - a target
        /// appearing, and a target dying - and runs the idle timer.
        /// </remarks>
        internal static void Tick(float dt)
        {
            if (!ModConfig.Enabled.Value)
            {
                return;
            }

            _untilSweep -= dt;
            if (_untilSweep > 0f)
            {
                return;
            }

            float elapsed = SweepSeconds - _untilSweep;
            _untilSweep = SweepSeconds;

            List<ChatterComponent> squad = ChatterComponent.All;
            for (int i = 0; i < squad.Count; i++)
            {
                squad[i].Sweep(elapsed);
            }
        }
    }
}
