using DTXMania.Game.Lib.Song.Components;
using Xunit;

namespace DTXMania.Test.Song
{
    /// <summary>
    /// Unit tests for the Note class
    /// Tests constructors, authored positions, lane naming, and string representation
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

        #region Authored Position Tests

        [Fact]
        public void AuthoredPosition_ShouldResolveWhenContainingChartIsFinalized()
        {
            var chart = new ParsedChart { Bpm = 120.0 };
            var note = new Note(0, 1, 96, 0, "01");
            Assert.Equal(0.0, note.TimeMs);

            chart.AddNote(note);
            chart.FinalizeChart();

            Assert.Equal(3000.0, note.TimeMs, precision: 3);
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
