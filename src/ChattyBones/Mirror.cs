using System;
using System.Collections.Generic;
using ChattyBones.Logic;
using UnityEngine;

namespace ChattyBones
{
    /// <summary>
    /// Turns the identities a skeleton broadcasts back into the words this client
    /// should read.
    /// </summary>
    /// <remarks>
    /// Everything an utterance carries is an identity rather than a word: a prefab
    /// hash for the creature, a ZDOID for the companion or the ally, a
    /// localization key for the weapon. That is what lets a German player read
    /// "Grauzwerg" from the same broadcast that gave you "Greydwarf", and it is what
    /// keeps a player's own name inside their own UGC filter. The cost is that every
    /// client has to do this lookup, and this is where it lives.
    ///
    /// The owner runs <see cref="Localize"/> too, which looks odd for a class about
    /// mirroring until you notice that the details are held as keys *because* they
    /// travel. Localizing late is the consequence of that decision rather than a
    /// separate one, so both sides come through here.
    /// </remarks>
    internal static class Mirror
    {
        /// <summary>Turn the keys a hook recorded into the words this client reads.</summary>
        /// <returns>The same details with all seven fields resolved.</returns>
        /// <param name="raw">What the hook worked out, and what goes on the wire.</param>
        /// <remarks>
        /// All seven, which was not always true: weapon skill and damage used to be the
        /// mod's own English words and were passed through raw. They are the game's own
        /// keys now, so a pack can be written in any language.
        ///
        /// That has a consequence worth knowing before adding a field. <see cref="Text"/>
        /// answers null for a key it cannot resolve, and a null token makes a line
        /// unrenderable - so a key we spell wrong does not fall back to English, it makes
        /// every line using that token quietly unavailable. LogChatter names the key.
        /// </remarks>
        internal static LineDetails Localize(LineDetails raw)
        {
            return new LineDetails(
                weapon: Text(raw.Weapon),
                weaponSkill: Text(raw.WeaponSkill),
                damage: Text(raw.Damage),
                status: Text(raw.Status),
                biome: Text(raw.Biome),
                item: Text(raw.Item),
                skill: Text(raw.Skill));
        }

        /// <summary>What a creature of this kind is called here.</summary>
        /// <returns>The localized name, or null when we cannot find the prefab.</returns>
        /// <param name="prefabHash">The subject of an utterance, as <see cref="Summons.PrefabOf"/> wrote it.</param>
        /// <remarks>
        /// Null for a prefab this client does not have, which is what a modded creature
        /// looks like to somebody without that mod. The line asking for {target} is then
        /// passed over and the listener says something else - see
        /// <see cref="LinePack.TryPickRenderable"/>.
        /// </remarks>
        internal static string CreatureName(int prefabHash)
        {
            if (prefabHash == 0 || ZNetScene.instance == null)
            {
                return null;
            }

            GameObject prefab = ZNetScene.instance.GetPrefab(prefabHash);
            if (prefab == null)
            {
                return null;
            }

            return Summons.CreatureName(prefab.GetComponent<Character>());
        }

        /// <summary>What to call whoever an utterance was about.</summary>
        /// <returns>Their name, or null when they are not loaded here.</returns>
        /// <param name="id">The ZDOID out of the skeleton's ZDO, or <c>ZDOID.None</c>.</param>
        /// <remarks>
        /// {companion} and {ally} arrive as a ZDOID each - see
        /// <see cref="ChatterComponent.OnSpoke"/> for why they are not one field. Both
        /// answers come from the same method name on two different components, and both
        /// of those apply the game's own UGC filter, which is the entire reason we send
        /// the identity rather than the string.
        ///
        /// Null when the subject is out of range on this client. Practically speaking
        /// that is rare for the two cases that matter: a companion stands next to the
        /// speaker, and an ally was close enough to be greeted in the first place.
        /// </remarks>
        internal static string NameFor(ZDOID id)
        {
            if (id == ZDOID.None || ZNetScene.instance == null)
            {
                return null;
            }

            GameObject go = ZNetScene.instance.FindInstance(id);
            if (go == null)
            {
                return null;
            }

            Player player = go.GetComponent<Player>();
            if (player != null)
            {
                return Blank(player.GetHoverName());
            }

            Tameable tameable = go.GetComponent<Tameable>();

            return tameable == null ? null : Blank(tameable.GetHoverName());
        }

        /// <summary>What to call a player we already have in hand.</summary>
        /// <returns>Their name, or null when there is nothing to show.</returns>
        /// <param name="player">Whoever walked up.</param>
        /// <remarks>
        /// The owner's side of <see cref="NameFor"/>, and it goes through the same
        /// GetHoverName rather than GetPlayerName so both ends apply this machine's own
        /// UGC filter to the same person.
        /// </remarks>
        internal static string PlayerName(Player player)
        {
            return player == null ? null : Blank(player.GetHoverName());
        }

        /// <summary>Who raised this skeleton.</summary>
        /// <returns>Their name, or null when we cannot say.</returns>
        /// <param name="skeleton">One of somebody's summons.</param>
        /// <remarks>
        /// For {player}, which on the owner's own machine is simply the local player
        /// and on everybody else's is this. It is the most-written token in the shipped
        /// pack by some way, so getting it wrong would have had Hella's skeletons
        /// calling her by your name on your screen.
        ///
        /// <c>ZDOVars.s_follow</c> holds the summoner's name and replicates, but it is
        /// the raw string a player typed - so we use it to find the Player and then ask
        /// *them* for a name, which runs it through this machine's own UGC filter. No
        /// match means no token, which sounds worse than it is: a summon follows its
        /// summoner and unsummons when it drifts too far, so a skeleton you can see
        /// almost always comes with the person who raised it.
        ///
        /// Told to stay put, a skeleton has its follow field blanked by
        /// <c>Tameable.RPC_Command</c> - vanilla writes an empty string there - and
        /// this quietly gives up. That is a real gap and it is the cheap way round; a
        /// second ZDO field holding the summoner's identity would close it, and is not
        /// worth a field until somebody notices.
        /// </remarks>
        internal static string SummonerName(Character skeleton)
        {
            if (skeleton == null)
            {
                return null;
            }

            ZNetView view = skeleton.GetComponent<ZNetView>();
            if (view == null || !view.IsValid())
            {
                return null;
            }

            string follow = view.GetZDO().GetString(ZDOVars.s_follow);
            if (string.IsNullOrEmpty(follow))
            {
                return null;
            }

            List<Player> players = Player.GetAllPlayers();
            for (int i = 0; i < players.Count; i++)
            {
                if (players[i].GetPlayerName() == follow)
                {
                    return Blank(players[i].GetHoverName());
                }
            }

            return null;
        }

        /// <summary>Localize a key, if there is one, anybody to ask, and an answer.</summary>
        /// <returns>The finished words, or null when this client cannot translate it.</returns>
        /// <param name="key">A localization key such as "$item_sword_iron", or null.</param>
        /// <remarks>
        /// Null for a key we have no translation for, and that is the point of the
        /// check rather than a nicety. Vanilla answers an unknown token with the token
        /// in brackets - <c>Localization.Translate</c> ends <c>return "[" + word +
        /// "]"</c> - so without this, somebody running an item mod you do not have has
        /// their skeleton say "That [item_modsword_frost] stings!" on your screen.
        /// Answering null instead sends the line back to
        /// <see cref="LinePack.TryPickRenderable"/>, which walks on to one we can say.
        /// That is the same answer <see cref="CreatureName"/> gives for a prefab we do
        /// not have, which is the same situation arriving by a different road.
        ///
        /// Compared exactly rather than looked for, because a real translation is
        /// perfectly entitled to contain a bracket.
        ///
        /// Said in the log rather than on screen, and the split is the point. Rendering
        /// it would tell you something true - that somebody has content you do not -
        /// which is why it was tempting to leave it. But the pack header teaches that a
        /// bracketed thing over a skeleton's head is a mistake in *your* file that you
        /// can go and fix, and this is neither yours nor fixable, so it would be the
        /// same signal meaning something else. It goes to LogChatter, where somebody
        /// asking the question can find the answer, and never to a warning - it is
        /// ordinary and outside anybody's control, which is the shape of thing this mod
        /// has already had to delete once. See the remarks on <see cref="EventTokens"/>.
        /// </remarks>
        private static string Text(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return null;
            }

            if (Localization.instance == null)
            {
                return key;
            }

            string words = Localization.instance.Localize(key);

            if (key[0] == '$'
                && string.Equals(words, "[" + key.Substring(1) + "]", StringComparison.Ordinal))
            {
                if (ModConfig.LogChatter.Value)
                {
                    ChattyBonesPlugin.Log.LogInfo(
                        "[chatter] no translation for " + key + " - probably a mod the other"
                        + " player has and you do not. Passing the line over.");
                }

                return null;
            }

            return Blank(words);
        }

        /// <summary>Empty is not a value.</summary>
        /// <returns>The string, or null if there was nothing in it.</returns>
        /// <param name="value">Whatever we resolved.</param>
        /// <remarks>
        /// <see cref="LineTokens.TryRender"/> refuses a line whose token is null and
        /// renders one whose token is blank, so an empty answer here would put "Hail, !"
        /// on screen instead of quietly choosing another line.
        /// </remarks>
        private static string Blank(string value)
        {
            return string.IsNullOrEmpty(value) ? null : value;
        }
    }
}
