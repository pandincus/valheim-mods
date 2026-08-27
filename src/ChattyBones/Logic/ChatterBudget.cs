using System.Collections.Generic;

namespace ChattyBones.Logic
{
    /// <summary>
    /// The things a skeleton can react to.
    /// </summary>
    /// <remarks>
    /// One entry per hook we install, plus Idle, which nothing triggers - it is
    /// just a timer running down with nothing else to say.
    ///
    /// The order here is not the priority order. Priority lives in
    /// <see cref="ChatterBudget.PriorityOf"/>, so that reordering this enum for
    /// readability cannot quietly change which skeleton gets to speak.
    /// </remarks>
    internal enum ChatterEvent
    {
        /// <summary>You just raised it with the Dead Raiser.</summary>
        Summoned,

        /// <summary>It picked something to attack, and is heading over.</summary>
        TargetAcquired,

        /// <summary>Something hit it hard enough to be worth mentioning.</summary>
        Hurt,

        /// <summary>It gained a status effect, e.g. you dropped a shield on it.</summary>
        Buffed,

        /// <summary>It killed something.</summary>
        /// <remarks>
        /// Not hooked off the victim's death, which sounds like the obvious place and
        /// is not. Character.OnDeath is reached from CheckDeath, which sits inside an
        /// IsOwner check, so a creature's death only fires on whichever client owns
        /// that creature - in a shared world that is often the host or another player,
        /// and your skeleton's kill would simply go uncommented.
        ///
        /// Instead we watch our own skeleton's target go from something to nothing
        /// and check whether that something is now dead, which reads replicated state
        /// and works whoever owns it. Attribution gets a little looser - the thing
        /// might have died to somebody else's axe - but "the creature my skeleton was
        /// charging at just died" is arguably the better trigger anyway. It fires
        /// when the skeleton thinks it won, which is the funnier moment.
        /// </remarks>
        Killed,

        /// <summary>You took a hit worth mentioning.</summary>
        /// <remarks>
        /// Damage on a Player resolves on that player's own client, and your client
        /// owns your summons, so your squad reacts to your injuries. Somebody else's
        /// skeletons will not, which is the right answer rather than a limitation -
        /// "cap'n" means their summoner, not you.
        ///
        /// Pass the attacker as the subject so the squad echo applies. Five skeletons
        /// all noticing you got hit should produce one remark, not five.
        /// </remarks>
        PlayerHurt,

        /// <summary>You hit something very hard.</summary>
        /// <remarks>
        /// The attacking client builds the HitData and calls Character.Damage, which
        /// is what then sends the RPC. So a hook on Damage rather than RPC_Damage
        /// runs on your machine with the number in hand, and no networking is
        /// involved at all - which is a nicer position than the kill events are in.
        /// </remarks>
        PlayerLandedABigHit,

        /// <summary>You killed something.</summary>
        /// <remarks>
        /// Kept separate from <see cref="PlayerLandedABigHit"/> even though a kill is
        /// the biggest hit there is, because "You got him!" and "Nice swing!" are
        /// different lines and a pack author should be able to write both. Event
        /// space is not scarce - see <see cref="Utterance"/>.
        /// </remarks>
        PlayerGotAKill,

        /// <summary>Another of your skeletons took a hit.</summary>
        /// <remarks>
        /// Both skeletons are yours and owned by the same client, so this needs no
        /// cleverness to detect. The fun is in <see cref="LineTokens.Companion"/>:
        /// they already have names, either the one they came with or whatever you
        /// renamed them to, so a line can be "Ach, {companion}!" rather than
        /// something vague about a colleague.
        /// </remarks>
        CompanionHurt,

        /// <summary>It died.</summary>
        Died,

        /// <summary>It timed out, or you summoned enough others to push it over the cap.</summary>
        Unsummoned,

        /// <summary>Nothing is happening and it feels the need to fill the silence.</summary>
        Idle,
    }

    /// <summary>
    /// The knobs that decide how talkative a squad is.
    /// </summary>
    /// <remarks>
    /// These all come from the BepInEx config in the real mod, but nothing here
    /// knows that - the whole point of this folder is that it runs without a game
    /// attached. The tests just build one of these by hand.
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
    /// Note the split: we decide *whether* someone speaks, and the line pack
    /// decides *what* they say. Keeping "don't repeat the same gag twice running"
    /// over there means this class only ever deals in timestamps and identifiers,
    /// which is much easier to reason about and to test.
    ///
    /// Everything is passed in - the current time, who is asking, what about. There
    /// is no clock in here and no game state, so a test can run a whole afternoon of
    /// skeleton chatter in a few microseconds by just handing it larger numbers.
    /// </remarks>
    internal sealed class ChatterBudget
    {
        private readonly ChatterSettings _settings;

        /// <summary>When each skeleton last spoke, keyed by its stable id.</summary>
        private readonly Dictionary<long, float> _lastSpokeBySpeaker = [];

        /// <summary>
        /// When something was last remarked upon, keyed by event and subject together.
        /// </summary>
        /// <remarks>
        /// See <see cref="SubjectKey"/> for how the two are packed into one long.
        /// </remarks>
        private readonly Dictionary<long, float> _lastRemarkBySubject = [];

        /// <summary>Scratch space for <see cref="Prune"/>, reused so it doesn't allocate.</summary>
        private readonly List<long> _expired = [];

        private float _lastSpokeAt = float.NegativeInfinity;
        private int _lastPriority = int.MinValue;

        /// <summary>Build a budget over the given settings.</summary>
        /// <param name="settings">
        /// Held by reference on purpose, not copied. The mod hands over the same
        /// object it updates when the player edits the config, so changes in
        /// ConfigurationManager take effect on the very next line without anyone
        /// having to rebuild this.
        /// </param>
        internal ChatterBudget(ChatterSettings settings)
        {
            _settings = settings;
        }

        /// <summary>
        /// Ask whether this skeleton may speak, and book the slot if so.
        /// </summary>
        /// <returns>
        /// True if it may talk, in which case we have already recorded that it did.
        /// False if it should stay quiet, and the caller should simply drop the line
        /// on the floor - there is no queue, and nothing is owed a turn later.
        /// Dropping is the right behaviour: a reaction to something that happened
        /// eight seconds ago is worse than no reaction at all.
        /// </returns>
        /// <param name="speakerId">
        /// Whichever stable number identifies this skeleton. The real mod derives it
        /// from the ZDO, but nothing here cares where it came from - it only ever
        /// gets compared for equality.
        /// </param>
        /// <param name="kind">What just happened.</param>
        /// <param name="subject">
        /// What the event was *about*, when that makes sense - the prefab hash of the
        /// greydwarf it just charged at, for instance. Pass 0 for events that are not
        /// about anything in particular, like Hurt or Idle, and the squad echo check
        /// below is skipped for them.
        /// </param>
        /// <param name="now">
        /// Seconds, from the game clock. Only differences matter, so any monotonic
        /// source will do and the origin is irrelevant.
        /// </param>
        /// <remarks>
        /// The four checks run cheapest-first, and each one can only reject:
        ///
        /// 1. Did the player switch this event off?
        /// 2. Has somebody already remarked on this exact thing recently?
        /// 3. Has this particular skeleton spoken too recently?
        /// 4. Has *anyone* spoken too recently - and if so, is this important
        ///    enough to barge in anyway?
        /// </remarks>
        internal bool TryClaim(long speakerId, ChatterEvent kind, int subject, float now)
        {
            if (_settings.DisabledEvents.Contains(kind))
            {
                return false;
            }

            if (subject != 0)
            {
                long subjectKey = SubjectKey(kind, subject);
                if (_lastRemarkBySubject.TryGetValue(subjectKey, out float remarkedAt)
                    && now - remarkedAt < _settings.SquadEchoWindowSeconds)
                {
                    return false;
                }
            }

            if (_lastSpokeBySpeaker.TryGetValue(speakerId, out float spokeAt)
                && now - spokeAt < _settings.SpeakerCooldownSeconds)
            {
                return false;
            }

            int priority = PriorityOf(kind);
            float sinceAnyone = now - _lastSpokeAt;
            if (sinceAnyone < _settings.MinGapSeconds)
            {
                // Something more important than whatever was last said gets to
                // interrupt, as long as it still leaves a beat. Note the strict
                // greater-than: two skeletons dying together does not produce two
                // overlapping death cries, because the second one is not *more*
                // important than the first. One set of last words is plenty.
                bool mayBargeIn = priority > _lastPriority
                    && sinceAnyone >= _settings.PreemptGapSeconds;

                if (!mayBargeIn)
                {
                    return false;
                }
            }

            _lastSpokeAt = now;
            _lastPriority = priority;
            _lastSpokeBySpeaker[speakerId] = now;

            if (subject != 0)
            {
                _lastRemarkBySubject[SubjectKey(kind, subject)] = now;
            }

            Prune(now);
            return true;
        }

        /// <summary>
        /// How much each kind of event deserves to be heard.
        /// </summary>
        /// <returns>
        /// A bigger number wins. The absolute values mean nothing on their own; only
        /// the ordering between them is used, and only ever inside
        /// <see cref="ChatterSettings.MinGapSeconds"/> of somebody else speaking.
        /// </returns>
        /// <remarks>
        /// The gaps are wide so there is room to slot something in later without
        /// renumbering everything.
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

        /// <summary>
        /// Forget bookkeeping that is too old to change any future answer.
        /// </summary>
        /// <param name="now">The current time, same clock as everything else.</param>
        /// <remarks>
        /// Without this, both dictionaries would grow for as long as the session
        /// lasts: a new entry per skeleton you ever summon, and per thing anyone ever
        /// remarks on. Neither is large, but "small leak that runs for eight hours"
        /// is still a leak.
        ///
        /// An entry can go once it is older than the longest window that would
        /// consult it, because from then on every comparison against it succeeds
        /// anyway and an absent entry gives the same answer as an ancient one.
        ///
        /// This is a scan of both dictionaries on every line spoken, which sounds
        /// worse than it is - we are talking about a handful of skeletons and the
        /// things they recently shouted at, and it only runs when somebody actually
        /// speaks rather than every frame.
        /// </remarks>
        private void Prune(float now)
        {
            CollectExpired(_lastSpokeBySpeaker, now, _settings.SpeakerCooldownSeconds);
            for (int i = 0; i < _expired.Count; i++)
            {
                _lastSpokeBySpeaker.Remove(_expired[i]);
            }

            CollectExpired(_lastRemarkBySubject, now, _settings.SquadEchoWindowSeconds);
            for (int i = 0; i < _expired.Count; i++)
            {
                _lastRemarkBySubject.Remove(_expired[i]);
            }
        }

        /// <summary>Fill <see cref="_expired"/> with the keys older than the given window.</summary>
        /// <param name="times">The bookkeeping to scan.</param>
        /// <param name="now">The current time.</param>
        /// <param name="window">How long an entry stays interesting.</param>
        /// <remarks>
        /// We gather first and delete afterwards because you cannot remove from a
        /// Dictionary while you are enumerating it. The list is a field rather than a
        /// local so that this allocates nothing on the repeat visits.
        /// </remarks>
        private void CollectExpired(Dictionary<long, float> times, float now, float window)
        {
            _expired.Clear();

            foreach (KeyValuePair<long, float> entry in times)
            {
                if (now - entry.Value >= window)
                {
                    _expired.Add(entry.Key);
                }
            }
        }
    }
}
