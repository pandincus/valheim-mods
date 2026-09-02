using System.Text;

namespace ChattyBones.Logic
{
    /// <summary>
    /// The values a line can drop into itself, and the code that does the dropping.
    /// </summary>
    /// <remarks>
    /// A pack author writes "Get lost, {target}!" and we turn that into "Get lost,
    /// Greydwarf!" at the moment it is said. Twelve tokens, all optional - five for
    /// the people involved, seven for the event itself:
    ///
    /// - {target} is whatever the skeleton is reacting to, already localized
    /// - {player} is you, whoever summoned it
    /// - {name} is the skeleton's own name
    /// - {companion} is another of your skeletons, for lines about each other
    /// - {ally} is another player standing near enough to talk to
    /// - {weapon}, {weapontype}, {damage}, {status}, {biome}, {item} and {skill}
    ///   describe what happened and where, and live on <see cref="LineDetails"/>
    ///
    /// Every client fills these in for itself rather than being sent the words. For
    /// {target} that means a German player reads "Grauzwerg" while you read
    /// "Greydwarf", because only the prefab hash travels.
    ///
    /// {name}, {companion} and {ally} work the same way for a different reason.
    /// Skeletons arrive with a name and you can rename them, and that name already syncs
    /// through the tamed-name field, so every client can read it. We send which
    /// skeleton rather than what it is called, which matters because the game runs
    /// tamed names through CensorShittyWords.FilterUGC and that filtering is
    /// per-user on console and crossplay. Shipping the raw string would route around
    /// somebody else's filter settings; letting each client resolve the name lets it
    /// apply its own. Player names go through the same filter, which is why {ally}
    /// travels as a ZDOID as well.
    ///
    /// I hand-rolled the scan below rather than reaching for a regular expression.
    /// It is a short string with at most a couple of braces in it, this runs every
    /// time anyone speaks, and honestly the loop is about as long as the pattern
    /// would have been.
    /// </remarks>
    internal readonly struct LineTokens
    {
        /// <summary>What the skeleton is reacting to, or null when it is not reacting to anything.</summary>
        internal string Target { get; }

        /// <summary>The player who summoned it, or null if we could not work that out.</summary>
        internal string Player { get; }

        /// <summary>The skeleton's own name, or null if it has not been given one.</summary>
        internal string Name { get; }

        /// <summary>Another skeleton this line is about, or null when there is nobody to name.</summary>
        /// <remarks>
        /// Two quite different things arrive here, and the difference matters when you
        /// are reading a line that used it. On the events
        /// <see cref="EventTokens.PromisedFor"/> marks with Companion, this is a
        /// *particular* skeleton the hook handed over - the one that died, the one that
        /// got the kill - and there is no fallback, so a line naming somebody names the
        /// right somebody. On every other event it is whichever of the squad happened
        /// to be standing about, which is what lets "{companion}, with me!" be written
        /// into a line about you being hurt.
        ///
        /// In a shared world the filled-in one can be another *player's* skeleton, not
        /// only your own. That is deliberate: two people standing together have one
        /// squad between them as far as anybody watching is concerned, and having the
        /// skeletons refuse to address each other across that line would be a strange
        /// thing to insist on. The events that name a particular one are unaffected -
        /// they are handed theirs by a hook that only ever sees yours.
        /// </remarks>
        internal string Companion { get; }

        /// <summary>Another player near enough to be talked to, or null when nobody is.</summary>
        /// <remarks>
        /// Never you, and the same split as <see cref="Companion"/>: the arrival event
        /// names the person who actually arrived, and anywhere else it is whoever is
        /// nearby. Usually null, because most of the time nobody is - which is exactly
        /// what makes it safe to write into an ordinary line.
        ///
        /// Already filtered through the game's own UGC pass, because it is resolved
        /// from a Player rather than read off the wire.
        /// </remarks>
        internal string Ally { get; }

        /// <summary>Gather up whatever we know at the moment of speaking.</summary>
        /// <param name="target">Localized creature name, or null.</param>
        /// <param name="player">Player name, or null.</param>
        /// <param name="name">The skeleton's name, or null.</param>
        /// <param name="companion">Another skeleton's name, or null.</param>
        /// <param name="ally">Another player's name, or null.</param>
        /// <param name="details">What is known about the event itself. Usually nothing.</param>
        /// <remarks>
        /// Five strings in a row is easy to get subtly wrong, and swapping two of
        /// them produces a line that reads almost right. Worth using named arguments
        /// at the call site - the tests all do.
        /// </remarks>
        internal LineTokens(
            string target,
            string player,
            string name,
            string companion = null,
            string ally = null,
            LineDetails details = default)
        {
            Target = target;
            Player = player;
            Name = name;
            Companion = companion;
            Ally = ally;
            Details = details;
        }

        /// <summary>What is known about the event itself, rather than about who is in it.</summary>
        internal LineDetails Details { get; }

        /// <summary>Fill a template in, if we have everything it asks for.</summary>
        /// <returns>
        /// False when the template wants a token we do not have a value for, and in
        /// that case the caller should pick a different line or stay quiet.
        ///
        /// This is deliberately a refusal rather than a blank. "Get lost, {target}!"
        /// on an event with no target would otherwise render as "Get lost, !", which
        /// looks like the mod is broken. Refusing means a pack author who puts a
        /// {target} in an idle line gets silence on that line and working lines
        /// everywhere else, which is a much gentler way to find out.
        ///
        /// Worth being precise about the multiplayer side, because it is tempting to
        /// state this more strongly than it deserves. Every client works its values
        /// out from the same broadcast information, so nobody ends up with a
        /// *different* value for a token - which is the failure that would matter,
        /// two players seeing the same skeleton say two different things.
        ///
        /// It is not quite true that everyone always agrees on whether a token can be
        /// filled at all. A remote client may not have the companion loaded, or may
        /// not know a creature we can name. When that happens it stays quiet while
        /// the owner speaks. A missing remark is a much smaller problem than a
        /// contradictory one, but it is not nothing, and whatever mirrors these to
        /// other players should expect it rather than be surprised by it.
        /// </returns>
        /// <param name="template">The raw line from the pack, braces and all.</param>
        /// <param name="rendered">The finished line, or null when we return false.</param>
        /// <remarks>
        /// An unknown token like {targat} is left exactly as it is rather than being
        /// stripped or refused, so a typo shows up in game as itself. That seemed
        /// much friendlier for someone writing a pack than silently swallowing it -
        /// you see "{targat}" floating over a skeleton's head and you know instantly
        /// what you did.
        ///
        /// A lone opening brace with no closing one is left alone too, on the same
        /// reasoning.
        /// </remarks>
        internal bool TryRender(string template, out string rendered)
        {
            rendered = null;

            if (template == null)
            {
                return false;
            }

            StringBuilder sb = new(template.Length + 16);
            int i = 0;

            while (i < template.Length)
            {
                char c = template[i];
                if (c != '{')
                {
                    sb.Append(c);
                    i++;
                    continue;
                }

                int close = template.IndexOf('}', i + 1);
                if (close < 0)
                {
                    // No closing brace anywhere after this, so there is no token here
                    // and the rest of the line is just text.
                    sb.Append(template, i, template.Length - i);
                    break;
                }

                string token = template.Substring(i + 1, close - i - 1);
                if (IsKnown(token))
                {
                    string value = ValueOf(token);
                    if (value == null)
                    {
                        return false;
                    }

                    sb.Append(value);
                }
                else
                {
                    // Not one of ours. Put it back verbatim, braces included.
                    sb.Append(template, i, close - i + 1);
                }

                i = close + 1;
            }

            rendered = sb.ToString();
            return true;
        }

        /// <summary>Is this one of the tokens we understand?</summary>
        /// <param name="token">The text between the braces, with the braces removed.</param>
        /// <returns>True for any of the twelve. False for anything else.</returns>
        /// <remarks>
        /// Case-sensitive on purpose. The pack is a file people edit by hand, and I
        /// would rather "{Target}" show up in game looking wrong than quietly work.
        /// Loose matching is only cheap while the set is small, and this set grows.
        /// </remarks>
        private static bool IsKnown(string token)
        {
            return token is "target" or "player" or "name" or "companion" or "ally"
                or "weapon" or "weapontype" or "damage" or "status" or "biome"
                or "item" or "skill";
        }

        /// <summary>The value for a known token, or null when we do not have one.</summary>
        /// <param name="token">One of the twelve token names.</param>
        /// <returns>The value, or null if it was not supplied.</returns>
        private string ValueOf(string token)
        {
            return token switch
            {
                "target" => Target,
                "player" => Player,
                "name" => Name,
                "companion" => Companion,
                "ally" => Ally,
                "weapon" => Details.Weapon,
                "weapontype" => Details.WeaponType,
                "damage" => Details.Damage,
                "status" => Details.Status,
                "biome" => Details.Biome,
                "item" => Details.Item,
                "skill" => Details.Skill,
                _ => null,
            };
        }
    }
}
