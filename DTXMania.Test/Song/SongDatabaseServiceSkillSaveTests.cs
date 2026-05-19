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
    [Trait("Category", "Integration")]
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
    }
}
