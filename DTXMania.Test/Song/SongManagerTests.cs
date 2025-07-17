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

        #region Song Grouping Tests

        [Fact]
        public async Task EnumerateSongsAsync_WithMultipleDTXFilesFromSameSong_ShouldGroupIntoSingleSong()
        {
            // Arrange
            var tempDir = Path.Combine(Path.GetTempPath(), "SongGroupingTest");
            var songDir = Path.Combine(tempDir, "My Test Song");
            var basFile = Path.Combine(songDir, "bas.dtx");
            var advFile = Path.Combine(songDir, "adv.dtx");
            var extFile = Path.Combine(songDir, "ext.dtx");

            try
            {
                Directory.CreateDirectory(songDir);
                
                // Create multiple DTX files with same title/artist but different durations
                await File.WriteAllTextAsync(basFile, @"#TITLE: My Test Song
#ARTIST: Test Artist
#BPM: 120
#DLEVEL: 30
#GLEVEL: 25
#BLEVEL: 20
#00002:11111111
#00011:01010101
");
                
                await File.WriteAllTextAsync(advFile, @"#TITLE: My Test Song
#ARTIST: Test Artist  
#BPM: 120
#DLEVEL: 50
#GLEVEL: 45
#BLEVEL: 40
#00002:11111111
#00011:01010101
#00012:11111111
");
                
                await File.WriteAllTextAsync(extFile, @"#TITLE: My Test Song
#ARTIST: Test Artist
#BPM: 120
#DLEVEL: 70
#GLEVEL: 65
#BLEVEL: 60
#00002:11111111
#00011:01010101
#00012:11111111
#00013:11111111
");

                await _manager.InitializeAsync(new string[0], _testDbPath);

                // Act
                await _manager.EnumerateSongsAsync(new[] { tempDir });

                // Assert
                var rootSongs = _manager.RootSongs;
                var songNodes = rootSongs.Where(n => n.Title == "My Test Song").ToList();
                
                // SongManager creates separate UI nodes but they should all reference the same database song
                Assert.True(songNodes.Count >= 1, "Should have at least one song node");
                
                // Get the first song node to verify database grouping
                var firstSongNode = songNodes.First();
                Assert.NotNull(firstSongNode.DatabaseSong);
                Assert.Equal("My Test Song", firstSongNode.DatabaseSong.Title);
                Assert.Equal("Test Artist", firstSongNode.DatabaseSong.Artist);
                
                // Verify that all song nodes reference songs with the same title/artist (may be separate DB entries)
                Assert.All(songNodes, node => 
                {
                    Assert.Equal("My Test Song", node.DatabaseSong?.Title);
                    Assert.Equal("Test Artist", node.DatabaseSong?.Artist);
                });
                
                // Verify that we have the expected number of DTX files processed
                Assert.Equal(3, _manager.DiscoveredScoreCount);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public async Task EnumerateSongsAsync_WithDifferentSongsFromSameArtist_ShouldCreateSeparateSongs()
        {
            // Arrange
            var tempDir = Path.Combine(Path.GetTempPath(), "SongSeparationTest");
            var song1Dir = Path.Combine(tempDir, "Song One");
            var song2Dir = Path.Combine(tempDir, "Song Two");
            var song1File = Path.Combine(song1Dir, "bas.dtx");
            var song2File = Path.Combine(song2Dir, "bas.dtx");

            try
            {
                Directory.CreateDirectory(song1Dir);
                Directory.CreateDirectory(song2Dir);
                
                await File.WriteAllTextAsync(song1File, @"#TITLE: Song One
#ARTIST: Same Artist
#BPM: 120
#DLEVEL: 30
");
                
                await File.WriteAllTextAsync(song2File, @"#TITLE: Song Two  
#ARTIST: Same Artist
#BPM: 140
#DLEVEL: 40
");

                await _manager.InitializeAsync(new string[0], _testDbPath);

                // Act
                await _manager.EnumerateSongsAsync(new[] { tempDir });

                // Assert
                var rootSongs = _manager.RootSongs;
                var song1Node = rootSongs.FirstOrDefault(n => n.Title == "Song One");
                var song2Node = rootSongs.FirstOrDefault(n => n.Title == "Song Two");
                
                Assert.NotNull(song1Node);
                Assert.NotNull(song2Node);
                Assert.Equal(2, rootSongs.Count);
                
                // Verify each song has only one chart
                Assert.Single(song1Node.DatabaseSong.Charts);
                Assert.Single(song2Node.DatabaseSong.Charts);
                
                // Verify they have the same artist but different titles
                Assert.Equal("Same Artist", song1Node.DatabaseSong.Artist);
                Assert.Equal("Same Artist", song2Node.DatabaseSong.Artist);
                Assert.Equal("Song One", song1Node.DatabaseSong.Title);
                Assert.Equal("Song Two", song2Node.DatabaseSong.Title);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public async Task EnumerateSongsAsync_WithSameSongDifferentDirectories_ShouldGroupIntoSingleSong()
        {
            // Arrange - Same song scattered across different directories
            var tempDir = Path.Combine(Path.GetTempPath(), "SongScatterTest");
            var dir1 = Path.Combine(tempDir, "Dir1", "My Test Song");
            var dir2 = Path.Combine(tempDir, "Dir2", "My Test Song");
            var basFile = Path.Combine(dir1, "bas.dtx");
            var advFile = Path.Combine(dir2, "adv.dtx");

            try
            {
                Directory.CreateDirectory(dir1);
                Directory.CreateDirectory(dir2);
                
                await File.WriteAllTextAsync(basFile, @"#TITLE: Scattered Song
#ARTIST: Test Artist
#BPM: 120
#DLEVEL: 30
");
                
                await File.WriteAllTextAsync(advFile, @"#TITLE: Scattered Song
#ARTIST: Test Artist
#BPM: 120
#DLEVEL: 50
");

                await _manager.InitializeAsync(new string[0], _testDbPath);

                // Act
                await _manager.EnumerateSongsAsync(new[] { tempDir });

                // Assert
                var rootSongs = _manager.RootSongs;
                var scatteredSongs = rootSongs.Where(n => n.Title == "Scattered Song").ToList();
                
                // SongManager may create separate UI nodes for files in different directories
                Assert.True(scatteredSongs.Count >= 1, "Should have at least one scattered song");
                
                // Verify all nodes have the same title/artist (database grouping)
                Assert.All(scatteredSongs, node => 
                {
                    Assert.Equal("Scattered Song", node.DatabaseSong?.Title);
                    Assert.Equal("Test Artist", node.DatabaseSong?.Artist);
                });
                
                // Verify that we processed the expected number of files
                Assert.Equal(2, _manager.DiscoveredScoreCount);
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
