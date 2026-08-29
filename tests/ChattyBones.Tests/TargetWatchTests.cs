using ChattyBones.Logic;

namespace ChattyBones.Tests
{
    /// <summary>
    /// Covers noticing that a fight has ended, which went wrong in a way worth
    /// keeping a test around for.
    /// </summary>
    /// <remarks>
    /// The one to read is <see cref="TheOperatorUnityGivesYouCannotAnswerThisQuestion"/>.
    /// The rest of this class pins a three-line rule; that one explains why the rule
    /// takes three booleans instead of two objects, and it is the only test here
    /// that would have caught the original bug rather than merely describing it.
    /// </remarks>
    public class TargetWatchTests
    {
        [Fact]
        public void AKillThatEndsTheFightIsNoticed()
        {
            // The case that was broken for two phases. The skeleton killed the last
            // greydwarf, nothing replaced it, and this has to come back true.
            Assert.True(TargetWatch.LostTarget(hadTarget: true, targetPresent: false, sameTarget: false));
        }

        [Fact]
        public void AKillFollowedStraightAwayByAnotherFightIsNoticed()
        {
            // MonsterAI re-picks on its own timer, so in a crowd a skeleton steps from
            // the greydwarf it just killed to the next one without ever having no
            // target. There is a target present, and it is not the one we were
            // following.
            Assert.True(TargetWatch.LostTarget(hadTarget: true, targetPresent: true, sameTarget: false));
        }

        [Fact]
        public void StillFightingTheSameThingIsNotNoticed()
        {
            Assert.False(TargetWatch.LostTarget(hadTarget: true, targetPresent: true, sameTarget: true));
        }

        [Fact]
        public void NotHavingBeenFightingAnythingIsNotNoticed()
        {
            // Idling with nothing around. Without the hadTarget guard this would fire
            // on every sweep and gloat about creatures that never existed.
            Assert.False(TargetWatch.LostTarget(hadTarget: false, targetPresent: false, sameTarget: false));
            Assert.False(TargetWatch.LostTarget(hadTarget: false, targetPresent: true, sameTarget: false));
        }

        [Fact]
        public void TheOperatorUnityGivesYouCannotAnswerThisQuestion()
        {
            // A standing demonstration of the trap, because a comment saying "Unity's
            // == treats a destroyed object as null" did not stop it being written.
            //
            // Destroyed is what a killed creature becomes, and it is the state that
            // matters: at that moment the AI's target is a real null and the one we
            // remembered is destroyed. Written the obvious way, the comparison asks
            // whether they differ and says no - so the kill was silently dropped.
            DestroyableStandIn remembered = new(destroyed: true);
            DestroyableStandIn nothing = null;

            Assert.False(nothing != remembered, "The trap has stopped reproducing - check this test, not the code.");

            // Which is why the two questions are asked with two different operators.
            bool targetPresent = nothing != null;
            bool sameTarget = ReferenceEquals(nothing, remembered);

            Assert.True(TargetWatch.LostTarget(hadTarget: true, targetPresent, sameTarget));
        }

        [Theory]
        [InlineData(0.00f, true)]
        [InlineData(0.25f, true)]
        [InlineData(1.00f, true)]
        [InlineData(1.01f, false)]
        [InlineData(18.13f, false)]
        public void OnlyAFreshSightingIsWorthGloatingAbout(float secondsSinceSeen, bool expected)
        {
            // 0.25 is one sweep, which is what an ordinary kill looks like. 18.13 is
            // taken from the log that found the bug: the kill was real, but it was not
            // noticed until a new target turned up eighteen seconds later, and being
            // thrown away at that point was correct.
            Assert.Equal(expected, TargetWatch.WorthRemarking(secondsSinceSeen, targetGone: true));
        }

        [Fact]
        public void ATargetThatSimplyChangedIsNotAKill()
        {
            // Losing interest is not winning. The AI switching targets while the old
            // one is alive and well must not produce a gloat.
            Assert.False(TargetWatch.WorthRemarking(secondsSinceSeen: 0.25f, targetGone: false));
        }

        /// <summary>Stands in for a UnityEngine.Object, equality quirk and all.</summary>
        /// <remarks>
        /// The test project cannot reference UnityEngine - that is the whole point of
        /// the Logic/ boundary - so the behavior is reproduced here instead. This
        /// mirrors what UnityEngine.Object actually does: a destroyed object compares
        /// equal to null, while the reference itself is still very much there.
        /// </remarks>
        private sealed class DestroyableStandIn
        {
            private readonly bool _destroyed;

            internal DestroyableStandIn(bool destroyed)
            {
                _destroyed = destroyed;
            }

            public static bool operator ==(DestroyableStandIn left, DestroyableStandIn right)
            {
                return IsNothing(left) == IsNothing(right)
                    && (IsNothing(left) || ReferenceEquals(left, right));
            }

            public static bool operator !=(DestroyableStandIn left, DestroyableStandIn right)
            {
                return !(left == right);
            }

            /// <summary>Null, or alive-but-destroyed. Unity draws no distinction and neither do we.</summary>
            private static bool IsNothing(DestroyableStandIn value)
            {
                return value is null || value._destroyed;
            }

            public override bool Equals(object obj)
            {
                return this == (obj as DestroyableStandIn);
            }

            public override int GetHashCode()
            {
                return 0;
            }
        }
    }
}
