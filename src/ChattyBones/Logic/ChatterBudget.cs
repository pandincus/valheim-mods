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
    /// **Treat an instance as frozen once you have handed it to a
    /// <see cref="ChatterBudget"/>.** To change a setting, build a whole new
    /// ChatterSettings and assign <see cref="ChatterBudget.Settings"/>. Do not reach
    /// in and edit a field, and above all do not Clear() and refill
    /// <see cref="DisabledEvents"/> in place.
    ///
    /// That is not fussiness. BepInEx's config file-watcher does not raise
    /// SettingChanged on Unity's main thread, so an edit made there runs while the
    /// game is somewhere in the middle of a frame. Swapping one reference is a
    /// single atomic write and a reader sees either the old settings or the new ones.
    /// Mutating in place lets a reader see a half-rebuilt set, which would show up as
    /// a skeleton ignoring an event for one frame - roughly the least debuggable
    /// symptom I can imagine.
    ///
    /// The defaults are a starting guess and I fully expect to move them after
    /// watching an actual squad. Five skeletons is a lot of mouths.
    /// </remarks>
    internal sealed class ChatterSettings
    {
        /// <summary>How long the whole squad stays quiet after any one of them speaks.</summary>
        internal float MinGapSeconds = 2.5f;

        /// <summary>
        /// The floor that even an important line respects.
        /// </summary>
        /// <remarks>
        /// A death cry is allowed to cut in on someone's idle muttering, but not in
        /// the same instant - two bits of text appearing together are two bits of
        /// text nobody reads. So a barge-in still waits this long.
        /// </remarks>
        internal float PreemptGapSeconds = 0.5f;

        /// <summary>How long one skeleton waits before it is allowed to speak again.</summary>
        /// <remarks>
        /// Deliberately much longer than <see cref="MinGapSeconds"/>. The squad as a
        /// whole can keep up a conversation while any individual in it stays
        /// relatively quiet, which is the effect we want - it reads as a group of
        /// people rather than one person with a lot to say.
        /// </remarks>
        internal float SpeakerCooldownSeconds = 8f;

        /// <summary>
        /// How long one remark about a particular thing stops everyone else
        /// remarking on the same thing.
        /// </summary>
        /// <remarks>
        /// This is the one that matters most in practice. Send five skeletons at one
        /// greydwarf and all five acquire it inside the same second, so without this
        /// you get five near-identical lines stacked on top of each other. With it,
        /// one of them calls the target and the rest just get on with it.
        /// </remarks>
        internal float SquadEchoWindowSeconds = 6f;

        /// <summary>Events the player has switched off.</summary>
        /// <remarks>
        /// Kept here rather than checked at each call site, so that "I am sick of
        /// them announcing every greydwarf" is one lookup in one place, and so the
        /// tests can cover it without a game running.
        ///
        /// Build it once and leave it alone - see the note on the class.
        /// </remarks>
        internal HashSet<ChatterEvent> DisabledEvents = [];
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
        /// </remarks>
        internal bool CanClaim(long speakerId, ChatterEvent kind, int subject, float now)
        {
            ChatterSettings settings = Settings;

            if (settings.DisabledEvents.Contains(kind))
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
        /// <remarks>
        /// Both dictionaries grow for the life of a session and are never trimmed,
        /// which was a deliberate reversal. There used to be a Prune method here, and
        /// it was wrong twice over.
        ///
        /// It was wrong on cost: this is not a leak worth code. The speaker map holds
        /// one small entry per skeleton you ever summon, and the subject map one per
        /// distinct (event, creature type) pair - which the game itself bounds, since
        /// there are only so many kinds of creature. Even an absurd session lands in
        /// the tens of kilobytes.
        ///
        /// And it was wrong on correctness, once settings became live. Entries were
        /// dropped against the window as it stood at the time, so raising the speaker
        /// cooldown from 8 seconds to 30 mid-session would let a skeleton that spoke
        /// 10 seconds ago speak again immediately, quietly contradicting the setting
        /// the player had just changed.
        ///
        /// It also could not be tested. Deleting the whole method changed no
        /// observable behaviour, which is exactly what you would expect of code whose
        /// only job is to forget things that no longer affect any answer - and is a
        /// good sign the code should not exist.
        /// </remarks>
        internal int TrackedSpeakers => _lastSpokeBySpeaker.Count;

        /// <summary>How many subjects we are currently remembering. For tests.</summary>
        internal int TrackedSubjects => _lastRemarkBySubject.Count;
    }
}
