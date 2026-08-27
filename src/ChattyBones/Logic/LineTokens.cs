using System.Text;

namespace ChattyBones.Logic
{
    /// <summary>
    /// The values a line can drop into itself, and the code that does the dropping.
    /// </summary>
    /// <remarks>
    /// A pack author writes "Get lost, {target}!" and we turn that into "Get lost,
    /// Greydwarf!" at the moment it is said. Three tokens, all optional:
    ///
    /// - {target} is whatever the skeleton is reacting to, already localised
    /// - {player} is you, whoever summoned it
    /// - {name} is the skeleton's own name
    ///
    /// Every client fills these in for itself from the prefab hash we sent, so a
    /// German player reads "Grauzwerg" while you read "Greydwarf" - the word never
    /// travels, only the hash does.
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

        /// <summary>Gather up whatever we know at the moment of speaking.</summary>
        /// <param name="target">Localised creature name, or null.</param>
        /// <param name="player">Player name, or null.</param>
        /// <param name="name">The skeleton's name, or null.</param>
        internal LineTokens(string target, string player, string name)
        {
            Target = target;
            Player = player;
            Name = name;
        }

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
        /// Worth noting for the multiplayer side: whether a token can be filled
        /// depends only on values every client works out from the same broadcast
        /// information, so all of them succeed or all of them refuse. This does not
        /// put us at risk of two players seeing different lines.
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

        /// <summary>Is this one of the three tokens we understand?</summary>
        /// <param name="token">The text between the braces, with the braces removed.</param>
        /// <returns>True for target, player and name. False for anything else.</returns>
        /// <remarks>
        /// Case-sensitive on purpose. The pack is a file people edit by hand, and I
        /// would rather "{Target}" show up in game looking wrong than quietly work,
        /// because the day we add a fourth token is the day case-insensitivity starts
        /// producing surprises.
        /// </remarks>
        private static bool IsKnown(string token)
        {
            return token is "target" or "player" or "name";
        }

        /// <summary>The value for a known token, or null when we do not have one.</summary>
        /// <param name="token">One of target, player or name.</param>
        /// <returns>The value, or null if it was not supplied.</returns>
        private string ValueOf(string token)
        {
            return token switch
            {
                "target" => Target,
                "player" => Player,
                "name" => Name,
                _ => null,
            };
        }
    }
}
