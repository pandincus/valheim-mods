using System.Collections.Generic;

namespace ChattyBones.Logic
{
    /// <summary>Why a claim was turned down. Only ever read by the debug log.</summary>
    internal enum ChatterRefusal
    {
        /// <summary>Not refused at all.</summary>
        None,

        /// <summary>The player switched this event off.</summary>
        EventDisabled,

        /// <summary>Somebody already remarked on this exact thing.</summary>
        SubjectEcho,

        /// <summary>This skeleton spoke too recently.</summary>
        SpeakerCooldown,

        /// <summary>Somebody spoke too recently, and this was not important enough to cut in.</summary>
        SquadGap,
    }

    /// <summary>
    /// Decides whether a skeleton is allowed to say something right now.
    /// </summary>
    /// <remarks>
    /// Five skeletons all reacting to everything is an unreadable wall of text, so
    /// a line has to get past four checks before anyone opens their mouth.
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

        /// <summary>What the squad is currently talking about, and whether anyone has answered.</summary>
        /// <remarks>
        /// The gaps space out *subjects of conversation*, not utterances. One event
        /// opens a moment, and an event that answers that moment is the second half of
        /// it rather than a new interruption - so it does not wait, in the same way
        /// that you do not pause before saying "oh no" when somebody drops something.
        /// </remarks>
        private ChatterEvent _momentKind;
        private float _momentAt = float.NegativeInfinity;
        private bool _momentAnswered;

        /// <summary>How long an answer stays part of the moment it is answering.</summary>
        /// <remarks>
        /// Answers are raised in the same frame as the thing they answer, so this is
        /// almost always zero. A second rather than nothing, so that answering from the
        /// next sweep instead would still count.
        /// </remarks>
        private const float AnswerWindowSeconds = 1f;

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
        /// <param name="why">Which check turned it down, or None when it did not. For the debug log.</param>
        internal bool CanClaim(long speakerId, ChatterEvent kind, int subject, float now, out ChatterRefusal why)
        {
            ChatterSettings settings = Settings;
            why = ChatterRefusal.None;

            if (settings.IsDisabled(kind))
            {
                why = ChatterRefusal.EventDisabled;
                return false;
            }

            if (subject != 0
                && _lastRemarkBySubject.TryGetValue(SubjectKey(kind, subject), out float remarkedAt)
                && now - remarkedAt < settings.SquadEchoWindowSeconds)
            {
                why = ChatterRefusal.SubjectEcho;
                return false;
            }

            if (!IsTerminal(kind)
                && _lastSpokeBySpeaker.TryGetValue(speakerId, out float spokeAt)
                && now - spokeAt < settings.SpeakerCooldownSeconds)
            {
                why = ChatterRefusal.SpeakerCooldown;
                return false;
            }

            if (IsAnsweringTheMoment(kind, now))
            {
                return true;
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
            if (PriorityOf(kind) > _lastPriority && sinceAnyone >= settings.PreemptGapSeconds)
            {
                return true;
            }

            why = ChatterRefusal.SquadGap;
            return false;
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
            _lastSpokeBySpeaker[speakerId] = now;

            // An answer never lowers the bar. It is part of the moment it answers, so
            // the standing that has to be beaten stays the opener's - "oh no" is not a
            // weaker thing to interrupt than the death that prompted it.
            int priority = PriorityOf(kind);
            if (Answers(kind) == null || priority > _lastPriority)
            {
                _lastPriority = priority;
            }

            if (subject != 0)
            {
                _lastRemarkBySubject[SubjectKey(kind, subject)] = now;
            }

            if (Answers(kind) == null)
            {
                _momentKind = kind;
                _momentAt = now;
                _momentAnswered = false;
            }
            else
            {
                // One answer to a moment, so a squad wipe is still one exchange rather
                // than four skeletons all saying "oh no" over each other.
                _momentAnswered = true;
            }
        }

        /// <summary>Is this claim the second half of what the squad is already saying?</summary>
        /// <returns>True if it answers the moment in progress, and nobody has answered yet.</returns>
        /// <param name="kind">The event being claimed.</param>
        /// <param name="now">The game clock.</param>
        private bool IsAnsweringTheMoment(ChatterEvent kind, float now)
        {
            return !_momentAnswered
                && Answers(kind) is ChatterEvent subject
                && subject.Equals(_momentKind)
                && now - _momentAt <= AnswerWindowSeconds;
        }

        /// <summary>Which event, if any, this one is a reply to.</summary>
        /// <returns>The event answered, or null when this starts a subject of its own.</returns>
        /// <param name="kind">The event to look up.</param>
        /// <remarks>
        /// An answer skips the squad gap and the preempt gap, because it is not a
        /// second remark - it is the rest of the first one. Everything else still
        /// applies, and the speaker cooldown in particular does: whoever answers
        /// should be a skeleton that has been quiet, not the one that has been
        /// narrating all fight.
        ///
        /// Both entries are moments where one skeleton is the subject and the rest have
        /// something to say about it: somebody dying, and somebody arriving.
        ///
        /// CompanionHurt and CompanionKilled are deliberately not here. They *cover*
        /// for a subject that could not speak rather than replying to one that did, so
        /// nothing was said and there is no gap to skip - that is done at the call
        /// site, by trying the subject first and the squad second. NOTES-ChattyBones.md
        /// has why promoting them is a content decision rather than a free one.
        /// </remarks>
        private static ChatterEvent? Answers(ChatterEvent kind)
        {
            // Plain ifs rather than a switch expression: a switch over two interesting
            // cases and thirteen nulls trips IDE0072, which wants every event spelled
            // out to be satisfied.
            if (kind == ChatterEvent.CompanionDied)
            {
                return ChatterEvent.Died;
            }

            if (kind == ChatterEvent.CompanionSummoned)
            {
                return ChatterEvent.Summoned;
            }

            return null;
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
        /// The gaps are wide so there is room to slot something in later. No two
        /// events may share a rank - barging in needs a strictly higher number, so a
        /// tie silently means neither can ever interrupt the other, and there is a
        /// test for that.
        ///
        /// I have left this hard-coded rather than exposing it in the config. It is
        /// hard to describe to a player in a way they could act on, and getting it
        /// wrong makes the mod worse in ways that are difficult to diagnose - if
        /// idle chatter outranked death cries you would probably just conclude the
        /// mod was broken. If somebody genuinely wants to reorder it, that is a good
        /// reason to revisit, but I would rather not invite the mistake by default.
        ///
        /// Internal rather than private because the combat hooks need it too. Two
        /// texture events can land on one blow and only one of them gets held for the
        /// end of it, so something has to say which - and the answer has to be this
        /// table rather than a second opinion beside it.
        /// </remarks>
        internal static int PriorityOf(ChatterEvent kind)
        {
            return kind switch
            {
                ChatterEvent.Died => 130,
                ChatterEvent.Unsummoned => 120,

                // Above the skeleton's own injuries on purpose. If you are being
                // chewed on and a skeleton is too, the one worth hearing about is you.
                ChatterEvent.PlayerHurt => 110,

                // Above Hurt, because dying beats being wounded; below PlayerHurt for
                // the same reason Hurt is.
                ChatterEvent.CompanionDied => 105,

                // Above Hurt, because the blow that sets you alight raises both in
                // the same frame - SEMan.AddStatusEffect runs from inside RPC_Damage -
                // and catching fire is the more interesting half of that pair. The
                // burning itself never reaches Hurt at all: SE_Burning ticks through
                // ApplyDamage directly and skips RPC_Damage, where our hook is.
                ChatterEvent.Afflicted => 102,

                ChatterEvent.Hurt => 100,

                // Below PlayerHurt, which is the same moment told better when both
                // fire. Above CompanionHurt, because you being knocked flat matters
                // more than a skeleton being scratched. It earns its place by firing
                // when PlayerHurt does not - the stagger bar fills from a run of small
                // hits, none of which clears HurtFraction on its own.
                ChatterEvent.PlayerStaggered => 95,

                ChatterEvent.CompanionHurt => 90,

                // Above the kill events, which is unusual for something this rare.
                // A raid announces itself and then immediately supplies things to
                // fight, so at any lower rank the squad would call out the first
                // greydwarf instead of the thing that brought it.
                ChatterEvent.Raid => 85,

                // Outcomes above intentions, which they were not at first. See
                // HowAFightEndedOutranksNoticingItStarted for what that cost.
                ChatterEvent.PlayerGotAKill => 80,
                ChatterEvent.Killed => 70,
                ChatterEvent.CompanionKilled => 60,

                ChatterEvent.TargetAcquired => 50,

                // The texture of a fight, all of it below TargetAcquired on purpose:
                // these three fire several times an encounter, so letting them
                // interrupt the narration would mean hearing about footwork instead
                // of about the troll. Ordered by how hard the thing is - a dodge that
                // turned a blow beats a parry beats knocking something about, and all
                // three beat landing an ordinary heavy hit.
                ChatterEvent.PlayerDodged => 48,
                ChatterEvent.PlayerParried => 45,
                ChatterEvent.StaggeredIt => 42,

                ChatterEvent.PlayerLandedABigHit => 40,

                // Far below Raid, and it does not need the standing: surviving one
                // is said into a quiet field with nothing to compete against.
                ChatterEvent.RaidEnded => 35,

                ChatterEvent.Buffed => 30,
                ChatterEvent.Summoned => 20,

                // Getting better at something is about you, so it sits above the
                // places and the sky, and below anything that happens to a person.
                ChatterEvent.PlayerSkilledUp => 19,

                // Arriving somewhere is worth more than the weather and less than
                // anything that happens to a person. Both fire while travelling,
                // which is when the squad has least to say.
                ChatterEvent.BiomeChanged => 18,
                ChatterEvent.Sheltered => 17,

                // Lunch is not urgent.
                ChatterEvent.PlayerAte => 16,

                ChatterEvent.CompanionSummoned => 15,

                // The day cycle is twenty real minutes, so these are a rhythm rather
                // than an occasion, and they sit with the small talk accordingly.
                // Dawn above Nightfall because it is the one an undead thing has a
                // view about.
                ChatterEvent.Dawn => 14,
                ChatterEvent.Nightfall => 13,

                // Being rained on is small talk, and any water at all applies it - so
                // anywhere higher and a skeleton crossing a stream talks over a kill.
                ChatterEvent.Weather => 12,

                // One above Idle, and doing the whole job of deciding how often you
                // hear about loot - there is no filter on what is worth mentioning,
                // because at this rank the squad gap already decides that better than
                // any judgement about items could.
                ChatterEvent.Looted => 11,

                ChatterEvent.Idle => 10,

                // Practically speaking we never land here, because the enum is ours
                // and every value above is covered. It exists so that adding a new
                // event and forgetting this switch gives you a skeleton that is
                // merely quiet rather than a crash mid-fight.
                _ => 0,
            };
        }

        /// <summary>Is this the last thing this skeleton will ever say?</summary>
        /// <returns>True for the two events a skeleton only ever reaches once, on its way out.</returns>
        /// <param name="kind">The event being claimed.</param>
        /// <remarks>
        /// These skip the speaker cooldown, because the cooldown is forward-looking:
        /// it rations how often one skeleton speaks *next*, so a single loud one does
        /// not carry the squad. A dying skeleton has no next, and holding back its
        /// last words protects a budget it will never spend.
        ///
        /// Nothing else is waived - the gaps still apply, and Commit still records it.
        /// </remarks>
        private static bool IsTerminal(ChatterEvent kind)
        {
            return kind is ChatterEvent.Died or ChatterEvent.Unsummoned;
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
