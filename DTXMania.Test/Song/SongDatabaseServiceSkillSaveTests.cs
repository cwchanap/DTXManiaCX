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
    }
}
