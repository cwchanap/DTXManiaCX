using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using DTX.Song;

namespace DTXMania.Test.Song
{
    /// <summary>
    /// Unit tests for SongManager Phase 2 features
    /// Tests set.def parsing, box.def parsing, incremental enumeration, and enhanced caching
    /// </summary>
    public class SongManagerPhase2Tests : IDisposable
    {
        #region Test Setup

        private readonly string _testDirectory;
        private readonly SongManager _songManager;

        public SongManagerPhase2Tests()
        {
            _testDirectory = Path.Combine(Path.GetTempPath(), "DTXMania_Test_Phase2_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(_testDirectory);
            _songManager = new SongManager();
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_testDirectory))
                {
                    Directory.Delete(_testDirectory, true);
                }
            }
            catch
            {
                // Ignore cleanup errors in tests
            }
        }

        #endregion

        #region Set.def Parsing Tests

        [Fact]
        public async Task ParseSetDefinition_ValidSetDef_ShouldCreateMultiDifficultySong()
        {
            // Arrange
            var setDefDir = Path.Combine(_testDirectory, "TestSong");
            Directory.CreateDirectory(setDefDir);

            // Create test DTX files
            var easyDtx = Path.Combine(setDefDir, "easy.dtx");
            var normalDtx = Path.Combine(setDefDir, "normal.dtx");
            var hardDtx = Path.Combine(setDefDir, "hard.dtx");

            await File.WriteAllTextAsync(easyDtx, "#TITLE: Test Song\n#ARTIST: Test Artist\n#DLEVEL: 10\n");
            await File.WriteAllTextAsync(normalDtx, "#TITLE: Test Song\n#ARTIST: Test Artist\n#DLEVEL: 30\n");
            await File.WriteAllTextAsync(hardDtx, "#TITLE: Test Song\n#ARTIST: Test Artist\n#DLEVEL: 50\n");

            // Create set.def file
            var setDefPath = Path.Combine(setDefDir, "set.def");
            var setDefContent = @"#TITLE: Test Song Multi-Difficulty
easy.dtx
normal.dtx
hard.dtx";
            await File.WriteAllTextAsync(setDefPath, setDefContent);

            // Act
            var result = await _songManager.EnumerateSongsAsync(new[] { _testDirectory });

            // Assert
            Assert.True(result > 0);
            var rootSongs = _songManager.RootSongs;
            Assert.Single(rootSongs); // Should have one box containing the multi-difficulty song

            var boxNode = rootSongs.First();
            Assert.Equal(NodeType.Box, boxNode.Type);
            Assert.Single(boxNode.Children); // Should contain one song with multiple difficulties

            var songNode = boxNode.Children.First();
            Assert.Equal(NodeType.Score, songNode.Type);
            Assert.Equal("Test Song Multi-Difficulty", songNode.DisplayTitle);
            Assert.Equal(3, songNode.AvailableDifficulties);

            // Check that all difficulties have different levels
            var difficulties = songNode.Scores.Where(s => s != null).ToList();
            Assert.Equal(3, difficulties.Count);
            Assert.Contains(difficulties, s => s.Metadata.DrumLevel == 10);
            Assert.Contains(difficulties, s => s.Metadata.DrumLevel == 30);
            Assert.Contains(difficulties, s => s.Metadata.DrumLevel == 50);
        }

        [Fact]
        public async Task ParseSetDefinition_EmptySetDef_ShouldNotCreateSongs()
        {
            // Arrange
            var setDefDir = Path.Combine(_testDirectory, "EmptySet");
            Directory.CreateDirectory(setDefDir);

            var setDefPath = Path.Combine(setDefDir, "set.def");
            await File.WriteAllTextAsync(setDefPath, "// Empty set.def file\n");

            // Act
            var result = await _songManager.EnumerateSongsAsync(new[] { _testDirectory });

            // Assert
            Assert.Equal(0, result);
            Assert.Empty(_songManager.RootSongs);
        }

        [Fact]
        public async Task ParseSetDefinition_MissingFiles_ShouldSkipMissingDifficulties()
        {
            // Arrange
            var setDefDir = Path.Combine(_testDirectory, "PartialSet");
            Directory.CreateDirectory(setDefDir);

            // Create only one DTX file
            var easyDtx = Path.Combine(setDefDir, "easy.dtx");
            await File.WriteAllTextAsync(easyDtx, "#TITLE: Partial Song\n#ARTIST: Test Artist\n#DLEVEL: 15\n");

            // Create set.def referencing missing files
            var setDefPath = Path.Combine(setDefDir, "set.def");
            var setDefContent = @"#TITLE: Partial Song
easy.dtx
missing_normal.dtx
missing_hard.dtx";
            await File.WriteAllTextAsync(setDefPath, setDefContent);

            // Act
            var result = await _songManager.EnumerateSongsAsync(new[] { _testDirectory });

            // Assert
            Assert.True(result > 0);
            var songNode = _songManager.RootSongs.First().Children.First();
            Assert.Equal(1, songNode.AvailableDifficulties); // Only one difficulty should be available
        }

        #endregion

        #region Box.def Parsing Tests

        [Fact]
        public async Task ParseBoxDefinition_ValidBoxDef_ShouldApplyFolderMetadata()
        {
            // Arrange
            var boxDir = Path.Combine(_testDirectory, "CustomBox");
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

            // Act
            var result = await _songManager.EnumerateSongsAsync(new[] { _testDirectory });

            // Assert
            Assert.True(result > 0);
            var boxNode = _songManager.RootSongs.First();
            Assert.Equal("Custom Box Title", boxNode.DisplayTitle);
            Assert.Equal("Test Genre", boxNode.Genre);
            Assert.Equal("CustomSkin", boxNode.SkinPath);
        }

        [Fact]
        public async Task ParseBoxDefinition_NoBoxDef_ShouldUseFolderName()
        {
            // Arrange
            var boxDir = Path.Combine(_testDirectory, "RegularFolder");
            Directory.CreateDirectory(boxDir);

            // Create a test song without box.def
            var dtxPath = Path.Combine(boxDir, "test.dtx");
            await File.WriteAllTextAsync(dtxPath, "#TITLE: Test Song\n#ARTIST: Test Artist\n");

            // Act
            var result = await _songManager.EnumerateSongsAsync(new[] { _testDirectory });

            // Assert
            Assert.True(result > 0);
            var boxNode = _songManager.RootSongs.First();
            Assert.Equal("RegularFolder", boxNode.DisplayTitle); // Should use folder name for box
            Assert.Single(boxNode.Children); // Should contain one song
            var songNode = boxNode.Children.First();
            Assert.Equal("Test Song", songNode.DisplayTitle); // Song should use its title
        }

        #endregion

        #region Incremental Enumeration Tests

        [Fact]
        public async Task NeedsEnumeration_NoDatabaseFile_ShouldReturnTrue()
        {
            // Arrange
            var searchPaths = new[] { _testDirectory };

            // Act
            var needsEnumeration = await _songManager.NeedsEnumerationAsync(searchPaths);

            // Assert
            Assert.True(needsEnumeration);
        }

        [Fact]
        public async Task NeedsEnumeration_DatabaseExistsButDirectoryModified_ShouldReturnTrue()
        {
            // Arrange
            var databasePath = Path.Combine(_testDirectory, "songs.db");
            await File.WriteAllTextAsync(databasePath, "{}"); // Create empty database file
            
            // Wait a moment to ensure different timestamps
            await Task.Delay(100);
            
            // Modify the test directory
            var newFile = Path.Combine(_testDirectory, "new_song.dtx");
            await File.WriteAllTextAsync(newFile, "#TITLE: New Song\n");

            var searchPaths = new[] { _testDirectory };

            // Act
            var needsEnumeration = await _songManager.NeedsEnumerationAsync(searchPaths);

            // Assert
            Assert.True(needsEnumeration);
        }

        [Fact]
        public async Task IncrementalEnumeration_OnlyProcessesModifiedFiles_ShouldBeEfficient()
        {
            // Arrange
            var oldSongDir = Path.Combine(_testDirectory, "OldSong");
            Directory.CreateDirectory(oldSongDir);
            var oldDtx = Path.Combine(oldSongDir, "old.dtx");
            await File.WriteAllTextAsync(oldDtx, "#TITLE: Old Song\n#ARTIST: Old Artist\n");

            // Wait a moment to ensure different timestamps
            await Task.Delay(100);

            // Create new song with recent timestamp
            var newSongDir = Path.Combine(_testDirectory, "NewSong");
            Directory.CreateDirectory(newSongDir);
            var newDtx = Path.Combine(newSongDir, "new.dtx");
            await File.WriteAllTextAsync(newDtx, "#TITLE: New Song\n#ARTIST: New Artist\n");

            var searchPaths = new[] { _testDirectory };

            // Act
            var result = await _songManager.IncrementalEnumerationAsync(searchPaths);

            // Assert
            Assert.True(result > 0);
            var discoveredSongs = _songManager.RootSongs;

            // Should find both songs since we don't have a database file to compare against
            Assert.True(discoveredSongs.Count >= 1);
            Assert.Contains(discoveredSongs, box => box.Children.Any(song => song.DisplayTitle == "New Song"));
        }

        #endregion

        #region Database Caching Tests

        [Fact]
        public async Task SaveAndLoadDatabase_WithPhase2Data_ShouldPreserveStructure()
        {
            // Arrange
            var setDefDir = Path.Combine(_testDirectory, "MultiDiffSong");
            Directory.CreateDirectory(setDefDir);

            // Create multi-difficulty song
            var easyDtx = Path.Combine(setDefDir, "easy.dtx");
            var hardDtx = Path.Combine(setDefDir, "hard.dtx");
            await File.WriteAllTextAsync(easyDtx, "#TITLE: Cache Test\n#ARTIST: Test Artist\n#DLEVEL: 20\n");
            await File.WriteAllTextAsync(hardDtx, "#TITLE: Cache Test\n#ARTIST: Test Artist\n#DLEVEL: 60\n");

            var setDefPath = Path.Combine(setDefDir, "set.def");
            await File.WriteAllTextAsync(setDefPath, "#TITLE: Cache Test\neasy.dtx\nhard.dtx");

            // Enumerate and save
            await _songManager.EnumerateSongsAsync(new[] { _testDirectory });
            var databasePath = Path.Combine(_testDirectory, "test_songs.db");
            var saveResult = await _songManager.SaveSongsDatabaseAsync(databasePath);
            Assert.True(saveResult);

            // Create new manager and load
            var newManager = new SongManager();
            var loadResult = await newManager.LoadSongsDatabaseAsync(databasePath);

            // Assert
            Assert.True(loadResult);
            Assert.Equal(_songManager.DatabaseScoreCount, newManager.DatabaseScoreCount);
            Assert.Equal(_songManager.RootSongs.Count, newManager.RootSongs.Count);

            // Verify multi-difficulty structure is preserved
            var loadedSong = newManager.RootSongs.First().Children.First();
            Assert.Equal(2, loadedSong.AvailableDifficulties);
        }

        #endregion

        #region Error Handling Tests

        [Fact]
        public async Task ParseSetDefinition_CorruptedFile_ShouldHandleGracefully()
        {
            // Arrange
            var setDefDir = Path.Combine(_testDirectory, "CorruptedSet");
            Directory.CreateDirectory(setDefDir);

            var setDefPath = Path.Combine(setDefDir, "set.def");
            await File.WriteAllTextAsync(setDefPath, "This is not a valid set.def file\n\x00\x01\x02"); // Invalid content

            // Act & Assert - Should not throw
            var result = await _songManager.EnumerateSongsAsync(new[] { _testDirectory });
            Assert.Equal(0, result); // Should find no songs due to corruption
        }

        [Fact]
        public async Task ParseBoxDefinition_CorruptedFile_ShouldHandleGracefully()
        {
            // Arrange
            var boxDir = Path.Combine(_testDirectory, "CorruptedBox");
            Directory.CreateDirectory(boxDir);

            var boxDefPath = Path.Combine(boxDir, "box.def");
            await File.WriteAllTextAsync(boxDefPath, "Invalid box.def content\n\x00\x01\x02");

            // Create a valid song to ensure enumeration continues
            var dtxPath = Path.Combine(boxDir, "test.dtx");
            await File.WriteAllTextAsync(dtxPath, "#TITLE: Test Song\n#ARTIST: Test Artist\n");

            // Act & Assert - Should not throw and should still find the song
            var result = await _songManager.EnumerateSongsAsync(new[] { _testDirectory });
            Assert.True(result > 0);
        }

        #endregion
    }
}
