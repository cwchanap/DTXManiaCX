using System.Threading.Tasks;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.Stage.Performance;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SongEntity = DTXMania.Game.Lib.Song.Entities.Song;
using Xunit;

namespace DTXMania.Test.Song
{
    [Trait("Category", "Unit")]
    public class SongDatabaseServiceHighSkillTests : System.IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly DbContextOptions<SongDbContext> _options;
        private readonly SongDatabaseService _svc;

        public SongDatabaseServiceHighSkillTests()
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
                var song = new SongEntity { Title = "HighSkill Test" };
                var chart = new SongChart { Song = song, FilePath = "highskill-test.dtx", DrumLevel = 78, DrumLevelDec = 33 };
                ctx.SongCharts.Add(chart);
                await ctx.SaveChangesAsync();
                return chart;
            }
            finally { ctx.Dispose(); }
        }

        private async Task SeedScoreAsync(int chartId, SongScore seed)
        {
            var ctx = new SongDbContext(_options);
            try
            {
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
        public async Task UpdateScoreAsync_WithPerformanceSummary_ShouldUpdateHighSkill()
        {
            var chart = await SeedChartAsync();
            await SeedScoreAsync(chart.Id, new SongScore
            {
                BestScore = 900000,
                HighSkill = 150.0,
                SongSkill = 150.0,
                PlayCount = 3
            });

            var summary = new PerformanceSummary
            {
                Score = 800000,
                MaxCombo = 90,
                ClearFlag = true,
                PerfectCount = 90,
                GreatCount = 10,
                GoodCount = 0,
                PoorCount = 0,
                MissCount = 0,
                TotalNotes = 100,
                PlayingSkill = 92.5,
                GameSkill = 162.6,
                ChartLevel = 78,
                ChartLevelDec = 33
            };

            await _svc.UpdateScoreAsync(chart.Id, EInstrumentPart.DRUMS, summary);

            var saved = await LoadSavedScoreAsync(chart.Id);
            Assert.Equal(900000, saved.BestScore);
            Assert.Equal(162.6, saved.HighSkill, 4);
            Assert.Equal(162.6, saved.SongSkill, 4);
            Assert.Equal(162.6, saved.LastSkillPoint, 4);
            Assert.Equal(800000, saved.LastScore);
            Assert.Equal(4, saved.PlayCount);
        }

        [Fact]
        public async Task UpdateScoreAsync_GameSkillNotHigher_ShouldPreserveHighSkill()
        {
            var chart = await SeedChartAsync();
            await SeedScoreAsync(chart.Id, new SongScore
            {
                BestScore = 950000,
                HighSkill = 180.0,
                SongSkill = 180.0,
                PlayCount = 2
            });

            var summary = new PerformanceSummary
            {
                Score = 700000,
                MaxCombo = 50,
                ClearFlag = false,
                PerfectCount = 50,
                MissCount = 50,
                TotalNotes = 100,
                PlayingSkill = 42.5,
                GameSkill = 70.0,
                ChartLevel = 78,
                ChartLevelDec = 33
            };

            await _svc.UpdateScoreAsync(chart.Id, EInstrumentPart.DRUMS, summary);

            var saved = await LoadSavedScoreAsync(chart.Id);
            Assert.Equal(950000, saved.BestScore);
            Assert.Equal(180.0, saved.HighSkill, 4);
            Assert.Equal(70.0, saved.SongSkill, 4);
            Assert.Equal(70.0, saved.LastSkillPoint, 4);
            Assert.Equal(3, saved.PlayCount);
        }

        [Fact]
        public async Task UpdateScoreAsync_ClearWithPoorButNoMiss_FullComboIsFalse()
        {
            var chart = await SeedChartAsync();
            await SeedScoreAsync(chart.Id, new SongScore { PlayCount = 0 });

            var summary = new PerformanceSummary
            {
                Score = 600000,
                MaxCombo = 80,
                ClearFlag = true,
                PerfectCount = 80,
                GreatCount = 0,
                GoodCount = 0,
                PoorCount = 20,
                MissCount = 0,
                TotalNotes = 100,
                PlayingSkill = 72.0,
                GameSkill = 118.0,
                ChartLevel = 78,
                ChartLevelDec = 33
            };

            await _svc.UpdateScoreAsync(chart.Id, EInstrumentPart.DRUMS, summary);

            var saved = await LoadSavedScoreAsync(chart.Id);
            Assert.False(saved.FullCombo);
            Assert.Equal(1, saved.ClearCount);
        }

        [Fact]
        public async Task UpdateScoreAsync_NewBestScoreWithLowerGameSkill_ShouldUpdateBestButNotHighSkill()
        {
            var chart = await SeedChartAsync();
            await SeedScoreAsync(chart.Id, new SongScore
            {
                BestScore = 800000,
                BestPerfect = 80,
                HighSkill = 160.0,
                SongSkill = 140.0,
                PlayCount = 5
            });

            var summary = new PerformanceSummary
            {
                Score = 850000,
                MaxCombo = 85,
                ClearFlag = true,
                PerfectCount = 85,
                GreatCount = 15,
                GoodCount = 0,
                PoorCount = 0,
                MissCount = 0,
                TotalNotes = 100,
                PlayingSkill = 78.625,
                GameSkill = 130.0,
                ChartLevel = 78,
                ChartLevelDec = 33
            };

            await _svc.UpdateScoreAsync(chart.Id, EInstrumentPart.DRUMS, summary);

            var saved = await LoadSavedScoreAsync(chart.Id);
            Assert.Equal(850000, saved.BestScore);
            Assert.Equal(85, saved.BestPerfect);
            Assert.Equal(85, saved.MaxCombo);
            Assert.True(saved.FullCombo);
            Assert.Equal(160.0, saved.HighSkill, 4);
            Assert.Equal(130.0, saved.SongSkill, 4);
            Assert.Equal(130.0, saved.LastSkillPoint, 4);
            Assert.Equal(6, saved.PlayCount);
        }

        [Fact]
        public async Task UpdateScoreAsync_ClearFlagTrue_ShouldIncrementClearCount()
        {
            var chart = await SeedChartAsync();
            await SeedScoreAsync(chart.Id, new SongScore { ClearCount = 2, PlayCount = 3 });

            var summary = new PerformanceSummary
            {
                Score = 500000,
                MaxCombo = 30,
                ClearFlag = true,
                PerfectCount = 30,
                MissCount = 70,
                TotalNotes = 100,
                PlayingSkill = 25.5,
                GameSkill = 40.0,
                ChartLevel = 78,
                ChartLevelDec = 33
            };

            await _svc.UpdateScoreAsync(chart.Id, EInstrumentPart.DRUMS, summary);

            var saved = await LoadSavedScoreAsync(chart.Id);
            Assert.Equal(3, saved.ClearCount);
            Assert.Equal(4, saved.PlayCount);
        }
    }
}
