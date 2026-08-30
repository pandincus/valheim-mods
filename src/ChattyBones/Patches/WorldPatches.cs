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
        /// <returns>The localized name, or null when there is nothing to say.</returns>
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

            string name = Localization.instance.Localize("$biome_" + biome.ToString().ToLowerInvariant());

            return string.IsNullOrEmpty(name) ? null : name;
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
    /// <c>OnActivate</c> is where vanilla shows its own centre-screen warning, so
    /// hooking it means the squad reacts at the moment you are told rather than a
    /// beat later.
    ///
    /// No token. <c>m_startMessage</c> is tempting and wrong: it is a whole localized
    /// sentence, so "{raid}" inside a line reads as one sentence wedged into another.
    /// The lines react to a raid rather than naming it.
    ///
    /// Both ends need guarding, and neither guard is obvious from the method names.
    ///
    /// RandEventSystem re-activates the event on every fixed update that finds you
    /// inside its range and deactivates it on every one that does not, with no
    /// hysteresis - so chasing something past the boundary and coming back re-runs
    /// OnActivate. Vanilla is quiet about that because its own warning is behind a
    /// <c>m_firstActivation</c> flag; ours needs the same, which is what
    /// <see cref="Raids"/> keeps.
    ///
    /// And <c>end</c> does not mean "ran its course", which is what the first version
    /// of this said. SetForcedEvent passes it too, and forced events include boss
    /// fights - so walking far enough from a live boss to drop its health bar would
    /// otherwise congratulate you on surviving something you ran away from. Requiring
    /// that we announced the start first is what closes it.
    ///
    /// One consequence worth knowing rather than guarding: boss fights and event
    /// zones reach this the same way a raid does. Engaging Eikthyr says "something is
    /// coming" and killing it says "we saw it off", which the lines are general enough
    /// to carry, and which is arguably the better behaviour anyway.
    /// </remarks>
    [HarmonyPatch(typeof(RandomEvent), "OnActivate")]
    internal static class RandomEventStartPatch
    {
        /// <summary>
        /// Catch everything, so a failure here cannot stop vanilla starting the raid.
        /// </summary>
        /// <param name="__instance">The event starting.</param>
        private static void Postfix(RandomEvent __instance)
        {
            try
            {
                if (Raids.Starting(__instance))
                {
                    WorldEvents.Announce(ChatterEvent.Raid, Heightmap.Biome.None);
                }
            }
            catch (Exception e)
            {
                ChattyBonesPlugin.Log.LogWarning("ChattyBones stumbled over a raid: " + e);
            }
        }
    }

    /// <summary>Remembers which raid we have already announced.</summary>
    /// <remarks>
    /// Vanilla keeps the equivalent as <c>m_firstActivation</c> on the event itself,
    /// which is per-clone and therefore not ours to read. This is the same idea by
    /// name, which survives the event object being rebuilt.
    /// </remarks>
    internal static class Raids
    {
        /// <summary>The raid we last said something about, or null.</summary>
        private static string _announced;

        /// <summary>Is this a raid we have not already announced?</summary>
        /// <returns>True the first time a given raid activates.</returns>
        /// <param name="raid">The event starting.</param>
        internal static bool Starting(RandomEvent raid)
        {
            string name = raid?.m_name;

            if (string.IsNullOrEmpty(name) || name == _announced)
            {
                return false;
            }

            _announced = name;
            return true;
        }

        /// <summary>Is this the end of a raid we announced the start of?</summary>
        /// <returns>True once, for a raid we spoke about.</returns>
        /// <param name="raid">The event ending.</param>
        /// <remarks>
        /// Requiring that we announced the start is what keeps a boss walking off our
        /// screen from reading as a raid survived.
        /// </remarks>
        internal static bool Ending(RandomEvent raid)
        {
            if (raid == null || raid.m_name != _announced)
            {
                return false;
            }

            _announced = null;
            return true;
        }
    }

    /// <summary>Reacts to the raid being over.</summary>
    [HarmonyPatch(typeof(RandomEvent), "OnDeactivate")]
    internal static class RandomEventEndPatch
    {
        /// <summary>
        /// Catch everything, for the same reason the start does.
        /// </summary>
        /// <param name="__instance">The event ending.</param>
        /// <param name="end">False when you simply walked out of range, which is not an ending.</param>
        private static void Postfix(RandomEvent __instance, bool end)
        {
            try
            {
                if (end && Raids.Ending(__instance))
                {
                    WorldEvents.Announce(ChatterEvent.RaidEnded, Heightmap.Biome.None);
                }
            }
            catch (Exception e)
            {
                ChattyBonesPlugin.Log.LogWarning("ChattyBones stumbled over the end of a raid: " + e);
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
