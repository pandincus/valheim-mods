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
    /// only one that can see a target being picked. The rest are here for Phase 6,
    /// which will have them read what the owner left in the ZDO and draw that instead.
    /// <see cref="IsOwned"/> is the line between the two.
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
        internal string Personality => field ??= ResolvePersonality();

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
        /// </remarks>
        private void Start()
        {
            if (!IsOwned || _ai == null)
            {
                return;
            }

            if (_ai.GetTimeSinceSpawned().TotalSeconds <= ModConfig.SummonGreetingSeconds.Value)
            {
                _ = Chatter.TrySpeak(this, ChatterEvent.Summoned, subject: 0, targetName: null, companion: null);
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
        /// </remarks>
        internal void Sweep(float dt)
        {
            if (!IsOwned || _ai == null)
            {
                return;
            }

            Character target = _ai.GetTargetCreature();

            if (target != null)
            {
                if (!_hadTarget || target != _lastTarget)
                {
                    Remember(target);
                    _ = Chatter.TrySpeak(this, ChatterEvent.TargetAcquired, _lastTargetPrefab, _lastTargetName, companion: null);
                }

                _untilIdle = NextIdleGap();
                return;
            }

            if (_hadTarget)
            {
                _hadTarget = false;

                // A destroyed Character compares equal to null, so "gone" and "dead"
                // arrive as the same answer here and both mean the fight is over. It
                // could also have been a zone unloading rather than a kill, which is
                // rare and costs at worst one undeserved boast.
                if (_lastTarget == null || _lastTarget.IsDead())
                {
                    _ = Chatter.TrySpeak(this, ChatterEvent.Killed, _lastTargetPrefab, _lastTargetName, companion: null);
                }

                _lastTarget = null;
                _untilIdle = NextIdleGap();
                return;
            }

            _untilIdle -= dt;
            if (_untilIdle <= 0f)
            {
                _untilIdle = NextIdleGap();
                _ = Chatter.TrySpeak(this, ChatterEvent.Idle, subject: 0, targetName: null, companion: null);
            }
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
        /// <remarks>
        /// Writing only. Nobody reads these yet - mirroring them onto other players'
        /// screens is Phase 6 - but the write belongs with the speaking, and doing it
        /// here means Phase 6 is a poll and a render rather than a rework.
        ///
        /// The counter is what makes a repeat visible: two identical remarks in a row
        /// would otherwise write the same int twice and a watcher polling the field
        /// would see no change at all. See <see cref="Utterance.Counter"/>.
        /// </remarks>
        internal void OnSpoke(ChatterEvent kind, int lineRef, int subject)
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
            zdo.Set(UtteranceKey, said.Pack());
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
        /// Every client attaches one of these to every summon it can see, which is
        /// what Phase 6 will need in order to draw somebody else's skeleton talking.
        /// Only one of those clients may actually decide, though, and this is the
        /// test for which.
        ///
        /// Worth being clear about why the hooks cannot be trusted to check this
        /// themselves. Character.RPC_Damage returns early on a non-owner, but a
        /// Harmony postfix runs regardless of which way the method left - so on a
        /// four-player server, one skeleton getting hit reaches this code four times.
        /// Three of those must come to nothing, and <see cref="Chatter.TrySpeak"/>
        /// asks here rather than leaving it to each hook to remember.
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
    }
}
