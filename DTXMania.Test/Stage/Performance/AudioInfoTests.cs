using DTX.Stage.Performance;
using Xunit;

namespace DTXMania.Test.Stage.Performance
{
    /// <summary>
    /// Unit tests for AudioInfo data class
    /// Tests data holding and ToString functionality
    /// </summary>
    public class AudioInfoTests
    {
        #region Constructor and Properties Tests

        [Fact]
        public void Constructor_CreatesWithDefaultValues()
        {
            // Act
            var audioInfo = new AudioInfo();

            // Assert
            Assert.Equal("", audioInfo.FilePath);
            Assert.Equal("", audioInfo.FileName);
            Assert.Equal(0, audioInfo.FileSize);
            Assert.False(audioInfo.IsLoaded);
        }

        [Fact]
        public void Properties_CanBeSetAndRetrieved()
        {
            // Arrange
            var audioInfo = new AudioInfo();
            const string testFilePath = "/path/to/audio.wav";
            const string testFileName = "audio.wav";
            const long testFileSize = 1024000;
            const bool testIsLoaded = true;

            // Act
            audioInfo.FilePath = testFilePath;
            audioInfo.FileName = testFileName;
            audioInfo.FileSize = testFileSize;
            audioInfo.IsLoaded = testIsLoaded;

            // Assert
            Assert.Equal(testFilePath, audioInfo.FilePath);
            Assert.Equal(testFileName, audioInfo.FileName);
            Assert.Equal(testFileSize, audioInfo.FileSize);
            Assert.Equal(testIsLoaded, audioInfo.IsLoaded);
        }

        #endregion

        #region ToString Tests

        [Fact]
        public void ToString_WithValidData_ReturnsFormattedString()
        {
            // Arrange
            var audioInfo = new AudioInfo
            {
                FilePath = "/path/to/test.wav",
                FileName = "test.wav",
                FileSize = 2048000, // 2MB in bytes
                IsLoaded = true
            };

            // Act
            var result = audioInfo.ToString();

            // Assert
            Assert.Contains("test.wav", result);
            Assert.Contains("2000KB", result); // 2048000 / 1024 = 2000
            Assert.Contains("Loaded", result);
        }

        [Fact]
        public void ToString_WhenNotLoaded_ShowsNotLoaded()
        {
            // Arrange
            var audioInfo = new AudioInfo
            {
                FileName = "test.wav",
                FileSize = 1024,
                IsLoaded = false
            };

            // Act
            var result = audioInfo.ToString();

            // Assert
            Assert.Contains("Not Loaded", result);
        }

        [Fact]
        public void ToString_WithZeroFileSize_ShowsZeroKB()
        {
            // Arrange
            var audioInfo = new AudioInfo
            {
                FileName = "empty.wav",
                FileSize = 0,
                IsLoaded = true
            };

            // Act
            var result = audioInfo.ToString();

            // Assert
            Assert.Contains("0KB", result);
        }

        [Fact]
        public void ToString_WithSmallFileSize_ShowsCorrectKB()
        {
            // Arrange
            var audioInfo = new AudioInfo
            {
                FileName = "small.wav",
                FileSize = 512, // Less than 1KB
                IsLoaded = true
            };

            // Act
            var result = audioInfo.ToString();

            // Assert
            Assert.Contains("0KB", result); // 512 / 1024 = 0 (integer division)
        }

        [Theory]
        [InlineData(1024, "1KB")]
        [InlineData(2048, "2KB")]
        [InlineData(1536, "1KB")] // 1.5KB rounds down to 1KB
        [InlineData(2560, "2KB")] // 2.5KB rounds down to 2KB
        [InlineData(1048576, "1024KB")] // 1MB
        public void ToString_WithVariousFileSizes_ShowsCorrectKB(long fileSize, string expectedKB)
        {
            // Arrange
            var audioInfo = new AudioInfo
            {
                FileName = "test.wav",
                FileSize = fileSize,
                IsLoaded = true
            };

            // Act
            var result = audioInfo.ToString();

            // Assert
            Assert.Contains(expectedKB, result);
        }

        [Fact]
        public void ToString_WithEmptyFileName_DoesNotThrow()
        {
            // Arrange
            var audioInfo = new AudioInfo
            {
                FileName = "",
                FileSize = 1024,
                IsLoaded = true
            };

            // Act & Assert (should not throw)
            var result = audioInfo.ToString();
            Assert.NotNull(result);
            Assert.Contains("1KB", result);
            Assert.Contains("Loaded", result);
        }

        #endregion
    }
}