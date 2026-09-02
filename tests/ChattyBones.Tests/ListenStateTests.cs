using ChattyBones.Logic;

namespace ChattyBones.Tests
{
    /// <summary>
    /// Covers what one client remembers about what one skeleton has said.
    /// </summary>
    /// <remarks>
    /// Three of these are bugs that reached a live session, and the first two were only
    /// findable with two machines. They are here rather than beside the ZDO reads that
    /// use them because the mod half of ChattyBones is not compiled by this project at
    /// all - so before this existed, deleting the fix in
    /// <see cref="ListenState.Recorded"/> outright left every test green.
    /// </remarks>
    public class ListenStateTests
    {
        [Fact]
        public void TheFirstLookNeverSpeaks()
        {
            ListenState heard = new();

            // A skeleton is rebuilt every time you walk into its zone, and its ZDO still
            // holds whatever it said an hour ago.
            Assert.False(heard.ShouldDraw(have: true, counter: 7));
        }

        [Fact]
        public void SomethingNewIsDrawnOnce()
        {
            ListenState heard = new();

            Assert.False(heard.ShouldDraw(have: true, counter: 7));
            Assert.True(heard.ShouldDraw(have: true, counter: 8));
            Assert.False(heard.ShouldDraw(have: true, counter: 8));
        }

        [Fact]
        public void ASkeletonThatHasNeverSpokenIsStillHeardTheFirstTimeItDoes()
        {
            ListenState heard = new();

            // Nothing on the wire yet, so the sync lands on 0 rather than on nothing.
            Assert.False(heard.ShouldDraw(have: false, counter: 0));
            Assert.True(heard.ShouldDraw(have: true, counter: 1));
        }

        [Fact]
        public void WeAreNeverToldAboutSomethingWeSaidOurselves()
        {
            ListenState heard = new();

            // The shape that reached a live session: this client was listening, took
            // ownership long enough for one line, and lost it again. Without Recorded,
            // the counter it wrote itself looks like news the moment it is a listener
            // again, and the skeleton says the line twice.
            Assert.False(heard.ShouldDraw(have: true, counter: 5));
            heard.Recorded(6);

            Assert.False(heard.ShouldDraw(have: true, counter: 6));
        }

        [Fact]
        public void WhatSomebodyElseSaidAfterUsIsStillNews()
        {
            ListenState heard = new();

            heard.Recorded(6);

            // Ownership moved on and the new owner spoke. Accounting for our own line
            // must not deafen us to the next one.
            Assert.True(heard.ShouldDraw(have: true, counter: 7));
        }

        [Fact]
        public void RecordingIsEnoughOnItsOwnToCountAsHavingLooked()
        {
            ListenState heard = new();

            // An owner that speaks before it has ever listened - the ordinary case for
            // a skeleton you summoned yourself.
            heard.Recorded(1);

            Assert.False(heard.ShouldDraw(have: true, counter: 1));
            Assert.True(heard.ShouldDraw(have: true, counter: 2));
        }

        [Fact]
        public void ForgettingSwallowsWhateverWasMissed()
        {
            ListenState heard = new();

            Assert.False(heard.ShouldDraw(have: true, counter: 5));

            // The master switch went off here, and four things were said while nothing
            // was polling. Turning it back on should not say the last of them.
            heard.Forget();

            Assert.False(heard.ShouldDraw(have: true, counter: 9));
            Assert.True(heard.ShouldDraw(have: true, counter: 10));
        }

        [Fact]
        public void NothingOnTheWireIsNotNews()
        {
            ListenState heard = new();

            Assert.False(heard.ShouldDraw(have: true, counter: 5));

            // An utterance this build cannot make sense of reads as nothing at all, and
            // nothing at all is not a change worth drawing. Without the have clause this
            // is a counter of 0 arriving after one of 5, which looks exactly like news.
            Assert.False(heard.ShouldDraw(have: false, counter: 0));
        }

        [Fact]
        public void AFirstLookAtNothingSyncsAtNothing()
        {
            ListenState heard = new();

            // The counter is meaningless when there is nothing there, so the first look
            // has to land on 0 rather than on whatever came back - otherwise the real
            // first utterance can collide with it and be swallowed.
            Assert.False(heard.ShouldDraw(have: false, counter: 9));

            Assert.True(heard.ShouldDraw(have: true, counter: 9));
        }

        [Fact]
        public void TheCounterWrappingRoundIsStillNews()
        {
            ListenState heard = new();

            // Utterance.NextCounter runs 1..255 and never yields 0, so the step after
            // 255 is 1 - which is smaller than what came before it and must not be
            // mistaken for the same line.
            Assert.False(heard.ShouldDraw(have: true, counter: 255));
            Assert.True(heard.ShouldDraw(have: true, counter: 1));
        }
    }
}
