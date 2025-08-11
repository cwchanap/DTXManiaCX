using System;
using System.IO;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Song;
using Xunit;
using DTXMania.Game.Lib.Song.Entities;

namespace DTXMania.Test.Song
{
    /// <summary>
    /// Unit tests for DTXChartParser metadata functionality
    /// Tests DTX file parsing and metadata extraction (consolidated from DTXMetadataParser)
    /// </summary>
    public class DTXMetadataParserTests
    {
        #region File Parsing Tests

        [Fact]
        public async Task ParseSongEntitiesAsync_WithNonExistentFile_ShouldThrowFileNotFoundException()
        {
            // Arrange
            var nonExistentFile = Path.Combine(Path.GetTempPath(), "NonExistent", "test.dtx");

            // Act & Assert
            await Assert.ThrowsAsync<FileNotFoundException>(() =>
                DTXChartParser.ParseSongEntitiesAsync(nonExistentFile));
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        public async Task ParseSongEntitiesAsync_WithInvalidPath_ShouldThrowFileNotFoundException(string filePath)
        {
            // Act & Assert
            await Assert.ThrowsAsync<FileNotFoundException>(() =>
                DTXChartParser.ParseSongEntitiesAsync(filePath));
        }

        [Fact]
        public async Task ParseSongEntitiesAsync_WithValidDTXFile_ShouldParseBasicMetadata()
        {
            // Arrange
            var tempFile = Path.GetTempFileName();
            var dtxFile = Path.ChangeExtension(tempFile, ".dtx");
            
            var dtxContent = @"#TITLE: Test Song
#ARTIST: Test Artist
#GENRE: Test Genre
#BPM: 120.5
#DLEVEL: 85
#GLEVEL: 78
#BLEVEL: 65
#COMMENT: This is a test song
#PREIMAGE: preview.ogg

// This is a comment
*01010: 11111111
";

            try
            {
                await File.WriteAllTextAsync(dtxFile, dtxContent);

                // Act
                var (song, chart) = await DTXChartParser.ParseSongEntitiesAsync(dtxFile);

                // Assert
                Assert.Equal("Test Song", song.Title);
                Assert.Equal("Test Artist", song.Artist);
                Assert.Equal("Test Genre", song.Genre);
                Assert.Equal(120.5, chart.BPM);
                Assert.Equal(85, chart.DrumLevel);
                Assert.Equal(78, chart.GuitarLevel);
                Assert.Equal(65, chart.BassLevel);
                Assert.Equal("This is a test song", song.Comment);
                Assert.Equal("preview.ogg", chart.PreviewImage);
                Assert.Equal(dtxFile, chart.FilePath);
            }
            finally
            {
                if (File.Exists(dtxFile))
                    File.Delete(dtxFile);
                if (File.Exists(tempFile))
                    File.Delete(tempFile);
            }
        }

        [Fact]
        public async Task ParseSongEntitiesAsync_WithLevelFormat_ShouldParseCorrectly()
        {
            // Arrange

            var tempFile = Path.GetTempFileName();
            var dtxFile = Path.ChangeExtension(tempFile, ".dtx");
            
            var dtxContent = @"#TITLE: Level Format Test
#LEVEL: DRUMS:90,GUITAR:85,BASS:70
";

            try
            {
                await File.WriteAllTextAsync(dtxFile, dtxContent);

                // Act
                var (song, chart) = await DTXChartParser.ParseSongEntitiesAsync(dtxFile);

                // Assert
                Assert.Equal("Level Format Test", song.Title);
                Assert.Equal(90, chart.DrumLevel);
                Assert.Equal(85, chart.GuitarLevel);
                Assert.Equal(70, chart.BassLevel);
            }
            finally
            {
                if (File.Exists(dtxFile))
                    File.Delete(dtxFile);
                if (File.Exists(tempFile))
                    File.Delete(tempFile);
            }
        }

        [Fact]
        public async Task ParseSongEntitiesAsync_WithQuotedValues_ShouldRemoveQuotes()
        {
            // Arrange
            var tempFile = Path.GetTempFileName();
            var dtxFile = Path.ChangeExtension(tempFile, ".dtx");

            var dtxContent = @"#TITLE: ""Quoted Title""
#ARTIST: ""Quoted Artist""
#GENRE: ""Quoted Genre""
";

            try
            {
                await File.WriteAllTextAsync(dtxFile, dtxContent);

                // Act
                var (song, chart) = await DTXChartParser.ParseSongEntitiesAsync(dtxFile);

                // Assert
                Assert.Equal("Quoted Title", song.Title);
                Assert.Equal("Quoted Artist", song.Artist);
                Assert.Equal("Quoted Genre", song.Genre);
            }
            finally
            {
                if (File.Exists(dtxFile))
                    File.Delete(dtxFile);
                if (File.Exists(tempFile))
                    File.Delete(tempFile);
            }
        }

        [Fact]
        public async Task ParseSongEntitiesAsync_WithEmptyFile_ShouldReturnBasicInfo()
        {
            // Arrange
            var tempFile = Path.GetTempFileName();
            var dtxFile = Path.ChangeExtension(tempFile, ".dtx");

            try
            {
                await File.WriteAllTextAsync(dtxFile, "");

                // Act
                var (song, chart) = await DTXChartParser.ParseSongEntitiesAsync(dtxFile);

                // Assert
                Assert.Equal(Path.GetFileNameWithoutExtension(dtxFile), song.Title);
                Assert.Equal("", song.Artist);
                Assert.Equal("", song.Genre);
                Assert.Null(chart.BPM);
                Assert.Equal(0, chart.DrumLevel);
                Assert.Equal(dtxFile, chart.FilePath);
            }
            finally
            {
                if (File.Exists(dtxFile))
                    File.Delete(dtxFile);
                if (File.Exists(tempFile))
                    File.Delete(tempFile);
            }
        }

        [Fact]
        public async Task ParseSongEntitiesAsync_WithUnsupportedExtension_ShouldReturnBasicInfo()
        {
            // Arrange
            var tempFile = Path.GetTempFileName();
            var txtFile = Path.ChangeExtension(tempFile, ".txt");

            try
            {
                await File.WriteAllTextAsync(txtFile, "#TITLE: Should Not Parse");

                // Act
                var (song, chart) = await DTXChartParser.ParseSongEntitiesAsync(txtFile);

                // Assert
                Assert.Equal(Path.GetFileNameWithoutExtension(txtFile), song.Title);
                Assert.Equal("", song.Artist); // Should not parse content
                Assert.Equal(txtFile, chart.FilePath);
            }
            finally
            {
                if (File.Exists(txtFile))
                    File.Delete(txtFile);
                if (File.Exists(tempFile))
                    File.Delete(tempFile);
            }
        }

        [Fact]
        public async Task ParseSongEntitiesAsync_WithDecimalLevels_ShouldRoundCorrectly()
        {
            // Arrange
            var tempFile = Path.GetTempFileName();
            var dtxFile = Path.ChangeExtension(tempFile, ".dtx");

            var dtxContent = @"#DLEVEL: 85.7
#GLEVEL: 78.3
#BLEVEL: 65.9
";

            try
            {
                await File.WriteAllTextAsync(dtxFile, dtxContent);

                // Act
                var (song, chart) = await DTXChartParser.ParseSongEntitiesAsync(dtxFile);

                // Assert
                Assert.Equal(86, chart.DrumLevel); // 85.7 rounded
                Assert.Equal(78, chart.GuitarLevel); // 78.3 rounded
                Assert.Equal(66, chart.BassLevel); // 65.9 rounded
            }
            finally
            {
                if (File.Exists(dtxFile))
                    File.Delete(dtxFile);
                if (File.Exists(tempFile))
                    File.Delete(tempFile);
            }
        }

        #endregion
    }
}
