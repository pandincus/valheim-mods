using System.Collections.Generic;
using ChattyBones.Logic;
using UnityEngine;

namespace ChattyBones
{
    /// <summary>
    /// Rides on one summoned skeleton and remembers what it was doing a moment ago.
    /// </summary>
    /// <remarks>
    /// Attached by a postfix on Tameable.Awake. There is no Update here on purpose -
    /// <see cref="Chatter.Tick"/> drives the whole squad in one pass, so the order in
    /// which skeletons get their turn is ours rather than Unity's.
    ///
    /// One of these goes on every summon on every client, but only the ZDO's owner
    /// decides anything: it is the only machine where the AI is running, so it is the
    /// only one that can see a target being picked. Every client reads what is in the
    /// ZDO and draws it, the owner included. <see cref="IsOwned"/> is the line between
    /// deciding and drawing, not between drawing and doing nothing.
    /// </remarks>
    internal sealed class ChatterComponent : MonoBehaviour
    {
        /// <summary>Every skeleton of ours currently loaded.</summary>
        /// <remarks>
        /// Maintained here rather than searched for. The alternative is
        /// <see cref="Summons.TryFindNearest"/>, which walks every loaded creature and
        /// does two GetComponents per entry - acceptable for a console command you
        /// type once, and not for a sweep running four times a second across a squad.
        ///
        /// Exposed as the live list rather than a copy, because copying it four times
        /// a second to avoid a hazard that has not happened would be the more
        /// expensive habit. The sweep does not add or remove entries.
        /// </remarks>
        internal static List<ChatterComponent> All { get; } = [];

        private BaseAI _ai;
        private ZNetView _view;

        /// <summary>Whether the last sweep saw a target, which is not the same as having one now.</summary>
        private bool _hadTarget;
        private Character _lastTarget;
        private int _lastTargetPrefab;
        private string _lastTargetName;

        private float _untilIdle;

        /// <summary>When we last actually saw the target we are remembering.</summary>
        private float _lastSawTargetAt = float.NegativeInfinity;

        /// <summary>The skeleton this is riding on.</summary>
        internal Character Character { get; private set; }

        /// <summary>A stable id for the budget to key this speaker by.</summary>
        /// <remarks>
        /// The same value <see cref="Speech"/> hands to Chat, and for the same reason:
        /// it is derived from the ZDOID, so it survives a zone reload and is identical
        /// on every client.
        ///
        /// Worked out on first use rather than in Awake. We are attached from a postfix
        /// on Tameable.Awake, and component Awake order within a GameObject is
        /// undefined - so the ZNetView may not have its ZDO yet at the moment we are
        /// added, and asking for the ZDOID then is a null reference. By the time
        /// anybody wants an id, the world is running.
        /// </remarks>
        internal long SpeakerId => field != 0L ? field : (field = ComputeSpeakerId());

        /// <summary>Which personality this one was given at summon.</summary>
        /// <remarks>
        /// Worked out once and remembered, but only for as long as the pack it was
        /// worked out against. A skeleton stores a position in the pack's sorted list
        /// of personalities, so renaming one, or adding or removing one, changes what
        /// that position means - all ordinary things to do while writing a pack.
        /// Comparing against <see cref="Chatter.PackGeneration"/> is what makes an edit
        /// show up on the squad standing in front of you rather than on the next one
        /// you summon.
        ///
        /// Only a real answer is remembered, because null is not "no personality" - it
        /// is the ZDO not being ready, an owner who has not assigned one yet, or a pack
        /// with nothing but common lines in it. The first two are worth asking about
        /// again, and the third costs an early return each time.
        /// </remarks>
        internal string Personality
        {
            get
            {
                if (field != null && _personalityGeneration == Chatter.PackGeneration)
                {
                    return field;
                }

                string resolved = ResolvePersonality();

                if (resolved != null)
                {
                    field = resolved;
                    _personalityGeneration = Chatter.PackGeneration;
                }

                return resolved;
            }
        }

        /// <summary>Which pack the personality above was worked out against.</summary>
        private int _personalityGeneration = -1;

        private void Awake()
        {
            Character = GetComponent<Character>();
            _ai = GetComponent<BaseAI>();
            _view = GetComponent<ZNetView>();

            _untilIdle = NextIdleGap();
        }

        /// <summary>Derive this skeleton's id from its ZDOID.</summary>
        /// <returns>A stable non-zero id, or 0 while the ZDO is not available.</returns>
        /// <remarks>
        /// Returning 0 leaves <see cref="SpeakerId"/> uncached, so the next caller
        /// tries again. SpeechFormat.SenderId never returns 0 for a real ZDOID, which
        /// is what makes 0 usable as "not worked out yet" - there is a test for it.
        /// </remarks>
        private long ComputeSpeakerId()
        {
            if (Character == null || _view == null || !_view.IsValid())
            {
                return 0L;
            }

            ZDOID id = Character.GetZDOID();

            return SpeechFormat.SenderId(id.UserID, id.ID);
        }

        private void OnEnable()
        {
            All.Add(this);
        }

        private void OnDisable()
        {
            _ = All.Remove(this);
        }

        /// <summary>Say hello, but only if we were just raised.</summary>
        /// <remarks>
        /// Skeletons are re-instantiated every time you walk back into their zone, so
        /// Start fires again and again over one skeleton's life. Only the first of
        /// those is a summoning.
        ///
        /// GetTimeSinceSpawned reads a timestamp written into the ZDO when the
        /// creature first existed, so it survives the reload that Start does not - a
        /// skeleton raised ten minutes ago reports six hundred seconds however many
        /// times you have walked past it since.
        ///
        /// It also writes that timestamp when it finds none, and unlike BaseAI.Awake,
        /// which guards the same write, it does no owner check of its own. So the
        /// IsOwned test below has to stay above it: writing to a ZDO we do not own
        /// pushes our copy's data revision past the world's, after which the real
        /// owner's updates are discarded until it catches up.
        /// </remarks>
        private void Start()
        {
            if (!IsOwned || _ai == null)
            {
                return;
            }

            if (_ai.GetTimeSinceSpawned().TotalSeconds > ModConfig.SummonGreetingSeconds.Value)
            {
                return;
            }

            if (Chatter.TrySpeak(this, ChatterEvent.Summoned, subject: 0, targetName: null, companion: null))
            {
                // Only if it actually introduced itself, so a squad raised in one breath
                // gets one exchange rather than a welcome each. The reference is how
                // SpeakAny knows not to have the newcomer welcome itself; no resolved
                // name is needed, because unlike a death the subject is still standing.
                _ = Chatter.SpeakAny(
                    ChatterEvent.CompanionSummoned,
                    subject: 0,
                    targetName: null,
                    companion: Character);
            }
        }

        /// <summary>Look at what this skeleton is up to. Called by <see cref="Chatter.Tick"/>.</summary>
        /// <param name="dt">Seconds since the previous sweep.</param>
        /// <remarks>
        /// Two things are found by looking rather than by being told: a target
        /// appearing, and a target dying. The second is why the first has to remember
        /// more than a reference - by the time a creature is dead the game has often
        /// destroyed it and put a ragdoll in its place, so its name and prefab have to
        /// have been taken while it was still there.
        ///
        /// A target we were following going *away* is the kill signal, and it does not
        /// only go away by becoming null. MonsterAI re-picks a target on its own timer,
        /// so a skeleton in a pack often steps straight from the greydwarf it just
        /// killed to the next one, and an earlier version that only watched for null
        /// missed those kills entirely - roughly one in eight, and precisely in the
        /// crowded fights where the squad has most to say.
        ///
        /// Both halves of that have to be asked separately, which is not obvious and is
        /// explained at the comparison below.
        /// </remarks>
        internal void Sweep(float dt)
        {
            // Asked on every sweep rather than only while somebody else owns it. A
            // client can be handed ownership in the same packet that carries the line
            // the old owner just said - ZDOMan applies the owner change and the data
            // together - and under the old shape that line was dropped on the spot and
            // then said minutes later, when ownership went back and the counter finally
            // looked new. Safe while we own it because OnSpoke records what we said
            // ourselves; see ListenState.
            //
            // Having drawn a line we stop for the sweep, because only one of them can
            // be seen: Speech.Say keys world text by talker, so a second line over the
            // same skeleton in the same frame replaces the first. The first owned sweep
            // after a handover is where two would meet - Forget has just cleared the
            // target, so TargetAcquired fires again immediately - and the line we were
            // handed is the one that would have lost. Our own bookkeeping waits a
            // quarter of a second.
            bool drew = Listen();

            // Folded together because both answers mean the same thing here: there is
            // nothing for us to decide. Somebody else's skeleton is drawn for above,
            // and one of ours with no AI is one we can learn nothing from.
            //
            // Ahead of the stop above, because Forget has to happen on every sweep we
            // do not own. Skipping it for the one sweep we happened to hear something
            // leaves a dead creature in _lastTarget, and if ownership comes back on the
            // very next sweep we boast about a kill we just finished listening to.
            if (!IsOwned || _ai == null)
            {
                Forget();
                return;
            }

            if (drew)
            {
                return;
            }

            CheckHome();

            Character target = _ai.GetTargetCreature();

            // Two questions, and they have to be asked with two different operators.
            // "Is there a target" wants Unity's ==, which counts a destroyed object as
            // absent. "Is it the one we were following" wants reference identity,
            // because Unity's == would call a destroyed target and a missing one the
            // same thing - which is what silently swallowed every kill that ended a
            // fight. TargetWatch has the full story and the tests.
            bool targetPresent = target != null;
            bool sameTarget = ReferenceEquals(target, _lastTarget);

            if (TargetWatch.LostTarget(_hadTarget, targetPresent, sameTarget))
            {
                Settle();
            }

            if (target != null)
            {
                _lastSawTargetAt = Time.time;

                if (!_hadTarget)
                {
                    Remember(target);
                    _ = Chatter.TrySpeak(this, ChatterEvent.TargetAcquired, _lastTargetPrefab, _lastTargetName, companion: null);
                }

                _untilIdle = NextIdleGap();
                return;
            }

            _untilIdle -= dt;
            if (_untilIdle <= 0f)
            {
                _untilIdle = NextIdleGap();

                // No companion passed, and that is not a loss: Idle does not name a
                // particular skeleton, so Chatter fills one in from whoever is standing
                // about, exactly as it does for every other event now. Handing one over
                // here also meant looking one up before the budget had said yes, on
                // every idle timer that expired.
                _ = Chatter.TrySpeak(
                    this,
                    ChatterEvent.Idle,
                    subject: 0,
                    targetName: null,
                    companion: null);
            }
        }

        /// <summary>Whether it was near your bed last time we looked.</summary>
        private bool _atHome;

        /// <summary>How many sweeps in a row it has looked to be outside.</summary>
        /// <remarks>
        /// The hysteresis, and it earns its keep more here than it would have against
        /// a boundary. Being safe at home is a conjunction of several conditions and
        /// some of them flicker - anything that targets you and can see or hear you
        /// drops Resting until about a second after it stops. So leaving takes
        /// agreeing with itself several times while arriving takes one. Wrong in the
        /// forgiving direction: the worst it does is call a squad home a few seconds
        /// longer than they were.
        /// </remarks>
        private int _sweepsOutside;

        /// <summary>How many sweeps outside before we believe it has actually left.</summary>
        private const int SweepsBeforeLeaving = 4;

        /// <summary>Sweeps still to skip before asking about shelter again.</summary>
        private int _untilHomeCheck;

        /// <summary>How many sweeps to skip between shelter checks.</summary>
        /// <remarks>
        /// Not for cost - IsSafeInHome is a bool field read, and vanilla recomputes it
        /// every fixed update whether we ask or not. It is for the hysteresis, which
        /// this is what sets the scale of: four consecutive misses at one check every
        /// two seconds is eight seconds of being away before the squad stops calling
        /// it home.
        ///
        /// An earlier version of this comment justified the throttle on the cost of a
        /// physics overlap, which was true of the query it replaced and not of this
        /// one.
        /// </remarks>
        private const int SweepsBetweenHomeChecks = 8;

        /// <summary>Notice when you are properly settled at home.</summary>
        /// <remarks>
        /// Vanilla's own answer, and it took two wrong ones to go and look for it.
        /// <c>Player.IsSafeInHome()</c> is public, and the game trusts it enough to
        /// bill your TimeInBase statistic against it.
        ///
        /// It unpacks to Resting, and under a roof, and within twenty metres of a
        /// base area - where Resting is itself near a fire, sheltered or sitting, not
        /// cold, not freezing, not on fire, not wet unless somewhere cozy, and
        /// unnoticed by anything.
        ///
        /// Worth reading that list once, because the base area in it is the same
        /// workbench-projected area that made the first attempt at this fire from a
        /// hundred metres out. It was never the wrong signal; it was the wrong signal
        /// on its own. Conjoined with a fire and a roof and nothing hunting you, it
        /// means what it looked like it meant.
        ///
        /// The cost is that this is about being settled rather than about arriving:
        /// walk in, craft for ten minutes and walk out without ever standing by the
        /// fire, and it never fires. That reads as the better moment - a skeleton
        /// remarking on it once everyone has stopped moving is closer to what the line
        /// is for than one triggered by crossing an invisible circle.
        /// </remarks>
        private void CheckHome()
        {
            if (--_untilHomeCheck > 0)
            {
                return;
            }

            _untilHomeCheck = SweepsBetweenHomeChecks;

            bool inside = Player.m_localPlayer != null && Player.m_localPlayer.IsSafeInHome();

            if (inside)
            {
                _sweepsOutside = 0;

                if (!_atHome)
                {
                    _atHome = true;
                    _ = Chatter.TrySpeak(this, ChatterEvent.AtHome, subject: 0, targetName: null, companion: null);
                }

                return;
            }

            if (!_atHome)
            {
                return;
            }

            _sweepsOutside++;
            if (_sweepsOutside >= SweepsBeforeLeaving)
            {
                _atHome = false;
            }
        }

        /// <summary>Work out what became of the target we were following, and say so.</summary>
        /// <remarks>
        /// A destroyed Character compares equal to null, so "gone" and "dead" arrive as
        /// the same answer and both mean the fight is over. Health rather than
        /// IsDead(): Character.IsDead() is a flat false that only Player overrides, so
        /// asking a greydwarf always says no, and it is the null that has been carrying
        /// this check.
        ///
        /// The staleness test is what stops a boast about something killed ten minutes
        /// ago. Sweeping can stop and restart - the master switch is advertised as safe
        /// to flip mid-game, and ownership can move away and back - and without this,
        /// the first sweep after the gap would find a target it last saw in another era
        /// and gloat about it by name.
        /// </remarks>
        private void Settle()
        {
            float since = Time.time - _lastSawTargetAt;

            _hadTarget = false;

            // Ordered so the null check still short-circuits: a destroyed Character
            // compares equal to null, and asking a destroyed object for its health
            // throws.
            bool gone = _lastTarget == null || _lastTarget.GetHealth() <= 0f;
            bool worth = TargetWatch.WorthRemarking(since, gone);

            if (ModConfig.LogChatter.Value)
            {
                Chatter.Trace(this, ChatterEvent.Killed, "lost its target after " + since.ToString("0.00")
                    + "s, dead or gone: " + gone + (worth ? " -> gloating" : " -> dropped"));
            }

            if (worth)
            {
                Boast();
            }

            _lastTarget = null;
            _untilIdle = NextIdleGap();
        }

        /// <summary>Drop everything we were remembering about a fight.</summary>
        private void Forget()
        {
            _hadTarget = false;
            _lastTarget = null;
            _lastTargetName = null;
            _lastTargetPrefab = 0;
        }

        /// <summary>Mark the kill - by the one who made it, or by somebody standing nearby.</summary>
        /// <remarks>
        /// The killer gets first refusal and usually has to decline, which is the
        /// whole reason this is not one line. A skeleton that announced its target a
        /// few seconds ago is still inside its own
        /// <see cref="ChatterSettings.SpeakerCooldownSeconds"/>, and it is the same
        /// skeleton now standing over the body - so left to itself, the kill would
        /// almost never get mentioned by the one that earned it.
        ///
        /// The cooldown is per speaker, so handing it to the squad is what gets past
        /// it, and <see cref="ChatterEvent.CompanionKilled"/> exists so the line can
        /// be addressed to the killer by name rather than being a bystander narrating
        /// somebody else's work.
        /// </remarks>
        private void Boast()
        {
            // No blow to read on a kill, so the weapon comes from its own hands.
            LineDetails details = Hits.WieldedBy(Character);

            if (Chatter.TrySpeak(this, ChatterEvent.Killed, _lastTargetPrefab, _lastTargetName, companion: null, details: details))
            {
                return;
            }

            _ = Chatter.SpeakAny(
                ChatterEvent.CompanionKilled,
                _lastTargetPrefab,
                _lastTargetName,
                companion: Character,
                details: details);
        }

        /// <summary>Take everything we will want about a target while it still exists.</summary>
        /// <param name="target">Whatever the skeleton has just decided to attack.</param>
        private void Remember(Character target)
        {
            _hadTarget = true;
            _lastTarget = target;
            _lastTargetPrefab = Summons.PrefabOf(target);
            _lastTargetName = Summons.CreatureName(target);
        }

        /// <summary>Leave a record of what was said, for other clients to find.</summary>
        /// <param name="kind">What was reacted to.</param>
        /// <param name="lineRef">Which line, in the form that survives a different pack.</param>
        /// <param name="subject">The prefab hash the remark was about, or 0.</param>
        /// <param name="companion">The skeleton it named, or <c>ZDOID.None</c>.</param>
        /// <param name="ally">The player it named, or <c>ZDOID.None</c>.</param>
        /// <param name="details">What was known about the event, still as keys.</param>
        /// <remarks>
        /// Five fields, and the utterance goes last. That is habit rather than
        /// necessity - a ZDO is serialized whole, so everything written here reaches a
        /// listener in one piece - but the utterance is the field <see cref="Listen"/>
        /// watches, and writing the thing that triggers the read after the things it
        /// reads is the ordering worth being in the habit of.
        ///
        /// A skeleton and a player get a field each rather than sharing one. Sharing
        /// worked while only the arrival event named a player, and stopped the moment a
        /// line was allowed to want both - "Careful, {ally}! {companion}, with me!" is
        /// two people in one sentence.
        ///
        /// The counter is what makes a repeat visible: two identical remarks in a row
        /// would otherwise write the same int twice and a watcher polling the field
        /// would see no change at all. See <see cref="Utterance.Counter"/>.
        /// </remarks>
        internal void OnSpoke(
            ChatterEvent kind, int lineRef, int subject, ZDOID companion, ZDOID ally, LineDetails details)
        {
            if (!IsOwned)
            {
                return;
            }

            ZDO zdo = _view.GetZDO();

            int previous = 0;
            if (Utterance.TryUnpack(zdo.GetInt(UtteranceKey, 0), 0, out Utterance last))
            {
                previous = last.Counter;
            }

            Utterance said = new(Utterance.NextCounter(previous), kind, lineRef, subject);

            zdo.Set(SubjectKey, subject);
            zdo.Set(CompanionKey, companion);
            zdo.Set(AllyKey, ally);
            zdo.Set(DetailsKey, DetailWire.Pack(details));
            zdo.Set(UtteranceKey, said.Pack());

            // Anything we said ourselves is already accounted for, so that we can never
            // later be told about it once ownership has moved on. See ListenState.
            _heard.Recorded(said.Counter);
        }

        /// <summary>What this client has already been told by this skeleton.</summary>
        private readonly ListenState _heard = new();

        /// <summary>Treat the next look at this skeleton as a first look.</summary>
        internal void ForgetWhatWasHeard()
        {
            _heard.Forget();
        }

        /// <summary>Draw whatever this skeleton has said that we have not accounted for.</summary>
        /// <returns>True when a line reached the screen, so the caller can stop.</returns>
        /// <remarks>
        /// The other half of <see cref="OnSpoke"/>, and the reason one of these rides on
        /// every summon on every client rather than only on the owner's. Polled from the
        /// sweep rather than subscribed to, because a ZDO field has nothing to subscribe
        /// to - four times a second is fast enough that a line lands while the moment is
        /// still the moment. <see cref="ListenState"/> holds the rules about what counts
        /// as news.
        ///
        /// Polling means only the latest line survives a sweep, so a skeleton speaking
        /// twice inside a quarter of a second would be heard once here. It cannot: the
        /// owner's own <see cref="ChatterSettings.SpeakerCooldownSeconds"/> is measured
        /// in seconds, and it is the same skeleton on both sides of the wire.
        /// </remarks>
        private bool Listen()
        {
            if (_view == null || !_view.IsValid())
            {
                return false;
            }

            ZDO zdo = _view.GetZDO();

            bool have = Utterance.TryUnpack(
                zdo.GetInt(UtteranceKey, 0), zdo.GetInt(SubjectKey, 0), out Utterance said);

            if (!_heard.ShouldDraw(have, have ? said.Counter : 0))
            {
                return false;
            }

            return Chatter.Hear(
                this, said, DetailsIn(zdo), zdo.GetZDOID(CompanionKey), zdo.GetZDOID(AllyKey));
        }

        /// <summary>Read everything this skeleton is currently broadcasting.</summary>
        /// <returns>False when it has never said anything.</returns>
        /// <param name="said">The utterance.</param>
        /// <param name="details">What it carried, still as keys.</param>
        /// <param name="companion">The skeleton it named, or <c>ZDOID.None</c>.</param>
        /// <param name="ally">The player it named, or <c>ZDOID.None</c>.</param>
        /// <remarks>
        /// For <c>cb_mirror</c>, which draws a skeleton's own broadcast back at you so
        /// the whole mirrored path can be exercised without a second machine.
        /// <see cref="Listen"/> does not use this: it reads the counter first and only
        /// touches the rest when something has changed, which is the difference between
        /// splitting a string four times a second per skeleton and not.
        /// </remarks>
        internal bool TryReadBroadcast(
            out Utterance said, out LineDetails details, out ZDOID companion, out ZDOID ally)
        {
            said = default;
            details = default;
            companion = ZDOID.None;
            ally = ZDOID.None;

            if (_view == null || !_view.IsValid())
            {
                return false;
            }

            ZDO zdo = _view.GetZDO();

            if (!Utterance.TryUnpack(zdo.GetInt(UtteranceKey, 0), zdo.GetInt(SubjectKey, 0), out said))
            {
                return false;
            }

            details = DetailsIn(zdo);
            companion = zdo.GetZDOID(CompanionKey);
            ally = zdo.GetZDOID(AllyKey);

            return true;
        }

        /// <summary>The details field, whatever state it is in.</summary>
        /// <returns>What was written, or nothing at all.</returns>
        /// <param name="zdo">The skeleton's ZDO.</param>
        private static LineDetails DetailsIn(ZDO zdo)
        {
            _ = DetailWire.TryUnpack(zdo.GetString(DetailsKey), out LineDetails details);

            return details;
        }

        /// <summary>Which personality this skeleton has, assigning one if it has none yet.</summary>
        /// <returns>A personality name from the pack, or null if the pack has none.</returns>
        /// <remarks>
        /// Stored as an index into the pack's sorted personality list, plus one - a
        /// ZDO int nobody has written reads as 0, so the offset is what tells "never
        /// assigned" from "assigned the first personality". The same trick, and the
        /// same reason, as <see cref="Utterance.Counter"/>.
        ///
        /// Only the owner assigns. Everyone else reads whatever is there, and a
        /// skeleton whose owner has not got round to it yet falls back to the shared
        /// personality, which every pack is expected to have.
        /// </remarks>
        private string ResolvePersonality()
        {
            IReadOnlyList<string> available = Chatter.Personalities;
            if (available == null || available.Count == 0 || _view == null || !_view.IsValid())
            {
                return null;
            }

            ZDO zdo = _view.GetZDO();
            int stored = zdo.GetInt(PersonalityKey, 0);

            if (stored <= 0)
            {
                if (!_view.IsOwner())
                {
                    return null;
                }

                stored = Random.Range(0, available.Count) + 1;
                zdo.Set(PersonalityKey, stored);
            }

            // Folded rather than trusted. The value may have been written by a client
            // whose pack had more personalities than ours does.
            return available[(stored - 1) % available.Count];
        }

        /// <summary>Are we the client that gets to decide what this skeleton says?</summary>
        /// <returns>True on the owner of a live skeleton.</returns>
        /// <remarks>
        /// Every client attaches one of these to every summon it can see, so that
        /// another client can later draw somebody else's skeleton talking. Only one of
        /// them may actually decide, and this is the test for which.
        /// </remarks>
        internal bool IsOwned => Character != null && _view != null && _view.IsValid() && _view.IsOwner();

        /// <summary>How long before this one gets bored, with a bit of scatter.</summary>
        /// <returns>Seconds.</returns>
        /// <remarks>
        /// Jittered by a quarter either way so a squad summoned in one breath does not
        /// reach its idle timer in one breath too. They would only ever get one line
        /// out of it between them - the budget would refuse the rest - so without the
        /// scatter the same skeleton would win every time and the other four would
        /// never idle at all.
        /// </remarks>
        private static float NextIdleGap()
        {
            float mean = ModConfig.IdleSeconds.Value;

            return mean * Random.Range(0.75f, 1.25f);
        }

        private static readonly int PersonalityKey = "cb_personality".GetStableHashCode();
        private static readonly int UtteranceKey = "cb_utterance".GetStableHashCode();
        private static readonly int SubjectKey = "cb_subject".GetStableHashCode();
        private static readonly int DetailsKey = "cb_details".GetStableHashCode();

        /// <summary>The keys the two identities are filed under, which are pairs.</summary>
        /// <remarks>
        /// A ZDOID is a long and a uint, and vanilla stores it as two separate fields -
        /// so each "key" is one hash for each half. Worked out once here rather than per
        /// write, since ZDO.Set(string, ZDOID) hashes both halves on every call.
        /// </remarks>
        private static readonly KeyValuePair<int, int> CompanionKey = ZDO.GetHashZDOID("cb_companion");

        /// <summary>Where the other player an utterance named is filed.</summary>
        private static readonly KeyValuePair<int, int> AllyKey = ZDO.GetHashZDOID("cb_ally");
    }
}
