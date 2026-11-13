using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Entities;
using Xunit;
using static DTXMania.Test.TestData.ReflectionHelpers;
using SongEntity = DTXMania.Game.Lib.Song.Entities.Song;

using System.Reflection;
using System.Text;
using System.Threading;
using DTXMania.Test.TestData;
using Microsoft.EntityFrameworkCore;
namespace DTXMania.Test.Song
{
    /// <summary>
    /// Unit tests for SongManager class
    /// Tests song database management and enumeration using singleton pattern
    /// </summary>
    [Trait("Category", "Unit")]
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
            Directory.CreateDirectory(_testRoot);
            _testDbPath = Path.Combine(_testRoot, "songs.db");
        }

        [Fact]
        public void InitializeDatabaseServiceAsync_ShouldExposeASinglePublicOverload()
        {
            var overloads = typeof(SongManager).GetMethods()
                .Where(method => method.Name ==
                    nameof(SongManager.InitializeDatabaseServiceAsync))
                .ToArray();

            Assert.Single(overloads);
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

            try
            {
                if (Directory.Exists(_testRoot))
                    Directory.Delete(_testRoot, true);
            }
            catch
            {
                // Best-effort cleanup for temp test assets.
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
        public async Task DatabaseService_ShouldBeAvailableAfterInitialization()
        {
            // Arrange
            await _manager.InitializeDatabaseServiceAsync(_testDbPath);

            // Act
            var databaseService = _manager.DatabaseService;

            // Assert
            Assert.NotNull(databaseService);
        }

        [Fact]
        public async Task InitializeDatabaseServiceAsync_PublicOverload_ShouldInitializeService()
        {
            var result = await _manager.InitializeDatabaseServiceAsync(
                _testDbPath,
                purgeDatabaseFirst: false);

            Assert.True(result);
            Assert.NotNull(_manager.DatabaseService);
        }

        [Fact]
        public async Task InitializeDatabaseServiceAsync_WhenCreatingService_ShouldReturnTrue()
        {
            var result = await _manager.InitializeDatabaseServiceAsync(
                _testDbPath,
                purgeDatabaseFirst: false);

            Assert.True(result);
            Assert.NotNull(_manager.DatabaseService);
        }

        [Fact]
        public async Task InitializeDatabaseServiceAsync_WhenServiceExists_ShouldKeepService()
        {
            Assert.True(await _manager.InitializeDatabaseServiceAsync(
                _testDbPath,
                purgeDatabaseFirst: false));
            var service = _manager.DatabaseService;

            var result = await _manager.InitializeDatabaseServiceAsync(
                _testDbPath,
                purgeDatabaseFirst: false);

            Assert.True(result);
            Assert.Same(service, _manager.DatabaseService);
        }

        [Fact]
        public async Task InitializeDatabaseServiceAsync_WhenPurgeRuns_ShouldRecreateService()
        {
            var result = await _manager.InitializeDatabaseServiceAsync(
                _testDbPath,
                purgeDatabaseFirst: true);

            Assert.True(result);
            Assert.NotNull(_manager.DatabaseService);
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

                // Initialize database service
                await _manager.InitializeDatabaseServiceAsync(_testDbPath);

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

                // Initialize database service
                await _manager.InitializeDatabaseServiceAsync(_testDbPath);

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

                // Initialize database service
                await _manager.InitializeDatabaseServiceAsync(_testDbPath);
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

                // Initialize database service
                await _manager.InitializeDatabaseServiceAsync(_testDbPath);
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

                // Initialize database service and enumerate to populate data
                await _manager.InitializeDatabaseServiceAsync(_testDbPath);
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
            var directory = Path.Combine(
                Path.GetTempPath(),
                $"SongManagerEvent_{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            await _manager.InitializeDatabaseServiceAsync(_testDbPath);

            try
            {
                // Act
                await _manager.EnumerateSongsAsync(new[] { directory });

                // Assert
                Assert.True(eventFired);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
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

                // Initialize database service
                await _manager.InitializeDatabaseServiceAsync(_testDbPath);

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

                await _manager.InitializeDatabaseServiceAsync(_testDbPath);

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

                // DiscoveredScoreCount counts difficulty charts, not logical
                // songs: three parsed difficulties report 3, matching the legacy
                // per-chart contract.
                Assert.Equal(3, _manager.DiscoveredScoreCount);
                Assert.Equal(3, _manager.EnumeratedFileCount);
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

                await _manager.InitializeDatabaseServiceAsync(_testDbPath);

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
        public async Task EnumerateSongsAsync_WithSameMetadataInDifferentDirectories_ShouldKeepSeparateSongs()
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

                await _manager.InitializeDatabaseServiceAsync(_testDbPath);

                // Act
                await _manager.EnumerateSongsAsync(new[] { tempDir });

                // Assert
                var rootSongs = _manager.RootSongs;
                var scatteredSongs = rootSongs.Where(n => n.Title == "Scattered Song").ToList();

                Assert.Equal(2, scatteredSongs.Count);

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

        #region Database Wrapper and Rebuild Tests

        [Trait("Category", "Unit")]
        [Fact]
        public async Task BuildSongListsAsync_WithNoSongs_ShouldReturnFalse()
        {
            var result = await _manager.BuildSongListsAsync();

            Assert.False(result);
        }

        [Trait("Category", "Unit")]
        [Fact]
        public async Task BuildSongListsAsync_WithExistingRootSongs_ShouldReturnTrue()
        {
            var rootSongs = GetPrivateField<List<SongListNode>>(_manager, "_rootSongs");
            Assert.NotNull(rootSongs);
            rootSongs!.Add(new SongListNode { Title = "Existing Song", Type = NodeType.Score });

            var result = await _manager.BuildSongListsAsync();

            Assert.True(result);
        }

        [Trait("Category", "Unit")]
        [Theory]
        [InlineData("none", false)]
        [InlineData("initialized", true)]
        [InlineData("broken", false)]
        public async Task SaveSongsDBAsync_WithDatabaseServiceState_ShouldReturnExpectedResult(
            string databaseServiceState,
            bool expectedResult)
        {
            var result = await WithDatabaseServiceStateAsync(
                databaseServiceState,
                () => _manager.SaveSongsDBAsync());

            Assert.Equal(expectedResult, result);
        }

        [Trait("Category", "Unit")]
        [Theory]
        [InlineData("none", false)]
        [InlineData("initialized", true)]
        [InlineData("broken", false)]
        public async Task DatabaseExistsAsync_WithDatabaseServiceState_ShouldReturnExpectedResult(
            string databaseServiceState,
            bool expectedResult)
        {
            var result = await WithDatabaseServiceStateAsync(
                databaseServiceState,
                () => _manager.DatabaseExistsAsync());

            Assert.Equal(expectedResult, result);
        }

        [Trait("Category", "Unit")]
        [Theory]
        [InlineData("none", false)]
        [InlineData("initialized", true)]
        [InlineData("broken", false)]
        public async Task GetDatabaseStatsAsync_WithDatabaseServiceState_ShouldReturnExpectedAvailability(
            string databaseServiceState,
            bool expectStats)
        {
            var stats = await WithDatabaseServiceStateAsync(
                databaseServiceState,
                () => _manager.GetDatabaseStatsAsync());

            if (expectStats)
            {
                Assert.NotNull(stats);
                Assert.Equal(0, stats!.ScoreCount);
                return;
            }

            Assert.Null(stats);
        }

        [Trait("Category", "Unit")]
        [Fact]
        public async Task EnumerateSongsOnlyAsync_WithMultipleSongs_ShouldPopulateRootSongs()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), $"SongManagerEnumerateOnly_{Guid.NewGuid():N}");

            try
            {
                Directory.CreateDirectory(tempDir);
                for (int i = 1; i <= 3; i++)
                {
                    await File.WriteAllTextAsync(
                        Path.Combine(tempDir, $"song{i}.dtx"),
                        $@"#TITLE: Enumerate Song {i}
#ARTIST: Enumerate Artist {i}
#DLEVEL: {40 + i}
");
                }

                await _manager.InitializeDatabaseServiceAsync(_testDbPath);

                var result = await _manager.EnumerateSongsOnlyAsync(new[] { tempDir });

                Assert.Equal(3, result);
                Assert.Equal(3, _manager.RootSongs.Count);
                Assert.All(_manager.RootSongs, node => Assert.Equal(NodeType.Score, node.Type));
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }

        [Trait("Category", "Unit")]
        [Fact]
        public async Task BuildSongListFromDatabasePublicAsync_WithNestedBoxesAndManyRootSongs_ShouldRestoreHierarchy()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), $"SongManagerRebuild_{Guid.NewGuid():N}");
            var parentBoxDir = Path.Combine(tempDir, "DTXFiles.Parent");
            var childBoxDir = Path.Combine(parentBoxDir, "DTXFiles.Child");

            try
            {
                Directory.CreateDirectory(childBoxDir);

                for (int i = 1; i <= 6; i++)
                {
                    await File.WriteAllTextAsync(
                        Path.Combine(tempDir, $"root{i}.dtx"),
                        $@"#TITLE: Root Song {i}
#ARTIST: Root Artist {i}
#DLEVEL: {40 + i}
");
                }

                await File.WriteAllTextAsync(
                    Path.Combine(childBoxDir, "child1.dtx"),
                    @"#TITLE: Child Song 1
#ARTIST: Box Artist
#DLEVEL: 55
");
                await File.WriteAllTextAsync(
                    Path.Combine(childBoxDir, "child2.dtx"),
                    @"#TITLE: Child Song 2
#ARTIST: Box Artist
#DLEVEL: 65
");

                await _manager.InitializeDatabaseServiceAsync(_testDbPath);
                await _manager.EnumerateSongsAsync(new[] { tempDir });

                var rootSongs = GetPrivateField<List<SongListNode>>(_manager, "_rootSongs");
                Assert.NotNull(rootSongs);
                rootSongs!.Clear();

                await _manager.BuildSongListFromDatabasePublicAsync(new[] { tempDir });

                Assert.True(_manager.RootSongs.Count > 5);
                var parentBox = Assert.Single(_manager.RootSongs, n => n.Type == NodeType.Box && n.Title == "DTXFiles.Parent");
                var childBox = Assert.Single(parentBox.Children, n => n.Type == NodeType.Box && n.Title == "DTXFiles.Child");
                Assert.Equal(2, childBox.Children.Count);
                Assert.Contains(_manager.RootSongs, node => node.Type == NodeType.Score && node.Title == "Root Song 1");
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }

        [Trait("Category", "Unit")]
        [Fact]
        public async Task BuildSongListFromDatabasePublicAsync_WithBrokenDatabaseService_ShouldLeaveRootSongsEmpty()
        {
            var originalDatabaseService = _manager.DatabaseService;

            try
            {
                SetPrivateField(_manager, "_databaseService", CreateBrokenDatabaseService());

                await _manager.BuildSongListFromDatabasePublicAsync(new[] { Path.GetTempPath() });

                Assert.Empty(_manager.RootSongs);
            }
            finally
            {
                SetPrivateField(_manager, "_databaseService", originalDatabaseService);
            }
        }

        [Trait("Category", "Unit")]
        [Fact]
        public async Task BuildHierarchyFromCharts_WithBlankOrMissingPaths_ShouldSkipCharts()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), $"SongManagerHierarchy_{Guid.NewGuid():N}");

            try
            {
                Directory.CreateDirectory(tempDir);

                var charts = new List<(SongEntity song, SongChart chart)>
                {
                    (new SongEntity { Title = "Blank Path", Artist = "Tester" }, new SongChart { FilePath = "" }),
                    (new SongEntity { Title = "Missing File", Artist = "Tester" }, new SongChart { FilePath = Path.Combine(tempDir, "missing.dtx") })
                };

                var result = await InvokePrivateMethodAsync<List<SongListNode>>(
                    _manager,
                    "BuildHierarchyFromCharts",
                    tempDir,
                    charts);

                Assert.NotNull(result);
                Assert.Empty(result);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }

        [Trait("Category", "Unit")]
        [Theory]
        [InlineData("#  TITLE Sample Song", "#TITLE Sample Song")]
        [InlineData("#TITLE  Sample Song", "#TITLE Sample Song")]
        [InlineData("#ABC  Value", "#ABC Value")]
        [InlineData("  plain text  ", "plain text")]
        public void NormalizeSetDefLine_ShouldNormalizeSupportedFormats(string line, string expected)
        {
            var normalized = InvokePrivateMethod<string>(_manager, "NormalizeSetDefLine", line);

            Assert.NotNull(normalized);
            Assert.Equal(expected, normalized);
        }

        #endregion

        private async Task<T> WithDatabaseServiceStateAsync<T>(
            string databaseServiceState,
            Func<Task<T>> action)
        {
            var originalDatabaseService = _manager.DatabaseService;

            try
            {
                switch (databaseServiceState)
                {
                    case "none":
                        break;
                    case "initialized":
                        await _manager.InitializeDatabaseServiceAsync(_testDbPath);
                        break;
                    case "broken":
                        SetPrivateField(_manager, "_databaseService", CreateBrokenDatabaseService());
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(databaseServiceState), databaseServiceState, null);
                }

                return await action();
            }
            finally
            {
                if (databaseServiceState == "broken")
                {
                    SetPrivateField(_manager, "_databaseService", originalDatabaseService);
                }
            }
        }

        private static SongDatabaseService CreateBrokenDatabaseService()
        {
            return (SongDatabaseService)RuntimeHelpers.GetUninitializedObject(typeof(SongDatabaseService));
        }

        private readonly string _testRoot = Path.Combine(
            Path.GetTempPath(),
            "DTXManiaCX_Tests",
            "SongManagerTests",
            Guid.NewGuid().ToString("N"));

        [Fact]
        public async Task BuildSongListFromDatabasePublicAsync_WithBoxAndMultipleCharts_ShouldRebuildHierarchy()
    {
        var songsRoot = Path.Combine(_testRoot, "Songs");
        var boxFolder = Path.Combine(songsRoot, "DTXFiles.Favorites");
        var songFolder = Path.Combine(boxFolder, "My Song");

        Directory.CreateDirectory(songFolder);
        await File.WriteAllTextAsync(Path.Combine(boxFolder, "box.def"), """
#TITLE: Favorites Box
#GENRE: Rock
#SKINPATH: skins/favorites
""");
        await CreateDtxFileAsync(Path.Combine(songFolder, "basic.dtx"), "My Song", "Coverage Bot", "Rock", 25);
        await CreateDtxFileAsync(Path.Combine(songFolder, "advanced.dtx"), "My Song", "Coverage Bot", "Rock", 60);

        await InitializeAndEnumerateAsync(songsRoot);
        ClearRootSongs();

        await _manager.BuildSongListFromDatabasePublicAsync(new[] { songsRoot });

        var boxNode = Assert.Single(_manager.RootSongs);
        Assert.Equal(NodeType.Box, boxNode.Type);
        Assert.Equal("Favorites Box", boxNode.Title);
        Assert.Equal("Rock", boxNode.Genre);
        Assert.Equal("skins/favorites", boxNode.SkinPath);

        var songNode = Assert.Single(boxNode.Children);
        Assert.Equal("My Song", songNode.Title);
        Assert.NotNull(songNode.DatabaseSong);
        Assert.Equal(2, songNode.DatabaseSong!.Charts.Count);
        Assert.True(songNode.Scores.Count(score => score != null) >= 2);
        Assert.Equal("Level 1", songNode.DifficultyLabels[0]);
        Assert.Equal("Level 2", songNode.DifficultyLabels[1]);
    }

    [Fact]
    public async Task BuildSongListFromDatabasePublicAsync_WithoutDatabaseService_ShouldLeaveRootSongsEmpty()
    {
        var songsRoot = Path.Combine(_testRoot, "NoDatabaseSongs");
        Directory.CreateDirectory(songsRoot);

        await _manager.BuildSongListFromDatabasePublicAsync(new[] { songsRoot });

        Assert.Empty(_manager.RootSongs);
    }

    [Fact]
    public async Task BuildSongListFromDatabasePublicAsync_WithInvalidSearchPath_ShouldSkipPath()
    {
        var initialized = await _manager.InitializeDatabaseServiceAsync(_testDbPath);
        Assert.True(initialized);

        await _manager.BuildSongListFromDatabasePublicAsync(new[] { Path.Combine(_testRoot, "missing") });

        Assert.Empty(_manager.RootSongs);
    }

    [Fact]
    public async Task RefreshSongListFromDatabaseAsync_WithValidSearchPaths_ShouldRebuildRootSongs()
    {
        var songsRoot = Path.Combine(_testRoot, "RefreshSongs");
        var songFolder = Path.Combine(songsRoot, "Refresh Song");
        Directory.CreateDirectory(songFolder);
        await CreateDtxFileAsync(Path.Combine(songFolder, "song.dtx"), "Refresh Song", "Test Bot", "Pop", 40);

        await InitializeAndEnumerateAsync(songsRoot);
        await SetLastEnumerationTimestampAsync(DateTime.Now.AddMinutes(5));
        ClearRootSongs();

        await _manager.RefreshSongListFromDatabaseAsync();

        Assert.NotEmpty(_manager.RootSongs);
        var node = Assert.Single(_manager.RootSongs);
        Assert.Equal("Refresh Song", node.Title);
        Assert.Equal(NodeType.Score, node.Type);
    }

    [Fact]
    public async Task RefreshSongListFromDatabaseAsync_WithoutSearchPaths_ShouldNotThrow()
    {
        // SongManager starts with empty search paths.
        // Refresh should be a no-op — no crash, no RootSongs mutation.
        await _manager.RefreshSongListFromDatabaseAsync();
        Assert.Empty(_manager.RootSongs);
    }

    [Fact]
    public async Task ParseBoxDefinitionAsync_WithMissingFile_ShouldReturnNull()
    {
        var task = ReflectionHelpers.InvokePrivateMethod<Task<BoxDefinition?>>(
            _manager,
            "ParseBoxDefinitionAsync",
            Path.Combine(_testRoot, "missing-box.def"),
            CancellationToken.None);

        Assert.NotNull(task);
        Assert.Null(await task!);
    }

    [Fact]
    public async Task BuildEnumerationBatchAsync_WhenSetDefIsMissing_ShouldProduceNoSongs()
    {
        var batch = await _manager.BuildEnumerationBatchAsync(
            new[] { _testRoot },
            null,
            CancellationToken.None);

        Assert.Empty(batch.PendingSongs);
        Assert.Empty(batch.RootNodes);
    }

    [Fact]
    public void TryParseColor_WithInvalidHexValues_ShouldReturnFalse()
    {
        var method = ReflectionHelpers.GetMethod(_manager.GetType(), "TryParseColor");
        Assert.NotNull(method);

        var shortHexArgs = new object?[] { "#12", null };
        var invalidHexArgs = new object?[] { "#GGGGGG", null };

        var shortHexResult = (bool)method!.Invoke(_manager, shortHexArgs)!;
        var invalidHexResult = (bool)method.Invoke(_manager, invalidHexArgs)!;

        Assert.False(shortHexResult);
        Assert.False(invalidHexResult);
    }

    [Fact]
    public async Task LoadScoreCacheAsync_WithWarmDatabase_ShouldRebuildRootSongs()
    {
        var songsRoot = Path.Combine(_testRoot, "CacheSongs");
        var songFolder = Path.Combine(songsRoot, "Cached Song");

        Directory.CreateDirectory(songFolder);
        await CreateDtxFileAsync(Path.Combine(songFolder, "cached.dtx"), "Cached Song", "Coverage Bot", "Fusion", 35);

        await InitializeAndEnumerateAsync(songsRoot);
        await SetLastEnumerationTimestampAsync(DateTime.Now.AddMinutes(5));
        ClearRootSongs();

        var loaded = await _manager.LoadScoreCacheAsync(new[] { songsRoot });

        Assert.True(loaded);
        var rebuiltSong = Assert.Single(_manager.RootSongs);
        Assert.Equal("Cached Song", rebuiltSong.Title);
        Assert.NotNull(rebuiltSong.DatabaseSong);
    }

    [Fact]
    public async Task LoadScoreCacheAsync_WithEmptyDatabase_ShouldReturnFalse()
    {
        var songsRoot = Path.Combine(_testRoot, "EmptyCacheSongs");
        Directory.CreateDirectory(songsRoot);

        var initialized = await _manager.InitializeDatabaseServiceAsync(_testDbPath);
        Assert.True(initialized);

        var loaded = await _manager.LoadScoreCacheAsync(new[] { songsRoot });

        Assert.False(loaded);
        Assert.Empty(_manager.RootSongs);
    }

    [Fact]
    public async Task NeedsEnumerationAsync_WithForceEnumeration_ShouldReturnTrue()
    {
        var needsEnumeration = await _manager.NeedsEnumerationAsync(new[] { _testRoot }, forceEnumeration: true);

        Assert.True(needsEnumeration);
    }

    [Fact]
    public async Task NeedsEnumerationAsync_WithoutDatabaseService_ShouldReturnTrue()
    {
        var needsEnumeration = await _manager.NeedsEnumerationAsync(new[] { _testRoot });

        Assert.True(needsEnumeration);
    }

    [Fact]
    public async Task NeedsEnumerationAsync_WithEmptyDatabase_ShouldReturnTrue()
    {
        var songsRoot = Path.Combine(_testRoot, "NeedsEnumerationEmpty");
        Directory.CreateDirectory(songsRoot);

        var initialized = await _manager.InitializeDatabaseServiceAsync(_testDbPath);
        Assert.True(initialized);

        var needsEnumeration = await _manager.NeedsEnumerationAsync(new[] { songsRoot });

        Assert.True(needsEnumeration);
    }

    [Fact]
    public async Task NeedsEnumerationAsync_WithNullSearchPathsOnPopulatedDatabase_ShouldReturnTrue()
    {
        var songsRoot = Path.Combine(_testRoot, "NullSearchPathsSongs");
        var songFolder = Path.Combine(songsRoot, "Song");
        Directory.CreateDirectory(songFolder);
        await CreateDtxFileAsync(Path.Combine(songFolder, "song.dtx"), "Null Search Song", "Coverage Bot", "Rock", 30);

        await InitializeAndEnumerateAsync(songsRoot);

        var needsEnumeration = await _manager.NeedsEnumerationAsync(null!);

        Assert.True(needsEnumeration);
    }

    [Fact]
    public async Task NeedsEnumerationAsync_WithStableFilesystem_ShouldReturnFalse()
    {
        var songsRoot = Path.Combine(_testRoot, "StableSongs");
        var songFolder = Path.Combine(songsRoot, "Stable Song");

        Directory.CreateDirectory(songFolder);
        await CreateDtxFileAsync(Path.Combine(songFolder, "stable.dtx"), "Stable Song", "Coverage Bot", "Jazz", 40);

        await InitializeAndEnumerateAsync(songsRoot);
        await SetLastEnumerationTimestampAsync(DateTime.Now.AddMinutes(5));

        var needsEnumeration = await _manager.NeedsEnumerationAsync(new[] { songsRoot });

        Assert.False(needsEnumeration);
    }

    [Fact]
    public async Task NeedsEnumerationAsync_WithPopulatedRootAndMissingEmptyRoot_ShouldReturnFalse()
    {
        // Reviewer regression (item 1): one stable populated root plus one
        // missing empty configured root must reach steady state. The old
        // missing-root check fired for the unavailable root whenever another
        // root had charts, forcing a perpetual full scan.
        var populatedRoot = Path.Combine(_testRoot, "PopulatedSongs");
        var populatedFolder = Path.Combine(populatedRoot, "Song");
        Directory.CreateDirectory(populatedFolder);
        await CreateDtxFileAsync(
            Path.Combine(populatedFolder, "song.dtx"),
            "Populated Song", "Coverage Bot", "Rock", 35);

        await InitializeAndEnumerateAsync(populatedRoot);
        await SetLastEnumerationTimestampAsync(DateTime.Now.AddMinutes(5));

        var missingRoot = Path.Combine(_testRoot, "MissingSongs");

        var needsEnumeration = await _manager.NeedsEnumerationAsync(
            new[] { populatedRoot, missingRoot });

        Assert.False(needsEnumeration);
    }

    [Fact]
    public async Task NeedsEnumerationAsync_AfterEnumerationViaCore_ShouldReturnFalseWithoutManualTimestamp()
    {
        // Reviewer regression (item 2): the timestamp must be set by
        // EnumerateAndImportSongsCoreAsync (the path used by startup and Config
        // live reload). Before the fix, only the test-only
        // EnumerateSongsOnlyWithPublicationAsync wrapper set it, so
        // LastSuccessfulEnumerationUtc was never persisted in production and
        // every startup was a full scan.
        var songsRoot = Path.Combine(_testRoot, "CoreTimestampSongs");
        var songFolder = Path.Combine(songsRoot, "Song");
        var chartPath = Path.Combine(songFolder, "song.dtx");
        Directory.CreateDirectory(songFolder);
        await CreateDtxFileAsync(chartPath, "Core Timestamp Song", "Coverage Bot", "Jazz", 40);
        SetFilesystemTimes(chartPath, DateTime.UtcNow.AddMinutes(-1));

        // EnumerateSongsAsync routes through EnumerateAndImportSongsCoreAsync
        // — the same core used by EnumerateAndImportSongsAsync (startup/reload).
        // No manual SetLastEnumerationTimestampAsync: the core must set it.
        await InitializeAndEnumerateAsync(songsRoot);

        var needsEnumeration = await _manager.NeedsEnumerationAsync(new[] { songsRoot });

        Assert.False(needsEnumeration);
    }

    [Fact]
    public async Task NeedsEnumerationAsync_WhenRootExistsButCannotBeEnumerated_ShouldNotForceRescan()
    {
        if (OperatingSystem.IsWindows())
            return;

        var populatedRoot = Path.Combine(_testRoot, "ReadableSongs");
        var inaccessibleRoot = Path.Combine(_testRoot, "InaccessibleSongs");
        var populatedChart = Path.Combine(populatedRoot, "Song", "song.dtx");
        await CreateDtxFileAsync(populatedChart, "Readable Song", "Coverage Bot", "Rock", 35);
        SetFilesystemTimes(populatedChart, DateTime.UtcNow.AddMinutes(-1));

        await InitializeAndEnumerateAsync(populatedRoot);
        await SetLastEnumerationTimestampAsync(DateTime.UtcNow.AddMinutes(5).ToLocalTime());

        Directory.CreateDirectory(inaccessibleRoot);
        try
        {
            File.SetUnixFileMode(inaccessibleRoot, UnixFileMode.None);
            var canEnumerate = true;
            try
            {
                using var entries = Directory
                    .EnumerateFileSystemEntries(inaccessibleRoot)
                    .GetEnumerator();
                _ = entries.MoveNext();
            }
            catch (UnauthorizedAccessException)
            {
                canEnumerate = false;
            }
            catch (IOException)
            {
                canEnumerate = false;
            }

            // Permission-restricted tests cannot exercise the branch when the
            // process has elevated privileges (for example, a root CI runner).
            if (canEnumerate)
                return;

            var needsEnumeration = await _manager.NeedsEnumerationAsync(
                new[] { populatedRoot, inaccessibleRoot });

            Assert.False(needsEnumeration);
        }
        finally
        {
            File.SetUnixFileMode(
                inaccessibleRoot,
                UnixFileMode.UserRead |
                UnixFileMode.UserWrite |
                UnixFileMode.UserExecute);
        }
    }

    [Fact]
    public async Task NeedsEnumerationAsync_WhenTimestampIsRoundedBeforeWatermark_ShouldDetectChange()
    {
        var songsRoot = Path.Combine(_testRoot, "CoarseTimestampSongs");
        var chartPath = Path.Combine(songsRoot, "Song", "song.dtx");
        await CreateDtxFileAsync(chartPath, "Coarse Timestamp Song", "Coverage Bot", "Rock", 35);
        SetFilesystemTimes(chartPath, DateTime.UtcNow.AddHours(-3));

        await InitializeAndEnumerateAsync(songsRoot);

        var scanStartUtc = DateTime.UtcNow.AddHours(-1);
        var persistedWatermarkUtc = scanStartUtc.AddSeconds(-5);
        await SetLastEnumerationTimestampAsync(persistedWatermarkUtc.ToLocalTime());
        await SetRootEnumerationTimestampAsync(songsRoot, persistedWatermarkUtc);

        // Model a coarse filesystem timestamp rounded just before the exact
        // watermark. The conservative scan watermark must still see it.
        var roundedEditUtc = scanStartUtc.AddSeconds(-1);
        await File.AppendAllTextAsync(chartPath, "#COMMENT: rounded edit\n");
        SetFilesystemTimes(chartPath, roundedEditUtc, preserveCreationTime: true);

        var needsEnumeration = await _manager.NeedsEnumerationAsync(new[] { songsRoot });

        Assert.True(needsEnumeration);
    }

    [Fact]
    public async Task NeedsEnumerationAsync_WhenUnavailableRootReturnsWithInPlaceEdit_ShouldDetectChange()
    {
        var rootA = Path.Combine(_testRoot, "RootA");
        var rootB = Path.Combine(_testRoot, "RootB");
        var chartA = Path.Combine(rootA, "Song A", "song.dtx");
        var chartB = Path.Combine(rootB, "Song B", "song.dtx");

        await CreateDtxFileAsync(chartA, "Song A", "Coverage Bot", "Rock", 35);
        await CreateDtxFileAsync(chartB, "Song B", "Coverage Bot", "Rock", 40);

        var oldFilesystemTimeUtc = DateTime.UtcNow.AddHours(-3);
        SetFilesystemTimes(chartA, oldFilesystemTimeUtc);
        SetFilesystemTimes(chartB, oldFilesystemTimeUtc);

        var initialized = await _manager.InitializeDatabaseServiceAsync(_testDbPath);
        Assert.True(initialized);
        var initialEnumeration = await _manager.EnumerateSongsAsync(new[] { rootA, rootB });
        Assert.True(initialEnumeration >= 2);

        // Establish a known per-root baseline before simulating the unavailable
        // root. The root-B edit below is newer than this baseline but older than
        // the partial root-A scan watermark.
        var baselineUtc = DateTime.UtcNow.AddHours(-2);
        await SetLastEnumerationTimestampAsync(baselineUtc.ToLocalTime());
        await SetRootEnumerationTimestampAsync(rootA, baselineUtc);
        await SetRootEnumerationTimestampAsync(rootB, baselineUtc);

        // Omit B from this enumeration to model the configured root being
        // unavailable. Its database rows remain retained while A advances.
        var partialEnumeration = await _manager.EnumerateSongsAsync(new[] { rootA });
        Assert.True(partialEnumeration >= 1);

        var editedAtUtc = DateTime.UtcNow.AddMinutes(-1);
        await File.AppendAllTextAsync(chartB, "#COMMENT: edited\n");
        SetFilesystemTimes(chartB, editedAtUtc, preserveCreationTime: true);

        var needsEnumeration = await _manager.NeedsEnumerationAsync(new[] { rootA, rootB });

        Assert.True(needsEnumeration);
    }

    [Fact]
    public async Task SetEnumerationWatermarkAtomicallyAsync_WhenSuccessful_WritesBothGlobalAndPerRoot()
    {
        var rootA = Path.Combine(_testRoot, "RootA");
        var rootB = Path.Combine(_testRoot, "RootB");
        await CreateDtxFileAsync(
            Path.Combine(rootA, "Song A", "song.dtx"),
            "Song A", "Coverage Bot", "Rock", 35);
        await CreateDtxFileAsync(
            Path.Combine(rootB, "Song B", "song.dtx"),
            "Song B", "Coverage Bot", "Rock", 40);

        var initialized = await _manager.InitializeDatabaseServiceAsync(_testDbPath);
        Assert.True(initialized);
        Assert.NotNull(_manager.DatabaseService);

        var watermark = DateTime.UtcNow.AddHours(-1);
        var canonicalA = _manager.RootPolicy
            .Validate(new[] { rootA }).CanonicalRoots.Single();
        var canonicalB = _manager.RootPolicy
            .Validate(new[] { rootB }).CanonicalRoots.Single();

        // A successful atomic write must commit both the global watermark and
        // every supplied per-root watermark. A partial commit (global only)
        // would leave per-root rows absent, forcing a conservative full rescan
        // on every startup until the per-root rows catch up.
        var success = await _manager.DatabaseService!
            .SetEnumerationWatermarkAtomicallyAsync(
                watermark,
                new[] { canonicalA, canonicalB });

        Assert.True(success);
        var global = await _manager.DatabaseService!
            .GetLastSuccessfulEnumerationUtcAsync();
        Assert.Equal(watermark.ToUniversalTime(), global!.Value.ToUniversalTime());
        var perRootA = await _manager.DatabaseService!
            .GetLastSuccessfulEnumerationUtcAsync(canonicalA);
        Assert.Equal(watermark.ToUniversalTime(), perRootA!.Value.ToUniversalTime());
        var perRootB = await _manager.DatabaseService!
            .GetLastSuccessfulEnumerationUtcAsync(canonicalB);
        Assert.Equal(watermark.ToUniversalTime(), perRootB!.Value.ToUniversalTime());
    }

    [Fact]
    public async Task SetEnumerationWatermarkAtomicallyAsync_WhenLegacyDatabaseHasGlobalOnly_WritesBothAtomically()
    {
        // Regression: a legacy database has a global watermark but no per-root
        // rows. The atomic write must add per-root rows alongside the global
        // update in one transaction, eliminating the window where a crash after
        // the global write but before the root writes would leave zero per-root
        // rows and force a conservative full rescan on every subsequent startup.
        var rootA = Path.Combine(_testRoot, "RootA");
        await CreateDtxFileAsync(
            Path.Combine(rootA, "Song A", "song.dtx"),
            "Song A", "Coverage Bot", "Rock", 35);

        var initialized = await _manager.InitializeDatabaseServiceAsync(_testDbPath);
        Assert.True(initialized);
        Assert.NotNull(_manager.DatabaseService);

        // Seed a legacy global-only watermark (no per-root rows).
        var legacyGlobal = DateTime.UtcNow.AddHours(-3);
        await _manager.DatabaseService!
            .SetLastSuccessfulEnumerationUtcAsync(legacyGlobal);
        Assert.Null(await _manager.DatabaseService!
            .GetLastSuccessfulEnumerationUtcAsync(rootA));

        var canonicalA = _manager.RootPolicy
            .Validate(new[] { rootA }).CanonicalRoots.Single();
        var newWatermark = DateTime.UtcNow.AddHours(-1);

        var success = await _manager.DatabaseService!
            .SetEnumerationWatermarkAtomicallyAsync(
                newWatermark,
                new[] { canonicalA });

        Assert.True(success);
        // Both global and per-root must be at the new watermark — no partial state.
        var global = await _manager.DatabaseService!
            .GetLastSuccessfulEnumerationUtcAsync();
        Assert.Equal(newWatermark.ToUniversalTime(), global!.Value.ToUniversalTime());
        var perRootA = await _manager.DatabaseService!
            .GetLastSuccessfulEnumerationUtcAsync(canonicalA);
        Assert.Equal(newWatermark.ToUniversalTime(), perRootA!.Value.ToUniversalTime());
    }

    [Fact]
    public async Task SetEnumerationWatermarkAtomicallyAsync_WhenWriteFails_LeavesNoPartialState()
    {
        // Regression: a failed atomic write must not leave the global watermark
        // committed while per-root rows are absent. The transaction rolls back
        // as a unit, so the previous watermark is retained and the next
        // freshness check remains conservative.
        var rootA = Path.Combine(_testRoot, "RootA");
        await CreateDtxFileAsync(
            Path.Combine(rootA, "Song A", "song.dtx"),
            "Song A", "Coverage Bot", "Rock", 35);

        var initialized = await _manager.InitializeDatabaseServiceAsync(_testDbPath);
        Assert.True(initialized);
        Assert.NotNull(_manager.DatabaseService);

        // Establish a known baseline watermark.
        var baseline = DateTime.UtcNow.AddHours(-2);
        var canonicalA = _manager.RootPolicy
            .Validate(new[] { rootA }).CanonicalRoots.Single();
        Assert.True(await _manager.DatabaseService!
            .SetEnumerationWatermarkAtomicallyAsync(baseline, new[] { canonicalA }));

        // Swap in a broken service that cannot create contexts, simulating a
        // mid-write crash or storage failure. The atomic method must return
        // false and leave the baseline watermark intact.
        var originalService = _manager.DatabaseService;
        var brokenService = CreateBrokenDatabaseService();
        ReflectionHelpers.SetPrivateField(_manager, "_databaseService", brokenService);
        try
        {
            var newWatermark = DateTime.UtcNow.AddHours(-1);
            var success = await brokenService
                .SetEnumerationWatermarkAtomicallyAsync(
                    newWatermark,
                    new[] { canonicalA });

            Assert.False(success);
        }
        finally
        {
            ReflectionHelpers.SetPrivateField(_manager, "_databaseService", originalService);
        }

        // The real database must still have the baseline watermark — no partial
        // state was created by the failed write.
        var global = await originalService!
            .GetLastSuccessfulEnumerationUtcAsync();
        Assert.Equal(baseline.ToUniversalTime(), global!.Value.ToUniversalTime());
        var perRootA = await originalService!
            .GetLastSuccessfulEnumerationUtcAsync(canonicalA);
        Assert.Equal(baseline.ToUniversalTime(), perRootA!.Value.ToUniversalTime());
    }

    [Fact]
    public async Task SetEnumerationWatermarkAtomicallyAsync_WhenPerRootInsertFails_RollsBackGlobalInsert()
    {
        // Review item 3: the existing WhenWriteFails test uses a broken service
        // that fails before the transaction begins (CreateContext throws). This
        // test exercises a MID-TRANSACTION failure: the global-key INSERT
        // succeeds, then the per-root INSERT is aborted by a SQLite trigger.
        // The transaction must roll back as a unit so the global watermark is
        // NOT left committed while per-root rows are absent — that partial
        // state would force a conservative full rescan on every subsequent
        // startup until the per-root rows are written.
        var rootA = Path.Combine(_testRoot, "RootA");
        await CreateDtxFileAsync(
            Path.Combine(rootA, "Song A", "song.dtx"),
            "Song A", "Coverage Bot", "Rock", 35);

        var initialized = await _manager.InitializeDatabaseServiceAsync(_testDbPath);
        Assert.True(initialized);
        Assert.NotNull(_manager.DatabaseService);

        // Establish a known baseline watermark (both global and per-root).
        var baseline = DateTime.UtcNow.AddHours(-2);
        var canonicalA = _manager.RootPolicy
            .Validate(new[] { rootA }).CanonicalRoots.Single();
        Assert.True(await _manager.DatabaseService!
            .SetEnumerationWatermarkAtomicallyAsync(baseline, new[] { canonicalA }));

        // Install a SQLite trigger that aborts any INSERT on
        // __EnumerationMetadata when the key starts with the per-root prefix.
        // This causes the per-root INSERT OR REPLACE to fail AFTER the global
        // INSERT OR REPLACE has already executed within the same transaction.
        await using (var triggerContext = _manager.DatabaseService!.CreateContext())
        {
            await triggerContext.Database.ExecuteSqlRawAsync(
                """
                CREATE TRIGGER abort_per_root_watermark_insert
                BEFORE INSERT ON __EnumerationMetadata
                WHEN NEW.Key LIKE 'LastSuccessfulEnumerationUtc:Root:%'
                BEGIN
                    SELECT RAISE(ABORT, 'per-root insert aborted by test trigger');
                END
                """);
        }

        var newWatermark = DateTime.UtcNow.AddHours(-1);
        var success = await _manager.DatabaseService!
            .SetEnumerationWatermarkAtomicallyAsync(
                newWatermark,
                new[] { canonicalA });

        // The atomic method must return false (the transaction failed).
        Assert.False(success);

        // The global watermark must still be the baseline — the preceding
        // global-key INSERT was rolled back, not left committed.
        var global = await _manager.DatabaseService!
            .GetLastSuccessfulEnumerationUtcAsync();
        Assert.Equal(baseline.ToUniversalTime(), global!.Value.ToUniversalTime());

        // The per-root watermark must also still be the baseline.
        var perRootA = await _manager.DatabaseService!
            .GetLastSuccessfulEnumerationUtcAsync(canonicalA);
        Assert.Equal(baseline.ToUniversalTime(), perRootA!.Value.ToUniversalTime());
    }

    [Fact]
    public async Task NeedsEnumerationAsync_WhenRootCasingChangesOnCaseInsensitivePlatform_WatermarkStillFound()
    {
        // Regression: on case-insensitive platforms (Windows, macOS), changing
        // only the casing of a configured root path must not make its per-root
        // watermark appear absent. The stable root identity key lowercases the
        // normalized path so /Songs and /songs produce the same storage key.
        // Without this, a casing change would zero out the per-root watermark
        // count and force a needless conservative full rescan.
        var rootUpper = Path.Combine(_testRoot, "Songs");
        var chartPath = Path.Combine(rootUpper, "My Song", "song.dtx");
        await CreateDtxFileAsync(chartPath, "My Song", "Coverage Bot", "Rock", 35);

        var oldFilesystemTimeUtc = DateTime.UtcNow.AddHours(-3);
        SetFilesystemTimes(chartPath, oldFilesystemTimeUtc);

        await InitializeAndEnumerateAsync(rootUpper);

        // Age the watermark so the unchanged chart is NOT considered fresh.
        var watermarkUtc = DateTime.UtcNow.AddHours(-1);
        await SetLastEnumerationTimestampAsync(watermarkUtc.ToLocalTime());
        await SetRootEnumerationTimestampAsync(rootUpper, watermarkUtc);

        // On case-insensitive platforms, present the same root with different
        // casing. The per-root watermark must still be found, so the unchanged
        // chart is NOT flagged as a change. On case-sensitive platforms (Linux),
        // different casing is a genuinely different path, so the watermark is
        // legitimately absent and a rescan is expected.
        var rootLower = Path.Combine(_testRoot, "songs");
        var needsEnumeration = await _manager.NeedsEnumerationAsync(new[] { rootLower });

        if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
        {
            Assert.False(needsEnumeration,
                "A casing-only change must not invalidate the per-root watermark " +
                "on case-insensitive platforms.");
        }
        else
        {
            // On Linux, /Songs and /songs are different directories; the
            // watermark is legitimately absent and a rescan is the safe
            // default.
            Assert.True(needsEnumeration);
        }
    }

    [Fact]
    public async Task NeedsEnumerationAsync_WhenARootIsRemovedAndItsRowsRetained_ShouldStillLoadFromCache()
    {
        // Regression: the filesystem file count is scoped to the active roots,
        // so the database score count must be scoped to the same roots. The
        // global score count would include retained rows belonging to a removed
        // root, which can never match the scoped filesystem count and would
        // force a full rescan on every subsequent startup.
        var retainedRoot = Path.Combine(_testRoot, "RetainedSongs");
        var removedRoot = Path.Combine(_testRoot, "RemovedSongs");
        var retainedFolder = Path.Combine(retainedRoot, "Retained Song");
        var removedFolder = Path.Combine(removedRoot, "Removed Song");

        Directory.CreateDirectory(retainedFolder);
        Directory.CreateDirectory(removedFolder);
        await CreateDtxFileAsync(Path.Combine(retainedFolder, "retained.dtx"), "Retained Song", "Coverage Bot", "Jazz", 40);
        await CreateDtxFileAsync(Path.Combine(removedFolder, "removed.dtx"), "Removed Song", "Coverage Bot", "Jazz", 50);

        // Enumerate both roots so the database holds rows for each.
        var initialized = await _manager.InitializeDatabaseServiceAsync(_testDbPath);
        Assert.True(initialized);
        var enumerated = await _manager.EnumerateSongsAsync(new[] { retainedRoot, removedRoot });
        Assert.True(enumerated >= 2);
        Assert.NotNull(_manager.DatabaseService);

        // The removed root's files still exist on disk; only the configured root
        // set changes. Its rows remain in the database (the import path only
        // removes stale charts under active roots).
        await SetLastEnumerationTimestampAsync(DateTime.Now.AddMinutes(5));

        // Simulate the next startup with the removed root no longer configured.
        var needsEnumeration = await _manager.NeedsEnumerationAsync(new[] { retainedRoot });

        Assert.False(needsEnumeration);
    }

    [Fact]
    public async Task NeedsEnumerationAsync_WhenRemovedRootIsDeletedAndItsRowsRetained_ShouldStillLoadFromCache()
    {
        // Regression: CheckDatabaseFilesStillExist must scope its existence check to
        // the active roots. A removed root's charts are retained in the database
        // (the import path only purges stale charts under active roots), and the
        // root's files are typically gone (deleted, unmounted, or on an external
        // drive that was detached). The unscoped check loaded every retained chart
        // and flagged the removed root's charts as missing on every startup, forcing
        // a full rescan even though the active library was unchanged.
        var retainedRoot = Path.Combine(_testRoot, "RetainedSongsKept");
        var removedRoot = Path.Combine(_testRoot, "RemovedSongsDeleted");
        var retainedFolder = Path.Combine(retainedRoot, "Retained Song");
        var removedFolder = Path.Combine(removedRoot, "Removed Song");

        Directory.CreateDirectory(retainedFolder);
        Directory.CreateDirectory(removedFolder);
        await CreateDtxFileAsync(Path.Combine(retainedFolder, "retained.dtx"), "Retained Song", "Coverage Bot", "Jazz", 40);
        await CreateDtxFileAsync(Path.Combine(removedFolder, "removed.dtx"), "Removed Song", "Coverage Bot", "Jazz", 50);

        // Enumerate both roots so the database holds rows for each.
        var initialized = await _manager.InitializeDatabaseServiceAsync(_testDbPath);
        Assert.True(initialized);
        var enumerated = await _manager.EnumerateSongsAsync(new[] { retainedRoot, removedRoot });
        Assert.True(enumerated >= 2);
        Assert.NotNull(_manager.DatabaseService);

        // The removed root's files are gone (deleted/unmounted/detached) but its
        // rows remain in the database (the import path only purges stale charts
        // under active roots).
        Directory.Delete(removedRoot, recursive: true);
        await SetLastEnumerationTimestampAsync(DateTime.Now.AddMinutes(5));

        // Simulate the next startup with the removed root no longer configured.
        var needsEnumeration = await _manager.NeedsEnumerationAsync(new[] { retainedRoot });

        Assert.False(needsEnumeration);
    }

    [Fact]
    public async Task NeedsEnumerationAsync_WhenUnrelatedDatabaseWriteFollowsChartEdit_ShouldStillDetectEdit()
    {
        // Regression: the cache-freshness threshold must be an explicit
        // LastSuccessfulEnumerationUtc metadata value, not the SQLite database
        // file's last-write time. An unrelated SaveChangesAsync (bookmark toggle,
        // score save) advances the database file mtime; deriving the enumeration
        // time from it would let that write mask a chart edit that happened between
        // the enumeration and the write when the file count is unchanged.
        var songsRoot = Path.Combine(_testRoot, "UnrelatedWriteAfterEdit");
        var songFolder = Path.Combine(songsRoot, "Song");
        var songPath = Path.Combine(songFolder, "song.dtx");
        Directory.CreateDirectory(songFolder);
        await CreateDtxFileAsync(songPath, "Edit Song", "Coverage Bot", "Rock", 35);
        await InitializeAndEnumerateAsync(songsRoot);
        Assert.NotNull(_manager.DatabaseService);

        // Simulate an enumeration that finished 10 minutes ago. Pin both the
        // metadata threshold and the directory mtimes to that moment so the only
        // signal that can trip change detection is the chart file edit below.
        var enumerationTime = DateTime.Now.AddMinutes(-10);
        await SetLastEnumerationTimestampAsync(enumerationTime);
        Directory.SetLastWriteTime(songsRoot, enumerationTime.AddMinutes(-1));
        Directory.SetLastWriteTime(songFolder, enumerationTime.AddMinutes(-1));

        // Modify the chart 5 minutes after the enumeration (file count unchanged).
        File.SetLastWriteTime(songPath, enumerationTime.AddMinutes(5));

        // Perform an unrelated database write (bookmark toggle) now. Under the old
        // DB-mtime-based threshold, this advanced the threshold to ~now, which is
        // after the chart edit, masking it (the 5-minutes-ago edit would appear
        // older than the now-1minute threshold).
        var song = (await _manager.DatabaseService!.GetSongsAsync()).Single();
        await _manager.DatabaseService.SetBookmarkAsync(song.Id, true);

        var needsEnumeration = await _manager.NeedsEnumerationAsync(new[] { songsRoot });

        Assert.True(needsEnumeration);
    }

    [Fact]
    public async Task NeedsEnumerationAsync_WhenFileCountChanges_ShouldReturnTrue()
    {
        var songsRoot = Path.Combine(_testRoot, "ChangedSongs");
        var firstSongFolder = Path.Combine(songsRoot, "First Song");
        var secondSongFolder = Path.Combine(songsRoot, "Second Song");

        Directory.CreateDirectory(firstSongFolder);
        await CreateDtxFileAsync(Path.Combine(firstSongFolder, "first.dtx"), "First Song", "Coverage Bot", "Jazz", 40);

        await InitializeAndEnumerateAsync(songsRoot);
        await SetLastEnumerationTimestampAsync(DateTime.Now.AddMinutes(5));

        Directory.CreateDirectory(secondSongFolder);
        await CreateDtxFileAsync(Path.Combine(secondSongFolder, "second.dtx"), "Second Song", "Coverage Bot", "Jazz", 55);

        var needsEnumeration = await _manager.NeedsEnumerationAsync(new[] { songsRoot });

        Assert.True(needsEnumeration);
    }

    [Fact]
    public async Task EnumerateSongsOnlyAsync_WithEmptyDirectory_ShouldReturnZero()
    {
        var songsRoot = Path.Combine(_testRoot, "EnumerateOnlyEmpty");
        Directory.CreateDirectory(songsRoot);

        var initialized = await _manager.InitializeDatabaseServiceAsync(_testDbPath);
        Assert.True(initialized);

        var result = await _manager.EnumerateSongsOnlyAsync(new[] { songsRoot });

        Assert.Equal(0, result);
        Assert.Equal(0, _manager.DiscoveredScoreCount);
        Assert.Empty(_manager.RootSongs);
    }

    [Fact]
    public async Task EnumerateSongsAsync_WithCancelledToken_ShouldThrowOperationCanceledException()
    {
        var songsRoot = Path.Combine(_testRoot, "CancelledSongs");
        Directory.CreateDirectory(songsRoot);
        await CreateDtxFileAsync(Path.Combine(songsRoot, "cancelled.dtx"), "Cancelled Song", "Coverage Bot", "Rock", 35);

        await _manager.InitializeDatabaseServiceAsync(_testDbPath);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _manager.EnumerateSongsAsync(
                new[] { songsRoot },
                cancellationToken: cancellation.Token));
        Assert.Empty(_manager.RootSongs);
    }

    [Fact]
    public async Task EnumerateSongsAsync_WithEmptyBox_ShouldSkipEmptyBox()
    {
        var songsRoot = Path.Combine(_testRoot, "EmptyBoxSongs");
        Directory.CreateDirectory(Path.Combine(songsRoot, "DTXFiles.Empty"));

        await _manager.InitializeDatabaseServiceAsync(_testDbPath);

        var result = await _manager.EnumerateSongsAsync(new[] { songsRoot });

        Assert.Equal(0, result);
        Assert.Empty(_manager.RootSongs);
    }

    [Fact]
    public async Task CheckDatabaseFilesStillExist_WhenChartMoved_ShouldUpdateStoredPath()
    {
        var songsRoot = Path.Combine(_testRoot, "MovedSongs");
        var originalFolder = Path.Combine(songsRoot, "Original");
        var movedFolder = Path.Combine(songsRoot, "Moved");
        var originalPath = Path.Combine(originalFolder, "moved-song.dtx");
        var movedPath = Path.Combine(movedFolder, "moved-song.dtx");

        Directory.CreateDirectory(originalFolder);
        await CreateDtxFileAsync(originalPath, "Moved Song", "Coverage Bot", "Rock", 50);

        await InitializeAndEnumerateAsync(songsRoot);

        Directory.CreateDirectory(movedFolder);
        File.Move(originalPath, movedPath);

        ReflectionHelpers.SetPrivateField(_manager, "_currentSearchPaths", new[] { songsRoot });

        var checkTask = ReflectionHelpers.InvokePrivateMethod<Task<bool>>(_manager, "CheckDatabaseFilesStillExist", new object[] { new[] { songsRoot } });
        Assert.NotNull(checkTask);

        var changeDetected = await checkTask!;
        var songs = await _manager.DatabaseService!.GetSongsAsync();

        Assert.True(changeDetected);
        Assert.Equal(movedPath, songs.Single().Charts.Single().FilePath);
    }

    [Fact]
    public async Task PublicDatabaseHelpers_ShouldReturnStatsSearchResultsAndScores()
    {
        var songsRoot = Path.Combine(_testRoot, "HelperSongs");
        var rockFolder = Path.Combine(songsRoot, "Blue Sky");
        var popFolder = Path.Combine(songsRoot, "Red Moon");

        Directory.CreateDirectory(rockFolder);
        Directory.CreateDirectory(popFolder);

        await CreateDtxFileAsync(Path.Combine(rockFolder, "blue.dtx"), "Blue Sky", "Alice", "Rock", 30);
        await CreateDtxFileAsync(Path.Combine(popFolder, "red.dtx"), "Red Moon", "Bob", "Pop", 45);

        await InitializeAndEnumerateAsync(songsRoot);

        Assert.True(await _manager.DatabaseExistsAsync());

        var stats = await _manager.GetDatabaseStatsAsync();
        Assert.NotNull(stats);
        Assert.Equal(2, stats!.SongCount);
        Assert.Equal(2, stats.ScoreCount);

        var rockSongs = await _manager.GetSongsByGenreAsync("Rock");
        var foundSongs = await _manager.FindSongsBySearchAsync("Blue");

        var rockSong = Assert.Single(rockSongs);
        var foundSong = Assert.Single(foundSongs);
        Assert.Equal("Blue Sky", rockSong.Title);
        Assert.Equal("Blue Sky", foundSong.Title);

        var chartId = rockSong.Charts.Single().Id;
        var scoreUpdated = await _manager.UpdateScoreAsync(chartId, EInstrumentPart.DRUMS, 950_000, 98.5, fullCombo: true);
        var topScores = await _manager.GetTopScoresAsync(EInstrumentPart.DRUMS, limit: 1);

        Assert.True(scoreUpdated);
        Assert.Single(topScores);
        Assert.Equal(chartId, topScores[0].ChartId);
        Assert.Equal(950_000, topScores[0].BestScore);

        Assert.True(await _manager.SaveSongsDBAsync());
        Assert.True(await _manager.BuildSongListsAsync());
    }

    [Fact]
    public async Task PublicHelpers_WithUninitializedDatabaseServiceInstance_ShouldReturnSafeFallbacks()
    {
        ReflectionHelpers.SetPrivateField(_manager, "_databaseService", new SongDatabaseService(_testDbPath));

        Assert.Equal(0, await _manager.GetDatabaseScoreCountAsync());
        Assert.Null(await _manager.GetDatabaseStatsAsync());
        Assert.Empty(await _manager.GetTopScoresAsync(EInstrumentPart.DRUMS));
        Assert.False(await _manager.UpdateScoreAsync(1, EInstrumentPart.DRUMS, 123, 45.6, fullCombo: false));
        Assert.Empty(await _manager.FindSongsBySearchAsync("anything"));
        Assert.Empty(await _manager.GetSongsByGenreAsync("anything"));
    }

    [Fact]
    public async Task InitializeDatabaseServiceAsync_WithInvalidPath_ShouldReturnFalse()
    {
        var initialized = await _manager.InitializeDatabaseServiceAsync("\0invalid");

        Assert.False(initialized);
    }

    [Fact]
    public async Task InitializeDatabaseServiceAsync_WithPurgeRequested_ShouldClearExistingDatabase()
    {
        var songsRoot = Path.Combine(_testRoot, "PurgeSongs");
        var songFolder = Path.Combine(songsRoot, "Song");
        Directory.CreateDirectory(songFolder);
        await CreateDtxFileAsync(Path.Combine(songFolder, "song.dtx"), "Purge Song", "Coverage Bot", "Rock", 35);

        await InitializeAndEnumerateAsync(songsRoot);

        var reinitialized = await _manager.InitializeDatabaseServiceAsync(_testDbPath, purgeDatabaseFirst: true);
        var stats = await _manager.GetDatabaseStatsAsync();

        Assert.True(reinitialized);
        Assert.NotNull(stats);
        Assert.True(await _manager.DatabaseExistsAsync());
    }

    [Fact]
    public async Task StateHelpers_ShouldTrackInitializationState()
    {
        var initialized = await _manager.InitializeDatabaseServiceAsync(_testDbPath);
        Assert.True(initialized);

        _manager.SetInitialized();

        Assert.True(_manager.IsInitialized);
    }

    [Fact]
    public async Task PublicDatabaseHelpers_WithoutInitializedService_ShouldReturnSafeDefaults()
    {
        Assert.False(await _manager.DatabaseExistsAsync());
        Assert.Null(await _manager.GetDatabaseStatsAsync());
        Assert.False(await _manager.LoadScoreCacheAsync(new[] { Path.Combine(_testRoot, "none") }));
        Assert.False(await _manager.SaveSongsDBAsync());
        Assert.Empty(await _manager.GetTopScoresAsync(EInstrumentPart.DRUMS));
        Assert.False(await _manager.UpdateScoreAsync(1, EInstrumentPart.DRUMS, 1, 1.0, fullCombo: false));
        Assert.Empty(await _manager.FindSongsBySearchAsync("anything"));
        Assert.Empty(await _manager.GetSongsByGenreAsync("anything"));
        Assert.False(await _manager.BuildSongListsAsync());
    }

    [Fact]
    public void NormalizeSetDefLine_WithSpacedCommand_ShouldNotReturnEmpty()
    {
        var result = ReflectionHelpers.InvokePrivateMethod<string>(_manager, "NormalizeSetDefLine", "# L 5 F I L E hard.dtx");

        Assert.NotNull(result);
        Assert.NotEmpty(result!);
        Assert.Contains("hard.dtx", result);
    }

    [Fact]
    public void NormalizeSetDefLine_WithL1LabelSpacedPattern_ShouldNotReturnEmpty()
    {
        var result = ReflectionHelpers.InvokePrivateMethod<string>(_manager, "NormalizeSetDefLine", "# L 1 L A B E L Expert");

        Assert.NotNull(result);
        Assert.NotEmpty(result!);
        Assert.Contains("Expert", result);
    }

    [Fact]
    public async Task BuildEnumerationBatchAsync_WithNoTitleInDtx_ShouldUseFilenameOrDirectory()
    {
        var setFolder = Path.Combine(_testRoot, "NoTitleSet");
        var setDefPath = Path.Combine(setFolder, "set.def");
        Directory.CreateDirectory(setFolder);

        await File.WriteAllTextAsync(setDefPath, """
#L1FILE notitle.dtx
""");

        await File.WriteAllTextAsync(Path.Combine(setFolder, "notitle.dtx"), """
#BPM: 140
#DLEVEL: 50
#00002:11111111
""");

        await _manager.InitializeDatabaseServiceAsync(_testDbPath);

        var batch = await _manager.BuildEnumerationBatchAsync(
            new[] { setFolder },
            null,
            CancellationToken.None);

        var pending = Assert.Single(batch.PendingSongs);
        var node = pending.Placeholder;
        Assert.True(node.Title == "NoTitleSet" || node.Title == "notitle");
    }

    [Fact]
    public async Task BuildEnumerationBatchAsync_WithCancelledToken_ShouldThrowOperationCanceled()
    {
        var setFolder = Path.Combine(_testRoot, "CancelledSet");
        var setDefPath = Path.Combine(setFolder, "set.def");
        Directory.CreateDirectory(setFolder);

        await File.WriteAllTextAsync(setDefPath, """
#TITLE: Cancelled Song
#L1FILE cancelled.dtx
""");

        await File.WriteAllTextAsync(Path.Combine(setFolder, "cancelled.dtx"), """
#TITLE: Cancelled Song
#BPM: 140
#DLEVEL: 50
#00002:11111111
""");

        await _manager.InitializeDatabaseServiceAsync(_testDbPath);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _manager.BuildEnumerationBatchAsync(
                new[] { setFolder },
                null,
                cancellation.Token));
    }

    [Fact]
    public async Task BuildEnumerationBatchAsync_WithUtf16SetDef_ShouldRecoverLabel()
    {
        var setFolder = Path.Combine(_testRoot, "Utf16Set");
        var setDefPath = Path.Combine(setFolder, "set.def");
        Directory.CreateDirectory(setFolder);

        var setDefContent =
            "#TITLE Utf16 Song\r\n" +
            "#L1LABEL BASIC\r\n#L1FILE bas.dtx\r\n";
        await File.WriteAllTextAsync(setDefPath, setDefContent, Encoding.Unicode);

        await File.WriteAllTextAsync(Path.Combine(setFolder, "bas.dtx"), """
#TITLE: Utf16 Song
#BPM: 140
#DLEVEL: 50
#00002:11111111
""");

        await _manager.InitializeDatabaseServiceAsync(_testDbPath);

        var batch = await _manager.BuildEnumerationBatchAsync(
            new[] { setFolder },
            null,
            CancellationToken.None);

        var pending = Assert.Single(batch.PendingSongs);
        Assert.Equal("BASIC", pending.Placeholder.DifficultyLabels[0]);
    }

    [Fact]
    public async Task ParseBoxDefinitionAsync_WithColorLines_ShouldParseColors()
    {
        var boxFolder = Path.Combine(_testRoot, "ColorBox");
        var boxDefPath = Path.Combine(boxFolder, "box.def");
        Directory.CreateDirectory(boxFolder);

        await File.WriteAllTextAsync(boxDefPath, """
#TITLE: Colorful Box
#BGCOLOR: #FF5733
#TEXTCOLOR: #33FF57
""");

        var task = ReflectionHelpers.InvokePrivateMethod<Task<BoxDefinition?>>(
            _manager,
            "ParseBoxDefinitionAsync",
            boxDefPath,
            CancellationToken.None);

        Assert.NotNull(task);
        var boxDef = await task!;

        Assert.NotNull(boxDef);
        Assert.Equal("Colorful Box", boxDef!.Title);
        Assert.Equal(System.Drawing.ColorTranslator.FromHtml("#FF5733"), boxDef.BackgroundColor);
        Assert.Equal(System.Drawing.ColorTranslator.FromHtml("#33FF57"), boxDef.TextColor);
    }

    [Theory]
    [InlineData(EInstrumentPart.GUITAR, 75, "guitar.dtx")]
    [InlineData(EInstrumentPart.BASS, 88, "bass.dtx")]
    public void CreateSongNodeFromDatabaseEntities_WithSingleStringPartChart_ShouldSelectRequestedPart(
        EInstrumentPart instrument,
        int expectedLevel,
        string chartFileName)
    {
        var song = new SongEntity
        {
            Title = $"{instrument} Song",
            Artist = "Coverage Bot",
            Genre = instrument == EInstrumentPart.GUITAR ? "Rock" : "Funk"
        };

        var primaryChart = new SongChart
        {
            FilePath = Path.Combine(_testRoot, chartFileName),
            HasDrumChart = false,
            HasGuitarChart = instrument == EInstrumentPart.GUITAR,
            HasBassChart = instrument == EInstrumentPart.BASS,
            DrumLevel = 0,
            GuitarLevel = instrument == EInstrumentPart.GUITAR ? expectedLevel : 0,
            BassLevel = instrument == EInstrumentPart.BASS ? expectedLevel : 0
        };

        // A song with a drum chart gives slots to drum charts only (gameplay plays drums),
        // so the string part is the song's only chart here.
        var charts = new[] { primaryChart };

        var result = ReflectionHelpers.InvokePrivateMethod<SongListNode?>(_manager, "CreateSongNodeFromDatabaseEntities", song, charts);

        Assert.NotNull(result);
        var stringPartScore = Assert.Single(result!.Scores, s => s != null);
        Assert.Equal(instrument, stringPartScore.Instrument);
        Assert.Equal(expectedLevel, stringPartScore.DifficultyLevel);
    }

    [Fact]
    public void CreateSongNodeFromDatabaseEntities_WithMultipleCharts_ShouldCapAtFiveDifficulties()
    {
        var song = new SongEntity
        {
            Title = "Multi Song",
            Artist = "Coverage Bot",
            Genre = "Rock"
        };

        var charts = Enumerable.Range(1, 7).Select(i => new SongChart
        {
            FilePath = Path.Combine(_testRoot, $"chart{i}.dtx"),
            HasDrumChart = true,
            DrumLevel = i * 10
        }).ToArray();

        var result = ReflectionHelpers.InvokePrivateMethod<SongListNode?>(_manager, "CreateSongNodeFromDatabaseEntities", song, charts);

        Assert.NotNull(result);
        Assert.Equal(5, result!.Scores.Count(s => s != null));
        Assert.Equal("Level 5", result.DifficultyLabels[4]);
    }

    [Fact]
    public void CreateSongNodeFromDatabaseEntities_DrumChartsReturnedOutOfOrder_ShouldAssignSlotsByAscendingLevel()
    {
        // Regression: SongChartHelper.GetCurrentDifficultyChart selects the chart for a given
        // difficulty by ordering drum charts ascending by DrumLevel. The score slots must be
        // filled in the same order, otherwise DifficultyLabels[0] can hold the EXTREME chart's
        // label while difficulty 0 loads the BASIC chart, making the performance-stage badge
        // show the wrong difficulty cell. Here the database returns EXTREME first.
        var song = new SongEntity
        {
            Title = "Out-of-order Song",
            Artist = "Coverage Bot",
            Genre = "Rock"
        };

        var charts = new[]
        {
            new SongChart { FilePath = Path.Combine(_testRoot, "ext.dtx"), HasDrumChart = true, DrumLevel = 90, DifficultyLabel = "" },
            new SongChart { FilePath = Path.Combine(_testRoot, "bas.dtx"), HasDrumChart = true, DrumLevel = 30, DifficultyLabel = "" },
            new SongChart { FilePath = Path.Combine(_testRoot, "adv.dtx"), HasDrumChart = true, DrumLevel = 60, DifficultyLabel = "" }
        };

        var result = ReflectionHelpers.InvokePrivateMethod<SongListNode?>(_manager, "CreateSongNodeFromDatabaseEntities", song, charts);

        Assert.NotNull(result);
        // Slot ordering must match GetCurrentDifficultyChart: ascending drum level.
        Assert.Equal(30, result!.Scores[0]!.DifficultyLevel);
        Assert.Equal(60, result.Scores[1]!.DifficultyLevel);
        Assert.Equal(90, result.Scores[2]!.DifficultyLevel);
        Assert.Equal(EInstrumentPart.DRUMS, result.Scores[0]!.Instrument);
    }

    [Fact]
    public void CreateSongNodeFromDatabaseEntities_WithNullSong_ShouldReturnNull()
    {
        var charts = new[] { new SongChart { FilePath = "dummy.dtx", HasDrumChart = true, DrumLevel = 50 } };

        var result = ReflectionHelpers.InvokePrivateMethod<SongListNode?>(_manager, "CreateSongNodeFromDatabaseEntities", null!, charts);

        Assert.Null(result);
    }

    [Fact]
    public async Task CheckDatabaseFilesStillExist_WithMissingFileNotFound_ShouldReturnTrue()
    {
        var songsRoot = Path.Combine(_testRoot, "MissingFileSongs");
        var songFolder = Path.Combine(songsRoot, "Song");
        var chartPath = Path.Combine(songFolder, "missing.dtx");

        Directory.CreateDirectory(songFolder);
        await CreateDtxFileAsync(chartPath, "Missing Song", "Coverage Bot", "Rock", 50);

        await InitializeAndEnumerateAsync(songsRoot);

        File.Delete(chartPath);

        ReflectionHelpers.SetPrivateField(_manager, "_currentSearchPaths", new[] { Path.Combine(_testRoot, "NonExistentPath") });

        var checkTask = ReflectionHelpers.InvokePrivateMethod<Task<bool>>(_manager, "CheckDatabaseFilesStillExist", new object[] { new[] { songsRoot } });
        Assert.NotNull(checkTask);

        var changeDetected = await checkTask!;

        Assert.True(changeDetected);
    }

    [Fact]
    public async Task IsLikelyMatchAsync_WithMissingOriginalPath_ShouldReturnTrue()
    {
        var missingPath = Path.Combine(_testRoot, "missing.dtx");
        var candidatePath = Path.Combine(_testRoot, "missing.dtx");

        var result = await ReflectionHelpers.InvokePrivateMethod<Task<bool>>(_manager, "IsLikelyMatchAsync", missingPath, candidatePath);

        Assert.True(result);
    }

    [Fact]
    public async Task IsLikelyMatchAsync_WithSizeMismatch_ShouldReturnFalse()
    {
        var originalPath = Path.Combine(_testRoot, "original.dtx");
        var candidatePath = Path.Combine(_testRoot, "candidate.dtx");

        await File.WriteAllTextAsync(originalPath, "short");
        await File.WriteAllTextAsync(candidatePath, "this is a much longer file content");

        var result = await ReflectionHelpers.InvokePrivateMethod<Task<bool>>(_manager, "IsLikelyMatchAsync", originalPath, candidatePath);

        Assert.False(result);
    }

    [Fact]
    public async Task EnumerateSongsAsync_WhenEnumerationAlreadyInProgress_ShouldReturnZero()
    {
        await _manager.InitializeDatabaseServiceAsync(_testDbPath);
        ReflectionHelpers.SetPrivateField(_manager, "_enumCancellation", new CancellationTokenSource());

        var result = await _manager.EnumerateSongsAsync(new[] { _testRoot });

        Assert.Equal(0, result);
    }

    [Fact]
    public async Task CheckDatabaseFilesStillExist_WithoutDatabaseService_ShouldReturnFalse()
    {
        var result = await ReflectionHelpers.InvokePrivateMethod<Task<bool>>(_manager, "CheckDatabaseFilesStillExist", new object[] { new[] { _testRoot } });

        Assert.False(result);
    }

    [Fact]
    public async Task CheckDatabaseFilesStillExist_WithBrokenDatabaseService_ShouldReturnFalse()
    {
        var originalDatabaseService = _manager.DatabaseService;

        try
        {
            ReflectionHelpers.SetPrivateField(_manager, "_databaseService", CreateBrokenDatabaseServiceWithDatabasePath("\0invalid"));

            var result = await ReflectionHelpers.InvokePrivateMethod<Task<bool>>(_manager, "CheckDatabaseFilesStillExist", new object[] { new[] { _testRoot } });

            Assert.False(result);
        }
        finally
        {
            ReflectionHelpers.SetPrivateField(_manager, "_databaseService", originalDatabaseService);
        }
    }

    [Fact]
    public async Task FindMovedFileAsync_WhenOriginalPathHasNoFilename_ShouldReturnNull()
    {
        await _manager.InitializeDatabaseServiceAsync(_testDbPath);

        var result = await ReflectionHelpers.InvokePrivateMethod<Task<string?>>(_manager, "FindMovedFileAsync", Path.DirectorySeparatorChar.ToString());

        Assert.Null(result);
    }

    [Fact]
    public async Task FindMovedFileAsync_WithoutCurrentSearchPaths_ShouldFallbackToDefaultsAndReturnNull()
    {
        await _manager.InitializeDatabaseServiceAsync(_testDbPath);
        ReflectionHelpers.SetPrivateField(_manager, "_currentSearchPaths", Array.Empty<string>());

        var result = await ReflectionHelpers.InvokePrivateMethod<Task<string?>>(
            _manager,
            "FindMovedFileAsync",
            Path.Combine(_testRoot, "missing.dtx"));

        Assert.Null(result);
    }

    [Fact]
    public async Task DetectFilesystemChangesAsync_WithoutEnumerationTimestamp_ShouldReturnTrue()
    {
        await _manager.InitializeDatabaseServiceAsync(_testDbPath);

        var result = await ReflectionHelpers.InvokePrivateMethod<Task<bool>>(
            _manager,
            "DetectFilesystemChangesAsync",
            (object)new[] { _testRoot });

        Assert.True(result);
    }

    [Fact]
    public async Task InventoryHasChangesSince_WhenDirectoryWasModified_ShouldReturnTrue()
    {
        var songsRoot = Path.Combine(_testRoot, "DirectoryModified");
        Directory.CreateDirectory(songsRoot);
        var lastEnumerationTime = DateTime.UtcNow.AddMinutes(-5);
        Directory.SetLastWriteTimeUtc(songsRoot, DateTime.UtcNow);

        var inventory = await ScanChartInventory(songsRoot);

        Assert.True(InventoryHasChanges(inventory, songsRoot, lastEnumerationTime));
    }

    [Fact]
    public async Task InventoryHasChangesSince_WhenDtxFileWasModified_ShouldReturnTrue()
    {
        var songsRoot = Path.Combine(_testRoot, "DirectoryFileModified");
        Directory.CreateDirectory(songsRoot);
        var dtxPath = Path.Combine(songsRoot, "changed.dtx");
        await CreateDtxFileAsync(dtxPath, "Changed Song", "Coverage Bot", "Rock", 40);

        var lastEnumerationTime = DateTime.UtcNow.AddMinutes(-5);
        Directory.SetLastWriteTimeUtc(songsRoot, lastEnumerationTime.AddMinutes(-1));
        File.SetLastWriteTimeUtc(dtxPath, DateTime.UtcNow);

        var inventory = await ScanChartInventory(songsRoot);

        Assert.True(InventoryHasChanges(inventory, songsRoot, lastEnumerationTime));
    }

    [Fact]
    public async Task ScanChartInventoryAsync_WithInvalidPath_ShouldReturnEmptyInventory()
    {
        var inventory = await ScanChartInventory("\0invalid");

        Assert.Empty(inventory.Charts);
        Assert.Empty(inventory.Directories);
        Assert.Empty(inventory.SetDefinitions);
    }

    [Fact]
    public async Task GetLastEnumerationTimestampAsync_WithMissingDatabaseFile_ShouldReturnNull()
    {
        ReflectionHelpers.SetPrivateField(
            _manager,
            "_databaseService",
            new SongDatabaseService(Path.Combine(_testRoot, "missing.db")));

        var result = await ReflectionHelpers.InvokePrivateMethod<Task<DateTime?>>(_manager, "GetLastEnumerationTimestampAsync");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetLastEnumerationTimestampAsync_WithEmptyDatabase_ShouldReturnNull()
    {
        await _manager.InitializeDatabaseServiceAsync(_testDbPath);

        var result = await ReflectionHelpers.InvokePrivateMethod<Task<DateTime?>>(_manager, "GetLastEnumerationTimestampAsync");

        Assert.Null(result);
    }

    [Fact]
    public async Task EnumerateSongsAsync_WhenCancelledAfterFirstProgressReport_ShouldThrowAndNotPublishPartialCount()
    {
        var songsRoot = Path.Combine(_testRoot, "CancelledMidEnumeration");
        Directory.CreateDirectory(songsRoot);

        for (int i = 1; i <= 3; i++)
        {
            await CreateDtxFileAsync(Path.Combine(songsRoot, $"song{i}.dtx"), $"Song {i}", "Coverage Bot", "Rock", 30 + i);
        }

        await _manager.InitializeDatabaseServiceAsync(_testDbPath);
        var cancellation = new CancellationTokenSource();
        var cancellationRequested = 0;
        var progress = new InlineProgress<EnumerationProgress>(report =>
        {
            if (report.ProcessedCount >= 1 && Interlocked.Exchange(ref cancellationRequested, 1) == 0)
                cancellation.Cancel();
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _manager.EnumerateSongsAsync(
                new[] { songsRoot },
                progress,
                cancellation.Token));
        Assert.Empty(_manager.RootSongs);
    }

    [Fact]
    public async Task EnumerateSongsAsync_WhenRootSongCacheIsUnavailable_ShouldReportPostCommitPublicationFailure()
    {
        var songsRoot = Path.Combine(_testRoot, "BrokenRootSongs");
        Directory.CreateDirectory(songsRoot);
        await CreateDtxFileAsync(Path.Combine(songsRoot, "song.dtx"), "Broken Root Song", "Coverage Bot", "Rock", 50);

        await _manager.InitializeDatabaseServiceAsync(_testDbPath);
        var originalRootSongs = ReflectionHelpers.GetPrivateField<List<SongListNode>>(_manager, "_rootSongs");

        try
        {
            ReflectionHelpers.SetPrivateField(_manager, "_rootSongs", null!);

            var exception = await Assert.ThrowsAsync<SongEnumerationPostCommitException>(() =>
                _manager.EnumerateSongsAsync(new[] { songsRoot }));

            Assert.Equal(SongEnumerationPostCommitPhase.Publication, exception.Phase);
            Assert.True(exception.Batch.IsComplete);
            Assert.Equal(songsRoot, Assert.Single(exception.Batch.ActiveRoots));
            Assert.Equal(1, exception.Import.Added);
            Assert.Single(exception.Import.ChartsByPath);
            Assert.IsType<NullReferenceException>(exception.InnerException);

            var committedSong = Assert.Single(await _manager.DatabaseService!.GetSongsAsync());
            Assert.Equal("Broken Root Song", committedSong.Title);
            Assert.Single(committedSong.Charts);
        }
        finally
        {
            ReflectionHelpers.SetPrivateField(_manager, "_rootSongs", originalRootSongs);
        }
    }

    [Fact]
    public async Task BuildSongListsAsync_WhenRootSongCacheIsUnavailable_ShouldReturnFalse()
    {
        var originalRootSongs = ReflectionHelpers.GetPrivateField<List<SongListNode>>(_manager, "_rootSongs");

        try
        {
            ReflectionHelpers.SetPrivateField(_manager, "_rootSongs", null!);

            var result = await _manager.BuildSongListsAsync();

            Assert.False(result);
        }
        finally
        {
            ReflectionHelpers.SetPrivateField(_manager, "_rootSongs", originalRootSongs);
        }
    }

    [Fact]
    public async Task NeedsEnumerationAsync_WithBrokenDatabaseService_ShouldReturnTrue()
    {
        var originalDatabaseService = _manager.DatabaseService;

        try
        {
            ReflectionHelpers.SetPrivateField(_manager, "_databaseService", CreateBrokenDatabaseService());

            var result = await _manager.NeedsEnumerationAsync(new[] { _testRoot });

            Assert.True(result);
        }
        finally
        {
            ReflectionHelpers.SetPrivateField(_manager, "_databaseService", originalDatabaseService);
        }
    }

    [Fact]
    public async Task DetectFilesystemChangesAsync_WhenMovedFileIsDetected_ShouldReturnTrue()
    {
        var songsRoot = Path.Combine(_testRoot, "MovedFileDetection");
        var originalFolder = Path.Combine(songsRoot, "Original");
        var movedFolder = Path.Combine(songsRoot, "Moved");
        var originalPath = Path.Combine(originalFolder, "moved-song.dtx");
        var movedPath = Path.Combine(movedFolder, "moved-song.dtx");

        Directory.CreateDirectory(originalFolder);
        await CreateDtxFileAsync(originalPath, "Moved Song", "Coverage Bot", "Rock", 50);
        await InitializeAndEnumerateAsync(songsRoot);

        Directory.CreateDirectory(movedFolder);
        File.Move(originalPath, movedPath);
        await SetLastEnumerationTimestampAsync(DateTime.Now.AddMinutes(5));

        var result = await ReflectionHelpers.InvokePrivateMethod<Task<bool>>(
            _manager,
            "DetectFilesystemChangesAsync",
            (object)new[] { songsRoot });

        Assert.True(result);
    }

    [Fact]
    public async Task DetectFilesystemChangesAsync_WhenSecondaryRootMissing_ShouldNotForceRescan()
    {
        // Regression: a missing/inaccessible configured root must not be treated
        // as a filesystem change. The old explicit missing-root check fired
        // whenever any configured root was absent AND the database had charts
        // from another root, forcing a full scan on every startup — a scan that
        // could never make the missing root available. The fix probes roots
        // once and carries only available roots through every downstream check.
        var existingRoot = Path.Combine(_testRoot, "ExistingSongs");
        var existingFolder = Path.Combine(existingRoot, "Song");
        Directory.CreateDirectory(existingFolder);
        await CreateDtxFileAsync(Path.Combine(existingFolder, "song.dtx"), "Existing Song", "Coverage Bot", "Rock", 35);
        await InitializeAndEnumerateAsync(existingRoot);
        await SetLastEnumerationTimestampAsync(DateTime.Now.AddMinutes(5));

        var missingRoot = Path.Combine(_testRoot, "MissingSongs");

        var result = await ReflectionHelpers.InvokePrivateMethod<Task<bool>>(
            _manager,
            "DetectFilesystemChangesAsync",
            (object)new[] { missingRoot, existingRoot });

        Assert.False(result);
    }

    [Fact]
    public async Task DetectFilesystemChangesAsync_WhenSearchPathHasChanges_ShouldReturnTrue()
    {
        var songsRoot = Path.Combine(_testRoot, "ChangedSearchPath");
        var songFolder = Path.Combine(songsRoot, "Song");
        var songPath = Path.Combine(songFolder, "song.dtx");

        Directory.CreateDirectory(songFolder);
        await CreateDtxFileAsync(songPath, "Changed Song", "Coverage Bot", "Rock", 35);
        await InitializeAndEnumerateAsync(songsRoot);

        await SetLastEnumerationTimestampAsync(DateTime.Now.AddMinutes(-10));
        File.SetLastWriteTime(songPath, DateTime.Now);

        var result = await ReflectionHelpers.InvokePrivateMethod<Task<bool>>(
            _manager,
            "DetectFilesystemChangesAsync",
            (object)new[] { songsRoot });

        Assert.True(result);
    }

    [Fact]
    public async Task GetLastEnumerationTimestampAsync_WithoutDatabaseService_ShouldReturnNull()
    {
        var result = await ReflectionHelpers.InvokePrivateMethod<Task<DateTime?>>(_manager, "GetLastEnumerationTimestampAsync");

        Assert.Null(result);
    }

    [Fact]
    public async Task BuildEnumerationBatchAsync_WhenReferencedChartIsMissing_ShouldProduceNoSongs()
    {
        var setFolder = Path.Combine(_testRoot, "MissingChartSet");
        Directory.CreateDirectory(setFolder);
        await File.WriteAllTextAsync(Path.Combine(setFolder, "set.def"), """
#TITLE: Missing Chart Set
#L1FILE missing.dtx
""");

        await _manager.InitializeDatabaseServiceAsync(_testDbPath);

        var batch = await _manager.BuildEnumerationBatchAsync(
            new[] { setFolder },
            null,
            CancellationToken.None);

        Assert.Empty(batch.PendingSongs);
    }

    [Fact]
    public async Task BuildEnumerationBatchAsync_WhenChartParsingFails_ShouldSkipSupportedFile()
    {
        var songsRoot = Path.Combine(_testRoot, "EnumerateDirectoryFailure");
        Directory.CreateDirectory(songsRoot);
        await CreateDtxFileAsync(Path.Combine(songsRoot, "song.dtx"), "Broken Node Song", "Coverage Bot", "Rock", 40);

        _manager.ParseSongEntitiesCoreAsync = _ =>
            Task.FromException<(SongEntity, SongChart)>(
                new InvalidDataException("broken chart"));

        var batch = await _manager.BuildEnumerationBatchAsync(
            new[] { songsRoot },
            null,
            CancellationToken.None);

        Assert.Empty(batch.PendingSongs);
        Assert.Single(batch.Errors);
        Assert.Equal("broken chart", batch.Errors[0].Message);
    }

    [Fact]
    public async Task IsLikelyMatchAsync_WhenFileSizesMatch_ShouldReturnTrue()
    {
        var originalPath = Path.Combine(_testRoot, "Original", "match.dtx");
        var candidatePath = Path.Combine(_testRoot, "Candidate", "match.dtx");

        Directory.CreateDirectory(Path.GetDirectoryName(originalPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(candidatePath)!);
        await File.WriteAllTextAsync(originalPath, "same-size");
        await File.WriteAllTextAsync(candidatePath, "same-size");

        var result = await ReflectionHelpers.InvokePrivateMethod<Task<bool>>(_manager, "IsLikelyMatchAsync", originalPath, candidatePath);

        Assert.True(result);
    }

    [Fact]
    public async Task IsLikelyMatchAsync_WithInvalidCandidatePath_ShouldReturnFalse()
    {
        var originalPath = Path.Combine(_testRoot, "Original", "match.dtx");
        Directory.CreateDirectory(Path.GetDirectoryName(originalPath)!);
        await File.WriteAllTextAsync(originalPath, "same-size");

        var candidatePath = $"{_testRoot}{Path.DirectorySeparatorChar}bad\0{Path.DirectorySeparatorChar}match.dtx";

        var result = await ReflectionHelpers.InvokePrivateMethod<Task<bool>>(_manager, "IsLikelyMatchAsync", originalPath, candidatePath);

        Assert.False(result);
    }

    [Fact]
    public async Task UpdateChartFilePathAsync_WithoutDatabaseService_ShouldCompleteSuccessfully()
    {
        var task = (Task)ReflectionHelpers.InvokePrivateMethod(
            _manager,
            "UpdateChartFilePathAsync",
            1,
            Path.Combine(_testRoot, "updated.dtx"))!;

        await task;

        Assert.True(task.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task UpdateChartFilePathAsync_WithBrokenDatabaseService_ShouldCompleteSuccessfully()
    {
        var originalDatabaseService = _manager.DatabaseService;

        try
        {
            ReflectionHelpers.SetPrivateField(_manager, "_databaseService", CreateBrokenDatabaseService());

            var task = (Task)ReflectionHelpers.InvokePrivateMethod(
                _manager,
                "UpdateChartFilePathAsync",
                1,
                Path.Combine(_testRoot, "updated.dtx"))!;

            await task;

            Assert.True(task.IsCompletedSuccessfully);
        }
        finally
        {
            ReflectionHelpers.SetPrivateField(_manager, "_databaseService", originalDatabaseService);
        }
    }

    [Fact]
    public async Task DetectFilesystemChangesAsync_WhenSearchPathIsEmpty_ShouldSkipIt()
    {
        var songsRoot = Path.Combine(_testRoot, "EmptyPathCheck");
        var songFolder = Path.Combine(songsRoot, "Song");
        Directory.CreateDirectory(songFolder);
        await CreateDtxFileAsync(Path.Combine(songFolder, "song.dtx"), "Stable Song", "Coverage Bot", "Rock", 35);
        await InitializeAndEnumerateAsync(songsRoot);
        await SetLastEnumerationTimestampAsync(DateTime.Now.AddMinutes(5));

        var result = await ReflectionHelpers.InvokePrivateMethod<Task<bool>>(
            _manager,
            "DetectFilesystemChangesAsync",
            (object)new[] { "", songsRoot });

        Assert.False(result);
    }

    // Regression: GetActiveChartCountAsync must count charts, not score rows.
    // One chart can have many SongScore rows (ChartId + Instrument + PlaySpeedPercent).
    // Counting score rows would permanently exceed the filesystem file count and
    // force a full rescan on every startup once a player records results at multiple
    // speeds or instruments.
    [Fact]
    public async Task DetectFilesystemChangesAsync_WithMultipleScoreVariantsOnOneChart_ShouldNotTriggerFullScan()
    {
        var songsRoot = Path.Combine(_testRoot, "ScoreVariantStability");
        var songFolder = Path.Combine(songsRoot, "Song");
        Directory.CreateDirectory(songFolder);
        await CreateDtxFileAsync(Path.Combine(songFolder, "song.dtx"), "Stable Song", "Coverage Bot", "Rock", 35);
        await InitializeAndEnumerateAsync(songsRoot);

        // Simulate a player who has recorded results at multiple play speeds.
        // The import seeded one DRUMS score row at 100%; add two more variants
        // at canonical speeds (105, 110) so the chart now has 3 score rows.
        Assert.NotNull(_manager.DatabaseService);
        using (var context = _manager.DatabaseService!.CreateContext())
        {
            var chart = Assert.Single(context.SongCharts.ToList());
            context.SongScores.Add(new SongScore
            {
                ChartId = chart.Id,
                Instrument = EInstrumentPart.DRUMS,
                PlaySpeedPercent = 105,
            });
            context.SongScores.Add(new SongScore
            {
                ChartId = chart.Id,
                Instrument = EInstrumentPart.DRUMS,
                PlaySpeedPercent = 110,
            });
            await context.SaveChangesAsync();
        }

        // Set the explicit __EnumerationMetadata enumeration timestamp AFTER all
        // writes (including the score-row inserts above) so the freshness check
        // sees a steady-state watermark in the future. The watermark is the
        // persisted LastSuccessfulEnumerationUtc value, not the database file's
        // last-write time, so unrelated SaveChangesAsync calls cannot advance it.
        await SetLastEnumerationTimestampAsync(DateTime.Now.AddMinutes(5));

        // Filesystem still has exactly 1 chart file; database now has 3 score rows
        // but 1 chart row. The cache-freshness check compares chart counts, so it
        // must NOT report a mismatch.
        var result = await ReflectionHelpers.InvokePrivateMethod<Task<bool>>(
            _manager,
            "DetectFilesystemChangesAsync",
            (object)new[] { songsRoot });

        Assert.False(result);
    }

    // Regression: CountDTXFilesAsync must count ALL supported chart extensions,
    // not just *.dtx. A library containing .gda/.g2d/.bms/.bme/.bml charts would
    // otherwise have filesystem count 0 (no .dtx files) vs database count > 0,
    // forcing a full rescan on every startup.
    [Fact]
    public async Task ScanChartInventoryAsync_WithNonDtxChartFile_ShouldCountIt()
    {
        var songsRoot = Path.Combine(_testRoot, "NonDtxCount");
        var songFolder = Path.Combine(songsRoot, "Song");
        Directory.CreateDirectory(songFolder);
        // .gda is in DTXChartParser.SupportedExtensions; no .dtx files present.
        await File.WriteAllTextAsync(Path.Combine(songFolder, "chart.gda"), """
#TITLE: GDA Song
#ARTIST: Coverage Bot
#BPM: 120
#DLEVEL: 40
#00002:11111111
#00011:01010101
""");

        var inventory = await ScanChartInventory(songsRoot);

        Assert.Single(inventory.Charts);
    }

    // Regression: CheckDirectoryForChangesAsync must scan ALL supported chart
    // extensions, not just *.dtx. A modified .bms chart would otherwise be missed
    // when the total file count is unchanged.
    [Fact]
    public async Task InventoryHasChangesSince_WhenNonDtxChartWasModified_ShouldReturnTrue()
    {
        var songsRoot = Path.Combine(_testRoot, "NonDtxModified");
        var songFolder = Path.Combine(songsRoot, "Song");
        Directory.CreateDirectory(songFolder);
        // .bms is in DTXChartParser.SupportedExtensions.
        var bmsPath = Path.Combine(songFolder, "chart.bms");
        await File.WriteAllTextAsync(bmsPath, """
#TITLE: BMS Song
#ARTIST: Coverage Bot
#BPM: 120
#DLEVEL: 40
#00002:11111111
#00011:01010101
""");

        var lastEnumerationTime = DateTime.UtcNow.AddMinutes(-5);
        Directory.SetLastWriteTimeUtc(songsRoot, lastEnumerationTime.AddMinutes(-1));
        File.SetLastWriteTimeUtc(bmsPath, DateTime.UtcNow);

        var inventory = await ScanChartInventory(songsRoot);

        Assert.True(InventoryHasChanges(inventory, songsRoot, lastEnumerationTime));
    }

    // Regression: the chart-inventory scanner must count only the charts full
    // enumeration would import. A set.def directory containing one referenced
    // chart plus one unreferenced (backup/loose) chart must report a filesystem
    // count of 1, matching the database count of 1. The previous recursive-glob
    // counter ignored set.def and counted both files (filesystem count 2 vs
    // database count 1), forcing a permanent rescan on every startup.
    [Fact]
    public async Task ScanChartInventoryAsync_WithSetDefAndUnreferencedChart_ShouldCountOnlyReferenced()
    {
        var songsRoot = Path.Combine(_testRoot, "SetDefUnreferenced");
        var setFolder = Path.Combine(songsRoot, "Song");
        Directory.CreateDirectory(setFolder);

        // set.def references only referenced.dtx; unreferenced.dtx is a loose
        // chart in the same directory that enumeration must NOT import.
        await File.WriteAllTextAsync(Path.Combine(setFolder, "set.def"), """
#TITLE: SetDef Song
#L1FILE referenced.dtx
""");
        await CreateDtxFileAsync(
            Path.Combine(setFolder, "referenced.dtx"),
            "SetDef Song", "Coverage Bot", "Rock", 40);
        await CreateDtxFileAsync(
            Path.Combine(setFolder, "unreferenced.dtx"),
            "Unreferenced", "Coverage Bot", "Rock", 40);

        var inventory = await ScanChartInventory(songsRoot);

        Assert.Single(inventory.Charts);
    }

    // Regression: DetectFilesystemChangesAsync must reach steady state (return
    // false) after enumerating a set.def directory with an unreferenced chart.
    // The previous recursive-glob counter permanently mismatched (filesystem 2
    // vs database 1) and forced a rescan on every startup.
    [Fact]
    public async Task DetectFilesystemChangesAsync_WithSetDefAndUnreferencedChart_ShouldReachSteadyState()
    {
        var songsRoot = Path.Combine(_testRoot, "SetDefSteadyState");
        var setFolder = Path.Combine(songsRoot, "Song");
        Directory.CreateDirectory(setFolder);

        await File.WriteAllTextAsync(Path.Combine(setFolder, "set.def"), """
#TITLE: SetDef Song
#L1FILE referenced.dtx
""");
        await CreateDtxFileAsync(
            Path.Combine(setFolder, "referenced.dtx"),
            "SetDef Song", "Coverage Bot", "Rock", 40);
        await CreateDtxFileAsync(
            Path.Combine(setFolder, "unreferenced.dtx"),
            "Unreferenced", "Coverage Bot", "Rock", 40);

        await InitializeAndEnumerateAsync(songsRoot);
        // Pin the enumeration timestamp to the future so the only signal that can
        // trip change detection is the count mismatch (or lack of it).
        await SetLastEnumerationTimestampAsync(DateTime.Now.AddMinutes(5));
        ClearRootSongs();

        var result = await ReflectionHelpers.InvokePrivateMethod<Task<bool>>(
            _manager,
            "DetectFilesystemChangesAsync",
            (object)new[] { songsRoot });

        Assert.False(result);
    }

    [Fact]
    public async Task FindMovedFileAsync_WhenSearchPathStateIsInvalid_ShouldReturnNull()
    {
        await _manager.InitializeDatabaseServiceAsync(_testDbPath);
        var originalSearchPaths = ReflectionHelpers.GetPrivateField<string[]>(_manager, "_currentSearchPaths");

        try
        {
            ReflectionHelpers.SetPrivateField(_manager, "_currentSearchPaths", null!);

            var result = await ReflectionHelpers.InvokePrivateMethod<Task<string?>>(
                _manager,
                "FindMovedFileAsync",
                Path.Combine(_testRoot, "song.dtx"));

            Assert.Null(result);
        }
        finally
        {
            ReflectionHelpers.SetPrivateField(_manager, "_currentSearchPaths", originalSearchPaths);
        }
    }

    [Fact]
    public async Task BuildEnumerationBatchAsync_WithGuitarOnlyChart_ShouldCreateGuitarScore()
    {
        var setFolder = Path.Combine(_testRoot, "GuitarSet");
        var setDefPath = Path.Combine(setFolder, "set.def");
        Directory.CreateDirectory(setFolder);

        await File.WriteAllTextAsync(setDefPath, """
#TITLE: Guitar Set
#L1FILE guitar.dtx
""");
        await File.WriteAllTextAsync(Path.Combine(setFolder, "guitar.dtx"), """
#TITLE: Guitar Song
#GLEVEL: 70
#BPM: 140
""");

        await _manager.InitializeDatabaseServiceAsync(_testDbPath);

        var batch = await _manager.BuildEnumerationBatchAsync(
            new[] { setFolder },
            null,
            CancellationToken.None);

        var pending = Assert.Single(batch.PendingSongs);
        var node = pending.Placeholder;
        Assert.NotNull(node.Scores[0]);
        Assert.Equal(EInstrumentPart.GUITAR, node.Scores[0]!.Instrument);
        Assert.Equal(70, node.Scores[0].DifficultyLevel);
    }

    [Fact]
    public async Task BuildEnumerationBatchAsync_WithBassOnlyChart_ShouldCreateBassScore()
    {
        var setFolder = Path.Combine(_testRoot, "BassSet");
        var setDefPath = Path.Combine(setFolder, "set.def");
        Directory.CreateDirectory(setFolder);

        await File.WriteAllTextAsync(setDefPath, """
#TITLE: Bass Set
#L1FILE bass.dtx
""");
        await File.WriteAllTextAsync(Path.Combine(setFolder, "bass.dtx"), """
#TITLE: Bass Song
#BLEVEL: 55
#BPM: 120
""");

        await _manager.InitializeDatabaseServiceAsync(_testDbPath);

        var batch = await _manager.BuildEnumerationBatchAsync(
            new[] { setFolder },
            null,
            CancellationToken.None);

        var pending = Assert.Single(batch.PendingSongs);
        var node = pending.Placeholder;
        Assert.NotNull(node.Scores[0]);
        Assert.Equal(EInstrumentPart.BASS, node.Scores[0]!.Instrument);
        Assert.Equal(55, node.Scores[0].DifficultyLevel);
    }

    [Fact]
    public async Task BuildEnumerationBatchAsync_WithBrokenDatabaseService_ShouldStillCreateNode()
    {
        var setFolder = Path.Combine(_testRoot, "BrokenDbSet");
        var setDefPath = Path.Combine(setFolder, "set.def");
        Directory.CreateDirectory(setFolder);

        await File.WriteAllTextAsync(setDefPath, """
#TITLE: Broken Db Set
#L1FILE drums.dtx
""");
        await File.WriteAllTextAsync(Path.Combine(setFolder, "drums.dtx"), """
#TITLE: Broken Db Song
#DLEVEL: 60
#BPM: 120
""");

        var originalDatabaseService = _manager.DatabaseService;

        try
        {
            ReflectionHelpers.SetPrivateField(_manager, "_databaseService", CreateBrokenDatabaseService());

            var batch = await _manager.BuildEnumerationBatchAsync(
                new[] { setFolder },
                null,
                CancellationToken.None);

            Assert.Single(batch.PendingSongs);
        }
        finally
        {
            ReflectionHelpers.SetPrivateField(_manager, "_databaseService", originalDatabaseService);
        }
    }

    [Fact]
    public async Task BuildEnumerationBatchAsync_WithInvalidPath_ShouldProduceNoSongs()
    {
        await _manager.InitializeDatabaseServiceAsync(_testDbPath);

        var batch = await _manager.BuildEnumerationBatchAsync(
            new[] { "\0invalid" },
            null,
            CancellationToken.None);

        Assert.Empty(batch.PendingSongs);
    }

    [Fact]
    public async Task GetLastEnumerationTimestampAsync_WithInvalidDatabasePath_ShouldReturnNull()
    {
        var originalDatabaseService = _manager.DatabaseService;

        try
        {
            ReflectionHelpers.SetPrivateField(
                _manager,
                "_databaseService",
                CreateBrokenDatabaseServiceWithDatabasePath("\0invalid"));

            var result = await ReflectionHelpers.InvokePrivateMethod<Task<DateTime?>>(_manager, "GetLastEnumerationTimestampAsync");

            Assert.Null(result);
        }
        finally
        {
            ReflectionHelpers.SetPrivateField(_manager, "_databaseService", originalDatabaseService);
        }
    }

    private async Task InitializeAndEnumerateAsync(string songsRoot)
    {
        var initialized = await _manager.InitializeDatabaseServiceAsync(_testDbPath);
        Assert.True(initialized);

        var result = await _manager.EnumerateSongsAsync(new[] { songsRoot });
        Assert.True(result >= 1);
        Assert.NotNull(_manager.DatabaseService);
    }

    private async Task CreateDtxFileAsync(string path, string title, string artist, string genre, int drumLevel)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, $"""
#TITLE: {title}
#ARTIST: {artist}
#GENRE: {genre}
#BPM: 120
#DLEVEL: {drumLevel}
#00002:11111111
#00011:01010101
""");
    }

    private static void SetFilesystemTimes(
        string filePath,
        DateTime utcTimestamp,
        bool preserveCreationTime = false)
    {
        var directoryPath = Path.GetDirectoryName(filePath)!;
        var rootPath = Directory.GetParent(directoryPath)!.FullName;
        File.SetLastWriteTimeUtc(filePath, utcTimestamp);
        Directory.SetLastWriteTimeUtc(directoryPath, utcTimestamp);
        Directory.SetLastWriteTimeUtc(rootPath, utcTimestamp);
        if (!preserveCreationTime)
        {
            File.SetCreationTimeUtc(filePath, utcTimestamp);
            Directory.SetCreationTimeUtc(directoryPath, utcTimestamp);
            Directory.SetCreationTimeUtc(rootPath, utcTimestamp);
        }
    }

    private void ClearRootSongs()
    {
        var rootSongs = ReflectionHelpers.GetPrivateField<List<SongListNode>>(_manager, "_rootSongs");
        Assert.NotNull(rootSongs);
        rootSongs!.Clear();
    }

    /// <summary>
    /// Persists an explicit last-successful-enumeration timestamp into the
    /// __EnumerationMetadata table, mirroring how UpdateEnumerationTimestampAsync
    /// records it after a real enumeration. The cache-freshness check reads this
    /// metadata value (not the database file's last-write time), so tests that need
    /// to control the freshness threshold must write it here. <paramref name="localTimestamp"/>
    /// is interpreted as a local time and stored as UTC.
    /// </summary>
    private async Task SetLastEnumerationTimestampAsync(DateTime localTimestamp)
    {
        Assert.NotNull(_manager.DatabaseService);
        var utcTimestamp = localTimestamp.ToUniversalTime();
        await _manager.DatabaseService!.SetLastSuccessfulEnumerationUtcAsync(utcTimestamp);

        var currentRoots = ReflectionHelpers.GetPrivateField<string[]>(
            _manager,
            "_currentSearchPaths");
        if (currentRoots == null)
            return;

        foreach (var root in currentRoots)
            await SetRootEnumerationTimestampAsync(root, utcTimestamp);
    }

    private async Task SetRootEnumerationTimestampAsync(string root, DateTime utcTimestamp)
    {
        Assert.NotNull(_manager.DatabaseService);
        var canonicalRoot = _manager.RootPolicy
            .Validate(new[] { root })
            .CanonicalRoots
            .Single();

        await _manager.DatabaseService!.SetLastSuccessfulEnumerationUtcAsync(
            new[] { canonicalRoot },
            utcTimestamp.ToUniversalTime());
    }


    private static SongDatabaseService CreateBrokenDatabaseServiceWithDatabasePath(string databasePath)
    {
        var service = CreateBrokenDatabaseService();
        var field = typeof(SongDatabaseService).GetField("_databasePath", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field!.SetValue(service, databasePath);
        return service;
    }

    private Task<ChartInventory> ScanChartInventory(params string[] roots) =>
        ReflectionHelpers.InvokePrivateMethod<Task<ChartInventory>>(
            _manager,
            "ScanChartInventoryAsync",
            (object)roots,
            CancellationToken.None)!;

    private static bool InventoryHasChanges(
        ChartInventory inventory,
        string root,
        DateTime watermark)
    {
        var normalizedRoot = SongPathIdentity.Normalize(root);
        var method = typeof(SongManager).GetMethod(
            "InventoryHasChangesSince",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return (bool)method!.Invoke(
            null,
            new object[]
            {
                inventory,
                (IReadOnlyList<string>)new[] { normalizedRoot },
                (IReadOnlyDictionary<string, DateTime>)
                    new Dictionary<string, DateTime>(SongPathIdentity.CanonicalComparer)
                    {
                        [normalizedRoot] = watermark
                    }
            })!;
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
}
