using System;
using System.Linq;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Song.Notation;
using Xunit;

namespace DTXMania.Test.Song.Notation
{
    /// <summary>
    /// Ports DTXWeb drumMapping.test.ts: DTX channel to drum-staff step and notehead shape.
    /// Staff steps use the E4 = 0 origin documented on DrumStaffPosition.
    /// </summary>
    [Trait("Category", "Song")]
    public class DrumStaffMappingTests
    {
        [Theory]
        [InlineData(0x11, 9, "X")]          // closed hi-hat: G5 x
        [InlineData(0x12, 5, "Normal")]     // snare: C5
        [InlineData(0x13, 1, "Normal")]     // bass drum: F4
        [InlineData(0x14, 7, "Normal")]     // high tom: E5
        [InlineData(0x15, 6, "Normal")]     // low tom: D5
        [InlineData(0x16, 10, "X")]         // right crash: A5 x
        [InlineData(0x17, 3, "Normal")]     // floor tom: A4
        [InlineData(0x18, 9, "CircledX")]   // open hi-hat: G5 circled x
        [InlineData(0x19, 8, "X")]          // ride: F5 x
        [InlineData(0x1A, 11, "X")]         // left crash: B5 x
        [InlineData(0x1B, -1, "X")]         // pedal hi-hat: D4 x
        [InlineData(0x1C, 1, "Normal")]     // left bass: F4
        public void PlayableChannel_ShouldMapToExpectedStaffStepAndShape(int channel, int expectedStep, string expectedShape)
        {
            // NoteheadShape stays internal, so the theory names the shape and compares by name.
            var position = DrumStaffMapping.GetPosition(channel);

            Assert.Equal(expectedStep, position.StaffStep);
            Assert.Equal(expectedShape, position.Shape.ToString());
        }

        [Theory]
        [InlineData(0x11, 0x18)] // closed vs open hi-hat: same step, different notehead
        [InlineData(0x1B, 0x1C)] // pedal hi-hat vs left bass
        [InlineData(0x16, 0x19)] // right crash vs ride
        [InlineData(0x19, 0x1A)] // ride vs left crash
        [InlineData(0x16, 0x1A)] // right crash vs left crash
        public void DistinctChannels_ShouldNotShareAStaffPosition(int first, int second)
        {
            Assert.NotEqual(DrumStaffMapping.GetPosition(first), DrumStaffMapping.GetPosition(second));
        }

        [Fact]
        public void BassAndLeftBass_ShouldShareOneStaffPosition()
        {
            Assert.Equal(DrumStaffMapping.GetPosition(0x13), DrumStaffMapping.GetPosition(0x1C));
        }

        [Fact]
        public void PlayableChannels_ShouldOnlyCollideForBassAndLeftBass()
        {
            var sharedGroups = Enumerable.Range(0x11, 12)
                .GroupBy(channel => DrumStaffMapping.GetPosition(channel))
                .Where(group => group.Count() > 1)
                .Select(group => group.ToArray())
                .ToList();

            var shared = Assert.Single(sharedGroups);
            Assert.Equal(new[] { 0x13, 0x1C }, shared);
        }

        [Fact]
        public void PedalHiHatAndLeftBassNotes_ShouldMapOnChannelNotLaneIndex()
        {
            // Both sit in gameplay lane LP (LaneIndex 2); only Channel separates them on the staff.
            var pedalHat = new Note(laneIndex: 2, bar: 0, tick: 0, channel: 0x1B, value: "01");
            var leftBass = new Note(laneIndex: 2, bar: 0, tick: 0, channel: 0x1C, value: "01");

            Assert.Equal(-1, DrumStaffMapping.GetPosition(pedalHat.Channel).StaffStep);
            Assert.Equal(1, DrumStaffMapping.GetPosition(leftBass.Channel).StaffStep);
        }

        [Theory]
        [InlineData(0x00)]
        [InlineData(0x10)]
        [InlineData(0x1D)]
        public void UnsupportedChannel_ShouldThrowNamingTheChannel(int channel)
        {
            var ex = Assert.Throws<ArgumentOutOfRangeException>(() => DrumStaffMapping.GetPosition(channel));

            Assert.Equal("channel", ex.ParamName);
            Assert.Contains($"0x{channel:X2}", ex.Message);
        }
    }
}
