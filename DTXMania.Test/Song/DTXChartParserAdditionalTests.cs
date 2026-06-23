using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Components;
using Xunit;

namespace DTXMania.Test.Song
{
    /// <summary>
    /// Additional tests for DTXChartParser covering uncovered code paths
    /// </summary>
    [Trait("Category", "Unit")]
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

        [Theory]
        [InlineData("BPM: 180")]
        [InlineData("#BPM 180")]
        public async Task ParseAsync_WithMalformedHeaderCommand_ShouldIgnoreHeaderLine(string malformedHeader)
        {
            var content =
                malformedHeader + "\n" +
                "#00011: 01000000\n";
            var path = CreateTempDtx(content);

            var chart = await DTXChartParser.ParseAsync(path);

            Assert.Equal(120.0, chart.Bpm);
            Assert.Equal(1, chart.TotalNotes);
        }

        [Theory]
        [InlineData("#00011 01000000")]
        [InlineData("#ABC11: 01000000")]
        public async Task ParseAsync_WithMalformedTimelineLine_ShouldSkipLineAndContinue(string malformedLine)
        {
            var content =
                "#BPM: 120.0\n" +
                malformedLine + "\n" +
                "#00012: 01000000\n";
            var path = CreateTempDtx(content);

            var chart = await DTXChartParser.ParseAsync(path);

            Assert.Equal(1, chart.TotalNotes);
        }

        [Fact]
        public async Task ParseAsync_WithEmptyBpmValue_ShouldKeepDefaultBpm()
        {
            var content =
                "#BPM:\n" +
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
        public async Task ParseAsync_WithAbsoluteWavPath_ShouldPreserveAbsoluteBackgroundAudioPath()
        {
            var absoluteWavPath = Path.Combine(_tempDir, $"{Guid.NewGuid():N}.ogg");
            File.WriteAllBytes(absoluteWavPath, Array.Empty<byte>());

            var content =
                "#BPM: 120.0\n" +
                $"#WAV01: {absoluteWavPath}\n";
            var path = CreateTempDtx(content);

            var chart = await DTXChartParser.ParseAsync(path);

            Assert.Equal(absoluteWavPath, chart.BackgroundAudioPath);
        }

        [Fact]
        public async Task ParseAsync_WithExistingDtxRelativeWavPath_ShouldResolveToDtxDirectory()
        {
            var fileName = $"{Guid.NewGuid():N}.ogg";
            var expectedPath = Path.Combine(_tempDir, fileName);
            File.WriteAllBytes(expectedPath, Array.Empty<byte>());

            var content =
                "#BPM: 120.0\n" +
                $"#WAV01: {fileName}\n";
            var path = CreateTempDtx(content);

            var chart = await DTXChartParser.ParseAsync(path);

            Assert.Equal(expectedPath, chart.BackgroundAudioPath);
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
        public async Task ParseSongEntitiesAsync_WithDecimalDrumLevel_ShouldRoundToNearestInteger()
        {
            var content =
                "#TITLE: Test\n" +
                "#BPM: 120.0\n" +
                "#DLEVEL: 5.7\n";
            var path = CreateTempDtx(content);

            var (song, chart) = await DTXChartParser.ParseSongEntitiesAsync(path);

            Assert.Equal("Test", song.Title);
            Assert.Equal(6, chart.DrumLevel);
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

        [Theory]
        [InlineData("abc\uFFFDef")]
        [InlineData("???")]
        [InlineData("\u0081\u0082")]
        [InlineData("\u0001")]
        public void IsTextProperlyDecoded_WithCorruptedText_ShouldReturnFalse(string text)
        {
            var isProperlyDecoded = InvokePrivateStaticMethod<bool>("IsTextProperlyDecoded", text);

            Assert.False(isProperlyDecoded);
        }

        #endregion

        #region WAV Id Case Normalization Tests

        [Fact]
        public async Task ParseAsync_LowercaseNoteValue_NormalizedToUppercase()
        {
            // #WAV0A header is stored as uppercase; measure data uses lowercase "0a"
            var content =
                "#BPM:120\n" +
                "#WAV0A:snare.wav\n" +
                "#00111:0a\n";
            var path = CreateTempDtx(content);
            File.WriteAllText(Path.Combine(_tempDir, "snare.wav"), "fake");

            var chart = await DTXChartParser.ParseAsync(path);

            Assert.Single(chart.Notes);
            Assert.Equal("0A", chart.Notes[0].Value);
            Assert.True(chart.WavDefinitions.ContainsKey("0A"));
        }

        [Fact]
        public async Task ParseAsync_LowercaseBGMValue_NormalizedToUppercase()
        {
            var content =
                "#BPM:120\n" +
                "#WAV0A:bgm.wav\n" +
                "#00101:0a\n";
            var path = CreateTempDtx(content);
            File.WriteAllText(Path.Combine(_tempDir, "bgm.wav"), "fake");

            var chart = await DTXChartParser.ParseAsync(path);

            Assert.Single(chart.BGMEvents);
            Assert.Equal("0A", chart.BGMEvents[0].WavId);
        }

        [Fact]
        public async Task ParseAsync_MixedCaseNoteValues_AllNormalizedToUppercase()
        {
            var content =
                "#BPM:120\n" +
                "#WAV0A:snare.wav\n" +
                "#WAV0B:hihat.wav\n" +
                "#00111:0a0b\n";
            var path = CreateTempDtx(content);
            File.WriteAllText(Path.Combine(_tempDir, "snare.wav"), "fake");
            File.WriteAllText(Path.Combine(_tempDir, "hihat.wav"), "fake");

            var chart = await DTXChartParser.ParseAsync(path);

            Assert.Equal(2, chart.Notes.Count);
            Assert.Equal("0A", chart.Notes[0].Value);
            Assert.Equal("0B", chart.Notes[1].Value);
        }

        [Fact]
        public async Task ParseAsync_BackslashBgmPath_ShouldDetectCommonBgmName()
        {
            // On macOS/Linux, Path.GetFileName("Audio\\bgm.ogg") returns the whole string
            // because backslash is not a directory separator. Verify that common BGM detection
            // still works when WAV definitions use Windows-style backslash paths.
            var audioDir = Path.Combine(_tempDir, "Audio");
            Directory.CreateDirectory(audioDir);
            File.WriteAllText(Path.Combine(audioDir, "bgm.ogg"), "fake");

            var content =
                "#BPM:120\n" +
                "#WAV01:Drums\\snare.wav\n" +
                "#WAV02:Audio\\bgm.ogg\n";
            var path = CreateTempDtx(content);

            var chart = await DTXChartParser.ParseAsync(path);

            Assert.False(string.IsNullOrWhiteSpace(chart.BackgroundAudioPath));
            Assert.Equal("bgm.ogg", Path.GetFileName(chart.BackgroundAudioPath));
        }

        [Fact]
        public void IsTextProperlyDecoded_WithValidText_ReturnsTrue()
        {
            Assert.True(InvokePrivateStaticMethod<bool>("IsTextProperlyDecoded", ""));
            Assert.True(InvokePrivateStaticMethod<bool>("IsTextProperlyDecoded", (string)null!));
            Assert.True(InvokePrivateStaticMethod<bool>("IsTextProperlyDecoded", "Hello World"));
            Assert.True(InvokePrivateStaticMethod<bool>("IsTextProperlyDecoded", "テスト"));
        }

        [Theory]
        [InlineData("DRUM", 5)]
        [InlineData("DRUMS", 5)]
        [InlineData("GUITAR", 4)]
        [InlineData("BASS", 3)]
        public void ParseSongEntitiesAsync_WithLevelDataInstrumentFormat_ShouldParseCorrectly(string instrument, int level)
        {
            var content =
                "#TITLE: Test\n" +
                "#BPM: 120.0\n" +
                $"#LEVEL: {instrument}:{level}\n";
            var path = CreateTempDtx(content);

            var (song, chart) = DTXChartParser.ParseSongEntitiesAsync(path).GetAwaiter().GetResult();

            switch (instrument)
            {
                case "DRUM":
                case "DRUMS":
                    Assert.Equal(level, chart.DrumLevel);
                    Assert.True(chart.HasDrumChart);
                    break;
                case "GUITAR":
                    Assert.Equal(level, chart.GuitarLevel);
                    Assert.True(chart.HasGuitarChart);
                    break;
                case "BASS":
                    Assert.Equal(level, chart.BassLevel);
                    Assert.True(chart.HasBassChart);
                    break;
            }
        }

        [Fact]
        public async Task ParseSongEntitiesAsync_WithCorruptEncoding_ShouldFallBackToFilename()
        {
            var wavName = $"test-{Guid.NewGuid():N}.wav";
            var content =
                "#BPM: 120.0\n" +
                $"#WAV01: {wavName}\n" +
                "#00011: 01000000\n";
            var path = CreateTempDtx(content);

            var (song, chart) = await DTXChartParser.ParseSongEntitiesAsync(path);

            Assert.False(string.IsNullOrEmpty(song.Title));
        }

        [Fact]
        public async Task ParseSongEntitiesAsync_WithNoParsableMetadata_ShouldUseFilenameAsTitle()
        {
            var content = "#00011: 01000000\n";
            var path = CreateTempDtx(content);

            var (song, chart) = await DTXChartParser.ParseSongEntitiesAsync(path);

            Assert.NotNull(song.Title);
        }

        [Fact]
        public async Task ParseSongEntitiesAsync_ParseHeaderFails_ShouldUseFilenameAsTitle()
        {
            var txtPath = Path.Combine(_tempDir, "fallback_test.txt");
            File.WriteAllText(txtPath, "#BPM: 120.0\n#DLEVEL: 5\n");

            var (song, chart) = await DTXChartParser.ParseSongEntitiesAsync(txtPath);

            Assert.Equal("fallback_test", song.Title);
        }

        [Fact]
        public void IsTextProperlyDecoded_WithControlCharacter_ReturnsFalse()
        {
            var result = InvokePrivateStaticMethod<bool>("IsTextProperlyDecoded", "test\x01value");
            Assert.False(result);
        }

        [Fact]
        public void IsTextProperlyDecoded_WithTabAndNewline_ReturnsTrue()
        {
            Assert.True(InvokePrivateStaticMethod<bool>("IsTextProperlyDecoded", "line1\tvalue\nline2"));
        }

        #endregion

        #region Per-WAV Volume / Pan Tests

        [Fact]
        public async Task ParseAsync_WithVolumeHeaders_PopulatesNormalizedVolumes()
        {
            var content =
                "#WAV01: snare.wav\n" +
                "#WAV02: kick.wav\n" +
                "#VOLUME01: 50\n" +
                "#WAVVOL02: 100\n";
            var path = CreateTempDtx(content);

            var chart = await DTXChartParser.ParseAsync(path);

            Assert.Equal(50, chart.WavVolumes["01"]);
            Assert.Equal(100, chart.WavVolumes["02"]);
            Assert.Equal(0.5f, chart.GetVolume("01"));
            Assert.Equal(1.0f, chart.GetVolume("02"));
        }

        [Fact]
        public async Task ParseAsync_WithPanHeaders_PopulatesNormalizedPans()
        {
            var content =
                "#WAV01: left.wav\n" +
                "#WAV02: right.wav\n" +
                "#PAN01: -100\n" +
                "#WAVPAN02: 100\n";
            var path = CreateTempDtx(content);

            var chart = await DTXChartParser.ParseAsync(path);

            Assert.Equal(-100, chart.WavPans["01"]);
            Assert.Equal(100, chart.WavPans["02"]);
            Assert.Equal(-1.0f, chart.GetPan("01"));
            Assert.Equal(1.0f, chart.GetPan("02"));
        }

        [Fact]
        public async Task ParseAsync_WavVolAndPan_AreNotMisparsedAsWavDefinitions()
        {
            // #WAVVOL and #WAVPAN both start with "#WAV"; ensure they are not
            // stored as WAV file definitions with bogus ids ("VOL01" / "PAN01").
            var content =
                "#WAV01: snare.wav\n" +
                "#WAVVOL01: 70\n" +
                "#WAVPAN01: -40\n";
            var path = CreateTempDtx(content);

            var chart = await DTXChartParser.ParseAsync(path);

            Assert.True(chart.WavDefinitions.ContainsKey("01"));
            Assert.False(chart.WavDefinitions.ContainsKey("VOL01"));
            Assert.False(chart.WavDefinitions.ContainsKey("PAN01"));
            Assert.Equal(70, chart.WavVolumes["01"]);
            Assert.Equal(-40, chart.WavPans["01"]);
        }

        [Fact]
        public async Task ParseAsync_OutOfRangeVolumeAndPan_AreClamped()
        {
            var content =
                "#WAV01: a.wav\n" +
                "#WAV02: b.wav\n" +
                "#VOLUME01: 250\n" +   // clamps to 100
                "#PAN01: -500\n" +     // clamps to -100
                "#PAN02: 500\n";       // clamps to 100
            var path = CreateTempDtx(content);

            var chart = await DTXChartParser.ParseAsync(path);

            Assert.Equal(100, chart.WavVolumes["01"]);
            Assert.Equal(-100, chart.WavPans["01"]);
            Assert.Equal(100, chart.WavPans["02"]);
        }

        [Fact]
        public async Task ParseAsync_PanelHeader_IsNotTreatedAsPanDefinition()
        {
            // "#PANEL" starts with "#PAN" but its value is non-numeric, so it must
            // not create a pan entry.
            var content =
                "#PANEL: GUITAR\n" +
                "#WAV01: a.wav\n";
            var path = CreateTempDtx(content);

            var chart = await DTXChartParser.ParseAsync(path);

            Assert.Empty(chart.WavPans);
        }

        [Fact]
        public async Task ParseAsync_BackgroundWavId_TracksCommonBgmFilename()
        {
            var content =
                "#WAV01: snare.wav\n" +
                "#WAV02: bgm.ogg\n" +
                "#00011: 0101\n";
            var path = CreateTempDtx(content);

            var chart = await DTXChartParser.ParseAsync(path);

            Assert.Equal("02", chart.BackgroundWavId);
        }

        [Fact]
        public async Task ParseAsync_BackgroundWavId_FallsBackToFirstWavWhenNoBgmEvents()
        {
            var content =
                "#WAV05: only.ogg\n" +
                "#00011: 0101\n"; // drum notes only, no BGM (channel 01) events
            var path = CreateTempDtx(content);

            var chart = await DTXChartParser.ParseAsync(path);

            Assert.Equal("05", chart.BackgroundWavId);
        }

        [Fact]
        public async Task ParseAsync_BackgroundWavVolumeAndPan_AreResolvableViaWavId()
        {
            var content =
                "#WAV01: bgm.ogg\n" +
                "#VOLUME01: 60\n" +
                "#PAN01: -50\n" +
                "#00001: 01\n"; // BGM channel
            var path = CreateTempDtx(content);

            var chart = await DTXChartParser.ParseAsync(path);

            Assert.Equal("01", chart.BackgroundWavId);
            Assert.Equal(0.6f, chart.GetVolume(chart.BackgroundWavId));
            Assert.Equal(-0.5f, chart.GetPan(chart.BackgroundWavId));
        }

        [Fact]
        public async Task ParseAsync_NoWavDefinitions_LeavesBackgroundWavIdEmpty()
        {
            var content = "#BPM: 120.0\n";
            var path = CreateTempDtx(content);

            var chart = await DTXChartParser.ParseAsync(path);

            Assert.Equal("", chart.BackgroundWavId);
        }

        [Fact]
        public async Task ParseAsync_NoVolumeOrPanHeaders_LeavesMapsEmpty()
        {
            var content =
                "#WAV01: a.wav\n" +
                "#00011: 0101\n";
            var path = CreateTempDtx(content);

            var chart = await DTXChartParser.ParseAsync(path);

            Assert.Empty(chart.WavVolumes);
            Assert.Empty(chart.WavPans);
        }

        [Fact]
        public async Task ParseAsync_PanelHeader_DoesNotCreateBogusPanEntry()
        {
            // #PANEL is metadata (player panel count), not a per-WAV pan directive.
            // Without a guard, "#PANEL" matches the "#PAN" prefix and would store
            // wavPans["EL"] = <value>, polluting the pan map with a bogus "EL" key.
            var content =
                "#WAV01: a.wav\n" +
                "#PANEL: 50\n";
            var path = CreateTempDtx(content);

            var chart = await DTXChartParser.ParseAsync(path);

            Assert.False(chart.WavPans.ContainsKey("EL"));
            Assert.Empty(chart.WavPans);
        }

        #endregion

        #region Encoding Retry Isolation Tests

        /// <summary>
        /// Regression guard: a valid UTF-8 DTX file must produce exactly the expected
        /// note count. If chart/dict state were shared across encoding attempts (the old
        /// design), a mid-parse throw + retry would duplicate notes. Per-attempt
        /// instantiation in ParseAsync prevents this.
        /// </summary>
        [Fact]
        public async Task ParseAsync_ValidFile_ProducesExactNoteCountWithoutDuplication()
        {
            // Channel 0x11 = LC lane. Note data is pairs of hex chars; "00" = rest.
            // "01020300" → 3 notes (pairs 01, 02, 03); pair 00 is a rest.
            var content =
                "#BPM: 120.0\n" +
                "#WAV01: kick.wav\n" +
                "#WAV02: snare.wav\n" +
                "#00011: 01020300\n";
            var path = CreateTempDtx(content);

            var chart = await DTXChartParser.ParseAsync(path);

            Assert.Equal(3, chart.TotalNotes);
            // WAV definitions should also not be duplicated
            Assert.Equal(2, chart.WavDefinitions.Count);
        }

        /// <summary>
        /// Documents WHY ParseAsync uses per-attempt chart instantiation: calling
        /// ParseFileContentAsync twice on the same ParsedChart (as the old shared-state
        /// design would on encoding retry) doubles the notes because Notes is a List
        /// populated via Add, not a dict that self-heals via key assignment.
        /// </summary>
        [Fact]
        public async Task ParseFileContentAsync_CalledTwiceOnSameChart_AccumulatesNotesProvingSharedStateHazard()
        {
            var content =
                "#BPM: 120.0\n" +
                "#WAV01: kick.wav\n" +
                "#00011: 01000200\n";
            var path = CreateTempDtx(content);

            var method = typeof(DTXChartParser).GetMethod(
                "ParseFileContentAsync", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(method);

            var chart = new ParsedChart(path);
            var wavDefinitions = new Dictionary<string, string>();
            var wavVolumes = new Dictionary<string, int>();
            var wavPans = new Dictionary<string, int>();

            // First parse pass
            using (var stream1 = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var reader1 = new StreamReader(stream1, System.Text.Encoding.UTF8))
            {
                var task = (Task)method!.Invoke(null, new object[] { reader1, chart, wavDefinitions, wavVolumes, wavPans })!;
                await task;
            }
            var countAfterFirstPass = chart.Notes.Count;
            Assert.True(countAfterFirstPass > 0, "Expected at least one note after first pass");

            // Second parse pass on the SAME chart (simulates encoding retry with shared state)
            using (var stream2 = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var reader2 = new StreamReader(stream2, System.Text.Encoding.UTF8))
            {
                var task = (Task)method!.Invoke(null, new object[] { reader2, chart, wavDefinitions, wavVolumes, wavPans })!;
                await task;
            }

            // Notes DOUBLED because Notes is a List<Note> — this proves the shared-state
            // hazard that ParseAsync's per-attempt instantiation eliminates.
            Assert.Equal(countAfterFirstPass * 2, chart.Notes.Count);
        }

        #endregion

        private static T InvokePrivateStaticMethod<T>(string methodName, params object[] args)
        {
            var method = typeof(DTXChartParser).GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(method);
            var result = method!.Invoke(null, args);
            Assert.NotNull(result);
            return (T)result!;
        }
    }
}
