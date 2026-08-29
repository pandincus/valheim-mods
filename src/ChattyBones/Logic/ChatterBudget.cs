using System.Collections.Generic;

namespace ChattyBones.Logic
{
    /// <summary>
    /// Decides whether a skeleton is allowed to say something right now.
    /// </summary>
    /// <remarks>
    /// This is what makes the mod bearable rather than what makes it funny. Five
    /// skeletons all reacting to everything is an unreadable wall of text, so a line
    /// has to get past four checks before anyone opens their mouth.
    ///
    /// We decide *whether* someone speaks; <see cref="LineChooser"/> decides *what*.
    ///
    /// Asking and booking are separate calls - see <see cref="CanClaim"/>.
    ///
    /// There is no clock and no game state in here; the caller passes the time in,
    /// so a test can cover an afternoon of chatter in microseconds.
    /// </remarks>
    internal sealed class ChatterBudget
    {
        /// <summary>When each skeleton last spoke, keyed by its stable id.</summary>
        /// <remarks>
        /// Never trimmed, and neither is <see cref="_lastRemarkBySubject"/>. One
        /// entry is 8 bytes of key and 4 of value, so a six-hour session summoning a
        /// skeleton every ten seconds costs about 50KB - and dead skeletons are the
        /// cheap case, since they stop adding entries. The subject map is bounded by
        /// how many kinds of creature the game has.
        ///
        /// There was a Prune pass. It was dropped because trimming against the
        /// *current* window meant raising the speaker cooldown mid-session was
        /// silently ignored for anyone who had already spoken.
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
        /// What the remark is about, as a prefab hash - the greydwarf being charged,
        /// or whatever just hit you. It exists so two skeletons noticing the same
        /// thing produce one remark rather than two. Pass 0 when the event is not
        /// about anything (Hurt, Idle) and the echo check is skipped.
        ///
        /// It must name a *kind* of thing, never a particular one. That is what keeps
        /// <see cref="TrackedSubjects"/> bounded. CompanionHurt is the trap - its
        /// subject is naturally one specific skeleton - so use the companion's prefab
        /// hash here and carry which one it was separately.
        /// </param>
        /// <param name="now">
        /// Seconds from the game clock (Unity's Time.time), not an epoch timestamp -
        /// the origin is arbitrary. Only differences are ever used.
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
        /// **Resolve one claim before asking about another.** Asking books nothing, so
        /// asking for two skeletons in a row says yes twice, and letting both speak
        /// defeats the squad gap and the echo window. The TargetAcquired poll loops
        /// over the squad, so the wrong shape is also the obvious one: ask, then
        /// <see cref="Commit"/> or give up, then move on.
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
        /// renumbering everything. That did not survive first contact - promoting the
        /// three outcome events above TargetAcquired needed more room than the gaps
        /// had, so the whole thing was renumbered at once. Harmless, since only the
        /// ordering is ever read, but it is the reason the numbers look freshly
        /// spaced. No two events may share a rank - barging in needs a strictly
        /// higher number, so a tie silently means neither can ever interrupt the
        /// other. There is a test for that.
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
                ChatterEvent.Died => 130,
                ChatterEvent.Unsummoned => 120,

                // Above the skeleton's own injuries on purpose. If you are being
                // chewed on and a skeleton is too, the one worth hearing about is you.
                ChatterEvent.PlayerHurt => 110,

                ChatterEvent.Hurt => 100,
                ChatterEvent.CompanionHurt => 90,

                // Outcomes above intentions. These three sat below TargetAcquired at
                // first, which sounds harmless and is not: a fight is usually over
                // inside MinGapSeconds, so the kill could not preempt the announcement
                // that preceded it and was dropped outright, while the next target
                // acquisition sailed through at the higher rank. The result in the
                // Black Forest was three "there's a greydwarf" and never a result.
                ChatterEvent.PlayerGotAKill => 80,
                ChatterEvent.Killed => 70,
                ChatterEvent.CompanionKilled => 60,

                ChatterEvent.TargetAcquired => 50,
                ChatterEvent.PlayerLandedABigHit => 40,
                ChatterEvent.Buffed => 30,
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
        /// Both parts matter. Keying on the subject alone would mean announcing a
        /// greydwarf silences a different skeleton killing that same greydwarf a
        /// moment later, and those are two remarks worth hearing.
        ///
        /// Worked example. TargetAcquired is event 1, and say the creature's prefab
        /// hash is -2:
        ///
        ///     (long)1 &lt;&lt; 32   0x0000_0001_0000_0000
        ///     (uint)-2         0x0000_0000_FFFF_FFFE
        ///     OR               0x0000_0001_FFFF_FFFE
        ///
        /// The uint cast is doing real work there. Drop it and (long)-2 is
        /// 0xFFFF_FFFF_FFFF_FFFE, which floods the event half: Killed about the same
        /// creature gives the identical key, so one remark silences the other.
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
