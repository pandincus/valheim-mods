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

            string name = Localization.instance.Localize("$biome_" + biome.ToString().ToLower());

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
    /// <c>{biome}</c> costs nothing here: a sunrise line can name what it is coming
    /// up over without us going and asking.
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
                World.Announce(ChatterEvent.Dawn, biome);
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
                World.Announce(ChatterEvent.Nightfall, biome);
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
                if (__instance == Player.m_localPlayer)
                {
                    World.Announce(ChatterEvent.BiomeChanged, biome);
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
    /// <c>OnDeactivate</c> takes a flag saying whether the event ran its course or
    /// was simply cancelled, and only the first is worth a line - a raid you walked
    /// away from is not one you survived.
    ///
    /// No token. <c>m_startMessage</c> is tempting and wrong: it is a whole localized
    /// sentence, so "{raid}" inside a line reads as one sentence wedged into another.
    /// The lines react to a raid rather than naming it.
    /// </remarks>
    [HarmonyPatch(typeof(RandomEvent), "OnActivate")]
    internal static class RandomEventStartPatch
    {
        /// <summary>
        /// Catch everything, so a failure here cannot stop vanilla starting the raid.
        /// </summary>
        private static void Postfix()
        {
            try
            {
                World.Announce(ChatterEvent.Raid, Heightmap.Biome.None);
            }
            catch (Exception e)
            {
                ChattyBonesPlugin.Log.LogWarning("ChattyBones stumbled over a raid: " + e);
            }
        }
    }

    /// <summary>Reacts to the raid being over.</summary>
    [HarmonyPatch(typeof(RandomEvent), "OnDeactivate")]
    internal static class RandomEventEndPatch
    {
        /// <summary>
        /// Catch everything, for the same reason the start does.
        /// </summary>
        /// <param name="end">True when the raid ran its course rather than being called off.</param>
        private static void Postfix(bool end)
        {
            try
            {
                if (end)
                {
                    World.Announce(ChatterEvent.RaidEnded, Heightmap.Biome.None);
                }
            }
            catch (Exception e)
            {
                ChattyBonesPlugin.Log.LogWarning("ChattyBones stumbled over the end of a raid: " + e);
            }
        }
    }

    /// <summary>The one place the world events go, so they all read the same.</summary>
    internal static class World
    {
        /// <summary>Let somebody in the squad remark on something the world did.</summary>
        /// <param name="kind">What happened.</param>
        /// <param name="biome">Where, or None when the event is not about a place.</param>
        /// <remarks>
        /// Subject 0 throughout, so the echo window never applies. These are about the
        /// world rather than about a kind of thing, and there is only one world - two
        /// sunrises are never the same sunrise, so there is nothing for the echo check
        /// to usefully dedupe.
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
