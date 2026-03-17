using System.Collections.Generic;
using System.Linq;
using DTXMania.Game.Lib.Song.Components;
using Xunit;

namespace DTXMania.Test.Song
{
    /// <summary>
    /// Tests for ParsedChart methods and statistics
    /// </summary>
    public class ParsedChartTests
    {
        #region Constructor Tests

        [Fact]
        public void DefaultConstructor_ShouldInitializeWithDefaults()
        {
            var chart = new ParsedChart();
            Assert.Equal(120.0, chart.Bpm);
            Assert.Equal("", chart.BackgroundAudioPath);
            Assert.Equal("", chart.FilePath);
            Assert.Equal(0.0, chart.DurationMs);
            Assert.Empty(chart.Notes);
            Assert.Empty(chart.BGMEvents);
            Assert.Equal(0, chart.TotalNotes);
        }

        [Fact]
        public void FilePathConstructor_ShouldSetFilePath()
        {
            var chart = new ParsedChart("/test/song.dtx");
            Assert.Equal("/test/song.dtx", chart.FilePath);
        }

        [Fact]
        public void DefaultConstructor_ShouldInitialize10LaneSlots()
        {
            var chart = new ParsedChart();
            Assert.Equal(10, chart.NotesPerLane.Count);
            for (int i = 0; i < 10; i++)
            {
                Assert.Equal(0, chart.NotesPerLane[i]);
            }
        }

        #endregion

        #region AddNote Tests

        [Fact]
        public void AddNote_NullNote_ShouldNotThrow()
        {
            var chart = new ParsedChart();
            chart.AddNote(null); // Should not throw
            Assert.Equal(0, chart.TotalNotes);
        }

        [Fact]
        public void AddNote_SingleNote_ShouldIncreaseNoteCount()
        {
            var chart = new ParsedChart();
            var note = new Note(laneIndex: 3, bar: 0, tick: 0, channel: 0x14, value: "01");
            note.TimeMs = 500.0;

            chart.AddNote(note);

            Assert.Equal(1, chart.TotalNotes);
            Assert.Equal(1, chart.NotesPerLane[3]);
        }

        [Fact]
        public void AddNote_ShouldUpdateDuration()
        {
            var chart = new ParsedChart();
            var note = new Note(3, 1, 0, 0x14, "01") { TimeMs = 2000.0 };

            chart.AddNote(note);

            Assert.Equal(2000.0, chart.DurationMs);
        }

        [Fact]
        public void AddNote_MultipleNotes_ShouldTrackMaxDuration()
        {
            var chart = new ParsedChart();
            chart.AddNote(new Note(0, 0, 0, 0x1A, "01") { TimeMs = 1000.0 });
            chart.AddNote(new Note(1, 2, 0, 0x18, "01") { TimeMs = 3000.0 });
            chart.AddNote(new Note(2, 1, 0, 0x1B, "01") { TimeMs = 2000.0 });

            Assert.Equal(3000.0, chart.DurationMs);
            Assert.Equal(3, chart.TotalNotes);
        }

        [Fact]
        public void AddNote_OutOfRangeLane_ShouldNotUpdateLaneStats()
        {
            var chart = new ParsedChart();
            var note = new Note(-1, 0, 0, 0, "01") { TimeMs = 500.0 };

            chart.AddNote(note);

            // Note is added but no lane stat is updated (lane index is out of range)
            Assert.Equal(1, chart.TotalNotes);
        }

        [Fact]
        public void AddNote_WithZeroTimeMs_ShouldCalculateFromBpm()
        {
            var chart = new ParsedChart { Bpm = 120.0 };
            var note = new Note(3, 1, 0, 0x14, "01") { TimeMs = 0.0 };

            chart.AddNote(note);

            // Bar 1 at 120 BPM = 2000ms
            Assert.Equal(2000.0, note.TimeMs, precision: 3);
        }

        #endregion

        #region AddBGMEvent Tests

        [Fact]
        public void AddBGMEvent_NullEvent_ShouldNotThrow()
        {
            var chart = new ParsedChart();
            chart.AddBGMEvent(null); // Should not throw
            Assert.Empty(chart.BGMEvents);
        }

        [Fact]
        public void AddBGMEvent_ValidEvent_ShouldAddToList()
        {
            var chart = new ParsedChart();
            var bgmEvent = new BGMEvent(0, 0, "01") { TimeMs = 0.0 };

            chart.AddBGMEvent(bgmEvent);

            Assert.Single(chart.BGMEvents);
        }

        [Fact]
        public void AddBGMEvent_ShouldUpdateDurationIfLater()
        {
            var chart = new ParsedChart();
            var bgmEvent = new BGMEvent(5, 0, "01") { TimeMs = 10000.0 };

            chart.AddBGMEvent(bgmEvent);

            Assert.Equal(10000.0, chart.DurationMs);
        }

        [Fact]
        public void AddBGMEvent_WithZeroTimeMs_ShouldCalculateFromBpm()
        {
            var chart = new ParsedChart { Bpm = 120.0 };
            var bgmEvent = new BGMEvent(bar: 2, tick: 0, wavId: "01") { TimeMs = 0.0 };

            chart.AddBGMEvent(bgmEvent);

            // 2 bars at 120 BPM = 4000ms
            Assert.Equal(4000.0, bgmEvent.TimeMs, precision: 3);
        }

        #endregion

        #region FinalizeChart Tests

        [Fact]
        public void FinalizeChart_WithNotes_ShouldAddDurationBuffer()
        {
            var chart = new ParsedChart();
            chart.AddNote(new Note(3, 1, 0, 0x14, "01") { TimeMs = 2000.0 });

            chart.FinalizeChart();

            // Duration should have 500ms buffer added
            Assert.Equal(2500.0, chart.DurationMs, precision: 0);
        }

        [Fact]
        public void FinalizeChart_EmptyChart_ShouldNotChangeDuration()
        {
            var chart = new ParsedChart();
            chart.FinalizeChart();
            Assert.Equal(0.0, chart.DurationMs);
        }

        [Fact]
        public void FinalizeChart_ShouldSortNotesByTime()
        {
            var chart = new ParsedChart();
            chart.Notes.Add(new Note(0, 2, 0, 0, "01") { TimeMs = 3000.0 });
            chart.Notes.Add(new Note(0, 0, 0, 0, "01") { TimeMs = 500.0 });
            chart.Notes.Add(new Note(0, 1, 0, 0, "01") { TimeMs = 1500.0 });

            chart.FinalizeChart();

            Assert.Equal(500.0, chart.Notes[0].TimeMs);
            Assert.Equal(1500.0, chart.Notes[1].TimeMs);
            Assert.Equal(3000.0, chart.Notes[2].TimeMs);
        }

        [Fact]
        public void FinalizeChart_ShouldSortBGMEventsByTime()
        {
            var chart = new ParsedChart();
            chart.BGMEvents.Add(new BGMEvent(2, 0, "02") { TimeMs = 3000.0 });
            chart.BGMEvents.Add(new BGMEvent(0, 0, "01") { TimeMs = 500.0 });

            chart.FinalizeChart();

            Assert.Equal(500.0, chart.BGMEvents[0].TimeMs);
            Assert.Equal(3000.0, chart.BGMEvents[1].TimeMs);
        }

        #endregion

        #region GetNotesInTimeRange Tests

        [Fact]
        public void GetNotesInTimeRange_ReturnsOnlyNotesInRange()
        {
            var chart = new ParsedChart();
            chart.Notes.Add(new Note(0, 0, 0, 0, "01") { TimeMs = 100.0 });
            chart.Notes.Add(new Note(1, 0, 0, 0, "01") { TimeMs = 500.0 });
            chart.Notes.Add(new Note(2, 0, 0, 0, "01") { TimeMs = 1000.0 });
            chart.Notes.Add(new Note(3, 0, 0, 0, "01") { TimeMs = 2000.0 });

            var inRange = chart.GetNotesInTimeRange(400.0, 1000.0).ToList();

            Assert.Equal(2, inRange.Count);
            Assert.All(inRange, n => Assert.True(n.TimeMs >= 400.0 && n.TimeMs <= 1000.0));
        }

        [Fact]
        public void GetNotesInTimeRange_EmptyRange_ReturnsNoNotes()
        {
            var chart = new ParsedChart();
            chart.Notes.Add(new Note(0, 0, 0, 0, "01") { TimeMs = 5000.0 });

            var inRange = chart.GetNotesInTimeRange(0.0, 100.0).ToList();

            Assert.Empty(inRange);
        }

        #endregion

        #region GetNotesForLane Tests

        [Fact]
        public void GetNotesForLane_ReturnsOnlyNotesInThatLane()
        {
            var chart = new ParsedChart();
            chart.Notes.Add(new Note(laneIndex: 0, 0, 0, 0, "01") { TimeMs = 100.0 });
            chart.Notes.Add(new Note(laneIndex: 0, 0, 0, 0, "01") { TimeMs = 500.0 });
            chart.Notes.Add(new Note(laneIndex: 3, 0, 0, 0, "01") { TimeMs = 300.0 });

            var lane0Notes = chart.GetNotesForLane(0).ToList();
            var lane3Notes = chart.GetNotesForLane(3).ToList();

            Assert.Equal(2, lane0Notes.Count);
            Assert.Single(lane3Notes);
        }

        [Fact]
        public void GetNotesForLane_EmptyLane_ReturnsEmpty()
        {
            var chart = new ParsedChart();
            chart.Notes.Add(new Note(0, 0, 0, 0, "01") { TimeMs = 100.0 });

            var lane5Notes = chart.GetNotesForLane(5).ToList();

            Assert.Empty(lane5Notes);
        }

        #endregion

        #region ToString Tests

        [Fact]
        public void ToString_ShouldContainBpmAndNoteCount()
        {
            var chart = new ParsedChart("/test/song.dtx") { Bpm = 145.0 };
            chart.Notes.Add(new Note(0, 0, 0, 0, "01") { TimeMs = 500.0 });

            var result = chart.ToString();

            Assert.Contains("145", result);
            Assert.Contains("1", result);
        }

        [Fact]
        public void ToString_EmptyChart_ShouldNotThrow()
        {
            var chart = new ParsedChart();
            var result = chart.ToString();
            Assert.NotNull(result);
        }

        #endregion
    }
}
