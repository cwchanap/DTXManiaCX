using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Song.Entities;
using Xunit;

namespace DTXMania.Test.Song
{
    /// <summary>
    /// Unit tests for DTXChartParser
    /// Tests chart parsing functionality for Phase 2 implementation
    /// </summary>
    public class DTXChartParserTests
    {
        private readonly string _testDataPath;
        private readonly string _sampleDtxPath;

        public DTXChartParserTests()
        {
            // Get the test data directory
            _testDataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TestData");
            _sampleDtxPath = Path.Combine(_testDataPath, "sample.dtx");
        }

        [Fact]
        public async Task ParseAsync_ValidDtxFile_ReturnsCorrectChart()
        {
            // Arrange
            if (!File.Exists(_sampleDtxPath))
            {
                // Skip test if sample file doesn't exist
                return;
            }

            // Act
            var chart = await DTXChartParser.ParseAsync(_sampleDtxPath);

            // Assert
            Assert.NotNull(chart);
            Assert.Equal(_sampleDtxPath, chart.FilePath);
            Assert.Equal(120.0, chart.Bpm);
            Assert.True(chart.TotalNotes > 0, "Chart should contain notes");
        }

        [Fact]
        public async Task ParseAsync_ValidDtxFile_ParsesNotesCorrectly()
        {
            // Arrange
            if (!File.Exists(_sampleDtxPath))
            {
                return;
            }

            // Act
            var chart = await DTXChartParser.ParseAsync(_sampleDtxPath);

            // Assert
            Assert.NotNull(chart);
            
            // Check that we have notes in the expected lanes
            var notesByLane = chart.Notes.GroupBy(n => n.LaneIndex).ToDictionary(g => g.Key, g => g.Count());
            
            // Based on our sample.dtx, we should have notes in lanes 0, 1, 2, 3, 5, 6, 7, 8
            // (corresponding to channels 11, 12, 13, 14, 16, 17, 18, 19)
            Assert.True(notesByLane.ContainsKey(2), "Should have Hi-hat notes (lane 2, channel 13)");
            Assert.True(notesByLane.ContainsKey(3), "Should have Snare notes (lane 3, channel 14)");
            Assert.True(notesByLane.ContainsKey(0), "Should have Left Cymbal notes (lane 0, channel 11)");
            
            // Verify note timing calculation
            var firstNote = chart.Notes.OrderBy(n => n.TimeMs).First();
            Assert.True(firstNote.TimeMs >= 0, "First note should have non-negative timing");
            
            var lastNote = chart.Notes.OrderBy(n => n.TimeMs).Last();
            Assert.True(lastNote.TimeMs > firstNote.TimeMs, "Last note should be after first note");
        }

        [Fact]
        public async Task ParseAsync_ValidDtxFile_CalculatesTimingCorrectly()
        {
            // Arrange
            if (!File.Exists(_sampleDtxPath))
            {
                return;
            }

            // Act
            var chart = await DTXChartParser.ParseAsync(_sampleDtxPath);

            // Assert
            Assert.NotNull(chart);
            
            // Find a note in measure 0 at the beginning
            var firstMeasureNotes = chart.Notes.Where(n => n.Bar == 0).OrderBy(n => n.Tick).ToList();
            Assert.True(firstMeasureNotes.Count > 0, "Should have notes in first measure");
            
            var firstNote = firstMeasureNotes.First();
            
            // At 120 BPM, one measure = 2000ms, so first note should be at 0ms
            Assert.True(Math.Abs(firstNote.TimeMs) < 1.0, $"First note timing should be near 0ms, got {firstNote.TimeMs}ms");
            
            // Find a note in measure 1
            var secondMeasureNotes = chart.Notes.Where(n => n.Bar == 1).OrderBy(n => n.Tick).ToList();
            if (secondMeasureNotes.Count > 0)
            {
                var secondMeasureFirstNote = secondMeasureNotes.First();
                // Should be around 2000ms (one measure at 120 BPM)
                Assert.True(Math.Abs(secondMeasureFirstNote.TimeMs - 2000.0) < 100.0, 
                    $"Second measure note should be around 2000ms, got {secondMeasureFirstNote.TimeMs}ms");
            }
        }

        [Fact]
        public async Task ParseAsync_ValidDtxFile_SetsCorrectChannelMapping()
        {
            // Arrange
            if (!File.Exists(_sampleDtxPath))
            {
                return;
            }

            // Act
            var chart = await DTXChartParser.ParseAsync(_sampleDtxPath);

            // Assert
            Assert.NotNull(chart);
            
            // Check channel to lane mapping
            foreach (var note in chart.Notes)
            {
                // Verify that channel and lane index match expected mapping
                switch (note.Channel)
                {
                    case 0x11: // LC
                        Assert.Equal(0, note.LaneIndex);
                        break;
                    case 0x12: // LP
                        Assert.Equal(1, note.LaneIndex);
                        break;
                    case 0x13: // HH
                        Assert.Equal(2, note.LaneIndex);
                        break;
                    case 0x14: // SD
                        Assert.Equal(3, note.LaneIndex);
                        break;
                    case 0x15: // BD
                        Assert.Equal(4, note.LaneIndex);
                        break;
                    case 0x16: // HT
                        Assert.Equal(5, note.LaneIndex);
                        break;
                    case 0x17: // LT
                        Assert.Equal(6, note.LaneIndex);
                        break;
                    case 0x18: // FT
                        Assert.Equal(7, note.LaneIndex);
                        break;
                    case 0x19: // CY
                        Assert.Equal(8, note.LaneIndex);
                        break;
                    default:
                        Assert.Fail($"Unexpected channel: {note.Channel:X2}");
                        break;
                }
            }
        }

        [Fact]
        public async Task ParseAsync_NonExistentFile_ThrowsFileNotFoundException()
        {
            // Arrange
            var nonExistentPath = Path.Combine(_testDataPath, "nonexistent.dtx");

            // Act & Assert
            await Assert.ThrowsAsync<FileNotFoundException>(() => DTXChartParser.ParseAsync(nonExistentPath));
        }

        [Fact]
        public async Task ParseAsync_EmptyPath_ThrowsFileNotFoundException()
        {
            // Act & Assert
            await Assert.ThrowsAsync<FileNotFoundException>(() => DTXChartParser.ParseAsync(""));
            await Assert.ThrowsAsync<FileNotFoundException>(() => DTXChartParser.ParseAsync(null));
        }

        [Fact]
        public void Note_CalculateTimeMs_CalculatesCorrectly()
        {
            // Arrange
            var note = new Note(0, 1, 96, 0x13, "01"); // Bar 1, tick 96 (half measure), channel 13

            // Act
            note.CalculateTimeMs(120.0); // 120 BPM

            // Assert
            // At 120 BPM: 1 measure = 2000ms, half measure = 1000ms
            // Bar 1 + half measure = 2000ms + 1000ms = 3000ms
            // Formula: (((bar*192)+tick)/192) * (60000/BPM) * 4
            // So: (((1*192)+96)/192) * (60000/120) * 4 = (288/192) * 500 * 4 = 1.5 * 500 * 4 = 3000ms
            var expectedTime = 3000.0;
            Assert.True(Math.Abs(note.TimeMs - expectedTime) < 1.0,
                $"Expected {expectedTime}ms, got {note.TimeMs}ms");
        }

        [Fact]
        public void Note_GetLaneName_ReturnsCorrectNames()
        {
            // Test all 10 lane names (matching gameplay order: LC, HH, LP, SN, HT, DB, LT, FT, CY, RD)
            // Channel values match canonical DTXChartParser.ChannelToLaneMap mapping
            Assert.Equal("LC", new Note(0, 0, 0, 0x1A, "01").GetLaneName());  // Left Crash (0x1A → Lane 0)
            Assert.Equal("HH", new Note(1, 0, 0, 0x11, "01").GetLaneName());  // Hi-Hat Close (0x11 → Lane 1)
            Assert.Equal("LP", new Note(2, 0, 0, 0x1B, "01").GetLaneName());  // Left Pedal (0x1B → Lane 2)
            Assert.Equal("SN", new Note(3, 0, 0, 0x12, "01").GetLaneName());  // Snare (0x12 → Lane 3)
            Assert.Equal("HT", new Note(4, 0, 0, 0x14, "01").GetLaneName());  // High Tom (0x14 → Lane 4)
            Assert.Equal("DB", new Note(5, 0, 0, 0x13, "01").GetLaneName());  // Bass Drum (0x13 → Lane 5)
            Assert.Equal("LT", new Note(6, 0, 0, 0x15, "01").GetLaneName());  // Low Tom (0x15 → Lane 6)
            Assert.Equal("FT", new Note(7, 0, 0, 0x17, "01").GetLaneName());  // Floor Tom (0x17 → Lane 7)
            Assert.Equal("CY", new Note(8, 0, 0, 0x19, "01").GetLaneName());  // Right Cymbal (0x19 → Lane 8)
            Assert.Equal("RD", new Note(9, 0, 0, 0x16, "01").GetLaneName());  // Ride (0x16 → Lane 9)
            Assert.Equal("??", new Note(10, 0, 0, 0xFF, "01").GetLaneName()); // Invalid lane (unmapped channel)
        }

        [Fact]
        public async Task ParseSongEntitiesAsync_ValidDtxFile_ReturnsCorrectEntities()
        {
            // Arrange
            if (!File.Exists(_sampleDtxPath))
            {
                // Skip test if sample file doesn't exist
                return;
            }

            // Act
            var (song, chart) = await DTXChartParser.ParseSongEntitiesAsync(_sampleDtxPath);

            // Assert
            Assert.NotNull(song);
            Assert.NotNull(chart);
            Assert.Equal(_sampleDtxPath, chart.FilePath);
            Assert.Equal(120.0, chart.Bpm);
            Assert.True(!string.IsNullOrEmpty(song.Title), "Song should have a title");
            Assert.True(chart.DrumNoteCount > 0, "Chart should have drum notes");
        }

        [Fact]
        public void IsSupported_ValidExtensions_ReturnsTrue()
        {
            // Test supported extensions
            Assert.True(DTXChartParser.IsSupported(".dtx"));
            Assert.True(DTXChartParser.IsSupported(".DTX"));
            Assert.True(DTXChartParser.IsSupported(".gda"));
            Assert.True(DTXChartParser.IsSupported(".bms"));
        }

        [Fact]
        public void IsSupported_InvalidExtensions_ReturnsFalse()
        {
            // Test unsupported extensions
            Assert.False(DTXChartParser.IsSupported(".txt"));
            Assert.False(DTXChartParser.IsSupported(".mp3"));
            Assert.False(DTXChartParser.IsSupported(""));
        }

        [Fact]
        public void IsSupportedFile_ValidFiles_ReturnsTrue()
        {
            // Test supported file paths
            Assert.True(DTXChartParser.IsSupportedFile("test.dtx"));
            Assert.True(DTXChartParser.IsSupportedFile("/path/to/song.DTX"));
            Assert.True(DTXChartParser.IsSupportedFile("C:\\music\\song.bms"));
        }

        [Fact]
        public void IsSupportedFile_InvalidFiles_ReturnsFalse()
        {
            // Test unsupported file paths
            Assert.False(DTXChartParser.IsSupportedFile("test.txt"));
            Assert.False(DTXChartParser.IsSupportedFile(""));
            Assert.False(DTXChartParser.IsSupportedFile(null));
        }
    }
}
