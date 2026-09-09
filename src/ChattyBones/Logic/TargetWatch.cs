namespace ChattyBones.Logic
{
    /// <summary>
    /// When a skeleton has stopped fighting what it was fighting.
    /// </summary>
    /// <remarks>
    /// Nothing here needs a game running, but that is not really why it exists. It
    /// takes three separate booleans rather than two <c>Character</c>s because
    /// conflating them is precisely the bug this was extracted after:
    /// <c>UnityEngine.Object</c> overloads <c>==</c> so a destroyed object equals
    /// null, and killing something destroys it, so "is there a target" and "is it
    /// the same target" quietly become the same question at the exact moment they
    /// differ. Written as <c>target != _lastTarget</c>, a fight that ended with
    /// nothing to move on to asked <c>null != null</c>, answered no, and was missed.
    ///
    /// Splitting them means a caller has to say which question it is asking, and
    /// the answers can be checked without a Valheim to destroy things in.
    /// </remarks>
    internal static class TargetWatch
    {
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
        /// skeleton that finished one greydwarf and stepped straight onto the next,
        /// which MonsterAI does on its own timer - roughly one fight in eight, and
        /// precisely in the crowded ones the squad has most to say about. Only
        /// checking that it changed misses every fight that ends with nothing left.
        /// </remarks>
        internal static bool LostTarget(bool hadTarget, bool targetPresent, bool sameTarget)
        {
            return hadTarget && (!targetPresent || !sameTarget);
        }
    }
}
