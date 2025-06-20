using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using DTX.Song;

namespace DTXMania.Test.Song
{
    /// <summary>
    /// Unit tests for SET.def file parsing functionality    /// <summary>
    /// Unit tests for SET.def file parsing functionality
    /// Tests the fixed SET.def parser that handles DTXMania format correctly
    /// </summary>
    public class SetDefParserTests : IDisposable
    {
        #region Test Setup

        private readonly SongManager _songManager;
        private readonly List<string> _tempFiles;

        public SetDefParserTests()
        {
            _songManager = SongManager.Instance;
            _tempFiles = new List<string>();
        }

        public void Dispose()
        {
            // Clean up temporary files
            foreach (var file in _tempFiles)
            {
                try
                {
                    if (File.Exists(file))
                        File.Delete(file);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }

            // SongManager doesn't implement IDisposable
        }

        private string CreateTempFile(string content, Encoding encoding = null)
        {
            var tempFile = Path.GetTempFileName();
            _tempFiles.Add(tempFile);

            encoding ??= Encoding.UTF8;
            File.WriteAllText(tempFile, content, encoding);
            return tempFile;
        }

        private string CreateTempDirectory()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);
            return tempDir;
        }

        #endregion

        #region Valid SET.def Parsing Tests

        [Fact]
        public async Task ParseSetDefinition_WithValidFormat_ParsesCorrectly()
        {
            // Arrange
            var setDefContent = @"#TITLE   Test Song Title
#L1LABEL Basic
#L1FILE  bas.dtx
#L2LABEL Advanced
#L2FILE  adv.dtx
#L3LABEL Extreme
#L3FILE  ext.dtx";

            var tempDir = CreateTempDirectory();
            var setDefPath = Path.Combine(tempDir, "set.def");
            File.WriteAllText(setDefPath, setDefContent, Encoding.UTF8);

            // Create dummy DTX files
            var basPath = Path.Combine(tempDir, "bas.dtx");
            var advPath = Path.Combine(tempDir, "adv.dtx");
            var extPath = Path.Combine(tempDir, "ext.dtx");
            
            File.WriteAllText(basPath, "#TITLE: Test Song\n#ARTIST: Test Artist\n#BPM: 120", Encoding.UTF8);
            File.WriteAllText(advPath, "#TITLE: Test Song\n#ARTIST: Test Artist\n#BPM: 120", Encoding.UTF8);
            File.WriteAllText(extPath, "#TITLE: Test Song\n#ARTIST: Test Artist\n#BPM: 120", Encoding.UTF8);

            try
            {
                // Act
                await _songManager.EnumerateSongsAsync(new[] { tempDir });

                // Assert
                Assert.True(_songManager.DatabaseScoreCount > 0);
                var rootSongs = _songManager.RootSongs;
                Assert.NotEmpty(rootSongs);

                var song = rootSongs.FirstOrDefault(s => s.Type == NodeType.Score);
                Assert.NotNull(song);
                Assert.Equal("Test Song Title", song.DisplayTitle);
                Assert.True(song.AvailableDifficulties >= 3);
            }
            finally
            {
                // Cleanup
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public async Task ParseSetDefinition_WithUTF8Encoding_ParsesCorrectly()
        {
            // Arrange
            var setDefContent = @"#TITLE   黎明novels (feat. hana)
#L1LABEL BASIC
#L1FILE  bas.dtx";

            var tempDir = CreateTempDirectory();
            var setDefPath = Path.Combine(tempDir, "set.def");
            File.WriteAllText(setDefPath, setDefContent, Encoding.UTF8);

            // Create dummy DTX file
            var basPath = Path.Combine(tempDir, "bas.dtx");
            File.WriteAllText(basPath, "#TITLE: Test Song\n#ARTIST: Test Artist", Encoding.UTF8);

            try
            {
                // Act
                await _songManager.EnumerateSongsAsync(new[] { tempDir });

                // Assert
                var rootSongs = _songManager.RootSongs;
                var song = rootSongs.FirstOrDefault(s => s.Type == NodeType.Score);
                Assert.NotNull(song);
                Assert.Contains("黎明novels", song.DisplayTitle);
            }
            finally
            {
                // Cleanup
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public async Task ParseSetDefinition_WithShiftJISEncoding_HandlesGracefully()
        {
            // Arrange
            var setDefContent = @"#TITLE   Test Song Title
#L1LABEL BASIC
#L1FILE  bas.dtx";

            var tempDir = CreateTempDirectory();
            var setDefPath = Path.Combine(tempDir, "set.def");

            try
            {
                // Try to use Shift_JIS encoding if available
                var encoding = Encoding.GetEncoding("Shift_JIS");
                File.WriteAllText(setDefPath, setDefContent, encoding);
            }
            catch (ArgumentException)
            {
                // Shift_JIS not available, use UTF8 instead
                File.WriteAllText(setDefPath, setDefContent, Encoding.UTF8);
            }

            // Create dummy DTX file
            var basPath = Path.Combine(tempDir, "bas.dtx");
            File.WriteAllText(basPath, "#TITLE: Test Song\n#ARTIST: Test Artist", Encoding.UTF8);

            try
            {
                // Act
                await _songManager.EnumerateSongsAsync(new[] { tempDir });

                // Assert - Should handle gracefully regardless of encoding
                var rootSongs = _songManager.RootSongs;
                var song = rootSongs.FirstOrDefault(s => s.Type == NodeType.Score);
                Assert.NotNull(song);
                Assert.Contains("Test Song", song.DisplayTitle);
            }
            finally
            {
                // Cleanup
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public async Task ParseSetDefinition_WithMultipleDifficulties_CreatesCorrectScores()
        {
            // Arrange
            var setDefContent = @"#TITLE   Multi Difficulty Song
#L1LABEL BASIC
#L1FILE  bas.dtx
#L2LABEL ADVANCED
#L2FILE  adv.dtx
#L3LABEL EXTREME
#L3FILE  ext.dtx
#L4LABEL MASTER
#L4FILE  mas.dtx";

            var tempDir = CreateTempDirectory();
            var setDefPath = Path.Combine(tempDir, "set.def");
            File.WriteAllText(setDefPath, setDefContent, Encoding.UTF8);

            // Create DTX files with different difficulty levels
            var difficulties = new[] { "bas", "adv", "ext", "mas" };
            var levels = new[] { 1, 3, 5, 7 };

            for (int i = 0; i < difficulties.Length; i++)
            {
                var dtxPath = Path.Combine(tempDir, $"{difficulties[i]}.dtx");
                var dtxContent = $"#TITLE: Multi Difficulty Song\n#DLEVEL: {levels[i]}";
                File.WriteAllText(dtxPath, dtxContent, Encoding.UTF8);
            }

            try
            {
                // Act
                await _songManager.EnumerateSongsAsync(new[] { tempDir });

                // Assert
                var rootSongs = _songManager.RootSongs;
                var song = rootSongs.FirstOrDefault(s => s.Type == NodeType.Score);
                Assert.NotNull(song);
                Assert.Equal(4, song.AvailableDifficulties);

                // Check difficulty labels
                for (int i = 0; i < 4; i++)
                {
                    var score = song.Scores[i];
                    Assert.NotNull(score);
                    Assert.Contains(difficulties[i].ToUpper(), score.DifficultyLabel.ToUpper());
                }
            }
            finally
            {
                // Cleanup
                Directory.Delete(tempDir, true);
            }
        }

        #endregion

        #region Invalid SET.def Handling Tests

        [Fact]
        public async Task ParseSetDefinition_WithMissingTitle_HandlesGracefully()
        {
            // Arrange
            var setDefContent = @"#L1LABEL Basic
#L1FILE  bas.dtx";

            var tempDir = CreateTempDirectory();
            var setDefPath = Path.Combine(tempDir, "set.def");
            File.WriteAllText(setDefPath, setDefContent, Encoding.UTF8);

            try
            {
                // Act & Assert - Should not throw
                var exception = await Record.ExceptionAsync(async () =>
                    await _songManager.EnumerateSongsAsync(new[] { tempDir }));
                
                Assert.Null(exception);
            }
            finally
            {
                // Cleanup
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public async Task ParseSetDefinition_WithMissingFiles_SkipsInvalidEntries()
        {
            // Arrange
            var setDefContent = @"#TITLE   Test Song
#L1LABEL Basic
#L1FILE  bas.dtx
#L2LABEL Advanced
#L2FILE  missing.dtx";

            var tempDir = CreateTempDirectory();
            var setDefPath = Path.Combine(tempDir, "set.def");
            File.WriteAllText(setDefPath, setDefContent, Encoding.UTF8);

            // Create only one DTX file
            var basPath = Path.Combine(tempDir, "bas.dtx");
            File.WriteAllText(basPath, "#TITLE: Test Song", Encoding.UTF8);

            try
            {
                // Act
                await _songManager.EnumerateSongsAsync(new[] { tempDir });

                // Assert
                var rootSongs = _songManager.RootSongs;
                var song = rootSongs.FirstOrDefault(s => s.Type == NodeType.Score);
                Assert.NotNull(song);
                Assert.Equal(1, song.AvailableDifficulties); // Only one valid difficulty
            }
            finally
            {
                // Cleanup
                Directory.Delete(tempDir, true);
            }
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("// Comment only")]
        public async Task ParseSetDefinition_WithEmptyOrCommentOnlyContent_HandlesGracefully(string content)
        {
            // Arrange
            var tempDir = CreateTempDirectory();
            var setDefPath = Path.Combine(tempDir, "set.def");
            File.WriteAllText(setDefPath, content, Encoding.UTF8);

            try
            {
                // Act & Assert - Should not throw
                var exception = await Record.ExceptionAsync(async () =>
                    await _songManager.EnumerateSongsAsync(new[] { tempDir }));
                
                Assert.Null(exception);
            }
            finally
            {
                // Cleanup
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public async Task ParseSetDefinition_WithMalformedLines_SkipsInvalidLines()
        {
            // Arrange
            var setDefContent = @"#TITLE   Valid Title
INVALID LINE WITHOUT HASH
#L1LABEL
#L1FILE  bas.dtx
#INVALID_COMMAND Some Value
#L2LABEL Advanced
#L2FILE  adv.dtx";

            var tempDir = CreateTempDirectory();
            var setDefPath = Path.Combine(tempDir, "set.def");
            File.WriteAllText(setDefPath, setDefContent, Encoding.UTF8);

            // Create DTX files
            File.WriteAllText(Path.Combine(tempDir, "bas.dtx"), "#TITLE: Test", Encoding.UTF8);
            File.WriteAllText(Path.Combine(tempDir, "adv.dtx"), "#TITLE: Test", Encoding.UTF8);

            try
            {
                // Act
                await _songManager.EnumerateSongsAsync(new[] { tempDir });

                // Assert - Should parse valid entries and skip invalid ones
                var rootSongs = _songManager.RootSongs;
                var song = rootSongs.FirstOrDefault(s => s.Type == NodeType.Score);
                Assert.NotNull(song);
                Assert.Equal("Valid Title", song.DisplayTitle);
                Assert.True(song.AvailableDifficulties >= 1);
            }
            finally
            {
                // Cleanup
                Directory.Delete(tempDir, true);
            }
        }

        #endregion

        #region Edge Case Tests

        [Theory]
        [InlineData("#TITLE\tTab Separated Title")]
        [InlineData("#TITLE    Multiple Spaces Title")]
        [InlineData("#TITLE Title With Trailing Spaces   ")]
        public async Task ParseSetDefinition_WithVariousWhitespace_ParsesCorrectly(string titleLine)
        {
            // Arrange
            var setDefContent = $@"{titleLine}
#L1LABEL Basic
#L1FILE  bas.dtx";

            var tempDir = CreateTempDirectory();
            var setDefPath = Path.Combine(tempDir, "set.def");
            File.WriteAllText(setDefPath, setDefContent, Encoding.UTF8);

            var basPath = Path.Combine(tempDir, "bas.dtx");
            File.WriteAllText(basPath, "#TITLE: Test", Encoding.UTF8);

            try
            {
                // Act
                await _songManager.EnumerateSongsAsync(new[] { tempDir });

                // Assert
                var rootSongs = _songManager.RootSongs;
                var song = rootSongs.FirstOrDefault(s => s.Type == NodeType.Score);
                Assert.NotNull(song);
                Assert.Contains("Title", song.DisplayTitle);
            }
            finally
            {
                // Cleanup
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public async Task ParseSetDefinition_WithNonSequentialLevels_HandlesCorrectly()
        {
            // Arrange
            var setDefContent = @"#TITLE   Non Sequential Song
#L1LABEL Level 1
#L1FILE  l1.dtx
#L5LABEL Level 5
#L5FILE  l5.dtx
#L3LABEL Level 3
#L3FILE  l3.dtx";

            var tempDir = CreateTempDirectory();
            var setDefPath = Path.Combine(tempDir, "set.def");
            File.WriteAllText(setDefPath, setDefContent, Encoding.UTF8);

            // Create DTX files
            File.WriteAllText(Path.Combine(tempDir, "l1.dtx"), "#TITLE: Test", Encoding.UTF8);
            File.WriteAllText(Path.Combine(tempDir, "l3.dtx"), "#TITLE: Test", Encoding.UTF8);
            File.WriteAllText(Path.Combine(tempDir, "l5.dtx"), "#TITLE: Test", Encoding.UTF8);

            try
            {
                // Act
                await _songManager.EnumerateSongsAsync(new[] { tempDir });

                // Assert
                var rootSongs = _songManager.RootSongs;
                var song = rootSongs.FirstOrDefault(s => s.Type == NodeType.Score);
                Assert.NotNull(song);
                Assert.Equal(3, song.AvailableDifficulties);
            }
            finally
            {
                // Cleanup
                Directory.Delete(tempDir, true);
            }
        }

        #endregion
    }
}
