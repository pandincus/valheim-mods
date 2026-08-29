namespace ChattyBones.Logic
{
    /// <summary>
    /// When a skeleton has stopped fighting what it was fighting, and whether that
    /// is worth a remark.
    /// </summary>
    /// <remarks>
    /// Nothing here needs a game running, but that is not really why it exists. It
    /// takes three separate booleans rather than two <c>Character</c>s because
    /// conflating them is precisely the bug this was extracted after:
    /// <c>UnityEngine.Object</c> overloads <c>==</c> so a destroyed object equals
    /// null, and killing something destroys it, so "is there a target" and "is it
    /// the same target" quietly become the same question at the exact moment they
    /// differ. Written as <c>target != _lastTarget</c>, a kill with nothing to move
    /// on to asked <c>null != null</c>, answered no, and was dropped.
    ///
    /// Splitting them means a caller has to say which question it is asking, and
    /// the answers can be checked without a Valheim to destroy things in.
    /// </remarks>
    internal static class TargetWatch
    {
        /// <summary>How out of date a sighting can be and still be worth a remark.</summary>
        /// <remarks>
        /// Four sweeps at the usual quarter-second rate. Long enough that an ordinary
        /// frame hitch does not lose a kill, short enough that a gap in sweeping - the
        /// master switch is advertised as safe to flip mid-game, and ownership can move
        /// away and back - cannot bank one and gloat about it minutes later.
        /// </remarks>
        internal const float StaleSeconds = 1f;

        /// <summary>Has the fight we were watching ended?</summary>
        /// <returns>True when the thing we were following is no longer the thing being fought.</returns>
        /// <param name="hadTarget">Whether we were following anything at all.</param>
        /// <param name="targetPresent">
        /// Whether there is a target now. Ask this with Unity's own <c>== null</c>,
        /// which counts a destroyed object as absent - here that is exactly right.
        /// </param>
        /// <param name="sameTarget">
        /// Whether that target is the object we were already following. Ask this with
        /// <c>ReferenceEquals</c>, never with <c>==</c>, or a destroyed target and a
        /// missing one become indistinguishable.
        /// </param>
        /// <remarks>
        /// Both halves matter. Only checking that the target went away misses the
        /// skeleton that killed a greydwarf and stepped straight onto the next one,
        /// which MonsterAI does on its own timer - roughly one kill in eight, and
        /// precisely in the crowded fights the squad has most to say about. Only
        /// checking that it changed misses every kill that ends a fight.
        /// </remarks>
        internal static bool LostTarget(bool hadTarget, bool targetPresent, bool sameTarget)
        {
            return hadTarget && (!targetPresent || !sameTarget);
        }

        /// <summary>Is the end of that fight worth saying something about?</summary>
        /// <returns>True when the skeleton should gloat.</returns>
        /// <param name="secondsSinceSeen">How long ago we last saw the target alive.</param>
        /// <param name="targetGone">Whether it is dead or destroyed, rather than merely somebody else's problem now.</param>
        /// <remarks>
        /// The staleness test is what stops a boast about something killed ten minutes
        /// ago. It also turned out to be the thing that made the bug above visible
        /// rather than merely wrong: kills were noticed eventually, at the moment a new
        /// target appeared, and then correctly thrown away for being twenty seconds
        /// stale.
        /// </remarks>
        internal static bool WorthRemarking(float secondsSinceSeen, bool targetGone)
        {
            return targetGone && secondsSinceSeen <= StaleSeconds;
        }
    }
}
