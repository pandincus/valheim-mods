using System;
using ChattyBones.Logic;
using HarmonyLib;

namespace ChattyBones.Patches
{
    /// <summary>Names the ground underfoot, for the events that care where they are.</summary>
    /// <remarks>
    /// Vanilla's own spelling, taken from <c>Player.AddKnownBiome</c>: the enum name
    /// lower-cased behind a <c>$biome_</c> prefix. Borrowed rather than invented so a
    /// German player reads "Schwarzwald" where you read "Black Forest", and so the
    /// mod cannot drift from the game's names if one is ever renamed.
    /// </remarks>
    internal static class Biomes
    {
        /// <summary>What to call a biome in a line.</summary>
        /// <returns>Its key, e.g. "$biome_blackforest", or null when there is nothing to say.</returns>
        /// <param name="biome">The biome to name.</param>
        /// <remarks>
        /// None comes back null rather than as a word. It is what Heightmap answers
        /// off the edge of the world and while a zone is still loading, and "you have
        /// arrived in the None" is worse than staying quiet.
        /// </remarks>
        internal static string NameOf(Heightmap.Biome biome)
        {
            if (biome == Heightmap.Biome.None)
            {
                return null;
            }

            return "$biome_" + biome.ToString().ToLowerInvariant();
        }
    }

    /// <summary>Reacts to the sun coming up and going down.</summary>
    /// <remarks>
    /// A Valheim day is <c>m_dayLengthSec</c>, which is 1200 - twenty real minutes -
    /// so these are a rhythm rather than an occasion, and they are ranked with the
    /// small talk to match.
    ///
    /// Both methods are handed the biome they are firing in, which is why
    /// <c>{biome}</c> costs nothing here. It is sampled at the camera rather than at
    /// the player, so in third person near a border it can name the ground behind
    /// you - which nobody will ever notice, and is worth knowing before somebody
    /// reports it as a bug.
    ///
    /// These run wherever EnvMan updates, which is every client - but EnvMan is the
    /// local player's view of the sky, and the events go to the local squad, so
    /// nothing about that needs owner-gating.
    /// </remarks>
    [HarmonyPatch(typeof(EnvMan), "OnMorning")]
    internal static class EnvManMorningPatch
    {
        /// <summary>
        /// Catch everything. Harmony does not wrap a patch body, and this one sits in
        /// the middle of vanilla changing the music.
        /// </summary>
        /// <param name="biome">Where the sun is coming up.</param>
        private static void Postfix(Heightmap.Biome biome)
        {
            try
            {
                WorldEvents.Announce(ChatterEvent.Dawn, biome);
            }
            catch (Exception e)
            {
                ChattyBonesPlugin.Log.LogWarning("ChattyBones stumbled over sunrise: " + e);
            }
        }
    }

    /// <summary>Reacts to the light going.</summary>
    [HarmonyPatch(typeof(EnvMan), "OnEvening")]
    internal static class EnvManEveningPatch
    {
        /// <summary>
        /// Catch everything, for the same reason the sunrise one does.
        /// </summary>
        /// <param name="biome">Where the light is going.</param>
        private static void Postfix(Heightmap.Biome biome)
        {
            try
            {
                WorldEvents.Announce(ChatterEvent.Nightfall, biome);
            }
            catch (Exception e)
            {
                ChattyBonesPlugin.Log.LogWarning("ChattyBones stumbled over nightfall: " + e);
            }
        }
    }

    /// <summary>Reacts to crossing into a different biome.</summary>
    /// <remarks>
    /// Every crossing, not only the first, which is what makes it usable. The method
    /// is called from <c>UpdateBiome</c> whenever the biome differs from the last one
    /// seen, and its own "you have discovered a new biome" banner is guarded
    /// separately inside it - so walking back into the Meadows reaches us and does
    /// not reach the banner.
    ///
    /// UpdateBiome runs on the local player, once a second, so this needs no gating
    /// and cannot fire for somebody else's crossing.
    /// </remarks>
    [HarmonyPatch(typeof(Player), "AddKnownBiome")]
    internal static class PlayerBiomePatch
    {
        /// <summary>
        /// Catch everything. Harmony does not wrap a patch body, and an exception here
        /// would land inside vanilla's biome bookkeeping.
        /// </summary>
        /// <param name="__instance">Whoever crossed the line.</param>
        /// <param name="biome">What they crossed into.</param>
        private static void Postfix(Player __instance, Heightmap.Biome biome)
        {
            try
            {
                // None is what Heightmap answers while a zone is still loading, which
                // is every portal trip and every login - m_currentBiome starts there
                // too. Announcing it spends the squad's quiet time on a line that
                // cannot name where it is, and the real crossing a second later is
                // then refused for arriving too soon after it.
                if (biome != Heightmap.Biome.None
                    && Player.m_localPlayer != null
                    && __instance == Player.m_localPlayer)
                {
                    WorldEvents.Announce(ChatterEvent.BiomeChanged, biome);
                }
            }
            catch (Exception e)
            {
                ChattyBonesPlugin.Log.LogWarning("ChattyBones stumbled over a biome: " + e);
            }
        }
    }

    /// <summary>Reacts to a raid starting and to surviving one.</summary>
    /// <remarks>
    /// A question rather than a hook, which is the third answer here and much the
    /// smallest. <c>GetCurrentRandomEvent()</c> is public and returns the raid that
    /// currently exists, so "is there a raid on" is a field read and the two events
    /// are the two edges of it.
    ///
    /// I hooked OnActivate and OnDeactivate first, because OnActivate is where vanilla
    /// puts its own centre-screen warning and reacting on the same frame seemed worth
    /// having. It is not: those two are about the raid being *near you*, not about it
    /// existing, so they fire again every time you cross the boundary, and OnDeactivate
    /// is handed an <c>end</c> flag that does not mean what it reads as - SetForcedEvent
    /// passes it too. Both then needed a guard, and each guard needed the other, which
    /// is when the bookkeeping started to cost more than the quarter second it saved.
    ///
    /// Three bugs came out with it, and they are worth listing because all three were
    /// state going stale rather than logic being wrong: a raid you walked away from
    /// could leave the tracking wedged so no raid of that name was ever announced
    /// again; walking away from a live boss said "we saw it off"; and the tracking
    /// committed before knowing whether anyone had actually spoken.
    ///
    /// A boss gets the arrival and not the departure, and that asymmetry is the whole
    /// point of it. <c>m_randomEvent</c> is only ever a raid, so bosses need their own
    /// question, and the honest one to ask is the health bar: <c>GetActiveBoss</c> is
    /// public and says whether one is up. But a health bar going away means either that
    /// you killed it or that you walked off, and nothing separates those without
    /// holding on to the boss and interrogating it afterwards - which is the stale-state
    /// bookkeeping this class was just rid of. So we say "something is coming" and let
    /// the kill events carry the other end, which they already do rather well.
    ///
    /// No token. <c>m_startMessage</c> is tempting and wrong: it is a whole localized
    /// sentence, so "{raid}" inside a line reads as one sentence wedged into another.
    /// The lines react to a raid rather than naming it.
    /// </remarks>
    internal static class Raids
    {
        /// <summary>Whether a raid was on the last time anybody looked.</summary>
        private static bool _underway;

        /// <summary>Whether a boss had a health bar up the last time anybody looked.</summary>
        private static bool _bossUp;

        /// <summary>Notice a raid arriving or finishing, and a boss arriving.</summary>
        /// <remarks>
        /// Driven from the sweep rather than a patch, so it is late by up to a quarter
        /// of a second. Nobody can tell.
        ///
        /// Deliberately says nothing about whether the line was heard. If the budget
        /// refuses the arrival the departure still speaks, which is a squad seeing off
        /// a raid they never mentioned - mild, and much better than the alternative,
        /// because a flag derived from a live question cannot get stuck the way one we
        /// maintain ourselves can.
        /// </remarks>
        internal static void Poll()
        {
            bool underway = RandEventSystem.instance != null
                && RandEventSystem.instance.GetCurrentRandomEvent() != null;

            if (underway != _underway)
            {
                _underway = underway;

                WorldEvents.Announce(
                    underway ? ChatterEvent.Raid : ChatterEvent.RaidEnded,
                    Heightmap.Biome.None);
            }

            // Walking back into range says it again, because the bar came back and this
            // is all the state there is. Rare, harmless, and the price of not keeping a
            // record of every boss you have ever met.
            bool bossUp = EnemyHud.instance != null && EnemyHud.instance.GetActiveBoss() != null;

            if (bossUp != _bossUp)
            {
                _bossUp = bossUp;

                if (bossUp)
                {
                    WorldEvents.Announce(ChatterEvent.Raid, Heightmap.Biome.None);
                }
            }
        }
    }

    /// <summary>The one place the world events go, so they all read the same.</summary>
    /// <remarks>
    /// Not called World, which is what it was: the game has a type of that name, and
    /// inside this namespace ours would win - so a later patch wanting vanilla's
    /// would get a baffling error rather than the type it asked for.
    /// </remarks>
    internal static class WorldEvents
    {
        /// <summary>Let somebody in the squad remark on something the world did.</summary>
        /// <param name="kind">What happened.</param>
        /// <param name="biome">Where, or None when the event is not about a place.</param>
        /// <remarks>
        /// Subject 0 throughout, so the echo window never applies. It exists to stop
        /// two skeletons both announcing the same greydwarf, and none of these events
        /// is about a kind of thing in that way.
        /// </remarks>
        internal static void Announce(ChatterEvent kind, Heightmap.Biome biome)
        {
            if (!ModConfig.Enabled.Value)
            {
                return;
            }

            _ = Chatter.SpeakAny(
                kind,
                subject: 0,
                targetName: null,
                companion: null,
                details: new LineDetails(biome: Biomes.NameOf(biome)));
        }
    }
}
