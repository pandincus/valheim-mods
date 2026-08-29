namespace ChattyBones.Logic
{
    /// <summary>
    /// The lines the mod ships with, so a fresh install has something to say.
    /// </summary>
    /// <remarks>
    /// Deliberately thin. Phase 5 replaces this with a YAML file that is written
    /// properly, and the job here is only to give every hook something to draw so
    /// that a misfiring event looks like a misfiring event rather than an empty
    /// pack.
    ///
    /// Most of it sits under <see cref="LinePack.SharedPersonality"/>. The four
    /// personalities each get lines only where the difference actually reads -
    /// meeting you, picking a fight, getting hurt, winning one, and standing about.
    /// A cowardly skeleton and a boastful one being interchangeable on
    /// <see cref="ChatterEvent.Buffed"/> is not worth four near-identical lines.
    ///
    /// Every event has at least one line in the shared group, which is what makes
    /// "nobody said anything" mean something during Phase 4 testing.
    /// </remarks>
    internal static class DefaultPack
    {
        /// <summary>Build the built-in pack.</summary>
        /// <returns>A pack with four personalities and a shared fallback.</returns>
        /// <remarks>
        /// Built fresh each call rather than cached in a static. It happens once at
        /// startup, and a mutable static holding the pack is exactly the thing that
        /// gets awkward when Phase 5 starts reloading the file on the fly.
        /// </remarks>
        internal static LinePack Build()
        {
            LinePack.Builder builder = new();

            Shared(builder);
            Cowardly(builder);
            Boastful(builder);
            Dutiful(builder);
            Veteran(builder);

            return builder.Build();
        }

        /// <summary>Lines anyone falls back on.</summary>
        /// <param name="b">The builder being filled.</param>
        private static void Shared(LinePack.Builder b)
        {
            _ = b.Add(C, ChatterEvent.Summoned, "Up we get.", "Who disturbs me? Oh. You.", "Right then, {player}.")
                .Add(C, ChatterEvent.TargetAcquired, "That {target} is mine.", "Oi! {target}!", "Here we go.")
                .Add(C, ChatterEvent.Hurt, "Ow.", "That was a rib!", "Still standing.")
                .Add(C, ChatterEvent.Buffed, "Ooh, that's the stuff.", "Much obliged.")
                .Add(C, ChatterEvent.Killed, "Down it goes.", "That {target} won't get up.", "Next.")
                .Add(C, ChatterEvent.Died, "Bugger.", "Tell my mum.", "Back to the dirt.")
                .Add(C, ChatterEvent.Unsummoned, "Off I go, then.", "See you, {player}.")
                .Add(C, ChatterEvent.Idle, "Nice weather for it.", "Anyone else cold?", "I miss having skin.", "Hmm.")
                .Add(C, ChatterEvent.PlayerHurt, "{player}! Watch it!", "They got you!", "Careful, {player}.")
                .Add(C, ChatterEvent.PlayerLandedABigHit, "Ooooh.", "Did you see that?", "Lovely swing, {player}.")
                .Add(C, ChatterEvent.PlayerGotAKill, "Got him!", "Nice one, {player}.")
                .Add(C, ChatterEvent.CompanionHurt, "{companion}!", "They're on {companion}!", "Hang on, {companion}!");
        }

        /// <summary>Would rather be somewhere else.</summary>
        /// <param name="b">The builder being filled.</param>
        private static void Cowardly(LinePack.Builder b)
        {
            _ = b.Add(Coward, ChatterEvent.Summoned, "Do I have to?", "I was having a lovely rest.")
                .Add(Coward, ChatterEvent.TargetAcquired, "Is that a {target}? I'd rather not.", "You first, {player}.")
                .Add(Coward, ChatterEvent.Hurt, "Aaargh!", "I'm hit! I'm hit!", "This is exactly what I meant.")
                .Add(Coward, ChatterEvent.Killed, "Did I do that?", "It was mostly {player}, honestly.")
                .Add(Coward, ChatterEvent.Idle, "Can we go home?", "It's very open out here.");
        }

        /// <summary>Convinced of its own legend.</summary>
        /// <param name="b">The builder being filled.</param>
        private static void Boastful(LinePack.Builder b)
        {
            _ = b.Add(Boast, ChatterEvent.Summoned, "The legend returns!", "You chose well, {player}.")
                .Add(Boast, ChatterEvent.TargetAcquired, "Watch this, {player}.", "That {target} picked the wrong day.")
                .Add(Boast, ChatterEvent.Hurt, "A scratch!", "I meant to do that.")
                .Add(Boast, ChatterEvent.Killed, "As foretold!", "Another {target} for the ballad.")
                .Add(Boast, ChatterEvent.Idle, "They'll sing about me, you know.", "Ask me about the ballad.");
        }

        /// <summary>Takes the job seriously.</summary>
        /// <param name="b">The builder being filled.</param>
        private static void Dutiful(LinePack.Builder b)
        {
            _ = b.Add(Duty, ChatterEvent.Summoned, "Reporting for duty.", "Orders, {player}?")
                .Add(Duty, ChatterEvent.TargetAcquired, "Engaging the {target}.", "Target sighted.")
                .Add(Duty, ChatterEvent.Hurt, "Wound sustained. Continuing.", "Still fit to fight.")
                .Add(Duty, ChatterEvent.Killed, "Target down.", "{target} neutralised.")
                .Add(Duty, ChatterEvent.Idle, "Holding position.", "Perimeter clear.");
        }

        /// <summary>Has done this too many times.</summary>
        /// <param name="b">The builder being filled.</param>
        private static void Veteran(LinePack.Builder b)
        {
            _ = b.Add(Vet, ChatterEvent.Summoned, "Again? Fine.", "Third time this week.")
                .Add(Vet, ChatterEvent.TargetAcquired, "Another {target}. Marvellous.", "Seen one, seen 'em all.")
                .Add(Vet, ChatterEvent.Hurt, "Yep. That'll bruise.", "Been worse.")
                .Add(Vet, ChatterEvent.Killed, "That's that, then.", "Anticlimactic.")
                .Add(Vet, ChatterEvent.Idle, "I've stood in worse fields.", "Wake me when it's interesting.");
        }

        // Short names because they appear on every line above, and the strings
        // themselves are what a reader is here to look at.
        private const string C = LinePack.SharedPersonality;
        private const string Coward = "cowardly";
        private const string Boast = "boastful";
        private const string Duty = "dutiful";
        private const string Vet = "veteran";
    }
}
