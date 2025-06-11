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
    /// Tests song database management and enumeration
    /// </summary>
    public class SongManagerTests
    {
        #region Constructor Tests

        [Fact]
        public void Constructor_ShouldInitializeWithDefaults()
        {
            // Arrange & Act
            var manager = new SongManager();

            // Assert
            Assert.NotNull(manager.SongsDatabase);
            Assert.Empty(manager.SongsDatabase);
            Assert.NotNull(manager.RootSongs);
            Assert.Empty(manager.RootSongs);
            Assert.Equal(0, manager.DatabaseScoreCount);
            Assert.Equal(0, manager.DiscoveredScoreCount);
            Assert.Equal(0, manager.EnumeratedFileCount);
            Assert.False(manager.IsEnumerating);
        }

        #endregion

        #region Database Management Tests

        [Fact]
        public async Task LoadSongsDatabaseAsync_WithNonExistentFile_ShouldReturnFalse()
        {
            // Arrange
            var manager = new SongManager();
            var nonExistentPath = @"C:\NonExistent\songs.db";

            // Act
            var result = await manager.LoadSongsDatabaseAsync(nonExistentPath);

            // Assert
            Assert.False(result);
            Assert.Equal(0, manager.DatabaseScoreCount);
        }

        [Fact]
        public async Task SaveSongsDatabaseAsync_WithValidPath_ShouldReturnTrue()
        {
            // Arrange
            var manager = new SongManager();
            var tempFile = Path.GetTempFileName();

            try
            {
                // Act
                var result = await manager.SaveSongsDatabaseAsync(tempFile);

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
            var manager = new SongManager();
            var tempFile = Path.GetTempFileName();

            try
            {
                // Add some test data
                await manager.EnumerateSongsAsync(new[] { "NonExistentPath" }); // This will create empty structure

                // Act - Save
                var saveResult = await manager.SaveSongsDatabaseAsync(tempFile);
                
                // Clear and reload
                manager.Clear();
                var loadResult = await manager.LoadSongsDatabaseAsync(tempFile);

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
            var manager = new SongManager();
            var invalidPath = @"C:\NonExistent\Directory\songs.db";

            // Act
            var result = await manager.SaveSongsDatabaseAsync(invalidPath);

            // Assert
            Assert.False(result);
        }

        #endregion

        #region Enumeration Tests

        [Fact]
        public async Task EnumerateSongsAsync_WithNonExistentPaths_ShouldReturnZero()
        {
            // Arrange
            var manager = new SongManager();
            var paths = new[] { "NonExistent1", "NonExistent2" };

            // Act
            var result = await manager.EnumerateSongsAsync(paths);

            // Assert
            Assert.Equal(0, result);
            Assert.Equal(0, manager.DiscoveredScoreCount);
            Assert.False(manager.IsEnumerating);
        }

        [Fact]
        public async Task EnumerateSongsAsync_WithEmptyArray_ShouldReturnZero()
        {
            // Arrange
            var manager = new SongManager();
            var paths = new string[0];

            // Act
            var result = await manager.EnumerateSongsAsync(paths);

            // Assert
            Assert.Equal(0, result);
            Assert.Equal(0, manager.DiscoveredScoreCount);
        }

        [Fact]
        public async Task EnumerateSongsAsync_WithNullPaths_ShouldHandleGracefully()
        {
            // Arrange
            var manager = new SongManager();
            var paths = new string[] { null, "", "NonExistent" };

            // Act
            var result = await manager.EnumerateSongsAsync(paths);

            // Assert
            Assert.Equal(0, result);
            Assert.Equal(0, manager.DiscoveredScoreCount);
        }

        [Fact]
        public async Task EnumerateSongsAsync_WithRealDirectory_ShouldEnumerateFiles()
        {
            // Arrange
            var manager = new SongManager();
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
                var result = await manager.EnumerateSongsAsync(new[] { tempDir });

                // Assert
                Assert.Equal(1, result);
                Assert.Equal(1, manager.DiscoveredScoreCount);
                Assert.True(manager.DatabaseScoreCount > 0);
                Assert.True(manager.RootSongs.Count > 0);
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
            var manager = new SongManager();
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
                var result = await manager.EnumerateSongsAsync(new[] { tempDir });

                // Assert
                Assert.Equal(1, result);
                Assert.True(manager.RootSongs.Any(n => n.Type == NodeType.Box));

                var boxNode = manager.RootSongs.First(n => n.Type == NodeType.Box);
                Assert.Equal("DTXFiles.TestFolder", boxNode.Title);
                Assert.True(boxNode.Children.Any(c => c.Type == NodeType.Score));
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
            // Arrange
            var manager = new SongManager();

            // Act & Assert (should not throw)
            manager.CancelEnumeration();
        }

        #endregion

        #region Helper Method Tests

        [Fact]
        public async Task FindSongByPath_WithExistingSong_ShouldReturnScore()
        {
            // Arrange
            var manager = new SongManager();
            var tempDir = Path.Combine(Path.GetTempPath(), "SongManagerTest");
            var dtxFile = Path.Combine(tempDir, "test.dtx");

            try
            {
                Directory.CreateDirectory(tempDir);
                await File.WriteAllTextAsync(dtxFile, @"#TITLE: Test Song
#DLEVEL: 50
");

                await manager.EnumerateSongsAsync(new[] { tempDir });

                // Act
                var result = manager.FindSongByPath(dtxFile);

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
            // Arrange
            var manager = new SongManager();

            // Act
            var result = manager.FindSongByPath(@"C:\NonExistent\test.dtx");

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public async Task GetSongsByGenre_WithMatchingGenre_ShouldReturnSongs()
        {
            // Arrange
            var manager = new SongManager();
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

                await manager.EnumerateSongsAsync(new[] { tempDir });

                // Act
                var rockSongs = manager.GetSongsByGenre("Rock").ToList();
                var popSongs = manager.GetSongsByGenre("Pop").ToList();

                // Assert
                Assert.Equal(2, rockSongs.Count);
                Assert.Equal(1, popSongs.Count);
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
            var manager = new SongManager();
            // Simulate some data
            typeof(SongManager).GetField("DiscoveredScoreCount")?.SetValue(manager, 5);
            typeof(SongManager).GetField("EnumeratedFileCount")?.SetValue(manager, 10);

            // Act
            manager.Clear();

            // Assert
            Assert.Equal(0, manager.DatabaseScoreCount);
            Assert.Equal(0, manager.RootSongs.Count);
            Assert.Equal(0, manager.DiscoveredScoreCount);
            Assert.Equal(0, manager.EnumeratedFileCount);
        }

        #endregion

        #region Event Tests

        [Fact]
        public async Task EnumerateSongsAsync_ShouldFireEnumerationCompleted()
        {
            // Arrange
            var manager = new SongManager();
            var eventFired = false;
            manager.EnumerationCompleted += (sender, args) => eventFired = true;

            // Act
            await manager.EnumerateSongsAsync(new[] { "NonExistentPath" });

            // Assert
            Assert.True(eventFired);
        }

        [Fact]
        public async Task EnumerateSongsAsync_WithRealSong_ShouldFireSongDiscovered()
        {
            // Arrange
            var manager = new SongManager();
            var tempDir = Path.Combine(Path.GetTempPath(), "SongManagerTest");
            var dtxFile = Path.Combine(tempDir, "test.dtx");
            SongListNode discoveredSong = null;

            manager.SongDiscovered += (sender, args) => discoveredSong = args.Song;

            try
            {
                Directory.CreateDirectory(tempDir);
                await File.WriteAllTextAsync(dtxFile, @"#TITLE: Event Test Song
#DLEVEL: 50
");

                // Act
                await manager.EnumerateSongsAsync(new[] { tempDir });

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

        #region Phase 2: Set.def Parsing Tests

        [Fact]
        public async Task ParseSetDefinition_ValidSetDef_ShouldCreateMultiDifficultySong()
        {
            // Arrange
            var tempDir = Path.Combine(Path.GetTempPath(), "SongManagerTest_SetDef");
            var setDefDir = Path.Combine(tempDir, "TestSong");

            try
            {
                Directory.CreateDirectory(setDefDir);

                // Create test DTX files
                var easyDtx = Path.Combine(setDefDir, "easy.dtx");
                var normalDtx = Path.Combine(setDefDir, "normal.dtx");
                var hardDtx = Path.Combine(setDefDir, "hard.dtx");

                await File.WriteAllTextAsync(easyDtx, "#TITLE: Test Song\n#ARTIST: Test Artist\n#DLEVEL: 10\n");
                await File.WriteAllTextAsync(normalDtx, "#TITLE: Test Song\n#ARTIST: Test Artist\n#DLEVEL: 30\n");
                await File.WriteAllTextAsync(hardDtx, "#TITLE: Test Song\n#ARTIST: Test Artist\n#DLEVEL: 50\n");

                // Create set.def file using proper DTXMania format
                var setDefPath = Path.Combine(setDefDir, "set.def");
                var setDefContent = @"#TITLE Test Song Multi-Difficulty
#L1LABEL Easy
#L1FILE easy.dtx
#L2LABEL Normal
#L2FILE normal.dtx
#L3LABEL Hard
#L3FILE hard.dtx";
                await File.WriteAllTextAsync(setDefPath, setDefContent);

                var manager = new SongManager();

                // Act
                var result = await manager.EnumerateSongsAsync(new[] { tempDir });

                // Assert
                Assert.True(result > 0);
                var rootSongs = manager.RootSongs;
                Assert.Single(rootSongs); // Should have one song with multiple difficulties (no box wrapper for regular folders)

                var songNode = rootSongs.First();
                Assert.Equal(NodeType.Score, songNode.Type);
                Assert.Equal("Test Song Multi-Difficulty", songNode.DisplayTitle);
                Assert.Equal(3, songNode.AvailableDifficulties);

                // Check that all difficulties have different levels
                var difficulties = songNode.Scores.Where(s => s != null).ToList();
                Assert.Equal(3, difficulties.Count);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public async Task ParseSetDefinition_EmptySetDef_ShouldNotCreateSongs()
        {
            // Arrange
            var tempDir = Path.Combine(Path.GetTempPath(), "SongManagerTest_EmptySet");
            var setDefDir = Path.Combine(tempDir, "EmptySet");

            try
            {
                Directory.CreateDirectory(setDefDir);

                var setDefPath = Path.Combine(setDefDir, "set.def");
                await File.WriteAllTextAsync(setDefPath, "// Empty set.def file\n");

                var manager = new SongManager();

                // Act
                var result = await manager.EnumerateSongsAsync(new[] { tempDir });

                // Assert
                Assert.Equal(0, result);
                Assert.Empty(manager.RootSongs);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public async Task ParseSetDefinition_MissingFiles_ShouldSkipMissingDifficulties()
        {
            // Arrange
            var tempDir = Path.Combine(Path.GetTempPath(), "SongManagerTest_PartialSet");
            var setDefDir = Path.Combine(tempDir, "PartialSet");

            try
            {
                Directory.CreateDirectory(setDefDir);

                // Create only one DTX file
                var easyDtx = Path.Combine(setDefDir, "easy.dtx");
                await File.WriteAllTextAsync(easyDtx, "#TITLE: Partial Song\n#ARTIST: Test Artist\n#DLEVEL: 15\n");

                // Create set.def referencing missing files using proper DTXMania format
                var setDefPath = Path.Combine(setDefDir, "set.def");
                var setDefContent = @"#TITLE Partial Song
#L1LABEL Easy
#L1FILE easy.dtx
#L2LABEL Normal
#L2FILE missing_normal.dtx
#L3LABEL Hard
#L3FILE missing_hard.dtx";
                await File.WriteAllTextAsync(setDefPath, setDefContent);

                var manager = new SongManager();

                // Act
                var result = await manager.EnumerateSongsAsync(new[] { tempDir });

                // Assert
                Assert.True(result > 0);
                var songNode = manager.RootSongs.First(); // No box wrapper for regular folders
                Assert.Equal(1, songNode.AvailableDifficulties); // Only one difficulty should be available
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }

        #endregion

        #region Phase 2: Box.def Parsing Tests

        [Fact]
        public async Task ParseBoxDefinition_ValidBoxDef_ShouldApplyFolderMetadata()
        {
            // Arrange
            var tempDir = Path.Combine(Path.GetTempPath(), "SongManagerTest_BoxDef");
            var boxDir = Path.Combine(tempDir, "CustomBox");

            try
            {
                Directory.CreateDirectory(boxDir);

                // Create box.def file
                var boxDefPath = Path.Combine(boxDir, "box.def");
                var boxDefContent = @"#TITLE: Custom Box Title
#GENRE: Test Genre
#SKINPATH: CustomSkin
#BGCOLOR: #FF0000
#TEXTCOLOR: White";
                await File.WriteAllTextAsync(boxDefPath, boxDefContent);

                // Create a test song in the box
                var songDir = Path.Combine(boxDir, "TestSong");
                Directory.CreateDirectory(songDir);
                var dtxPath = Path.Combine(songDir, "test.dtx");
                await File.WriteAllTextAsync(dtxPath, "#TITLE: Test Song\n#ARTIST: Test Artist\n");

                var manager = new SongManager();

                // Act
                var result = await manager.EnumerateSongsAsync(new[] { tempDir });

                // Assert
                Assert.True(result > 0);
                var boxNode = manager.RootSongs.First();
                Assert.Equal("Custom Box Title", boxNode.DisplayTitle);
                Assert.Equal("Test Genre", boxNode.Genre);
                Assert.Equal("CustomSkin", boxNode.SkinPath);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public async Task ParseBoxDefinition_NoBoxDef_ShouldTreatAsIndividualSong()
        {
            // Arrange
            var tempDir = Path.Combine(Path.GetTempPath(), "SongManagerTest_NoBoxDef");
            var songDir = Path.Combine(tempDir, "RegularFolder");

            try
            {
                Directory.CreateDirectory(songDir);

                // Create a test song without box.def (regular song folder)
                var dtxPath = Path.Combine(songDir, "test.dtx");
                await File.WriteAllTextAsync(dtxPath, "#TITLE: Test Song\n#ARTIST: Test Artist\n");

                var manager = new SongManager();

                // Act
                var result = await manager.EnumerateSongsAsync(new[] { tempDir });

                // Assert
                Assert.True(result > 0);
                var songNode = manager.RootSongs.First();
                Assert.Equal(NodeType.Score, songNode.Type); // Should be a song, not a box
                Assert.Equal("Test Song", songNode.DisplayTitle); // Song should use its title
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public async Task EnumerateSongsAsync_WithRegularSongFolder_ShouldTreatAsIndividualSong()
        {
            // Arrange
            var manager = new SongManager();
            var tempDir = Path.Combine(Path.GetTempPath(), "SongManagerTest_RegularFolder");
            var songDir = Path.Combine(tempDir, "MySong"); // Regular folder (no DTXFiles. prefix)
            var dtxFile = Path.Combine(songDir, "song.dtx");

            try
            {
                Directory.CreateDirectory(songDir);
                await File.WriteAllTextAsync(dtxFile, @"#TITLE: My Song
#ARTIST: My Artist
#DLEVEL: 45
");

                // Act
                var result = await manager.EnumerateSongsAsync(new[] { tempDir });

                // Assert
                Assert.Equal(1, result);
                Assert.Single(manager.RootSongs);

                var songNode = manager.RootSongs.First();
                Assert.Equal(NodeType.Score, songNode.Type); // Should be a song, not a box
                Assert.Equal("My Song", songNode.DisplayTitle);
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
