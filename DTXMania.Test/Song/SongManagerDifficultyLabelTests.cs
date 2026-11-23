using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Test.TestData;
using Xunit;
using SongEntity = DTXMania.Game.Lib.Song.Entities.Song;

namespace DTXMania.Test.Song
{
    /// <summary>
    /// Verifies the difficulty-tier label resolution that drives the performance-stage difficulty
    /// badge. Regression guard for the bug where a multi-difficulty song loaded from the database
    /// showed only the fallback (DTX) badge because the per-difficulty label was synthesized as
    /// "Level N" instead of the authentic SET.def #LnLABEL (BASIC/ADVANCED/EXTREME/MASTER/REAL).
    /// </summary>
    public class SongManagerDifficultyLabelTests : IDisposable
    {
        private readonly SongLibrary _songManager;
        private readonly List<string> _tempDirs = new();
        private readonly string _testDbPath;

        public SongManagerDifficultyLabelTests()
        {
            _testDbPath = Path.Combine(Path.GetTempPath(), $"test_difflabel_{Guid.NewGuid()}.db");
            _songManager = new SongLibrary(new SongDatabase(_testDbPath));
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
            _songManager?.Dispose();

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

        /// <summary>
        /// The initialized database service is stored in a private field swapped
        /// under the manager lock; tests read it via the established reflection
        /// seam after the test-only DatabaseService accessor was removed
        /// (HPA-801 Decision 0).
        /// </summary>
        private SongDatabase ManagerDatabaseService => _songManager.Database;


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
            Assert.Equal("EXTREME", SongHierarchyBuilder.ResolveDifficultyLabel(chart, setDefLabels, 0));
        }

        [Trait("Category", "Unit")]
        [Fact]
        public void ResolveDifficultyLabel_EmptyPersistedLabel_ShouldRecoverFromSetDefByChartPath()
        {
            var chart = new SongChart { FilePath = "/songs/song/adv.dtx", DifficultyLabel = "" };
            var setDefLabels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["/songs/song/bas.dtx"] = "BASIC",
                ["/songs/song/adv.dtx"] = "ADVANCED",
                ["/songs/song/ext.dtx"] = "EXTREME"
            };

            // Legacy databases stored an empty label; recover the authentic name by resolved chart path.
            Assert.Equal("ADVANCED", SongHierarchyBuilder.ResolveDifficultyLabel(chart, setDefLabels, 1));
        }

        [Trait("Category", "Unit")]
        [Fact]
        public void ResolveDifficultyLabel_FileNameCaseDiffers_ShouldRecoverCaseInsensitively()
        {
            var chart = new SongChart { FilePath = "/songs/song/MAS.DTX", DifficultyLabel = "   " };
            var setDefLabels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["/songs/song/mas.dtx"] = "MASTER"
            };

            Assert.Equal("MASTER", SongHierarchyBuilder.ResolveDifficultyLabel(chart, setDefLabels, 3));
        }

        [Trait("Category", "Unit")]
        [Fact]
        public void ResolveDifficultyLabel_NoMatchAndNoPersistedLabel_ShouldFallBackToLevelN()
        {
            var chart = new SongChart { FilePath = "/songs/song/unknown.dtx", DifficultyLabel = "" };
            var setDefLabels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["/songs/song/bas.dtx"] = "BASIC"
            };

            // scoreIndex 2 -> "Level 3" (one-based for display).
            Assert.Equal("Level 3", SongHierarchyBuilder.ResolveDifficultyLabel(chart, setDefLabels, 2));
        }

        [Trait("Category", "Unit")]
        [Fact]
        public void ResolveDifficultyLabel_UnlabeledSetDefSlot_ShouldFallBackToTheSlotLevel()
        {
            // An unlabeled #L3FILE persists DifficultyLevel 3; the scan shows it as "Level 3".
            var chart = new SongChart { FilePath = "/songs/song/ext.dtx", DifficultyLabel = "", DifficultyLevel = 3 };

            Assert.Equal("Level 3", SongHierarchyBuilder.ResolveDifficultyLabel(chart, null!, 0));
        }

        [Trait("Category", "Unit")]
        [Fact]
        public void ResolveDifficultyLabel_NullSetDefLabels_ShouldFallBackToLevelN()
        {
            var chart = new SongChart { FilePath = "/songs/song/bas.dtx", DifficultyLabel = "" };

            Assert.Equal("Level 1", SongHierarchyBuilder.ResolveDifficultyLabel(chart, null!, 0));
        }

        #endregion

        [Trait("Category", "Unit")]
        [Fact]
        public void ResolveDifficultyLabel_SubDirectoryChartPath_ShouldRecoverByResolvedPath()
        {
            // End-to-end: a chart whose FilePath includes a subdirectory ("charts/bas.dtx") must
            // recover its label via the resolved path the SET.def map is keyed on — a bare
            // file-name key would collide with same-named charts elsewhere on disk.
            var chart = new SongChart { FilePath = "/songs/song/charts/bas.dtx", DifficultyLabel = "" };
            var setDefLabels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["/songs/song/charts/bas.dtx"] = "BASIC",
                ["/songs/song/other/bas.dtx"] = "MASTER"
            };

            Assert.Equal("BASIC", SongHierarchyBuilder.ResolveDifficultyLabel(chart, setDefLabels, 0));
        }

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

            await _songManager.InitializeAsync();
            await _songManager.EnumerateAndImportSongsAsync(new[] { dir });

            // Simulate a restart: a fresh library over the same database, so the
            // assertions observe the warm DB-load path
            // (CreateSongNodeFromDatabaseEntities) and not nodes left in memory
            // by the scan.
            using var reloaded = new SongLibrary(new SongDatabase(_testDbPath));
            Assert.True(await reloaded.InitializeAsync());
            await reloaded.BuildHierarchyFromDatabaseOnceAsync(new[] { dir });

            SongListNode? song = null;
            foreach (var node in reloaded.RootSongs)
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

        [Trait("Category", "Integration")]
        [Fact]
        public async System.Threading.Tasks.Task BuildSongListFromDatabase_OverlongDifficultyLabel_ShouldBeClampedToMaxLength()
        {
            // SongChart.DifficultyLabel is [MaxLength(50)]. A raw SET.def label longer than that
            // must be clamped before persistence so the column contract is never violated.
            var dir = CreateTempDirectory();
            var overlongLabel = new string('A', 60); // 60 chars > 50
            var setDefContent =
                "#TITLE Clamp test\n" +
                "#L1LABEL " + overlongLabel + "\n#L1FILE bas.dtx\n";
            File.WriteAllText(Path.Combine(dir, "set.def"), setDefContent, Encoding.UTF8);
            File.WriteAllText(Path.Combine(dir, "bas.dtx"), "#TITLE: Clamp test\n#DLEVEL: 36", Encoding.UTF8);

            await _songManager.InitializeAsync();
            await _songManager.EnumerateAndImportSongsAsync(new[] { dir });

            // Fresh library over the same database: observe the warm build, not
            // nodes left in memory by the scan.
            using var reloaded = new SongLibrary(new SongDatabase(_testDbPath));
            Assert.True(await reloaded.InitializeAsync());
            await reloaded.BuildHierarchyFromDatabaseOnceAsync(new[] { dir });

            SongListNode? song = null;
            foreach (var node in reloaded.RootSongs)
            {
                if (node.Type == NodeType.Score)
                {
                    song = node;
                    break;
                }
            }
            Assert.NotNull(song);

            var persistedChart = song!.DatabaseChart ?? song.DatabaseSong?.Charts?.FirstOrDefault();
            Assert.NotNull(persistedChart);
            Assert.Equal(50, persistedChart!.DifficultyLabel.Length);
        }

        [Trait("Category", "Integration")]
        [Fact]
        public async System.Threading.Tasks.Task BuildSongListFromDatabase_AfterSetDefRemoval_ShouldRetainPersistedLabels()
        {
            // Contract: once difficulty labels are persisted to the database, the DB-load path
            // (CreateSongNodeFromDatabaseEntities -> ResolveDifficultyLabel) surfaces them without
            // needing SET.def on disk. This is the user-facing guarantee behind the skip optimization
            // at SongManager.cs (only read SET.def when a chart label is missing).
            //
            // NOTE: this does NOT prove the disk read is skipped — that is a pure performance
            // optimization that is unobservable behaviorally (the persisted label always wins in
            // ResolveDifficultyLabel, verified by ResolveDifficultyLabel_PersistedLabelPresent).
            // The skip is correct by inspection; this test locks in the contract it relies on.
            var dir = CreateTempDirectory();
            var setDefContent =
                "#TITLE Persistence contract\n" +
                "#L1LABEL BASIC\n#L1FILE bas.dtx\n" +
                "#L2LABEL ADVANCED\n#L2FILE adv.dtx\n";
            File.WriteAllText(Path.Combine(dir, "set.def"), setDefContent, Encoding.UTF8);
            File.WriteAllText(Path.Combine(dir, "bas.dtx"), "#TITLE: Persistence contract\n#DLEVEL: 36", Encoding.UTF8);
            File.WriteAllText(Path.Combine(dir, "adv.dtx"), "#TITLE: Persistence contract\n#DLEVEL: 60", Encoding.UTF8);

            // First launch: enumerate, persisting the recovered labels to the database.
            await _songManager.InitializeAsync();
            await _songManager.EnumerateAndImportSongsAsync(new[] { dir });

            // Simulate a subsequent launch where SET.def is gone (renamed/moved) but the DB persists:
            // a fresh library over the same database exercises the real warm build.
            File.Delete(Path.Combine(dir, "set.def"));
            using var reloaded = new SongLibrary(new SongDatabase(_testDbPath));
            Assert.True(await reloaded.InitializeAsync());
            await reloaded.BuildHierarchyFromDatabaseOnceAsync(new[] { dir });

            SongListNode? song = null;
            foreach (var node in reloaded.RootSongs)
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
        }

        [Trait("Category", "Unit")]
        [Fact]
        public void CreateSongNodeFromDatabaseEntities_ChartsInSubdirectoryWithEmptyLabels_ShouldRecoverFromParentSetDef()
        {
            // Reproduces the exact scenario from review: a multi-difficulty SET.def references
            // charts in a subdirectory ("#L1FILE charts/bas.dtx"), and a legacy database stored
            // an empty SongChart.DifficultyLabel. primaryChart.FilePath then points inside
            // "charts/", so the DB-load path used to read "charts/set.def" (missing) instead of
            // the song-folder "set.def". The directory walk-up must recover the authentic labels
            // so the performance-stage badge shows BASIC/EXTREME rather than "Level N"/DTX.
            var songDir = CreateTempDirectory();
            var chartsDir = Path.Combine(songDir, "charts");
            Directory.CreateDirectory(chartsDir);
            var setDefContent =
                "#TITLE Subdir song\n" +
                "#L1LABEL BASIC\n#L1FILE charts/bas.dtx\n" +
                "#L2LABEL EXTREME\n#L2FILE charts/ext.dtx\n";
            File.WriteAllText(Path.Combine(songDir, "set.def"), setDefContent, Encoding.UTF8);

            var song = new SongEntity { Title = "Subdir song" };
            var charts = new[]
            {
                new SongChart
                {
                    FilePath = Path.Combine(chartsDir, "bas.dtx"),
                    DifficultyLabel = "",   // legacy DB: empty persisted label
                    HasDrumChart = true,
                    DrumLevel = 36
                },
                new SongChart
                {
                    FilePath = Path.Combine(chartsDir, "ext.dtx"),
                    DifficultyLabel = "",
                    HasDrumChart = true,
                    DrumLevel = 74
                }
            };

            var result = _songManager.Hierarchy.CreateSongNodeFromDatabaseEntities(song, charts);

            Assert.NotNull(result);
            Assert.Equal("BASIC", result!.DifficultyLabels[0]);
            Assert.Equal("EXTREME", result.DifficultyLabels[1]);
        }

        #endregion
    }
}
