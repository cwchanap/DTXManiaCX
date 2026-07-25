using System;
using System.Linq;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.Stage.Performance;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SongEntity = DTXMania.Game.Lib.Song.Entities.Song;
using Xunit;

namespace DTXMania.Test.Song
{
    /// <summary>
    /// Tests for the SongDatabaseService.UpdateScoreAsync overload that takes a
    /// PerformanceSummary and persists score + skill values.
    /// Shared SqliteConnection lifecycle pattern mirrors SongDbContextTests
    /// (which has notes on coverlet "using var" quirks for EF disposables).
    /// </summary>
    [Trait("Category", "Unit")]
    public class SongDatabaseServiceSkillSaveTests : System.IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly DbContextOptions<SongDbContext> _options;
        private readonly SongDatabaseService _svc;

        public SongDatabaseServiceSkillSaveTests()
        {
            _connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
            _connection.Open();

            _options = new DbContextOptionsBuilder<SongDbContext>().UseSqlite(_connection).Options;

            var setupCtx = new SongDbContext(_options);
            try { setupCtx.Database.EnsureCreated(); }
            finally { setupCtx.Dispose(); }

            _svc = new SongDatabaseService(_options);
        }

        public void Dispose() { _connection.Dispose(); }

        private async Task<SongChart> SeedChartAsync()
        {
            var ctx = new SongDbContext(_options);
            try
            {
                var song = new SongEntity { Title = "Test Song" };
                var chart = new SongChart { Song = song, FilePath = "test.dtx", DrumLevel = 78, DrumLevelDec = 33 };
                ctx.SongCharts.Add(chart);
                await ctx.SaveChangesAsync();
                return chart;
            }
            finally { ctx.Dispose(); }
        }

        private async Task SeedScoreAsync(int chartId, SongScore? seed = null)
        {
            var ctx = new SongDbContext(_options);
            try
            {
                seed ??= new SongScore { ChartId = chartId, Instrument = EInstrumentPart.DRUMS };
                seed.ChartId = chartId;
                seed.Instrument = EInstrumentPart.DRUMS;
                ctx.SongScores.Add(seed);
                await ctx.SaveChangesAsync();
            }
            finally { ctx.Dispose(); }
        }

        private async Task<SongScore> LoadSavedScoreAsync(int chartId)
        {
            var ctx = new SongDbContext(_options);
            try
            {
                return await ctx.SongScores.AsNoTracking().FirstAsync(s => s.ChartId == chartId);
            }
            finally { ctx.Dispose(); }
        }

        [Fact]
        public async Task UpdateScoreAsync_WithSummary_PersistsBestSkill()
        {
            var chart = await SeedChartAsync();
            await SeedScoreAsync(chart.Id);

            var summary = new PerformanceSummary
            {
                Score = 800000,
                MaxCombo = 100,
                ClearFlag = true,
                PerfectCount = 100, GreatCount = 0, GoodCount = 0, PoorCount = 0, MissCount = 0,
                TotalNotes = 100,
                PlayingSkill = 100.0,
                GameSkill = 162.6,
                ChartLevel = 78, ChartLevelDec = 33
            };

            await _svc.UpdateScoreAsync(chart.Id, EInstrumentPart.DRUMS, summary);

            var saved = await LoadSavedScoreAsync(chart.Id);
            Assert.Equal(800000, saved.BestScore);
            Assert.Equal(100, saved.BestPerfect);
            Assert.Equal(100, saved.MaxCombo);
            Assert.True(saved.FullCombo);
            Assert.Equal(162.6, saved.HighSkill, 4);
            Assert.Equal(162.6, saved.SongSkill, 4);
            Assert.Equal(162.6, saved.LastSkillPoint, 4);
            Assert.Equal(1, saved.PlayCount);
        }

        [Fact]
        public async Task UpdateScoreAsync_LowerScore_KeepsExistingBestButUpdatesLast()
        {
            var chart = await SeedChartAsync();
            await SeedScoreAsync(chart.Id, new SongScore
            {
                BestScore = 900000, BestPerfect = 95, MaxCombo = 90,
                HighSkill = 170.0, SongSkill = 170.0, PlayCount = 5
            });

            var lowerSummary = new PerformanceSummary
            {
                Score = 500000, MaxCombo = 50, TotalNotes = 100,
                PerfectCount = 50, MissCount = 50,
                PlayingSkill = 50.0, GameSkill = 78.0,
                ChartLevel = 78, ChartLevelDec = 33
            };

            await _svc.UpdateScoreAsync(chart.Id, EInstrumentPart.DRUMS, lowerSummary);

            var saved = await LoadSavedScoreAsync(chart.Id);
            Assert.Equal(900000, saved.BestScore);
            Assert.Equal(170.0, saved.HighSkill, 4);
            Assert.Equal(500000, saved.LastScore);
            Assert.Equal(78.0, saved.LastSkillPoint, 4);
            Assert.Equal(6, saved.PlayCount);
        }

        [Fact]
        public async Task UpdateScoreAsync_LowerScoreWithBetterRunAggregates_UpdatesThemIndependently()
        {
            var chart = await SeedChartAsync();
            await SeedScoreAsync(chart.Id, new SongScore
            {
                BestScore = 900000,
                BestRank = 50,
                MaxCombo = 50,
                FullCombo = false,
                PlayCount = 1
            });

            var summary = new PerformanceSummary
            {
                Score = 500000,
                MaxCombo = 100,
                ClearFlag = true,
                TotalNotes = 100,
                PerfectCount = 90,
                GreatCount = 10,
                PlayingSkill = 90.0,
                GameSkill = 78.0
            };

            await _svc.UpdateScoreAsync(chart.Id, EInstrumentPart.DRUMS, summary);

            var saved = await LoadSavedScoreAsync(chart.Id);
            Assert.Equal(900000, saved.BestScore);
            Assert.Equal(90, saved.BestRank);
            Assert.Equal(100, saved.MaxCombo);
            Assert.True(saved.FullCombo);
        }

        [Fact]
        public async Task UpdateScoreAsync_FirstZeroScoreRun_InitializesBestRunFields()
        {
            var chart = await SeedChartAsync();
            var summary = new PerformanceSummary
            {
                Score = 0,
                MaxCombo = 30,
                ClearFlag = false,
                TotalNotes = 100,
                PerfectCount = 70,
                MissCount = 30,
                PlayingSkill = 70.0,
                GameSkill = 0.0
            };

            await _svc.UpdateScoreAsync(chart.Id, EInstrumentPart.DRUMS, summary);

            var saved = await LoadSavedScoreAsync(chart.Id);
            Assert.Equal(0, saved.BestScore);
            Assert.Equal(70, saved.BestRank);
            Assert.Equal(100, saved.TotalNotes);
            Assert.Equal(70, saved.BestPerfect);
            Assert.Equal(30, saved.BestMiss);
            Assert.Equal(30, saved.MaxCombo);
            Assert.Equal(1, saved.PlayCount);
        }

        [Fact]
        public async Task UpdateScoreAsync_FirstPlay_CreatesScoreRow()
        {
            var chart = await SeedChartAsync();
            // Note: do NOT seed a score row — verifies create-on-miss behavior

            var summary = new PerformanceSummary
            {
                Score = 600000, MaxCombo = 80, ClearFlag = true, TotalNotes = 100,
                PerfectCount = 80, GreatCount = 20,
                PlayingSkill = 87.0, GameSkill = 141.31,
                ChartLevel = 78, ChartLevelDec = 33
            };

            await _svc.UpdateScoreAsync(chart.Id, EInstrumentPart.DRUMS, summary);

            var saved = await LoadSavedScoreAsync(chart.Id);
            Assert.Equal(600000, saved.BestScore);
            Assert.Equal(80, saved.MaxCombo);
            Assert.Equal(141.31, saved.HighSkill, 4);
            Assert.Equal(1, saved.PlayCount);
        }

        [Fact]
        public async Task UpdateScoreAsync_NullSummary_ShouldThrow()
        {
            var chart = await SeedChartAsync();
            await SeedScoreAsync(chart.Id);

            await Assert.ThrowsAsync<System.ArgumentNullException>(
                () => _svc.UpdateScoreAsync(chart.Id, EInstrumentPart.DRUMS, null!));
        }

        [Fact]
        public async Task UpdateScoreAsync_ClearFlagFalse_DoesNotIncrementClearCount()
        {
            var chart = await SeedChartAsync();
            await SeedScoreAsync(chart.Id, new SongScore { ClearCount = 3 });

            var summary = new PerformanceSummary
            {
                Score = 100000, MaxCombo = 10, ClearFlag = false, TotalNotes = 100,
                PerfectCount = 5, MissCount = 95,
                PlayingSkill = 5.75, GameSkill = 9.35,
                ChartLevel = 78, ChartLevelDec = 33
            };

            await _svc.UpdateScoreAsync(chart.Id, EInstrumentPart.DRUMS, summary);

            var saved = await LoadSavedScoreAsync(chart.Id);
            Assert.Equal(3, saved.ClearCount); // unchanged
            Assert.Equal(1, saved.PlayCount);  // incremented
        }

        [Fact]
        public async Task UpdateScoreAsync_ClearedWithMisses_FullComboIsFalse()
        {
            var chart = await SeedChartAsync();
            await SeedScoreAsync(chart.Id);

            var summary = new PerformanceSummary
            {
                Score = 700000, MaxCombo = 90, ClearFlag = true, TotalNotes = 100,
                PerfectCount = 80, GreatCount = 15, MissCount = 5,
                PlayingSkill = 80.0, GameSkill = 130.08,
                ChartLevel = 78, ChartLevelDec = 33
            };

            await _svc.UpdateScoreAsync(chart.Id, EInstrumentPart.DRUMS, summary);

            var saved = await LoadSavedScoreAsync(chart.Id);
            Assert.False(saved.FullCombo); // had misses, so not full combo despite clearing
        }

        /// <summary>
        /// Regression guard: when the PerformanceHistory insert in MergeAsync fails
        /// mid-transaction, the score changes from the first SaveChangesAsync must be
        /// rolled back. This exercises the transaction boundary in UpdateScoreAsync — a
        /// separate code path from the NxImporter rollback tested in NxScoreImporterTests.
        /// </summary>
        [Fact]
        public async Task UpdateScoreAsync_WhenHistorySaveFails_ShouldRollbackScoreChanges()
        {
            var chart = await SeedChartAsync();
            await SeedScoreAsync(chart.Id, new SongScore
            {
                BestScore = 900000,
                PlayCount = 3,
            });

            // Delete the Song row (with FK temporarily off) so that the PerformanceHistory
            // insert inside MergeAsync hits a FK violation. The score was already loaded
            // and its first SaveChangesAsync succeeded, so only the transaction rollback
            // can undo the PlayCount increment.
            using (var cmd = _connection.CreateCommand())
            {
                cmd.CommandText = "PRAGMA foreign_keys = OFF";
                cmd.ExecuteNonQuery();
                cmd.CommandText = $"DELETE FROM Songs WHERE Id = {chart.SongId}";
                cmd.ExecuteNonQuery();
                cmd.CommandText = "PRAGMA foreign_keys = ON";
                cmd.ExecuteNonQuery();
            }

            var summary = new PerformanceSummary
            {
                Score = 950000, MaxCombo = 100, ClearFlag = true, TotalNotes = 100,
                PerfectCount = 95, GreatCount = 5,
                PlayingSkill = 95.0, GameSkill = 155.0,
                ChartLevel = 78, ChartLevelDec = 33
            };

            // The MergeAsync call should throw due to the dangling SongId FK.
            await Assert.ThrowsAnyAsync<DbUpdateException>(
                () => _svc.UpdateScoreAsync(chart.Id, EInstrumentPart.DRUMS, summary));

            // Verify the score's PlayCount was NOT incremented (transaction rolled back).
            var score = await LoadSavedScoreAsync(chart.Id);
            Assert.Equal(3, score.PlayCount);
        }

        /// <summary>
        /// Regression guard for the NX-imported best-score data-loss bug.
        /// NxScoreData.HasDrumData allows BestScore > 0 with PlayCount == 0,
        /// and NxScoreImporter writes best fields without requiring PlayCount > 0.
        /// The save path previously used PlayCount == 0 alone as the "first play"
        /// signal, which treated the imported row as empty and overwrote the
        /// imported best (and rank) even when the new CX result was worse.
        /// The fix treats a row as a first play only when it is newly created
        /// or a genuinely empty placeholder (PlayCount == 0 AND BestScore == 0).
        /// </summary>
        [Fact]
        public async Task UpdateScoreAsync_ImportedBestWithZeroPlayCount_LowerScoreKeepsImportedBest()
        {
            var chart = await SeedChartAsync();
            // Simulate an NX-imported row: BestScore > 0, PlayCount == 0.
            // This is the exact state NxScoreImporter produces when the source
            // .score.ini has a high score but zero play count.
            await SeedScoreAsync(chart.Id, new SongScore
            {
                BestScore = 950000,
                BestPerfect = 95,
                BestGreat = 5,
                BestGood = 0,
                BestPoor = 0,
                BestMiss = 0,
                BestRank = 90,
                TotalNotes = 100,
                MaxCombo = 95,
                HighSkill = 180.0,
                PlayCount = 0,
            });

            // First CX play with a score lower than the imported best.
            var worseSummary = new PerformanceSummary
            {
                Score = 600000,
                MaxCombo = 60,
                ClearFlag = true,
                TotalNotes = 100,
                PerfectCount = 60,
                GreatCount = 30,
                GoodCount = 10,
                PlayingSkill = 60.0,
                GameSkill = 95.0,
                ChartLevel = 78,
                ChartLevelDec = 33,
            };

            await _svc.UpdateScoreAsync(chart.Id, EInstrumentPart.DRUMS, worseSummary);

            var saved = await LoadSavedScoreAsync(chart.Id);
            // The imported best must survive the worse first CX play.
            Assert.Equal(950000, saved.BestScore);
            Assert.Equal(95, saved.BestPerfect);
            Assert.Equal(5, saved.BestGreat);
            Assert.Equal(0, saved.BestGood);
            Assert.Equal(0, saved.BestPoor);
            Assert.Equal(0, saved.BestMiss);
            Assert.Equal(100, saved.TotalNotes);
            Assert.Equal(95, saved.MaxCombo);
            Assert.Equal(180.0, saved.HighSkill, 4);
            // BestRank must keep the imported 90, not be overwritten by the
            // worse run's 60.
            Assert.Equal(90, saved.BestRank);
            // Last-play fields reflect the new CX play.
            Assert.Equal(600000, saved.LastScore);
            Assert.Equal(95.0, saved.LastSkillPoint, 4);
            Assert.Equal(1, saved.PlayCount);
            Assert.Equal(1, saved.ClearCount);
        }

        /// <summary>
        /// Companion to the regression above: when the first CX play on an
        /// imported row beats the imported best, the new best is recorded.
        /// Ensures the fix does not over-correct and block legitimate best
        /// updates on imported rows.
        /// </summary>
        [Fact]
        public async Task UpdateScoreAsync_ImportedBestWithZeroPlayCount_HigherScoreUpdatesBest()
        {
            var chart = await SeedChartAsync();
            await SeedScoreAsync(chart.Id, new SongScore
            {
                BestScore = 500000,
                BestPerfect = 50,
                BestGreat = 50,
                BestRank = 50,
                TotalNotes = 100,
                MaxCombo = 50,
                HighSkill = 80.0,
                PlayCount = 0,
            });

            var betterSummary = new PerformanceSummary
            {
                Score = 900000,
                MaxCombo = 90,
                ClearFlag = true,
                TotalNotes = 100,
                PerfectCount = 90,
                GreatCount = 10,
                PlayingSkill = 90.0,
                GameSkill = 150.0,
                ChartLevel = 78,
                ChartLevelDec = 33,
            };

            await _svc.UpdateScoreAsync(chart.Id, EInstrumentPart.DRUMS, betterSummary);

            var saved = await LoadSavedScoreAsync(chart.Id);
            Assert.Equal(900000, saved.BestScore);
            Assert.Equal(90, saved.BestPerfect);
            Assert.Equal(10, saved.BestGreat);
            Assert.Equal(90, saved.BestRank);
            Assert.Equal(90, saved.MaxCombo);
            Assert.Equal(150.0, saved.HighSkill, 4);
            Assert.Equal(1, saved.PlayCount);
        }
    }
}
