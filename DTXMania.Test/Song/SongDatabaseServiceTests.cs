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
using DTXMania.Game.Lib.Stage;
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
    /// Unit tests for SongDatabaseService class
    /// Tests song grouping functionality and database operations
    /// </summary>
    [Trait("Category", "Unit")]
    public class SongDatabaseServiceTests : IDisposable
    {
        private readonly SongDatabaseService _databaseService;
        private readonly string _testDbPath;
        private readonly string _tempRoot = Path.Combine(
            Path.GetTempPath(),
            "DTXManiaCX_Tests",
            nameof(SongDatabaseServiceTests),
            Guid.NewGuid().ToString("N"));

        public SongDatabaseServiceTests()
        {
            // Isolate chart files and the database per test and per process.
            Directory.CreateDirectory(_tempRoot);
            _testDbPath = Path.Combine(_tempRoot, "songs.db");
            _databaseService = new SongDatabaseService(_testDbPath);
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

            Assert.True(await _databaseService.DatabaseExistsAsync());
        }

        [Fact]
        public async Task InitializeDatabaseAsync_WhenPreinitialized_ShouldReuseDatabase()
        {
            using (var initializer = new SongDatabaseService(_testDbPath))
            {
                await initializer.InitializeDatabaseAsync();
            }
            using var service = new SongDatabaseService(_testDbPath);

            await service.InitializeDatabaseAsync();

            Assert.True(await service.DatabaseExistsAsync());
        }

        [Fact]
        public async Task InitializeDatabaseAsync_WhenInvalidFileRecovered_ShouldRecreateDatabase()
        {
            await File.WriteAllTextAsync(_testDbPath, "not a SQLite database");

            await _databaseService.InitializeDatabaseAsync();

            Assert.True(await _databaseService.DatabaseExistsAsync());
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

        #region Song Grouping Tests

        [Fact]
        public async Task LegacyAddSongAsync_WithSameTitleAndArtist_ShouldGroupChartsIntoSingleSong()
        {
            // Arrange
            await _databaseService.InitializeDatabaseAsync();

            var song1 = new DTXMania.Game.Lib.Song.Entities.Song
            {
                Title = "Test Song",
                Artist = "Test Artist",
                Genre = "Test Genre"
            };

            var song2 = new DTXMania.Game.Lib.Song.Entities.Song
            {
                Title = "Test Song", // Same title
                Artist = "Test Artist", // Same artist
                Genre = "Test Genre"
            };

            var chart1 = new SongChart
            {
                FilePath = "/path/to/bas.dtx",
                Duration = 120.5,
                Bpm = 140,
                DrumLevel = 30,
                HasDrumChart = true
            };

            var chart2 = new SongChart
            {
                FilePath = "/path/to/adv.dtx",
                Duration = 180.7,
                Bpm = 140,
                DrumLevel = 50,
                HasDrumChart = true
            };

            // Act
            var songId1 = await _databaseService.AddSongAsync(song1, chart1);
            var songId2 = await _databaseService.AddSongAsync(song2, chart2);

            // Assert
            Assert.Equal(songId1, songId2); // Should return the same song ID

            // Verify database has only one song with two charts
            var songs = await _databaseService.GetSongsAsync();
            var testSong = songs.Single(s => s.Title == "Test Song");

            Assert.Equal("Test Song", testSong.Title);
            Assert.Equal("Test Artist", testSong.Artist);
            Assert.Equal(2, testSong.Charts.Count);

            // Verify charts have different file paths and properties
            var chartPaths = testSong.Charts.Select(c => c.FilePath).ToArray();
            Assert.Contains("/path/to/bas.dtx", chartPaths);
            Assert.Contains("/path/to/adv.dtx", chartPaths);

            var chart1Retrieved = testSong.Charts.Single(c => c.FilePath == "/path/to/bas.dtx");
            var chart2Retrieved = testSong.Charts.Single(c => c.FilePath == "/path/to/adv.dtx");

            Assert.Equal(120.5, chart1Retrieved.Duration);
            Assert.Equal(180.7, chart2Retrieved.Duration);
            Assert.Equal(30, chart1Retrieved.DrumLevel);
            Assert.Equal(50, chart2Retrieved.DrumLevel);
        }

        [Fact]
        public async Task LegacyAddSongAsync_WithDifferentTitles_ShouldCreateSeparateSongs()
        {
            // Arrange
            await _databaseService.InitializeDatabaseAsync();

            var song1 = new DTXMania.Game.Lib.Song.Entities.Song
            {
                Title = "Song One",
                Artist = "Same Artist",
                Genre = "Test Genre"
            };

            var song2 = new DTXMania.Game.Lib.Song.Entities.Song
            {
                Title = "Song Two", // Different title
                Artist = "Same Artist", // Same artist
                Genre = "Test Genre"
            };

            var chart1 = new SongChart
            {
                FilePath = "/path/to/song1.dtx",
                Duration = 120.5,
                Bpm = 140
            };

            var chart2 = new SongChart
            {
                FilePath = "/path/to/song2.dtx",
                Duration = 180.7,
                Bpm = 150
            };

            // Act
            var songId1 = await _databaseService.AddSongAsync(song1, chart1);
            var songId2 = await _databaseService.AddSongAsync(song2, chart2);

            // Assert
            Assert.NotEqual(songId1, songId2); // Should be different songs

            // Verify database has two separate songs
            var songs = await _databaseService.GetSongsAsync();
            var retrievedSongs = songs.Where(s => s.Artist == "Same Artist").ToList();

            Assert.Equal(2, retrievedSongs.Count);

            var song1Retrieved = retrievedSongs.Single(s => s.Title == "Song One");
            var song2Retrieved = retrievedSongs.Single(s => s.Title == "Song Two");

            Assert.Single(song1Retrieved.Charts);
            Assert.Single(song2Retrieved.Charts);
        }

        [Fact]
        public async Task LegacyAddSongAsync_WithDifferentArtists_ShouldCreateSeparateSongs()
        {
            // Arrange
            await _databaseService.InitializeDatabaseAsync();

            var song1 = new DTXMania.Game.Lib.Song.Entities.Song
            {
                Title = "Same Title",
                Artist = "Artist One",
                Genre = "Test Genre"
            };

            var song2 = new DTXMania.Game.Lib.Song.Entities.Song
            {
                Title = "Same Title", // Same title
                Artist = "Artist Two", // Different artist
                Genre = "Test Genre"
            };

            var chart1 = new SongChart
            {
                FilePath = "/path/to/artist1.dtx",
                Duration = 120.5,
                Bpm = 140
            };

            var chart2 = new SongChart
            {
                FilePath = "/path/to/artist2.dtx",
                Duration = 180.7,
                Bpm = 150
            };

            // Act
            var songId1 = await _databaseService.AddSongAsync(song1, chart1);
            var songId2 = await _databaseService.AddSongAsync(song2, chart2);

            // Assert
            Assert.NotEqual(songId1, songId2); // Should be different songs

            // Verify database has two separate songs
            var songs = await _databaseService.GetSongsAsync();
            var retrievedSongs = songs.Where(s => s.Title == "Same Title").ToList();

            Assert.Equal(2, retrievedSongs.Count);

            var song1Retrieved = retrievedSongs.Single(s => s.Artist == "Artist One");
            var song2Retrieved = retrievedSongs.Single(s => s.Artist == "Artist Two");

            Assert.Single(song1Retrieved.Charts);
            Assert.Single(song2Retrieved.Charts);
        }

        [Fact]
        public async Task LegacyAddSongAsync_WithSameFilePath_ShouldReturnExistingSongId()
        {
            // Arrange
            await _databaseService.InitializeDatabaseAsync();

            var song = new DTXMania.Game.Lib.Song.Entities.Song
            {
                Title = "Duplicate File Test",
                Artist = "Test Artist",
                Genre = "Test Genre"
            };

            var chart = new SongChart
            {
                FilePath = "/path/to/duplicate.dtx",
                Duration = 120.5,
                Bpm = 140
            };

            // Act - Add the same song twice
            var songId1 = await _databaseService.AddSongAsync(song, chart);
            var songId2 = await _databaseService.AddSongAsync(song, chart);

            // Assert
            Assert.Equal(songId1, songId2); // Should return the same ID

            // Verify database has only one song with one chart
            var songs = await _databaseService.GetSongsAsync();
            var testSongs = songs.Where(s => s.Title == "Duplicate File Test").ToList();

            Assert.Single(testSongs);
            Assert.Single(testSongs[0].Charts);
        }

        // Regression: when a SET.def difficulty already exists in the DB and is bookmarked,
        // a rescan re-parses a fresh Song entity (IsBookmarked defaults false) for the same
        // chart file path. AddSongAsync must hydrate the persisted id AND bookmark onto the
        // caller's entity, otherwise the in-memory node built from it loses the star marker
        // and the B-key toggle inverts (sets instead of clears).
        [Fact]
        public async Task LegacyAddSongAsync_WithDuplicateBookmarkedSong_HydratesIdAndBookmarkOntoParsedEntity()
        {
            await _databaseService.InitializeDatabaseAsync();

            var song = new DTXMania.Game.Lib.Song.Entities.Song
            {
                Title = "Bookmarked Rescan", Artist = "A"
            };
            var chart = new SongChart
            {
                FilePath = "/dup/bookmarked.dtx", HasDrumChart = true, DrumLevel = 30
            };
            var songId = await _databaseService.AddSongAsync(song, chart);
            await _databaseService.SetBookmarkAsync(songId, true);

            // Simulate a rescan: a freshly parsed entity (Id=0, IsBookmarked=false) for the
            // exact same chart file path.
            var rescannedSong = new DTXMania.Game.Lib.Song.Entities.Song
            {
                Title = "Bookmarked Rescan", Artist = "A"
            };
            var rescannedChart = new SongChart
            {
                FilePath = "/dup/bookmarked.dtx", HasDrumChart = true, DrumLevel = 30
            };
            var returnedId = await _databaseService.AddSongAsync(rescannedSong, rescannedChart);

            Assert.Equal(songId, returnedId);            // same persisted row
            Assert.Equal(songId, rescannedSong.Id);      // id hydrated onto parsed entity
            Assert.True(rescannedSong.IsBookmarked);     // bookmark hydrated from DB
        }

        // Same bug class for the title+artist duplicate branch: a new chart file for an
        // existing (bookmarked) song must hydrate the bookmark onto the parsed entity.
        [Fact]
        public async Task LegacyAddSongAsync_WithNewChartForBookmarkedSong_HydratesIdAndBookmarkOntoParsedEntity()
        {
            await _databaseService.InitializeDatabaseAsync();

            var song = new DTXMania.Game.Lib.Song.Entities.Song
            {
                Title = "Multi Chart Bookmark", Artist = "A"
            };
            var basChart = new SongChart
            {
                FilePath = "/multi/bas.dtx", HasDrumChart = true, DrumLevel = 30
            };
            var songId = await _databaseService.AddSongAsync(song, basChart);
            await _databaseService.SetBookmarkAsync(songId, true);

            // A different chart file for the same title+artist (a new difficulty).
            var rescannedSong = new DTXMania.Game.Lib.Song.Entities.Song
            {
                Title = "Multi Chart Bookmark", Artist = "A"
            };
            var advChart = new SongChart
            {
                FilePath = "/multi/adv.dtx", HasDrumChart = true, DrumLevel = 60
            };
            var returnedId = await _databaseService.AddSongAsync(rescannedSong, advChart);

            Assert.Equal(songId, returnedId);            // grouped into existing song
            Assert.Equal(songId, rescannedSong.Id);      // id hydrated
            Assert.True(rescannedSong.IsBookmarked);     // bookmark hydrated
        }

        [Fact]
        public async Task LegacyAddSongAsync_WithMultipleChartsForSameSong_ShouldMaintainCorrectDurations()
        {
            // Arrange - Simulate "My Hope Is Gone" scenario
            await _databaseService.InitializeDatabaseAsync();

            var song = new DTXMania.Game.Lib.Song.Entities.Song
            {
                Title = "My Hope Is Gone",
                Artist = "GALNERYUS",
                Genre = "Rock"
            };

            var basChart = new SongChart
            {
                FilePath = "/dtx/My Hope Is Gone/bas.dtx",
                Duration = 123.116636039934,
                Bpm = 184.0,
                DrumLevel = 30
            };

            var advChart = new SongChart
            {
                FilePath = "/dtx/My Hope Is Gone/adv.dtx",
                Duration = 123.116636039934,
                Bpm = 184.0,
                DrumLevel = 50
            };

            var fullChart = new SongChart
            {
                FilePath = "/dtx/My Hope Is Gone/full.dtx",
                Duration = 398.326086956521,
                Bpm = 184.0,
                DrumLevel = 70
            };

            // Act
            var songId1 = await _databaseService.AddSongAsync(song, basChart);
            var songId2 = await _databaseService.AddSongAsync(song, advChart);
            var songId3 = await _databaseService.AddSongAsync(song, fullChart);

            // Assert
            Assert.Equal(songId1, songId2);
            Assert.Equal(songId2, songId3);

            // Verify database structure
            var songs = await _databaseService.GetSongsAsync();
            var myHopeIsGone = songs.Single(s => s.Title == "My Hope Is Gone");

            Assert.Equal("GALNERYUS", myHopeIsGone.Artist);
            Assert.Equal(3, myHopeIsGone.Charts.Count);

            // Verify durations are preserved correctly
            var charts = myHopeIsGone.Charts.ToList();
            var basChartRetrieved = charts.Single(c => c.FilePath.EndsWith("bas.dtx"));
            var advChartRetrieved = charts.Single(c => c.FilePath.EndsWith("adv.dtx"));
            var fullChartRetrieved = charts.Single(c => c.FilePath.EndsWith("full.dtx"));

            Assert.Equal(123.116636039934, basChartRetrieved.Duration, 6);
            Assert.Equal(123.116636039934, advChartRetrieved.Duration, 6);
            Assert.Equal(398.326086956521, fullChartRetrieved.Duration, 6);

            // Verify BPM is consistent
            Assert.Equal(184.0, basChartRetrieved.Bpm);
            Assert.Equal(184.0, advChartRetrieved.Bpm);
            Assert.Equal(184.0, fullChartRetrieved.Bpm);

            // Verify difficulty levels
            Assert.Equal(30, basChartRetrieved.DrumLevel);
            Assert.Equal(50, advChartRetrieved.DrumLevel);
            Assert.Equal(70, fullChartRetrieved.DrumLevel);
        }

        #endregion

        private sealed class ScriptedInitializationService : SongDatabaseService
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

            Assert.True(await _databaseService.DatabaseExistsAsync());

            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            await using var connection = new Microsoft.Data.Sqlite.SqliteConnection(
                $"Data Source={_testDbPath};Pooling=False");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version;";
            Assert.Equal(
                (long)SongDatabaseService.CurrentSongSchemaVersion,
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
        public async Task SearchSongsAsync_ShouldMatchTitleAndArtist()
        {
            await _databaseService.InitializeDatabaseAsync();
            await AddSongAsync("Blue Sky", "Alice", "blue-sky.dtx");
            await AddSongAsync("Red Moon", "Bob", "red-moon.dtx");

            var byTitle = await _databaseService.SearchSongsAsync("Blue");
            var byArtist = await _databaseService.SearchSongsAsync("Bob");

            var titleMatch = Assert.Single(byTitle);
            Assert.Equal("Blue Sky", titleMatch.Title);

            var artistMatch = Assert.Single(byArtist);
            Assert.Equal("Red Moon", artistMatch.Title);
        }

        [Fact]
        public async Task GetSongWithChartsAsync_WhenSongExists_ShouldReturnSongAndCharts()
        {
            await _databaseService.InitializeDatabaseAsync();

            var (songId, firstChart) = await AddSongAsync("Grouped Song", "Coverage Bot", "grouped-basic.dtx", drumLevel: 25);
            var secondChart = CreateChart("grouped-advanced.dtx", drumLevel: 65);

            await _databaseService.AddSongAsync(
                CreateSong("Grouped Song", "Coverage Bot"),
                secondChart);

            var result = await _databaseService.GetSongWithChartsAsync(songId);

            Assert.NotNull(result);
            Assert.Equal("Grouped Song", result!.Value.song.Title);
            Assert.Equal(2, result.Value.charts.Length);
            Assert.Contains(result.Value.charts, chart => chart.FilePath == firstChart.FilePath);
            Assert.Contains(result.Value.charts, chart => chart.FilePath == secondChart.FilePath);
        }

        [Fact]
        public async Task GetSongWithChartsAsync_WhenSongDoesNotExist_ShouldReturnNull()
        {
            await _databaseService.InitializeDatabaseAsync();

            var result = await _databaseService.GetSongWithChartsAsync(9999);

            Assert.Null(result);
        }

        [Fact]
        public async Task GetSongWithChartsAsync_ShouldIncludePersistedScores()
        {
            await _databaseService.InitializeDatabaseAsync();

            var (songId, chart) = await AddSongAsync("Scored Song", "Coverage Bot", "scored-song.dtx", drumLevel: 50);

            // Submit a score so there is a persisted SongScore row
            await _databaseService.UpdateScoreAsync(chart.Id, EInstrumentPart.DRUMS, 850_000, 88.0, fullCombo: false);

            var result = await _databaseService.GetSongWithChartsAsync(songId);

            Assert.NotNull(result);
            var returnedChart = Assert.Single(result!.Value.charts);
            Assert.NotNull(returnedChart.Scores);
            var persistedScore = Assert.Single(returnedChart.Scores);
            Assert.Equal(850_000, persistedScore.BestScore);
            Assert.Equal(88.0, persistedScore.BestAchievementRate);
            Assert.Equal(1, persistedScore.PlayCount);
        }

        [Fact]
        public async Task GetSongsAsync_ShouldIncludeScoreScopedPerformanceHistory()
        {
            await _databaseService.InitializeDatabaseAsync();

            var (songId, chart) = await AddSongAsync("History Song", "Coverage Bot", "history-song.dtx", drumLevel: 55);

            await _databaseService.UpdateScoreAsync(chart.Id, EInstrumentPart.DRUMS, 850_000, 88.0, fullCombo: false);
            var savedScore = await LoadScoreAsync(chart.Id, EInstrumentPart.DRUMS);

            using (var context = _databaseService.CreateContext())
            {
                context.PerformanceHistory.Add(new PerformanceHistory
                {
                    SongId = songId,
                    SongScoreId = savedScore.Id,
                    PerformedAt = new DateTime(2026, 6, 13, 12, 0, 0, DateTimeKind.Utc),
                    DisplayOrder = 1,
                    HistoryLine = "1.26/6/13 Cleared (A: 88.00)"
                });
                await context.SaveChangesAsync();
            }

            var songs = await _databaseService.GetSongsAsync();

            var loadedSong = Assert.Single(songs, song => song.Id == songId);
            var loadedChart = Assert.Single(loadedSong.Charts);
            var loadedScore = Assert.Single(loadedChart.Scores);
            var history = Assert.Single(loadedScore.PerformanceHistory);
            Assert.Equal(savedScore.Id, history.SongScoreId);
            Assert.Equal("1.26/6/13 Cleared (A: 88.00)", history.HistoryLine);
        }

        [Fact]
        public async Task UpdateScoreAsync_WhenNewBestScoreSubmitted_ShouldUpdateBestLastAndCounters()
        {
            await _databaseService.InitializeDatabaseAsync();
            var (_, chart) = await AddSongAsync("High Score", "Coverage Bot", "high-score.dtx", drumLevel: 75);

            await _databaseService.UpdateScoreAsync(chart.Id, EInstrumentPart.DRUMS, 900_000, 95.5, fullCombo: true);

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

            await _databaseService.UpdateScoreAsync(chart.Id, EInstrumentPart.DRUMS, 900_000, 95.5, fullCombo: true);
            await _databaseService.UpdateScoreAsync(chart.Id, EInstrumentPart.DRUMS, 500_000, 55.0, fullCombo: false);

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

            await _databaseService.UpdateScoreAsync(chart.Id, EInstrumentPart.DRUMS, 900_000, 95.5, fullCombo: true);
            await _databaseService.UpdateScoreAsync(chart.Id, EInstrumentPart.DRUMS, 950_000, 96.0, fullCombo: false);

            var score = await LoadScoreAsync(chart.Id, EInstrumentPart.DRUMS);

            Assert.Equal(950_000, score.BestScore);
            Assert.True(score.FullCombo);  // preserved from the first clear, not overwritten
        }

        [Fact]
        public async Task GetTopScoresAsync_ShouldReturnScoresSortedByBestScoreAndRespectLimit()
        {
            await _databaseService.InitializeDatabaseAsync();

            var (_, firstChart) = await AddSongAsync("First", "Coverage Bot", "first.dtx", drumLevel: 50);
            var (_, secondChart) = await AddSongAsync("Second", "Coverage Bot", "second.dtx", drumLevel: 50);
            var (_, thirdChart) = await AddSongAsync("Third", "Coverage Bot", "third.dtx", drumLevel: 50);

            await _databaseService.UpdateScoreAsync(firstChart.Id, EInstrumentPart.DRUMS, 600_000, 60.0, fullCombo: false);
            await _databaseService.UpdateScoreAsync(secondChart.Id, EInstrumentPart.DRUMS, 950_000, 98.0, fullCombo: true);
            await _databaseService.UpdateScoreAsync(thirdChart.Id, EInstrumentPart.DRUMS, 800_000, 83.0, fullCombo: false);

            var topScores = await _databaseService.GetTopScoresAsync(EInstrumentPart.DRUMS, limit: 2);

            Assert.Equal(2, topScores.Count);
            Assert.Equal(950_000, topScores[0].BestScore);
            Assert.Equal("Second", topScores[0].Chart.Song.Title);
            Assert.Equal(800_000, topScores[1].BestScore);
            Assert.Equal("Third", topScores[1].Chart.Song.Title);
        }

        [Fact]
        public async Task CleanupStaleChartsAsync_ShouldRemoveMissingChartsAndOrphanedSongs()
        {
            await _databaseService.InitializeDatabaseAsync();

            await AddSongAsync("Keep Me", "Coverage Bot", "keep-me.dtx", drumLevel: 40);

            var missingChartPath = Path.Combine(_tempRoot, "missing-chart.dtx");
            await _databaseService.AddSongAsync(
                CreateSong("Remove Me", "Coverage Bot"),
                new SongChart
                {
                    FilePath = missingChartPath,
                    Duration = 120,
                    Bpm = 180,
                    DrumLevel = 50,
                    HasDrumChart = true
                });

            await _databaseService.CleanupStaleChartsAsync();

            var songs = await _databaseService.GetSongsAsync();

            var remainingSong = Assert.Single(songs);
            Assert.Equal("Keep Me", remainingSong.Title);
            Assert.Single(remainingSong.Charts);
            Assert.DoesNotContain(songs, song => song.Title == "Remove Me");
        }

        private async Task<(int SongId, SongChart Chart)> AddSongAsync(
            string title,
            string artist,
            string fileName,
            int drumLevel = 40)
        {
            var song = CreateSong(title, artist);
            var chart = CreateChart(fileName, drumLevel);
            var songId = await _databaseService.AddSongAsync(song, chart);
            return (songId, chart);
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
