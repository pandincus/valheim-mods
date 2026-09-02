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
        [Fact]
        public void TheEventTokensRenderLikeAnyOther()
        {
            LineTokens tokens = new(
                target: null,
                player: "Ragnar",
                name: "Botvid",
                details: new LineDetails(
                    weapon: "Mistwalker", weaponType: "sword", damage: "slash", status: "Burning"));

            Assert.True(tokens.TryRender(
                "Nice {damage} hit with that {weapon}, {player} - {weapontype} work. {status}!", out string line));

            Assert.Equal("Nice slash hit with that Mistwalker, Ragnar - sword work. Burning!", line);
        }

        [Fact]
        public void ALineWantingAWeaponIsPassedOverWhenThereIsNone()
        {
            LineTokens tokens = new(target: null, player: "Ragnar", name: "Botvid");

            Assert.False(tokens.TryRender("Nice hit with that {weapon}!", out _));
            Assert.True(tokens.TryRender("Nice hit!", out _));
        }

        [Fact]
        public void PartialDetailsRefuseOnlyWhatIsMissing()
        {
            // A hit always knows its damage type but may not know the weapon - an
            // arrow in flight, say. The {damage} line should still be sayable.
            LineTokens tokens = new(
                target: null,
                player: "Ragnar",
                name: "Botvid",
                details: new LineDetails(damage: "pierce"));

            Assert.True(tokens.TryRender("Right in the {damage}.", out string line));
            Assert.Equal("Right in the pierce.", line);

            Assert.False(tokens.TryRender("That {weapon} of yours.", out _));
        }

        private static LineTokens Full()
        {
            return new LineTokens(
                target: "Greydwarf", player: "Ragnar", name: "Rattles", companion: "Bjorn", ally: "Sigrid");
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
            Assert.Equal("Rattles here. Get lost, Greydwarf! Right, Ragnar?", line);
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
            LineTokens noTarget = new(target: null, player: "Ragnar", name: "Rattles");

            Assert.False(noTarget.TryRender("Get lost, {target}!", out string line));
            Assert.Null(line);
        }

        [Fact]
        public void ALineNotWantingTheMissingThingIsStillFine()
        {
            LineTokens noTarget = new(target: null, player: "Ragnar", name: "Rattles");

            Assert.True(noTarget.TryRender("Thanks, {player}!", out string line));
            Assert.Equal("Thanks, Ragnar!", line);
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
            // that than have it quietly work: loose matching is only cheap while the
            // set of tokens is small, and this set grows.
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
        public void ACompanionIsNamedLikeAnyOtherToken()
        {
            // The whole point of the companion events. "Ach, Bjorn!" reads as one
            // skeleton reacting to another it knows, which is far better than
            // something vague about a colleague.
            Assert.True(Full().TryRender("Ach, {companion}!", out string line));
            Assert.Equal("Ach, Bjorn!", line);
        }

        [Fact]
        public void ACompanionLineWhereThereIsNoCompanionIsRefused()
        {
            // {companion} is only filled in for the events that are about another
            // skeleton. Put one in an idle line and that line stays quiet rather than
            // rendering "Ach, !".
            LineTokens alone = new(target: "Greydwarf", player: "Ragnar", name: "Rattles");

            Assert.False(alone.TryRender("Ach, {companion}!", out _));
        }

        [Fact]
        public void AnAllyIsNamedLikeAnyOtherToken()
        {
            Assert.True(Full().TryRender("Hail, {ally}.", out string line));
            Assert.Equal("Hail, Sigrid.", line);
        }

        [Fact]
        public void AnAllyLineWithNobodyThereIsRefused()
        {
            // The usual case, and the reason {ally} is safe to scatter through a pack:
            // on your own nobody is ever nearby, so every line asking for one is passed
            // over rather than rendered as "Hail, .". It is also how a listener behaves
            // when the person being named is not loaded on their machine.
            LineTokens alone = new(target: "Greydwarf", player: "Ragnar", name: "Rattles");

            Assert.False(alone.TryRender("Hail, {ally}.", out _));
        }

        [Fact]
        public void ASkeletonCanNameItselfAndAnotherInOneLine()
        {
            Assert.True(Full().TryRender("{name} has got you, {companion}!", out string line));
            Assert.Equal("Rattles has got you, Bjorn!", line);
        }

        [Fact]
        public void WithNothingSuppliedOnlyPlainLinesSurvive()
        {
            // What a skeleton looks like before we know anything about it - no name,
            // no target, and somehow no player either.
            LineTokens nothing = new(target: null, player: null, name: null);

            Assert.True(nothing.TryRender("Hrmph.", out string plain));
            Assert.Equal("Hrmph.", plain);

            Assert.False(nothing.TryRender("Hello {player}.", out _));
        }
    }
}
