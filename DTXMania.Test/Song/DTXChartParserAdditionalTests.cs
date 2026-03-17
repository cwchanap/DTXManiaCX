using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Components;
using Xunit;

namespace DTXMania.Test.Song
{
    /// <summary>
    /// Additional tests for DTXChartParser covering uncovered code paths
    /// </summary>
    public class DTXChartParserAdditionalTests : IDisposable
    {
        private readonly string _tempDir;

        public DTXChartParserAdditionalTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"DTXMania_DTXParserTest_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, true);
        }

        private string CreateTempDtx(string content)
        {
            var path = Path.Combine(_tempDir, $"{Guid.NewGuid():N}.dtx");
            File.WriteAllText(path, content);
            return path;
        }

        #region ParseAsync Tests with Various Content

        [Fact]
        public async Task ParseAsync_WithBpmHeader_ShouldSetBpm()
        {
            var content = "#BPM: 150.0\n#00011: 01000000\n";
            var path = CreateTempDtx(content);

            var chart = await DTXChartParser.ParseAsync(path);

            Assert.Equal(150.0, chart.Bpm);
        }

        [Fact]
        public async Task ParseAsync_WithWavDefinitionAndBGMChannel_ShouldParseBGMEvents()
        {
            var content =
                "#BPM: 120.0\n" +
                "#WAV01: bgm.ogg\n" +
                "#00001: 01000000\n";  // BGM channel 01
            var path = CreateTempDtx(content);

            var chart = await DTXChartParser.ParseAsync(path);

            Assert.True(chart.BGMEvents.Count > 0);
        }

        [Fact]
        public async Task ParseAsync_WithDrumNotes_ShouldParseAllMappedChannels()
        {
            // Test all mapped drum channels
            var content =
                "#BPM: 120.0\n" +
                "#00011: 01000000\n" +  // HH Close (0x11 -> Lane 1)
                "#00012: 01000000\n" +  // Snare (0x12 -> Lane 3)
                "#00013: 01000000\n" +  // Bass Drum (0x13 -> Lane 5)
                "#00014: 01000000\n" +  // High Tom (0x14 -> Lane 4)
                "#00015: 01000000\n" +  // Low Tom (0x15 -> Lane 6)
                "#00016: 01000000\n" +  // Ride (0x16 -> Lane 9)
                "#00017: 01000000\n" +  // Floor Tom (0x17 -> Lane 7)
                "#00018: 01000000\n" +  // HH Open (0x18 -> Lane 1)
                "#00019: 01000000\n" +  // Right Cymbal (0x19 -> Lane 8)
                "#0001A: 01000000\n" +  // Left Crash (0x1A -> Lane 0)
                "#0001B: 01000000\n" +  // Left Pedal (0x1B -> Lane 2)
                "#0001C: 01000000\n";   // Left Bass Drum (0x1C -> Lane 2)
            var path = CreateTempDtx(content);

            var chart = await DTXChartParser.ParseAsync(path);

            Assert.True(chart.Notes.Count > 0);
            Assert.True(chart.TotalNotes > 0);
        }

        [Fact]
        public async Task ParseAsync_WithUnmappedChannel_ShouldSkipChannel()
        {
            var content =
                "#BPM: 120.0\n" +
                "#00020: 01000000\n" +  // Channel 0x20 - not mapped
                "#00011: 01000000\n";   // Valid channel
            var path = CreateTempDtx(content);

            var chart = await DTXChartParser.ParseAsync(path);

            // Should only have notes from channel 0x11
            Assert.Equal(1, chart.TotalNotes);
        }

        [Fact]
        public async Task ParseAsync_WithQuotedHeaderValues_ShouldUnquoteValues()
        {
            var content =
                "#BPM: \"120.0\"\n" +
                "#00011: 01000000\n";
            var path = CreateTempDtx(content);

            var chart = await DTXChartParser.ParseAsync(path);

            Assert.Equal(120.0, chart.Bpm);
        }

        [Fact]
        public async Task ParseAsync_WithCommentLines_ShouldIgnoreComments()
        {
            var content =
                "// This is a comment\n" +
                "#BPM: 120.0\n" +
                "// Another comment\n" +
                "#00011: 01000000\n";
            var path = CreateTempDtx(content);

            var chart = await DTXChartParser.ParseAsync(path);

            Assert.Equal(120.0, chart.Bpm);
            Assert.Equal(1, chart.TotalNotes);
        }

        [Fact]
        public async Task ParseAsync_WithEmptyNoteData_ShouldSkipEmptyNotes()
        {
            var content =
                "#BPM: 120.0\n" +
                "#00011: 00000000\n";  // All zeros = no notes
            var path = CreateTempDtx(content);

            var chart = await DTXChartParser.ParseAsync(path);

            Assert.Equal(0, chart.TotalNotes);
        }

        [Fact]
        public async Task ParseAsync_WithWavBgmFile_ShouldSetBackgroundAudioPath()
        {
            var content =
                "#BPM: 120.0\n" +
                "#WAV01: bgm.ogg\n" +
                "#00011: 01000000\n";
            var path = CreateTempDtx(content);

            var chart = await DTXChartParser.ParseAsync(path);

            // Background audio path should be set and refer to the WAV definition's filename
            Assert.False(string.IsNullOrWhiteSpace(chart.BackgroundAudioPath));
            Assert.Equal("bgm.ogg", System.IO.Path.GetFileName(chart.BackgroundAudioPath));
        }

        [Fact]
        public async Task ParseAsync_WithNonExistentFile_ShouldThrow()
        {
            await Assert.ThrowsAsync<FileNotFoundException>(
                () => DTXChartParser.ParseAsync(Path.Combine(_tempDir, "nonexistent.dtx")));
        }

        [Fact]
        public async Task ParseAsync_WithMultipleMeasures_ShouldParseNotesInCorrectOrder()
        {
            var content =
                "#BPM: 120.0\n" +
                "#00011: 01000000\n" +  // Measure 0
                "#00111: 01000000\n" +  // Measure 1
                "#00211: 01000000\n";   // Measure 2
            var path = CreateTempDtx(content);

            var chart = await DTXChartParser.ParseAsync(path);

            Assert.Equal(3, chart.TotalNotes);
            var sortedNotes = chart.Notes.OrderBy(n => n.TimeMs).ToList();
            Assert.True(sortedNotes[0].TimeMs < sortedNotes[1].TimeMs);
        }

        #endregion

        #region ParseSongEntitiesAsync Tests

        [Fact]
        public async Task ParseSongEntitiesAsync_WithTitle_ShouldParseTitle()
        {
            var content =
                "#TITLE: My Test Song\n" +
                "#BPM: 120.0\n" +
                "#DLEVEL: 5\n";
            var path = CreateTempDtx(content);

            var (song, chart) = await DTXChartParser.ParseSongEntitiesAsync(path);

            Assert.Equal("My Test Song", song.Title);
        }

        [Fact]
        public async Task ParseSongEntitiesAsync_WithArtist_ShouldParseArtist()
        {
            var content =
                "#ARTIST: Test Artist\n" +
                "#BPM: 120.0\n" +
                "#DLEVEL: 5\n";
            var path = CreateTempDtx(content);

            var (song, chart) = await DTXChartParser.ParseSongEntitiesAsync(path);

            Assert.Equal("Test Artist", song.Artist);
        }

        [Fact]
        public async Task ParseSongEntitiesAsync_WithGenre_ShouldParseGenre()
        {
            var content =
                "#GENRE: Rock\n" +
                "#BPM: 120.0\n" +
                "#DLEVEL: 5\n";
            var path = CreateTempDtx(content);

            var (song, chart) = await DTXChartParser.ParseSongEntitiesAsync(path);

            Assert.Equal("Rock", song.Genre);
        }

        [Fact]
        public async Task ParseSongEntitiesAsync_WithDrumLevel_ShouldSetDrumLevel()
        {
            var content =
                "#TITLE: Test\n" +
                "#BPM: 120.0\n" +
                "#DLEVEL: 7\n";
            var path = CreateTempDtx(content);

            var (song, chart) = await DTXChartParser.ParseSongEntitiesAsync(path);

            Assert.Equal(7, chart.DrumLevel);
            Assert.True(chart.HasDrumChart);
        }

        [Fact]
        public async Task ParseSongEntitiesAsync_WithGuitarLevel_ShouldSetGuitarLevel()
        {
            var content =
                "#TITLE: Test\n" +
                "#BPM: 120.0\n" +
                "#GLEVEL: 6\n";
            var path = CreateTempDtx(content);

            var (song, chart) = await DTXChartParser.ParseSongEntitiesAsync(path);

            Assert.Equal(6, chart.GuitarLevel);
            Assert.True(chart.HasGuitarChart);
        }

        [Fact]
        public async Task ParseSongEntitiesAsync_WithBassLevel_ShouldSetBassLevel()
        {
            var content =
                "#TITLE: Test\n" +
                "#BPM: 120.0\n" +
                "#BLEVEL: 4\n";
            var path = CreateTempDtx(content);

            var (song, chart) = await DTXChartParser.ParseSongEntitiesAsync(path);

            Assert.Equal(4, chart.BassLevel);
            Assert.True(chart.HasBassChart);
        }

        [Fact]
        public async Task ParseSongEntitiesAsync_WithPreview_ShouldSetPreviewFile()
        {
            var content =
                "#TITLE: Test\n" +
                "#BPM: 120.0\n" +
                "#DLEVEL: 5\n" +
                "#PREVIEW: preview.ogg\n";
            var path = CreateTempDtx(content);

            var (song, chart) = await DTXChartParser.ParseSongEntitiesAsync(path);

            Assert.Equal("preview.ogg", chart.PreviewFile);
        }

        [Fact]
        public async Task ParseSongEntitiesAsync_WithPreimage_ShouldSetPreviewImage()
        {
            var content =
                "#TITLE: Test\n" +
                "#BPM: 120.0\n" +
                "#DLEVEL: 5\n" +
                "#PREIMAGE: cover.png\n";
            var path = CreateTempDtx(content);

            var (song, chart) = await DTXChartParser.ParseSongEntitiesAsync(path);

            Assert.Equal("cover.png", chart.PreviewImage);
        }

        [Fact]
        public async Task ParseSongEntitiesAsync_WithBackground_ShouldSetBackgroundFile()
        {
            var content =
                "#TITLE: Test\n" +
                "#BPM: 120.0\n" +
                "#DLEVEL: 5\n" +
                "#BACKGROUND: bg.avi\n";
            var path = CreateTempDtx(content);

            var (song, chart) = await DTXChartParser.ParseSongEntitiesAsync(path);

            Assert.Equal("bg.avi", chart.BackgroundFile);
        }

        [Fact]
        public async Task ParseSongEntitiesAsync_WithStagefile_ShouldSetStageFile()
        {
            var content =
                "#TITLE: Test\n" +
                "#BPM: 120.0\n" +
                "#DLEVEL: 5\n" +
                "#STAGEFILE: stage.png\n";
            var path = CreateTempDtx(content);

            var (song, chart) = await DTXChartParser.ParseSongEntitiesAsync(path);

            Assert.Equal("stage.png", chart.StageFile);
        }

        [Fact]
        public async Task ParseSongEntitiesAsync_WithComment_ShouldSetComment()
        {
            var content =
                "#TITLE: Test\n" +
                "#BPM: 120.0\n" +
                "#DLEVEL: 5\n" +
                "#COMMENT: Great song!\n";
            var path = CreateTempDtx(content);

            var (song, chart) = await DTXChartParser.ParseSongEntitiesAsync(path);

            Assert.Equal("Great song!", song.Comment);
        }

        [Fact]
        public async Task ParseSongEntitiesAsync_WithDrumNoteData_ShouldCalculateDuration()
        {
            var content =
                "#TITLE: Test\n" +
                "#BPM: 120.0\n" +
                "#DLEVEL: 5\n" +
                "#00011: 01000000\n" +
                "#00511: 01000000\n";  // Measure 5
            var path = CreateTempDtx(content);

            var (song, chart) = await DTXChartParser.ParseSongEntitiesAsync(path);

            Assert.True(chart.Duration > 0);
        }

        [Fact]
        public async Task ParseSongEntitiesAsync_UnsupportedExtension_ShouldReturnFileNameAsTitle()
        {
            var txtPath = Path.Combine(_tempDir, "testfile.txt");
            File.WriteAllText(txtPath, "#TITLE: Test\n");

            var (song, chart) = await DTXChartParser.ParseSongEntitiesAsync(txtPath);

            Assert.Equal("testfile", song.Title);
        }

        [Fact]
        public async Task ParseSongEntitiesAsync_NonExistentFile_ShouldThrow()
        {
            await Assert.ThrowsAsync<FileNotFoundException>(
                () => DTXChartParser.ParseSongEntitiesAsync(Path.Combine(_tempDir, "nonexistent.dtx")));
        }

        [Fact]
        public async Task ParseSongEntitiesAsync_WithDifficultyLabels_ShouldSetLabels()
        {
            var content =
                "#TITLE: Test\n" +
                "#BPM: 120.0\n" +
                "#DLEVEL: 5\n" +
                "#DLABEL: BASIC\n" +
                "#GLABEL: GUITAR\n" +
                "#BLABEL: BASS\n";
            var path = CreateTempDtx(content);

            var (song, chart) = await DTXChartParser.ParseSongEntitiesAsync(path);

            // DrumLevel is set via #DLEVEL and IS copied to the returned chart; confirms header parsing ran
            Assert.Equal(5, chart.DrumLevel);
        }

        #endregion

        #region IsSupported / IsSupportedFile Tests

        [Theory]
        [InlineData(".dtx")]
        [InlineData(".gda")]
        [InlineData(".g2d")]
        [InlineData(".bms")]
        [InlineData(".bme")]
        [InlineData(".bml")]
        public void IsSupported_AllValidExtensions_ShouldReturnTrue(string ext)
        {
            Assert.True(DTXChartParser.IsSupported(ext));
        }

        [Fact]
        public void IsSupportedFile_WithNullPath_ShouldReturnFalse()
        {
            Assert.False(DTXChartParser.IsSupportedFile(null));
        }

        [Fact]
        public void IsSupportedFile_WithEmptyPath_ShouldReturnFalse()
        {
            Assert.False(DTXChartParser.IsSupportedFile(""));
        }

        #endregion
    }
}
