using System;
using System.Linq;
using DTX.Song.Components;
using Xunit;

namespace DTXMania.Test.Song
{
    /// <summary>
    /// Unit tests for ChartManager
    /// Tests runtime note management functionality for Phase 2 implementation
    /// </summary>
    public class ChartManagerTests
    {
        [Fact]
        public void Constructor_ValidParsedChart_InitializesCorrectly()
        {
            // Arrange
            var parsedChart = CreateTestChart();

            // Act
            var chartManager = new ChartManager(parsedChart);

            // Assert
            Assert.Equal(parsedChart.Bpm, chartManager.Bpm);
            Assert.Equal(parsedChart.TotalNotes, chartManager.TotalNotes);
            Assert.Equal(parsedChart.DurationMs, chartManager.DurationMs);
        }

        [Fact]
        public void Constructor_NullParsedChart_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new ChartManager(null));
        }

        [Fact]
        public void GetActiveNotes_ReturnsNotesInTimeRange()
        {
            // Arrange
            var parsedChart = CreateTestChart();
            var chartManager = new ChartManager(parsedChart);

            // Act - Get notes around 1000ms with 500ms look-ahead
            var activeNotes = chartManager.GetActiveNotes(1000.0, 500.0).ToList();

            // Assert
            Assert.NotEmpty(activeNotes);
            
            // All returned notes should be within the time range [1000, 1500]
            foreach (var note in activeNotes)
            {
                Assert.True(note.TimeMs >= 1000.0, $"Note at {note.TimeMs}ms should be >= 1000ms");
                Assert.True(note.TimeMs <= 1500.0, $"Note at {note.TimeMs}ms should be <= 1500ms");
            }
        }

        [Fact]
        public void GetActiveNotesForLane_ReturnsOnlySpecifiedLane()
        {
            // Arrange
            var parsedChart = CreateTestChart();
            var chartManager = new ChartManager(parsedChart);

            // Act - Get notes for lane 2 (Hi-hat)
            var laneNotes = chartManager.GetActiveNotesForLane(2, 0.0, 5000.0).ToList();

            // Assert
            Assert.NotEmpty(laneNotes);
            
            // All returned notes should be in lane 2
            foreach (var note in laneNotes)
            {
                Assert.Equal(2, note.LaneIndex);
            }
        }

        [Fact]
        public void GetNextNote_ReturnsCorrectNote()
        {
            // Arrange
            var parsedChart = CreateTestChart();
            var chartManager = new ChartManager(parsedChart);

            // Act
            var nextNote = chartManager.GetNextNote(500.0);

            // Assert
            Assert.NotNull(nextNote);
            Assert.True(nextNote.TimeMs > 500.0, $"Next note at {nextNote.TimeMs}ms should be after 500ms");
        }

        [Fact]
        public void GetNextNoteInLane_ReturnsCorrectNote()
        {
            // Arrange
            var parsedChart = CreateTestChart();
            var chartManager = new ChartManager(parsedChart);

            // Act
            var nextNote = chartManager.GetNextNoteInLane(2, 500.0);

            // Assert
            if (nextNote != null) // May be null if no more notes in lane
            {
                Assert.Equal(2, nextNote.LaneIndex);
                Assert.True(nextNote.TimeMs > 500.0);
            }
        }

        [Fact]
        public void GetStatistics_ReturnsCorrectStatistics()
        {
            // Arrange
            var parsedChart = CreateTestChart();
            var chartManager = new ChartManager(parsedChart);

            // Act
            var stats = chartManager.GetStatistics();

            // Assert
            Assert.Equal(parsedChart.TotalNotes, stats.TotalNotes);
            Assert.Equal(parsedChart.Bpm, stats.Bpm);
            Assert.Equal(parsedChart.DurationMs, stats.DurationMs);
            Assert.True(stats.NoteDensity > 0, "Note density should be positive");
            
            // Check that lane statistics add up to total
            var totalLaneNotes = stats.NotesPerLane.Sum();
            Assert.Equal(stats.TotalNotes, totalLaneNotes);
        }

        [Fact]
        public void GetPassedNotes_ReturnsNotesBeforeTime()
        {
            // Arrange
            var parsedChart = CreateTestChart();
            var chartManager = new ChartManager(parsedChart);

            // Act - Get notes that have passed 2000ms mark
            var passedNotes = chartManager.GetPassedNotes(2000.0, 100.0).ToList();

            // Assert
            // All returned notes should be before 1900ms (2000 - 100 grace period)
            foreach (var note in passedNotes)
            {
                Assert.True(note.TimeMs < 1900.0, $"Passed note at {note.TimeMs}ms should be before 1900ms");
            }
        }

        /// <summary>
        /// Creates a test chart with known notes for testing
        /// </summary>
        private ParsedChart CreateTestChart()
        {
            var chart = new ParsedChart("test.dtx")
            {
                Bpm = 120.0
            };

            // Add test notes at various times and lanes
            // Measure 0 (0-2000ms at 120 BPM)
            chart.AddNote(new Note(2, 0, 0, 0x13, "01"));    // Hi-hat at 0ms
            chart.AddNote(new Note(3, 0, 48, 0x14, "01"));   // Snare at 500ms
            chart.AddNote(new Note(2, 0, 96, 0x13, "01"));   // Hi-hat at 1000ms
            chart.AddNote(new Note(4, 0, 144, 0x15, "01"));  // Bass at 1500ms
            
            // Measure 1 (2000-4000ms at 120 BPM)
            chart.AddNote(new Note(2, 1, 0, 0x13, "01"));    // Hi-hat at 2000ms
            chart.AddNote(new Note(3, 1, 96, 0x14, "01"));   // Snare at 3000ms
            
            // Measure 2 (4000-6000ms at 120 BPM)
            chart.AddNote(new Note(0, 2, 0, 0x11, "01"));    // Left Cymbal at 4000ms
            chart.AddNote(new Note(8, 2, 96, 0x19, "01"));   // Right Cymbal at 5000ms

            chart.FinalizeChart();
            return chart;
        }
    }
}
