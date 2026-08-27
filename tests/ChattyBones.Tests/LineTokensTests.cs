using ChattyBones.Logic;

namespace ChattyBones.Tests
{
    /// <summary>
    /// Covers turning "Get lost, {target}!" into something a skeleton can shout.
    /// </summary>
    /// <remarks>
    /// Most of this is about what happens when a pack author gets it slightly wrong,
    /// because a pack is a file people edit by hand and they will. The rule we
    /// settled on is that mistakes should be visible rather than swallowed, except
    /// where being visible would look like the mod is broken.
    /// </remarks>
    public class LineTokensTests
    {
        private static LineTokens Full()
        {
            return new LineTokens("Greydwarf", "Dan", "Rattles");
        }

        [Fact]
        public void ALineWithNoTokensComesBackUntouched()
        {
            Assert.True(Full().TryRender("My bones are itchy.", out string line));
            Assert.Equal("My bones are itchy.", line);
        }

        [Fact]
        public void EachTokenIsFilledIn()
        {
            Assert.True(Full().TryRender("{name} here. Get lost, {target}! Right, {player}?", out string line));
            Assert.Equal("Rattles here. Get lost, Greydwarf! Right, Dan?", line);
        }

        [Fact]
        public void TheSameTokenCanAppearMoreThanOnce()
        {
            Assert.True(Full().TryRender("{target}? A {target}!", out string line));
            Assert.Equal("Greydwarf? A Greydwarf!", line);
        }

        [Fact]
        public void ATokenRightAtEachEndStillWorks()
        {
            Assert.True(Full().TryRender("{target} spotted by {name}", out string line));
            Assert.Equal("Greydwarf spotted by Rattles", line);
        }

        [Fact]
        public void ALineWantingSomethingWeDoNotHaveIsRefused()
        {
            // "Get lost, !" would look like the mod had fallen over. Refusing means
            // the skeleton picks something else or stays quiet, and every other line
            // in the pack carries on working.
            LineTokens noTarget = new(null, "Dan", "Rattles");

            Assert.False(noTarget.TryRender("Get lost, {target}!", out string line));
            Assert.Null(line);
        }

        [Fact]
        public void ALineNotWantingTheMissingThingIsStillFine()
        {
            LineTokens noTarget = new(null, "Dan", "Rattles");

            Assert.True(noTarget.TryRender("Thanks, {player}!", out string line));
            Assert.Equal("Thanks, Dan!", line);
        }

        [Fact]
        public void AMisspelledTokenIsLeftWhereItIs()
        {
            // Deliberate. You see "{targat}" floating over a skeleton's head and you
            // know immediately what you typed. Stripping it would leave you staring
            // at a gap wondering what happened.
            Assert.True(Full().TryRender("Get lost, {targat}!", out string line));
            Assert.Equal("Get lost, {targat}!", line);
        }

        [Fact]
        public void TokensAreCaseSensitive()
        {
            // "{Target}" is not one of ours, so it shows up as itself. I would rather
            // that than have it quietly work, because the day we add a fourth token
            // is the day loose matching starts producing surprises.
            Assert.True(Full().TryRender("{Target} and {target}", out string line));
            Assert.Equal("{Target} and Greydwarf", line);
        }

        [Fact]
        public void AnUnclosedBraceIsJustText()
        {
            Assert.True(Full().TryRender("What is {this even", out string line));
            Assert.Equal("What is {this even", line);
        }

        [Fact]
        public void AnEmptyPairOfBracesIsJustText()
        {
            Assert.True(Full().TryRender("Nothing {} to see", out string line));
            Assert.Equal("Nothing {} to see", line);
        }

        [Fact]
        public void AnEmptyLineRendersToAnEmptyLine()
        {
            // The pack builder drops blanks, so this should not arrive in practice.
            // It should not throw if it somehow does.
            Assert.True(Full().TryRender("", out string line));
            Assert.Equal("", line);
        }

        [Fact]
        public void ANullLineIsRefusedRatherThanThrowing()
        {
            Assert.False(Full().TryRender(null, out string line));
            Assert.Null(line);
        }

        [Fact]
        public void WithNothingSuppliedOnlyPlainLinesSurvive()
        {
            // What a skeleton looks like before we know anything about it - no name,
            // no target, and somehow no player either.
            LineTokens nothing = new(null, null, null);

            Assert.True(nothing.TryRender("Hrmph.", out string plain));
            Assert.Equal("Hrmph.", plain);

            Assert.False(nothing.TryRender("Hello {player}.", out _));
        }
    }
}
