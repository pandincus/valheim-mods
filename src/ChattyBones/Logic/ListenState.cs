namespace ChattyBones.Logic
{
    /// <summary>Remember which of one skeleton's remarks we have already been told about.</summary>
    /// <remarks>
    /// A skeleton broadcasts what it said as a counter in its ZDO, and every client
    /// polls it. Three things have to be true at once, and each of them cost a bug:
    ///
    /// - <b>The first look never speaks.</b> A skeleton is rebuilt every time you walk
    ///   into its zone and its ZDO still holds the last thing it said, so without this
    ///   the whole squad greets its summoner again every time you come over the hill.
    /// - <b>Anything we said ourselves is already accounted for.</b> Ownership of a
    ///   summon moves between clients by zone every two seconds, so a client that spoke
    ///   through a skeleton is very often a listener for that same skeleton a moment
    ///   later - and would otherwise find a counter it had never seen and say the line
    ///   a second time. Watched happening in the first two-machine session.
    /// - <b>The master switch can put the whole thing to sleep.</b> Nothing polls while
    ///   the mod is off, so the counter on the wire moves on without us and everything
    ///   we remember goes stale. <see cref="Forget"/> makes the next look a first look,
    ///   which swallows the backlog rather than saying all of it at once.
    ///
    /// All three are rules about a counter rather than about Valheim, which is what lets
    /// them live here where something can check them - see ListenStateTests.
    /// </remarks>
    internal sealed class ListenState
    {
        /// <summary>Whether we have looked at this skeleton at all yet.</summary>
        private bool _synced;

        /// <summary>The counter on the last thing we drew, or 0.</summary>
        private int _lastHeard;

        /// <summary>Decide whether what a skeleton is broadcasting is news to us.</summary>
        /// <returns>True when it is something we have neither drawn nor said ourselves.</returns>
        /// <param name="have">Whether it has ever said anything at all.</param>
        /// <param name="counter">The counter it is broadcasting now.</param>
        /// <remarks>
        /// A skeleton that has never spoken syncs at 0 and is still heard the first
        /// time it does, which is why <paramref name="counter"/> is taken whether or
        /// not there was anything there.
        /// </remarks>
        internal bool ShouldDraw(bool have, int counter)
        {
            if (!_synced)
            {
                _synced = true;
                _lastHeard = have ? counter : 0;
                return false;
            }

            if (!have || counter == _lastHeard)
            {
                return false;
            }

            _lastHeard = counter;
            return true;
        }

        /// <summary>Account for a remark we made ourselves, while we owned it.</summary>
        /// <param name="counter">The counter we just wrote.</param>
        internal void Recorded(int counter)
        {
            _synced = true;
            _lastHeard = counter;
        }

        /// <summary>Treat the next look as a first look, and say nothing for it.</summary>
        internal void Forget()
        {
            _synced = false;
        }
    }
}
