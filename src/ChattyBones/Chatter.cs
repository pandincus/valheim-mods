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
            _pack = PackFile.Load();
            _chooser = new LineChooser();
            _budget = new ChatterBudget(SettingsFromConfig());
            _random = new System.Random();

            ChattyBonesPlugin.Log.LogInfo(
                "Line pack loaded with " + _pack.Personalities.Count + " personalities.");
        }

        /// <summary>The personalities a skeleton can be assigned, in a stable order.</summary>
        internal static IReadOnlyList<string> Personalities => _pack.Personalities;

        /// <summary>
        /// Which pack is in force, counting up from zero. What
        /// <see cref="ChatterComponent.Personality"/> compares against to know its
        /// cached answer has gone stale.
        /// </summary>
        internal static int PackGeneration { get; private set; }

        /// <summary>Take up an edited pack file, keeping the current one if it will not parse.</summary>
        private static void ReloadPack()
        {
            LinePack reloaded = PackFile.Reload();

            if (reloaded == null)
            {
                return;
            }

            _pack = reloaded;
            PackGeneration++;

            ChattyBonesPlugin.Log.LogInfo(
                "Line pack reloaded with " + _pack.Personalities.Count + " personalities.");
        }

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
        /// Already localized, and resolved by the caller rather than in here. Killed
        /// is why: by the time a skeleton gets to gloat, the thing it killed has often
        /// been destroyed and replaced with a ragdoll, so the name has to be taken
        /// while there is still something to take it from.
        /// </param>
        /// <param name="companion">Another of your skeletons, for lines about each other.</param>
        /// <param name="companionName">
        /// The companion's name, already resolved, for when it is not around to be
        /// asked any more. Wins over <paramref name="companion"/> when supplied.
        /// </param>
        /// <param name="details">What is known about the event itself. Usually nothing.</param>
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
            Character companion,
            string companionName = null,
            LineDetails details = default)
        {
            if (!ModConfig.Enabled.Value || speaker == null || _budget == null)
            {
                return false;
            }

            // A Harmony postfix runs whichever way the patched method returned, and
            // RPC_Damage bails out early on a non-owner while ours does not. Ownership
            // can also move between an RPC being sent and arriving, which is the case
            // RPC_Damage's own owner check exists to cover.
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

            if (!_budget.CanClaim(speakerId, kind, subject, now, out ChatterRefusal why))
            {
                Trace(speaker, kind, why);
                return false;
            }

            // Built only once the budget has said yes. Summons.NameOf costs two ZDO
            // reads and a filter pass, and the refusal path is much the busier one.
            //
            // companionName wins when supplied, for a companion that is not around to
            // be asked any more - a skeleton being mourned is destroyed moments later,
            // so its name has to be taken while it is still standing.
            LineTokens tokens = new(
                target: targetName,
                player: Player.m_localPlayer == null ? null : Player.m_localPlayer.GetPlayerName(),
                name: Summons.NameOf(character),
                companion: companionName ?? (companion == null ? null : Summons.NameOf(companion)),
                details: details);

            if (!_chooser.TryChoose(_pack, speaker.Personality, kind, tokens, _random, out int lineRef, out string line))
            {
                Trace(speaker, kind, "nothing it could say as " + (speaker.Personality ?? "no personality yet"));
                return false;
            }

            if (Speech.Say(character, line, _pack.Colors.TagFor(kind)) == Drew.Nothing)
            {
                Trace(speaker, kind, "had \"" + line + "\" but nothing was drawn");
                return false;
            }

            _budget.Commit(speakerId, kind, subject, now);
            speaker.OnSpoke(kind, lineRef, subject);

            Trace(speaker, kind, "said \"" + line + "\"");

            return true;
        }

        /// <summary>Write down what became of one attempt to speak, when the player asked us to.</summary>
        /// <param name="speaker">Which skeleton was trying.</param>
        /// <param name="kind">What it was reacting to.</param>
        /// <param name="why">Which check turned it down.</param>
        /// <remarks>
        /// Its own overload because refusal is the common case by design, and the
        /// squad is asked once per skeleton - so building the message at the call site
        /// would allocate all fight for a log nobody has switched on.
        /// </remarks>
        internal static void Trace(ChatterComponent speaker, ChatterEvent kind, ChatterRefusal why)
        {
            if (ModConfig.LogChatter.Value)
            {
                Trace(speaker, kind, "turned down by " + why);
            }
        }

        /// <summary>Write down what became of one attempt to speak, when the player asked us to.</summary>
        /// <param name="speaker">Which skeleton was trying.</param>
        /// <param name="kind">What it was reacting to.</param>
        /// <param name="what">The outcome, in words.</param>
        /// <remarks>
        /// Less noisy than it looks: this runs once per event, not once per sweep, so
        /// a busy fight is a handful of lines a second. The name lookup costs two ZDO
        /// reads and a filter pass, which is why it sits behind the check.
        /// </remarks>
        internal static void Trace(ChatterComponent speaker, ChatterEvent kind, string what)
        {
            if (!ModConfig.LogChatter.Value)
            {
                return;
            }

            string name = speaker.Character == null ? "?" : Summons.NameOf(speaker.Character) ?? "?";

            ChattyBonesPlugin.Log.LogInfo("[chatter] " + name + " / " + kind + ": " + what);
        }

        /// <summary>Let whichever skeleton is willing react to something that happened nearby.</summary>
        /// <returns>True if one of them spoke.</returns>
        /// <param name="kind">What happened.</param>
        /// <param name="subject">A prefab hash, or 0.</param>
        /// <param name="targetName">Already localized, or null.</param>
        /// <param name="companion">The skeleton the remark is about. Never the speaker - it is excluded by reference.</param>
        /// <param name="companionName">Its name, already resolved, when it may no longer exist to be asked.</param>
        /// <param name="details">What is known about the event itself. Usually nothing.</param>
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
        internal static bool SpeakAny(
            ChatterEvent kind,
            int subject,
            string targetName,
            Character companion,
            string companionName = null,
            LineDetails details = default)
        {
            List<ChatterComponent> squad = ChatterComponent.All;
            int count = squad.Count;
            if (count == 0)
            {
                return false;
            }

            // Masked to stay positive - a negative remainder is a negative index.
            int start = (_nextSpeaker++ & int.MaxValue) % count;

            for (int offset = 0; offset < count; offset++)
            {
                ChatterComponent speaker = squad[(start + offset) % count];

                if (companion != null && speaker.Character == companion)
                {
                    // Nobody commiserates with themselves.
                    continue;
                }

                if (TrySpeak(speaker, kind, subject, targetName, companion, companionName, details))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Rotates so the same skeleton is not always asked first. Wraps harmlessly.</summary>
        private static int _nextSpeaker;

        /// <summary>Find somebody for a skeleton to talk about, or to.</summary>
        /// <returns>Another loaded skeleton, or null when it is on its own.</returns>
        /// <param name="speaker">Whoever is looking for company.</param>
        /// <remarks>
        /// For the idle lines. Random rather than the nearest, so a squad of three
        /// does not settle into two of them always addressing each other.
        /// </remarks>
        internal static Character AnotherOf(ChatterComponent speaker)
        {
            List<ChatterComponent> squad = ChatterComponent.All;
            if (squad.Count < 2)
            {
                return null;
            }

            // Drawn from the others rather than from everybody, which matters more
            // than it looks: picking a random start and walking to the first
            // non-speaker hands the speaker's list neighbour two chances in every
            // three with a squad of three, and list order is summon order - so it
            // would be the same neighbour every time.
            int pick = _random.Next(0, squad.Count - 1);

            for (int i = 0; i < squad.Count; i++)
            {
                ChatterComponent other = squad[i];

                if (ReferenceEquals(other, speaker))
                {
                    continue;
                }

                if (pick-- == 0)
                {
                    return other.Character;
                }
            }

            return null;
        }

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
            // Ahead of the Enabled check, and it has to be: the countdown behind
            // ShouldReload runs on the dt we pass it, so skipping the call while the
            // mod is switched off would leave a pending edit frozen mid-settle and
            // apply it at some arbitrary later moment.
            if (PackFile.ShouldReload(dt))
            {
                ReloadPack();
            }

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

            // Guarded per skeleton rather than around the loop, so one of them failing
            // costs its own turn rather than the rest of the squad's. This is the only
            // way into our code that is not a Harmony patch - the patches carry their
            // own catch, for the same reason and with rather more at stake.
            List<ChatterComponent> squad = ChatterComponent.All;
            for (int i = 0; i < squad.Count; i++)
            {
                try
                {
                    squad[i].Sweep(elapsed);
                }
                catch (System.Exception e)
                {
                    ChattyBonesPlugin.Log.LogWarning("ChattyBones stumbled watching a skeleton: " + e);
                }
            }
        }
    }
}
