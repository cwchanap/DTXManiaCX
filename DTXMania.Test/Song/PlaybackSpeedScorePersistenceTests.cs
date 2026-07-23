using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.Stage.Performance;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;
using SongEntity = DTXMania.Game.Lib.Song.Entities.Song;

namespace DTXMania.Test.Song
{
    [Trait("Category", "Integration")]
    public sealed class PlaybackSpeedScorePersistenceTests : IDisposable
    {
        private readonly string _dbPath;
        private readonly SongDatabaseService _service;

        public PlaybackSpeedScorePersistenceTests()
        {
            _dbPath = Path.Combine(
                Path.GetTempPath(),
                $"speed_persistence_{Guid.NewGuid()}.db");
            _service = new SongDatabaseService(_dbPath);
        }

        public void Dispose()
        {
            _service.Dispose();
            if (File.Exists(_dbPath))
            {
                try { File.Delete(_dbPath); } catch { /* ignore */ }
            }
        }

        [Fact]
        public async Task DistinctRunsAtSameSpeed_ShouldUpdateOneAggregateOnceEach()
        {
            var chartId = await InitializeAndSeedChartAsync();

            var first = await _service.UpdateScoreAsync(
                chartId,
                EInstrumentPart.DRUMS,
                Summary(Guid.NewGuid(), 100, 0, 600000));
            var second = await _service.UpdateScoreAsync(
                chartId,
                EInstrumentPart.DRUMS,
                Summary(Guid.NewGuid(), 100, 2, 700000));

            Assert.Equal(ScoreSaveStatus.Saved, first.Status);
            Assert.Equal(ScoreSaveStatus.Saved, second.Status);
            using var context = _service.CreateContext();
            var score = await context.SongScores
                .Include(saved => saved.PerformanceHistory)
                .SingleAsync(saved => saved.ChartId == chartId);
            Assert.Equal(100, score.PlaySpeedPercent);
            Assert.Equal(2, score.PlayCount);
            Assert.Equal(2, score.PerformanceHistory.Count);
            Assert.Equal(2, await context.ScoreSaveReceipts.CountAsync());
        }

        [Fact]
        public async Task DifferentSpeeds_ShouldCreateIndependentAggregates()
        {
            var chartId = await InitializeAndSeedChartAsync();

            await _service.UpdateScoreAsync(
                chartId,
                EInstrumentPart.DRUMS,
                Summary(Guid.NewGuid(), 75, -2, 750000));
            await _service.UpdateScoreAsync(
                chartId,
                EInstrumentPart.DRUMS,
                Summary(Guid.NewGuid(), 100, 3, 500000));

            using var context = _service.CreateContext();
            var variants = await context.SongScores
                .Include(score => score.PerformanceHistory)
                .Where(score => score.ChartId == chartId)
                .OrderBy(score => score.PlaySpeedPercent)
                .ToListAsync();
            Assert.Equal(new[] { 75, 100 }, variants
                .Select(score => score.PlaySpeedPercent)
                .ToArray());
            Assert.All(variants, score => Assert.Equal(1, score.PlayCount));
            Assert.Equal(
                -2,
                Assert.Single(variants[0].PerformanceHistory).PitchSemitones);
            Assert.Equal(
                3,
                Assert.Single(variants[1].PerformanceHistory).PitchSemitones);
        }

        [Fact]
        public async Task DifferentPitchesAtSameSpeed_ShouldRetainStructuredAndHumanIdentity()
        {
            var chartId = await InitializeAndSeedChartAsync();

            await _service.UpdateScoreAsync(
                chartId,
                EInstrumentPart.DRUMS,
                Summary(Guid.NewGuid(), 75, -3, 500000));
            await _service.UpdateScoreAsync(
                chartId,
                EInstrumentPart.DRUMS,
                Summary(Guid.NewGuid(), 75, 5, 600000));

            using var context = _service.CreateContext();
            var score = await context.SongScores
                .Include(saved => saved.PerformanceHistory)
                .SingleAsync(saved => saved.ChartId == chartId);
            var history = score.PerformanceHistory
                .OrderBy(row => row.PerformedAt)
                .ToList();
            Assert.Equal(new[] { -3, 5 }, history
                .Select(row => row.PitchSemitones)
                .ToArray());
            Assert.All(history, row => Assert.Contains("0.75x", row.HistoryLine));
            Assert.Contains(history, row => row.HistoryLine.Contains("-3 st"));
            Assert.Contains(history, row => row.HistoryLine.Contains("+5 st"));
        }

        [Fact]
        public async Task RepeatedRunId_ShouldReturnAlreadySavedWithoutSecondMutation()
        {
            var chartId = await InitializeAndSeedChartAsync();
            var runId = Guid.NewGuid();
            var summary = Summary(runId, 100, 0, 700000);

            var first = await _service.UpdateScoreAsync(
                chartId,
                EInstrumentPart.DRUMS,
                summary);
            var repeated = await _service.UpdateScoreAsync(
                chartId,
                EInstrumentPart.DRUMS,
                summary);

            Assert.Equal(ScoreSaveStatus.Saved, first.Status);
            Assert.Equal(ScoreSaveStatus.AlreadySaved, repeated.Status);
            using var context = _service.CreateContext();
            Assert.Equal(1, await context.SongScores.SumAsync(score => score.PlayCount));
            Assert.Equal(1, await context.PerformanceHistory.CountAsync());
            Assert.Equal(1, await context.ScoreSaveReceipts.CountAsync());
        }

        [Fact]
        public async Task OldRunId_AfterHistoryRotation_ShouldRemainAlreadySaved()
        {
            var chartId = await InitializeAndSeedChartAsync();
            var oldRunId = Guid.NewGuid();
            var oldSummary = Summary(oldRunId, 100, 0, 100000);
            await _service.UpdateScoreAsync(
                chartId,
                EInstrumentPart.DRUMS,
                oldSummary);
            for (var index = 1; index <= 6; index++)
            {
                await _service.UpdateScoreAsync(
                    chartId,
                    EInstrumentPart.DRUMS,
                    Summary(Guid.NewGuid(), 100, index, 100000 + index));
            }

            var repeated = await _service.UpdateScoreAsync(
                chartId,
                EInstrumentPart.DRUMS,
                oldSummary);

            Assert.Equal(ScoreSaveStatus.AlreadySaved, repeated.Status);
            using var context = _service.CreateContext();
            var score = await context.SongScores.SingleAsync();
            Assert.Equal(7, score.PlayCount);
            Assert.Equal(5, await context.PerformanceHistory.CountAsync());
            Assert.Equal(7, await context.ScoreSaveReceipts.CountAsync());
        }

        [Fact]
        public async Task OldRunId_AfterScoreCleanup_ShouldUseStoredIdentityAndDetectCollision()
        {
            var chartId = await InitializeAndSeedChartAsync();
            var runId = Guid.NewGuid();
            var summary = Summary(runId, 100, 0, 700000);
            await _service.UpdateScoreAsync(
                chartId,
                EInstrumentPart.DRUMS,
                summary);
            await ExecuteSqlAsync(
                "DELETE FROM SongScores WHERE PlaySpeedPercent=100");

            var matching = await _service.UpdateScoreAsync(
                chartId,
                EInstrumentPart.DRUMS,
                summary);
            var collision = await _service.UpdateScoreAsync(
                chartId,
                EInstrumentPart.DRUMS,
                Summary(runId, 75, 0, 700000));

            Assert.Equal(ScoreSaveStatus.AlreadySaved, matching.Status);
            Assert.Null(matching.SongScoreId);
            Assert.Equal(ScoreSaveStatus.Failed, collision.Status);
            Assert.Contains("RunId collision", collision.ErrorMessage);
            using var context = _service.CreateContext();
            var receipt = await context.ScoreSaveReceipts.SingleAsync();
            Assert.Null(receipt.SongScoreId);
            Assert.Empty(context.SongScores);
        }

        [Fact]
        public async Task HistoryFailure_ShouldRollbackScoreHistoryAndReceiptTogether()
        {
            var chartId = await InitializeAndSeedChartAsync();
            using (var context = _service.CreateContext())
            {
                context.SongScores.Add(new SongScore
                {
                    ChartId = chartId,
                    Instrument = EInstrumentPart.DRUMS,
                    PlaySpeedPercent = 100,
                    BestScore = 400000,
                    PlayCount = 3
                });
                await context.SaveChangesAsync();
            }
            await ExecuteSqlAsync(
                "PRAGMA foreign_keys=OFF; DELETE FROM Songs; PRAGMA foreign_keys=ON;");

            await Assert.ThrowsAnyAsync<DbUpdateException>(() =>
                _service.UpdateScoreAsync(
                    chartId,
                    EInstrumentPart.DRUMS,
                    Summary(Guid.NewGuid(), 100, 1, 900000)));

            using var verify = _service.CreateContext();
            var score = await verify.SongScores.AsNoTracking().SingleAsync();
            Assert.Equal(400000, score.BestScore);
            Assert.Equal(3, score.PlayCount);
            Assert.Empty(verify.PerformanceHistory);
            Assert.Empty(verify.ScoreSaveReceipts);
        }

        [Fact]
        public async Task ConcurrentSameRunId_ShouldCommitExactlyOnce()
        {
            var chartId = await InitializeAndSeedChartAsync();
            var summary = Summary(Guid.NewGuid(), 100, 2, 800000);

            var results = await Task.WhenAll(
                _service.UpdateScoreAsync(
                    chartId,
                    EInstrumentPart.DRUMS,
                    summary),
                _service.UpdateScoreAsync(
                    chartId,
                    EInstrumentPart.DRUMS,
                    summary));

            Assert.Contains(results, result => result.Status == ScoreSaveStatus.Saved);
            Assert.Contains(results, result => result.Status == ScoreSaveStatus.AlreadySaved);
            using var context = _service.CreateContext();
            Assert.Equal(1, await context.SongScores.SumAsync(score => score.PlayCount));
            Assert.Equal(1, await context.PerformanceHistory.CountAsync());
            Assert.Equal(1, await context.ScoreSaveReceipts.CountAsync());
        }

        [Fact]
        public async Task PrimitiveCompatibilityWrite_ShouldUpdateOnlyDefaultSpeed()
        {
            var chartId = await InitializeAndSeedChartAsync();
            using (var context = _service.CreateContext())
            {
                context.SongScores.AddRange(
                    new SongScore
                    {
                        ChartId = chartId,
                        Instrument = EInstrumentPart.DRUMS,
                        PlaySpeedPercent = 75
                    },
                    new SongScore
                    {
                        ChartId = chartId,
                        Instrument = EInstrumentPart.DRUMS,
                        PlaySpeedPercent = 100
                    });
                await context.SaveChangesAsync();
            }

            await _service.UpdateScoreAsync(
                chartId,
                EInstrumentPart.DRUMS,
                newScore: 123456,
                achievementRate: 12.34,
                fullCombo: false);

            using var verify = _service.CreateContext();
            var variants = await verify.SongScores
                .OrderBy(score => score.PlaySpeedPercent)
                .ToListAsync();
            Assert.Equal(0, variants[0].PlayCount);
            Assert.Equal(1, variants[1].PlayCount);
            Assert.Equal(123456, variants[1].LastScore);
        }

        private async Task<int> InitializeAndSeedChartAsync()
        {
            await _service.InitializeDatabaseAsync();
            using var context = _service.CreateContext();
            var song = new SongEntity
            {
                Title = "Persistence",
                Artist = "Test"
            };
            var chart = new SongChart
            {
                Song = song,
                FilePath = $"/persistence/{Guid.NewGuid():N}.dtx",
                HasDrumChart = true,
                DrumLevel = 50
            };
            context.SongCharts.Add(chart);
            await context.SaveChangesAsync();
            return chart.Id;
        }

        private static PerformanceSummary Summary(
            Guid runId,
            int speed,
            int pitch,
            int score)
        {
            return new PerformanceSummary
            {
                RunId = runId,
                PlaySpeedPercent = speed,
                PitchSemitones = pitch,
                Score = score,
                MaxCombo = 90,
                ClearFlag = true,
                PerfectCount = 90,
                GreatCount = 10,
                TotalNotes = 100,
                PlayingSkill = 95.0,
                GameSkill = 150.0
            };
        }

        private async Task ExecuteSqlAsync(string sql)
        {
            using var connection = new SqliteConnection(
                $"Data Source={_dbPath};Foreign Keys=True;Pooling=False");
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync();
        }
    }
}