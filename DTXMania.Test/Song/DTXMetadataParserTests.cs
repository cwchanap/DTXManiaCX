using System;
using System.IO;
using System.Threading.Tasks;
using DTX.Song;
using Xunit;

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
        public void GetBasicFileInfo_ShouldReturnCorrectMetadata()
        {
            // Arrange
            var tempFile = Path.GetTempFileName();
            var dtxFile = Path.ChangeExtension(tempFile, ".dtx");
            
            try
            {
                File.WriteAllText(dtxFile, "test content");
                var fileInfo = new FileInfo(dtxFile);

                // Act
                var metadata = DTXMetadataParser.GetBasicFileInfo(dtxFile);

                // Assert
                Assert.Equal(dtxFile, metadata.FilePath);
                Assert.Equal(Path.GetFileNameWithoutExtension(dtxFile), metadata.Title);
                Assert.Equal(fileInfo.Length, metadata.FileSize);
                Assert.Equal(".dtx", metadata.FileFormat);
                Assert.True(metadata.LastModified > DateTime.MinValue);
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
        public async Task ParseMetadataAsync_WithNonExistentFile_ShouldThrowFileNotFoundException()
        {
            // Arrange
            var parser = new DTXMetadataParser();
            var nonExistentFile = @"C:\NonExistent\test.dtx";

            // Act & Assert
            await Assert.ThrowsAsync<FileNotFoundException>(() => 
                parser.ParseMetadataAsync(nonExistentFile));
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        public async Task ParseMetadataAsync_WithInvalidPath_ShouldThrowFileNotFoundException(string filePath)
        {
            // Arrange
            var parser = new DTXMetadataParser();

            // Act & Assert
            await Assert.ThrowsAsync<FileNotFoundException>(() => 
                parser.ParseMetadataAsync(filePath));
        }

        [Fact]
        public async Task ParseMetadataAsync_WithValidDTXFile_ShouldParseBasicMetadata()
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
#PREVIEW: preview.ogg

// This is a comment
*01010: 11111111
";

            try
            {
                await File.WriteAllTextAsync(dtxFile, dtxContent);

                // Act
                var metadata = await parser.ParseMetadataAsync(dtxFile);

                // Assert
                Assert.Equal("Test Song", metadata.Title);
                Assert.Equal("Test Artist", metadata.Artist);
                Assert.Equal("Test Genre", metadata.Genre);
                Assert.Equal(120.5, metadata.BPM);
                Assert.Equal(85, metadata.DrumLevel);
                Assert.Equal(78, metadata.GuitarLevel);
                Assert.Equal(65, metadata.BassLevel);
                Assert.Equal("This is a test song", metadata.Comment);
                Assert.Equal("preview.ogg", metadata.PreviewFile);
                Assert.Equal(dtxFile, metadata.FilePath);
                Assert.Equal(".dtx", metadata.FileFormat);
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
        public async Task ParseMetadataAsync_WithLevelFormat_ShouldParseCorrectly()
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
                var metadata = await parser.ParseMetadataAsync(dtxFile);

                // Assert
                Assert.Equal("Level Format Test", metadata.Title);
                Assert.Equal(90, metadata.DrumLevel);
                Assert.Equal(85, metadata.GuitarLevel);
                Assert.Equal(70, metadata.BassLevel);
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
        public async Task ParseMetadataAsync_WithQuotedValues_ShouldRemoveQuotes()
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
                var metadata = await parser.ParseMetadataAsync(dtxFile);

                // Assert
                Assert.Equal("Quoted Title", metadata.Title);
                Assert.Equal("Quoted Artist", metadata.Artist);
                Assert.Equal("Quoted Genre", metadata.Genre);
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
        public async Task ParseMetadataAsync_WithEmptyFile_ShouldReturnBasicInfo()
        {
            // Arrange
            var parser = new DTXMetadataParser();
            var tempFile = Path.GetTempFileName();
            var dtxFile = Path.ChangeExtension(tempFile, ".dtx");
            
            try
            {
                await File.WriteAllTextAsync(dtxFile, "");

                // Act
                var metadata = await parser.ParseMetadataAsync(dtxFile);

                // Assert
                Assert.Equal(Path.GetFileNameWithoutExtension(dtxFile), metadata.Title);
                Assert.Equal("", metadata.Artist);
                Assert.Equal("", metadata.Genre);
                Assert.Null(metadata.BPM);
                Assert.Null(metadata.DrumLevel);
                Assert.Equal(dtxFile, metadata.FilePath);
                Assert.Equal(".dtx", metadata.FileFormat);
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
        public async Task ParseMetadataAsync_WithUnsupportedExtension_ShouldReturnBasicInfo()
        {
            // Arrange
            var parser = new DTXMetadataParser();
            var tempFile = Path.GetTempFileName();
            var txtFile = Path.ChangeExtension(tempFile, ".txt");
            
            try
            {
                await File.WriteAllTextAsync(txtFile, "#TITLE: Should Not Parse");

                // Act
                var metadata = await parser.ParseMetadataAsync(txtFile);

                // Assert
                Assert.Equal(Path.GetFileNameWithoutExtension(txtFile), metadata.Title);
                Assert.Equal("", metadata.Artist); // Should not parse content
                Assert.Equal(txtFile, metadata.FilePath);
                Assert.Equal(".txt", metadata.FileFormat);
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
        public async Task ParseMetadataAsync_WithDecimalLevels_ShouldRoundCorrectly()
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
                var metadata = await parser.ParseMetadataAsync(dtxFile);

                // Assert
                Assert.Equal(86, metadata.DrumLevel); // 85.7 rounded
                Assert.Equal(78, metadata.GuitarLevel); // 78.3 rounded
                Assert.Equal(66, metadata.BassLevel); // 65.9 rounded
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
