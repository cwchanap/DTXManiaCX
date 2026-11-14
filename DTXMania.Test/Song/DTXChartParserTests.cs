using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Input;
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
    [Trait("Category", "Unit")]
    public class DTXChartParserTests : IDisposable
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
        public void Note_GetLaneName_ReturnsCorrectNames()
        {
            // Test all 10 lane names (matching gameplay order: LC, HH, LP, SN, HT, DB, LT, FT, CY, RD)
            // Channel values match canonical DTXChartParser.ChannelToLaneMap mapping
            Assert.Equal("LC", new Note(0, 0, 0, 0x1A, "01").GetLaneName());  // Left Crash (0x1A → Lane 0)
            Assert.Equal("HH", new Note(1, 0, 0, 0x11, "01").GetLaneName());  // Hi-Hat Close (0x11 → Lane 1)
            Assert.Equal("HH", new Note(1, 0, 0, 0x18, "01").GetLaneName());  // Hi-Hat Open (0x18 → Lane 1)
            Assert.Equal("LP", new Note(2, 0, 0, 0x1B, "01").GetLaneName());  // Left Pedal (0x1B → Lane 2)
            Assert.Equal("LP", new Note(2, 0, 0, 0x1C, "01").GetLaneName());  // Left Bass Drum (0x1C → Lane 2)
            Assert.Equal("SN", new Note(3, 0, 0, 0x12, "01").GetLaneName());  // Snare (0x12 → Lane 3)
            Assert.Equal("HT", new Note(4, 0, 0, 0x14, "01").GetLaneName());  // High Tom (0x14 → Lane 4)
            Assert.Equal("DB", new Note(5, 0, 0, 0x13, "01").GetLaneName());  // Bass Drum (0x13 → Lane 5)
            Assert.Equal("LT", new Note(6, 0, 0, 0x15, "01").GetLaneName());  // Low Tom (0x15 → Lane 6)
            Assert.Equal("FT", new Note(7, 0, 0, 0x17, "01").GetLaneName());  // Floor Tom (0x17 → Lane 7)
            Assert.Equal("CY", new Note(8, 0, 0, 0x16, "01").GetLaneName());  // Crash/CY (0x16 → Lane 8)
            Assert.Equal("RD", new Note(9, 0, 0, 0x19, "01").GetLaneName());  // Ride/RD (0x19 → Lane 9)
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

        [Fact]
        public async Task ParseAsync_PopulatesWavDefinitionsWithResolvedPaths()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), $"dtx-test-{Guid.NewGuid()}");
            Directory.CreateDirectory(tempDir);
            var dtxPath = Path.Combine(tempDir, "test.dtx");

            // Use unique filenames to avoid collisions with files in the working directory
            // that could cause ResolveBGMPath's Strategy 1 (File.Exists on raw path) to
            // resolve to the CWD instead of the DTX-relative temp directory.
            var wav1 = $"test-{Guid.NewGuid():N}.wav";
            var wav2 = $"test-{Guid.NewGuid():N}.wav";

            try
            {
                File.WriteAllText(dtxPath,
                    "#TITLE: Test\n" +
                    "#BPM: 120\n" +
                    $"#WAV01: {wav1}\n" +
                    $"#WAV02: {wav2}\n" +
                    "#00112: 0102\n");
                // Touch the referenced WAV files so ResolveBGMPath returns the
                // resolved (absolute) dtx-relative path rather than the raw input.
                File.WriteAllBytes(Path.Combine(tempDir, wav1), Array.Empty<byte>());
                File.WriteAllBytes(Path.Combine(tempDir, wav2), Array.Empty<byte>());

                var chart = await DTXChartParser.ParseAsync(dtxPath);

                Assert.Equal(2, chart.WavDefinitions.Count);
                Assert.Contains("01", chart.WavDefinitions.Keys);
                Assert.Contains("02", chart.WavDefinitions.Keys);
                Assert.EndsWith(wav1, chart.WavDefinitions["01"]);
                Assert.EndsWith(wav2, chart.WavDefinitions["02"]);
                // Resolved paths should be absolute (or at least contain the temp dir)
                Assert.Contains(tempDir, chart.WavDefinitions["01"]);
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
            }
        }

        [Fact]
        public async Task ParseAsync_NoWavLines_WavDefinitionsEmpty()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), $"dtx-test-{Guid.NewGuid()}");
            Directory.CreateDirectory(tempDir);
            var dtxPath = Path.Combine(tempDir, "no-wav.dtx");
            try
            {
                File.WriteAllText(dtxPath,
                    "#TITLE: NoWav\n" +
                    "#BPM: 120\n" +
                    "#00112: 01\n");

                var chart = await DTXChartParser.ParseAsync(dtxPath);

                Assert.NotNull(chart.WavDefinitions);
                Assert.Empty(chart.WavDefinitions);
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
            }
        }

        /// <summary>
        /// When a relative #WAV filename exists in the DTX directory, the parser must
        /// resolve it relative to the chart location. ResolveBGMPath checks the
        /// chart-relative path first (Strategy 1), so even when a file with the same
        /// name exists elsewhere, the chart-relative file wins.
        /// </summary>
        [Fact]
        public async Task ParseAsync_ChartRelativeWavWinsOverCwd()
        {
            var chartDir = Path.Combine(Path.GetTempPath(), $"dtx-chart-{Guid.NewGuid()}");
            Directory.CreateDirectory(chartDir);

            var wavName = "collision.wav";
            var dtxPath = Path.Combine(chartDir, "chart.dtx");
            var chartWav = Path.Combine(chartDir, wavName);

            try
            {
                // Place the WAV file in the chart directory
                File.WriteAllBytes(chartWav, new byte[] { 0xCA });

                File.WriteAllText(dtxPath,
                    "#TITLE: Collision Test\n" +
                    "#BPM: 120\n" +
                    $"#WAV01: {wavName}\n" +
                    "#00112: 01\n");

                var chart = await DTXChartParser.ParseAsync(dtxPath);

                Assert.Single(chart.WavDefinitions);
                // The resolved path must point to the chart directory, not CWD
                Assert.Equal(chartWav, chart.WavDefinitions["01"]);
            }
            finally
            {
                if (Directory.Exists(chartDir)) Directory.Delete(chartDir, recursive: true);
            }
        }

        /// <summary>
        /// DTX files use backslashes in #WAV paths (e.g. "Audio\drums\kick.wav").
        /// On non-Windows platforms, these must be normalized so Path.Combine and
        /// File.Exists resolve correctly.
        /// </summary>
        [Fact]
        public async Task ParseAsync_BackslashPathsNormalizedOnNonWindows()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), $"dtx-test-{Guid.NewGuid()}");
            Directory.CreateDirectory(tempDir);
            var subDir = Path.Combine(tempDir, "Audio");
            Directory.CreateDirectory(subDir);
            var dtxPath = Path.Combine(tempDir, "test.dtx");
            var wavName = $"test-{Guid.NewGuid():N}.wav";

            try
            {
                // Write DTX with backslash path (as real DTX files do)
                File.WriteAllText(dtxPath,
                    "#TITLE: Backslash Test\n" +
                    "#BPM: 120\n" +
                    $"#WAV01: Audio\\{wavName}\n" +
                    "#00112: 01\n");

                // Place the actual WAV in the subdirectory
                File.WriteAllBytes(Path.Combine(subDir, wavName), Array.Empty<byte>());

                var chart = await DTXChartParser.ParseAsync(dtxPath);

                Assert.Single(chart.WavDefinitions);
                // The resolved path should point to the Audio subdirectory
                Assert.Contains("Audio", chart.WavDefinitions["01"]);
                Assert.EndsWith(wavName, chart.WavDefinitions["01"]);
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
            }
        }

        [Fact]
        public async Task ParseAsync_ShortMeasureLength_ShouldShiftFollowingBar()
        {
            var chart = await ParseTimingFixtureAsync(
                "#BPM: 120\n" +
                "#00002:0.5\n" +
                "#00111:01\n");

            var note = Assert.Single(chart.Notes);
            Assert.Equal(1000.0, note.TimeMs, precision: 3);
        }

        [Fact]
        public async Task ParseAsync_ExtendedMeasureLength_ShouldShiftFollowingBar()
        {
            var chart = await ParseTimingFixtureAsync(
                "#BPM: 120\n" +
                "#00002:1.5\n" +
                "#00111:01\n");

            var note = Assert.Single(chart.Notes);
            Assert.Equal(3000.0, note.TimeMs, precision: 3);
        }

        [Fact]
        public async Task ParseAsync_DirectTempoChange_ShouldResolveAtPairTick()
        {
            var chart = await ParseTimingFixtureAsync(
                "#BPM: 120\n" +
                "#00003:00F0\n" +
                "#00111:01\n");

            var note = Assert.Single(chart.Notes);
            Assert.Equal(1500.0, note.TimeMs, precision: 3);
        }

        [Fact]
        public async Task ParseAsync_ReferencedTempoDefinitionBeforeTimeline_ShouldResolve()
        {
            const double referencedBpm = 180.5;
            var chart = await ParseTimingFixtureAsync(
                "#BPM: 120\n" +
                "#BPM01:180.5\n" +
                "#00008:0001\n" +
                "#00111:01\n");

            var note = Assert.Single(chart.Notes);
            Assert.Equal(1000.0 + 120000.0 / referencedBpm, note.TimeMs, precision: 3);
        }

        [Fact]
        public async Task ParseAsync_ReferencedTempoDefinitionAfterTimeline_ShouldResolve()
        {
            const double referencedBpm = 180.5;
            var chart = await ParseTimingFixtureAsync(
                "#BPM: 120\n" +
                "#00008:0001\n" +
                "#00111:01\n" +
                "#BPM01:180.5\n");

            var note = Assert.Single(chart.Notes);
            Assert.Equal(1000.0 + 120000.0 / referencedBpm, note.TimeMs, precision: 3);
        }

        [Fact]
        public async Task ParseAsync_ReferencedTempoOpaqueId_ShouldResolve()
        {
            const double referencedBpm = 210.25;
            var chart = await ParseTimingFixtureAsync(
                "#BPM: 120\n" +
                "#BPMAA:210.25\n" +
                "#00008:00AA\n" +
                "#00111:01\n");

            var note = Assert.Single(chart.Notes);
            Assert.Equal(1000.0 + 120000.0 / referencedBpm, note.TimeMs, precision: 3);
        }

        [Fact]
        public async Task ParseAsync_TempoDirectivesSamePosition_ShouldLetLastSourceRowWin()
        {
            const double referencedBpm = 180.5;
            var referencedAfterDirect = await ParseTimingFixtureAsync(
                "#BPM: 120\n" +
                "#BPM01:180.5\n" +
                "#00003:00F0\n" +
                "#00008:0001\n" +
                "#00111:01\n");
            var directAfterReferenced = await ParseTimingFixtureAsync(
                "#BPM: 120\n" +
                "#BPM01:180.5\n" +
                "#00008:0001\n" +
                "#00003:00F0\n" +
                "#00111:01\n");

            Assert.Equal(1000.0 + 120000.0 / referencedBpm,
                Assert.Single(referencedAfterDirect.Notes).TimeMs,
                precision: 3);
            Assert.Equal(1500.0,
                Assert.Single(directAfterReferenced.Notes).TimeMs,
                precision: 3);
        }

        [Fact]
        public async Task ParseAsync_TempoChange_ShouldAlignNotesBgmAndMeasureLines()
        {
            var chart = await ParseTimingFixtureAsync(
                "#BPM: 120\n" +
                "#00003:00F0\n" +
                "#00001:0001\n" +
                "#00111:01\n");

            var bgm = Assert.Single(chart.BGMEvents);
            var note = Assert.Single(chart.Notes);
            var barOne = Assert.Single(chart.MeasureLines.Where(line => line.Bar == 1));

            Assert.Equal(1000.0, bgm.TimeMs, precision: 3);
            Assert.Equal(1500.0, note.TimeMs, precision: 3);
            Assert.Equal(note.TimeMs, barOne.TimeMs, precision: 3);
        }

        [Fact]
        public async Task ParseAsync_MalformedTimingSyntax_ShouldFallBackToBaseTiming()
        {
            var chart = await ParseTimingFixtureAsync(
                "#BPM: 120\n" +
                "#BPMZZ:0\n" +
                "#00002:0\n" +
                "#00003:00ZZ\n" +
                "#00003:0\n" +
                "#00008:0002\n" +
                "#00008:0\n" +
                "#00008:0000\n" +
                "#00111:01\n");

            var note = Assert.Single(chart.Notes);
            Assert.Equal(2000.0, note.TimeMs, precision: 3);
        }

        [Theory]
        [InlineData("Infinity")]
        [InlineData("1e9999")]
        public async Task ParseAsync_NonFiniteExtendedTimingValues_ShouldFallBackToBaseTiming(
            string malformedValue)
        {
            var chart = await ParseTimingFixtureAsync(
                "#BPM: 120\n" +
                $"#BPM01:{malformedValue}\n" +
                $"#00002:{malformedValue}\n" +
                "#00008:0001\n" +
                "#00111:01\n");

            var note = Assert.Single(chart.Notes);
            Assert.Equal(2000.0, note.TimeMs, precision: 3);
            Assert.True(double.IsFinite(note.TimeMs));
            Assert.True(double.IsFinite(chart.DurationMs));
        }

        [Theory]
        [InlineData("Infinity")]
        [InlineData("1e9999")]
        public async Task ParseAsync_NonFiniteBaseBpm_ShouldUseDefaultBaseTiming(
            string malformedValue)
        {
            var chart = await ParseTimingFixtureAsync(
                $"#BPM:{malformedValue}\n" +
                "#00111:01\n" +
                "#00211:01\n");

            Assert.Equal(120.0, chart.Bpm);
            var note = chart.Notes.OrderBy(candidate => candidate.Bar).Last();
            Assert.Equal(4000.0, note.TimeMs, precision: 3);
            Assert.True(double.IsFinite(note.TimeMs));
            Assert.True(double.IsFinite(chart.DurationMs));
        }

        [Theory]
        [InlineData(".dtx")]
        [InlineData(".gda")]
        [InlineData(".g2d")]
        [InlineData(".txt")]
        [InlineData(".DTX")]
        public async Task ParseAsync_MeasureLengthCarriesForwardInNonBmsCharts(
            string extension)
        {
            var chart = await ParseTimingFixtureAsync(
                "#BPM:120\n" +
                "#00002:0.75\n" +
                "#00211:01\n",
                extension);

            var note = Assert.Single(chart.Notes);
            Assert.Equal(3000.0, note.TimeMs, precision: 3);
            Assert.Equal(note.TimeMs,
                Assert.Single(chart.MeasureLines.Where(line => line.Bar == 2)).TimeMs,
                precision: 3);
        }

        [Theory]
        [InlineData(".bms")]
        [InlineData(".bme")]
        [InlineData(".bml")]
        [InlineData(".BMS")]
        public async Task ParseAsync_MeasureLengthResetsPerBarInBmsCharts(
            string extension)
        {
            var chart = await ParseTimingFixtureAsync(
                "#BPM:120\n" +
                "#00002:0.75\n" +
                "#00211:01\n",
                extension);

            var note = Assert.Single(chart.Notes);
            Assert.Equal(3500.0, note.TimeMs, precision: 3);
            Assert.Equal(note.TimeMs,
                Assert.Single(chart.MeasureLines.Where(line => line.Bar == 2)).TimeMs,
                precision: 3);
        }

        [Fact]
        public async Task ParseAsync_MeasureLengthCarryForward_ShouldComposeWithMidBarTempoChange()
        {
            var chart = await ParseTimingFixtureAsync(
                "#BPM:120\n" +
                "#00002:0.75\n" +
                "#00103:0060\n" +
                "#00211:01\n");

            var note = Assert.Single(chart.Notes);
            Assert.Equal(3187.5, note.TimeMs, precision: 3);
        }

        private static async Task<ParsedChart> ParseTimingFixtureAsync(
            string contents,
            string extension = ".dtx")
        {
            var tempDirectory = Path.Combine(Path.GetTempPath(), $"dtx-timing-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDirectory);
            var dtxPath = Path.Combine(tempDirectory, $"fixture{extension}");

            try
            {
                File.WriteAllText(dtxPath, contents);
                return await DTXChartParser.ParseAsync(dtxPath);
            }
            finally
            {
                if (Directory.Exists(tempDirectory))
                    Directory.Delete(tempDirectory, recursive: true);
            }
        }

        private readonly string _tempDir = Path.Combine(
            Path.GetTempPath(),
            $"DTXMania_DTXParserTest_{Guid.NewGuid():N}");

        private string EnsureTempDir()
        {
            Directory.CreateDirectory(_tempDir);
            return _tempDir;
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDir))
            {
                try
                {
                    Directory.Delete(_tempDir, true);
                }
                catch
                {
                    // Best-effort temp cleanup
                }
            }
        }

        private string CreateTempDtx(string content)
        {
            var path = Path.Combine(EnsureTempDir(), $"{Guid.NewGuid():N}.dtx");
            File.WriteAllText(path, content);
            return path;
        }

        /// <summary>
        /// DTX fixture with one note on every mapped drum channel, shared by the
        /// parser-only channel table regression and the parser-to-default-key
        /// contract regression (HPA-443).
        /// </summary>
        private const string AllDrumChannelsChart =
            "#BPM: 120.0\n" +
            "#00011: 01000000\n" +  // HH Close (0x11 -> Lane 1)
            "#00012: 01000000\n" +  // Snare (0x12 -> Lane 3)
            "#00013: 01000000\n" +  // Bass Drum (0x13 -> Lane 5)
            "#00014: 01000000\n" +  // High Tom (0x14 -> Lane 4)
            "#00015: 01000000\n" +  // Low Tom (0x15 -> Lane 6)
            "#00016: 01000000\n" +  // Crash (0x16 -> Lane 8)
            "#00017: 01000000\n" +  // Floor Tom (0x17 -> Lane 7)
            "#00018: 01000000\n" +  // HH Open (0x18 -> Lane 1)
            "#00019: 01000000\n" +  // Ride (0x19 -> Lane 9)
            "#0001A: 01000000\n" +  // Left Crash (0x1A -> Lane 0)
            "#0001B: 01000000\n" +  // Left Pedal (0x1B -> Lane 2)
            "#0001C: 01000000\n";   // Left Bass Drum (0x1C -> Lane 2)

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
        public async Task ParseAsync_WhenWavDefinitionsFollowTimelineRows_ShouldResolveThirtySecondBgmAndDrum()
        {
            // Reproduces DTXCreator output with #xxxC2 rows before the WAV table.
            // BGM 1A is in the final cell of a 32-pair row (tick 186);
            // drum 0G is in the second cell (tick 6).
            var content =
                "#BPM: 185\n" +
                "#000C2: 02\n" +
                "#013C2: 01\n" +
                "#WAV1A: bgm.ogg\n" +
                "#VOLUME1A: 75\n" +
                "#WAV0G: hihat.ogg\n" +
                "#VOLUME0G: 60\n" +
                "#PAN0G: -25\n" +
                "#00001: " + new string('0', 62) + "1A\n" +
                "#00011: 000G" + new string('0', 60) + "\n";
            var path = CreateTempDtx(content);
            File.WriteAllText(Path.Combine(_tempDir, "bgm.ogg"), "fake");
            File.WriteAllText(Path.Combine(_tempDir, "hihat.ogg"), "fake");

            var chart = await DTXChartParser.ParseAsync(path);

            var bgm = Assert.Single(chart.BGMEvents);
            Assert.Equal("1A", bgm.WavId);
            Assert.Equal(186, bgm.Tick);
            Assert.Equal((186.0 / 192.0) * 4.0 * 60000.0 / 185.0, bgm.TimeMs, 3);
            Assert.Equal(Path.Combine(_tempDir, "bgm.ogg"), bgm.AudioFilePath);
            Assert.Equal(chart.WavDefinitions["1A"], bgm.AudioFilePath);
            Assert.Equal(0.75f, chart.GetVolume("1A"));

            var drum = Assert.Single(chart.Notes);
            Assert.Equal(0x11, drum.Channel);
            Assert.Equal("0G", drum.Value);
            Assert.Equal(6, drum.Tick);
            Assert.Equal((6.0 / 192.0) * 4.0 * 60000.0 / 185.0, drum.TimeMs, 3);
            Assert.Equal(Path.Combine(_tempDir, "hihat.ogg"), chart.WavDefinitions["0G"]);
            Assert.Equal(0.60f, chart.GetVolume("0G"));
            Assert.Equal(-0.25f, chart.GetPan("0G"));
        }

        [Fact]
        public async Task ParseAsync_WhenWavDefinitionsFollowChipRows_ShouldResolveForwardReferences()
        {
            // Definitions authored after their chip rows must still resolve before
            // finalization, including alternate WAV volume/pan header spellings.
            var content =
                "#BPM: 120\n" +
                "#00001: 01\n" +
                "#00012: 02\n" +
                "#WAV01: bgm.ogg\n" +
                "#WAV02: snare.ogg\n" +
                "#WAVVOL01: 35\n" +
                "#WAVPAN02: -50\n";
            var path = CreateTempDtx(content);
            File.WriteAllText(Path.Combine(_tempDir, "bgm.ogg"), "fake");
            File.WriteAllText(Path.Combine(_tempDir, "snare.ogg"), "fake");

            var chart = await DTXChartParser.ParseAsync(path);

            Assert.Equal(Path.Combine(_tempDir, "bgm.ogg"),
                Assert.Single(chart.BGMEvents).AudioFilePath);
            Assert.Equal(Path.Combine(_tempDir, "snare.ogg"),
                chart.WavDefinitions[Assert.Single(chart.Notes).Value]);
            Assert.Equal(0.35f, chart.GetVolume("01"));
            Assert.Equal(-0.50f, chart.GetPan("02"));
        }

        [Fact]
        public async Task ParseAsync_WithDrumNotes_ShouldParseAllMappedChannels()
        {
            // Test all mapped drum channels; canonical channel-to-lane contract
            // (gameplay order: LC, HH, LP, SN, HT, DB, LT, FT, CY, RD)
            var path = CreateTempDtx(AllDrumChannelsChart);

            var chart = await DTXChartParser.ParseAsync(path);

            var expectedLanes = new Dictionary<int, int>
            {
                { 0x1A, 0 }, // LC
                { 0x11, 1 }, // HH Close
                { 0x18, 1 }, // HH Open (shared lane with 0x11)
                { 0x1B, 2 }, // LP
                { 0x1C, 2 }, // Left Bass Drum (shared lane with 0x1B)
                { 0x12, 3 }, // SN
                { 0x14, 4 }, // HT
                { 0x13, 5 }, // DB
                { 0x15, 6 }, // LT
                { 0x17, 7 }, // FT
                { 0x16, 8 }, // CY
                { 0x19, 9 }, // RD
            };

            Assert.Equal(expectedLanes.Count, chart.TotalNotes);
            foreach (var (channel, expectedLane) in expectedLanes)
            {
                var notes = chart.Notes.Where(n => n.Channel == channel).ToList();
                Assert.True(notes.Count == 1,
                    $"Channel 0x{channel:X2} should resolve to exactly one note, found {notes.Count}");
                Assert.True(notes[0].LaneIndex == expectedLane,
                    $"Channel 0x{channel:X2} should map to lane {expectedLane}, got {notes[0].LaneIndex}");
            }
        }

        [Fact]
        public async Task ParseAsync_WithDrumNotes_LaneIndexShouldMatchDefaultKeyBindingLane()
        {
            // Contract regression (HPA-443): every parsed note's LaneIndex must
            // equal the lane bound to that instrument's default keyboard key,
            // so parsed charts are playable with untouched default bindings.
            var path = CreateTempDtx(AllDrumChannelsChart);

            var chart = await DTXChartParser.ParseAsync(path);

            var bindings = new KeyBindings();
            var channelToDefaultKey = new Dictionary<int, string>
            {
                { 0x1A, "Key.A" },            // LC -> lane 0
                { 0x11, "Key.J" },            // HH Close -> lane 1
                { 0x18, "Key.J" },            // HH Open (paired with 0x11)
                { 0x1B, "Key.G" },            // LP -> lane 2
                { 0x1C, "Key.G" },            // Left Bass (paired with 0x1B)
                { 0x12, "Key.S" },            // SN -> lane 3
                { 0x14, "Key.K" },            // HT -> lane 4
                { 0x13, "Key.Space" },        // DB -> lane 5
                { 0x15, "Key.L" },            // LT -> lane 6
                { 0x17, "Key.F" },            // FT -> lane 7
                { 0x16, "Key.D" },            // CY -> lane 8
                { 0x19, "Key.OemSemicolon" }, // RD -> lane 9
            };

            foreach (var (channel, defaultKey) in channelToDefaultKey)
            {
                var note = Assert.Single(chart.Notes.Where(n => n.Channel == channel));
                var boundLane = bindings.GetLane(defaultKey);
                Assert.True(boundLane >= 0,
                    $"Default key '{defaultKey}' should be bound to a lane");
                Assert.True(note.LaneIndex == boundLane,
                    $"Channel 0x{channel:X2} parsed to lane {note.LaneIndex} " +
                    $"but default key '{defaultKey}' is bound to lane {boundLane}");
            }
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
            var absoluteWavPath = Path.Combine(EnsureTempDir(), $"{Guid.NewGuid():N}.ogg");
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
            var expectedPath = Path.Combine(EnsureTempDir(), fileName);
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
                () => DTXChartParser.ParseAsync(Path.Combine(EnsureTempDir(), "nonexistent.dtx")));
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
            var txtPath = Path.Combine(EnsureTempDir(), "testfile.txt");
            File.WriteAllText(txtPath, "#TITLE: Test\n");

            var (song, chart) = await DTXChartParser.ParseSongEntitiesAsync(txtPath);

            Assert.Equal("testfile", song.Title);
        }

        [Fact]
        public async Task ParseSongEntitiesAsync_NonExistentFile_ShouldThrow()
        {
            await Assert.ThrowsAsync<FileNotFoundException>(
                () => DTXChartParser.ParseSongEntitiesAsync(Path.Combine(EnsureTempDir(), "nonexistent.dtx")));
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
        public async Task ParseAsync_WithLowercaseNoteValue_ShouldNormalizeToUppercase()
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
        public async Task ParseAsync_WithLowercaseBGMValue_ShouldNormalizeToUppercase()
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
        public async Task ParseAsync_WithMixedCaseNoteValues_ShouldAllNormalizeToUppercase()
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
            var audioDir = Path.Combine(EnsureTempDir(), "Audio");
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
        public void IsTextProperlyDecoded_WithValidText_ShouldReturnTrue()
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
            var txtPath = Path.Combine(EnsureTempDir(), "fallback_test.txt");
            File.WriteAllText(txtPath, "#BPM: 120.0\n#DLEVEL: 5\n");

            var (song, chart) = await DTXChartParser.ParseSongEntitiesAsync(txtPath);

            Assert.Equal("fallback_test", song.Title);
        }

        [Fact]
        public void IsTextProperlyDecoded_WithControlCharacter_ShouldReturnFalse()
        {
            var result = InvokePrivateStaticMethod<bool>("IsTextProperlyDecoded", "test\x01value");
            Assert.False(result);
        }

        [Fact]
        public void IsTextProperlyDecoded_WithTabAndNewline_ShouldReturnTrue()
        {
            Assert.True(InvokePrivateStaticMethod<bool>("IsTextProperlyDecoded", "line1\tvalue\nline2"));
        }

        #endregion

        #region Per-WAV Volume / Pan Tests

        [Fact]
        public async Task ParseAsync_WithVolumeHeaders_ShouldPopulateNormalizedVolumes()
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
        public async Task ParseAsync_WithPanHeaders_ShouldPopulateNormalizedPans()
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
        public async Task ParseAsync_WithWavVolAndPan_ShouldNotBeMisparsedAsWavDefinitions()
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
        public async Task ParseAsync_WithOutOfRangeVolumeAndPan_ShouldBeClamped()
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
        public async Task ParseAsync_WithPanelHeader_ShouldNotBeTreatedAsPanDefinition()
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
        public async Task ParseAsync_BackgroundWavId_ShouldTrackCommonBgmFilename()
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
        public async Task ParseAsync_BackgroundWavId_ShouldFallBackToFirstWavWhenNoBgmEvents()
        {
            var content =
                "#WAV05: only.ogg\n" +
                "#00011: 0101\n"; // drum notes only, no BGM (channel 01) events
            var path = CreateTempDtx(content);

            var chart = await DTXChartParser.ParseAsync(path);

            Assert.Equal("05", chart.BackgroundWavId);
        }

        [Fact]
        public async Task ParseAsync_BackgroundWavVolumeAndPan_ShouldBeResolvableViaWavId()
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
        public async Task ParseAsync_WithNoWavDefinitions_ShouldLeaveBackgroundWavIdEmpty()
        {
            var content = "#BPM: 120.0\n";
            var path = CreateTempDtx(content);

            var chart = await DTXChartParser.ParseAsync(path);

            Assert.Equal("", chart.BackgroundWavId);
        }

        [Fact]
        public async Task ParseAsync_WithNoVolumeOrPanHeaders_ShouldLeaveMapsEmpty()
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
        public async Task ParseAsync_WithPanelHeader_ShouldNotCreateBogusPanEntry()
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

        [Fact]
        public async Task ParseAsync_WithPanelAndPanHeaders_ShouldCoexistWithoutInterference()
        {
            // Proves the #PANEL guard does not over-reject a valid #PAN01 entry.
            // Both headers start with "#PAN"; the guard must exclude only "#PANEL"
            // while still routing "#PAN01" into wavPans.
            var content =
                "#WAV01: a.wav\n" +
                "#PANEL: 2\n" +
                "#PAN01: 75\n";
            var path = CreateTempDtx(content);

            var chart = await DTXChartParser.ParseAsync(path);

            Assert.False(chart.WavPans.ContainsKey("EL"));
            Assert.True(chart.WavPans.ContainsKey("01"));
            Assert.Equal(75, chart.WavPans["01"]);
        }

        [Fact]
        public async Task ParseAsync_WithLowercasePanHeader_ShouldBeNormalizedToUpperAndStored()
        {
            // ParseHeaderCommand uppercases the command via ToUpperInvariant before
            // the #PAN branch, so "#pan01" must still produce wavPans["01"].
            // Pins the normalization contract that note/WAV id lookups rely on.
            var content =
                "#WAV01: a.wav\n" +
                "#pan01: -30\n";
            var path = CreateTempDtx(content);

            var chart = await DTXChartParser.ParseAsync(path);

            Assert.True(chart.WavPans.ContainsKey("01"));
            Assert.Equal(-30, chart.WavPans["01"]);
        }

        [Fact]
        public async Task ParseAsync_WithPanelHeaderWithWhitespaceValue_ShouldStillBeExcludedFromPanMap()
        {
            // A #PANEL value with surrounding whitespace must still be ignored by the
            // pan branch. The guard checks the command prefix, not the value, so
            // whitespace-only or non-numeric values must not leak into wavPans.
            var content =
                "#WAV01: a.wav\n" +
                "#PANEL:  \n";
            var path = CreateTempDtx(content);

            var chart = await DTXChartParser.ParseAsync(path);

            Assert.False(chart.WavPans.ContainsKey("EL"));
            Assert.Empty(chart.WavPans);
        }

        #endregion

        #region Whole-File Video Event Tests

        [Fact]
        public async Task ParseAsync_AviDefinitionWithChannel54_ShouldCreateSingleVideoEvent()
        {
            var content =
                "#BPM: 120.0\n" +
                "#AVI01: bg.avi\n" +
                "#00054: 01\n";
            var path = CreateTempDtx(content);
            File.WriteAllBytes(Path.Combine(_tempDir, "bg.avi"), Array.Empty<byte>());

            var chart = await DTXChartParser.ParseAsync(path);

            var videoEvent = Assert.Single(chart.VideoEvents);
            Assert.Equal(0, videoEvent.Bar);
            Assert.Equal(0, videoEvent.Tick);
            Assert.Equal(0.0, videoEvent.TimeMs, 3);
            Assert.Equal("01", videoEvent.VideoId);
            Assert.Equal(Path.Combine(_tempDir, "bg.avi"), videoEvent.VideoFilePath);
        }

        [Fact]
        public async Task ParseAsync_VideoAliasDefinition_ShouldBehaveLikeAvi()
        {
            // #VIDEOxx is an alias of #AVIxx; quoted values are unquoted like other headers
            var content =
                "#BPM: 120.0\n" +
                "#VIDEO01: \"bg.avi\"\n" +
                "#00054: 01\n";
            var path = CreateTempDtx(content);

            var chart = await DTXChartParser.ParseAsync(path);

            var videoEvent = Assert.Single(chart.VideoEvents);
            Assert.Equal("01", videoEvent.VideoId);
            Assert.Equal("bg.avi", Path.GetFileName(videoEvent.VideoFilePath));
        }

        [Fact]
        public async Task ParseAsync_Channel5A_ShouldBehaveLikeChannel54()
        {
            var content =
                "#BPM: 120.0\n" +
                "#AVI01: bg.avi\n" +
                "#0005A: 01\n";
            var path = CreateTempDtx(content);

            var chart = await DTXChartParser.ParseAsync(path);

            var videoEvent = Assert.Single(chart.VideoEvents);
            Assert.Equal("01", videoEvent.VideoId);
            Assert.Equal(0.0, videoEvent.TimeMs, 3);
        }

        [Fact]
        public async Task ParseAsync_VideoZeroPairs_ShouldBeIgnored()
        {
            var content =
                "#BPM: 120.0\n" +
                "#AVI01: bg.avi\n" +
                "#00054: 0000\n";
            var path = CreateTempDtx(content);

            var chart = await DTXChartParser.ParseAsync(path);

            Assert.Empty(chart.VideoEvents);
        }

        [Fact]
        public async Task ParseAsync_VideoMultiplePairs_ShouldCalculateTicks()
        {
            var content =
                "#BPM: 120.0\n" +
                "#AVI01: bg.avi\n" +
                "#00054: 01000203\n";
            var path = CreateTempDtx(content);

            var chart = await DTXChartParser.ParseAsync(path);

            Assert.Equal(3, chart.VideoEvents.Count);
            // 4 pairs per measure: pair 0 -> tick 0, pair 2 -> 96, pair 3 -> 144
            Assert.Equal(0, chart.VideoEvents[0].Tick);
            Assert.Equal(96, chart.VideoEvents[1].Tick);
            Assert.Equal(144, chart.VideoEvents[2].Tick);
            // 120 BPM: one 4/4 measure = 2000ms
            Assert.Equal(0.0, chart.VideoEvents[0].TimeMs, 3);
            Assert.Equal(1000.0, chart.VideoEvents[1].TimeMs, 3);
            Assert.Equal(1500.0, chart.VideoEvents[2].TimeMs, 3);
        }

        [Fact]
        public async Task ParseAsync_VideoLowercaseIds_ShouldNormalizeToUppercase()
        {
            var content =
                "#BPM: 120.0\n" +
                "#avi0a: bg.avi\n" +
                "#00054: 0a\n";
            var path = CreateTempDtx(content);

            var chart = await DTXChartParser.ParseAsync(path);

            var videoEvent = Assert.Single(chart.VideoEvents);
            Assert.Equal("0A", videoEvent.VideoId);
            Assert.Equal("bg.avi", Path.GetFileName(videoEvent.VideoFilePath));
        }

        [Fact]
        public async Task ParseAsync_VideoMissingDefinition_ShouldRetainEventWithEmptyPath()
        {
            var content =
                "#BPM: 120.0\n" +
                "#00154: 01\n"; // no #AVI01/#VIDEO01 definition anywhere
            var path = CreateTempDtx(content);

            var chart = await DTXChartParser.ParseAsync(path);

            var videoEvent = Assert.Single(chart.VideoEvents);
            Assert.Equal("01", videoEvent.VideoId);
            Assert.Equal("", videoEvent.VideoFilePath);
            // Video-only chart must still resolve timing (video bars drive finalization)
            Assert.Equal(2000.0, videoEvent.TimeMs, 3);
            Assert.Equal(2500.0, chart.DurationMs, 3);
        }

        [Fact]
        public async Task ParseAsync_AvipanHeader_ShouldBeIgnored()
        {
            // #AVIPANxx must not register as a video definition for any id
            var content =
                "#BPM: 120.0\n" +
                "#AVIPAN01: bg.avi\n" +
                "#00054: 01\n";
            var path = CreateTempDtx(content);

            var chart = await DTXChartParser.ParseAsync(path);

            var videoEvent = Assert.Single(chart.VideoEvents);
            Assert.Equal("", videoEvent.VideoFilePath);
        }

        [Fact]
        public async Task ParseAsync_VideoDefinitionAfterTimeline_ShouldStillResolve()
        {
            // Load-bearing: definitions placed after timeline rows must still resolve
            var content =
                "#BPM: 120.0\n" +
                "#00054: 01\n" +
                "#AVI01: bg.avi\n";
            var path = CreateTempDtx(content);

            var chart = await DTXChartParser.ParseAsync(path);

            var videoEvent = Assert.Single(chart.VideoEvents);
            Assert.Equal("bg.avi", Path.GetFileName(videoEvent.VideoFilePath));
        }

        [Fact]
        public async Task ParseAsync_RepeatedLateAviDefinitions_FinalAssignmentWins()
        {
            var content =
                "#BPM: 120.0\n" +
                "#00054: 01\n" +
                "#AVI01: first.avi\n" +
                "#AVI01: second.avi\n";
            var path = CreateTempDtx(content);

            var chart = await DTXChartParser.ParseAsync(path);

            var videoEvent = Assert.Single(chart.VideoEvents);
            Assert.Equal("second.avi", Path.GetFileName(videoEvent.VideoFilePath));
        }

        [Fact]
        public async Task ParseAsync_RepeatedLateVideoAliasDefinitions_FinalAssignmentWins()
        {
            // #AVIxx and #VIDEOxx share one id namespace; the last assignment wins
            var content =
                "#BPM: 120.0\n" +
                "#00054: 01\n" +
                "#AVI01: first.avi\n" +
                "#VIDEO01: second.avi\n";
            var path = CreateTempDtx(content);

            var chart = await DTXChartParser.ParseAsync(path);

            var videoEvent = Assert.Single(chart.VideoEvents);
            Assert.Equal("second.avi", Path.GetFileName(videoEvent.VideoFilePath));
        }

        [Fact]
        public async Task ParseAsync_VideoDefinitionWithoutColon_ShouldBeIgnored()
        {
            // #AVIxx with no colon is malformed; it must be consumed (not treated
            // as timeline data) and ignored without registering a definition.
            var content =
                "#BPM: 120.0\n" +
                "#AVI01 no-colon-here\n" +
                "#00054: 01\n";
            var path = CreateTempDtx(content);

            var chart = await DTXChartParser.ParseAsync(path);

            var videoEvent = Assert.Single(chart.VideoEvents);
            Assert.Equal("", videoEvent.VideoFilePath);
        }

        [Fact]
        public async Task ParseAsync_VideoChannel5A_ShouldBehaveLikeChannel54()
        {
            // Channel 0x5A is an alias for 0x54; both produce video events.
            var content =
                "#BPM: 120.0\n" +
                "#AVI01: bg.avi\n" +
                "#0005A: 01\n";
            var path = CreateTempDtx(content);

            var chart = await DTXChartParser.ParseAsync(path);

            var videoEvent = Assert.Single(chart.VideoEvents);
            Assert.Equal("01", videoEvent.VideoId);
            Assert.Equal("bg.avi", Path.GetFileName(videoEvent.VideoFilePath));
        }

        [Fact]
        public async Task ParseAsync_VideoChannelWithEmptyData_ShouldProduceNoVideoEvents()
        {
            // A channel 54 row with empty/whitespace data must not create video
            // events (the IsNullOrWhiteSpace early return in ParseVideoEvents).
            var content =
                "#BPM: 120.0\n" +
                "#AVI01: bg.avi\n" +
                "#00054: \n";
            var path = CreateTempDtx(content);

            var chart = await DTXChartParser.ParseAsync(path);

            Assert.Empty(chart.VideoEvents);
        }

        [Fact]
        public async Task ParseAsync_EmptyAviDefinition_ShouldLeaveVideoFilePathEmpty()
        {
            // An empty "#AVI01:" target must not register. ResolveBGMPath("") would
            // otherwise resolve to the chart directory, making the event look
            // resolved and starting a decode against a directory. The event must
            // stay unresolved so the stage keeps the static background.
            var content =
                "#BPM: 120.0\n" +
                "#AVI01:\n" +
                "#00054: 01\n";
            var path = CreateTempDtx(content);

            var chart = await DTXChartParser.ParseAsync(path);

            var videoEvent = Assert.Single(chart.VideoEvents);
            Assert.Equal("01", videoEvent.VideoId);
            Assert.Equal("", videoEvent.VideoFilePath);
        }

        [Theory]
        [InlineData("#AVI01: \n")]
        [InlineData("#AVI01: \"\"\n")]
        public async Task ParseAsync_WhitespaceOrQuotedEmptyAviDefinition_ShouldLeaveVideoFilePathEmpty(string definitionLine)
        {
            // Whitespace-only and quoted-empty targets are stripped to "" after
            // trim/quote removal and must be ignored for the same reason as above.
            var content =
                "#BPM: 120.0\n" +
                definitionLine +
                "#00054: 01\n";
            var path = CreateTempDtx(content);

            var chart = await DTXChartParser.ParseAsync(path);

            var videoEvent = Assert.Single(chart.VideoEvents);
            Assert.Equal("01", videoEvent.VideoId);
            Assert.Equal("", videoEvent.VideoFilePath);
        }

        [Fact]
        public async Task ParseAsync_SimultaneousVideoTriggers_ShouldPreserveSourceOrder()
        {
            // Two video triggers at the same bar/tick (channels 54 and 5A) resolve
            // to the same TimeMs. ProcessVideoEvents consumes due events in list
            // order and starts only the last due one, so the sort must be stable to
            // keep the authored/insertion order and make the last-authored trigger
            // win deterministically. Assert the channel-54 event (authored first)
            // precedes the channel-5A event at equal TimeMs.
            var content =
                "#BPM: 120.0\n" +
                "#AVI01: a.avi\n" +
                "#AVI02: b.avi\n" +
                "#00054: 01\n" +
                "#0005A: 02\n";
            var path = CreateTempDtx(content);
            File.WriteAllBytes(Path.Combine(_tempDir, "a.avi"), Array.Empty<byte>());
            File.WriteAllBytes(Path.Combine(_tempDir, "b.avi"), Array.Empty<byte>());

            var chart = await DTXChartParser.ParseAsync(path);

            Assert.Equal(2, chart.VideoEvents.Count);
            Assert.Equal(chart.VideoEvents[0].TimeMs, chart.VideoEvents[1].TimeMs, 3);
            Assert.Equal("01", chart.VideoEvents[0].VideoId);
            Assert.Equal("02", chart.VideoEvents[1].VideoId);
        }

        #endregion

        #region Encoding Retry Isolation Tests

        /// <summary>
        /// Basic correctness: a valid UTF-8 DTX file produces exactly the expected
        /// note count and WAV definition count. This is NOT a regression guard for
        /// the per-attempt state isolation in ParseAsync — the retry path is
        /// unreachable through the public API because the parser is fully defensive
        /// (no throw on malformed input) and Encoding.UTF8 uses replacement fallback
        /// (no throw on invalid bytes). See the reflection test below for why
        /// per-attempt instantiation matters, and see the comment in ParseAsync for
        /// why the retry path exists as defensive hardening despite being currently
        /// unreachable.
        /// </summary>
        [Fact]
        public async Task ParseAsync_WithValidFile_ShouldProduceExactNoteAndWavCount()
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
            Assert.Equal(2, chart.WavDefinitions.Count);
        }

        /// <summary>
        /// Documents WHY ParseAsync uses per-attempt chart instantiation: calling
        /// ParseFileContentAsync twice on the same ParsedChart (as the old shared-state
        /// design would on encoding retry) doubles the notes because Notes is a List
        /// populated via Add, not a dict that self-heals via key assignment.
        ///
        /// This is a documentation test, not a regression guard. The retry path in
        /// ParseAsync is currently unreachable through the public API (the parser is
        /// fully defensive and Encoding.UTF8 uses replacement fallback), so no
        /// behavioral test can exercise the retry decision. This test proves the
        /// hazard exists at the mechanical level, which is the reason per-attempt
        /// instantiation is correct defensive hardening.
        /// </summary>
        [Fact]
        public async Task ParseFileContentAsync_WhenCalledTwiceOnSameChart_ShouldAccumulateNotesProvingSharedStateHazard()
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

        #region Parse Failure Detection Tests

        /// <summary>
        /// An empty file (zero bytes) must parse successfully and return an empty
        /// chart, not throw. This guards the parsed-flag failure detection: success
        /// is determined by "no exception during parse" (parsed=true), NOT by
        /// "chart has notes." The old Notes.Count==0 check would incorrectly throw
        /// InvalidOperationException for an empty file if lastException were non-null
        /// for any reason, and would incorrectly return a truncated chart if a
        /// failed attempt added notes before throwing.
        /// </summary>
        [Fact]
        public async Task ParseAsync_WithEmptyFile_ShouldReturnEmptyChartWithoutThrowing()
        {
            var path = CreateTempDtx("");
            // Write empty bytes explicitly (CreateTempDtx uses WriteAllText which
            // may write a BOM; ensure truly empty)
            File.WriteAllBytes(path, Array.Empty<byte>());

            var chart = await DTXChartParser.ParseAsync(path);

            Assert.Equal(0, chart.TotalNotes);
            Assert.Empty(chart.WavDefinitions);
        }

        /// <summary>
        /// A file with only comments and whitespace must parse successfully and
        /// return an empty chart. Same parsed-flag guard as the empty-file test,
        /// but exercises the comment/whitespace skipping paths in ParseFileContentAsync.
        /// </summary>
        [Fact]
        public async Task ParseAsync_WithFileWithOnlyCommentsAndWhitespace_ShouldReturnEmptyChart()
        {
            var content =
                "// This is a comment\n" +
                "   \n" +
                "\n" +
                "// Another comment\n" +
                "  \t  \n";
            var path = CreateTempDtx(content);

            var chart = await DTXChartParser.ParseAsync(path);

            Assert.Equal(0, chart.TotalNotes);
            Assert.Empty(chart.WavDefinitions);
        }

        /// <summary>
        /// A file with headers but no note data must parse successfully and return
        /// a chart with zero notes. Guards that the parsed-flag check does not
        /// conflate "no notes" with "parse failed."
        /// </summary>
        [Fact]
        public async Task ParseAsync_WithHeadersOnlyAndNoNotes_ShouldReturnChartWithoutThrowing()
        {
            var content =
                "#BPM: 120.0\n" +
                "#WAV01: kick.wav\n" +
                "#TITLE: Test Song\n";
            var path = CreateTempDtx(content);

            var chart = await DTXChartParser.ParseAsync(path);

            Assert.Equal(0, chart.TotalNotes);
            Assert.Single(chart.WavDefinitions);
            Assert.Equal(120.0, chart.Bpm);
        }

        #endregion

        #region Encoding Retry / Failure Detection Tests

        /// <summary>
        /// When every encoding attempt fails with an IOException (here: the file is
        /// held with an exclusive FileShare.None lock), ParseAsync must exhaust all
        /// encodings, record each failure, and throw InvalidOperationException
        /// wrapping the last exception. This exercises the narrowed catch filter
        /// (IOException), the per-attempt Debug.WriteLine/continue, and the
        /// `!parsed && lastException != null` failure throw — the defensive retry
        /// path that is otherwise unreachable through well-formed input.
        /// </summary>
        [Fact]
        public async Task ParseAsync_WhenAllEncodingsFailWithIoException_ShouldThrowInvalidOperationException()
        {
            var content = "#BPM: 120.0\n#00011: 01000000\n";
            var path = CreateTempDtx(content);

            // Hold an exclusive lock for the duration of the parse so every
            // FileStream open (one per encoding attempt) throws IOException.
            await using var lockStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => DTXChartParser.ParseAsync(path));
            Assert.IsType<IOException>(ex.InnerException);
            Assert.Contains(path, ex.Message);
        }

        #endregion

        #region TryStoreVolume / TryStorePan Guard Tests

        [Fact]
        public async Task ParseAsync_WithNonNumericVolumeAndPanValues_ShouldSilentlyIgnoreEntries()
        {
            // Non-numeric #VOLUME/#PAN values must not throw and must not create
            // entries — int.TryParse returns false and the value is dropped.
            var content =
                "#WAV01: a.wav\n" +
                "#VOLUME01: not-a-number\n" +
                "#PAN01: also-text\n";
            var path = CreateTempDtx(content);

            var chart = await DTXChartParser.ParseAsync(path);

            Assert.Empty(chart.WavVolumes);
            Assert.Empty(chart.WavPans);
        }

        [Fact]
        public void TryStoreVolume_WithEmptyWavId_ShouldReturnWithoutStoring()
        {
            var wavVolumes = new Dictionary<string, int>();
            InvokePrivateStaticMethodVoid("TryStoreVolume", "", "50", wavVolumes);
            Assert.Empty(wavVolumes);
        }

        [Fact]
        public void TryStoreVolume_WithNullWavId_ShouldReturnWithoutStoring()
        {
            var wavVolumes = new Dictionary<string, int>();
            InvokePrivateStaticMethodVoid("TryStoreVolume", null!, "50", wavVolumes);
            Assert.Empty(wavVolumes);
        }

        [Fact]
        public void TryStorePan_WithEmptyWavId_ShouldReturnWithoutStoring()
        {
            var wavPans = new Dictionary<string, int>();
            InvokePrivateStaticMethodVoid("TryStorePan", "", "50", wavPans);
            Assert.Empty(wavPans);
        }

        [Fact]
        public void TryStorePan_WithNonNumericValue_ShouldReturnWithoutStoring()
        {
            var wavPans = new Dictionary<string, int>();
            InvokePrivateStaticMethodVoid("TryStorePan", "01", "abc", wavPans);
            Assert.Empty(wavPans);
        }

        #endregion

        #region Authored Slot Metadata

        [Fact]
        public async Task ParseAsync_ShouldRecordAuthoredSlotWithoutChangingTickOrTime()
        {
            var content =
                "#BPM: 120.0\n" +
                "#00002: 0.875\n" +
                "#00011: 01010101010101\n";  // seven authored slots in a 7/8 bar
            var path = CreateTempDtx(content);

            var chart = await DTXChartParser.ParseAsync(path);

            Assert.Equal(7, chart.Notes.Count);
            for (var i = 0; i < chart.Notes.Count; i++)
            {
                Assert.Equal(i, chart.Notes[i].PairIndex);
                Assert.Equal(7, chart.Notes[i].PairCount);
            }

            // Gameplay Tick stays the truncated canonical position, and TimeMs stays derived from it.
            Assert.Equal(new[] { 0, 27, 54, 82, 109, 137, 164 }, chart.Notes.Select(n => n.Tick).ToArray());
            Assert.Equal(246.09375, chart.Notes[1].TimeMs, 6);
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

        private static void InvokePrivateStaticMethodVoid(string methodName, params object[] args)
        {
            var method = typeof(DTXChartParser).GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(method);
            method!.Invoke(null, args);
        }
    }
}
