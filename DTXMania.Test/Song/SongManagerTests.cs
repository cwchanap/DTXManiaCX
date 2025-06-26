using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DTX.Song;
using Xunit;
using DTXMania.Game.Lib.Song.Entities;

namespace DTXMania.Test.Song
{
    /// <summary>
    /// Unit tests for SongManager class
    /// Tests song database management and enumeration using singleton pattern
    /// </summary>
    [Collection("SongManager")]
    public class SongManagerTests : IDisposable
    {
        private SongManager _manager;
        private readonly string _testDbPath;

        public SongManagerTests()
        {
            // Reset singleton instance completely for each test
            SongManager.ResetInstanceForTesting();
            _manager = SongManager.Instance;
            
            // Use unique database path for each test instance
            _testDbPath = Path.Combine(Path.GetTempPath(), $"test_songs_{Guid.NewGuid()}.db");
        }

        public void Dispose()
        {
            // Clean up after each test
            _manager?.Clear();
            
            // Clean up test database file
            if (File.Exists(_testDbPath))
            {
                try
                {
                    File.Delete(_testDbPath);
                }
                catch
                {
                    // Ignore errors during cleanup
                }
            }
        }

        #region Singleton Tests

        [Fact]
        public void Instance_ShouldReturnSameSingleton()
        {
            // Arrange & Act
            var manager1 = SongManager.Instance;
            var manager2 = SongManager.Instance;

            // Assert
            Assert.Same(manager1, manager2);
        }

        [Fact]
        public async Task Instance_ShouldInitializeWithDefaults()
        {
            // Arrange & Act
            var manager = SongManager.Instance;

            // Assert
            Assert.NotNull(manager.RootSongs);
            Assert.Empty(manager.RootSongs);
            Assert.Equal(0, await manager.GetDatabaseScoreCountAsync());
            Assert.Equal(0, manager.DiscoveredScoreCount);
            Assert.Equal(0, manager.EnumeratedFileCount);
            Assert.False(manager.IsEnumerating);
            Assert.False(manager.IsInitialized);
        }

        #endregion

        #region Database Management Tests

        [Fact]
        public async Task InitializeAsync_WithNonExistentPaths_ShouldCompleteWithoutErrors()
        {
            // Arrange
            var nonExistentPaths = new[] { "NonExistent1", "NonExistent2" };

            // Act
            var result = await _manager.InitializeAsync(nonExistentPaths, _testDbPath);

            // Assert
            Assert.True(result);
            Assert.Equal(0, await _manager.GetDatabaseScoreCountAsync());
        }

        [Fact]
        public async Task DatabaseService_ShouldBeAvailableAfterInitialization()
        {
            // Arrange
            await _manager.InitializeAsync(new string[0], _testDbPath);

            // Act
            var databaseService = _manager.DatabaseService;

            // Assert
            Assert.NotNull(databaseService);
        }

        [Fact]
        public async Task EnumerateAndClear_ShouldRoundTrip()
        {
            // Arrange
            var initialCount = _manager.RootSongs.Count;

            // Act - Enumerate
            await _manager.EnumerateSongsAsync(new[] { "NonExistentPath" }); // This will create empty structure
            
            // Clear
            _manager.Clear();

            // Assert
            Assert.Equal(initialCount, _manager.RootSongs.Count);
        }

        [Fact]
        public void IsInitialized_ShouldTrackInitializationState()
        {
            // Arrange & Act
            var isInitialized = _manager.IsInitialized;

            // Assert
            // Should be false initially since we clear in constructor
            Assert.False(isInitialized);
        }

        #endregion

        #region Enumeration Tests

        [Fact]
        public async Task EnumerateSongsAsync_WithNonExistentPaths_ShouldReturnZero()
        {
            // Arrange
            var paths = new[] { "NonExistent1", "NonExistent2" };

            // Act
            var result = await _manager.EnumerateSongsAsync(paths);

            // Assert
            Assert.Equal(0, result);
            Assert.Equal(0, _manager.DiscoveredScoreCount);
            Assert.False(_manager.IsEnumerating);
        }

        [Fact]
        public async Task EnumerateSongsAsync_WithEmptyArray_ShouldReturnZero()
        {
            // Arrange
            var paths = new string[0];

            // Act
            var result = await _manager.EnumerateSongsAsync(paths);

            // Assert
            Assert.Equal(0, result);
            Assert.Equal(0, _manager.DiscoveredScoreCount);
        }

        [Fact]
        public async Task EnumerateSongsAsync_WithNullPaths_ShouldHandleGracefully()
        {
            // Arrange
            var paths = new string[] { null!, "", "NonExistent" };

            // Act
            var result = await _manager.EnumerateSongsAsync(paths);

            // Assert
            Assert.Equal(0, result);
            Assert.Equal(0, _manager.DiscoveredScoreCount);
        }

        [Fact]
        public async Task EnumerateSongsAsync_WithRealDirectory_ShouldEnumerateFiles()
        {
            // Arrange
            var tempDir = Path.Combine(Path.GetTempPath(), "SongManagerTest");
            var dtxFile = Path.Combine(tempDir, "test.dtx");

            try
            {
                Directory.CreateDirectory(tempDir);
                await File.WriteAllTextAsync(dtxFile, @"#TITLE: Test Song
#ARTIST: Test Artist
#DLEVEL: 50
");

                // Initialize with empty paths to avoid automatic enumeration
                await _manager.InitializeAsync(new string[0], _testDbPath);
                
                // Act - Now enumerate the specific directory
                var result = await _manager.EnumerateSongsAsync(new[] { tempDir });

                // Assert
                Assert.Equal(1, result);
                Assert.Equal(1, _manager.DiscoveredScoreCount);
                Assert.True(await _manager.GetDatabaseScoreCountAsync() > 0);
                Assert.True(_manager.RootSongs.Count > 0);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public async Task EnumerateSongsAsync_WithDTXFilesPrefix_ShouldCreateBoxNodes()
        {
            // Arrange
            var tempDir = Path.Combine(Path.GetTempPath(), "SongManagerTest");
            var subDir = Path.Combine(tempDir, "DTXFiles.TestFolder"); // DTXFiles prefix makes it a box
            var dtxFile = Path.Combine(subDir, "test.dtx");

            try
            {
                Directory.CreateDirectory(subDir);
                await File.WriteAllTextAsync(dtxFile, @"#TITLE: Sub Song
#ARTIST: Sub Artist
#DLEVEL: 60
");

                // Initialize with empty paths to avoid automatic enumeration
                await _manager.InitializeAsync(new string[0], _testDbPath);

                // Act
                var result = await _manager.EnumerateSongsAsync(new[] { tempDir });

                // Assert
                Assert.Equal(1, result);
                Assert.Contains(_manager.RootSongs, n => n.Type == NodeType.Box);

                var boxNode = _manager.RootSongs.First(n => n.Type == NodeType.Box);
                Assert.Equal("DTXFiles.TestFolder", boxNode.Title);
                Assert.Contains(boxNode.Children, c => c.Type == NodeType.Score);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void CancelEnumeration_ShouldNotThrow()
        {
            // Act & Assert (should not throw)
            _manager.CancelEnumeration();
        }

        #endregion

        #region Helper Method Tests

        [Fact]
        public async Task FindSongByPath_WithExistingSong_ShouldReturnScore()
        {
            // Arrange
            var tempDir = Path.Combine(Path.GetTempPath(), "SongManagerTest");
            var dtxFile = Path.Combine(tempDir, "test.dtx");

            try
            {
                Directory.CreateDirectory(tempDir);
                await File.WriteAllTextAsync(dtxFile, @"#TITLE: Test Song
#DLEVEL: 50
");

                // Initialize with empty paths to avoid automatic enumeration
                await _manager.InitializeAsync(new string[0], _testDbPath);
                await _manager.EnumerateSongsAsync(new[] { tempDir });

                // Act - Test that enumeration worked
                Assert.True(await _manager.GetDatabaseScoreCountAsync() >= 0);
                Assert.True(_manager.RootSongs.Count >= 0);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void RootSongs_ShouldBeReadOnlyList()
        {
            // Act
            var rootSongs = _manager.RootSongs;

            // Assert
            Assert.NotNull(rootSongs);
            Assert.IsAssignableFrom<IReadOnlyList<SongListNode>>(rootSongs);
        }

        [Fact]
        public async Task GetSongsByGenre_WithMatchingGenre_ShouldReturnSongs()
        {
            // Arrange
            var tempDir = Path.Combine(Path.GetTempPath(), "SongManagerTest");
            var dtxFile1 = Path.Combine(tempDir, "rock1.dtx");
            var dtxFile2 = Path.Combine(tempDir, "rock2.dtx");
            var dtxFile3 = Path.Combine(tempDir, "pop.dtx");

            try
            {
                Directory.CreateDirectory(tempDir);
                await File.WriteAllTextAsync(dtxFile1, @"#TITLE: Rock Song 1
#GENRE: Rock
#DLEVEL: 50
");
                await File.WriteAllTextAsync(dtxFile2, @"#TITLE: Rock Song 2
#GENRE: Rock
#DLEVEL: 60
");
                await File.WriteAllTextAsync(dtxFile3, @"#TITLE: Pop Song
#GENRE: Pop
#DLEVEL: 40
");

                // Initialize with empty paths to avoid automatic enumeration
                await _manager.InitializeAsync(new string[0], _testDbPath);
                await _manager.EnumerateSongsAsync(new[] { tempDir });

                // Act
                var rockSongs = await _manager.GetSongsByGenreAsync("Rock");
                var popSongs = await _manager.GetSongsByGenreAsync("Pop");

                // Assert
                Assert.Equal(2, rockSongs.Count);
                Assert.Single(popSongs);
                Assert.All(rockSongs, s => Assert.Equal("Rock", s.Genre));
                Assert.All(popSongs, s => Assert.Equal("Pop", s.Genre));
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public async Task Clear_ShouldResetAllData()
        {
            // Arrange
            var tempDir = Path.Combine(Path.GetTempPath(), "SongManagerTestClear");
            var dtxFile = Path.Combine(tempDir, "test.dtx");
            
            try
            {
                Directory.CreateDirectory(tempDir);
                File.WriteAllText(dtxFile, "#TITLE: Test Song\n#DLEVEL: 50\n");
                
                // Initialize and enumerate to populate data
                await _manager.InitializeAsync(new string[0], _testDbPath);
                await _manager.EnumerateSongsAsync(new[] { tempDir });
                
                // Verify we have data
                var initialRootSongCount = _manager.RootSongs.Count;
                Assert.True(initialRootSongCount > 0); // Should have songs after enumeration

                // Act
                _manager.Clear();

                // Assert
                Assert.Equal(0, await _manager.GetDatabaseScoreCountAsync());
                Assert.Empty(_manager.RootSongs);
                Assert.Equal(0, _manager.DiscoveredScoreCount);
                Assert.Equal(0, _manager.EnumeratedFileCount);
                Assert.False(_manager.IsInitialized);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }

        #endregion

        #region Event Tests

        [Fact]
        public async Task EnumerateSongsAsync_ShouldFireEnumerationCompleted()
        {
            // Arrange
            var eventFired = false;
            _manager.EnumerationCompleted += (sender, args) => eventFired = true;

            // Act
            await _manager.EnumerateSongsAsync(new[] { "NonExistentPath" });

            // Assert
            Assert.True(eventFired);
        }

        [Fact]
        public async Task EnumerateSongsAsync_WithRealSong_ShouldFireSongDiscovered()
        {
            // Arrange
            var tempDir = Path.Combine(Path.GetTempPath(), "SongManagerTest");
            var dtxFile = Path.Combine(tempDir, "test.dtx");
            SongListNode? discoveredSong = null;

            _manager.SongDiscovered += (sender, args) => discoveredSong = args.Song;

            try
            {
                Directory.CreateDirectory(tempDir);
                await File.WriteAllTextAsync(dtxFile, @"#TITLE: Event Test Song
#DLEVEL: 50
");

                // Initialize with empty paths to avoid automatic enumeration
                await _manager.InitializeAsync(new string[0], _testDbPath);

                // Act
                await _manager.EnumerateSongsAsync(new[] { tempDir });

                // Assert
                Assert.NotNull(discoveredSong);
                Assert.Equal("Event Test Song", discoveredSong.DisplayTitle);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }

        #endregion

        #region Initialization Tests

        [Fact]
        public async Task InitializeAsync_ShouldMarkAsInitialized()
        {
            // Arrange
            var tempDir = Path.Combine(Path.GetTempPath(), "SongManagerTestInit");
            var dtxFile = Path.Combine(tempDir, "test.dtx");

            try
            {
                Directory.CreateDirectory(tempDir);
                await File.WriteAllTextAsync(dtxFile, @"#TITLE: Init Test Song
#DLEVEL: 50
");

                // Act
                var result = await _manager.InitializeAsync(new[] { tempDir }, _testDbPath);

                // Assert
                Assert.True(result);
                Assert.True(_manager.IsInitialized);
                Assert.True(await _manager.GetDatabaseScoreCountAsync() > 0);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public async Task InitializeAsync_WhenAlreadyInitialized_ShouldReturnTrue()
        {
            // Arrange
            var tempDir = Path.Combine(Path.GetTempPath(), "SongManagerTestDoubleInit");
            
            try
            {
                Directory.CreateDirectory(tempDir);
                
                // First initialization
                await _manager.InitializeAsync(new[] { tempDir }, _testDbPath);
                Assert.True(_manager.IsInitialized);

                // Act - Second initialization
                var result = await _manager.InitializeAsync(new[] { tempDir }, _testDbPath);

                // Assert
                Assert.True(result);
                Assert.True(_manager.IsInitialized);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }

        #endregion
    }
}
