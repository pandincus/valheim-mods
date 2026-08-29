namespace ChattyBones.Logic
{
    /// <summary>
    /// The things a skeleton can react to.
    /// </summary>
    /// <remarks>
    /// One entry per hook we install, plus Idle, which nothing triggers - it is
    /// just a timer running down with nothing else to say.
    ///
    /// This lives in its own file rather than beside the budget, because it is the
    /// most depended-upon type in the folder - the pack, the tokens, the chooser and
    /// the utterance all speak in these - and a shared vocabulary filed under one of
    /// its consumers is a slightly odd place to go looking.
    ///
    /// The order here is not the priority order. Priority lives in
    /// <see cref="ChatterBudget"/>, so that reordering this enum for readability
    /// cannot quietly change which skeleton gets to speak.
    /// </remarks>
    internal enum ChatterEvent
    {
        /// <summary>You just raised it with the Dead Raiser.</summary>
        Summoned,

        /// <summary>It picked something to attack, and is heading over.</summary>
        TargetAcquired,

        /// <summary>Something hit it hard enough to be worth mentioning.</summary>
        Hurt,

        /// <summary>It gained a status effect, e.g. you dropped a shield on it.</summary>
        Buffed,

        /// <summary>It killed something.</summary>
        /// <remarks>
        /// Not hooked off the victim's death, which sounds like the obvious place and
        /// is not. Character.OnDeath is reached from CheckDeath, which sits inside an
        /// IsOwner check, so a creature's death only fires on whichever client owns
        /// that creature - in a shared world that is often the host or another player,
        /// and your skeleton's kill would simply go uncommented.
        ///
        /// Instead we watch our own skeleton's target go from something to nothing
        /// and check whether that something is now dead, which reads replicated state
        /// and works whoever owns it. Attribution gets a little looser - the thing
        /// might have died to somebody else's axe - but "the creature my skeleton was
        /// charging at just died" is arguably the better trigger anyway. It fires
        /// when the skeleton thinks it won, which is the funnier moment.
        /// </remarks>
        Killed,

        /// <summary>It died.</summary>
        Died,

        /// <summary>It timed out, or you summoned enough others to push it over the cap.</summary>
        Unsummoned,

        /// <summary>Nothing is happening and it feels the need to fill the silence.</summary>
        Idle,

        /// <summary>You took a hit worth mentioning.</summary>
        /// <remarks>
        /// Damage on a Player resolves on that player's own client, which also owns
        /// their summons - so your squad reacts to your injuries and nobody else's.
        /// </remarks>
        PlayerHurt,

        /// <summary>You hit something very hard.</summary>
        /// <remarks>
        /// The attacking client builds the HitData and calls Character.Damage, which
        /// is what then sends the RPC. So a hook on Damage rather than RPC_Damage
        /// runs on your machine with the number in hand, and no networking is
        /// involved at all - which is a nicer position than the kill events are in.
        /// </remarks>
        PlayerLandedABigHit,

        /// <summary>You killed something.</summary>
        /// <remarks>
        /// Kept separate from <see cref="PlayerLandedABigHit"/> even though a kill is
        /// the biggest hit there is, because "You got him!" and "Nice swing!" are
        /// different lines and a pack author should be able to write both. Event
        /// space is not scarce - see <see cref="Utterance"/>.
        /// </remarks>
        PlayerGotAKill,

        /// <summary>Another of your skeletons took a hit.</summary>
        /// <remarks>
        /// Both skeletons are yours and owned by the same client, so this needs no
        /// cleverness to detect. The fun is in <see cref="LineTokens.Companion"/>:
        /// they already have names, either the one they came with or whatever you
        /// renamed them to, so a line can be "Ach, {companion}!" rather than
        /// something vague about a colleague.
        /// </remarks>
        CompanionHurt,

        /// <summary>Another of your skeletons got a kill.</summary>
        /// <remarks>
        /// Fired only when the one that actually did it cannot speak, which in
        /// practice means its own cooldown: the skeleton that announced the target a
        /// few seconds ago is usually the same one now standing over the body, and it
        /// is still serving out the <see cref="ChatterSettings.SpeakerCooldownSeconds"/>
        /// it spent on the announcement.
        ///
        /// Kept separate from <see cref="Killed"/> because the line is addressed to
        /// somebody. "Nice one, {companion}!" is a different sentence from "Down it
        /// goes", and a squad that congratulates each other by name reads as a group
        /// of people rather than as several narrators.
        ///
        /// Appended rather than slotted in beside CompanionHurt, and that is on
        /// purpose: the enum's values travel in a packed int, so inserting one here
        /// would renumber every event after it and two clients on different builds
        /// would disagree about what they had just been told.
        /// </remarks>
        CompanionKilled,

        /// <summary>Another of your skeletons just died.</summary>
        /// <remarks>
        /// Unlike the two above, this does not wait for the fallen one to fail to
        /// speak. It answers the death cry rather than covering for it - see
        /// <see cref="ChatterBudget.Answers"/> - so "Bugger." and "Oh no, {companion}!"
        /// land together, from two skeletons standing apart, and read as one moment
        /// rather than two remarks.
        /// </remarks>
        CompanionDied,

        /// <summary>Somebody new has just been raised.</summary>
        /// <remarks>
        /// The other side of <see cref="Summoned"/>, and the cheerful one: the newcomer
        /// introduces itself and the squad welcomes it, in the same breath.
        ///
        /// Only the first arrival of a batch gets a welcome. Raise three at once and
        /// the second and third are refused, because barging in wants a strictly
        /// higher rank than the greeting already in progress and they are all the same
        /// event - so you get one exchange rather than three people talking at once.
        /// </remarks>
        CompanionSummoned,

        /// <summary>Something unpleasant has taken hold of it - fire, poison, frost.</summary>
        /// <remarks>
        /// The other half of <see cref="Buffed"/>, and a correction to it: that event
        /// fired for every status effect, so a burning skeleton used to thank you for
        /// it. <see cref="StatusKind"/> is what tells the two apart.
        /// </remarks>
        Afflicted,

        /// <summary>The weather has got to it - rain, water, cold.</summary>
        /// <remarks>
        /// Wet is the one that matters, and it is why this exists separately from
        /// <see cref="Afflicted"/>: any water at all applies it, so ranking a remark
        /// about being damp anywhere near an injury meant a skeleton wading into a
        /// swamp talking over its own kills. Here it sits just above
        /// <see cref="Idle"/>, which is the company it belongs in.
        /// </remarks>
        Weather,
    }
}
