using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Stage;
using DTXMania.Game.Lib.Stage.Performance;
using DTXMania.Test.TestData;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Win32.SafeHandles;
using Xunit;
using SongEntity = DTXMania.Game.Lib.Song.Entities.Song;
using SongScoreEntity = DTXMania.Game.Lib.Song.Entities.SongScore;
namespace DTXMania.Test.Song
{
    /// <summary>
    /// Unit tests for SongDatabase class
    /// Tests song grouping functionality and database operations
    /// </summary>
    [Trait("Category", "Unit")]
    public class SongDatabaseTests : IDisposable
    {
        private readonly SongDatabase _databaseService;
        private readonly string _testDbPath;
        private readonly string _tempRoot = Path.Combine(
            Path.GetTempPath(),
            "DTXManiaCX_Tests",
            nameof(SongDatabaseTests),
            Guid.NewGuid().ToString("N"));

        public SongDatabaseTests()
        {
            // Isolate chart files and the database per test and per process.
            Directory.CreateDirectory(_tempRoot);
            _testDbPath = Path.Combine(_tempRoot, "songs.db");
            _databaseService = new SongDatabase(_testDbPath);
        }

        public void Dispose()
        {
            // Clean up after each test
            _databaseService?.Dispose();

            try
            {
                if (Directory.Exists(_tempRoot))
                {
                    Directory.Delete(_tempRoot, true);
                }
            }
            catch
            {
                // Best-effort cleanup for temp test assets.
            }
        }

        [Fact]
        public async Task InitializeDatabaseAsync_WhenFresh_ShouldCreateDatabase()
        {
            await _databaseService.InitializeDatabaseAsync();

            Assert.True(await _databaseService.CanConnectAsync());
        }

        [Fact]
        public async Task InitializeDatabaseAsync_WhenPreinitialized_ShouldReuseDatabase()
        {
            using (var initializer = new SongDatabase(_testDbPath))
            {
                await initializer.InitializeDatabaseAsync();
            }
            using var service = new SongDatabase(_testDbPath);

            await service.InitializeDatabaseAsync();

            Assert.True(await service.CanConnectAsync());
        }

        [Fact]
        public async Task InitializeDatabaseAsync_WhenInvalidFileRecovered_ShouldRecreateDatabase()
        {
            await File.WriteAllTextAsync(_testDbPath, "not a SQLite database");

            await _databaseService.InitializeDatabaseAsync();

            Assert.True(await _databaseService.CanConnectAsync());
        }

        [Fact]
        public async Task InitializeDatabaseAsync_WhenEnsureCreatedThrowsNotDatabase_ShouldPropagate()
        {
            await using var fixture = await SqliteInitializationFixture.CreateAsync();
            using var service = new ScriptedInitializationService(
                fixture.Options,
                fixture.CreateContext,
                (_, _) => throw new InvalidOperationException(
                    "file is not a database"));

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.InitializeDatabaseAsync());

            Assert.Contains("file is not a database", exception.Message);
            Assert.Throws<InvalidOperationException>(service.CreateContext);
        }

        [Fact]
        public async Task InitializeDatabaseAsync_WhenEnsureCreatedReportsExistingTables_ShouldPropagate()
        {
            await using var fixture = await SqliteInitializationFixture.CreateAsync();
            using var service = new ScriptedInitializationService(
                fixture.Options,
                fixture.CreateContext,
                (_, _) => throw new InvalidOperationException(
                    "table Songs already exists"));

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.InitializeDatabaseAsync());

            Assert.Contains("already exists", exception.Message);
            Assert.Throws<InvalidOperationException>(service.CreateContext);
        }

        [Fact]
        public async Task InitializeDatabaseAsync_WhenEnumerationMetadataCreationFails_ShouldPropagate()
        {
            var interceptor = new ThrowingCommandInterceptor(
                commandText => commandText.Contains(
                    "__EnumerationMetadata",
                    StringComparison.Ordinal),
                "enumeration metadata failure");
            await using var fixture =
                await SqliteInitializationFixture.CreateAsync(interceptor);
            using var service = new ScriptedInitializationService(
                fixture.Options,
                fixture.CreateContext);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.InitializeDatabaseAsync());

            Assert.Contains("enumeration metadata failure", exception.ToString());
            Assert.Throws<InvalidOperationException>(service.CreateContext);

            await using var verify = fixture.CreateContext();
            var connection = verify.Database.GetDbConnection();
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version;";
            Assert.Equal(0L, (long)(await command.ExecuteScalarAsync())!);
        }

        private sealed class ScriptedInitializationService : SongDatabase
        {
            private readonly Func<SongDbContext> _initializationContextFactory;
            private readonly Func<SongDbContext, int, Task> _ensureCreated;
            private int _ensureAttempts;

            public ScriptedInitializationService(
                DbContextOptions<SongDbContext> options,
                Func<SongDbContext> contextFactory,
                Func<SongDbContext, int, Task>? ensureCreated = null)
                : base(options, contextFactory, initialized: false)
            {
                _initializationContextFactory = contextFactory;
                _ensureCreated = ensureCreated ??
                    ((context, _) => context.Database.EnsureCreatedAsync());
            }

            internal override bool InitializationDatabaseFileExists() => false;

            internal override SongDbContext CreateInitializationContext() =>
                _initializationContextFactory();

            internal override Task EnsureCreatedForInitializationAsync(
                SongDbContext context) =>
                _ensureCreated(context, Interlocked.Increment(ref _ensureAttempts));
        }

        private sealed class SqliteInitializationFixture : IAsyncDisposable
        {
            private readonly SqliteConnection _connection;

            private SqliteInitializationFixture(
                SqliteConnection connection,
                DbContextOptions<SongDbContext> options)
            {
                _connection = connection;
                Options = options;
            }

            public DbContextOptions<SongDbContext> Options { get; }

            public SongDbContext CreateContext() => new(Options);

            public static async Task<SqliteInitializationFixture> CreateAsync(
                DbCommandInterceptor? interceptor = null)
            {
                var connection = new SqliteConnection("Data Source=:memory:");
                await connection.OpenAsync();
                var builder = new DbContextOptionsBuilder<SongDbContext>()
                    .UseSqlite(connection);
                if (interceptor is not null)
                {
                    builder.AddInterceptors(interceptor);
                }

                var fixture =
                    new SqliteInitializationFixture(connection, builder.Options);
                await using var context = fixture.CreateContext();
                await context.Database.EnsureCreatedAsync();
                return fixture;
            }

            public async ValueTask DisposeAsync()
            {
                await _connection.DisposeAsync();
            }
        }

        private sealed class ThrowingCommandInterceptor : DbCommandInterceptor
        {
            private readonly Func<string, bool> _matches;
            private readonly string _message;

            public ThrowingCommandInterceptor(
                Func<string, bool> matches,
                string message)
            {
                _matches = matches;
                _message = message;
            }

            public override async ValueTask<InterceptionResult<DbDataReader>>
                ReaderExecutingAsync(
                    DbCommand command,
                    CommandEventData eventData,
                    InterceptionResult<DbDataReader> result,
                    CancellationToken cancellationToken = default)
            {
                await ThrowIfMatchedAsync(command, cancellationToken);
                return result;
            }

            public override async ValueTask<InterceptionResult<int>>
                NonQueryExecutingAsync(
                    DbCommand command,
                    CommandEventData eventData,
                    InterceptionResult<int> result,
                    CancellationToken cancellationToken = default)
            {
                await ThrowIfMatchedAsync(command, cancellationToken);
                return result;
            }

            private async Task ThrowIfMatchedAsync(
                DbCommand command,
                CancellationToken cancellationToken)
            {
                if (!_matches(command.CommandText))
                    return;

                await Task.Delay(5, cancellationToken);
                throw new InvalidOperationException(_message);
            }
        }


        [Fact]
        public void CreateContext_BeforeInitialization_ShouldThrowInvalidOperationException()
        {
            Assert.Throws<InvalidOperationException>(() => _databaseService.CreateContext());
        }

        [Fact]
        public async Task InitializeDatabaseAsync_WithInvalidDatabaseFile_ShouldRecreateValidDatabase()
        {
            await File.WriteAllTextAsync(_testDbPath, "not a sqlite database");

            await _databaseService.InitializeDatabaseAsync();

            Assert.True(await _databaseService.CanConnectAsync());

            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            await using var connection = new Microsoft.Data.Sqlite.SqliteConnection(
                $"Data Source={_testDbPath};Pooling=False");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version;";
            Assert.Equal(
                (long)SongDatabase.CurrentSongSchemaVersion,
                (long)(await command.ExecuteScalarAsync())!);
        }

        [Fact]
        public async Task PurgeDatabaseAsync_ShouldDeleteDatabaseAndRequireReinitialization()
        {
            await _databaseService.InitializeDatabaseAsync();

            Assert.True(File.Exists(_testDbPath));

            await _databaseService.PurgeDatabaseAsync();

            Assert.False(File.Exists(_testDbPath));
            Assert.Throws<InvalidOperationException>(() => _databaseService.CreateContext());
        }

        [Fact]
        public async Task GetSongsAsync_ShouldIncludeScoreScopedPerformanceHistory()
        {
            await _databaseService.InitializeDatabaseAsync();

            var (songId, chart) = await AddSongAsync("History Song", "Coverage Bot", "history-song.dtx", drumLevel: 55);

            // The live PerformanceSummary save records a score-scoped history row.
            await SaveScoreAsync(chart.Id, 850_000, 88.0, fullCombo: false);
            var savedScore = await LoadScoreAsync(chart.Id, EInstrumentPart.DRUMS);

            var songs = await new SongCatalogStore(_databaseService).GetSongsAsync();

            var loadedSong = Assert.Single(songs, song => song.Id == songId);
            var loadedChart = Assert.Single(loadedSong.Charts);
            var loadedScore = Assert.Single(loadedChart.Scores);
            // The score's history must be eagerly loaded and scoped to this score row.
            var history = Assert.Single(loadedScore.PerformanceHistory);
            Assert.Equal(savedScore.Id, history.SongScoreId);
            Assert.NotEmpty(history.HistoryLine);
        }

        [Fact]
        public async Task UpdateScoreAsync_WhenNewBestScoreSubmitted_ShouldUpdateBestLastAndCounters()
        {
            await _databaseService.InitializeDatabaseAsync();
            var (_, chart) = await AddSongAsync("High Score", "Coverage Bot", "high-score.dtx", drumLevel: 75);

            await SaveScoreAsync(chart.Id, 900_000, 95.5, fullCombo: true);

            var score = await LoadScoreAsync(chart.Id, EInstrumentPart.DRUMS);

            Assert.Equal(900_000, score.BestScore);
            Assert.Equal(900_000, score.LastScore);
            Assert.Equal(95.5, score.BestAchievementRate);
            Assert.True(score.FullCombo);
            Assert.Equal(1, score.PlayCount);
            Assert.Equal(1, score.ClearCount);
            Assert.NotNull(score.LastPlayedAt);
        }

        [Fact]
        public async Task UpdateScoreAsync_WhenScoreIsNotANewBest_ShouldPreserveBestButAdvanceLastAndCounts()
        {
            await _databaseService.InitializeDatabaseAsync();
            var (_, chart) = await AddSongAsync("Score History", "Coverage Bot", "score-history.dtx", drumLevel: 70);

            await SaveScoreAsync(chart.Id, 900_000, 95.5, fullCombo: true);
            await SaveScoreAsync(chart.Id, 500_000, 55.0, fullCombo: false);

            var score = await LoadScoreAsync(chart.Id, EInstrumentPart.DRUMS);

            Assert.Equal(900_000, score.BestScore);
            Assert.Equal(95.5, score.BestAchievementRate);
            Assert.True(score.FullCombo);
            Assert.Equal(500_000, score.LastScore);
            Assert.Equal(2, score.PlayCount);
            Assert.Equal(2, score.ClearCount);
        }

        [Fact]
        public async Task UpdateScoreAsync_NewBestScoreWithoutFullCombo_ShouldPreserveExistingFullCombo()
        {
            // Regression: the older overload also unconditionally overwrote FullCombo on new best.
            await _databaseService.InitializeDatabaseAsync();
            var (_, chart) = await AddSongAsync("FC Regression", "Coverage Bot", "fc-regression.dtx", drumLevel: 70);

            await SaveScoreAsync(chart.Id, 900_000, 95.5, fullCombo: true);
            await SaveScoreAsync(chart.Id, 950_000, 96.0, fullCombo: false);

            var score = await LoadScoreAsync(chart.Id, EInstrumentPart.DRUMS);

            Assert.Equal(950_000, score.BestScore);
            Assert.True(score.FullCombo);  // preserved from the first clear, not overwritten
        }

        [Fact]
        public async Task GetTopScoresForSpeedAsync_ShouldReturnScoresSortedByBestScoreAndRespectLimit()
        {
            await _databaseService.InitializeDatabaseAsync();

            var (_, firstChart) = await AddSongAsync("First", "Coverage Bot", "first.dtx", drumLevel: 50);
            var (_, secondChart) = await AddSongAsync("Second", "Coverage Bot", "second.dtx", drumLevel: 50);
            var (_, thirdChart) = await AddSongAsync("Third", "Coverage Bot", "third.dtx", drumLevel: 50);

            await SaveScoreAsync(firstChart.Id, 600_000, 60.0, fullCombo: false);
            await SaveScoreAsync(secondChart.Id, 950_000, 98.0, fullCombo: true);
            await SaveScoreAsync(thirdChart.Id, 800_000, 83.0, fullCombo: false);

            var topScores = await new SongScoreStore(_databaseService).GetTopScoresForSpeedAsync(EInstrumentPart.DRUMS, PlaySpeedRange.Default, 2);

            Assert.Equal(2, topScores.Count);
            Assert.Equal(950_000, topScores[0].BestScore);
            Assert.Equal("Second", topScores[0].Chart.Song.Title);
            Assert.Equal(800_000, topScores[1].BestScore);
            Assert.Equal("Third", topScores[1].Chart.Song.Title);
        }

        private async Task<(int SongId, SongChart Chart)> AddSongAsync(
            string title,
            string artist,
            string fileName,
            int drumLevel = 40)
        {
            var song = CreateSong(title, artist);
            var chart = CreateChart(fileName, drumLevel);
            var songId = await _databaseService.SeedSongAsync(song, chart);
            return (songId, chart);
        }

        /// <summary>
        /// Saves one play through the live PerformanceSummary overload. Replaces
        /// the removed boolean UpdateScoreAsync overload in score-accounting tests:
        /// same best/last/counters semantics (BestAchievementRate follows the
        /// high-skill record, FullCombo = cleared with no miss/poor).
        /// </summary>
        private async Task<ScoreSaveResult> SaveScoreAsync(
            int chartId,
            int score,
            double achievementRate,
            bool fullCombo)
        {
            return await new SongScoreStore(_databaseService).UpdateScoreAsync(
                chartId,
                EInstrumentPart.DRUMS,
                new PerformanceSummary
                {
                    RunId = Guid.NewGuid(),
                    PlaySpeedPercent = 100,
                    Score = score,
                    MaxCombo = 100,
                    ClearFlag = true,
                    PerfectCount = fullCombo ? 100 : 80,
                    GreatCount = fullCombo ? 0 : 15,
                    GoodCount = 0,
                    PoorCount = 0,
                    MissCount = fullCombo ? 0 : 5,
                    TotalNotes = 100,
                    PlayingSkill = achievementRate,
                    GameSkill = achievementRate,
                    ChartLevel = 50,
                    CompletionReason = CompletionReason.SongComplete,
                });
        }

        private SongEntity CreateSong(string title, string artist)
        {
            return new SongEntity
            {
                Title = title,
                Artist = artist,
                Genre = "Coverage"
            };
        }

        private SongChart CreateChart(string fileName, int drumLevel)
        {
            var filePath = Path.Combine(_tempRoot, fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            File.WriteAllText(filePath, "#TITLE:Coverage");

            return new SongChart
            {
                FilePath = filePath,
                Duration = 123.45,
                Bpm = 180.0,
                DrumLevel = drumLevel,
                HasDrumChart = drumLevel > 0
            };
        }

        private async Task<SongScoreEntity> LoadScoreAsync(int chartId, EInstrumentPart instrument)
        {
            using var context = _databaseService.CreateContext();
            return await context.SongScores.SingleAsync(score => score.ChartId == chartId && score.Instrument == instrument);
        }
    }
}
