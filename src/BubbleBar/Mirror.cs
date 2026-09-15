using System.Collections.Generic;
using BubbleBar.Logic;

namespace BubbleBar
{
    /// <summary>
    /// Publishes our own characters' barriers so other clients can draw them, and reads
    /// everybody else's back.
    /// </summary>
    /// <remarks>
    /// Only the client that owns a character runs its status effects, so a barrier on another
    /// player is invisible to us in every sense except the bubble. Vanilla has the same problem
    /// with health and solves it the same way - <c>Character.SetHealth</c> writes the ZDO when
    /// it owns it, <c>GetHealth</c> reads it everywhere - and this is a second value beside it.
    ///
    /// The sweep is deliberately its own pass rather than something bolted onto the one
    /// <see cref="Bars"/> already runs. That one walks EnemyHud's huds, which is what is near
    /// *us*; another player can have our Skelett on their screen at a moment when we have
    /// nothing on ours, and it still has to be publishing.
    /// </remarks>
    internal static class Mirror
    {
        /// <summary>The ZDO keys, hashed once. <c>GetStableHashCode</c> lives in assembly_utils.</summary>
        private static readonly int FractionHash = BubbleWire.FractionKey.GetStableHashCode();
        private static readonly int ExpiryHash = BubbleWire.ExpiryKey.GetStableHashCode();

        /// <summary>How often to publish, in seconds.</summary>
        /// <remarks>
        /// Ten times a second. The bar is read every frame on the other end, so this is the
        /// rate at which somebody else's bar can move - fast enough that a hit lands visibly,
        /// slow enough that the sweep is not on the frame budget.
        /// </remarks>
        private const float SweepSeconds = 0.1f;

        /// <summary>
        /// How long to claim a barrier will last when it does not say.
        /// </summary>
        /// <remarks>
        /// <c>SE_Shield</c> takes its duration from the staff, so in vanilla there is always
        /// one - and <c>SEMan.Update</c> reaps an effect in the same call that crosses its ttl,
        /// so a barrier we can see never has a negative remainder either. This is only ever
        /// reached by a barrier with no ttl at all, which gets a short window instead, renewed
        /// on every sweep for as long as we keep publishing.
        /// </remarks>
        private const float GraceSeconds = 2f;

        private static float _sweepTimer;

        /// <summary>Whether the last sweep published anything, so we retract exactly once.</summary>
        private static bool _publishing;

        /// <summary>Publish every barrier on a character we own. Called once a frame.</summary>
        /// <param name="dt">Seconds since the last frame.</param>
        internal static void Publish(float dt)
        {
            if (!ModConfig.Enabled.Value)
            {
                // Retract, rather than just going quiet. Stopping leaves whatever we last wrote
                // replicating, so every other player keeps a frozen bar over our head until the
                // deadline passes - which can be most of a minute. The kill switch promises the
                // mod does nothing at all, and that has to be true on their screen too.
                if (_publishing)
                {
                    Sweep(retractEverything: true);
                    _publishing = false;
                }

                return;
            }

            _sweepTimer += dt;

            if (_sweepTimer < SweepSeconds)
            {
                return;
            }

            _sweepTimer = 0f;
            _publishing = Sweep(retractEverything: false);
        }

        /// <summary>Walk every character we own, publishing or retracting each.</summary>
        /// <returns>True when at least one barrier was published.</returns>
        /// <param name="retractEverything">Retract regardless of what the character carries.</param>
        /// <remarks>
        /// <c>Character.Instances</c> rather than <c>GetAllCharacters()</c>, and the difference
        /// matters. <c>Instances</c> is the list the game drives its own fixed updates from, so
        /// it holds exactly the characters whose status effects are running; the one behind
        /// <c>GetAllCharacters</c> is keyed on Awake and OnDestroy instead. A character disabled
        /// without being destroyed would sit in the second and not the first, its barrier's
        /// clock frozen while network time kept moving - so the deadline we published would
        /// slide forward a second per second for ever, rewriting the ZDO twice a second and
        /// never expiring on anybody's screen. I could not find a vanilla path into that state,
        /// which is a reason to close it cheaply rather than a reason to leave it open.
        /// </remarks>
        private static bool Sweep(bool retractEverything)
        {
            ZNet net = ZNet.instance;

            if (net == null)
            {
                return false;
            }

            long now = Now(net);
            bool published = false;

            // A hand-rolled loop rather than LINQ: this is every simulating character, ten
            // times a second.
            List<IMonoUpdater> all = Character.Instances;

            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] is not Character c || c == null || c.m_nview == null
                    || !c.m_nview.IsValid() || !c.m_nview.IsOwner())
                {
                    continue;
                }

                ZDO zdo = c.m_nview.GetZDO();

                if (zdo == null)
                {
                    continue;
                }

                SE_Shield shield = null;
                ShieldReading reading = ShieldReading.None;

                if (!retractEverything && Shields.TryGetShield(c, out shield))
                {
                    reading = ShieldReading.For(shield.m_damage, shield.m_totalAbsorbDamage);
                }

                if (!reading.IsPresent)
                {
                    Clear(zdo);
                    continue;
                }

                published = true;

                float remaining = shield.GetRemaningTime();
                long expiry = now + (long)((remaining > 0f ? remaining : GraceSeconds) * 1000f);

                if (BubbleWire.ShouldWrite(zdo.GetFloat(FractionHash, 0f), reading.Fraction))
                {
                    zdo.Set(FractionHash, reading.Fraction);
                }

                if (BubbleWire.ShouldWriteExpiry(zdo.GetLong(ExpiryHash, 0L), expiry))
                {
                    zdo.Set(ExpiryHash, expiry);
                }
            }

            return published;
        }

        /// <summary>Read what another client published about a character.</summary>
        /// <returns>The reading, or <see cref="ShieldReading.None"/> when there is nothing live.</returns>
        /// <param name="zdo">The character's ZDO.</param>
        internal static ShieldReading Read(ZDO zdo)
        {
            ZNet net = ZNet.instance;

            if (zdo == null || net == null)
            {
                return ShieldReading.None;
            }

            return BubbleWire.Read(
                zdo.GetFloat(FractionHash, 0f),
                zdo.GetLong(ExpiryHash, 0L),
                Now(net));
        }

        /// <summary>Network time now, in whole milliseconds.</summary>
        /// <returns>The shared clock every client compares against.</returns>
        /// <param name="net">The live ZNet.</param>
        private static long Now(ZNet net)
        {
            return (long)(net.GetTimeSeconds() * 1000.0);
        }

        /// <summary>Say that a character has no barrier any more.</summary>
        /// <param name="zdo">The character's ZDO, which we own.</param>
        /// <remarks>
        /// Worth doing promptly rather than leaving to the deadline. A barrier that breaks early
        /// - which is the normal way they end - would otherwise stay on everybody else's screen
        /// for whatever was left of its duration, which can be most of a minute.
        ///
        /// The read guard is not just an optimisation: writing zeroes to every character we own
        /// on every sweep would bump the data revision on all of them and replicate the lot.
        ///
        /// Zeroing rather than removing, and the tidier-looking version is broken.
        /// <c>ZDO.RemoveFloat</c> and <c>RemoveLong</c> do not call <c>IncreaseDataRevision</c>,
        /// so a removal never replicates - every other client would keep the stale value until
        /// the deadline passed anyway, which is the whole thing we are trying to avoid.
        /// </remarks>
        private static void Clear(ZDO zdo)
        {
            if (zdo.GetLong(ExpiryHash, 0L) == 0L)
            {
                return;
            }

            zdo.Set(ExpiryHash, 0L);
            zdo.Set(FractionHash, 0f);
        }
    }
}
