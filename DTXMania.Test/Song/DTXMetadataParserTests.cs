using System;
using System.IO;
using System.Threading.Tasks;
using DTX.Song;
using Xunit;
using DTXMania.Game.Lib.Song.Entities;

namespace DTXMania.Test.Song
{
    /// <summary>
    /// Unit tests for DTXMetadataParser class
    /// Tests DTX file parsing and metadata extraction
    /// </summary>
    public class DTXMetadataParserTests
    {
        #region Constructor Tests

        [Fact]
        public void Constructor_ShouldInitializeSuccessfully()
        {
            // Arrange & Act
            var parser = new DTXMetadataParser();

            // Assert
            Assert.NotNull(parser);
        }

        #endregion

        #region File Extension Tests

        [Theory]
        [InlineData(".dtx", true)]
        [InlineData(".gda", true)]
        [InlineData(".g2d", true)]
        [InlineData(".bms", true)]
        [InlineData(".bme", true)]
        [InlineData(".bml", true)]
        [InlineData(".DTX", true)] // Case insensitive
        [InlineData(".GDA", true)]
        [InlineData(".txt", false)]
        [InlineData(".mp3", false)]
        [InlineData(".wav", false)]
        [InlineData(".jpg", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void IsSupported_ShouldReturnCorrectValue(string extension, bool expected)
        {
            // Arrange
            var parser = new DTXMetadataParser();

            // Act
            var result = parser.IsSupported(extension);

            // Assert
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData(@"C:\Songs\test.dtx", true)]
        [InlineData(@"C:\Songs\test.gda", true)]
        [InlineData(@"C:\Songs\test.bms", true)]
        [InlineData(@"C:\Songs\test.txt", false)]
        [InlineData(@"C:\Songs\test", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void IsSupportedFile_ShouldReturnCorrectValue(string filePath, bool expected)
        {
            // Act
            var result = DTXMetadataParser.IsSupportedFile(filePath);

            // Assert
            Assert.Equal(expected, result);
        }

        #endregion

        #region Basic File Info Tests

        [Fact]
        public void IsSupportedFile_WithBasicFileInfo_ShouldWorkCorrectly()
        {
            // Arrange
            var tempFile = Path.GetTempFileName();
            var dtxFile = Path.ChangeExtension(tempFile, ".dtx");
            
            try
            {
                File.WriteAllText(dtxFile, "test content");
                var fileInfo = new FileInfo(dtxFile);

                // Act
                var isSupported = DTXMetadataParser.IsSupportedFile(dtxFile);

                // Assert
                Assert.True(isSupported);
                Assert.True(fileInfo.Length > 0);
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

        #region File Parsing Tests

        [Fact]
        public async Task ParseSongEntitiesAsync_WithNonExistentFile_ShouldThrowFileNotFoundException()
        {
            // Arrange
            var parser = new DTXMetadataParser();
            var nonExistentFile = @"C:\NonExistent\test.dtx";

            // Act & Assert
            await Assert.ThrowsAsync<FileNotFoundException>(() => 
                parser.ParseSongEntitiesAsync(nonExistentFile));
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        public async Task ParseSongEntitiesAsync_WithInvalidPath_ShouldThrowFileNotFoundException(string filePath)
        {
            // Arrange
            var parser = new DTXMetadataParser();

            // Act & Assert
            await Assert.ThrowsAsync<FileNotFoundException>(() => 
                parser.ParseSongEntitiesAsync(filePath));
        }

        [Fact]
        public async Task ParseSongEntitiesAsync_WithValidDTXFile_ShouldParseBasicMetadata()
        {
            // Arrange
            var parser = new DTXMetadataParser();
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
                var (song, chart) = await parser.ParseSongEntitiesAsync(dtxFile);

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
            var parser = new DTXMetadataParser();
            var tempFile = Path.GetTempFileName();
            var dtxFile = Path.ChangeExtension(tempFile, ".dtx");
            
            var dtxContent = @"#TITLE: Level Format Test
#LEVEL: DRUMS:90,GUITAR:85,BASS:70
";

            try
            {
                await File.WriteAllTextAsync(dtxFile, dtxContent);

                // Act
                var (song, chart) = await parser.ParseSongEntitiesAsync(dtxFile);

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
            var parser = new DTXMetadataParser();
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
                var (song, chart) = await parser.ParseSongEntitiesAsync(dtxFile);

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
            var parser = new DTXMetadataParser();
            var tempFile = Path.GetTempFileName();
            var dtxFile = Path.ChangeExtension(tempFile, ".dtx");
            
            try
            {
                await File.WriteAllTextAsync(dtxFile, "");

                // Act
                var (song, chart) = await parser.ParseSongEntitiesAsync(dtxFile);

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
            var parser = new DTXMetadataParser();
            var tempFile = Path.GetTempFileName();
            var txtFile = Path.ChangeExtension(tempFile, ".txt");
            
            try
            {
                await File.WriteAllTextAsync(txtFile, "#TITLE: Should Not Parse");

                // Act
                var (song, chart) = await parser.ParseSongEntitiesAsync(txtFile);

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
            var parser = new DTXMetadataParser();
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
                var (song, chart) = await parser.ParseSongEntitiesAsync(dtxFile);

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
