using System;
using DTXMania.Game.Lib.Song.Components;
using Xunit;

namespace DTXMania.Test.Song
{
    /// <summary>
    /// Unit tests for the Note class
    /// Tests constructors, time calculation, lane naming, and string representation
    /// </summary>
    [Trait("Category", "Song")]
    public class NoteTests
    {
        #region Constructor Tests

        [Fact]
        public void DefaultConstructor_ShouldSetDefaultValues()
        {
            var note = new Note();

            Assert.Equal(0, note.LaneIndex);
            Assert.Equal(0, note.Bar);
            Assert.Equal(0, note.Tick);
            Assert.Equal(0.0, note.TimeMs);
            Assert.Equal(0, note.Channel);
            Assert.Equal("", note.Value);
        }

        [Fact]
        public void ParameterizedConstructor_ShouldSetAllProperties()
        {
            var note = new Note(laneIndex: 3, bar: 2, tick: 96, channel: 0x14, value: "0A");

            Assert.Equal(3, note.LaneIndex);
            Assert.Equal(2, note.Bar);
            Assert.Equal(96, note.Tick);
            Assert.Equal(0x14, note.Channel);
            Assert.Equal("0A", note.Value);
        }

        [Fact]
        public void ParameterizedConstructor_ShouldLeaveTimeMsAtZero()
        {
            var note = new Note(0, 1, 0, 0x1A, "01");
            Assert.Equal(0.0, note.TimeMs);
        }

        #endregion

        #region CalculateTimeMs Tests

        [Theory]
        [InlineData(120.0, 0, 0, 0.0)]        // Bar 0, Tick 0 → 0ms
        [InlineData(120.0, 1, 0, 2000.0)]     // Bar 1, Tick 0 at 120 BPM → 2000ms
        [InlineData(120.0, 2, 0, 4000.0)]     // Bar 2, Tick 0 at 120 BPM → 4000ms
        [InlineData(120.0, 0, 192, 2000.0)]   // Bar 0, Tick 192 (= 1 measure) at 120 BPM → 2000ms
        [InlineData(60.0, 1, 0, 4000.0)]      // Bar 1, Tick 0 at 60 BPM → 4000ms
        [InlineData(240.0, 1, 0, 1000.0)]     // Bar 1, Tick 0 at 240 BPM → 1000ms
        public void CalculateTimeMs_ValidBpm_ShouldComputeCorrectTime(double bpm, int bar, int tick, double expectedMs)
        {
            var note = new Note(0, bar, tick, 0, "01");

            note.CalculateTimeMs(bpm);

            Assert.Equal(expectedMs, note.TimeMs, precision: 3);
        }

        [Theory]
        [InlineData(0.0)]
        [InlineData(-1.0)]
        [InlineData(-120.0)]
        public void CalculateTimeMs_InvalidBpm_ShouldThrowArgumentException(double bpm)
        {
            var note = new Note(0, 1, 0, 0, "01");

            Assert.Throws<ArgumentException>(() => note.CalculateTimeMs(bpm));
        }

        [Fact]
        public void CalculateTimeMs_HalfMeasureTick_ShouldReturnHalfMeasureDuration()
        {
            // Half measure = tick 96 out of 192 at 120 BPM
            var note = new Note(0, 0, 96, 0, "01");

            note.CalculateTimeMs(120.0);

            // Half of 2000ms = 1000ms
            Assert.Equal(1000.0, note.TimeMs, precision: 3);
        }

        [Fact]
        public void CalculateTimeMs_ShouldUpdateTimeMsProperty()
        {
            var note = new Note(0, 1, 0, 0, "01");
            Assert.Equal(0.0, note.TimeMs);

            note.CalculateTimeMs(120.0);

            Assert.NotEqual(0.0, note.TimeMs);
        }

        #endregion

        #region GetLaneName Tests

        [Theory]
        [InlineData(0, "LC")]
        [InlineData(1, "HH")]
        [InlineData(2, "LP")]
        [InlineData(3, "SN")]
        [InlineData(4, "HT")]
        [InlineData(5, "DB")]
        [InlineData(6, "LT")]
        [InlineData(7, "FT")]
        [InlineData(8, "CY")]
        [InlineData(9, "RD")]
        public void GetLaneName_ValidLanes_ShouldReturnCorrectAbbreviation(int laneIndex, string expected)
        {
            var note = new Note { LaneIndex = laneIndex };
            Assert.Equal(expected, note.GetLaneName());
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(10)]
        [InlineData(99)]
        public void GetLaneName_InvalidLane_ShouldReturnQuestionMarks(int laneIndex)
        {
            var note = new Note { LaneIndex = laneIndex };
            Assert.Equal("??", note.GetLaneName());
        }

        #endregion

        #region ToString Tests

        [Fact]
        public void ToString_ShouldContainLaneName()
        {
            var note = new Note(3, 0, 0, 0x14, "01") { TimeMs = 500.0 };
            var result = note.ToString();
            Assert.Contains("SN", result);
        }

        [Fact]
        public void ToString_ShouldContainBarNumber()
        {
            var note = new Note(0, 5, 0, 0x1A, "01") { TimeMs = 0.0 };
            var result = note.ToString();
            Assert.Contains("5", result);
        }

        [Fact]
        public void ToString_ShouldContainValue()
        {
            var note = new Note(0, 0, 0, 0, "FF") { TimeMs = 0.0 };
            var result = note.ToString();
            Assert.Contains("FF", result);
        }

        [Fact]
        public void ToString_ShouldNotThrowForDefaultNote()
        {
            var note = new Note();
            var result = note.ToString();
            Assert.NotNull(result);
            Assert.NotEmpty(result);
        }

        #endregion

        #region Id Property Tests

        [Fact]
        public void Id_DefaultValue_ShouldBeZero()
        {
            var note = new Note();
            Assert.Equal(0, note.Id);
        }

        #endregion
    }
}
