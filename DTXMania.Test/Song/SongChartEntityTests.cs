using System.Collections.Generic;
using DTXMania.Game.Lib.Song.Entities;
using Xunit;

namespace DTXMania.Test.Song
{
    /// <summary>
    /// Extended tests for Song and SongChart entity methods
    /// Covers uncovered properties and methods in these entity classes
    /// </summary>
    public class SongChartEntityTests
    {
        #region SongChart.FormattedDuration

        [Fact]
        public void FormattedDuration_ZeroDuration_ShouldReturnPlaceholder()
        {
            var chart = new SongChart { Duration = 0 };
            Assert.Equal("--:--", chart.FormattedDuration);
        }

        [Fact]
        public void FormattedDuration_NegativeDuration_ShouldReturnPlaceholder()
        {
            var chart = new SongChart { Duration = -5 };
            Assert.Equal("--:--", chart.FormattedDuration);
        }

        [Fact]
        public void FormattedDuration_60Seconds_ShouldReturnOneMinute()
        {
            var chart = new SongChart { Duration = 60 };
            Assert.Equal("01:00", chart.FormattedDuration);
        }

        [Fact]
        public void FormattedDuration_90Seconds_ShouldReturnOneMinuteThirty()
        {
            var chart = new SongChart { Duration = 90 };
            Assert.Equal("01:30", chart.FormattedDuration);
        }

        [Fact]
        public void FormattedDuration_3661Seconds_ShouldFormatCorrectly()
        {
            var chart = new SongChart { Duration = 3661 };
            Assert.Equal("61:01", chart.FormattedDuration);
        }

        #endregion

        #region SongChart.TotalNoteCount

        [Fact]
        public void TotalNoteCount_SumsAllInstruments()
        {
            var chart = new SongChart
            {
                DrumNoteCount = 100,
                GuitarNoteCount = 50,
                BassNoteCount = 30
            };
            Assert.Equal(180, chart.TotalNoteCount);
        }

        [Fact]
        public void TotalNoteCount_NoNotes_ReturnsZero()
        {
            var chart = new SongChart();
            Assert.Equal(0, chart.TotalNoteCount);
        }

        #endregion

        #region SongChart.HasAnyNotes

        [Fact]
        public void HasAnyNotes_WithDrumNotes_ShouldReturnTrue()
        {
            var chart = new SongChart { DrumNoteCount = 100 };
            Assert.True(chart.HasAnyNotes());
        }

        [Fact]
        public void HasAnyNotes_WithNoNotes_ShouldReturnFalse()
        {
            var chart = new SongChart();
            Assert.False(chart.HasAnyNotes());
        }

        #endregion

        #region SongChart.GetFormattedNoteCount

        [Fact]
        public void GetFormattedNoteCount_NoNotes_ShouldReturnNoNotes()
        {
            var chart = new SongChart();
            Assert.Equal("No notes", chart.GetFormattedNoteCount());
        }

        [Fact]
        public void GetFormattedNoteCount_WithNotes_NoBreakdown_ShouldReturnTotal()
        {
            var chart = new SongChart { DrumNoteCount = 500 };
            var result = chart.GetFormattedNoteCount(false);
            Assert.Contains("500", result);
            Assert.Contains("notes", result);
        }

        [Fact]
        public void GetFormattedNoteCount_WithDrumNotes_WithBreakdown_ShouldIncludeDrumPrefix()
        {
            var chart = new SongChart { DrumNoteCount = 200, GuitarNoteCount = 0, BassNoteCount = 0 };
            var result = chart.GetFormattedNoteCount(true);
            Assert.Contains("D:200", result);
        }

        [Fact]
        public void GetFormattedNoteCount_AllInstruments_WithBreakdown_ShouldIncludeAll()
        {
            var chart = new SongChart { DrumNoteCount = 100, GuitarNoteCount = 50, BassNoteCount = 30 };
            var result = chart.GetFormattedNoteCount(true);
            Assert.Contains("D:100", result);
            Assert.Contains("G:50", result);
            Assert.Contains("B:30", result);
            Assert.Contains("180", result); // Total
        }

        #endregion

        #region SongChart.GetInstrumentNoteCount (string)

        [Theory]
        [InlineData("DRUMS", 100)]
        [InlineData("GUITAR", 50)]
        [InlineData("BASS", 30)]
        [InlineData("drums", 100)] // case insensitive
        [InlineData("Guitar", 50)]
        public void GetInstrumentNoteCount_ByString_ShouldReturnCorrectCount(string instrument, int expected)
        {
            var chart = new SongChart
            {
                DrumNoteCount = 100,
                GuitarNoteCount = 50,
                BassNoteCount = 30
            };
            Assert.Equal(expected, chart.GetInstrumentNoteCount(instrument));
        }

        [Fact]
        public void GetInstrumentNoteCount_UnknownInstrument_ShouldReturnZero()
        {
            var chart = new SongChart { DrumNoteCount = 100 };
            Assert.Equal(0, chart.GetInstrumentNoteCount("INVALID"));
        }

        [Fact]
        public void GetInstrumentNoteCount_NullInstrument_ShouldReturnZero()
        {
            var chart = new SongChart { DrumNoteCount = 100 };
            Assert.Equal(0, chart.GetInstrumentNoteCount((string)null));
        }

        #endregion

        #region SongChart.GetInstrumentNoteCount (enum)

        [Theory]
        [InlineData(EInstrumentPart.DRUMS, 100)]
        [InlineData(EInstrumentPart.GUITAR, 50)]
        [InlineData(EInstrumentPart.BASS, 30)]
        public void GetInstrumentNoteCount_ByEnum_ShouldReturnCorrectCount(EInstrumentPart part, int expected)
        {
            var chart = new SongChart
            {
                DrumNoteCount = 100,
                GuitarNoteCount = 50,
                BassNoteCount = 30
            };
            Assert.Equal(expected, chart.GetInstrumentNoteCount(part));
        }

        [Fact]
        public void GetInstrumentNoteCount_InvalidEnum_ShouldReturnZero()
        {
            var chart = new SongChart { DrumNoteCount = 100 };
            Assert.Equal(0, chart.GetInstrumentNoteCount((EInstrumentPart)999));
        }

        #endregion

        #region SongChart.GetNoteCountStats

        [Fact]
        public void GetNoteCountStats_ShouldReturnAllValues()
        {
            var chart = new SongChart
            {
                DrumNoteCount = 100,
                GuitarNoteCount = 50,
                BassNoteCount = 30
            };
            var (total, drums, guitar, bass) = chart.GetNoteCountStats();
            Assert.Equal(180, total);
            Assert.Equal(100, drums);
            Assert.Equal(50, guitar);
            Assert.Equal(30, bass);
        }

        #endregion

        #region SongChart.GetDifficultyLevel

        [Theory]
        [InlineData("DRUMS", 85)]
        [InlineData("GUITAR", 78)]
        [InlineData("BASS", 65)]
        public void GetDifficultyLevel_ValidInstruments_ShouldReturnCorrectLevel(string instrument, int expected)
        {
            var chart = new SongChart { DrumLevel = 85, GuitarLevel = 78, BassLevel = 65 };
            Assert.Equal(expected, chart.GetDifficultyLevel(instrument));
        }

        [Fact]
        public void GetDifficultyLevel_ZeroLevel_ShouldReturnNull()
        {
            var chart = new SongChart { DrumLevel = 0 };
            Assert.Null(chart.GetDifficultyLevel("DRUMS"));
        }

        #endregion

        #region SongChart.SetDifficultyLevel and SetNoteCount

        [Fact]
        public void SetDifficultyLevel_Drums_ShouldSetLevelAndFlag()
        {
            var chart = new SongChart();
            chart.SetDifficultyLevel("DRUMS", 80);
            Assert.Equal(80, chart.DrumLevel);
            Assert.True(chart.HasDrumChart);
        }

        [Fact]
        public void SetDifficultyLevel_Guitar_ShouldSetLevelAndFlag()
        {
            var chart = new SongChart();
            chart.SetDifficultyLevel("GUITAR", 75);
            Assert.Equal(75, chart.GuitarLevel);
            Assert.True(chart.HasGuitarChart);
        }

        [Fact]
        public void SetDifficultyLevel_Bass_ShouldSetLevelAndFlag()
        {
            var chart = new SongChart();
            chart.SetDifficultyLevel("BASS", 60);
            Assert.Equal(60, chart.BassLevel);
            Assert.True(chart.HasBassChart);
        }

        [Fact]
        public void SetDifficultyLevel_Zero_ShouldClearFlag()
        {
            var chart = new SongChart { DrumLevel = 85, HasDrumChart = true };
            chart.SetDifficultyLevel("DRUMS", 0);
            Assert.Equal(0, chart.DrumLevel);
            Assert.False(chart.HasDrumChart);
        }

        [Fact]
        public void SetNoteCount_Drums_ShouldSetCount()
        {
            var chart = new SongChart();
            chart.SetNoteCount("DRUMS", 300);
            Assert.Equal(300, chart.DrumNoteCount);
        }

        [Fact]
        public void SetNoteCount_Guitar_ShouldSetCount()
        {
            var chart = new SongChart();
            chart.SetNoteCount("GUITAR", 200);
            Assert.Equal(200, chart.GuitarNoteCount);
        }

        [Fact]
        public void SetNoteCount_Bass_ShouldSetCount()
        {
            var chart = new SongChart();
            chart.SetNoteCount("BASS", 150);
            Assert.Equal(150, chart.BassNoteCount);
        }

        [Fact]
        public void GetNoteCount_ValidInstruments_ShouldReturnCorrectValues()
        {
            var chart = new SongChart { DrumNoteCount = 100, GuitarNoteCount = 50, BassNoteCount = 0 };
            Assert.Equal(100, chart.GetNoteCount("DRUMS"));
            Assert.Equal(50, chart.GetNoteCount("GUITAR"));
            Assert.Null(chart.GetNoteCount("BASS")); // 0 returns null
        }

        #endregion

        #region SongChart.Clone

        [Fact]
        public void Clone_ShouldCopyAllProperties()
        {
            var original = new SongChart
            {
                SongId = 1,
                FilePath = "/test/song.dtx",
                FileHash = "abc123",
                FileSize = 12345,
                DifficultyLevel = 2,
                DifficultyLabel = "Expert",
                Bpm = 180.0,
                Duration = 240.0,
                BGMAdjust = 5,
                DrumLevel = 90,
                GuitarLevel = 85,
                BassLevel = 70,
                HasDrumChart = true,
                HasGuitarChart = true,
                HasBassChart = false,
                DrumNoteCount = 500,
                GuitarNoteCount = 300,
                BassNoteCount = 0,
                PreviewFile = "preview.mp3",
                PreviewImage = "preview.jpg",
                BackgroundFile = "bg.jpg",
                StageFile = "stage.jpg",
                FileFormat = "DTX"
            };

            var clone = original.Clone();

            Assert.Equal(original.SongId, clone.SongId);
            Assert.Equal(original.FilePath, clone.FilePath);
            Assert.Equal(original.FileHash, clone.FileHash);
            Assert.Equal(original.Bpm, clone.Bpm);
            Assert.Equal(original.Duration, clone.Duration);
            Assert.Equal(original.DrumLevel, clone.DrumLevel);
            Assert.Equal(original.HasDrumChart, clone.HasDrumChart);
            Assert.Equal(original.DrumNoteCount, clone.DrumNoteCount);
            Assert.Equal(original.PreviewFile, clone.PreviewFile);
            Assert.NotSame(original, clone);
        }

        #endregion

        #region SongChart Legacy Properties

        [Fact]
        public void BPM_WhenBpmPositive_ShouldReturnValue()
        {
            var chart = new SongChart { Bpm = 180.0 };
            Assert.Equal(180.0, chart.BPM);
        }

        [Fact]
        public void BPM_WhenBpmZero_ShouldReturnNull()
        {
            var chart = new SongChart { Bpm = 0 };
            Assert.Null(chart.BPM);
        }

        [Fact]
        public void BPM_Set_ShouldUpdateBpm()
        {
            var chart = new SongChart();
            chart.BPM = 120.0;
            Assert.Equal(120.0, chart.Bpm);
        }

        [Fact]
        public void BPM_SetNull_ShouldSetBpmToZero()
        {
            var chart = new SongChart { Bpm = 180.0 };
            chart.BPM = null;
            Assert.Equal(0, chart.Bpm);
        }

        [Fact]
        public void BackgroundImage_WithBackgroundFile_ShouldReturnFile()
        {
            var chart = new SongChart { BackgroundFile = "bg.jpg" };
            Assert.Equal("bg.jpg", chart.BackgroundImage);
        }

        [Fact]
        public void BackgroundImage_WithEmptyBackgroundFile_ShouldReturnNull()
        {
            var chart = new SongChart { BackgroundFile = "" };
            Assert.Null(chart.BackgroundImage);
        }

        [Fact]
        public void DrumLevelNullable_WithPositiveLevel_ShouldReturnValue()
        {
            var chart = new SongChart { DrumLevel = 80 };
            Assert.Equal(80, chart.DrumLevelNullable);
        }

        [Fact]
        public void DrumLevelNullable_WithZeroLevel_ShouldReturnNull()
        {
            var chart = new SongChart { DrumLevel = 0 };
            Assert.Null(chart.DrumLevelNullable);
        }

        #endregion
    }

    /// <summary>
    /// Extended tests for Song entity methods
    /// </summary>
    public class SongEntityExtendedTests
    {
        #region Song.DisplayTitle

        [Fact]
        public void DisplayTitle_WithTitle_ShouldReturnTitle()
        {
            var song = new DTXMania.Game.Lib.Song.Entities.Song { Title = "My Song" };
            Assert.Equal("My Song", song.DisplayTitle);
        }

        [Fact]
        public void DisplayTitle_EmptyTitle_WithChartFilePath_ShouldReturnFilename()
        {
            var song = new DTXMania.Game.Lib.Song.Entities.Song
            {
                Title = "",
                Charts = new List<SongChart>
                {
                    new SongChart { FilePath = "/music/awesome_track.dtx" }
                }
            };
            Assert.Equal("awesome_track", song.DisplayTitle);
        }

        [Fact]
        public void DisplayTitle_EmptyTitle_NoCharts_ShouldReturnUnknownSong()
        {
            var song = new DTXMania.Game.Lib.Song.Entities.Song
            {
                Title = "",
                Charts = new List<SongChart>()
            };
            Assert.Equal("Unknown Song", song.DisplayTitle);
        }

        [Fact]
        public void DisplayTitle_NullTitle_ShouldReturnUnknownSong()
        {
            var song = new DTXMania.Game.Lib.Song.Entities.Song { Title = null };
            Assert.Equal("Unknown Song", song.DisplayTitle);
        }

        #endregion

        #region Song.DisplayArtist

        [Fact]
        public void DisplayArtist_WithArtist_ShouldReturnArtist()
        {
            var song = new DTXMania.Game.Lib.Song.Entities.Song { Artist = "Test Artist" };
            Assert.Equal("Test Artist", song.DisplayArtist);
        }

        [Fact]
        public void DisplayArtist_EmptyArtist_ShouldReturnUnknownArtist()
        {
            var song = new DTXMania.Game.Lib.Song.Entities.Song { Artist = "" };
            Assert.Equal("Unknown Artist", song.DisplayArtist);
        }

        [Fact]
        public void DisplayArtist_NullArtist_ShouldReturnUnknownArtist()
        {
            var song = new DTXMania.Game.Lib.Song.Entities.Song { Artist = null };
            Assert.Equal("Unknown Artist", song.DisplayArtist);
        }

        #endregion

        #region Song.DisplayGenre

        [Fact]
        public void DisplayGenre_WithGenre_ShouldReturnGenre()
        {
            var song = new DTXMania.Game.Lib.Song.Entities.Song { Genre = "Rock" };
            Assert.Equal("Rock", song.DisplayGenre);
        }

        [Fact]
        public void DisplayGenre_EmptyGenre_ShouldReturnUnknownGenre()
        {
            var song = new DTXMania.Game.Lib.Song.Entities.Song { Genre = "" };
            Assert.Equal("Unknown Genre", song.DisplayGenre);
        }

        #endregion

        #region Song.MaxDifficultyLevel

        [Fact]
        public void MaxDifficultyLevel_NoCharts_ShouldReturnZero()
        {
            var song = new DTXMania.Game.Lib.Song.Entities.Song();
            Assert.Equal(0, song.MaxDifficultyLevel);
        }

        [Fact]
        public void MaxDifficultyLevel_SingleChartWithDrums_ShouldReturnDrumLevel()
        {
            var song = new DTXMania.Game.Lib.Song.Entities.Song
            {
                Charts = new List<SongChart>
                {
                    new SongChart { DrumLevel = 95, GuitarLevel = 0, BassLevel = 0 }
                }
            };
            Assert.Equal(95, song.MaxDifficultyLevel);
        }

        [Fact]
        public void MaxDifficultyLevel_MultipleCharts_ShouldReturnOverallMax()
        {
            var song = new DTXMania.Game.Lib.Song.Entities.Song
            {
                Charts = new List<SongChart>
                {
                    new SongChart { DrumLevel = 80, GuitarLevel = 90, BassLevel = 70 },
                    new SongChart { DrumLevel = 95, GuitarLevel = 60, BassLevel = 50 }
                }
            };
            Assert.Equal(95, song.MaxDifficultyLevel);
        }

        [Fact]
        public void MaxDifficultyLevel_NullCharts_ShouldReturnZero()
        {
            var song = new DTXMania.Game.Lib.Song.Entities.Song { Charts = null };
            Assert.Equal(0, song.MaxDifficultyLevel);
        }

        #endregion

        #region Song.AvailableInstruments

        [Fact]
        public void AvailableInstruments_DrumAndGuitarChart_ShouldReturnBoth()
        {
            var song = new DTXMania.Game.Lib.Song.Entities.Song
            {
                Charts = new List<SongChart>
                {
                    new SongChart { HasDrumChart = true, DrumLevel = 80, HasGuitarChart = true, GuitarLevel = 70 }
                }
            };
            var instruments = song.AvailableInstruments;
            Assert.Contains("DRUMS", instruments);
            Assert.Contains("GUITAR", instruments);
        }

        [Fact]
        public void AvailableInstruments_NoCharts_ShouldReturnEmpty()
        {
            var song = new DTXMania.Game.Lib.Song.Entities.Song { Charts = new List<SongChart>() };
            Assert.Empty(song.AvailableInstruments);
        }

        [Fact]
        public void AvailableInstruments_NoDuplicates_WhenMultipleChartsSameInstrument()
        {
            var song = new DTXMania.Game.Lib.Song.Entities.Song
            {
                Charts = new List<SongChart>
                {
                    new SongChart { HasDrumChart = true, DrumLevel = 80 },
                    new SongChart { HasDrumChart = true, DrumLevel = 95 }
                }
            };
            var instruments = song.AvailableInstruments;
            Assert.Single(instruments); // DRUMS only, no duplicates
        }

        #endregion

        #region Song.Clone

        [Fact]
        public void Clone_ShouldCopyAllMetadata()
        {
            var original = new DTXMania.Game.Lib.Song.Entities.Song
            {
                Title = "Test Song",
                Artist = "Test Artist",
                Genre = "Rock",
                Comment = "A test song"
            };

            var clone = original.Clone();

            Assert.Equal(original.Title, clone.Title);
            Assert.Equal(original.Artist, clone.Artist);
            Assert.Equal(original.Genre, clone.Genre);
            Assert.Equal(original.Comment, clone.Comment);
            Assert.NotSame(original, clone);
        }

        [Fact]
        public void Clone_ShouldNotCopyCharts()
        {
            var original = new DTXMania.Game.Lib.Song.Entities.Song
            {
                Title = "Test Song",
                Charts = new List<SongChart> { new SongChart { FilePath = "/test.dtx" } }
            };

            var clone = original.Clone();

            Assert.Empty(clone.Charts); // Charts not cloned
        }

        #endregion
    }
}
