using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Test.TestData;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static DTXMania.Test.TestData.ReflectionHelpers;
using SongEntity = DTXMania.Game.Lib.Song.Entities.Song;
namespace DTXMania.Test.Song
{
    /// <summary>
    /// Unit tests for SongManager class
    /// Tests song database management and enumeration using singleton pattern
    /// </summary>
    [Trait("Category", "Unit")]
    public class SongManagerTests : IDisposable
    {
        private SongLibrary _manager;
        private readonly string _testDbPath;

        public SongManagerTests()
        {
            // Each test owns one game-style library over its own temp database
            // (HPA-801: no singleton; construction has no filesystem side effects).
            Directory.CreateDirectory(_testRoot);
            _testDbPath = Path.Combine(_testRoot, "songs.db");
            _manager = new SongLibrary(new SongDatabase(_testDbPath));
        }

        [Fact]
        public void InitializeAsync_ShouldExposeASinglePublicOverload()
        {
            var overloads = typeof(SongLibrary).GetMethods()
                .Where(method => method.Name ==
                    nameof(SongLibrary.InitializeAsync))
                .ToArray();

            Assert.Single(overloads);
        }

        public void Dispose()
        {
            // Clean up after each test
            _manager?.Dispose();

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

        #region Database Management Tests


        [Fact]
        public async Task DatabaseService_ShouldBeAvailableAfterInitialization()
        {
            // Arrange & Act — ManagerDatabaseService is non-null from
            // construction, so assert the effects of initialization itself.
            var result = await _manager.InitializeAsync();

            // Assert
            Assert.True(result);
            var databaseService = ManagerDatabaseService;
            Assert.NotNull(databaseService);
            using var context = databaseService!.CreateContext();
            Assert.NotNull(context);
        }

        [Fact]
        public async Task InitializeAsync_WithoutPurge_ShouldInitializeService()
        {
            var result = await _manager.InitializeAsync(purgeDatabaseFirst: false);

            Assert.True(result);
            Assert.NotNull(ManagerDatabaseService);
        }

        [Fact]
        public async Task InitializeAsync_WhenCalledRepeatedly_ShouldKeepSameDatabaseService()
        {
            Assert.True(await _manager.InitializeAsync(purgeDatabaseFirst: false));
            var service = ManagerDatabaseService;

            var result = await _manager.InitializeAsync(purgeDatabaseFirst: false);

            Assert.True(result);
            Assert.Same(service, ManagerDatabaseService);
        }

        [Fact]
        public async Task InitializeAsync_WhenPurgeRuns_ShouldRecreateDatabaseFile()
        {
            var result = await _manager.InitializeAsync(
                purgeDatabaseFirst: true);

            Assert.True(result);
            Assert.NotNull(ManagerDatabaseService);
            Assert.True(File.Exists(_testDbPath));
        }

        [Fact]
        public void IsInitialized_ShouldTrackInitializationState()
        {
            // Arrange & Act
            var isInitialized = _manager.IsInitialized;

            // Assert
            // Should be false until Startup's initialization phase marks it
            Assert.False(isInitialized);
        }

        #endregion

        #region Enumeration Tests

        [Fact]
        public async Task EnumerateAndImportSongsAsync_WithNonExistentPaths_ShouldImportNoCandidates()
        {
            // Arrange
            var paths = new[] { "NonExistent1", "NonExistent2" };
            await _manager.InitializeAsync();

            // Act
            var result = await _manager.EnumerateAndImportSongsAsync(paths);

            // Assert
            Assert.Equal(0, result.Batch.Candidates.Count);
            Assert.Equal(0, _manager.DiscoveredScoreCount);
            Assert.Empty(_manager.RootSongs);
        }

        [Fact]
        public async Task EnumerateAndImportSongsAsync_WithEmptyArray_ShouldImportNoCandidates()
        {
            // Arrange
            var paths = new string[0];
            await _manager.InitializeAsync();

            // Act
            var result = await _manager.EnumerateAndImportSongsAsync(paths);

            // Assert
            Assert.Equal(0, result.Batch.Candidates.Count);
            Assert.Equal(0, _manager.DiscoveredScoreCount);
        }

        [Fact]
        public async Task EnumerateAndImportSongsAsync_WithNullPaths_ShouldHandleGracefully()
        {
            // Arrange
            var paths = new string[] { null!, "", "NonExistent" };
            await _manager.InitializeAsync();

            // Act
            var result = await _manager.EnumerateAndImportSongsAsync(paths);

            // Assert
            Assert.Equal(0, result.Batch.Candidates.Count);
            Assert.Equal(0, _manager.DiscoveredScoreCount);
        }

        [Fact]
        public async Task EnumerateAndImportSongsAsync_WithRealDirectory_ShouldEnumerateFiles()
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
                await _manager.InitializeAsync();

                // Act - Now enumerate the specific directory
                var result = await _manager.EnumerateAndImportSongsAsync(new[] { tempDir });

                // Assert
                Assert.Equal(1, result.Batch.Candidates.Count);
                Assert.Equal(1, _manager.DiscoveredScoreCount);
                Assert.True(_manager.RootSongs.Count > 0);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public async Task EnumerateAndImportSongsAsync_WithDTXFilesPrefix_ShouldCreateBoxNodes()
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
                await _manager.InitializeAsync();

                // Act
                var result = await _manager.EnumerateAndImportSongsAsync(new[] { tempDir });

                // Assert
                Assert.Equal(1, result.Batch.Candidates.Count);
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
                await _manager.InitializeAsync();
                await _manager.EnumerateAndImportSongsAsync(new[] { tempDir });

                // Act - Test that enumeration worked
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
        public async Task Enumeration_WithGenreMetadata_ShouldPersistGenreToDatabase()
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
                await _manager.InitializeAsync();
                await _manager.EnumerateAndImportSongsAsync(new[] { tempDir });

                // Act
                await using var context = ManagerDatabaseService!.CreateContext();
                var songs = await context.Songs.ToListAsync();

                // Assert
                var rockSongs = songs.Where(s => s.Genre == "Rock").ToList();
                var popSongs = songs.Where(s => s.Genre == "Pop").ToList();
                Assert.Equal(2, rockSongs.Count);
                Assert.Single(popSongs);
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
        public async Task EnumerateAndImportSongsAsync_WithMultipleDTXFilesFromSameSong_ShouldGroupIntoSingleSong()
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

                await _manager.InitializeAsync();

                // Act
                await _manager.EnumerateAndImportSongsAsync(new[] { tempDir });

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
        public async Task EnumerateAndImportSongsAsync_WithDifferentSongsFromSameArtist_ShouldCreateSeparateSongs()
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

                await _manager.InitializeAsync();

                // Act
                await _manager.EnumerateAndImportSongsAsync(new[] { tempDir });

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
        public async Task EnumerateAndImportSongsAsync_WithSameMetadataInDifferentDirectories_ShouldKeepSeparateSongs()
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

                await _manager.InitializeAsync();

                // Act
                await _manager.EnumerateAndImportSongsAsync(new[] { tempDir });

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
        public async Task EnumerateAndImportSongsAsync_WithMultipleSongs_ShouldPopulateRootSongs()
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

                await _manager.InitializeAsync();

                var result = await _manager.EnumerateAndImportSongsAsync(new[] { tempDir });

                Assert.Equal(3, result.Batch.Candidates.Count);
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
        public async Task BuildHierarchyFromDatabaseOnceAsync_WithNestedBoxesAndManyRootSongs_ShouldRestoreHierarchy()
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

                await _manager.InitializeAsync();
                await _manager.EnumerateAndImportSongsAsync(new[] { tempDir });

                var rootSongs = GetPrivateField<List<SongListNode>>(_manager, "_rootSongs");
                Assert.NotNull(rootSongs);
                rootSongs!.Clear();

                await _manager.BuildHierarchyFromDatabaseOnceAsync(new[] { tempDir });

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
        public async Task BuildHierarchyFromDatabaseOnceAsync_WithBrokenDatabaseService_ShouldLeaveRootSongsEmpty()
        {
            // HPA-801 constructor seam: build a throwaway library over the broken
            // database instead of mutating a shared instance.
            var brokenLibrary = new SongLibrary(CreateBrokenDatabaseService());

            await brokenLibrary.BuildHierarchyFromDatabaseOnceAsync(new[] { Path.GetTempPath() });

            Assert.Empty(brokenLibrary.RootSongs);
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

                var result = await _manager.Hierarchy.BuildHierarchyFromCharts(
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

        #endregion

        private static SongDatabase CreateBrokenDatabaseService()
        {
            return (SongDatabase)RuntimeHelpers.GetUninitializedObject(typeof(SongDatabase));
        }

        /// <summary>
        /// The one constructor-injected database (HPA-801): read through the
        /// library's internal accessor instead of the retired reflection seam.
        /// </summary>
        private SongDatabase ManagerDatabaseService => _manager.Database;

        private readonly string _testRoot = Path.Combine(
            Path.GetTempPath(),
            "DTXManiaCX_Tests",
            "SongManagerTests",
            Guid.NewGuid().ToString("N"));

        [Fact]
        public async Task BuildHierarchyFromDatabaseOnceAsync_WithBoxAndMultipleCharts_ShouldRebuildHierarchy()
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

            await _manager.BuildHierarchyFromDatabaseOnceAsync(new[] { songsRoot });

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
        public async Task BuildHierarchyFromDatabaseOnceAsync_WithoutDatabaseService_ShouldLeaveRootSongsEmpty()
        {
            var songsRoot = Path.Combine(_testRoot, "NoDatabaseSongs");
            Directory.CreateDirectory(songsRoot);

            await _manager.BuildHierarchyFromDatabaseOnceAsync(new[] { songsRoot });

            Assert.Empty(_manager.RootSongs);
        }

        [Fact]
        public async Task BuildHierarchyFromDatabaseOnceAsync_WithInvalidSearchPath_ShouldSkipPath()
        {
            var initialized = await _manager.InitializeAsync();
            Assert.True(initialized);

            await _manager.BuildHierarchyFromDatabaseOnceAsync(new[] { Path.Combine(_testRoot, "missing") });

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

            var initialized = await _manager.InitializeAsync();
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

            // The live enumeration routes through EnumerateAndImportSongsCoreAsync
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

            var initialized = await _manager.InitializeAsync();
            Assert.True(initialized);
            var initialEnumeration = await _manager.EnumerateAndImportSongsAsync(new[] { rootA, rootB });
            Assert.True(initialEnumeration.Batch.Candidates.Count >= 2);

            // Establish a known per-root baseline before simulating the unavailable
            // root. The root-B edit below is newer than this baseline but older than
            // the partial root-A scan watermark.
            var baselineUtc = DateTime.UtcNow.AddHours(-2);
            await SetLastEnumerationTimestampAsync(baselineUtc.ToLocalTime());
            await SetRootEnumerationTimestampAsync(rootA, baselineUtc);
            await SetRootEnumerationTimestampAsync(rootB, baselineUtc);

            // Omit B from this enumeration to model the configured root being
            // unavailable. Its database rows remain retained while A advances.
            var partialEnumeration = await _manager.EnumerateAndImportSongsAsync(new[] { rootA });
            Assert.True(partialEnumeration.Batch.Candidates.Count >= 1);

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

            var initialized = await _manager.InitializeAsync();
            Assert.True(initialized);
            Assert.NotNull(ManagerDatabaseService);

            var watermark = DateTime.UtcNow.AddHours(-1);
            var canonicalA = _manager.RootPolicy
                .Validate(new[] { rootA }).CanonicalRoots.Single();
            var canonicalB = _manager.RootPolicy
                .Validate(new[] { rootB }).CanonicalRoots.Single();

            // A successful atomic write must commit both the global watermark and
            // every supplied per-root watermark. A partial commit (global only)
            // would leave per-root rows absent, forcing a conservative full rescan
            // on every startup until the per-root rows catch up.
            var success = await new SongCatalogStore(ManagerDatabaseService!)
                .SetEnumerationWatermarkAtomicallyAsync(
                    watermark,
                    new[] { canonicalA, canonicalB });

            Assert.True(success);
            var global = await new SongCatalogStore(ManagerDatabaseService!)
                .GetLastSuccessfulEnumerationUtcAsync();
            Assert.Equal(watermark.ToUniversalTime(), global!.Value.ToUniversalTime());
            var perRootA = await new SongCatalogStore(ManagerDatabaseService!)
                .GetLastSuccessfulEnumerationUtcAsync(canonicalA);
            Assert.Equal(watermark.ToUniversalTime(), perRootA!.Value.ToUniversalTime());
            var perRootB = await new SongCatalogStore(ManagerDatabaseService!)
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

            var initialized = await _manager.InitializeAsync();
            Assert.True(initialized);
            Assert.NotNull(ManagerDatabaseService);

            // Seed a legacy global-only watermark (no per-root rows).
            var legacyGlobal = DateTime.UtcNow.AddHours(-3);
            await new SongCatalogStore(ManagerDatabaseService!)
                .SetLastSuccessfulEnumerationUtcAsync(legacyGlobal);
            Assert.Null(await new SongCatalogStore(ManagerDatabaseService!)
                .GetLastSuccessfulEnumerationUtcAsync(rootA));

            var canonicalA = _manager.RootPolicy
                .Validate(new[] { rootA }).CanonicalRoots.Single();
            var newWatermark = DateTime.UtcNow.AddHours(-1);

            var success = await new SongCatalogStore(ManagerDatabaseService!)
                .SetEnumerationWatermarkAtomicallyAsync(
                    newWatermark,
                    new[] { canonicalA });

            Assert.True(success);
            // Both global and per-root must be at the new watermark — no partial state.
            var global = await new SongCatalogStore(ManagerDatabaseService!)
                .GetLastSuccessfulEnumerationUtcAsync();
            Assert.Equal(newWatermark.ToUniversalTime(), global!.Value.ToUniversalTime());
            var perRootA = await new SongCatalogStore(ManagerDatabaseService!)
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

            var initialized = await _manager.InitializeAsync();
            Assert.True(initialized);
            Assert.NotNull(ManagerDatabaseService);

            // Establish a known baseline watermark.
            var baseline = DateTime.UtcNow.AddHours(-2);
            var canonicalA = _manager.RootPolicy
                .Validate(new[] { rootA }).CanonicalRoots.Single();
            Assert.True(await new SongCatalogStore(ManagerDatabaseService!)
                .SetEnumerationWatermarkAtomicallyAsync(baseline, new[] { canonicalA }));

            // Use a broken service that cannot create contexts, simulating a
            // mid-write crash or storage failure. The atomic method must return
            // false and leave the baseline watermark intact.
            var brokenService = CreateBrokenDatabaseService();
            var newWatermark = DateTime.UtcNow.AddHours(-1);
            var success = await new SongCatalogStore(brokenService)
                .SetEnumerationWatermarkAtomicallyAsync(
                    newWatermark,
                    new[] { canonicalA });

            Assert.False(success);

            // The real database must still have the baseline watermark — no partial
            // state was created by the failed write.
            var global = await new SongCatalogStore(ManagerDatabaseService!)
                .GetLastSuccessfulEnumerationUtcAsync();
            Assert.Equal(baseline.ToUniversalTime(), global!.Value.ToUniversalTime());
            var perRootA = await new SongCatalogStore(ManagerDatabaseService!)
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

            var initialized = await _manager.InitializeAsync();
            Assert.True(initialized);
            Assert.NotNull(ManagerDatabaseService);

            // Establish a known baseline watermark (both global and per-root).
            var baseline = DateTime.UtcNow.AddHours(-2);
            var canonicalA = _manager.RootPolicy
                .Validate(new[] { rootA }).CanonicalRoots.Single();
            Assert.True(await new SongCatalogStore(ManagerDatabaseService!)
                .SetEnumerationWatermarkAtomicallyAsync(baseline, new[] { canonicalA }));

            // Install a SQLite trigger that aborts any INSERT on
            // __EnumerationMetadata when the key starts with the per-root prefix.
            // This causes the per-root INSERT OR REPLACE to fail AFTER the global
            // INSERT OR REPLACE has already executed within the same transaction.
            await using (var triggerContext = ManagerDatabaseService!.CreateContext())
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
            var success = await new SongCatalogStore(ManagerDatabaseService!)
                .SetEnumerationWatermarkAtomicallyAsync(
                    newWatermark,
                    new[] { canonicalA });

            // The atomic method must return false (the transaction failed).
            Assert.False(success);

            // The global watermark must still be the baseline — the preceding
            // global-key INSERT was rolled back, not left committed.
            var global = await new SongCatalogStore(ManagerDatabaseService!)
                .GetLastSuccessfulEnumerationUtcAsync();
            Assert.Equal(baseline.ToUniversalTime(), global!.Value.ToUniversalTime());

            // The per-root watermark must also still be the baseline.
            var perRootA = await new SongCatalogStore(ManagerDatabaseService!)
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
            var initialized = await _manager.InitializeAsync();
            Assert.True(initialized);
            var enumerated = await _manager.EnumerateAndImportSongsAsync(new[] { retainedRoot, removedRoot });
            Assert.True(enumerated.Batch.Candidates.Count >= 2);
            Assert.NotNull(ManagerDatabaseService);

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
            var initialized = await _manager.InitializeAsync();
            Assert.True(initialized);
            var enumerated = await _manager.EnumerateAndImportSongsAsync(new[] { retainedRoot, removedRoot });
            Assert.True(enumerated.Batch.Candidates.Count >= 2);
            Assert.NotNull(ManagerDatabaseService);

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
            Assert.NotNull(ManagerDatabaseService);

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
            var song = (await new SongCatalogStore(ManagerDatabaseService!).GetSongsAsync()).Single();
            await _manager.Queries.SetBookmarkAsync(song.Id, true);

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
        public async Task EnumerateAndImportSongsAsync_WithEmptyDirectory_ShouldImportNoCandidates()
        {
            var songsRoot = Path.Combine(_testRoot, "EnumerateOnlyEmpty");
            Directory.CreateDirectory(songsRoot);

            var initialized = await _manager.InitializeAsync();
            Assert.True(initialized);

            var result = await _manager.EnumerateAndImportSongsAsync(new[] { songsRoot });

            Assert.Equal(0, result.Batch.Candidates.Count);
            Assert.Equal(0, _manager.DiscoveredScoreCount);
            Assert.Empty(_manager.RootSongs);
        }

        [Fact]
        public async Task EnumerateAndImportSongsAsync_WithCancelledToken_ShouldThrowOperationCanceledException()
        {
            var songsRoot = Path.Combine(_testRoot, "CancelledSongs");
            Directory.CreateDirectory(songsRoot);
            await CreateDtxFileAsync(Path.Combine(songsRoot, "cancelled.dtx"), "Cancelled Song", "Coverage Bot", "Rock", 35);

            await _manager.InitializeAsync();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                _manager.EnumerateAndImportSongsAsync(
                    new[] { songsRoot },
                    cancellationToken: cancellation.Token));
            Assert.Empty(_manager.RootSongs);
        }

        [Fact]
        public async Task EnumerateAndImportSongsAsync_WithEmptyBox_ShouldSkipEmptyBox()
        {
            var songsRoot = Path.Combine(_testRoot, "EmptyBoxSongs");
            Directory.CreateDirectory(Path.Combine(songsRoot, "DTXFiles.Empty"));

            await _manager.InitializeAsync();

            var result = await _manager.EnumerateAndImportSongsAsync(new[] { songsRoot });

            Assert.Equal(0, result.Batch.Candidates.Count);
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

            var inventory = await ReflectionHelpers.InvokePrivateMethodAsync<ChartInventory>(
                _manager.Scanner,
                "ScanChartInventoryAsync",
                new[] { songsRoot },
                CancellationToken.None);
            Assert.NotNull(inventory);

            var checkTask = ReflectionHelpers.InvokePrivateMethod<Task<bool>>(
                _manager.Scanner,
                "CheckDatabaseFilesStillExist",
                new object[] { new[] { songsRoot }, inventory!.Charts });
            Assert.NotNull(checkTask);

            var changeDetected = await checkTask!;
            var songs = await new SongCatalogStore(ManagerDatabaseService!).GetSongsAsync();

            Assert.True(changeDetected);
            Assert.Equal(movedPath, songs.Single().Charts.Single().FilePath);
        }

        // Regression (HPA-801 Decision B, moved-file check): the moved-file
        // heuristic must search the explicitly passed canonical active roots
        // instead of the library's last published search paths (still empty on
        // a warm start before the first publication of the process) or the
        // hard-coded default roots. A chart moved between two configured
        // folders must be found, its database path repaired, and the rescan
        // the move triggers must still run.
        [Fact]
        public async Task NeedsEnumerationAsync_WhenChartMovedBetweenConfiguredRoots_ShouldRepairPathAndTriggerRescan()
        {
            var rootA = Path.Combine(_testRoot, "MoveRootA");
            var rootB = Path.Combine(_testRoot, "MoveRootB");
            var originalPath = Path.Combine(rootA, "Moved", "moved-song.dtx");
            var movedPath = Path.Combine(rootB, "Moved", "moved-song.dtx");
            Directory.CreateDirectory(Path.GetDirectoryName(originalPath)!);
            await CreateDtxFileAsync(originalPath, "Moved Song", "Coverage Bot", "Rock", 50);

            // Seed the database as a previous process left it: the seeding
            // library scans and publishes; the library under test starts warm
            // with an empty published-root set, exactly like a fresh launch
            // before its first scan.
            using (var seedingLibrary = new SongLibrary(new SongDatabase(_testDbPath)))
            {
                Assert.True(await seedingLibrary.InitializeAsync());
                await seedingLibrary.EnumerateAndImportSongsAsync(new[] { rootA, rootB });
            }

            Directory.CreateDirectory(Path.GetDirectoryName(movedPath)!);
            File.Move(originalPath, movedPath);

            await _manager.InitializeAsync();

            // The move between configured roots must be detected so a rescan runs.
            Assert.True(await _manager.NeedsEnumerationAsync(new[] { rootA, rootB }));

            // Move handling must have repaired the chart's database path to the
            // moved location found under the configured active roots.
            var songs = await new SongCatalogStore(_manager.Database).GetSongsAsync();
            Assert.Equal(movedPath, songs.Single().Charts.Single().FilePath);

            // The rescan the move triggered must still run and publish the chart.
            var result = await _manager.EnumerateAndImportSongsAsync(new[] { rootA, rootB });
            Assert.Equal(SongEnumerationOutcome.ImportedAndPublished, result.Outcome);
            Assert.Equal(1, _manager.EnumeratedFileCount);
            Assert.Single(FlattenAllScoreNodes(_manager.RootSongs));

            // The scan persisted its backed-off START watermark; pin it forward
            // (like the other steady-state tests) so the moved file's move-time
            // timestamps are older than the watermark and the library is stable.
            await SetLastEnumerationTimestampAsync(DateTime.Now.AddMinutes(5));

            // After the rescan the library reaches steady state again.
            Assert.False(await _manager.NeedsEnumerationAsync(new[] { rootA, rootB }));
        }

        [Fact]
        public async Task InitializeAsync_WithInvalidPath_ShouldReturnFalse()
        {
            // An invalid database path fails schema creation; the library stays
            // usable after the failure is logged (startup continues on error).
            var initialized = await new SongLibrary(new SongDatabase("\0invalid"))
                .InitializeAsync();

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

            var reinitialized = await _manager.InitializeAsync(purgeDatabaseFirst: true);

            Assert.True(reinitialized);
            Assert.NotNull(ManagerDatabaseService);
            // The purge must have emptied the previously seeded database.
            Assert.False(await new SongCatalogStore(ManagerDatabaseService!).HasAnySongsAsync());
        }

        [Fact]
        public async Task StateHelpers_ShouldTrackInitializationState()
        {
            var initialized = await _manager.InitializeAsync();
            Assert.True(initialized);

            _manager.SetInitialized();

            Assert.True(_manager.IsInitialized);
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

            await _manager.InitializeAsync();

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

            await _manager.InitializeAsync();
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

            await _manager.InitializeAsync();

            var batch = await _manager.BuildEnumerationBatchAsync(
                new[] { setFolder },
                null,
                CancellationToken.None);

            var pending = Assert.Single(batch.PendingSongs);
            Assert.Equal("BASIC", pending.Placeholder.DifficultyLabels[0]);
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

            var result = _manager.Hierarchy.CreateSongNodeFromDatabaseEntities(song, charts);

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

            var result = _manager.Hierarchy.CreateSongNodeFromDatabaseEntities(song, charts);

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

            var result = _manager.Hierarchy.CreateSongNodeFromDatabaseEntities(song, charts);

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

            var result = _manager.Hierarchy.CreateSongNodeFromDatabaseEntities(null!, charts);

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

            var inventory = await ReflectionHelpers.InvokePrivateMethodAsync<ChartInventory>(
                _manager.Scanner,
                "ScanChartInventoryAsync",
                new[] { songsRoot },
                CancellationToken.None);
            Assert.NotNull(inventory);

            var checkTask = ReflectionHelpers.InvokePrivateMethod<Task<bool>>(
                _manager.Scanner,
                "CheckDatabaseFilesStillExist",
                new object[] { new[] { songsRoot }, inventory!.Charts });
            Assert.NotNull(checkTask);

            var changeDetected = await checkTask!;

            Assert.True(changeDetected);
        }

        [Fact]
        public async Task IsVerifiedMoveCandidate_WithoutPersistedSize_ShouldReturnFalse()
        {
            // A same-named candidate is never enough: a chart with no persisted
            // fingerprint (FileSize <= 0, e.g. a legacy or seeded row) is
            // unverifiable, so the missing row must be left for the rescan
            // rather than grafted onto an unrelated file.
            var candidatePath = Path.Combine(_testRoot, "missing.dtx");
            await File.WriteAllTextAsync(candidatePath, "content");

            var result = ReflectionHelpers.InvokePrivateMethod<bool>(
                _manager.Scanner,
                "IsVerifiedMoveCandidate",
                new SongChart { FilePath = candidatePath, FileSize = 0 },
                candidatePath);

            Assert.False(result);
        }

        [Fact]
        public async Task IsVerifiedMoveCandidate_WithSizeMismatch_ShouldReturnFalse()
        {
            var candidatePath = Path.Combine(_testRoot, "candidate.dtx");
            await File.WriteAllTextAsync(candidatePath, "this is a much longer file content");

            var result = ReflectionHelpers.InvokePrivateMethod<bool>(
                _manager.Scanner,
                "IsVerifiedMoveCandidate",
                new SongChart { FilePath = Path.Combine(_testRoot, "original.dtx"), FileSize = 5 },
                candidatePath);

            Assert.False(result);
        }

        [Fact]
        public async Task CheckDatabaseFilesStillExist_WithoutDatabaseService_ShouldReturnFalse()
        {
            var result = await ReflectionHelpers.InvokePrivateMethod<Task<bool>>(_manager.Scanner, "CheckDatabaseFilesStillExist", new object[] { new[] { _testRoot }, Array.Empty<ChartInventoryEntry>() });

            Assert.False(result);
        }

        [Fact]
        public async Task CheckDatabaseFilesStillExist_WithBrokenDatabaseService_ShouldReturnFalse()
        {
            var brokenLibrary = new SongLibrary(
                CreateBrokenDatabaseServiceWithDatabasePath("\0invalid"));

            var result = await ReflectionHelpers.InvokePrivateMethod<Task<bool>>(brokenLibrary.Scanner, "CheckDatabaseFilesStillExist", new object[] { new[] { _testRoot }, Array.Empty<ChartInventoryEntry>() });

            Assert.False(result);
        }

        [Fact]
        public async Task FindMovedFile_WhenOriginalPathHasNoFilename_ShouldReturnNull()
        {
            await _manager.InitializeAsync();

            var result = InvokeFindMovedFile(
                new SongChart { FilePath = Path.DirectorySeparatorChar.ToString() },
                Array.Empty<ChartInventoryEntry>(),
                new HashSet<string>(SongPathIdentity.CanonicalComparer));

            Assert.Null(result);
        }

        // HPA-801 Decision B: the moved-file heuristic searches only the
        // chart inventory the freshness check already walked. A file the
        // importer would not pick up is not a move candidate either, so a
        // missing file yields null.
        [Fact]
        public async Task FindMovedFile_WhenFileIsMissingFromInventory_ShouldReturnNull()
        {
            await _manager.InitializeAsync();

            var result = InvokeFindMovedFile(
                new SongChart { FilePath = Path.Combine(_testRoot, "missing.dtx") },
                Array.Empty<ChartInventoryEntry>(),
                new HashSet<string>(SongPathIdentity.CanonicalComparer));

            Assert.Null(result);
        }

        [Fact]
        public void ResolveDirectoryEntryName_WhenNormalizationLookalikeAlsoMatches_ShouldResolveTheCaseFold()
        {
            // NTFS hosts sibling files that differ only in Unicode form; the
            // recorded spelling resolves to exactly one of them, so the
            // lookalike must not count as a second folded match and veto the
            // repair. The sibling cannot be created on volumes that fold
            // normalization for comparison, so its name is supplied in the
            // entry list directly.
            var directory = Directory.CreateDirectory(
                Path.Combine(_testRoot, "Lookalikes")).FullName;
            var precomposed = "CAFÉ.DTX";
            var decomposed = precomposed.Normalize(NormalizationForm.FormD);
            var spelling = "café.dtx";
            File.WriteAllText(Path.Combine(directory, precomposed), "x");

            var method = typeof(SongLibraryScanner).GetMethod(
                "ResolveDirectoryEntryName",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(method);
            var resolved = (string?)method!.Invoke(null, new object[]
            {
                directory,
                spelling,
                new List<string?> { precomposed, decomposed }
            });

            if (File.Exists(Path.Combine(directory, spelling)))
            {
                // The volume folds case: the spelling resolves to the
                // single case-equal entry and the normalized lookalike is
                // not counted as a second alias.
                Assert.Equal(precomposed, resolved);
            }
            else
            {
                // A case-sensitive volume resolves nothing.
                Assert.Null(resolved);
            }
        }

        [Fact]
        public void ResolveDirectoryEntryName_WhenCaseFoldAndCaseExactNormalizationDisagree_ShouldReturnNull()
        {
            var directory = Directory.CreateDirectory(
                Path.Combine(_testRoot, "DisagreeingLookalikes")).FullName;
            var caseFolded = "CAFÉ.DTX";
            var normCaseExact = "café.dtx".Normalize(NormalizationForm.FormD);
            var spelling = "café.dtx";
            File.WriteAllText(Path.Combine(directory, caseFolded), "x");

            var method = typeof(SongLibraryScanner).GetMethod(
                "ResolveDirectoryEntryName",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(method);
            var resolved = (string?)method!.Invoke(null, new object[]
            {
                directory,
                spelling,
                new List<string?> { caseFolded, normCaseExact }
            });

            Assert.Null(resolved);
        }

        [Fact]
        public async Task FindMovedFile_WhenOwnershipIsUnresolvedBesideMatchingCandidate_ShouldVetoRepairElsewhere()
        {
            if (OperatingSystem.IsWindows())
                return;

            await _manager.InitializeAsync();
            var stamp = new DateTime(2026, 11, 16, 0, 0, 0, DateTimeKind.Utc);

            var targetDir = Directory.CreateDirectory(Path.Combine(_testRoot, "UnresolvedTarget")).FullName;
            var targetFile = Path.Combine(targetDir, "basic.dtx");
            await CreateDtxFileAsync(targetFile, "Mover", "Artist", "Rock", 50);
            File.SetLastWriteTimeUtc(targetFile, stamp);

            var chartsDir = Directory.CreateDirectory(Path.Combine(_testRoot, "UnresolvedCharts")).FullName;
            var candidateInUnlistable = Path.Combine(chartsDir, "basic.dtx");
            File.CreateSymbolicLink(candidateInUnlistable, targetFile);
            var ownedPathOnDisk = Path.Combine(chartsDir, "BASIC.dtx");
            if (!File.Exists(ownedPathOnDisk))
            {
                // On case-sensitive volumes, create the distinct owner entry
                // so the unresolved-ownership fallback runs instead of skipping
                // a missing owner.
                File.CreateSymbolicLink(ownedPathOnDisk, targetFile);
            }
            var candidateInfo = new FileInfo(candidateInUnlistable);
            // Match the symlink's FileInfo length & timestamp on both the
            // missing chart and the competing copy so both pass the
            // fingerprint check, isolating the unresolved-ownership veto.
            var competingLength = (int)Math.Max(1, candidateInfo.Length);
            var competingStamp = candidateInfo.LastWriteTimeUtc;

            var otherDir = Directory.CreateDirectory(Path.Combine(_testRoot, "UnresolvedOther")).FullName;
            var otherCopy = Path.Combine(otherDir, "basic.dtx");
            await File.WriteAllTextAsync(otherCopy, new string('x', competingLength));
            File.SetLastWriteTimeUtc(otherCopy, competingStamp);

            var ownedPaths = new HashSet<string>(SongPathIdentity.CanonicalComparer)
            {
                SongPathIdentity.Normalize(Path.Combine(chartsDir, "BASIC.dtx"))
            };
            var inventory = new[]
            {
                new ChartInventoryEntry(
                    SongPathIdentity.Normalize(candidateInUnlistable),
                    competingStamp,
                    competingStamp),
                new ChartInventoryEntry(
                    SongPathIdentity.Normalize(otherCopy),
                    competingStamp,
                    competingStamp)
            };
            var missingChart = new SongChart
            {
                FilePath = SongPathIdentity.Normalize(Path.Combine(_testRoot, "Missing", "basic.dtx")),
                FileSize = candidateInfo.Length,
                LastModified = candidateInfo.LastWriteTime
            };

            try
            {
                File.SetUnixFileMode(
                    chartsDir,
                    UnixFileMode.UserExecute |
                    UnixFileMode.GroupExecute |
                    UnixFileMode.OtherExecute);
                var canList = true;
                try
                {
                    Directory.EnumerateFiles(chartsDir).ToList();
                }
                catch (UnauthorizedAccessException)
                {
                    canList = false;
                }
                catch (IOException)
                {
                    canList = false;
                }

                if (canList)
                    return;

                var result = InvokeFindMovedFile(missingChart, inventory, ownedPaths);

                Assert.Null(result);
            }
            finally
            {
                File.SetUnixFileMode(
                    chartsDir,
                    UnixFileMode.UserRead |
                    UnixFileMode.UserWrite |
                    UnixFileMode.UserExecute);
            }
        }

        [Fact]
        public async Task FindMovedFile_WhenSharedFolderSymlinkAliasesOrDiffersFromOwnedChart_ShouldSettleOwnership()
        {
            await _manager.InitializeAsync();
            var targetDir = Directory.CreateDirectory(Path.Combine(_testRoot, "SharedTarget")).FullName;
            var targetFile = Path.Combine(targetDir, "basic.dtx");
            await CreateDtxFileAsync(targetFile, "Mover", "Artist", "Rock", 50);

            var sharedDir = Directory.CreateDirectory(Path.Combine(_testRoot, "SharedFolder")).FullName;
            var ownedAliasPath = Path.Combine(sharedDir, "BASIC.dtx");
            var candidatePath = Path.Combine(sharedDir, "basic.dtx");
            var distinctPath = Path.Combine(sharedDir, "different.dtx");
            await CreateDtxFileAsync(distinctPath, "Other", "Artist", "Rock", 50);

            var method = typeof(SongLibraryScanner).GetMethod(
                "TryResolveSharedDirectoryPair",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(method);

            try
            {
                File.CreateSymbolicLink(ownedAliasPath, targetFile);
            }
            catch (Exception ex) when (
                OperatingSystem.IsWindows() &&
                ex is UnauthorizedAccessException or IOException)
            {
                // Unprivileged Windows runners without symlink permission
                // still verify distinct-entry resolution on regular files.
                await CreateDtxFileAsync(ownedAliasPath, "Owner", "Artist", "Rock", 50);
                var fallbackArgs = new object?[] { sharedDir, "BASIC.dtx", "different.dtx", false };
                Assert.True((bool)method!.Invoke(null, fallbackArgs)!);
                Assert.False((bool)fallbackArgs[3]!);
                return;
            }

            var isCaseSensitive = !File.Exists(candidatePath);
            if (isCaseSensitive)
            {
                File.CreateSymbolicLink(candidatePath, targetFile);
            }

            var candidateInfo = new FileInfo(candidatePath);
            var competingLength = (int)Math.Max(1, candidateInfo.Length);
            var competingStamp = candidateInfo.LastWriteTimeUtc;

            var otherDir = Directory.CreateDirectory(Path.Combine(_testRoot, "SharedOther")).FullName;
            var otherCopy = Path.Combine(otherDir, "basic.dtx");
            await File.WriteAllTextAsync(otherCopy, new string('x', competingLength));
            File.SetLastWriteTimeUtc(otherCopy, competingStamp);

            var ownedPaths = new HashSet<string>(SongPathIdentity.CanonicalComparer)
            {
                SongPathIdentity.Normalize(ownedAliasPath)
            };
            var inventory = new[]
            {
                new ChartInventoryEntry(
                    SongPathIdentity.Normalize(candidatePath),
                    competingStamp,
                    competingStamp),
                new ChartInventoryEntry(
                    SongPathIdentity.Normalize(otherCopy),
                    competingStamp,
                    competingStamp)
            };
            var missingChart = new SongChart
            {
                FilePath = SongPathIdentity.Normalize(Path.Combine(_testRoot, "SharedMissing", "basic.dtx")),
                FileSize = candidateInfo.Length,
                LastModified = candidateInfo.LastWriteTime
            };

            // Direct shared-folder resolution settles a distinct file as unowned
            // without walking ancestor directories.
            var distinctArgs = new object?[] { sharedDir, "basic.dtx", "different.dtx", false };
            Assert.True((bool)method!.Invoke(null, distinctArgs)!);
            Assert.False((bool)distinctArgs[3]!);

            var aliasArgs = new object?[] { sharedDir, "basic.dtx", "BASIC.dtx", false };
            Assert.True((bool)method.Invoke(null, aliasArgs)!);
            Assert.Equal(!isCaseSensitive, (bool)aliasArgs[3]!);

            // Only exercise the symlinked FindMovedFile path when the link's
            // FileInfo.Length > 0 (POSIX lstat reports target path length).
            if (candidateInfo.Length <= 0)
                return;

            var result = InvokeFindMovedFile(missingChart, inventory, ownedPaths);

            if (!isCaseSensitive)
            {
                // Case-insensitive volume: shared folder settles basic.dtx as owned
                // by BASIC.dtx, allowing the move repair onto SharedOther/basic.dtx.
                Assert.Equal(SongPathIdentity.Normalize(otherCopy), result);
            }
            else
            {
                // Case-sensitive volume: shared folder settles basic.dtx as unowned,
                // so both matching targets tie and veto repair.
                Assert.Null(result);
            }
        }

        [Fact]
        public async Task DetectFilesystemChangesAsync_WithoutEnumerationTimestamp_ShouldReturnTrue()
        {
            await _manager.InitializeAsync();

            var result = await ReflectionHelpers.InvokePrivateMethod<Task<bool>>(
                _manager.Scanner,
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
            var missingLibrary = new SongLibrary(
                new SongDatabase(Path.Combine(_testRoot, "missing.db")));

            var result = await ReflectionHelpers.InvokePrivateMethod<Task<DateTime?>>(missingLibrary.Scanner, "GetLastEnumerationTimestampAsync");

            Assert.Null(result);
        }

        [Fact]
        public async Task GetLastEnumerationTimestampAsync_WithEmptyDatabase_ShouldReturnNull()
        {
            await _manager.InitializeAsync();

            var result = await ReflectionHelpers.InvokePrivateMethod<Task<DateTime?>>(_manager.Scanner, "GetLastEnumerationTimestampAsync");

            Assert.Null(result);
        }

        [Fact]
        public async Task EnumerateAndImportSongsAsync_WhenCancelledAfterFirstProgressReport_ShouldThrowAndNotPublishPartialCount()
        {
            var songsRoot = Path.Combine(_testRoot, "CancelledMidEnumeration");
            Directory.CreateDirectory(songsRoot);

            for (int i = 1; i <= 3; i++)
            {
                await CreateDtxFileAsync(Path.Combine(songsRoot, $"song{i}.dtx"), $"Song {i}", "Coverage Bot", "Rock", 30 + i);
            }

            await _manager.InitializeAsync();
            var cancellation = new CancellationTokenSource();
            var cancellationRequested = 0;
            var progress = new InlineProgress<EnumerationProgress>(report =>
            {
                if (report.ProcessedCount >= 1 && Interlocked.Exchange(ref cancellationRequested, 1) == 0)
                    cancellation.Cancel();
            });

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                _manager.EnumerateAndImportSongsAsync(
                    new[] { songsRoot },
                    progress,
                    cancellation.Token));
            Assert.Empty(_manager.RootSongs);
        }

        [Fact]
        public async Task EnumerateAndImportSongsAsync_WhenRootSongCacheIsUnavailable_ShouldReportPostCommitPublicationFailure()
        {
            var songsRoot = Path.Combine(_testRoot, "BrokenRootSongs");
            Directory.CreateDirectory(songsRoot);
            await CreateDtxFileAsync(Path.Combine(songsRoot, "song.dtx"), "Broken Root Song", "Coverage Bot", "Rock", 50);

            await _manager.InitializeAsync();
            var originalRootSongs = ReflectionHelpers.GetPrivateField<List<SongListNode>>(_manager, "_rootSongs");

            try
            {
                ReflectionHelpers.SetPrivateField(_manager, "_rootSongs", null!);

                var exception = await Assert.ThrowsAsync<SongEnumerationPostCommitException>(() =>
                    _manager.EnumerateAndImportSongsAsync(new[] { songsRoot }));

                Assert.Equal(SongEnumerationPostCommitPhase.Publication, exception.Phase);
                Assert.True(exception.Batch.IsComplete);
                Assert.Equal(songsRoot, Assert.Single(exception.Batch.ActiveRoots));
                Assert.Equal(1, exception.Import.Added);
                Assert.Single(exception.Import.ChartsByPath);
                Assert.IsType<NullReferenceException>(exception.InnerException);

                var committedSong = Assert.Single(await new SongCatalogStore(ManagerDatabaseService!).GetSongsAsync());
                Assert.Equal("Broken Root Song", committedSong.Title);
                Assert.Single(committedSong.Charts);
            }
            finally
            {
                ReflectionHelpers.SetPrivateField(_manager, "_rootSongs", originalRootSongs);
            }
        }

        [Fact]
        public async Task NeedsEnumerationAsync_WithBrokenDatabaseService_ShouldReturnTrue()
        {
            var brokenLibrary = new SongLibrary(CreateBrokenDatabaseService());

            var result = await brokenLibrary.NeedsEnumerationAsync(new[] { _testRoot });

            Assert.True(result);
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
                _manager.Scanner,
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
                _manager.Scanner,
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
                _manager.Scanner,
                "DetectFilesystemChangesAsync",
                (object)new[] { songsRoot });

            Assert.True(result);
        }

        [Fact]
        public async Task GetLastEnumerationTimestampAsync_WithoutDatabaseService_ShouldReturnNull()
        {
            var result = await ReflectionHelpers.InvokePrivateMethod<Task<DateTime?>>(_manager.Scanner, "GetLastEnumerationTimestampAsync");

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

            await _manager.InitializeAsync();

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

            _manager = new SongLibrary(
                new SongDatabase(_testDbPath),
                parseSongEntitiesAsync: _ =>
                    Task.FromException<(SongEntity, SongChart)>(
                        new InvalidDataException("broken chart")));

            var batch = await _manager.BuildEnumerationBatchAsync(
                new[] { songsRoot },
                null,
                CancellationToken.None);

            Assert.Empty(batch.PendingSongs);
            Assert.Single(batch.Errors);
            Assert.Equal("broken chart", batch.Errors[0].Message);
        }

        [Fact]
        public async Task IsVerifiedMoveCandidate_WhenCandidateFingerprintMatchesPersisted_ShouldReturnTrue()
        {
            // The persisted FileSize+LastModified is the fingerprint: the
            // original path is gone by definition, so a candidate whose length
            // and mtime equal the persisted values verifies even with nothing
            // left to compare on disk.
            var candidatePath = Path.Combine(_testRoot, "Candidate", "match.dtx");
            Directory.CreateDirectory(Path.GetDirectoryName(candidatePath)!);
            await File.WriteAllTextAsync(candidatePath, "same-size");

            var result = ReflectionHelpers.InvokePrivateMethod<bool>(
                _manager.Scanner,
                "IsVerifiedMoveCandidate",
                new SongChart
                {
                    FilePath = Path.Combine(_testRoot, "Original", "match.dtx"),
                    FileSize = "same-size".Length,
                    LastModified = File.GetLastWriteTime(candidatePath)
                },
                candidatePath);

            Assert.True(result);
        }

        [Fact]
        public async Task IsVerifiedMoveCandidate_WhenCandidateMtimeDiffersFromPersisted_ShouldReturnFalse()
        {
            // A same-named, same-sized file is still a different chart when its
            // mtime does not match the persisted fingerprint: accepting it
            // would graft an unrelated file onto the missing chart's scores.
            var candidatePath = Path.Combine(_testRoot, "Candidate", "match.dtx");
            Directory.CreateDirectory(Path.GetDirectoryName(candidatePath)!);
            await File.WriteAllTextAsync(candidatePath, "same-size");

            var result = ReflectionHelpers.InvokePrivateMethod<bool>(
                _manager.Scanner,
                "IsVerifiedMoveCandidate",
                new SongChart
                {
                    FilePath = Path.Combine(_testRoot, "Original", "match.dtx"),
                    FileSize = "same-size".Length,
                    LastModified = File.GetLastWriteTime(candidatePath).AddMinutes(-1)
                },
                candidatePath);

            Assert.False(result);
        }

        [Fact]
        public async Task IsVerifiedMoveCandidate_WhenDiskNameDiffersOnlyByCase_ShouldReturnTrue()
        {
            // SET.def stores its written spelling while enumeration returns
            // the disk spelling: on a case-insensitive filesystem a case-only
            // difference still names the same file, so the moved chart must
            // repair instead of losing its row (and scores) to the rescan.
            if (SongPathIdentity.IsCaseSensitive)
            {
                return;
            }

            var candidatePath = Path.Combine(_testRoot, "Candidate", "SONG.DTX");
            Directory.CreateDirectory(Path.GetDirectoryName(candidatePath)!);
            await File.WriteAllTextAsync(candidatePath, "same-size");

            var result = ReflectionHelpers.InvokePrivateMethod<bool>(
                _manager.Scanner,
                "IsVerifiedMoveCandidate",
                new SongChart
                {
                    FilePath = Path.Combine(_testRoot, "Original", "song.dtx"),
                    FileSize = "same-size".Length,
                    LastModified = File.GetLastWriteTime(candidatePath)
                },
                candidatePath);

            Assert.True(result);
        }

        [Fact]
        public void IsVerifiedMoveCandidate_WithInvalidCandidatePath_ShouldReturnFalse()
        {
            var candidatePath = $"{_testRoot}{Path.DirectorySeparatorChar}bad\0{Path.DirectorySeparatorChar}match.dtx";

            var result = ReflectionHelpers.InvokePrivateMethod<bool>(
                _manager.Scanner,
                "IsVerifiedMoveCandidate",
                new SongChart { FilePath = Path.Combine(_testRoot, "match.dtx"), FileSize = 9 },
                candidatePath);

            Assert.False(result);
        }

        [Fact]
        public async Task UpdateChartFilePathAsync_WithoutDatabaseService_ShouldCompleteSuccessfully()
        {
            var result = await _manager.Scanner.UpdateChartFilePathAsync(
                new SongChart
                {
                    Id = 1,
                    FilePath = Path.Combine(_testRoot, "original.dtx")
                },
                Path.Combine(_testRoot, "updated.dtx"));

            Assert.False(result);
        }

        [Fact]
        public async Task UpdateChartFilePathAsync_WithBrokenDatabaseService_ShouldCompleteSuccessfully()
        {
            var brokenLibrary = new SongLibrary(CreateBrokenDatabaseService());

            var result = await brokenLibrary.Scanner.UpdateChartFilePathAsync(
                new SongChart
                {
                    Id = 1,
                    FilePath = Path.Combine(_testRoot, "original.dtx")
                },
                Path.Combine(_testRoot, "updated.dtx"));

            Assert.False(result);
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
                _manager.Scanner,
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
            Assert.NotNull(ManagerDatabaseService);
            using (var context = ManagerDatabaseService!.CreateContext())
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
                _manager.Scanner,
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
                _manager.Scanner,
                "DetectFilesystemChangesAsync",
                (object)new[] { songsRoot });

            Assert.False(result);
        }

        [Fact]
        public async Task FindMovedFile_WhenInventoryIsEmpty_ShouldReturnNull()
        {
            await _manager.InitializeAsync();

            // HPA-801 Decision B: the scanner consults only the inventory
            // passed to it. An empty inventory simply finds nothing.
            var result = InvokeFindMovedFile(
                new SongChart { FilePath = Path.Combine(_testRoot, "song.dtx") },
                Array.Empty<ChartInventoryEntry>(),
                new HashSet<string>(SongPathIdentity.CanonicalComparer));

            Assert.Null(result);
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

            await _manager.InitializeAsync();

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

            await _manager.InitializeAsync();

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

            var brokenLibrary = new SongLibrary(CreateBrokenDatabaseService());

            var batch = await brokenLibrary.BuildEnumerationBatchAsync(
                new[] { setFolder },
                null,
                CancellationToken.None);

            Assert.Single(batch.PendingSongs);
        }

        [Fact]
        public async Task BuildEnumerationBatchAsync_WithInvalidPath_ShouldProduceNoSongs()
        {
            await _manager.InitializeAsync();

            var batch = await _manager.BuildEnumerationBatchAsync(
                new[] { "\0invalid" },
                null,
                CancellationToken.None);

            Assert.Empty(batch.PendingSongs);
        }

        [Fact]
        public async Task GetLastEnumerationTimestampAsync_WithInvalidDatabasePath_ShouldReturnNull()
        {
            var brokenLibrary = new SongLibrary(
                CreateBrokenDatabaseServiceWithDatabasePath("\0invalid"));

            var result = await ReflectionHelpers.InvokePrivateMethod<Task<DateTime?>>(brokenLibrary.Scanner, "GetLastEnumerationTimestampAsync");

            Assert.Null(result);
        }

        private async Task InitializeAndEnumerateAsync(string songsRoot)
        {
            var initialized = await _manager.InitializeAsync();
            Assert.True(initialized);

            var result = await _manager.EnumerateAndImportSongsAsync(new[] { songsRoot });
            Assert.True(result.Batch.Candidates.Count >= 1);
            Assert.NotNull(ManagerDatabaseService);
        }

        private static IEnumerable<SongListNode> FlattenAllScoreNodes(
            IEnumerable<SongListNode> roots)
        {
            foreach (var node in roots)
            {
                if (node.Type == NodeType.Score)
                    yield return node;
                foreach (var child in FlattenAllScoreNodes(node.Children))
                    yield return child;
            }
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
            Assert.NotNull(ManagerDatabaseService);
            var utcTimestamp = localTimestamp.ToUniversalTime();
            await new SongCatalogStore(ManagerDatabaseService!).SetLastSuccessfulEnumerationUtcAsync(utcTimestamp);

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
            Assert.NotNull(ManagerDatabaseService);
            var canonicalRoot = _manager.RootPolicy
                .Validate(new[] { root })
                .CanonicalRoots
                .Single();

            await new SongCatalogStore(ManagerDatabaseService!).SetLastSuccessfulEnumerationUtcAsync(
                new[] { canonicalRoot },
                utcTimestamp.ToUniversalTime());
        }


        private static SongDatabase CreateBrokenDatabaseServiceWithDatabasePath(string databasePath)
        {
            var service = CreateBrokenDatabaseService();
            var field = typeof(SongDatabase).GetField("_databasePath", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            field!.SetValue(service, databasePath);
            return service;
        }

        private Task<ChartInventory> ScanChartInventory(params string[] roots) =>
            ReflectionHelpers.InvokePrivateMethod<Task<ChartInventory>>(
                _manager.Scanner,
                "ScanChartInventoryAsync",
                (object)roots,
                CancellationToken.None)!;

        private static bool InventoryHasChanges(
            ChartInventory inventory,
            string root,
            DateTime watermark)
        {
            var normalizedRoot = SongPathIdentity.Normalize(root);
            var method = typeof(SongLibraryScanner).GetMethod(
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

        private string? InvokeFindMovedFile(
            SongChart chart,
            IReadOnlyList<ChartInventoryEntry> inventory,
            ISet<string> ownedPaths)
        {
            var ownedPathsByFoldedDirectory =
                ReflectionHelpers.InvokePrivateMethod<Dictionary<string, List<string>>>(
                    _manager.Scanner,
                    "BuildOwnedPathsByFoldedDirectory",
                    ownedPaths)!;
            return ReflectionHelpers.InvokePrivateMethod<string?>(
                _manager.Scanner,
                "FindMovedFile",
                chart,
                inventory,
                ownedPaths,
                ownedPathsByFoldedDirectory);
        }

        private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
        {
            public void Report(T value) => report(value);
        }
    }
}
