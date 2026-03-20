using DTXMania.Game.Lib.Song.Entities;
using Xunit;

namespace DTXMania.Test.Song
{
    /// <summary>
    /// Tests for InputLane enum and InputLaneExtensions utility methods.
    /// Covers channel mappings, index mappings, display names, and round-trip conversions.
    /// </summary>
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
        public void FromChannel_ValidPrimaryChannel_ShouldReturnCorrectLane(int channel, InputLane expected)
        {
            var result = InputLaneExtensions.FromChannel(channel);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void FromChannel_Channel0x11_ShouldReturnFloorTomLeftCymbal()
        {
            // 0x11 is an alternate channel that maps to the same lane as 0x18
            var result = InputLaneExtensions.FromChannel(0x11);
            Assert.Equal(InputLane.FloorTomLeftCymbal, result);
        }

        [Fact]
        public void FromChannel_Channel0x1C_ShouldReturnHiHatFootLeftCrash()
        {
            // 0x1C is an alternate channel that maps to the same lane as 0x1B
            var result = InputLaneExtensions.FromChannel(0x1C);
            Assert.Equal(InputLane.HiHatFootLeftCrash, result);
        }

        [Fact]
        public void FromChannel_Channel0x19_ShouldReturnLowTomRightCymbal()
        {
            // 0x19 is an alternate channel that maps to the same lane as 0x17
            var result = InputLaneExtensions.FromChannel(0x19);
            Assert.Equal(InputLane.LowTomRightCymbal, result);
        }

        #endregion

        #region FromChannel Tests - Invalid Channels

        [Fact]
        public void FromChannel_InvalidChannel_ShouldReturnNull()
        {
            var result = InputLaneExtensions.FromChannel(0xFF);
            Assert.Null(result);
        }

        [Fact]
        public void FromChannel_ZeroChannel_ShouldReturnNull()
        {
            var result = InputLaneExtensions.FromChannel(0);
            Assert.Null(result);
        }

        [Fact]
        public void FromChannel_NegativeChannel_ShouldReturnNull()
        {
            var result = InputLaneExtensions.FromChannel(-1);
            Assert.Null(result);
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
            // Cast an invalid value to InputLane
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

        [Fact]
        public void FromLaneIndex_NegativeIndex_ShouldReturnNull()
        {
            var result = InputLaneExtensions.FromLaneIndex(-1);
            Assert.Null(result);
        }

        [Fact]
        public void FromLaneIndex_IndexTooLarge_ShouldReturnNull()
        {
            var result = InputLaneExtensions.FromLaneIndex(9);
            Assert.Null(result);
        }

        [Fact]
        public void FromLaneIndex_LargeIndex_ShouldReturnNull()
        {
            var result = InputLaneExtensions.FromLaneIndex(100);
            Assert.Null(result);
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

        [Fact]
        public void MultiChannelLanes_BothChannels_ShouldMapToSameLane()
        {
            // Channels 0x11 and 0x18 should map to the same lane (FloorTomLeftCymbal)
            var lane1 = InputLaneExtensions.FromChannel(0x11);
            var lane2 = InputLaneExtensions.FromChannel(0x18);
            Assert.Equal(lane1, lane2);
        }

        [Fact]
        public void MultiChannelLanes_HiHatFootBothChannels_ShouldMapToSameLane()
        {
            // Channels 0x1B and 0x1C should map to HiHatFootLeftCrash
            var lane1 = InputLaneExtensions.FromChannel(0x1B);
            var lane2 = InputLaneExtensions.FromChannel(0x1C);
            Assert.Equal(lane1, lane2);
        }

        [Fact]
        public void MultiChannelLanes_LowTomBothChannels_ShouldMapToSameLane()
        {
            // Channels 0x17 and 0x19 should map to LowTomRightCymbal
            var lane1 = InputLaneExtensions.FromChannel(0x17);
            var lane2 = InputLaneExtensions.FromChannel(0x19);
            Assert.Equal(lane1, lane2);
        }

        #endregion
    }
}
