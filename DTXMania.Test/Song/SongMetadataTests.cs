using System;
using System.Collections.Generic;
using DTX.Song;
using Xunit;

namespace DTXMania.Test.Song
{
    /// <summary>
    /// Unit tests for SongMetadata class
    /// Tests metadata handling, calculated properties, and helper methods
    /// </summary>
    public class SongMetadataTests
    {
        #region Basic Property Tests

        [Fact]
        public void Constructor_ShouldInitializeWithDefaults()
        {
            // Arrange & Act
            var metadata = new SongMetadata();

            // Assert
            Assert.Equal("", metadata.Title);
            Assert.Equal("", metadata.Artist);
            Assert.Equal("", metadata.Genre);
            Assert.Equal("", metadata.Comment);
            Assert.Equal("", metadata.FilePath);
            Assert.Equal("", metadata.FileFormat);
            Assert.Null(metadata.BPM);
            Assert.Null(metadata.DrumLevel);
            Assert.Null(metadata.GuitarLevel);
            Assert.Null(metadata.BassLevel);
            Assert.NotNull(metadata.DifficultyLabels);
            Assert.Empty(metadata.DifficultyLabels);
        }

        [Theory]
        [InlineData("Test Song", "Test Artist", "Test Genre")]
        [InlineData("", "", "")]
        [InlineData("日本語タイトル", "日本語アーティスト", "J-POP")]
        public void BasicProperties_ShouldSetAndGetCorrectly(string title, string artist, string genre)
        {
            // Arrange
            var metadata = new SongMetadata();

            // Act
            metadata.Title = title;
            metadata.Artist = artist;
            metadata.Genre = genre;

            // Assert
            Assert.Equal(title, metadata.Title);
            Assert.Equal(artist, metadata.Artist);
            Assert.Equal(genre, metadata.Genre);
        }

        #endregion

        #region Difficulty Level Tests

        [Theory]
        [InlineData(85, 78, 65)]
        [InlineData(0, 0, 0)]
        [InlineData(100, 100, 100)]
        [InlineData(null, null, null)]
        public void DifficultyLevels_ShouldSetAndGetCorrectly(int? drumLevel, int? guitarLevel, int? bassLevel)
        {
            // Arrange
            var metadata = new SongMetadata();

            // Act
            metadata.DrumLevel = drumLevel;
            metadata.GuitarLevel = guitarLevel;
            metadata.BassLevel = bassLevel;

            // Assert
            Assert.Equal(drumLevel, metadata.DrumLevel);
            Assert.Equal(guitarLevel, metadata.GuitarLevel);
            Assert.Equal(bassLevel, metadata.BassLevel);
        }

        [Theory]
        [InlineData("DRUMS", 85)]
        [InlineData("GUITAR", 78)]
        [InlineData("BASS", 65)]
        [InlineData("drums", 85)] // Case insensitive
        [InlineData("guitar", 78)]
        [InlineData("bass", 65)]
        public void GetDifficultyLevel_ShouldReturnCorrectValue(string instrument, int expectedLevel)
        {
            // Arrange
            var metadata = new SongMetadata
            {
                DrumLevel = 85,
                GuitarLevel = 78,
                BassLevel = 65
            };

            // Act
            var result = metadata.GetDifficultyLevel(instrument);

            // Assert
            Assert.Equal(expectedLevel, result);
        }

        [Theory]
        [InlineData("INVALID")]
        [InlineData("")]
        [InlineData(null)]
        public void GetDifficultyLevel_WithInvalidInstrument_ShouldReturnNull(string instrument)
        {
            // Arrange
            var metadata = new SongMetadata
            {
                DrumLevel = 85,
                GuitarLevel = 78,
                BassLevel = 65
            };

            // Act
            var result = metadata.GetDifficultyLevel(instrument);

            // Assert
            Assert.Null(result);
        }

        [Theory]
        [InlineData("DRUMS", 90)]
        [InlineData("GUITAR", 85)]
        [InlineData("BASS", 80)]
        public void SetDifficultyLevel_ShouldUpdateCorrectProperty(string instrument, int level)
        {
            // Arrange
            var metadata = new SongMetadata();

            // Act
            metadata.SetDifficultyLevel(instrument, level);

            // Assert
            var result = metadata.GetDifficultyLevel(instrument);
            Assert.Equal(level, result);
        }

        #endregion

        #region Calculated Properties Tests

        [Theory]
        [InlineData(85, 78, 65, 85)]
        [InlineData(null, 78, 65, 78)]
        [InlineData(null, null, 65, 65)]
        [InlineData(null, null, null, 0)]
        [InlineData(50, 75, 100, 100)]
        public void MaxDifficultyLevel_ShouldReturnHighestValue(int? drumLevel, int? guitarLevel, int? bassLevel, int expected)
        {
            // Arrange
            var metadata = new SongMetadata
            {
                DrumLevel = drumLevel,
                GuitarLevel = guitarLevel,
                BassLevel = bassLevel
            };

            // Act
            var result = metadata.MaxDifficultyLevel;

            // Assert
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(85, 78, 65, new[] { "DRUMS", "GUITAR", "BASS" })]
        [InlineData(85, null, 65, new[] { "DRUMS", "BASS" })]
        [InlineData(null, null, null, new string[0])]
        [InlineData(0, 0, 0, new string[0])] // Zero levels don't count as available
        public void AvailableInstruments_ShouldReturnCorrectList(int? drumLevel, int? guitarLevel, int? bassLevel, string[] expected)
        {
            // Arrange
            var metadata = new SongMetadata
            {
                DrumLevel = drumLevel,
                GuitarLevel = guitarLevel,
                BassLevel = bassLevel
            };

            // Act
            var result = metadata.AvailableInstruments;

            // Assert
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("Test Song", "Test Song")]
        [InlineData("", "test")]
        [InlineData(null, "test")]
        public void DisplayTitle_ShouldReturnTitleOrFilename(string title, string expected)
        {
            // Arrange
            var metadata = new SongMetadata
            {
                Title = title,
                FilePath = System.IO.Path.Combine("Songs", "test.dtx")
            };

            // Act
            var result = metadata.DisplayTitle;

            // Assert
            Assert.Equal(expected, result);
        }

        [Fact]
        public void DisplayTitle_WithEmptyFilePath_ShouldReturnUnknownSong()
        {
            // Arrange
            var metadata = new SongMetadata
            {
                Title = "",
                FilePath = ""
            };

            // Act
            var result = metadata.DisplayTitle;

            // Assert
            Assert.Equal("Unknown Song", result);
        }

        [Theory]
        [InlineData("Test Artist", "Test Artist")]
        [InlineData("", "Unknown Artist")]
        [InlineData(null, "Unknown Artist")]
        public void DisplayArtist_ShouldReturnArtistOrDefault(string artist, string expected)
        {
            // Arrange
            var metadata = new SongMetadata { Artist = artist };

            // Act
            var result = metadata.DisplayArtist;

            // Assert
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("Rock", "Rock")]
        [InlineData("", "Unknown Genre")]
        [InlineData(null, "Unknown Genre")]
        public void DisplayGenre_ShouldReturnGenreOrDefault(string genre, string expected)
        {
            // Arrange
            var metadata = new SongMetadata { Genre = genre };

            // Act
            var result = metadata.DisplayGenre;

            // Assert
            Assert.Equal(expected, result);
        }

        #endregion

        #region Clone Tests

        [Fact]
        public void Clone_ShouldCreateDeepCopy()
        {
            // Arrange
            var original = new SongMetadata
            {
                Title = "Test Song",
                Artist = "Test Artist",
                Genre = "Test Genre",
                Comment = "Test Comment",
                BPM = 120.5,
                Duration = 180.5,
                DrumLevel = 85,
                GuitarLevel = 78,
                BassLevel = 65,
                DrumNoteCount = 1250,
                GuitarNoteCount = 890,
                BassNoteCount = 650,
                PreviewFile = "preview.ogg",
                FilePath = @"C:\Songs\test.dtx",
                FileSize = 1024,
                FileFormat = ".dtx"
            };
            original.DifficultyLabels["DRUMS"] = "EXTREME";

            // Act
            var clone = original.Clone();

            // Assert
            Assert.NotSame(original, clone);
            Assert.Equal(original.Title, clone.Title);
            Assert.Equal(original.Artist, clone.Artist);
            Assert.Equal(original.Genre, clone.Genre);
            Assert.Equal(original.Comment, clone.Comment);
            Assert.Equal(original.BPM, clone.BPM);
            Assert.Equal(original.Duration, clone.Duration);
            Assert.Equal(original.DrumLevel, clone.DrumLevel);
            Assert.Equal(original.GuitarLevel, clone.GuitarLevel);
            Assert.Equal(original.BassLevel, clone.BassLevel);
            Assert.Equal(original.DrumNoteCount, clone.DrumNoteCount);
            Assert.Equal(original.GuitarNoteCount, clone.GuitarNoteCount);
            Assert.Equal(original.BassNoteCount, clone.BassNoteCount);
            Assert.Equal(original.PreviewFile, clone.PreviewFile);
            Assert.Equal(original.FilePath, clone.FilePath);
            Assert.Equal(original.FileSize, clone.FileSize);
            Assert.Equal(original.FileFormat, clone.FileFormat);
            
            // Verify deep copy of dictionary
            Assert.NotSame(original.DifficultyLabels, clone.DifficultyLabels);
            Assert.Equal(original.DifficultyLabels["DRUMS"], clone.DifficultyLabels["DRUMS"]);
        }

        [Fact]
        public void Clone_ModifyingClone_ShouldNotAffectOriginal()
        {
            // Arrange
            var original = new SongMetadata { Title = "Original Title" };
            var clone = original.Clone();

            // Act
            clone.Title = "Modified Title";
            clone.DifficultyLabels["DRUMS"] = "EXTREME";

            // Assert
            Assert.Equal("Original Title", original.Title);
            Assert.Equal("Modified Title", clone.Title);
            Assert.False(original.DifficultyLabels.ContainsKey("DRUMS"));
            Assert.True(clone.DifficultyLabels.ContainsKey("DRUMS"));
        }

        #endregion

        #region Phase 5 Enhancement Tests

        [Theory]
        [InlineData(0, "--:--")]
        [InlineData(60, "01:00")]
        [InlineData(125, "02:05")]
        [InlineData(3661, "61:01")]
        public void FormattedDuration_ShouldReturnCorrectFormat(double seconds, string expected)
        {
            // Arrange
            var metadata = new SongMetadata { Duration = seconds };

            // Act
            var result = metadata.FormattedDuration;

            // Assert
            Assert.Equal(expected, result);
        }

        [Fact]
        public void FormattedDuration_WithNullDuration_ShouldReturnDashes()
        {
            // Arrange
            var metadata = new SongMetadata { Duration = null };

            // Act
            var result = metadata.FormattedDuration;

            // Assert
            Assert.Equal("--:--", result);
        }

        [Theory]
        [InlineData("DRUMS", 1250)]
        [InlineData("GUITAR", 890)]
        [InlineData("BASS", 650)]
        [InlineData("INVALID", null)]
        public void GetNoteCount_ShouldReturnCorrectValue(string instrument, int? expected)
        {
            // Arrange
            var metadata = new SongMetadata
            {
                DrumNoteCount = 1250,
                GuitarNoteCount = 890,
                BassNoteCount = 650
            };

            // Act
            var result = metadata.GetNoteCount(instrument);

            // Assert
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("DRUMS", 1500)]
        [InlineData("GUITAR", 1200)]
        [InlineData("BASS", 800)]
        public void SetNoteCount_ShouldSetCorrectValue(string instrument, int count)
        {
            // Arrange
            var metadata = new SongMetadata();

            // Act
            metadata.SetNoteCount(instrument, count);

            // Assert
            var result = metadata.GetNoteCount(instrument);
            Assert.Equal(count, result);
        }

        [Fact]
        public void TotalNoteCount_ShouldSumAllInstruments()
        {
            // Arrange
            var metadata = new SongMetadata
            {
                DrumNoteCount = 1250,
                GuitarNoteCount = 890,
                BassNoteCount = 650
            };

            // Act
            var result = metadata.TotalNoteCount;

            // Assert
            Assert.Equal(2790, result);
        }

        [Fact]
        public void TotalNoteCount_WithNullValues_ShouldTreatAsZero()
        {
            // Arrange
            var metadata = new SongMetadata
            {
                DrumNoteCount = 1250,
                GuitarNoteCount = null,
                BassNoteCount = 650
            };

            // Act
            var result = metadata.TotalNoteCount;

            // Assert
            Assert.Equal(1900, result);
        }

        [Fact]
        public void TotalNoteCount_WithAllNullValues_ShouldReturnZero()
        {
            // Arrange
            var metadata = new SongMetadata();

            // Act
            var result = metadata.TotalNoteCount;

            // Assert
            Assert.Equal(0, result);
        }

        #endregion
    }
}
