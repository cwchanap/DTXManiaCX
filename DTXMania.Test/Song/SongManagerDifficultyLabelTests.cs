using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Entities;
using Xunit;

namespace DTXMania.Test.Song
{
    /// <summary>
    /// Verifies the difficulty-tier label resolution that drives the performance-stage difficulty
    /// badge. Regression guard for the bug where a multi-difficulty song loaded from the database
    /// showed only the fallback (DTX) badge because the per-difficulty label was synthesized as
    /// "Level N" instead of the authentic SET.def #LnLABEL (BASIC/ADVANCED/EXTREME/MASTER/REAL).
    /// </summary>
    [Collection("SongManager")]
    public class SongManagerDifficultyLabelTests : IDisposable
    {
        private readonly SongManager _songManager;
        private readonly List<string> _tempDirs = new();
        private readonly string _testDbPath;

        public SongManagerDifficultyLabelTests()
        {
            SongManager.ResetInstanceForTesting();
            _songManager = SongManager.Instance;
            _testDbPath = Path.Combine(Path.GetTempPath(), $"test_difflabel_{Guid.NewGuid()}.db");
        }

        public void Dispose()
        {
            foreach (var dir in _tempDirs)
            {
                try
                {
                    if (Directory.Exists(dir))
                        Directory.Delete(dir, true);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
            _songManager?.Clear();

            try
            {
                if (File.Exists(_testDbPath))
                    File.Delete(_testDbPath);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }

        private string CreateTempDirectory()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);
            _tempDirs.Add(tempDir);
            return tempDir;
        }

        #region ResolveDifficultyLabel

        [Trait("Category", "Unit")]
        [Fact]
        public void ResolveDifficultyLabel_PersistedLabelPresent_ShouldReturnPersistedLabel()
        {
            var chart = new SongChart { FilePath = "/songs/song/bas.dtx", DifficultyLabel = "EXTREME" };
            var setDefLabels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["bas.dtx"] = "BASIC"
            };

            // The persisted chart label wins over the SET.def recovery so re-reading the disk is skipped.
            Assert.Equal("EXTREME", SongManager.ResolveDifficultyLabel(chart, setDefLabels, 0));
        }

        [Trait("Category", "Unit")]
        [Fact]
        public void ResolveDifficultyLabel_EmptyPersistedLabel_ShouldRecoverFromSetDefByFileName()
        {
            var chart = new SongChart { FilePath = "/songs/song/adv.dtx", DifficultyLabel = "" };
            var setDefLabels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["bas.dtx"] = "BASIC",
                ["adv.dtx"] = "ADVANCED",
                ["ext.dtx"] = "EXTREME"
            };

            // Legacy databases stored an empty label; recover the authentic name by chart file name.
            Assert.Equal("ADVANCED", SongManager.ResolveDifficultyLabel(chart, setDefLabels, 1));
        }

        [Trait("Category", "Unit")]
        [Fact]
        public void ResolveDifficultyLabel_FileNameCaseDiffers_ShouldRecoverCaseInsensitively()
        {
            var chart = new SongChart { FilePath = "/songs/song/MAS.DTX", DifficultyLabel = "   " };
            var setDefLabels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["mas.dtx"] = "MASTER"
            };

            Assert.Equal("MASTER", SongManager.ResolveDifficultyLabel(chart, setDefLabels, 3));
        }

        [Trait("Category", "Unit")]
        [Fact]
        public void ResolveDifficultyLabel_NoMatchAndNoPersistedLabel_ShouldFallBackToLevelN()
        {
            var chart = new SongChart { FilePath = "/songs/song/unknown.dtx", DifficultyLabel = "" };
            var setDefLabels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["bas.dtx"] = "BASIC"
            };

            // scoreIndex 2 -> "Level 3" (one-based for display).
            Assert.Equal("Level 3", SongManager.ResolveDifficultyLabel(chart, setDefLabels, 2));
        }

        [Trait("Category", "Unit")]
        [Fact]
        public void ResolveDifficultyLabel_NullSetDefLabels_ShouldFallBackToLevelN()
        {
            var chart = new SongChart { FilePath = "/songs/song/bas.dtx", DifficultyLabel = "" };

            Assert.Equal("Level 1", SongManager.ResolveDifficultyLabel(chart, null!, 0));
        }

        #endregion

        #region GetSetDefLabelsByFile

        [Trait("Category", "Unit")]
        [Fact]
        public void GetSetDefLabelsByFile_StandardSetDef_ShouldMapFilesToLabels()
        {
            var dir = CreateTempDirectory();
            var content =
                "#TITLE Soukyuu e no shouka\n" +
                "#L1LABEL BASIC\n#L1FILE bas.dtx\n" +
                "#L2LABEL ADVANCED\n#L2FILE adv.dtx\n" +
                "#L3LABEL EXTREME\n#L3FILE ext.dtx\n" +
                "#L4LABEL MASTER\n#L4FILE mas.dtx\n" +
                "#L5LABEL REAL\n#L5FILE real.dtx\n";
            File.WriteAllText(Path.Combine(dir, "set.def"), content, Encoding.UTF8);

            var labels = _songManager.GetSetDefLabelsByFile(dir);

            Assert.Equal("BASIC", labels["bas.dtx"]);
            Assert.Equal("ADVANCED", labels["adv.dtx"]);
            Assert.Equal("EXTREME", labels["ext.dtx"]);
            Assert.Equal("MASTER", labels["mas.dtx"]);
            Assert.Equal("REAL", labels["real.dtx"]);
        }

        [Trait("Category", "Unit")]
        [Fact]
        public void GetSetDefLabelsByFile_Utf16SpacedFormat_ShouldMapFilesToLabels()
        {
            // DTXMania SET.def files are commonly UTF-16; read as UTF-8 they appear as the
            // spaced "# L 1 L A B E L   B A S I C" form. NormalizeSetDefLine must repair this so
            // the difficulty badge still resolves. This mirrors the real on-disk file that
            // exposed the original bug.
            var dir = CreateTempDirectory();
            var content =
                "#TITLE Soukyuu\r\n" +
                "#L1LABEL BASIC\r\n#L1FILE bas.dtx\r\n" +
                "#L2LABEL ADVANCED\r\n#L2FILE adv.dtx\r\n" +
                "#L3LABEL EXTREME\r\n#L3FILE ext.dtx\r\n" +
                "#L4LABEL MASTER\r\n#L4FILE mas.dtx\r\n";
            File.WriteAllText(Path.Combine(dir, "set.def"), content, Encoding.Unicode);

            var labels = _songManager.GetSetDefLabelsByFile(dir);

            Assert.Equal("BASIC", labels["bas.dtx"]);
            Assert.Equal("ADVANCED", labels["adv.dtx"]);
            Assert.Equal("EXTREME", labels["ext.dtx"]);
            Assert.Equal("MASTER", labels["mas.dtx"]);
        }

        [Trait("Category", "Unit")]
        [Fact]
        public void GetSetDefLabelsByFile_NoSetDef_ShouldReturnEmpty()
        {
            var dir = CreateTempDirectory();

            var labels = _songManager.GetSetDefLabelsByFile(dir);

            Assert.Empty(labels);
        }

        #endregion

        #region Database-load path (normal launch)

        [Trait("Category", "Integration")]
        [Fact]
        public async System.Threading.Tasks.Task BuildSongListFromDatabase_MultiDifficultySetDef_ShouldExposeRealDifficultyLabels()
        {
            // Reproduces the user-facing scenario: a multi-difficulty SET.def song is enumerated,
            // then re-loaded from the database on the next launch. The per-difficulty labels
            // surfaced to the performance-stage difficulty badge must be the authentic SET.def
            // names (BASIC/ADVANCED/EXTREME/MASTER), not the "Level N" placeholder that produced
            // the always-fallback badge.
            var dir = CreateTempDirectory();
            var setDefContent =
                "#TITLE Soukyuu e no shouka\n" +
                "#L1LABEL BASIC\n#L1FILE bas.dtx\n" +
                "#L2LABEL ADVANCED\n#L2FILE adv.dtx\n" +
                "#L3LABEL EXTREME\n#L3FILE ext.dtx\n" +
                "#L4LABEL MASTER\n#L4FILE mas.dtx\n";
            File.WriteAllText(Path.Combine(dir, "set.def"), setDefContent, Encoding.UTF8);

            var files = new[] { "bas", "adv", "ext", "mas" };
            var levels = new[] { 36, 60, 74, 87 };
            for (int i = 0; i < files.Length; i++)
            {
                File.WriteAllText(
                    Path.Combine(dir, $"{files[i]}.dtx"),
                    $"#TITLE: Soukyuu e no shouka\n#DLEVEL: {levels[i]}",
                    Encoding.UTF8);
            }

            await _songManager.InitializeDatabaseServiceAsync(_testDbPath);
            await _songManager.EnumerateSongsAsync(new[] { dir });

            // Force the database-load path used on a normal launch (clears in-memory nodes and
            // rebuilds them from persisted entities via CreateSongNodeFromDatabaseEntities).
            await _songManager.BuildSongListFromDatabasePublicAsync(new[] { dir });

            SongListNode? song = null;
            foreach (var node in _songManager.RootSongs)
            {
                if (node.Type == NodeType.Score)
                {
                    song = node;
                    break;
                }
            }

            Assert.NotNull(song);
            Assert.Equal("BASIC", song!.DifficultyLabels[0]);
            Assert.Equal("ADVANCED", song.DifficultyLabels[1]);
            Assert.Equal("EXTREME", song.DifficultyLabels[2]);
            Assert.Equal("MASTER", song.DifficultyLabels[3]);
        }

        #endregion
    }
}
