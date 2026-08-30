namespace ChattyBones.Logic
{
    /// <summary>
    /// Whether something you just picked up is worth a remark.
    /// </summary>
    /// <remarks>
    /// Valheim hoovers items up automatically, so a session is hundreds of pickups and
    /// almost all of them are wood, stone, resin and feathers. Remarking on each would
    /// be unbearable, and remarking on none would waste the event.
    ///
    /// The filter matters much less than it looks, though, and that is deliberate:
    /// Looted is ranked just above Idle, so it can only ever speak when nothing else
    /// is happening. The worst a wrong answer here can do is give you a loot mutter
    /// where you would have had an idle one - which is why this can afford to be four
    /// blunt tests rather than a careful model of what a player finds interesting.
    ///
    /// Four signals, and any one of them is enough. They are chosen because Valheim
    /// already sorts its own items along these lines rather than because they are
    /// clever.
    /// </remarks>
    internal static class LootKind
    {
        /// <summary>Is this pickup worth saying something about?</summary>
        /// <returns>True for the things that are not raw material.</returns>
        /// <param name="trophy">Whether the game files it as a trophy.</param>
        /// <param name="coinValue">What a trader would pay, which is 0 for most things.</param>
        /// <param name="maxStackSize">How many fit in one slot. One means it is a thing rather than a resource.</param>
        /// <param name="quality">The item's upgrade level, which is 1 for anything not upgraded.</param>
        /// <remarks>
        /// Worked through with real items. A trophy is rare and you went and got it, so
        /// it passes on the first test. Coins, amber and rubies have a coin value where
        /// wood and stone have zero, which catches treasure without needing to know
        /// what treasure is. Anything that does not stack is equipment, a tool or a
        /// weapon - Valheim stacks its materials fifty deep and its swords not at all -
        /// so a max stack of one is a good proxy for "an object" rather than "some
        /// stuff". And a quality above one means you upgraded it, so it is yours in a
        /// way a fresh one is not.
        ///
        /// A leather scrap fails all four, which is the point.
        /// </remarks>
        internal static bool IsNotable(bool trophy, int coinValue, int maxStackSize, int quality)
        {
            return trophy
                || coinValue > 0
                || maxStackSize <= 1
                || quality > 1;
        }
    }
}
