using System.Collections.Generic;
using ChattyBones.Logic;
using UnityEngine;

namespace ChattyBones
{
    /// <summary>
    /// The squad's shared voice: what it may say, and whether it may say it now.
    /// </summary>
    /// <remarks>
    /// One budget, one chooser and one pack for every skeleton you have out. Both
    /// pieces of memory only work that way round - a per-skeleton chooser would let
    /// five of them say the same line in turn, and a per-skeleton budget would let
    /// them all shout at once, which is the thing the budget exists to stop.
    ///
    /// The hooks do not talk to <see cref="Speech"/> directly. Everything goes
    /// through <see cref="TrySpeak"/>, so the ask-then-book ordering is written down
    /// once here rather than trusted to a dozen call sites.
    /// </remarks>
    internal static class Chatter
    {
        private static ChatterBudget _budget;
        private static LineChooser _chooser;
        private static LinePack _pack;
        // Qualified because UnityEngine.Random is also in scope here. LineChooser
        // wants the System one - it is handed a seeded instance in the tests.
        private static System.Random _random;

        /// <summary>Seconds of game time left to run before the sweep fires again.</summary>
        private static float _untilSweep;

        /// <summary>Whether the mod was switched on the last time we looked.</summary>
        private static bool _wasEnabled = true;

        /// <summary>How often we look at what the skeletons are doing.</summary>
        /// <remarks>
        /// Four times a second. Target changes and kills have no event to hang off -
        /// see <see cref="ChatterEvent.Killed"/> - so they are found by looking, and
        /// this is the rate at which we look. Fast enough that a line lands while the
        /// moment is still the moment, slow enough that the cost is a handful of
        /// field reads per skeleton per quarter second.
        /// </remarks>
        private const float SweepSeconds = 0.25f;

        /// <summary>Build the pack and the budget. Called once from Awake.</summary>
        internal static void Init()
        {
            _pack = PackFile.Load();
            _chooser = new LineChooser();
            _budget = new ChatterBudget(SettingsFromConfig());
            _random = new System.Random();

            ChattyBonesPlugin.Log.LogInfo(
                "Line pack loaded with " + _pack.Personalities.Count + " personalities.");

            ReportUncoveredEvents();
            ReportUnusableContexts();
        }

        /// <summary>The personalities a skeleton can be assigned, in a stable order.</summary>
        internal static IReadOnlyList<string> Personalities => _pack.Personalities;

        /// <summary>
        /// Which pack is in force, counting up from zero. What
        /// <see cref="ChatterComponent.Personality"/> compares against to know its
        /// cached answer has gone stale.
        /// </summary>
        internal static int PackGeneration { get; private set; }

        /// <summary>Take up an edited pack file, keeping the current one if it will not parse.</summary>
        private static void ReloadPack()
        {
            LinePack reloaded = PackFile.Reload();

            if (reloaded == null)
            {
                return;
            }

            _pack = reloaded;
            PackGeneration++;

            ChattyBonesPlugin.Log.LogInfo(
                "Line pack reloaded with " + _pack.Personalities.Count + " personalities.");

            ReportUncoveredEvents();
            ReportUnusableContexts();
        }

        /// <summary>Mention which events the pack has no lines for, because nothing else will.</summary>
        /// <remarks>
        /// Not behind <see cref="ModConfig.LogChatter"/>, unlike the rest of the
        /// tracing: this is about the player's own file rather than our diagnostics,
        /// and the symptom is otherwise invisible. A pack written before an event
        /// existed leaves that event silent, which looks exactly like a hook that does
        /// not work - which is how the combat events came to be completely inaudible
        /// in a session, while being correct.
        ///
        /// Worded as a statement rather than a complaint, and that matters. The pack
        /// header offers deleting an event as the way to switch off a kind of chatter,
        /// so a player who has done that deliberately must not be told off for it on
        /// every save while they are editing.
        /// </remarks>
        private static void ReportUncoveredEvents()
        {
            IReadOnlyList<ChatterEvent> missing = _pack.EventsWithNoLines();
            if (missing.Count == 0)
            {
                return;
            }

            ChattyBonesPlugin.Log.LogInfo(
                "Silent by omission - your pack has no lines for " + string.Join(", ", missing)
                + ". That is fine if you meant it. If you did not, " + PackFile.ReferenceFileName
                + " next to your own pack is what this version shipped with.");
        }

        /// <summary>Complain about context values that name nothing the game has.</summary>
        /// <remarks>
        /// A warning rather than a statement, which is the opposite of the call above,
        /// and deliberately so: leaving an event out is something a player may have
        /// meant, while <c>Idle[biome=Swamps]</c> is not something anybody means. It
        /// parses, it matches nothing, and it is silent forever.
        /// </remarks>
        private static void ReportUnusableContexts()
        {
            IReadOnlyList<string> bad = Contexts.Unusable(_pack);

            if (bad.Count == 0)
            {
                return;
            }

            ChattyBonesPlugin.Log.LogWarning(
                "These groups are tagged with something that is not a real value, so nothing"
                + " will ever match them: " + string.Join(", ", bad)
                + ". Biome names are the game's own spellings, like Meadows, BlackForest,"
                + " Swamp, Mountain, Plains, Mistlands, AshLands and DeepNorth. None and"
                + " All are real names in the game's list but no skeleton is ever standing"
                + " in one - for lines that suit anywhere, write a plain group instead.");
        }

        /// <summary>Read the current config into a fresh settings object.</summary>
        /// <returns>Settings matching what the player has set right now.</returns>
        /// <remarks>
        /// A new object every time rather than editing the one in force, because
        /// BepInEx raises SettingChanged off the main thread - see the note on
        /// <see cref="ChatterSettings"/>.
        /// </remarks>
        private static ChatterSettings SettingsFromConfig()
        {
            return new ChatterSettings(
                minGapSeconds: ModConfig.MinGapSeconds.Value,
                preemptGapSeconds: ModConfig.PreemptGapSeconds.Value,
                speakerCooldownSeconds: ModConfig.SpeakerCooldownSeconds.Value,
                squadEchoWindowSeconds: ModConfig.SquadEchoWindowSeconds.Value);
        }

        /// <summary>Say which group a speaker would draw from right now.</summary>
        /// <returns>The group in words, or null when it has no lines for this event at all.</returns>
        /// <param name="personality">The speaker's personality, or null if it has none yet.</param>
        /// <param name="kind">The event to ask about.</param>
        /// <param name="contexts">What the speaker currently satisfies, from <see cref="Contexts.For"/>.</param>
        /// <remarks>
        /// For <c>cb_who</c>, and it exists because the tie-break is deliberately silent:
        /// two context groups can both match and the one written first wins, with nothing
        /// said about the other. That is the rule we want, but it leaves an author whose
        /// group never fires with nothing to go on - the same shape as the two silent
        /// failures <c>cb_tokens</c> and <c>EventsWithNoLines</c> were built for.
        ///
        /// Here rather than in the command so the pack stays private. The command has no
        /// business holding one, and this is the only question it needs answered.
        /// </remarks>
        internal static string DescribeChoice(
            string personality, ChatterEvent kind, IReadOnlyList<string> contexts)
        {
            if (_pack == null
                || !_pack.TryGetSpace(personality, kind, out LineSpace space)
                || !space.TrySelect(contexts, out int offset, out int length))
            {
                return null;
            }

            // A personality with no lines of its own for this event is handed the shared
            // space, and every group in *that* space is flagged Personal - the builder
            // passes common's groups as both bands, so they land in the first one. So the
            // flag alone answers "which band", not "whose lines", and HasOwnLines is what
            // answers the question a person is actually asking. Without it cb_who called
            // common's lines "its own" for nineteen of boastful's thirty-one events, and
            // for every event of a skeleton with no personality yet.
            bool ownSpace = _pack.HasOwnLines(personality, kind);

            // Offset and length together, not offset alone. TrySelect has a fallback that
            // hands back the whole numbering starting at 0, and group[0] also starts at 0,
            // so matching on offset would report the first group and quietly swallow the
            // one case worth seeing.
            for (int i = 0; i < space.Groups.Count; i++)
            {
                LineSpace.Group group = space.Groups[i];

                if (group.Offset != offset || group.Length != length)
                {
                    continue;
                }

                return (group.Context ?? "plain")
                    + ", " + group.Length + (group.Length == 1 ? " line" : " lines")
                    + ", " + (ownSpace && group.Personal ? "its own" : "shared");
            }

            return length + " lines, the whole numbering - which means nothing matched";
        }

        /// <summary>Pick up a config change without a restart.</summary>
        internal static void RefreshSettings()
        {
            if (_budget != null)
            {
                _budget.Settings = SettingsFromConfig();
            }
        }

        /// <summary>Have a skeleton react to something, if it is allowed to and has words for it.</summary>
        /// <returns>True if a line actually appeared on screen.</returns>
        /// <param name="speaker">Which of ours is reacting.</param>
        /// <param name="kind">What happened.</param>
        /// <param name="subject">
        /// A prefab hash for whatever the remark is about, or 0 when it is not about
        /// anything. Never an instance id - see <see cref="ChatterBudget.CanClaim"/>.
        /// </param>
        /// <param name="targetName">
        /// Already localized, and resolved by the caller rather than in here. Killed
        /// is why: by the time a skeleton gets to gloat, the thing it killed has often
        /// been destroyed and replaced with a ragdoll, so the name has to be taken
        /// while there is still something to take it from.
        /// </param>
        /// <param name="companion">Another of your skeletons, for lines about each other.</param>
        /// <param name="companionName">
        /// The companion's name, already resolved, for when it is not around to be
        /// asked any more. Wins over <paramref name="companion"/> when supplied.
        /// </param>
        /// <param name="details">What is known about the event itself. Usually nothing.</param>
        /// <param name="ally">
        /// The particular other player the event is about, for the events that name
        /// one. Left null everywhere else, where whoever is standing nearby is filled
        /// in instead.
        /// </param>
        /// <remarks>
        /// Three ways to come back false, and all three are ordinary: the budget said
        /// no, the pack had nothing sayable, or the world is not in a state to draw.
        /// Nothing is queued for later in any of those cases, deliberately - see
        /// <see cref="ChatterBudget.CanClaim"/>.
        ///
        /// The order below is the whole point of the method. Asking books nothing, so
        /// a caller that asked on behalf of two skeletons before resolving either
        /// would get two yeses and blow through the squad gap. Here, one call is one
        /// resolved claim.
        ///
        /// Committing last, after <see cref="Speech.Say"/> has confirmed something was
        /// drawn, means a line that failed to render does not spend the squad's quiet
        /// time. The skeleton simply stays eligible.
        /// </remarks>
        internal static bool TrySpeak(
            ChatterComponent speaker,
            ChatterEvent kind,
            int subject,
            string targetName,
            Character companion,
            string companionName = null,
            LineDetails details = default,
            Player ally = null)
        {
            if (!ModConfig.Enabled.Value || speaker == null || _budget == null)
            {
                return false;
            }

            // A Harmony postfix runs whichever way the patched method returned, and
            // RPC_Damage bails out early on a non-owner while ours does not. Ownership
            // can also move between an RPC being sent and arriving, which is the case
            // RPC_Damage's own owner check exists to cover.
            if (!speaker.IsOwned)
            {
                return false;
            }

            Character character = speaker.Character;
            if (character == null)
            {
                return false;
            }

            long speakerId = speaker.SpeakerId;
            float now = Time.time;

            if (!_budget.CanClaim(speakerId, kind, subject, now, out ChatterRefusal why))
            {
                Trace(speaker, kind, why);
                return false;
            }

            // Everything below is built only once the budget has said yes. Summons.NameOf
            // costs two ZDO reads and a filter pass, and the refusal path is much the
            // busier one.
            //
            // Whoever the hook itself named. companionName wins when supplied, for a
            // companion that is not around to be asked any more - a skeleton being
            // mourned is destroyed moments later, so its name has to be taken while it
            // is still standing.
            string namedCompanion = companionName ?? Summons.NameOf(companion);
            string namedAlly = Mirror.PlayerName(ally);

            // Noted before anything is filled in, because cb_tokens is asking what the
            // call site handed over. Recording the filled-in values would have every
            // event in the mod reporting that it supplies {companion}, which is no
            // report at all.
            EventTokens.Note(kind, targetName, namedCompanion, namedAlly, details);

            // Somebody to talk to, where the event has not named one itself. This is
            // what lets "{companion}, with me!" go in a line about you being hurt, and
            // "How are things, {ally}?" in an idle mutter - both are passed over
            // whenever there is nobody about, which is most of the time.
            //
            // Deliberately not done for the events PromisedFor marks. There the hook
            // names a *particular* person, and a fallback would name the wrong one - a
            // CompanionDied whose hook broke would mourn a skeleton still standing
            // rather than falling quiet and being noticed.
            Character talkingTo = companion;
            if (EventTokens.ShouldFillIn(kind, TokenSet.Companion))
            {
                talkingTo = AnotherOf(speaker);
                namedCompanion = Summons.NameOf(talkingTo);
            }

            Player nearby = ally;
            if (EventTokens.ShouldFillIn(kind, TokenSet.Ally))
            {
                nearby = Allies.Nearby(character.transform.position);
                namedAlly = Mirror.PlayerName(nearby);
            }

            LineTokens tokens = new(
                target: targetName,
                // Through the same GetHoverName the listening side uses, so both screens
                // read the same name. It is the game's own UGC filter, and it applies to
                // your own name as much as to anybody else's.
                player: Mirror.PlayerName(Player.m_localPlayer),
                name: Summons.NameOf(character),
                companion: namedCompanion,
                ally: namedAlly,

                // Words here, keys on the wire. The hooks record a localization key so
                // that the same broadcast reads in everybody's own language - see
                // Mirror and Logic/DetailWire.
                details: Mirror.Localize(details));

            if (!_chooser.TryChoose(
                    _pack, speaker.Personality, kind, tokens, _random,
                    out int lineRef, out string line, Contexts.For(character)))
            {
                Trace(speaker, kind, "nothing it could say as " + (speaker.Personality ?? "no personality yet"));
                return false;
            }

            if (Speech.Say(character, line, _pack.Colors.TagFor(kind)) == Drew.Nothing)
            {
                Trace(speaker, kind, "had \"" + line + "\" but nothing was drawn");
                return false;
            }

            _budget.Commit(speakerId, kind, subject, now);

            // The unlocalized details, not the ones we just said. Everything after this
            // point is for the other clients, and they do their own localizing. The two
            // identities are whoever we settled on above, fallback included, so a
            // listener names the same people rather than choosing its own.
            speaker.OnSpoke(kind, lineRef, subject, IdOf(talkingTo), IdOf(nearby), details);

            Trace(speaker, kind, "said \"" + line + "\"");

            return true;
        }

        /// <summary>Somebody's identity, for the clients that have to name them too.</summary>
        /// <returns>Their ZDOID, or <c>ZDOID.None</c> when there is nobody.</returns>
        /// <param name="who">A skeleton or a player. Null is fine.</param>
        /// <remarks>
        /// Player derives from Character, so one method serves both of the fields an
        /// utterance carries.
        ///
        /// A skeleton being mourned is the one case that regularly answers None:
        /// Character.OnDeath has already reset its ZDO by the time the squad speaks, so
        /// there is no identity left to send. Listeners then fall through to a line
        /// that does not name anybody, which is the right shape for a death nobody else
        /// can see the body of.
        /// </remarks>
        private static ZDOID IdOf(Character who)
        {
            return who == null ? ZDOID.None : who.GetZDOID();
        }

        /// <summary>Draw a line somebody else's skeleton has just said.</summary>
        /// <returns>True when something actually reached the screen.</returns>
        /// <param name="listener">One of their summons, loaded on this client.</param>
        /// <param name="said">What came off the ZDO.</param>
        /// <param name="raw">The details it carried, still as keys.</param>
        /// <param name="companion">The skeleton it named, or <c>ZDOID.None</c>.</param>
        /// <param name="ally">The player it named, or <c>ZDOID.None</c>.</param>
        /// <remarks>
        /// The budget is deliberately not consulted, beyond the per-event switches. It
        /// is the owner's job to decide whether the squad may speak, and it has already
        /// done it - running the gaps and cooldowns again here would drop lines on one
        /// screen and not the other, which is the exact desync the whole mirroring
        /// scheme exists to avoid. What we do honour is this player's own event
        /// switches, because that is a statement about what they want to read rather
        /// than about what anybody's skeleton may say.
        ///
        /// Nothing is committed either. This client's own squad has its own budget and
        /// somebody else's skeleton talking must not spend it - two players standing
        /// together would otherwise fall into taking turns to be allowed to speak.
        /// </remarks>
        internal static bool Hear(
            ChatterComponent listener, Utterance said, LineDetails raw, ZDOID companion, ZDOID ally)
        {
            if (!ModConfig.Enabled.Value || !ModConfig.HearOthers.Value || _pack == null)
            {
                return false;
            }

            if (_budget != null && _budget.Settings.IsDisabled(said.Kind))
            {
                return false;
            }

            Character character = listener.Character;
            if (character == null)
            {
                return false;
            }

            // The target is gated on what the event promises, and that is load-bearing
            // rather than tidy. The subject field is the *budget's* dedup key, and for
            // several events it is not a creature at all: AllyArrived sends the Player
            // prefab, CompanionHurt sends the companion's, Looted sends the item's.
            // Filling {target} from it unasked would have this side rendering a line the
            // owner's side refused, and naming a stone as the thing being fought.
            //
            // The two people fields need no such gate, because each carries exactly one
            // kind of thing. They shared a field at first, with the event saying which,
            // which was tidy right up until a line wanted both at once.
            TokenSet promised = EventTokens.PromisedFor(said.Kind);

            LineTokens tokens = new(
                target: (promised & TokenSet.Target) == 0 ? null : Mirror.CreatureName(said.Subject),
                player: Mirror.SummonerName(character),
                name: Summons.NameOf(character),
                companion: Mirror.NameFor(companion),
                ally: Mirror.NameFor(ally),
                details: Mirror.Localize(raw));

            if (!_pack.TryPickRenderable(listener.Personality, said.Kind, said.LineRef, tokens, out string line))
            {
                Trace(listener, said.Kind, "heard it, but has nothing it could say back");
                return false;
            }

            if (Speech.Say(character, line, _pack.Colors.TagFor(said.Kind)) == Drew.Nothing)
            {
                return false;
            }

            Trace(listener, said.Kind, "heard \"" + line + "\"");
            return true;
        }

        /// <summary>Write down what became of one attempt to speak, when the player asked us to.</summary>
        /// <param name="speaker">Which skeleton was trying.</param>
        /// <param name="kind">What it was reacting to.</param>
        /// <param name="why">Which check turned it down.</param>
        /// <remarks>
        /// Its own overload because refusal is the common case by design, and the
        /// squad is asked once per skeleton - so building the message at the call site
        /// would allocate all fight for a log nobody has switched on.
        /// </remarks>
        internal static void Trace(ChatterComponent speaker, ChatterEvent kind, ChatterRefusal why)
        {
            if (ModConfig.LogChatter.Value)
            {
                Trace(speaker, kind, "turned down by " + why);
            }
        }

        /// <summary>Write down what became of one attempt to speak, when the player asked us to.</summary>
        /// <param name="speaker">Which skeleton was trying.</param>
        /// <param name="kind">What it was reacting to.</param>
        /// <param name="what">The outcome, in words.</param>
        /// <remarks>
        /// Less noisy than it looks: this runs once per event, not once per sweep, so
        /// a busy fight is a handful of lines a second. The name lookup costs two ZDO
        /// reads and a filter pass, which is why it sits behind the check.
        /// </remarks>
        internal static void Trace(ChatterComponent speaker, ChatterEvent kind, string what)
        {
            if (!ModConfig.LogChatter.Value)
            {
                return;
            }

            string name = speaker.Character == null ? "?" : Summons.NameOf(speaker.Character) ?? "?";

            ChattyBonesPlugin.Log.LogInfo("[chatter] " + name + " / " + kind + ": " + what);
        }

        /// <summary>Let whichever skeleton is willing react to something that happened nearby.</summary>
        /// <returns>True if one of them spoke.</returns>
        /// <param name="kind">What happened.</param>
        /// <param name="subject">A prefab hash, or 0.</param>
        /// <param name="targetName">Already localized, or null.</param>
        /// <param name="companion">The skeleton the remark is about. Never the speaker - it is excluded by reference.</param>
        /// <param name="companionName">Its name, already resolved, when it may no longer exist to be asked.</param>
        /// <param name="details">What is known about the event itself. Usually nothing.</param>
        /// <param name="ally">The particular other player the event is about, or null.</param>
        /// <remarks>
        /// Used by the events that happen to you or to the world rather than to one
        /// particular skeleton - you took a hit, you landed one, somebody's colleague
        /// got clobbered. Somebody should say something and it does not much matter who.
        ///
        /// We ask each in turn and stop at the first yes, which is the ask-then-book
        /// rule again: asking books nothing, so asking all five first and then picking
        /// one would have got five yeses and taught us nothing about which of them was
        /// actually free.
        ///
        /// The starting point rotates. Without it the loop always begins at the same
        /// end of the list, and since the speaker cooldown is the usual reason for a
        /// refusal, the skeleton you summoned first would do a noticeably large share
        /// of the talking.
        /// </remarks>
        internal static bool SpeakAny(
            ChatterEvent kind,
            int subject,
            string targetName,
            Character companion,
            string companionName = null,
            LineDetails details = default,
            Player ally = null)
        {
            List<ChatterComponent> squad = ChatterComponent.All;
            int count = squad.Count;
            if (count == 0)
            {
                return false;
            }

            // Masked to stay positive - a negative remainder is a negative index.
            int start = (_nextSpeaker++ & int.MaxValue) % count;

            for (int offset = 0; offset < count; offset++)
            {
                ChatterComponent speaker = squad[(start + offset) % count];

                if (companion != null && speaker.Character == companion)
                {
                    // Nobody commiserates with themselves.
                    continue;
                }

                if (TrySpeak(speaker, kind, subject, targetName, companion, companionName, details, ally))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Rotates so the same skeleton is not always asked first. Wraps harmlessly.</summary>
        private static int _nextSpeaker;

        /// <summary>Find somebody for a skeleton to talk about, or to.</summary>
        /// <returns>Another loaded skeleton, or null when it is on its own.</returns>
        /// <param name="speaker">Whoever is looking for company.</param>
        /// <remarks>
        /// What fills {companion} on every event that does not name a particular one.
        /// Random rather than the nearest, so a squad of three does not settle into two
        /// of them always addressing each other.
        ///
        /// Drawn from every loaded summon rather than only from ours, so in a shared
        /// world a skeleton will happily rib one of somebody else's. See the remarks on
        /// <see cref="LineTokens.Companion"/> for why that is the wanted answer.
        /// </remarks>
        internal static Character AnotherOf(ChatterComponent speaker)
        {
            List<ChatterComponent> squad = ChatterComponent.All;
            if (squad.Count < 2)
            {
                return null;
            }

            // Drawn from the others rather than from everybody, which matters more
            // than it looks: picking a random start and walking to the first
            // non-speaker hands the speaker's list neighbour two chances in every
            // three with a squad of three, and list order is summon order - so it
            // would be the same neighbour every time.
            int pick = _random.Next(0, squad.Count - 1);

            for (int i = 0; i < squad.Count; i++)
            {
                ChatterComponent other = squad[i];

                if (ReferenceEquals(other, speaker))
                {
                    continue;
                }

                if (pick-- == 0)
                {
                    // Null when it is being destroyed this frame - OnDisable has not
                    // run yet, so it is still in the registry. The caller treats that
                    // as nobody, which is right.
                    return other.Character == null ? null : other.Character;
                }
            }

            return null;
        }

        /// <summary>Look at what the squad is doing, and let one of them comment.</summary>
        /// <param name="dt">Seconds since the last frame.</param>
        /// <remarks>
        /// Driven from the plugin's Update rather than from a MonoBehaviour on each
        /// skeleton. One loop in a known order is what makes the ask-then-book rule
        /// easy to keep: with an Update per skeleton, the order is Unity's, and five
        /// of them would each ask before any of them had booked.
        ///
        /// The sweep looks for the two things that have no event to hook - a target
        /// appearing, and a target dying - and runs the idle timer.
        /// </remarks>
        internal static void Tick(float dt)
        {
            // Ahead of the Enabled check, and it has to be: the countdown behind
            // ShouldReload runs on the dt we pass it, so skipping the call while the
            // mod is switched off would leave a pending edit frozen mid-settle and
            // apply it at some arbitrary later moment.
            if (PackFile.ShouldReload(dt))
            {
                ReloadPack();
            }

            if (!ModConfig.Enabled.Value)
            {
                // On the way down rather than the way back up, so a skeleton loaded
                // while the mod is off is already in the right state. See ListenState
                // for what this is protecting against.
                if (_wasEnabled)
                {
                    _wasEnabled = false;

                    List<ChatterComponent> asleep = ChatterComponent.All;
                    for (int i = 0; i < asleep.Count; i++)
                    {
                        asleep[i].ForgetWhatWasHeard();
                    }
                }

                return;
            }

            _wasEnabled = true;

            // Anything recorded during a blow is said by the postfix that ends it.
            // A skill can also go up while you are chopping a tree, where there is no
            // blow to end - so a record that outlives its frame is said here instead.
            Patches.Blow.FlushStale();

            _untilSweep -= dt;
            if (_untilSweep > 0f)
            {
                return;
            }

            float elapsed = SweepSeconds - _untilSweep;
            _untilSweep = SweepSeconds;

            // Asked once for the squad rather than once per skeleton: it is a question
            // about the world, and the answer does not vary between them.
            try
            {
                Patches.Raids.Poll();
            }
            catch (System.Exception e)
            {
                ChattyBonesPlugin.Log.LogWarning("ChattyBones stumbled over a raid: " + e);
            }

            // Also a question about the world rather than about any one skeleton, and
            // guarded separately so a bad answer to one does not cost the other.
            try
            {
                Allies.Poll(elapsed);
            }
            catch (System.Exception e)
            {
                ChattyBonesPlugin.Log.LogWarning("ChattyBones stumbled over an ally: " + e);
            }

            // Guarded per skeleton rather than around the loop, so one of them failing
            // costs its own turn rather than the rest of the squad's. This is the only
            // way into our code that is not a Harmony patch - the patches carry their
            // own catch, for the same reason and with rather more at stake.
            List<ChatterComponent> squad = ChatterComponent.All;
            for (int i = 0; i < squad.Count; i++)
            {
                try
                {
                    squad[i].Sweep(elapsed);
                }
                catch (System.Exception e)
                {
                    ChattyBonesPlugin.Log.LogWarning("ChattyBones stumbled watching a skeleton: " + e);
                }
            }
        }
    }
}
