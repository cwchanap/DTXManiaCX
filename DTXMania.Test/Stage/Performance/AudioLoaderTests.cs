using System;
using System.IO;
using System.Threading.Tasks;
using DTX.Resources;
using DTX.Stage.Performance;
using DTXMania.Test.Utilities;
using Moq;
using Xunit;
using Xunit.Abstractions;

namespace DTXMania.Test.Stage.Performance
{
    /// <summary>
    /// Unit tests for AudioLoader functionality including:
    /// - Constructor validation
    /// - Basic loading behavior and state management
    /// - Error handling and validation
    /// - Disposal and resource management
    /// Note: Some tests use real audio files due to MonoGame integration complexity
    /// </summary>
    public class AudioLoaderTests : IClassFixture<AudioLoaderTests.TestFileFixture>, IDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly TestFileFixture _fixture;
        private readonly Mock<IResourceManager> _mockResourceManager;

        public AudioLoaderTests(ITestOutputHelper output, TestFileFixture fixture)
        {
            _output = output;
            _fixture = fixture;
            _mockResourceManager = new Mock<IResourceManager>();
        }

        public void Dispose()
        {
            // Individual test cleanup if needed
        }

        /// <summary>
        /// Shared test fixture that creates reusable test files for all tests in the class
        /// </summary>
        public class TestFileFixture : IDisposable
        {
            public string TempDir { get; }
            public string ValidAudioFile { get; }
            public string NonExistentFile { get; }
            public string ChartFile { get; }

            public TestFileFixture()
            {
                // Create temp directory for test files
                TempDir = Path.Combine(Path.GetTempPath(), "DTXMania_AudioLoader_Test_" + Guid.NewGuid().ToString("N")[..8]);
                Directory.CreateDirectory(TempDir);

                // Create test files
                ValidAudioFile = AudioTestUtils.CreateTestWavFile(TempDir, "test_audio.wav");
                NonExistentFile = Path.Combine(TempDir, "nonexistent.wav");
                ChartFile = Path.Combine(TempDir, "test_chart.dtx");
                File.WriteAllText(ChartFile, "#TITLE: Test Chart\n#ARTIST: Test Artist\n");
            }

            public void Dispose()
            {
                // Clean up temp directory
                if (Directory.Exists(TempDir))
                {
                    try
                    {
                        Directory.Delete(TempDir, true);
                    }
                    catch
                    {
                        // Ignore cleanup errors in tests
                    }
                }
            }
        }

        #region Constructor Tests

        [Fact]
        public void Constructor_WithValidResourceManager_CreatesSuccessfully()
        {
            // Arrange & Act
            using var audioLoader = new AudioLoader(_mockResourceManager.Object);

            // Assert
            Assert.NotNull(audioLoader);
            Assert.False(audioLoader.IsLoaded);
            Assert.Equal("", audioLoader.LoadedAudioPath);
        }

        [Fact]
        public void Constructor_WithNullResourceManager_ThrowsArgumentNullException()
        {
            // Act & Assert
            var exception = Assert.Throws<ArgumentNullException>(() => new AudioLoader(null!));
            Assert.Equal("resourceManager", exception.ParamName);
        }

        #endregion

        #region LoadBackgroundMusicAsync Tests

        [Fact]
        public async Task LoadBackgroundMusicAsync_WithValidAudioFile_UpdatesState()
        {
            // Arrange
            using var audioLoader = new AudioLoader(_mockResourceManager.Object);

            // Act
            await audioLoader.LoadBackgroundMusicAsync(_fixture.ValidAudioFile);

            // Assert
            Assert.True(audioLoader.IsLoaded);
            Assert.Equal(_fixture.ValidAudioFile, audioLoader.LoadedAudioPath);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public async Task LoadBackgroundMusicAsync_WithNullOrEmptyPath_ThrowsArgumentException(string? audioPath)
        {
            // Arrange
            using var audioLoader = new AudioLoader(_mockResourceManager.Object);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<ArgumentException>(
                () => audioLoader.LoadBackgroundMusicAsync(audioPath!));
            Assert.Equal("audioPath", exception.ParamName);
        }

        [Fact]
        public async Task LoadBackgroundMusicAsync_WithWhitespacePath_ThrowsInvalidOperationException()
        {
            // Arrange
            using var audioLoader = new AudioLoader(_mockResourceManager.Object);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => audioLoader.LoadBackgroundMusicAsync("   "));
            Assert.Contains("Failed to load background music", exception.Message);
        }

        [Fact]
        public async Task LoadBackgroundMusicAsync_WithNonExistentFile_ThrowsInvalidOperationException()
        {
            // Arrange
            using var audioLoader = new AudioLoader(_mockResourceManager.Object);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => audioLoader.LoadBackgroundMusicAsync(_fixture.NonExistentFile));
            Assert.Contains("Failed to load background music", exception.Message);
            Assert.Contains(_fixture.NonExistentFile, exception.Message);

            // Verify the inner exception is FileNotFoundException
            Assert.IsType<FileNotFoundException>(exception.InnerException);
            Assert.Contains("Audio file not found", exception.InnerException.Message);
        }

        [Fact]
        public async Task LoadBackgroundMusicAsync_WhenDisposed_ThrowsObjectDisposedException()
        {
            // Arrange
            var audioLoader = new AudioLoader(_mockResourceManager.Object);
            audioLoader.Dispose();

            // Act & Assert
            await Assert.ThrowsAsync<ObjectDisposedException>(
                () => audioLoader.LoadBackgroundMusicAsync(_fixture.ValidAudioFile));
        }

        [Fact]
        public async Task LoadBackgroundMusicAsync_LoadingSecondFile_UpdatesPath()
        {
            // Arrange
            using var audioLoader = new AudioLoader(_mockResourceManager.Object);
            var firstFile = _fixture.ValidAudioFile;
            var secondFile = AudioTestUtils.CreateTestWavFile(_fixture.TempDir, "second_audio.wav");

            // Act
            await audioLoader.LoadBackgroundMusicAsync(firstFile);
            var firstLoadedPath = audioLoader.LoadedAudioPath;
            
            await audioLoader.LoadBackgroundMusicAsync(secondFile);

            // Assert
            Assert.Equal(firstFile, firstLoadedPath);
            Assert.True(audioLoader.IsLoaded);
            Assert.Equal(secondFile, audioLoader.LoadedAudioPath);
        }

        #endregion

        #region PreloadForChartAsync Tests

        [Fact]
        public async Task PreloadForChartAsync_WithValidAudioPath_LoadsSuccessfully()
        {
            // Arrange
            using var audioLoader = new AudioLoader(_mockResourceManager.Object);

            // Act
            await audioLoader.PreloadForChartAsync(_fixture.ChartFile, _fixture.ValidAudioFile);

            // Assert
            Assert.True(audioLoader.IsLoaded);
            Assert.Equal(_fixture.ValidAudioFile, audioLoader.LoadedAudioPath);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public async Task PreloadForChartAsync_WithNullOrEmptyAudioPath_DoesNotLoad(string? backgroundAudioPath)
        {
            // Arrange
            using var audioLoader = new AudioLoader(_mockResourceManager.Object);

            // Act
            await audioLoader.PreloadForChartAsync(_fixture.ChartFile, backgroundAudioPath!);

            // Assert
            Assert.False(audioLoader.IsLoaded);
            Assert.Equal("", audioLoader.LoadedAudioPath);
        }

        #endregion

        #region CreateSongTimer Tests

        [Fact]
        public async Task CreateSongTimer_WhenAudioLoaded_ReturnsValidSongTimer()
        {
            // Arrange
            using var audioLoader = new AudioLoader(_mockResourceManager.Object);
            await audioLoader.LoadBackgroundMusicAsync(_fixture.ValidAudioFile);

            // Act
            var songTimer = audioLoader.CreateSongTimer();

            // Assert
            Assert.NotNull(songTimer);
            using (songTimer) { } // Ensure proper disposal
        }

        [Fact]
        public void CreateSongTimer_WhenNotLoaded_ReturnsNull()
        {
            // Arrange
            using var audioLoader = new AudioLoader(_mockResourceManager.Object);

            // Act
            var songTimer = audioLoader.CreateSongTimer();

            // Assert
            Assert.Null(songTimer);
        }

        #endregion

        #region GetAudioInfo Tests

        [Fact]
        public async Task GetAudioInfo_WhenAudioLoaded_ReturnsValidInfo()
        {
            // Arrange
            using var audioLoader = new AudioLoader(_mockResourceManager.Object);
            await audioLoader.LoadBackgroundMusicAsync(_fixture.ValidAudioFile);

            // Act
            var audioInfo = audioLoader.GetAudioInfo();

            // Assert
            Assert.NotNull(audioInfo);
            Assert.Equal(_fixture.ValidAudioFile, audioInfo.FilePath);
            Assert.Equal(Path.GetFileName(_fixture.ValidAudioFile), audioInfo.FileName);
            Assert.True(audioInfo.FileSize > 0);
            Assert.True(audioInfo.IsLoaded);
        }

        [Fact]
        public void GetAudioInfo_WhenNotLoaded_ReturnsNull()
        {
            // Arrange
            using var audioLoader = new AudioLoader(_mockResourceManager.Object);

            // Act
            var audioInfo = audioLoader.GetAudioInfo();

            // Assert
            Assert.Null(audioInfo);
        }

        #endregion

        #region UnloadCurrentSound Tests

        [Fact]
        public async Task UnloadCurrentSound_WhenAudioLoaded_UnloadsSuccessfully()
        {
            // Arrange
            using var audioLoader = new AudioLoader(_mockResourceManager.Object);
            await audioLoader.LoadBackgroundMusicAsync(_fixture.ValidAudioFile);
            Assert.True(audioLoader.IsLoaded); // Verify it was loaded

            // Act
            audioLoader.UnloadCurrentSound();

            // Assert
            Assert.False(audioLoader.IsLoaded);
            Assert.Equal("", audioLoader.LoadedAudioPath);
        }

        [Fact]
        public void UnloadCurrentSound_WhenNotLoaded_DoesNotThrow()
        {
            // Arrange
            using var audioLoader = new AudioLoader(_mockResourceManager.Object);

            // Act & Assert (should not throw)
            audioLoader.UnloadCurrentSound();
            Assert.False(audioLoader.IsLoaded);
        }

        #endregion

        #region IsLoaded Property Tests

        [Fact]
        public async Task IsLoaded_WhenAudioLoaded_ReturnsTrue()
        {
            // Arrange
            using var audioLoader = new AudioLoader(_mockResourceManager.Object);

            // Act
            await audioLoader.LoadBackgroundMusicAsync(_fixture.ValidAudioFile);

            // Assert
            Assert.True(audioLoader.IsLoaded);
        }

        [Fact]
        public void IsLoaded_WhenNotLoaded_ReturnsFalse()
        {
            // Arrange
            using var audioLoader = new AudioLoader(_mockResourceManager.Object);

            // Assert
            Assert.False(audioLoader.IsLoaded);
        }

        [Fact]
        public async Task IsLoaded_AfterDisposal_ReturnsFalse()
        {
            // Arrange
            var audioLoader = new AudioLoader(_mockResourceManager.Object);
            await audioLoader.LoadBackgroundMusicAsync(_fixture.ValidAudioFile);
            Assert.True(audioLoader.IsLoaded); // Verify it was loaded

            // Act
            audioLoader.Dispose();

            // Assert
            Assert.False(audioLoader.IsLoaded);
        }

        #endregion

        #region Disposal Tests

        [Fact]
        public async Task Dispose_WhenAudioLoaded_DisposesCorrectly()
        {
            // Arrange
            var audioLoader = new AudioLoader(_mockResourceManager.Object);
            await audioLoader.LoadBackgroundMusicAsync(_fixture.ValidAudioFile);

            // Act
            audioLoader.Dispose();

            // Assert
            Assert.False(audioLoader.IsLoaded);
            Assert.Equal("", audioLoader.LoadedAudioPath);
        }

        [Fact]
        public void Dispose_WhenNotLoaded_DoesNotThrow()
        {
            // Arrange
            var audioLoader = new AudioLoader(_mockResourceManager.Object);

            // Act & Assert (should not throw)
            audioLoader.Dispose();
        }

        [Fact]
        public void Dispose_CalledMultipleTimes_DoesNotThrow()
        {
            // Arrange
            var audioLoader = new AudioLoader(_mockResourceManager.Object);

            // Act & Assert (should not throw)
            audioLoader.Dispose();
            audioLoader.Dispose();
            audioLoader.Dispose();
        }

        #endregion
    }
}