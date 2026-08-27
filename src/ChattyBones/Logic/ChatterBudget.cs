using System.Collections.Generic;

namespace ChattyBones.Logic
{
    /// <summary>
    /// The knobs that decide how talkative a squad is.
    /// </summary>
    /// <remarks>
    /// These all come from the BepInEx config in the real mod, but nothing here
    /// knows that - the whole point of this folder is that it runs without a game
    /// attached. The tests just build one of these by hand.
    ///
    /// Immutable, and that is load-bearing rather than tidiness. To change a
    /// setting, build a whole new one and assign
    /// <see cref="ChatterBudget.Settings"/>. BepInEx's config file-watcher does not
    /// raise SettingChanged on Unity's main thread, so an edit made there runs while
    /// the game is somewhere in the middle of a frame. Swapping one reference is a
    /// single atomic write and a reader sees either the old settings or the new
    /// ones, whole. Editing in place would let a reader catch a half-rebuilt set -
    /// which shows up as a skeleton ignoring an event for exactly one frame, roughly
    /// the least debuggable symptom I can imagine.
    ///
    /// This started out as plain mutable fields with a comment asking nicely. A
    /// review pointed out that the one operation the comment forbade was also the
    /// easiest thing the type offered, which is a fair definition of a bad contract.
    ///
    /// (Get-only properties rather than `init` accessors because the mod targets
    /// net472, where `init` needs an IsExternalInit shim that the net10.0 test
    /// project would then define twice.)
    ///
    /// The defaults are a starting guess and I fully expect to move them after
    /// watching an actual squad. Five skeletons is a lot of mouths.
    /// </remarks>
    internal sealed class ChatterSettings
    {
        private readonly HashSet<ChatterEvent> _disabled;

        /// <summary>Build a set of settings. Everything has a default, so name only what you are changing.</summary>
        /// <param name="minGapSeconds">How long the whole squad stays quiet after any one of them speaks.</param>
        /// <param name="preemptGapSeconds">
        /// The floor that even an important line respects. A death cry may cut in on
        /// somebody's idle muttering, but not in the same instant - two bits of text
        /// appearing together are two bits of text nobody reads.
        /// </param>
        /// <param name="speakerCooldownSeconds">
        /// How long one skeleton waits before speaking again. Deliberately much
        /// longer than the squad gap: the group as a whole can keep up a conversation
        /// while any individual in it stays fairly quiet, which reads as a group of
        /// people rather than one person with a lot to say.
        /// </param>
        /// <param name="squadEchoWindowSeconds">
        /// How long one remark about a particular thing stops everyone else remarking
        /// on the same thing. The one that matters most in practice - send five
        /// skeletons at one greydwarf and all five acquire it inside the same second.
        /// </param>
        /// <param name="disabledEvents">Events the player has switched off, or null for none. Copied, not held.</param>
        internal ChatterSettings(
            float minGapSeconds = 2.5f,
            float preemptGapSeconds = 0.5f,
            float speakerCooldownSeconds = 8f,
            float squadEchoWindowSeconds = 6f,
            IEnumerable<ChatterEvent> disabledEvents = null)
        {
            MinGapSeconds = minGapSeconds;
            PreemptGapSeconds = preemptGapSeconds;
            SpeakerCooldownSeconds = speakerCooldownSeconds;
            SquadEchoWindowSeconds = squadEchoWindowSeconds;
            _disabled = disabledEvents == null ? [] : [.. disabledEvents];
        }

        /// <summary>How long the whole squad stays quiet after any one of them speaks.</summary>
        internal float MinGapSeconds { get; }

        /// <summary>The floor that even an important line respects.</summary>
        internal float PreemptGapSeconds { get; }

        /// <summary>How long one skeleton waits before it is allowed to speak again.</summary>
        internal float SpeakerCooldownSeconds { get; }

        /// <summary>How long one remark about a thing stops everyone else remarking on it.</summary>
        internal float SquadEchoWindowSeconds { get; }

        /// <summary>Has the player switched this event off?</summary>
        /// <param name="kind">The event to check.</param>
        /// <returns>True if nobody should react to it at all.</returns>
        /// <remarks>
        /// A method rather than an exposed set, so that "I am sick of them announcing
        /// every greydwarf" is one lookup in one place and there is nothing for a
        /// caller to accidentally edit.
        /// </remarks>
        internal bool IsDisabled(ChatterEvent kind)
        {
            return _disabled.Contains(kind);
        }
    }

    /// <summary>
    /// Decides whether a skeleton is allowed to say something right now.
    /// </summary>
    /// <remarks>
    /// This is the part that makes the mod bearable rather than the part that makes
    /// it funny. A Dead Raiser squad is up to five skeletons, and if every one of
    /// them reacts to everything, you get an unreadable wall of text and the joke is
    /// dead inside a minute. So every line has to get past four separate checks
    /// before anyone opens their mouth.
    ///
    /// Asking and booking are two calls on purpose - <see cref="CanClaim"/> then
    /// <see cref="Commit"/>. It is tempting to have one method that answers and
    /// records in one go, and that is what this did first, but the caller cannot know
    /// there is anything to *say* until after it has asked: the pack may have no
    /// lines for that personality and event, or every line it does have may want a
    /// {target} we have not got. A silent event would then have burned the squad's
    /// gap, the skeleton's cooldown and an echo lock on that subject, for a line
    /// nobody heard. With a half-written pack - which the shared-personality fallback
    /// deliberately invites - the squad would just go quieter than the numbers say,
    /// and nothing in the config would explain why.
    ///
    /// Doing it the other way round, choosing a line first and asking afterwards, is
    /// worse: you would pay for line choice on every event that gets refused, which
    /// is most of them.
    ///
    /// Note the split of responsibilities too: we decide *whether* someone speaks,
    /// and <see cref="LineChooser"/> decides *what* they say. Keeping "don't repeat
    /// the same gag twice running" over there means this class only ever deals in
    /// timestamps and identifiers.
    ///
    /// Everything is passed in - the current time, who is asking, what about. There
    /// is no clock in here and no game state, so a test can run a whole afternoon of
    /// skeleton chatter in a few microseconds by just handing it larger numbers.
    /// </remarks>
    internal sealed class ChatterBudget
    {
        /// <summary>When each skeleton last spoke, keyed by its stable id.</summary>
        /// <remarks>
        /// This and <see cref="_lastRemarkBySubject"/> grow for the life of a session
        /// and are never trimmed. That is deliberate: there used to be a Prune pass
        /// here and it was wrong twice over.
        ///
        /// Wrong on cost. This is not a leak worth code. This map holds one small
        /// entry per skeleton you ever summon, and the other one entry per distinct
        /// (event, creature type) pair - which the game itself bounds, since there
        /// are only so many kinds of creature. Even an absurd session lands in the
        /// tens of kilobytes. See the subject parameter on <see cref="CanClaim"/> for
        /// the one thing that would break that bound.
        ///
        /// Wrong on correctness, once settings became live. Entries were dropped
        /// against the window as it stood at the time, so raising the speaker
        /// cooldown from 8 seconds to 30 mid-session let a skeleton that spoke 10
        /// seconds ago speak again immediately, quietly contradicting the setting the
        /// player had just changed.
        ///
        /// It also could not be tested. Deleting the whole method changed no
        /// observable behaviour, which is what you would expect of code whose only
        /// job is to forget things that no longer affect any answer - and is a decent
        /// sign the code should not exist. <see cref="TrackedSpeakers"/> exists so a
        /// regression guard can fail if somebody puts it back.
        /// </remarks>
        private readonly Dictionary<long, float> _lastSpokeBySpeaker = [];

        /// <summary>
        /// When something was last remarked upon, keyed by event and subject together.
        /// </summary>
        /// <remarks>
        /// See <see cref="SubjectKey"/> for how the two are packed into one long.
        /// </remarks>
        private readonly Dictionary<long, float> _lastRemarkBySubject = [];

        private float _lastSpokeAt = float.NegativeInfinity;
        private int _lastPriority = int.MinValue;

        /// <summary>Build a budget over the given settings.</summary>
        /// <param name="settings">The starting settings. See <see cref="Settings"/>.</param>
        internal ChatterBudget(ChatterSettings settings)
        {
            Settings = settings;
        }

        /// <summary>The settings in force.</summary>
        /// <remarks>
        /// Assign a freshly built <see cref="ChatterSettings"/> to change anything;
        /// never edit the one already in here. The reasoning is on ChatterSettings
        /// itself, and it comes down to BepInEx raising SettingChanged off the main
        /// thread.
        /// </remarks>
        internal ChatterSettings Settings { get; set; }

        /// <summary>
        /// Would this skeleton be allowed to speak right now?
        /// </summary>
        /// <returns>
        /// True if it may talk. Nothing has been recorded - call <see cref="Commit"/>
        /// once you actually have a line, and only then.
        ///
        /// False if it should stay quiet, and the caller should simply drop the event
        /// on the floor. There is no queue and nothing is owed a turn later, which is
        /// deliberate: a reaction to something that happened eight seconds ago is
        /// worse than no reaction at all.
        /// </returns>
        /// <param name="speakerId">
        /// Whichever stable number identifies this skeleton. The real mod derives it
        /// from the ZDO, but nothing here cares where it came from - it only ever
        /// gets compared for equality.
        /// </param>
        /// <param name="kind">What just happened.</param>
        /// <param name="subject">
        /// What the event was *about*, when that makes sense - the prefab hash of the
        /// greydwarf it just charged at, or whatever hit you. Pass 0 for events that
        /// are not about anything in particular, like Hurt or Idle, and the squad echo
        /// check below is skipped for them.
        ///
        /// **This must identify a kind of thing, never a particular one.** A prefab
        /// hash is per creature type, and that is the only reason
        /// <see cref="TrackedSubjects"/> is bounded and we can get away with never
        /// forgetting anything. CompanionHurt is the trap: its subject is naturally
        /// another skeleton, and folding an instance id in here would make this map
        /// grow without limit for the whole session. Use the companion's prefab hash
        /// and carry which one it was somewhere else.
        /// </param>
        /// <param name="now">
        /// Seconds, from the game clock. Only differences matter, so any monotonic
        /// source will do and the origin is irrelevant.
        /// </param>
        /// <remarks>
        /// The four checks run cheapest-first, and each one can only refuse:
        ///
        /// 1. Did the player switch this event off?
        /// 2. Has somebody already remarked on this exact thing recently?
        /// 3. Has this particular skeleton spoken too recently?
        /// 4. Has *anyone* spoken too recently - and if so, is this important
        ///    enough to barge in anyway?
        ///
        /// **Resolve one claim before asking about another.** Because asking does not
        /// book anything, asking for two skeletons back to back will happily say yes
        /// twice - and if you then let both speak, the squad gap and the echo window
        /// have both been defeated. That is not hypothetical: the TargetAcquired poll
        /// runs over the whole squad several times a second, so "collect everyone
        /// whose target changed, ask for each, then say them all" is the natural way
        /// to write it and is wrong.
        ///
        /// Ask, then either <see cref="Commit"/> or give up, then move on to the next
        /// skeleton.
        /// </remarks>
        internal bool CanClaim(long speakerId, ChatterEvent kind, int subject, float now)
        {
            ChatterSettings settings = Settings;

            if (settings.IsDisabled(kind))
            {
                return false;
            }

            if (subject != 0
                && _lastRemarkBySubject.TryGetValue(SubjectKey(kind, subject), out float remarkedAt)
                && now - remarkedAt < settings.SquadEchoWindowSeconds)
            {
                return false;
            }

            if (_lastSpokeBySpeaker.TryGetValue(speakerId, out float spokeAt)
                && now - spokeAt < settings.SpeakerCooldownSeconds)
            {
                return false;
            }

            float sinceAnyone = now - _lastSpokeAt;
            if (sinceAnyone >= settings.MinGapSeconds)
            {
                return true;
            }

            // Something more important than whatever was last said gets to interrupt,
            // as long as it still leaves a beat. Note the strict greater-than: two
            // skeletons dying together does not produce two overlapping death cries,
            // because the second one is not *more* important than the first. One set
            // of last words is plenty.
            return PriorityOf(kind) > _lastPriority
                && sinceAnyone >= settings.PreemptGapSeconds;
        }

        /// <summary>Record that this skeleton did in fact speak.</summary>
        /// <param name="speakerId">Who spoke.</param>
        /// <param name="kind">What about.</param>
        /// <param name="subject">What it concerned, or 0.</param>
        /// <param name="now">The same time you passed to <see cref="CanClaim"/>.</param>
        /// <remarks>
        /// Call this only after a line has actually been produced and said. Calling it
        /// without <see cref="CanClaim"/> having returned true is not checked for and
        /// will simply push the windows out, which is the caller getting what it asked
        /// for rather than something to guard against.
        /// </remarks>
        internal void Commit(long speakerId, ChatterEvent kind, int subject, float now)
        {
            _lastSpokeAt = now;
            _lastPriority = PriorityOf(kind);
            _lastSpokeBySpeaker[speakerId] = now;

            if (subject != 0)
            {
                _lastRemarkBySubject[SubjectKey(kind, subject)] = now;
            }
        }

        /// <summary>
        /// How much each kind of event deserves to be heard.
        /// </summary>
        /// <returns>
        /// A bigger number wins. The absolute values mean nothing on their own; only
        /// the ordering between them is used, and only ever inside
        /// <see cref="ChatterSettings.MinGapSeconds"/> of somebody else speaking.
        /// </returns>
        /// <param name="kind">The event to rank.</param>
        /// <remarks>
        /// The gaps are wide so there is room to slot something in later without
        /// renumbering everything. No two events may share a rank - barging in needs
        /// a strictly higher number, so a tie silently means neither can ever
        /// interrupt the other. There is a test for that.
        ///
        /// I have left this hard-coded rather than exposing it in the config. It is
        /// hard to describe to a player in a way they could act on, and getting it
        /// wrong makes the mod worse in ways that are difficult to diagnose - if
        /// idle chatter outranked death cries you would probably just conclude the
        /// mod was broken. If somebody genuinely wants to reorder it, that is a good
        /// reason to revisit, but I would rather not invite the mistake by default.
        /// </remarks>
        private static int PriorityOf(ChatterEvent kind)
        {
            return kind switch
            {
                ChatterEvent.Died => 100,
                ChatterEvent.Unsummoned => 90,

                // Above the skeleton's own injuries on purpose. If you are being
                // chewed on and a skeleton is too, the one worth hearing about is you.
                ChatterEvent.PlayerHurt => 80,

                ChatterEvent.Hurt => 70,
                ChatterEvent.CompanionHurt => 60,
                ChatterEvent.TargetAcquired => 50,
                ChatterEvent.Buffed => 40,
                ChatterEvent.PlayerGotAKill => 35,
                ChatterEvent.Killed => 30,
                ChatterEvent.PlayerLandedABigHit => 25,
                ChatterEvent.Summoned => 20,
                ChatterEvent.Idle => 10,

                // Practically speaking we never land here, because the enum is ours
                // and every value above is covered. It exists so that adding a new
                // event and forgetting this switch gives you a skeleton that is
                // merely quiet rather than a crash mid-fight.
                _ => 0,
            };
        }

        /// <summary>Pack an event and its subject into a single dictionary key.</summary>
        /// <returns>The event in the high 32 bits, the subject in the low 32.</returns>
        /// <param name="kind">What happened.</param>
        /// <param name="subject">What it happened to. Never 0 here - callers check first.</param>
        /// <remarks>
        /// Both parts matter. Keying on the subject alone would mean one skeleton
        /// announcing a greydwarf silences a different skeleton killing that same
        /// greydwarf a moment later, which are two different remarks and both worth
        /// hearing.
        ///
        /// The cast to uint before widening is load-bearing: prefab hashes are
        /// happily negative, and without it a negative hash sign-extends and stomps
        /// all over the event bits.
        /// </remarks>
        private static long SubjectKey(ChatterEvent kind, int subject)
        {
            return ((long)kind << 32) | (uint)subject;
        }

        /// <summary>How many speakers we are currently remembering. For tests.</summary>
        internal int TrackedSpeakers => _lastSpokeBySpeaker.Count;

        /// <summary>How many subjects we are currently remembering. For tests.</summary>
        internal int TrackedSubjects => _lastRemarkBySubject.Count;
    }
}
