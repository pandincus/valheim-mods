using System.Collections.Generic;
using ChattyBones.Logic;

namespace ChattyBones.Tests
{
    /// <summary>
    /// Covers the comma-separated list of events a player can switch off.
    /// </summary>
    /// <remarks>
    /// The one worth reading is <see cref="ANumberIsNotAnEvent"/>. Enum.TryParse takes
    /// "9" and hands back the ninth event looking perfectly successful, so without that
    /// guard somebody typing a number would silence something they never named.
    /// </remarks>
    public class EventListTests
    {
        [Fact]
        public void NothingTypedSilencesNothing()
        {
            Assert.Empty(EventList.Parse(null, out IReadOnlyList<string> unknown));
            Assert.Empty(unknown);

            Assert.Empty(EventList.Parse("   ", out unknown));
            Assert.Empty(unknown);
        }

        [Fact]
        public void NamesAreReadWhateverTheSpacingAndCase()
        {
            IReadOnlyList<ChatterEvent> off =
                EventList.Parse("  weather ,PlayerAte,  IDLE ", out IReadOnlyList<string> unknown);

            Assert.Empty(unknown);
            Assert.Equal(
                [ChatterEvent.Weather, ChatterEvent.PlayerAte, ChatterEvent.Idle],
                off);
        }

        [Fact]
        public void WhatIsNotAnEventIsReportedAndSkipped()
        {
            IReadOnlyList<ChatterEvent> off =
                EventList.Parse("Idle, Wetaher, Died", out IReadOnlyList<string> unknown);

            // The good ones still take effect. A typo costs the player that one entry
            // rather than the whole line, which is the difference between a note in the
            // log and a squad that inexplicably ignores the list.
            Assert.Equal([ChatterEvent.Idle, ChatterEvent.Died], off);
            Assert.Equal(["Wetaher"], unknown);
        }

        [Fact]
        public void ANumberIsNotAnEvent()
        {
            IReadOnlyList<ChatterEvent> off = EventList.Parse("9", out IReadOnlyList<string> unknown);

            Assert.Empty(off);
            Assert.Equal(["9"], unknown);
        }

        [Fact]
        public void AnEventNamedTwiceIsStillOneEvent()
        {
            IReadOnlyList<ChatterEvent> off =
                EventList.Parse("Idle, Idle, idle", out IReadOnlyList<string> unknown);

            Assert.Equal([ChatterEvent.Idle], off);
            Assert.Empty(unknown);
        }

        [Fact]
        public void TrailingAndDoubledCommasAreNotComplainedAbout()
        {
            // A player tidying a list leaves these behind constantly, and they say
            // nothing about intent - so they are skipped rather than reported.
            IReadOnlyList<ChatterEvent> off =
                EventList.Parse("Idle,,Died,", out IReadOnlyList<string> unknown);

            Assert.Equal([ChatterEvent.Idle, ChatterEvent.Died], off);
            Assert.Empty(unknown);
        }

        [Fact]
        public void EveryEventCanBeNamed()
        {
            // The list is only as good as the spellings the pack header advertises, so
            // every one of them has to round-trip through here.
            foreach (ChatterEvent kind in System.Enum.GetValues(typeof(ChatterEvent)))
            {
                IReadOnlyList<ChatterEvent> off =
                    EventList.Parse(kind.ToString(), out IReadOnlyList<string> unknown);

                Assert.Empty(unknown);
                Assert.Equal([kind], off);
            }
        }
    }
}
