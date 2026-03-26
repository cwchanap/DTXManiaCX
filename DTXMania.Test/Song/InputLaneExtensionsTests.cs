using DTXMania.Game.Lib.Song.Entities;
using Xunit;

namespace DTXMania.Test.Song
{
    /// <summary>
    /// Tests for InputLane enum and InputLaneExtensions utility methods.
    /// Covers channel mappings, index mappings, display names, and round-trip conversions.
    /// </summary>
    [Trait("Category", "Unit")]
    public class InputLaneExtensionsTests
    {
        #region FromChannel Tests - Valid Channels

        [Theory]
        [InlineData(0x1A, InputLane.Splash)]
        [InlineData(0x18, InputLane.FloorTomLeftCymbal)]
        [InlineData(0x1B, InputLane.HiHatFootLeftCrash)]
        [InlineData(0x12, InputLane.LeftPedal)]
        [InlineData(0x14, InputLane.SnareDrum)]
        [InlineData(0x13, InputLane.HiHat)]
        [InlineData(0x15, InputLane.BassDrum)]
        [InlineData(0x16, InputLane.HighTom)]
        [InlineData(0x17, InputLane.LowTomRightCymbal)]
        [InlineData(0x11, InputLane.FloorTomLeftCymbal)] // alternate channel for FloorTomLeftCymbal
        [InlineData(0x1C, InputLane.HiHatFootLeftCrash)] // alternate channel for HiHatFootLeftCrash
        [InlineData(0x19, InputLane.LowTomRightCymbal)]  // alternate channel for LowTomRightCymbal
        public void FromChannel_ValidChannel_ShouldReturnCorrectLane(int channel, InputLane expected)
        {
            var result = InputLaneExtensions.FromChannel(channel);
            Assert.Equal(expected, result);
        }

        #endregion

        #region FromChannel Tests - Invalid Channels

        [Theory]
        [InlineData(0xFF)]
        [InlineData(0)]
        [InlineData(-1)]
        public void FromChannel_InvalidChannel_ShouldReturnNull(int channel)
        {
            Assert.Null(InputLaneExtensions.FromChannel(channel));
        }

        #endregion

        #region ToLaneIndex Tests

        [Theory]
        [InlineData(InputLane.Splash, 0)]
        [InlineData(InputLane.FloorTomLeftCymbal, 1)]
        [InlineData(InputLane.HiHatFootLeftCrash, 2)]
        [InlineData(InputLane.LeftPedal, 3)]
        [InlineData(InputLane.SnareDrum, 4)]
        [InlineData(InputLane.HiHat, 5)]
        [InlineData(InputLane.BassDrum, 6)]
        [InlineData(InputLane.HighTom, 7)]
        [InlineData(InputLane.LowTomRightCymbal, 8)]
        public void ToLaneIndex_ValidLane_ShouldReturnCorrectIndex(InputLane lane, int expectedIndex)
        {
            Assert.Equal(expectedIndex, lane.ToLaneIndex());
        }

        [Fact]
        public void ToLaneIndex_InvalidLane_ShouldReturnNegativeOne()
        {
            var invalidLane = (InputLane)0xFF;
            Assert.Equal(-1, invalidLane.ToLaneIndex());
        }

        #endregion

        #region FromLaneIndex Tests - Valid Indices

        [Theory]
        [InlineData(0, InputLane.Splash)]
        [InlineData(1, InputLane.FloorTomLeftCymbal)]
        [InlineData(2, InputLane.HiHatFootLeftCrash)]
        [InlineData(3, InputLane.LeftPedal)]
        [InlineData(4, InputLane.SnareDrum)]
        [InlineData(5, InputLane.HiHat)]
        [InlineData(6, InputLane.BassDrum)]
        [InlineData(7, InputLane.HighTom)]
        [InlineData(8, InputLane.LowTomRightCymbal)]
        public void FromLaneIndex_ValidIndex_ShouldReturnCorrectLane(int index, InputLane expected)
        {
            var result = InputLaneExtensions.FromLaneIndex(index);
            Assert.Equal(expected, result);
        }

        #endregion

        #region FromLaneIndex Tests - Invalid Indices

        [Theory]
        [InlineData(-1)]
        [InlineData(9)]
        [InlineData(100)]
        public void FromLaneIndex_InvalidIndex_ShouldReturnNull(int index)
        {
            Assert.Null(InputLaneExtensions.FromLaneIndex(index));
        }

        #endregion

        #region GetDisplayName Tests

        [Theory]
        [InlineData(InputLane.Splash, "Splash/Crash")]
        [InlineData(InputLane.FloorTomLeftCymbal, "Floor Tom/Left Cymbal")]
        [InlineData(InputLane.HiHatFootLeftCrash, "Hi-Hat Foot/Left Crash")]
        [InlineData(InputLane.LeftPedal, "Left Pedal")]
        [InlineData(InputLane.SnareDrum, "Snare Drum")]
        [InlineData(InputLane.HiHat, "Hi-Hat")]
        [InlineData(InputLane.BassDrum, "Bass Drum")]
        [InlineData(InputLane.HighTom, "High Tom")]
        [InlineData(InputLane.LowTomRightCymbal, "Low Tom/Right Cymbal")]
        public void GetDisplayName_ValidLane_ShouldReturnNonEmptyString(InputLane lane, string expectedName)
        {
            var name = lane.GetDisplayName();
            Assert.Equal(expectedName, name);
        }

        [Fact]
        public void GetDisplayName_InvalidLane_ShouldReturnUnknown()
        {
            var invalidLane = (InputLane)0xFF;
            Assert.Equal("Unknown", invalidLane.GetDisplayName());
        }

        #endregion

        #region Round-Trip Conversion Tests

        [Theory]
        [InlineData(InputLane.Splash)]
        [InlineData(InputLane.FloorTomLeftCymbal)]
        [InlineData(InputLane.HiHatFootLeftCrash)]
        [InlineData(InputLane.LeftPedal)]
        [InlineData(InputLane.SnareDrum)]
        [InlineData(InputLane.HiHat)]
        [InlineData(InputLane.BassDrum)]
        [InlineData(InputLane.HighTom)]
        [InlineData(InputLane.LowTomRightCymbal)]
        public void LaneIndex_RoundTrip_LaneToIndexToLane_ShouldReturnOriginalLane(InputLane lane)
        {
            int index = lane.ToLaneIndex();
            var roundTrip = InputLaneExtensions.FromLaneIndex(index);
            Assert.Equal(lane, roundTrip);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        [InlineData(6)]
        [InlineData(7)]
        [InlineData(8)]
        public void LaneIndex_RoundTrip_IndexToLaneToIndex_ShouldReturnOriginalIndex(int index)
        {
            var lane = InputLaneExtensions.FromLaneIndex(index);
            Assert.NotNull(lane);
            int roundTrip = lane!.Value.ToLaneIndex();
            Assert.Equal(index, roundTrip);
        }

        [Theory]
        [InlineData(0x1A, 0)]
        [InlineData(0x18, 1)]
        [InlineData(0x1B, 2)]
        [InlineData(0x12, 3)]
        [InlineData(0x14, 4)]
        [InlineData(0x13, 5)]
        [InlineData(0x15, 6)]
        [InlineData(0x16, 7)]
        [InlineData(0x17, 8)]
        public void Channel_ToLaneIndex_ShouldProduceCorrectIndex(int channel, int expectedIndex)
        {
            var lane = InputLaneExtensions.FromChannel(channel);
            Assert.NotNull(lane);
            Assert.Equal(expectedIndex, lane!.Value.ToLaneIndex());
        }

        #endregion

        #region Multi-Channel Mapping Tests

        [Theory]
        [InlineData(0x11, 0x18)] // FloorTomLeftCymbal: alternate and primary map to same lane
        [InlineData(0x1C, 0x1B)] // HiHatFootLeftCrash: alternate and primary map to same lane
        [InlineData(0x19, 0x17)] // LowTomRightCymbal: alternate and primary map to same lane
        public void MultiChannelLanes_BothChannels_ShouldMapToSameLane(int alternateChannel, int primaryChannel)
        {
            Assert.Equal(
                InputLaneExtensions.FromChannel(primaryChannel),
                InputLaneExtensions.FromChannel(alternateChannel));
        }

        #endregion
    }
}
