using ChattyBones.Logic;

namespace ChattyBones.Tests
{
    /// <summary>
    /// Covers reading the event key a pack writes, brackets and all.
    /// </summary>
    /// <remarks>
    /// Mostly about what a mistake costs. This is the one file players edit by hand,
    /// and a context group that parses but never fires is the failure mode this mod
    /// keeps paying for - so almost everything here is a bad key being refused with a
    /// reason somebody could act on, rather than accepted and silently useless.
    /// </remarks>
    public class EventKeyTests
    {
        [Fact]
        public void APlainNameIsTheGroupThatSuitsAnywhere()
        {
            Assert.True(EventKey.TryParse("Idle", out EventKey key, out _));

            Assert.Equal(ChatterEvent.Idle, key.Kind);
            Assert.Null(key.Context);
            Assert.True(key.IsPlain);
        }

        [Fact]
        public void ABracketedNameCarriesItsContext()
        {
            Assert.True(EventKey.TryParse("Idle[biome=Swamp]", out EventKey key, out _));

            Assert.Equal(ChatterEvent.Idle, key.Kind);
            Assert.Equal("biome=Swamp", key.Context);
            Assert.False(key.IsPlain);
        }

        [Fact]
        public void SpacesInsideTheBracketsAreForgiven()
        {
            // A kindness to hand-editors, and it costs nothing.
            Assert.True(EventKey.TryParse("Idle[ biome = Swamp ]", out EventKey key, out _));

            Assert.Equal("biome=Swamp", key.Context);
        }

        [Fact]
        public void TheValueKeepsItsCase()
        {
            // It is matched against the game's own enum spelling, so BlackForest has
            // to survive as written rather than being folded to lower case.
            Assert.True(EventKey.TryParse("Idle[biome=BlackForest]", out EventKey key, out _));

            Assert.Equal("biome=BlackForest", key.Context);
        }

        [Theory]
        [InlineData("Idle[biome=Swamp", "never closed")]
        [InlineData("Idle[]", "empty")]
        [InlineData("Idle[biome]", "missing an =")]
        [InlineData("Idle[biome=]", "no value")]
        [InlineData("Idle[bioem=Swamp]", "not a context")]
        [InlineData("Idle[biome=Swamp,time=night]", "two contexts")]
        [InlineData("Idel[biome=Swamp]", "not one of the events")]
        [InlineData("Idel", "not one of the events")]
        [InlineData("", "missing")]
        public void ABadKeyIsRefusedWithAReason(string text, string expected)
        {
            Assert.False(EventKey.TryParse(text, out _, out string problem));
            Assert.Contains(expected, problem);
        }

        [Fact]
        public void AnUnknownContextSaysWhichOnesExist()
        {
            // Being told "no" is half the message. Being told what would have worked is
            // the half that gets somebody back to editing.
            Assert.False(EventKey.TryParse("Idle[weather=Rain]", out _, out string problem));

            Assert.Contains("biome", problem);
        }

        [Theory]
        [InlineData("Idle[time=morning]", "time=morning")]
        [InlineData("Idle[time=afternoon]", "time=afternoon")]
        [InlineData("Idle[time=evening]", "time=evening")]
        [InlineData("Idle[time=night]", "time=night")]
        [InlineData("Idle[home=yes]", "home=yes")]
        [InlineData("Idle[home=no]", "home=no")]
        public void TheOtherContextsParseTheSameWay(string text, string expected)
        {
            Assert.True(EventKey.TryParse(text, out EventKey key, out _));

            Assert.Equal(expected, key.Context);
        }

        [Theory]
        [InlineData("Idle[time=noon]")]
        [InlineData("Idle[time=dusk]")]
        [InlineData("Idle[time=Night]")]
        [InlineData("Idle[home=true]")]
        [InlineData("Idle[home=Yes]")]
        public void AValueOutsideAContextsVocabularyIsRefused(string text)
        {
            // The whole point of checking these here rather than at load: a value we
            // can see is wrong is caught against the line of the file it came from.
            // Capitals count, the same as they do everywhere else in a pack.
            Assert.False(EventKey.TryParse(text, out _, out string problem));

            Assert.Contains("is not one of the values", problem);
        }

        [Fact]
        public void ARefusedValueSaysWhichOnesWouldHaveWorked()
        {
            Assert.False(EventKey.TryParse("Idle[time=noon]", out _, out string problem));

            Assert.Contains("morning", problem);
            Assert.Contains("afternoon", problem);
            Assert.Contains("evening", problem);
            Assert.Contains("night", problem);
        }

        [Fact]
        public void ABiomeValueIsNotCheckedHere()
        {
            // Deliberately, and it is the one exception. The spellings are a Unity enum
            // this assembly cannot see, so Contexts.Unusable catches a wrong one at
            // load instead. A test rather than a comment because the asymmetry looks
            // like an oversight.
            Assert.True(EventKey.TryParse("Idle[biome=Swamps]", out EventKey key, out _));

            Assert.Equal("biome=Swamps", key.Context);
        }

        [Fact]
        public void TheSameGroupComparesEqualHoweverItWasBuilt()
        {
            Assert.True(EventKey.TryParse("Idle[biome=Swamp]", out EventKey one, out _));
            Assert.True(EventKey.TryParse("Idle[ biome=Swamp ]", out EventKey two, out _));

            Assert.Equal(one, two);
            Assert.Equal(one.GetHashCode(), two.GetHashCode());
        }

        [Fact]
        public void AContextGroupIsNotThePlainGroup()
        {
            Assert.True(EventKey.TryParse("Idle[biome=Swamp]", out EventKey tagged, out _));

            Assert.NotEqual(EventKey.Plain(ChatterEvent.Idle), tagged);
        }

        [Fact]
        public void ItWritesItselfBackTheWayAPackWouldSpellIt()
        {
            Assert.True(EventKey.TryParse("Idle[biome=Swamp]", out EventKey key, out _));

            Assert.Equal("Idle[biome=Swamp]", key.ToString());
            Assert.Equal("Idle", EventKey.Plain(ChatterEvent.Idle).ToString());
        }
    }
}
