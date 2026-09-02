using ChattyBones.Logic;

namespace ChattyBones.Tests
{
    /// <summary>
    /// Covers the detail record that travels beside an utterance.
    /// </summary>
    /// <remarks>
    /// The two worth reading first are the pair about a record written by a different
    /// version of the mod. Everything else here is a round trip; those two are the
    /// reason the format is a delimited list with the newest field on the end rather
    /// than something fixed-width, and they are the ones that stop two players on
    /// different builds getting an exception instead of a joke.
    /// </remarks>
    public class DetailWireTests
    {
        [Fact]
        public void EveryFieldSurvivesTheRoundTrip()
        {
            LineDetails sent = new(
                weapon: "$item_sword_mistwalker",
                weaponType: "sword",
                damage: "slash",
                status: "$se_burning",
                biome: "$biome_blackforest",
                item: "$item_carrotsoup",
                skill: "$skill_blocking");

            Assert.True(DetailWire.TryUnpack(DetailWire.Pack(sent), out LineDetails got));

            Assert.Equal(sent.Weapon, got.Weapon);
            Assert.Equal(sent.WeaponType, got.WeaponType);
            Assert.Equal(sent.Damage, got.Damage);
            Assert.Equal(sent.Status, got.Status);
            Assert.Equal(sent.Biome, got.Biome);
            Assert.Equal(sent.Item, got.Item);
            Assert.Equal(sent.Skill, got.Skill);
        }

        [Fact]
        public void AnEventWithNothingToSayAboutItselfPacksToNothing()
        {
            // Which is most of them. Empty rather than null because it is written
            // straight into a ZDO string field, and a listener needs it to overwrite
            // whatever the previous utterance left there.
            Assert.Equal(string.Empty, DetailWire.Pack(default));
            Assert.False(DetailWire.TryUnpack(string.Empty, out _));
            Assert.False(DetailWire.TryUnpack(null, out _));
        }

        [Fact]
        public void AGapInTheMiddleComesBackAsNothingRatherThanAsBlank()
        {
            // LineTokens refuses a line whose token is null and renders one whose
            // token is blank, so this is the difference between quietly choosing
            // another line and putting "Nice  hit!" over somebody's head.
            LineDetails sent = new(weapon: "$item_axe_bronze", damage: "slash");

            Assert.True(DetailWire.TryUnpack(DetailWire.Pack(sent), out LineDetails got));

            Assert.Equal("$item_axe_bronze", got.Weapon);
            Assert.Null(got.WeaponType);
            Assert.Equal("slash", got.Damage);
        }

        [Fact]
        public void OnlyTheFieldsUpToTheLastOneUsedAreWritten()
        {
            // Not for the bytes, which do not matter at the rate a squad speaks. It is
            // so that the common case - one field, near the front - reads as itself in
            // a log rather than as a row of pipes.
            Assert.Equal("$item_sword_iron", DetailWire.Pack(new LineDetails(weapon: "$item_sword_iron")));
            Assert.Equal("|sword", DetailWire.Pack(new LineDetails(weaponType: "sword")));
        }

        [Fact]
        public void ARecordFromANewerVersionIsReadAsFarAsWeUnderstandIt()
        {
            // A later build adds an eighth field. We keep the seven we know and ignore
            // the rest, rather than refusing the whole record and going silent.
            Assert.True(
                DetailWire.TryUnpack(
                    "$item_sword_iron|sword|slash||||$skill_swords|something_new|and_another",
                    out LineDetails got));

            Assert.Equal("$item_sword_iron", got.Weapon);
            Assert.Equal("$skill_swords", got.Skill);
        }

        [Fact]
        public void ARecordFromAnOlderVersionLeavesTheTailEmpty()
        {
            // The other direction: they wrote three fields because that was all their
            // build had. The four we grew since are simply not there, and the lines
            // asking for them are passed over.
            Assert.True(DetailWire.TryUnpack("$item_sword_iron|sword|slash", out LineDetails got));

            Assert.Equal("slash", got.Damage);
            Assert.Null(got.Status);
            Assert.Null(got.Item);
            Assert.Null(got.Skill);
        }

        [Fact]
        public void AFieldCarryingTheSeparatorIsDroppedRatherThanBreakingTheRecord()
        {
            // Practically speaking this cannot happen - every value is a localization
            // key or a word from one of two fixed tables - so it is insurance against
            // a future token read off something less disciplined. The failure it
            // prevents is the nasty kind: one field with a pipe in it shifts every
            // field after it along by one, so the reader gets a biome in the item slot
            // and says something confidently wrong.
            LineDetails sent = new(weapon: "bad|name", weaponType: "sword", damage: "slash");

            Assert.True(DetailWire.TryUnpack(DetailWire.Pack(sent), out LineDetails got));

            Assert.Null(got.Weapon);
            Assert.Equal("sword", got.WeaponType);
            Assert.Equal("slash", got.Damage);
        }

        [Fact]
        public void GarbageIsReadAsFieldsWeHaveNoValueFor()
        {
            // Somebody else's mod writing to a key that happens to collide, or a
            // corrupted field. It must not throw: this runs inside the sweep, on a
            // value another machine wrote.
            Assert.True(DetailWire.TryUnpack("||||||", out LineDetails empty));
            Assert.Null(empty.Weapon);
            Assert.Null(empty.Skill);

            Assert.True(DetailWire.TryUnpack("not a record at all", out LineDetails odd));
            Assert.Equal("not a record at all", odd.Weapon);
            Assert.Null(odd.WeaponType);
        }
    }
}
