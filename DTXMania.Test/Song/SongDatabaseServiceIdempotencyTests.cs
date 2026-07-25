using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.Stage.Performance;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SongEntity = DTXMania.Game.Lib.Song.Entities.Song;
using Xunit;

namespace DTXMania.Test.Song
{
    /// <summary>
    /// Coverage for the idempotent score-save path in SongDatabaseService:
    /// receipt-based deduplication, non-default-speed bucket isolation,
    /// and argument validation for the PerformanceSummary-based overload.
    /// </summary>
    [Trait("Category", "Unit")]
    public class SongDatabaseServiceIdempotencyTests : System.IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly DbContextOptions<SongDbContext> _options;
        private readonly SongDatabaseService _svc;

        public SongDatabaseServiceIdempotencyTests()
        {
            _connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
            _connection.Open();

            _options = new DbContextOptionsBuilder<SongDbContext>()
                .UseSqlite(_connection).Options;

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
                var song = new SongEntity { Title = "Idempotency Test" };
                var chart = new SongChart
                {
                    Song = song,
                    FilePath = "idempotency-test.dtx",
                    DrumLevel = 50,
                    DrumLevelDec = 0
                };
                ctx.SongCharts.Add(chart);
                await ctx.SaveChangesAsync();
                return chart;
            }
            finally { ctx.Dispose(); }
        }

        private static PerformanceSummary SavableSummary(
            int playSpeedPercent = PlaySpeedRange.Default,
            int score = 800_000,
            Guid? runId = null)
        {
            return new PerformanceSummary
            {
                RunId = runId ?? Guid.NewGuid(),
                PlaySpeedPercent = playSpeedPercent,
                PitchSemitones = 0,
                CompletionReason = CompletionReason.SongComplete,
                ClearFlag = true,
                Score = score,
                MaxCombo = 100,
                PerfectCount = 80,
                GreatCount = 15,
                GoodCount = 3,
                PoorCount = 1,
                MissCount = 1,
                GameSkill = 85.0,
                PlayingSkill = 80.0,
                TotalNotes = 100,
            };
        }

        private async Task<ScoreSaveReceipt?> LoadReceiptAsync(Guid runId)
        {
            var ctx = new SongDbContext(_options);
            try
            {
                return await ctx.ScoreSaveReceipts
                    .AsNoTracking()
                    .FirstOrDefaultAsync(r => r.RunId == runId);
            }
            finally { ctx.Dispose(); }
        }

        private async Task<SongScore?> LoadScoreAsync(int chartId, int playSpeedPercent)
        {
            var ctx = new SongDbContext(_options);
            try
            {
                return await ctx.SongScores
                    .AsNoTracking()
                    .FirstOrDefaultAsync(s =>
                        s.ChartId == chartId
                        && s.Instrument == EInstrumentPart.DRUMS
                        && s.PlaySpeedPercent == playSpeedPercent);
            }
            finally { ctx.Dispose(); }
        }

        #region Argument Validation

        [Fact]
        public async Task UpdateScoreAsync_WithNullSummary_ThrowsArgumentNullException()
        {
            var chart = await SeedChartAsync();

            await Assert.ThrowsAsync<ArgumentNullException>(
                () => _svc.UpdateScoreAsync(chart.Id, EInstrumentPart.DRUMS, null!));
        }

        [Theory]
        [InlineData(49)]
        [InlineData(151)]
        [InlineData(77)]
        [InlineData(102)]
        public async Task UpdateScoreAsync_WithNonCanonicalPlaySpeed_ThrowsArgumentOutOfRangeException(
            int invalidSpeed)
        {
            var chart = await SeedChartAsync();
            var summary = SavableSummary(playSpeedPercent: invalidSpeed);

            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
                () => _svc.UpdateScoreAsync(chart.Id, EInstrumentPart.DRUMS, summary));
        }

        #endregion

        #region Idempotency / Receipt

        [Fact]
        public async Task UpdateScoreAsync_SameRunIdTwice_SecondReturnsAlreadySaved()
        {
            var chart = await SeedChartAsync();
            var runId = Guid.NewGuid();
            var summary = SavableSummary(runId: runId);

            var first = await _svc.UpdateScoreAsync(
                chart.Id, EInstrumentPart.DRUMS, summary);
            var second = await _svc.UpdateScoreAsync(
                chart.Id, EInstrumentPart.DRUMS, summary);

            Assert.Equal(ScoreSaveStatus.Saved, first.Status);
            Assert.True(first.IsSuccess);
            Assert.Equal(ScoreSaveStatus.AlreadySaved, second.Status);
            Assert.True(second.IsSuccess);
        }

        [Fact]
        public async Task UpdateScoreAsync_PersistsScoreSaveReceipt()
        {
            var chart = await SeedChartAsync();
            var runId = Guid.NewGuid();
            var summary = SavableSummary(runId: runId, playSpeedPercent: 75);

            await _svc.UpdateScoreAsync(chart.Id, EInstrumentPart.DRUMS, summary);

            var receipt = await LoadReceiptAsync(runId);
            Assert.NotNull(receipt);
            Assert.Equal(chart.Id, receipt!.ChartId);
            Assert.Equal(EInstrumentPart.DRUMS, receipt.Instrument);
            Assert.Equal(75, receipt.PlaySpeedPercent);
            Assert.NotEqual(default(DateTime), receipt.SavedAtUtc);
        }

        [Fact]
        public async Task UpdateScoreAsync_AlreadySavedReceipt_DoesNotCreateSecondScore()
        {
            var chart = await SeedChartAsync();
            var runId = Guid.NewGuid();
            var summary = SavableSummary(runId: runId, score: 900_000);

            await _svc.UpdateScoreAsync(chart.Id, EInstrumentPart.DRUMS, summary);
            // Retry with different score — should be ignored (AlreadySaved)
            var retrySummary = SavableSummary(runId: runId, score: 950_000);
            var result = await _svc.UpdateScoreAsync(
                chart.Id, EInstrumentPart.DRUMS, retrySummary);

            Assert.Equal(ScoreSaveStatus.AlreadySaved, result.Status);
            // Only one score row should exist
            var ctx = new SongDbContext(_options);
            try
            {
                var count = await ctx.SongScores
                    .CountAsync(s => s.ChartId == chart.Id
                        && s.Instrument == EInstrumentPart.DRUMS
                        && s.PlaySpeedPercent == PlaySpeedRange.Default);
                Assert.Equal(1, count);
            }
            finally { ctx.Dispose(); }
        }

        #endregion

        #region Non-Default Speed Bucket Isolation

        [Fact]
        public async Task UpdateScoreAsync_NonDefaultSpeed_CreatesSeparateScoreRow()
        {
            var chart = await SeedChartAsync();

            // Save at default speed
            await _svc.UpdateScoreAsync(
                chart.Id,
                EInstrumentPart.DRUMS,
                SavableSummary(playSpeedPercent: 100, score: 900_000));

            // Save at 75% speed
            await _svc.UpdateScoreAsync(
                chart.Id,
                EInstrumentPart.DRUMS,
                SavableSummary(playSpeedPercent: 75, score: 750_000));

            var defaultScore = await LoadScoreAsync(chart.Id, 100);
            var slowScore = await LoadScoreAsync(chart.Id, 75);

            Assert.NotNull(defaultScore);
            Assert.NotNull(slowScore);
            Assert.NotEqual(defaultScore!.Id, slowScore!.Id);
            Assert.Equal(900_000, defaultScore.BestScore);
            Assert.Equal(750_000, slowScore.BestScore);
        }

        [Fact]
        public async Task UpdateScoreAsync_SameNonDefaultSpeedTwice_UpdatesExistingRow()
        {
            var chart = await SeedChartAsync();
            var speed = 75;

            await _svc.UpdateScoreAsync(
                chart.Id,
                EInstrumentPart.DRUMS,
                SavableSummary(playSpeedPercent: speed, score: 700_000));

            await _svc.UpdateScoreAsync(
                chart.Id,
                EInstrumentPart.DRUMS,
                SavableSummary(playSpeedPercent: speed, score: 850_000));

            var ctx = new SongDbContext(_options);
            try
            {
                var scores = await ctx.SongScores
                    .AsNoTracking()
                    .Where(s => s.ChartId == chart.Id
                        && s.Instrument == EInstrumentPart.DRUMS
                        && s.PlaySpeedPercent == speed)
                    .ToListAsync();
                Assert.Single(scores);
                Assert.Equal(850_000, scores[0].BestScore);
            }
            finally { ctx.Dispose(); }
        }

        #endregion

        #region Cancellation

        [Fact]
        public async Task UpdateScoreAsync_WithCancelledToken_ThrowsOperationCanceledException()
        {
            var chart = await SeedChartAsync();
            var summary = SavableSummary();
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => _svc.UpdateScoreAsync(
                    chart.Id,
                    EInstrumentPart.DRUMS,
                    summary,
                    cts.Token));
        }

        #endregion

        #region Legacy RunId (Empty Guid)

        [Fact]
        public async Task UpdateScoreAsync_WithEmptyRunId_GeneratesNewRunId()
        {
            var chart = await SeedChartAsync();
            var summary = SavableSummary(runId: Guid.Empty);

            // Should not throw — legacy callers with empty RunId get a generated one
            var result = await _svc.UpdateScoreAsync(
                chart.Id, EInstrumentPart.DRUMS, summary);

            Assert.True(result.IsSuccess);
            // A receipt should exist with a non-empty RunId
            var ctx = new SongDbContext(_options);
            try
            {
                var receiptCount = await ctx.ScoreSaveReceipts
                    .CountAsync(r => r.ChartId == chart.Id);
                Assert.Equal(1, receiptCount);
            }
            finally { ctx.Dispose(); }
        }

        #endregion
    }
}
