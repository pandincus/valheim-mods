using System.Collections.Generic;
using ChattyBones.Logic;
using UnityEngine;

namespace ChattyBones
{
    /// <summary>Keeps track of the other players near you, so the squad can talk to them.</summary>
    /// <remarks>
    /// Two jobs, and they are worth telling apart. <see cref="Poll"/> notices somebody
    /// *arriving* and has the squad say hello - the one event that does nothing at all
    /// on your own, and the only one that needed the mirroring to exist before it was
    /// worth writing, because a greeting only the greeter can see is not a greeting.
    /// <see cref="Nearby"/> answers the quieter question of who is simply *about*, which
    /// is what lets any line at all use {ally}: "So, how are things, {ally}?" in an idle
    /// mutter is passed over on your own and lands when somebody is standing there.
    ///
    /// Both clients run this for their own squad, so walking up to somebody gets you
    /// their skeletons and gives them yours.
    ///
    /// Asked once for the squad rather than once per skeleton, the way raids are, and
    /// measured from the skeletons rather than from you. Nearly all the time those are
    /// the same question, because a summon follows you around - but the version that
    /// reads right is the skeleton's, because the skeleton is the thing they can
    /// see talking. One left behind at your base greets somebody who walks past it, and
    /// does not greet somebody standing next to you a hundred metres away.
    /// </remarks>
    internal static class Allies
    {
        /// <summary>Somebody we have already said hello to, and how long since we saw them.</summary>
        /// <remarks>Named apart from <see cref="Nearby"/>, which answers a different question.</remarks>
        private readonly struct Tracked
        {
            /// <summary>Note a player and how long they have been out of sight.</summary>
            /// <param name="id">Their character.</param>
            /// <param name="missing">Seconds since we last saw them in range.</param>
            internal Tracked(ZDOID id, float missing)
            {
                Id = id;
                Missing = missing;
            }

            /// <summary>Which player.</summary>
            internal ZDOID Id { get; }

            /// <summary>How long they have been continuously away, in seconds.</summary>
            internal float Missing { get; }

            /// <summary>The same entry, one sweep later.</summary>
            /// <returns>A copy with the clock advanced.</returns>
            /// <param name="dt">Seconds since the previous sweep.</param>
            internal Tracked Missed(float dt)
            {
                return new Tracked(Id, Missing + dt);
            }
        }

        /// <summary>Everyone in range, plus everyone recently in range.</summary>
        private static readonly List<Tracked> Seen = [];

        /// <summary>Seconds before a refused greeting may be attempted again.</summary>
        /// <remarks>
        /// One number for the whole squad rather than one per person, so this stays a
        /// throttle and not the "have we greeted them yet" state <see cref="Poll"/>
        /// deliberately does not keep.
        ///
        /// It is here because retrying at the sweep rate is not free: each attempt asks
        /// the budget, resolves a biome and builds two names, five times over, four
        /// times a second, for as long as somebody stands there ungreeted. Usually that
        /// is under a second. For a pack with no AllyArrived lines in it at all, it is the
        /// whole session.
        ///
        /// A second is short enough that a greeting still lands while the moment is the
        /// moment. The only cost is that a brand-new arrival can wait that long when
        /// somebody else's greeting has just been turned down.
        /// </remarks>
        private static float _untilRetry;

        /// <summary>How long to wait before asking again. See <see cref="_untilRetry"/>.</summary>
        private const float RetrySeconds = 1f;

        /// <summary>Look for anybody new. Called once per sweep by <see cref="Chatter.Tick"/>.</summary>
        /// <param name="dt">Seconds since the previous sweep.</param>
        /// <remarks>
        /// **Somebody is recorded only once a skeleton has actually said hello**, which
        /// is the whole of the retry and is why there is no "have we greeted them yet"
        /// flag anywhere. An arrival the budget turns down simply never enters the list,
        /// so the next sweep sees the same person as new and asks again.
        ///
        /// Recording them first and greeting second was the first version and it lost
        /// greetings for good: <see cref="ChatterEvent.AllyArrived"/> ranks below everything
        /// in a fight, on purpose, so meeting somebody mid-fight was refused by the
        /// whole squad and the arrival was spent on the refusal. It could not come back
        /// until they had walked away for a minute and returned.
        ///
        /// Retrying also fixed something the first version did not know it had. Two
        /// people arriving together share one greeting through the echo window, and the
        /// second of them used to be dropped outright; now the squad comes back to them
        /// once the window has passed.
        /// </remarks>
        internal static void Poll(float dt)
        {
            Player me = Player.m_localPlayer;

            if (me == null || ChatterComponent.All.Count == 0)
            {
                Seen.Clear();
                return;
            }

            // Only while it is running, so a long session does not leave this counting
            // down into a large negative for no reason.
            if (_untilRetry > 0f)
            {
                _untilRetry -= dt;
            }

            for (int i = 0; i < Seen.Count; i++)
            {
                Seen[i] = Seen[i].Missed(dt);
            }

            float range = ModConfig.AllyGreetingDistance.Value;
            float rangeSq = range * range;

            List<Player> players = Player.GetAllPlayers();
            for (int i = 0; i < players.Count; i++)
            {
                Player other = players[i];

                if (other == null || ReferenceEquals(other, me) || other.IsDead())
                {
                    continue;
                }

                // Nearest first, so the range test and the skeleton that will do the
                // greeting come out of one walk rather than two.
                ChatterComponent nearest = NearestTo(other.transform.position, out float distanceSq);
                if (nearest == null || distanceSq > rangeSq)
                {
                    continue;
                }

                ZDOID id = other.GetZDOID();
                if (id == ZDOID.None)
                {
                    continue;
                }

                int at = IndexOf(id);
                if (at >= 0)
                {
                    Seen[at] = new Tracked(id, 0f);
                    continue;
                }

                if (_untilRetry > 0f)
                {
                    continue;
                }

                if (Greet(other, nearest))
                {
                    Seen.Add(new Tracked(id, 0f));
                }
                else
                {
                    _untilRetry = RetrySeconds;
                }
            }

            // Backwards, so removing one does not skip the next.
            float forget = ModConfig.AllyGreetingForgetSeconds.Value;
            for (int i = Seen.Count - 1; i >= 0; i--)
            {
                if (Seen[i].Missing >= forget)
                {
                    Seen.RemoveAt(i);
                }
            }
        }

        /// <summary>Have somebody say hail.</summary>
        /// <returns>True if a greeting actually appeared. False means try again later.</returns>
        /// <param name="ally">Whoever has just walked up.</param>
        /// <param name="nearest">The skeleton standing closest to them.</param>
        /// <remarks>
        /// The nearest gets first refusal and the squad covers for it, which is the
        /// same shape as a kill going to the killer first: a greeting from whichever
        /// skeleton happens to be standing closest reads as somebody noticing you, and
        /// one from the far end of the field reads as a bug. But a greeting from
        /// slightly the wrong skeleton is much better than none, so the fallback is
        /// there.
        ///
        /// The subject is the Player prefab, so the whole squad's attempts collapse
        /// into one remark through the echo window. Two different people arriving
        /// within a few seconds of each other therefore share a greeting, which is the
        /// price of keeping the budget's subject map about kinds of thing rather than
        /// particular ones - and a party arriving together wanting one hail rather than
        /// four is arguably the better reading anyway.
        /// </remarks>
        private static bool Greet(Player ally, ChatterComponent nearest)
        {
            int subject = Summons.PrefabOf(ally);

            if (Chatter.TrySpeak(
                    nearest,
                    ChatterEvent.AllyArrived,
                    subject,
                    targetName: null,
                    companion: null,
                    ally: ally))
            {
                return true;
            }

            return Chatter.SpeakAny(
                ChatterEvent.AllyArrived,
                subject,
                targetName: null,
                companion: null,
                ally: ally);
        }

        /// <summary>Whichever of ours is standing closest to a point.</summary>
        /// <returns>The nearest skeleton we own, or null when we have none loaded.</returns>
        /// <param name="point">Where they are standing.</param>
        /// <param name="distanceSq">How far away it is, squared. <c>MaxValue</c> when there is none.</param>
        /// <remarks>
        /// Ours only. <see cref="ChatterComponent.All"/> holds every summon loaded on
        /// this client, other players' included, and one of theirs is no use here - it
        /// would be handed to <see cref="Chatter.TrySpeak"/>, refused for not being
        /// ours, and would have made the range test answer for a skeleton that can
        /// never speak.
        /// </remarks>
        private static ChatterComponent NearestTo(Vector3 point, out float distanceSq)
        {
            ChatterComponent best = null;
            distanceSq = float.MaxValue;

            List<ChatterComponent> squad = ChatterComponent.All;
            for (int i = 0; i < squad.Count; i++)
            {
                Character character = squad[i].Character;
                if (character == null || !squad[i].IsOwned)
                {
                    continue;
                }

                float distance = (character.transform.position - point).sqrMagnitude;
                if (distance < distanceSq)
                {
                    distanceSq = distance;
                    best = squad[i];
                }
            }

            return best;
        }

        /// <summary>Somebody near enough to a skeleton to be talked to, if anybody is.</summary>
        /// <returns>A player within range, chosen at random, or null when it is alone.</returns>
        /// <param name="from">Where the skeleton that is about to speak is standing.</param>
        /// <remarks>
        /// Measured from the speaker rather than from you, which is the rule everything
        /// about a skeleton's surroundings follows. A skeleton told to hold position at
        /// your base does not chat to somebody standing beside you across the valley.
        ///
        /// Asked when a skeleton is about to speak rather than kept as state, because it
        /// only runs once the budget has already said yes - a few times a minute, over a
        /// list that is never more than a handful long.
        ///
        /// Random rather than nearest, and for the same reason
        /// <see cref="Chatter.AnotherOf"/> is: with two people standing together, always
        /// picking the closer one would have the squad addressing one of them all
        /// evening and the other never.
        ///
        /// Only the owner ever calls this. Every other client is handed the ZDOID of
        /// whoever was chosen, so nobody picks a second time and two screens cannot
        /// disagree about who is being spoken to.
        /// </remarks>
        internal static Player Nearby(Vector3 from)
        {
            Player me = Player.m_localPlayer;
            if (me == null)
            {
                return null;
            }

            float range = ModConfig.AllyGreetingDistance.Value;
            float rangeSq = range * range;

            Player found = null;
            int seen = 0;

            List<Player> players = Player.GetAllPlayers();
            for (int i = 0; i < players.Count; i++)
            {
                Player other = players[i];

                if (other == null || ReferenceEquals(other, me) || other.IsDead())
                {
                    continue;
                }

                if ((other.transform.position - from).sqrMagnitude > rangeSq)
                {
                    continue;
                }

                // Reservoir sampling, so one pass gives an even choice without building
                // a list first. With one candidate it takes it; with the second it swaps
                // half the time, and so on.
                seen++;
                if (Random.Range(0, seen) == 0)
                {
                    found = other;
                }
            }

            return found;
        }

        /// <summary>Where a player sits in <see cref="Seen"/>.</summary>
        /// <returns>The index, or -1 if we are not tracking them.</returns>
        /// <param name="id">The player's character.</param>
        private static int IndexOf(ZDOID id)
        {
            for (int i = 0; i < Seen.Count; i++)
            {
                if (Seen[i].Id == id)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
