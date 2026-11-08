using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SongEntity = DTXMania.Game.Lib.Song.Entities.Song;
using Xunit;

namespace DTXMania.Test.Song
{
    /// <summary>
    /// Coverage for the single-version song-database policy: a fresh file is
    /// stamped with <c>PRAGMA user_version = 1</c>, a matching file reopens
    /// without a rebuild, and an unversioned/mismatched/invalid file is purged
    /// once through the throwing <see cref="SongDatabaseService.PurgeDatabaseAsync"/>.
    /// </summary>
    [Trait("Category", "Unit")]
    public sealed class SongDatabaseSchemaVersionTests : IDisposable
    {
        private readonly string _tempRoot;
        private readonly string _dbPath;

        public SongDatabaseSchemaVersionTests()
        {
            _tempRoot = Path.Combine(
                Path.GetTempPath(),
                "DTXManiaCX_Tests",
                nameof(SongDatabaseSchemaVersionTests),
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempRoot);
            _dbPath = Path.Combine(_tempRoot, "songs.db");
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            try
            {
                if (Directory.Exists(_tempRoot))
                    Directory.Delete(_tempRoot, recursive: true);
            }
            catch
            {
                // Best-effort cleanup for temp test assets.
            }
        }

        [Fact]
        public async Task InitializeDatabaseAsync_WhenFresh_CreatesSchemaMetadataAndStampsVersion()
        {
            using var service = new SongDatabaseService(_dbPath);

            await service.InitializeDatabaseAsync();

            Assert.Equal(
                SongDatabaseService.CurrentSongSchemaVersion,
                await ReadUserVersionAsync(_dbPath));
            foreach (var table in new[]
            {
                "Songs",
                "SongCharts",
                "SongScores",
                "SongHierarchy",
                "PerformanceHistory",
                "ScoreSaveReceipts",
                "__EnumerationMetadata"
            })
            {
                Assert.Equal(1, await TableCountAsync(_dbPath, table));
            }
            Assert.Equal(0, await TableCountAsync(_dbPath, "__DatabaseVersion"));

            using var context = service.CreateContext();
            var receiptType = context.Model.FindEntityType(typeof(ScoreSaveReceipt));
            Assert.NotNull(receiptType);
            Assert.Equal(
                nameof(ScoreSaveReceipt.RunId),
                Assert.Single(receiptType!.FindPrimaryKey()!.Properties).Name);
            Assert.Equal(
                100,
                receiptType.FindProperty(nameof(ScoreSaveReceipt.PlaySpeedPercent))!
                    .GetDefaultValue());
            Assert.Equal(
                DeleteBehavior.SetNull,
                Assert.Single(receiptType.GetForeignKeys()).DeleteBehavior);
            Assert.Contains(
                receiptType.GetIndexes(),
                index => !index.IsUnique
                    && index.Properties.Select(property => property.Name)
                        .SequenceEqual(new[] { nameof(ScoreSaveReceipt.SongScoreId) }));
        }

        [Fact]
        public async Task InitializeDatabaseAsync_JapaneseTitleAndPath_RoundTrips()
        {
            using var service = new SongDatabaseService(_dbPath);
            await service.InitializeDatabaseAsync();

            var songFolder = Path.Combine(_tempRoot, "楽曲");
            Directory.CreateDirectory(songFolder);
            var chartPath = Path.Combine(songFolder, "桜色のメロディ.dtx");
            await File.WriteAllTextAsync(chartPath, "#TITLE:桜色のメロディ");

            var songId = await service.AddSongAsync(
                new SongEntity
                {
                    Title = "桜色のメロディ",
                    Artist = "夢見るドラマー",
                    Genre = "和風ポップ"
                },
                new SongChart
                {
                    FilePath = chartPath,
                    HasDrumChart = true,
                    DrumLevel = 45
                });

            var result = await service.GetSongWithChartsAsync(songId);
            Assert.NotNull(result);
            Assert.Equal("桜色のメロディ", result!.Value.song.Title);
            Assert.Equal("夢見るドラマー", result.Value.song.Artist);
            var storedChart = Assert.Single(result.Value.charts);
            Assert.Equal(chartPath, storedChart.FilePath);
        }

        [Fact]
        public async Task InitializeDatabaseAsync_WhenReopeningCurrentVersion_PreservesLibraryState()
        {
            var watermark = new DateTime(2026, 11, 7, 12, 0, 0, DateTimeKind.Utc);
            var canonicalRoot = SongPathIdentity.Normalize(_tempRoot);
            int songId;
            int chartId;

            using (var first = new CountingPurgeService(_dbPath))
            {
                await first.InitializeDatabaseAsync();

                var chartPath = Path.Combine(_tempRoot, "retain.dtx");
                await File.WriteAllTextAsync(chartPath, "#TITLE:Retain Me");
                var chart = new SongChart
                {
                    FilePath = chartPath,
                    HasDrumChart = true,
                    DrumLevel = 50
                };
                songId = await first.AddSongAsync(
                    new SongEntity { Title = "Retain Me", Artist = "Keeper" },
                    chart);
                chartId = chart.Id;
                await first.SetBookmarkAsync(songId, true);
                await first.UpdateScoreAsync(
                    chartId, EInstrumentPart.DRUMS, 900_000, 95.0, fullCombo: true);

                using (var context = first.CreateContext())
                {
                    var score = await context.SongScores
                        .SingleAsync(saved => saved.ChartId == chartId);
                    score.BestRank = 95;
                    context.PerformanceHistory.Add(new PerformanceHistory
                    {
                        SongId = songId,
                        SongScoreId = score.Id,
                        PerformedAt = watermark,
                        HistoryLine = "retained run",
                        DisplayOrder = 0
                    });
                    await context.SaveChangesAsync();
                }

                Assert.True(await first.SetEnumerationWatermarkAtomicallyAsync(
                    watermark, new[] { canonicalRoot }));
                await first.SetRejectedChartCountsAsync(
                    new Dictionary<string, int> { [canonicalRoot] = 3 });
            }

            using (var second = new CountingPurgeService(_dbPath))
            {
                await second.InitializeDatabaseAsync();

                // A matching-version database is reopened, never rebuilt.
                Assert.Equal(0, second.PurgeCalls);
                Assert.Equal(
                    SongDatabaseService.CurrentSongSchemaVersion,
                    await ReadUserVersionAsync(_dbPath));

                var songs = await second.GetSongsAsync();
                var reloaded = Assert.Single(songs, saved => saved.Id == songId);
                Assert.True(reloaded.IsBookmarked);
                var reloadedChart = Assert.Single(reloaded.Charts);
                var score = Assert.Single(
                    reloadedChart.Scores,
                    saved => saved.Instrument == EInstrumentPart.DRUMS);
                Assert.Equal(900_000, score.BestScore);
                Assert.Equal(95, score.BestRank);
                Assert.Equal(1, score.PlayCount);
                Assert.Equal(1, score.ClearCount);
                Assert.True(score.FullCombo);
                Assert.Equal(
                    "retained run",
                    Assert.Single(score.PerformanceHistory).HistoryLine);

                Assert.Equal(
                    watermark,
                    await second.GetLastSuccessfulEnumerationUtcAsync());
                Assert.Equal(
                    watermark,
                    await second.GetLastSuccessfulEnumerationUtcAsync(canonicalRoot));
                Assert.Equal(
                    3,
                    await second.GetRejectedChartCountAsync(new[] { canonicalRoot }));
            }
        }

        [Fact]
        public async Task InitializeDatabaseAsync_WhenVersionIsLegacyZero_PurgesOnceAndRebuilds()
        {
            await CreateLegacyVersionZeroDatabaseAsync();

            using var service = new CountingPurgeService(_dbPath);
            await service.InitializeDatabaseAsync();

            // Exactly one purge; none of the stale schema survives the rebuild.
            Assert.Equal(1, service.PurgeCalls);
            Assert.Equal(
                SongDatabaseService.CurrentSongSchemaVersion,
                await ReadUserVersionAsync(_dbPath));
            Assert.Equal(0, await TableCountAsync(_dbPath, "__DatabaseVersion"));
            Assert.Equal(0, await TableCountAsync(_dbPath, "LegacySentinel"));
            Assert.Equal(0, await ScalarAsync(_dbPath, "SELECT COUNT(*) FROM Songs"));
            Assert.Equal(1, await TableCountAsync(_dbPath, "__EnumerationMetadata"));

            // Re-enumeration works against the rebuilt schema.
            var songId = await service.AddSongAsync(
                new SongEntity { Title = "Rebuilt", Artist = "Fresh" },
                new SongChart
                {
                    FilePath = Path.Combine(_tempRoot, "rebuilt.dtx"),
                    HasDrumChart = true,
                    DrumLevel = 40
                });
            var songs = await service.GetSongsAsync();
            Assert.Single(songs, saved => saved.Id == songId);
        }

        [Fact]
        public async Task InitializeDatabaseAsync_WhenVersionMismatch_PurgesAndRebuilds()
        {
            using (var first = new SongDatabaseService(_dbPath))
            {
                await first.InitializeDatabaseAsync();
                await first.AddSongAsync(
                    new SongEntity { Title = "Old Data", Artist = "Old" },
                    new SongChart
                    {
                        FilePath = Path.Combine(_tempRoot, "old.dtx"),
                        HasDrumChart = true,
                        DrumLevel = 30
                    });
            }
            await ExecuteSqlAsync("PRAGMA user_version = 999");

            using var service = new CountingPurgeService(_dbPath);
            await service.InitializeDatabaseAsync();

            Assert.Equal(1, service.PurgeCalls);
            Assert.Equal(
                SongDatabaseService.CurrentSongSchemaVersion,
                await ReadUserVersionAsync(_dbPath));
            Assert.Equal(0, await ScalarAsync(_dbPath, "SELECT COUNT(*) FROM Songs"));
        }

        [Fact]
        public async Task InitializeDatabaseAsync_WhenFileIsNotSqlite_PurgesAndRebuilds()
        {
            await File.WriteAllTextAsync(_dbPath, "not a SQLite database");

            using var service = new CountingPurgeService(_dbPath);
            await service.InitializeDatabaseAsync();

            Assert.Equal(1, service.PurgeCalls);
            Assert.Equal(
                SongDatabaseService.CurrentSongSchemaVersion,
                await ReadUserVersionAsync(_dbPath));
            Assert.True(await service.DatabaseExistsAsync());
        }

        [Fact]
        public async Task InitializeDatabaseAsync_WhenProbeFailsWithNonCorruptionError_Propagates()
        {
            using (var first = new SongDatabaseService(_dbPath))
            {
                await first.InitializeDatabaseAsync();
            }

            var probeError = new IOException("simulated permission failure");
            using var service = new CountingPurgeService(
                _dbPath,
                readVersion: () => throw probeError);

            var thrown = await Assert.ThrowsAsync<IOException>(
                () => service.InitializeDatabaseAsync());

            Assert.Same(probeError, thrown);
            // Unrelated I/O failures must not trigger destructive recovery.
            Assert.Equal(0, service.PurgeCalls);
            Assert.Equal(
                SongDatabaseService.CurrentSongSchemaVersion,
                await ReadUserVersionAsync(_dbPath));
            Assert.Throws<InvalidOperationException>(() => service.CreateContext());
        }

        [Fact]
        public async Task InitializeDatabaseAsync_WhenPurgeFails_PropagatesWithoutInitialization()
        {
            await CreateLegacyVersionZeroDatabaseAsync();

            var deleteError = new IOException("simulated delete failure");
            using var service = new CountingPurgeService(
                _dbPath,
                purgeBehavior: () => Task.FromException(deleteError));

            var thrown = await Assert.ThrowsAsync<IOException>(
                () => service.InitializeDatabaseAsync());

            Assert.Same(deleteError, thrown);
            // The purge ran exactly once and its failure was not swallowed.
            Assert.Equal(1, service.PurgeCalls);
            Assert.True(File.Exists(_dbPath));
            Assert.Equal(0, await ReadUserVersionAsync(_dbPath));
            Assert.Throws<InvalidOperationException>(() => service.CreateContext());
        }

        [Fact]
        public async Task InitializeDatabaseAsync_WhenDatabasePathIsDirectory_PropagatesWithoutDeleting()
        {
            var directoryDbPath = Path.Combine(_tempRoot, "not-a-db");
            Directory.CreateDirectory(directoryDbPath);

            using var service = new SongDatabaseService(directoryDbPath);
            var exception = await Record.ExceptionAsync(
                () => service.InitializeDatabaseAsync());

            Assert.NotNull(exception);
            // The path was not treated as a corrupt database and deleted.
            Assert.True(Directory.Exists(directoryDbPath));
            Assert.Throws<InvalidOperationException>(() => service.CreateContext());
        }

        [Fact]
        public async Task InitializeDatabaseAsync_WhenResettingSongDatabase_ConfigDatabaseIsUntouched()
        {
            var configPath = Path.Combine(_tempRoot, "config.db");
            var configStore = new SqliteConfigStore(configPath);
            configStore.Save(new Dictionary<string, string>
            {
                ["SongRoot.0"] = _tempRoot,
                ["GameApiPort"] = "3777"
            });

            await CreateLegacyVersionZeroDatabaseAsync();
            using var service = new SongDatabaseService(_dbPath);
            await service.InitializeDatabaseAsync();

            // The reset deleted only songs.db; the config database still loads.
            var entries = configStore.Load();
            Assert.Equal(_tempRoot, entries["SongRoot.0"]);
            Assert.Equal("3777", entries["GameApiPort"]);
        }

        [Fact]
        public async Task CurrentSongSchemaHash_WhenSchemaChanges_FailsWithBumpInstruction()
        {
            Assert.Equal(1, SongDatabaseService.CurrentSongSchemaVersion);

            var options = new DbContextOptionsBuilder<SongDbContext>()
                .UseSqlite("Data Source=:memory:")
                .Options;
            await using var context = new SongDbContext(options);
            var createScript = context.Database.GenerateCreateScript()
                .Replace("\r\n", "\n", StringComparison.Ordinal);
            var metadataDdl = SongDatabaseService.EnumerationMetadataTableDdl
                .Replace("\r\n", "\n", StringComparison.Ordinal);
            var hash = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(createScript + metadataDdl)));

            Assert.True(
                hash == ExpectedSongSchemaHash,
                "schema changed → bump CurrentSongSchemaVersion and update hash " +
                $"(expected {ExpectedSongSchemaHash}, actual {hash})");
        }

        private const string ExpectedSongSchemaHash =
            "7D6517469E34FAB83608AD5D2AB4A07860F853A9DF50549BCB48E725B5846539";

        private async Task CreateLegacyVersionZeroDatabaseAsync()
        {
            var options = new DbContextOptionsBuilder<SongDbContext>()
                .UseSqlite($"Data Source={_dbPath}")
                .Options;

            await using (var context = new SongDbContext(options))
            {
                await context.Database.EnsureCreatedAsync();
                context.Songs.Add(new SongEntity
                {
                    Title = "Legacy Song",
                    Artist = "Legacy Artist"
                });
                await context.SaveChangesAsync();
            }

            await ExecuteSqlAsync(@"
                CREATE TABLE __DatabaseVersion (
                    Feature TEXT PRIMARY KEY,
                    Version INTEGER NOT NULL,
                    AppliedAt TEXT NOT NULL)");
            await ExecuteSqlAsync("CREATE TABLE LegacySentinel (Id INTEGER PRIMARY KEY)");

            Assert.Equal(0, await ReadUserVersionAsync(_dbPath));
        }

        private static async Task<long> ReadUserVersionAsync(string databasePath)
        {
            SqliteConnection.ClearAllPools();
            await using var connection = new SqliteConnection(
                new SqliteConnectionStringBuilder
                {
                    DataSource = databasePath,
                    Pooling = false
                }.ToString());
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version;";
            return (long)(await command.ExecuteScalarAsync())!;
        }

        private Task<long> TableCountAsync(string databasePath, string table) =>
            ScalarAsync(
                databasePath,
                $"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{table}'");

        private static async Task<long> ScalarAsync(string databasePath, string sql)
        {
            SqliteConnection.ClearAllPools();
            await using var connection = new SqliteConnection(
                new SqliteConnectionStringBuilder
                {
                    DataSource = databasePath,
                    Pooling = false
                }.ToString());
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            return (long)(await command.ExecuteScalarAsync())!;
        }

        private async Task ExecuteSqlAsync(string sql)
        {
            SqliteConnection.ClearAllPools();
            await using var connection = new SqliteConnection(
                new SqliteConnectionStringBuilder
                {
                    DataSource = _dbPath,
                    Pooling = false
                }.ToString());
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync();
        }

        private sealed class CountingPurgeService : SongDatabaseService
        {
            private readonly Func<long>? _readVersion;
            private readonly Func<Task>? _purgeBehavior;
            private readonly Func<bool>? _fileExists;

            public CountingPurgeService(
                string databasePath,
                Func<long>? readVersion = null,
                Func<Task>? purgeBehavior = null,
                Func<bool>? fileExists = null)
                : base(databasePath)
            {
                _readVersion = readVersion;
                _purgeBehavior = purgeBehavior;
                _fileExists = fileExists;
            }

            public int PurgeCalls { get; private set; }

            internal override bool InitializationDatabaseFileExists() =>
                _fileExists?.Invoke() ?? base.InitializationDatabaseFileExists();

            internal override Task<long> ReadSchemaVersionOrMismatchAsync() =>
                _readVersion != null
                    ? Task.FromResult(_readVersion())
                    : base.ReadSchemaVersionOrMismatchAsync();

            public override async Task PurgeDatabaseAsync()
            {
                PurgeCalls++;
                if (_purgeBehavior != null)
                {
                    await _purgeBehavior();
                    return;
                }
                await base.PurgeDatabaseAsync();
            }
        }
    }
}
