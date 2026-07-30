using System;
using System.Data.Common;
using System.Diagnostics;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.Stage;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

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

        public SongDatabaseServiceTests()
        {
            // Use unique database path for each test instance
            _testDbPath = Path.Combine(Path.GetTempPath(), $"test_song_db_{Guid.NewGuid()}.db");
            _databaseService = new SongDatabaseService(_testDbPath);
        }

        public void Dispose()
        {
            // Clean up after each test
            _databaseService?.Dispose();
            
            // Clean up test database file
            if (File.Exists(_testDbPath))
            {
                try
                {
                    File.Delete(_testDbPath);
                }
                catch
                {
                    // Ignore errors during cleanup
                }
            }
        }

        [Fact]
        public async Task InitializeDatabaseAsync_WhenFresh_ShouldRecordOneEnsureAndSchemaPartition()
        {
            var observer = new RecordingObserver();

            await _databaseService.InitializeDatabaseAsync(observer);

            Assert.True(await _databaseService.DatabaseExistsAsync());
            Assert.Equal(
                new[]
                {
                    "begin:EnsureCreated",
                    "end:EnsureCreated",
                    "begin:EncodingPragmas",
                    "end:EncodingPragmas",
                    "begin:VersionWork",
                    "end:VersionWork",
                    "begin:SchemaEnsures",
                    "end:SchemaEnsures"
                },
                observer.Events);
            Assert.Equal(1, observer.BeginCount(StartupDatabaseTimingSpan.EnsureCreated));
            Assert.Equal(1, observer.BeginCount(StartupDatabaseTimingSpan.SchemaEnsures));
            Assert.Equal(0, observer.BeginCount(StartupDatabaseTimingSpan.CorruptionProbe));
            Assert.Equal(0, observer.BeginCount(StartupDatabaseTimingSpan.InvalidRecovery));
        }

        [Fact]
        public async Task InitializeDatabaseAsync_WhenPreinitialized_ShouldRecordProbeAndSchemaPartition()
        {
            using (var initializer = new SongDatabaseService(_testDbPath))
            {
                await initializer.InitializeDatabaseAsync();
            }
            var observer = new RecordingObserver();
            using var service = new SongDatabaseService(_testDbPath);

            await service.InitializeDatabaseAsync(observer);

            Assert.True(await service.DatabaseExistsAsync());
            Assert.Equal(2, observer.BeginCount(StartupDatabaseTimingSpan.CorruptionProbe));
            Assert.Equal(2, observer.EndCount(StartupDatabaseTimingSpan.CorruptionProbe));
            Assert.Equal(1, observer.BeginCount(StartupDatabaseTimingSpan.EnsureCreated));
            Assert.Equal(1, observer.BeginCount(StartupDatabaseTimingSpan.SchemaEnsures));
            Assert.Equal(0, observer.BeginCount(StartupDatabaseTimingSpan.InvalidRecovery));
        }

        [Fact]
        public async Task InitializeDatabaseAsync_WhenObserverThrows_ShouldStillInitialize()
        {
            var observer = new ThrowingObserver();

            var exception = await Record.ExceptionAsync(
                () => _databaseService.InitializeDatabaseAsync(observer));

            Assert.Null(exception);
            Assert.True(await _databaseService.DatabaseExistsAsync());
            await using var context = _databaseService.CreateContext();
            Assert.Empty(await context.Songs.ToListAsync());
        }

        [Fact]
        public async Task InitializeDatabaseAsync_WhenInvalidFileRecovered_ShouldCountRecovery()
        {
            await File.WriteAllTextAsync(_testDbPath, "not a SQLite database");
            var observer = new RecordingObserver();

            await _databaseService.InitializeDatabaseAsync(observer);

            Assert.True(await _databaseService.DatabaseExistsAsync());
            Assert.Equal(2, observer.BeginCount(StartupDatabaseTimingSpan.CorruptionProbe));
            Assert.Equal(1, observer.BeginCount(StartupDatabaseTimingSpan.InvalidRecovery));
            Assert.Equal(1, observer.EndCount(StartupDatabaseTimingSpan.InvalidRecovery));
            Assert.Equal(1, observer.BeginCount(StartupDatabaseTimingSpan.EnsureCreated));
        }

        [Fact]
        public async Task InitializeDatabaseAsync_WhenFirstEnsureThrowsNotDatabase_ShouldMeasureBothAttempts()
        {
            await using var fixture = await SqliteInitializationFixture.CreateAsync();
            var observer = new RecordingObserver();
            var beginCountsAtCall = new List<int>();
            var recoveryBeginCountsAtCall = new List<int>();
            using var service = new ScriptedInitializationService(
                fixture.Options,
                fixture.CreateContext,
                async (context, attempt) =>
                {
                    beginCountsAtCall.Add(
                        observer.BeginCount(StartupDatabaseTimingSpan.EnsureCreated));
                    await Task.Delay(5);
                    if (attempt == 1)
                    {
                        throw new InvalidOperationException(
                            "file is not a database");
                    }
                },
                () =>
                {
                    recoveryBeginCountsAtCall.Add(
                        observer.BeginCount(StartupDatabaseTimingSpan.InvalidRecovery));
                    return Task.CompletedTask;
                });

            await service.InitializeDatabaseAsync(observer);

            Assert.Equal(new[] { 1, 2 }, beginCountsAtCall);
            Assert.Equal(2, observer.BeginCount(StartupDatabaseTimingSpan.EnsureCreated));
            Assert.Equal(2, observer.EndCount(StartupDatabaseTimingSpan.EnsureCreated));
            Assert.Equal(2, observer.Elapsed(StartupDatabaseTimingSpan.EnsureCreated).Count);
            Assert.All(
                observer.Elapsed(StartupDatabaseTimingSpan.EnsureCreated),
                elapsed => Assert.True(elapsed > TimeSpan.Zero));
            Assert.Equal(new[] { 1 }, recoveryBeginCountsAtCall);
            Assert.Equal(1, observer.BeginCount(StartupDatabaseTimingSpan.InvalidRecovery));
            Assert.NotNull(service.CreateContext());
        }

        [Fact]
        public async Task InitializeDatabaseAsync_WhenEnsureThrowsTableExists_ShouldNotifyObserverAndKeepProductionSuccess()
        {
            await using var fixture = await SqliteInitializationFixture.CreateAsync();
            var clock = new IncrementingMonotonicClock();
            var trace = StartupCriticalPathTrace.Start(
                clock,
                new FixedUtcMicrosecondClock(),
                entryTimestamp: 0,
                entryUnixMicroseconds: 1_000_000,
                exitAfterPublication: false);
            using var service = new ScriptedInitializationService(
                fixture.Options,
                fixture.CreateContext,
                (_, _) => throw new InvalidOperationException(
                    "table Songs already exists"));

            await service.InitializeDatabaseAsync(trace);

            Assert.NotNull(service.CreateContext());
            using var writer = new StringWriter();
            Assert.True(trace.TryPublishTerminal(writer));
            Assert.StartsWith(
                "HPA192_CRITICAL_PATH_FAILURE outcome=failure " +
                "error=unexpected_table_exists_path ",
                writer.ToString());
            Assert.DoesNotContain(
                "HPA192_CRITICAL_PATH outcome=success",
                writer.ToString());
        }

        [Fact]
        public async Task InitializeDatabaseAsync_WhenSchemaEnsureThrows_ShouldCloseSpanAndPropagate()
        {
            var interceptor = new ThrowingCommandInterceptor(
                commandText => commandText.Contains(
                    "pragma_table_info('Songs')",
                    StringComparison.Ordinal),
                "schema ensure failure");
            await using var fixture =
                await SqliteInitializationFixture.CreateAsync(interceptor);
            var observer = new RecordingObserver();
            using var service = new ScriptedInitializationService(
                fixture.Options,
                fixture.CreateContext);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.InitializeDatabaseAsync(observer));

            Assert.Contains("schema ensure failure", exception.ToString());
            Assert.Equal(1, observer.BeginCount(StartupDatabaseTimingSpan.SchemaEnsures));
            Assert.Equal(1, observer.EndCount(StartupDatabaseTimingSpan.SchemaEnsures));
            Assert.All(
                observer.Elapsed(StartupDatabaseTimingSpan.SchemaEnsures),
                elapsed => Assert.True(elapsed > TimeSpan.Zero));
            Assert.Throws<InvalidOperationException>(service.CreateContext);
        }

        [Fact]
        public async Task InitializeDatabaseAsync_WhenVersionWorkSwallowsFailure_ShouldRetainCurrentSuccess()
        {
            var interceptor = new ThrowingCommandInterceptor(
                commandText => commandText.Contains(
                    "CREATE TABLE IF NOT EXISTS __DatabaseVersion",
                    StringComparison.Ordinal),
                "version work failure");
            await using var fixture =
                await SqliteInitializationFixture.CreateAsync(interceptor);
            var observer = new RecordingObserver();
            using var service = new ScriptedInitializationService(
                fixture.Options,
                fixture.CreateContext);

            var exception = await Record.ExceptionAsync(
                () => service.InitializeDatabaseAsync(observer));

            Assert.Null(exception);
            Assert.Equal(1, observer.BeginCount(StartupDatabaseTimingSpan.VersionWork));
            Assert.Equal(1, observer.EndCount(StartupDatabaseTimingSpan.VersionWork));
            Assert.All(
                observer.Elapsed(StartupDatabaseTimingSpan.VersionWork),
                elapsed => Assert.True(elapsed > TimeSpan.Zero));
            Assert.Equal(1, observer.BeginCount(StartupDatabaseTimingSpan.SchemaEnsures));
            Assert.Equal(1, observer.EndCount(StartupDatabaseTimingSpan.SchemaEnsures));
            Assert.NotNull(service.CreateContext());
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

        private class RecordingObserver : IStartupSongLoadTimingObserver
        {
            private readonly Dictionary<StartupDatabaseTimingSpan, int> _begins =
                new();
            private readonly Dictionary<StartupDatabaseTimingSpan, int> _ends =
                new();
            private readonly Dictionary<StartupDatabaseTimingSpan, long> _active =
                new();
            private readonly Dictionary<StartupDatabaseTimingSpan, List<TimeSpan>>
                _elapsed = new();
            private int _unexpectedTableExistsPaths;

            public List<string> Events { get; } = new();

            public int BeginCount(StartupDatabaseTimingSpan span) =>
                _begins.GetValueOrDefault(span);

            public int EndCount(StartupDatabaseTimingSpan span) =>
                _ends.GetValueOrDefault(span);

            public IReadOnlyList<TimeSpan> Elapsed(StartupDatabaseTimingSpan span) =>
                _elapsed.TryGetValue(span, out var elapsed)
                    ? elapsed
                    : Array.Empty<TimeSpan>();

            public int UnexpectedTableExistsPaths =>
                _unexpectedTableExistsPaths;

            public virtual void BeginDatabaseSpan(StartupDatabaseTimingSpan span)
            {
                _begins[span] = BeginCount(span) + 1;
                _active[span] = Stopwatch.GetTimestamp();
                Events.Add($"begin:{span}");
            }

            public virtual void EndDatabaseSpan(StartupDatabaseTimingSpan span)
            {
                _ends[span] = EndCount(span) + 1;
                if (!_elapsed.TryGetValue(span, out var elapsed))
                {
                    elapsed = new List<TimeSpan>();
                    _elapsed[span] = elapsed;
                }
                elapsed.Add(Stopwatch.GetElapsedTime(_active[span]));
                _active.Remove(span);
                Events.Add($"end:{span}");
            }

            public virtual void RecordUnexpectedTableExistsPath() =>
                Interlocked.Increment(ref _unexpectedTableExistsPaths);

            public virtual void RecordEnumerationTerminal(
                SongEnumerationResult? result,
                StartupOperationOutcome outcome)
            {
            }
        }

        private sealed class ThrowingObserver : IStartupSongLoadTimingObserver
        {
            public void BeginDatabaseSpan(StartupDatabaseTimingSpan span) =>
                throw new InvalidOperationException("observer begin failure");

            public void EndDatabaseSpan(StartupDatabaseTimingSpan span) =>
                throw new InvalidOperationException("observer end failure");

            public void RecordUnexpectedTableExistsPath() =>
                throw new InvalidOperationException("observer table-exists failure");

            public void RecordEnumerationTerminal(
                SongEnumerationResult? result,
                StartupOperationOutcome outcome) =>
                throw new InvalidOperationException("observer terminal failure");
        }

        private sealed class ScriptedInitializationService : SongDatabaseService
        {
            private readonly Func<SongDbContext> _initializationContextFactory;
            private readonly Func<SongDbContext, int, Task> _ensureCreated;
            private readonly Func<Task> _handleInvalidDatabaseFile;
            private int _ensureAttempts;

            public ScriptedInitializationService(
                DbContextOptions<SongDbContext> options,
                Func<SongDbContext> contextFactory,
                Func<SongDbContext, int, Task>? ensureCreated = null,
                Func<Task>? handleInvalidDatabaseFile = null)
                : base(options, contextFactory, initialized: false)
            {
                _initializationContextFactory = contextFactory;
                _ensureCreated = ensureCreated ??
                    ((context, _) => context.Database.EnsureCreatedAsync());
                _handleInvalidDatabaseFile =
                    handleInvalidDatabaseFile ?? (() => Task.CompletedTask);
            }

            internal override bool InitializationDatabaseFileExists() => false;

            internal override SongDbContext CreateInitializationContext() =>
                _initializationContextFactory();

            internal override Task<bool> IsValidSqliteDatabaseAsync() =>
                Task.FromResult(true);

            internal override Task<bool> HasProperUnicodeConfigurationAsync() =>
                Task.FromResult(true);

            internal override Task HandleInvalidDatabaseFileAsync() =>
                _handleInvalidDatabaseFile();

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

        private sealed class IncrementingMonotonicClock : IMonotonicClock
        {
            private long _timestamp;

            public long TimestampFrequency => 1_000;

            public long GetTimestamp() =>
                Interlocked.Increment(ref _timestamp);
        }

        private sealed class FixedUtcMicrosecondClock : IUtcMicrosecondClock
        {
            public long GetUnixMicroseconds() => 2_000_000;
        }
    }
}
