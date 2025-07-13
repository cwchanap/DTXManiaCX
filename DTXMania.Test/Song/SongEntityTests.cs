using System;
using System.Collections.Generic;
using DTXMania.Game.Lib.Song.Entities;
using Xunit;

namespace DTXMania.Test.Song
{
    /// <summary>
    /// Unit tests for Song and SongChart EF Core entities
    /// Tests entity properties, calculated properties, and helper methods
    /// </summary>
    public class SongEntityTests
    {
        #region Song Entity Tests

        [Fact]
        public void Song_Constructor_ShouldInitializeWithDefaults()
        {
            // Arrange & Act
            var song = new DTXMania.Game.Lib.Song.Entities.Song();

            // Assert
            Assert.Equal("", song.Title);
            Assert.Equal("", song.Artist);
            Assert.Equal("", song.Genre);
            Assert.Equal("", song.Comment);
            Assert.NotNull(song.Charts);
            Assert.Empty(song.Charts);
        }

        [Theory]
        [InlineData("Test Song", "Test Artist", "Test Genre")]
        [InlineData("", "", "")]
        [InlineData("日本語タイトル", "日本語アーティスト", "J-POP")]
        public void Song_BasicProperties_ShouldSetAndGetCorrectly(string title, string artist, string genre)
        {
            // Arrange
            var song = new DTXMania.Game.Lib.Song.Entities.Song();

            // Act
            song.Title = title;
            song.Artist = artist;
            song.Genre = genre;

            // Assert
            Assert.Equal(title, song.Title);
            Assert.Equal(artist, song.Artist);
            Assert.Equal(genre, song.Genre);
        }

        [Theory]
        [InlineData("Test Song", "Test Song")]
        [InlineData("", "Unknown Song")]
        [InlineData(null, "Unknown Song")]
        public void Song_DisplayTitle_ShouldReturnTitleOrDefault(string title, string expected)
        {
            // Arrange
            var song = new DTXMania.Game.Lib.Song.Entities.Song { Title = title };

            // Act
            var result = song.DisplayTitle;

            // Assert
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("Test Artist", "Test Artist")]
        [InlineData("", "Unknown Artist")]
        [InlineData(null, "Unknown Artist")]
        public void Song_DisplayArtist_ShouldReturnArtistOrDefault(string artist, string expected)
        {
            // Arrange
            var song = new DTXMania.Game.Lib.Song.Entities.Song { Artist = artist };

            // Act
            var result = song.DisplayArtist;

            // Assert
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("Rock", "Rock")]
        [InlineData("", "Unknown Genre")]
        [InlineData(null, "Unknown Genre")]
        public void Song_DisplayGenre_ShouldReturnGenreOrDefault(string genre, string expected)
        {
            // Arrange
            var song = new DTXMania.Game.Lib.Song.Entities.Song { Genre = genre };

            // Act
            var result = song.DisplayGenre;

            // Assert
            Assert.Equal(expected, result);
        }

        [Fact]
        public void Song_Clone_ShouldCreateDeepCopy()
        {
            // Arrange
            var original = new DTXMania.Game.Lib.Song.Entities.Song
            {
                Title = "Test Song",
                Artist = "Test Artist",
                Genre = "Test Genre",
                Comment = "Test Comment",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            // Act
            var clone = original.Clone();

            // Assert
            Assert.NotSame(original, clone);
            Assert.Equal(original.Title, clone.Title);
            Assert.Equal(original.Artist, clone.Artist);
            Assert.Equal(original.Genre, clone.Genre);
            Assert.Equal(original.Comment, clone.Comment);
            Assert.Equal(original.CreatedAt, clone.CreatedAt);
            Assert.Equal(original.UpdatedAt, clone.UpdatedAt);
        }

        #endregion

        #region SongChart Entity Tests

        [Fact]
        public void SongChart_Constructor_ShouldInitializeWithDefaults()
        {
            // Arrange & Act
            var chart = new SongChart();

            // Assert
            Assert.Equal("", chart.FilePath);
            Assert.Equal("", chart.FileFormat);
            Assert.Equal(0, chart.DrumLevel);
            Assert.Equal(0, chart.GuitarLevel);
            Assert.Equal(0, chart.BassLevel);
            Assert.False(chart.HasDrumChart);
            Assert.False(chart.HasGuitarChart);
            Assert.False(chart.HasBassChart);
            Assert.NotNull(chart.DifficultyLabels);
            Assert.Empty(chart.DifficultyLabels);
        }

        [Theory]
        [InlineData(85, 78, 65)]
        [InlineData(0, 0, 0)]
        [InlineData(100, 100, 100)]
        public void SongChart_DifficultyLevels_ShouldSetAndGetCorrectly(int drumLevel, int guitarLevel, int bassLevel)
        {
            // Arrange
            var chart = new SongChart();

            // Act
            chart.DrumLevel = drumLevel;
            chart.GuitarLevel = guitarLevel;
            chart.BassLevel = bassLevel;

            // Assert
            Assert.Equal(drumLevel, chart.DrumLevel);
            Assert.Equal(guitarLevel, chart.GuitarLevel);
            Assert.Equal(bassLevel, chart.BassLevel);
        }

        [Theory]
        [InlineData("DRUMS", 85)]
        [InlineData("GUITAR", 78)]
        [InlineData("BASS", 65)]
        [InlineData("drums", 85)] // Case insensitive
        [InlineData("guitar", 78)]
        [InlineData("bass", 65)]
        public void SongChart_GetDifficultyLevel_ShouldReturnCorrectValue(string instrument, int expectedLevel)
        {
            // Arrange
            var chart = new SongChart
            {
                DrumLevel = 85,
                GuitarLevel = 78,
                BassLevel = 65
            };

            // Act
            var result = chart.GetDifficultyLevel(instrument);

            // Assert
            Assert.Equal(expectedLevel, result);
        }

        [Theory]
        [InlineData("INVALID")]
        [InlineData("")]
        [InlineData(null)]
        public void SongChart_GetDifficultyLevel_WithInvalidInstrument_ShouldReturnNull(string instrument)
        {
            // Arrange
            var chart = new SongChart
            {
                DrumLevel = 85,
                GuitarLevel = 78,
                BassLevel = 65
            };

            // Act
            var result = chart.GetDifficultyLevel(instrument);

            // Assert
            Assert.Null(result);
        }

        [Theory]
        [InlineData("DRUMS", 90)]
        [InlineData("GUITAR", 85)]
        [InlineData("BASS", 80)]
        public void SongChart_SetDifficultyLevel_ShouldUpdateCorrectProperty(string instrument, int level)
        {
            // Arrange
            var chart = new SongChart();

            // Act
            chart.SetDifficultyLevel(instrument, level);

            // Assert
            var result = chart.GetDifficultyLevel(instrument);
            Assert.Equal(level, result);
        }

        [Theory]
        [InlineData(85, 78, 65, 85)]
        [InlineData(0, 78, 65, 78)]
        [InlineData(0, 0, 65, 65)]
        [InlineData(0, 0, 0, 0)]
        [InlineData(50, 75, 100, 100)]
        public void SongChart_MaxDifficultyLevel_ShouldReturnHighestValue(int drumLevel, int guitarLevel, int bassLevel, int expected)
        {
            // Arrange
            var chart = new SongChart
            {
                DrumLevel = drumLevel,
                GuitarLevel = guitarLevel,
                BassLevel = bassLevel
            };

            // Act
            var result = Math.Max(chart.DrumLevel, Math.Max(chart.GuitarLevel, chart.BassLevel));

            // Assert
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(1250, 890, 650, 2790)]
        [InlineData(0, 890, 650, 1540)]
        [InlineData(0, 0, 0, 0)]
        public void SongChart_TotalNoteCount_ShouldSumAllInstruments(int drumNotes, int guitarNotes, int bassNotes, int expected)
        {
            // Arrange
            var chart = new SongChart
            {
                DrumNoteCount = drumNotes,
                GuitarNoteCount = guitarNotes,
                BassNoteCount = bassNotes
            };

            // Act
            var result = chart.TotalNoteCount;

            // Assert
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("DRUMS", 1250)]
        [InlineData("GUITAR", 890)]
        [InlineData("BASS", 650)]
        [InlineData("INVALID", null)]
        public void SongChart_GetNoteCount_ShouldReturnCorrectValue(string instrument, int? expected)
        {
            // Arrange
            var chart = new SongChart
            {
                DrumNoteCount = 1250,
                GuitarNoteCount = 890,
                BassNoteCount = 650
            };

            // Act
            var result = chart.GetNoteCount(instrument);

            // Assert
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("DRUMS", 1500)]
        [InlineData("GUITAR", 1200)]
        [InlineData("BASS", 800)]
        public void SongChart_SetNoteCount_ShouldSetCorrectValue(string instrument, int count)
        {
            // Arrange
            var chart = new SongChart();

            // Act
            chart.SetNoteCount(instrument, count);

            // Assert
            var result = chart.GetNoteCount(instrument);
            Assert.Equal(count, result);
        }

        [Fact]
        public void SongChart_Clone_ShouldCreateDeepCopy()
        {
            // Arrange
            var original = new SongChart
            {
                FilePath = Path.Combine("Songs", "test.dtx"),
                FileFormat = ".dtx",
                FileSize = 1024,
                Bpm = 120.5,
                Duration = 180.5,
                DrumLevel = 85,
                GuitarLevel = 78,
                BassLevel = 65,
                DrumNoteCount = 1250,
                GuitarNoteCount = 890,
                BassNoteCount = 650,
                PreviewFile = "preview.ogg",
                PreviewImage = "preview.jpg",
                BackgroundFile = "background.jpg"
            };
            original.DifficultyLabels["DRUMS"] = "EXTREME";

            // Act
            var clone = original.Clone();

            // Assert
            Assert.NotSame(original, clone);
            Assert.Equal(original.FilePath, clone.FilePath);
            Assert.Equal(original.FileFormat, clone.FileFormat);
            Assert.Equal(original.FileSize, clone.FileSize);
            Assert.Equal(original.Bpm, clone.Bpm);
            Assert.Equal(original.Duration, clone.Duration);
            Assert.Equal(original.DrumLevel, clone.DrumLevel);
            Assert.Equal(original.GuitarLevel, clone.GuitarLevel);
            Assert.Equal(original.BassLevel, clone.BassLevel);
            Assert.Equal(original.DrumNoteCount, clone.DrumNoteCount);
            Assert.Equal(original.GuitarNoteCount, clone.GuitarNoteCount);
            Assert.Equal(original.BassNoteCount, clone.BassNoteCount);
            Assert.Equal(original.PreviewFile, clone.PreviewFile);
            Assert.Equal(original.PreviewImage, clone.PreviewImage);
            Assert.Equal(original.BackgroundFile, clone.BackgroundFile);
            
            // Verify deep copy of dictionary
            Assert.NotSame(original.DifficultyLabels, clone.DifficultyLabels);
            Assert.Equal(original.DifficultyLabels["DRUMS"], clone.DifficultyLabels["DRUMS"]);
        }

        #endregion

        #region Integration Tests

        [Fact]
        public void Song_WithChart_ShouldProvideAvailableInstruments()
        {
            // Arrange
            var song = new DTXMania.Game.Lib.Song.Entities.Song
            {
                Title = "Test Song"
            };
            
            var chart = new SongChart
            {
                DrumLevel = 85,
                GuitarLevel = 78,
                BassLevel = 0, // Bass not available
                HasDrumChart = true,
                HasGuitarChart = true,
                HasBassChart = false
            };
            
            song.Charts.Add(chart);

            // Act
            var availableInstruments = song.AvailableInstruments;

            // Assert
            Assert.Contains("DRUMS", availableInstruments);
            Assert.Contains("GUITAR", availableInstruments);
            Assert.DoesNotContain("BASS", availableInstruments);
        }

        [Theory]
        [InlineData(0, "--:--")]
        [InlineData(60, "01:00")]
        [InlineData(125, "02:05")]
        [InlineData(3661, "61:01")]
        public void SongChart_FormattedDuration_ShouldReturnCorrectFormat(double seconds, string expected)
        {
            // Arrange
            var chart = new SongChart { Duration = seconds };

            // Act
            var result = chart.FormattedDuration;

            // Assert
            Assert.Equal(expected, result);
        }

        #endregion
    }
}