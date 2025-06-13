using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DTX.Song;
using Xunit;

namespace DTXMania.Test.Song
{
    /// <summary>
    /// Unit tests for SongManager class
    /// Tests song database management and enumeration using singleton pattern
    /// </summary>
    public class SongManagerTests : IDisposable
    {
        private SongManager _manager;

        public SongManagerTests()
        {
            // Get singleton instance and clear it for each test
            _manager = SongManager.Instance;
            _manager.Clear();
        }

        public void Dispose()
        {
            // Clean up after each test
            _manager?.Clear();
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
        public void Instance_ShouldInitializeWithDefaults()
        {
            // Arrange & Act
            var manager = SongManager.Instance;

            // Assert
            Assert.NotNull(manager.SongsDatabase);
            Assert.Empty(manager.SongsDatabase);
            Assert.NotNull(manager.RootSongs);
            Assert.Empty(manager.RootSongs);
            Assert.Equal(0, manager.DatabaseScoreCount);
            Assert.Equal(0, manager.DiscoveredScoreCount);
            Assert.Equal(0, manager.EnumeratedFileCount);
            Assert.False(manager.IsEnumerating);
            Assert.False(manager.IsInitialized);
        }

        #endregion

        #region Database Management Tests

        [Fact]
        public async Task LoadSongsDatabaseAsync_WithNonExistentFile_ShouldReturnFalse()
        {
            // Arrange
            var nonExistentPath = @"C:\NonExistent\songs.db";

            // Act
            var result = await _manager.LoadSongsDatabaseAsync(nonExistentPath);

            // Assert
            Assert.False(result);
            Assert.Equal(0, _manager.DatabaseScoreCount);
        }

        [Fact]
        public async Task SaveSongsDatabaseAsync_WithValidPath_ShouldReturnTrue()
        {
            // Arrange
            var tempFile = Path.GetTempFileName();

            try
            {
                // Act
                var result = await _manager.SaveSongsDatabaseAsync(tempFile);

                // Assert
                Assert.True(result);
                Assert.True(File.Exists(tempFile));
                
                var content = await File.ReadAllTextAsync(tempFile);
                Assert.Contains("\"scores\"", content);
                Assert.Contains("\"rootNodes\"", content);
            }
            finally
            {
                if (File.Exists(tempFile))
                    File.Delete(tempFile);
            }
        }

        [Fact]
        public async Task SaveAndLoadDatabase_ShouldRoundTrip()
        {
            // Arrange
            var tempFile = Path.GetTempFileName();

            try
            {
                // Add some test data
                await _manager.EnumerateSongsAsync(new[] { "NonExistentPath" }); // This will create empty structure

                // Act - Save
                var saveResult = await _manager.SaveSongsDatabaseAsync(tempFile);
                
                // Clear and reload
                _manager.Clear();
                var loadResult = await _manager.LoadSongsDatabaseAsync(tempFile);

                // Assert
                Assert.True(saveResult);
                Assert.True(loadResult);
            }
            finally
            {
                if (File.Exists(tempFile))
                    File.Delete(tempFile);
            }
        }

        [Fact]
        public async Task SaveSongsDatabaseAsync_WithInvalidPath_ShouldReturnFalse()
        {
            // Arrange
            var invalidPath = @"C:\NonExistent\Directory\songs.db";

            // Act
            var result = await _manager.SaveSongsDatabaseAsync(invalidPath);

            // Assert
            Assert.False(result);
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

                // Act
                var result = await _manager.EnumerateSongsAsync(new[] { tempDir });

                // Assert
                Assert.Equal(1, result);
                Assert.Equal(1, _manager.DiscoveredScoreCount);
                Assert.True(_manager.DatabaseScoreCount > 0);
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

                await _manager.EnumerateSongsAsync(new[] { tempDir });

                // Act
                var result = _manager.FindSongByPath(dtxFile);

                // Assert
                Assert.NotNull(result);
                Assert.Equal("Test Song", result.Metadata.Title);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void FindSongByPath_WithNonExistentPath_ShouldReturnNull()
        {
            // Act
            var result = _manager.FindSongByPath(@"C:\NonExistent\test.dtx");

            // Assert
            Assert.Null(result);
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

                await _manager.EnumerateSongsAsync(new[] { tempDir });

                // Act
                var rockSongs = _manager.GetSongsByGenre("Rock").ToList();
                var popSongs = _manager.GetSongsByGenre("Pop").ToList();

                // Assert
                Assert.Equal(2, rockSongs.Count);
                Assert.Single(popSongs);
                Assert.All(rockSongs, s => Assert.Equal("Rock", s.Metadata.Genre));
                Assert.All(popSongs, s => Assert.Equal("Pop", s.Metadata.Genre));
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public void Clear_ShouldResetAllData()
        {
            // Arrange
            var tempDir = Path.Combine(Path.GetTempPath(), "SongManagerTestClear");
            var dtxFile = Path.Combine(tempDir, "test.dtx");
            
            try
            {
                Directory.CreateDirectory(tempDir);
                File.WriteAllText(dtxFile, "#TITLE: Test Song\n#DLEVEL: 50\n");
                
                // Enumerate to populate data
                _manager.EnumerateSongsAsync(new[] { tempDir }).Wait();
                
                // Verify we have data
                Assert.True(_manager.DatabaseScoreCount > 0);

                // Act
                _manager.Clear();

                // Assert
                Assert.Equal(0, _manager.DatabaseScoreCount);
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
                var result = await _manager.InitializeAsync(new[] { tempDir });

                // Assert
                Assert.True(result);
                Assert.True(_manager.IsInitialized);
                Assert.True(_manager.DatabaseScoreCount > 0);
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
                await _manager.InitializeAsync(new[] { tempDir });
                Assert.True(_manager.IsInitialized);

                // Act - Second initialization
                var result = await _manager.InitializeAsync(new[] { tempDir });

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
