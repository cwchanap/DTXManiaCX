using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DTXMania.Test.Song
{
    [Trait("Category", "Integration")]
    [Collection("Song database migration")]
    public sealed class PlaybackSpeedScoreMigrationTests : IDisposable
    {
        private readonly string _dbPath;

        public PlaybackSpeedScoreMigrationTests()
        {
            _dbPath = Path.Combine(
                Path.GetTempPath(),
                $"playback_speed_score_mig_{Guid.NewGuid()}.db");
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(_dbPath))
            {
                try { File.Delete(_dbPath); } catch { /* ignore */ }
            }
        }

        [Fact]
        public async Task InitializeDatabaseAsync_OnFreshDatabase_ShouldCreateMatchingEfSchema()
        {
            using var service = new SongDatabaseService(_dbPath);
            await service.InitializeDatabaseAsync();

            Assert.Equal(1, await ColumnCountAsync("SongScores", "PlaySpeedPercent"));
            Assert.Equal(1, await ColumnCountAsync("PerformanceHistory", "PitchSemitones"));
            Assert.Equal(1, await CorrectSpeedIndexCountAsync());
            Assert.Equal(0, await IndexCountAsync("IX_SongScores_ChartId_Instrument"));
            Assert.Equal(1, await TableCountAsync("ScoreSaveReceipts"));
            Assert.Equal(1, await ReceiptSetNullForeignKeyCountAsync());
            Assert.Equal(1, await CorrectReceiptIndexCountAsync());

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
        public async Task InitializeDatabaseAsync_OnLegacyDatabase_ShouldPreserveScoreAndHistoryIdentity()
        {
            await CreateLegacyDatabaseAsync();

            using var service = new SongDatabaseService(_dbPath);
            await service.InitializeDatabaseAsync();

            Assert.Equal(1, await ScalarIntAsync(
                "SELECT COUNT(*) FROM SongScores " +
                "WHERE Id=41 AND ChartId=7 AND Instrument=0 AND PlaySpeedPercent=100"));
            Assert.Equal(1, await ScalarIntAsync(
                "SELECT COUNT(*) FROM PerformanceHistory " +
                "WHERE Id=73 AND SongScoreId=41 AND PitchSemitones=0"));
            Assert.Equal(1, await ScalarIntAsync(
                "SELECT COUNT(*) FROM pragma_foreign_key_list('PerformanceHistory') " +
                "WHERE [table]='SongScores' AND [from]='SongScoreId' AND [on_delete]='SET NULL'"));
            Assert.Equal(1, await CorrectSpeedIndexCountAsync());
            Assert.Equal(0, await IndexCountAsync("IX_SongScores_ChartId_Instrument"));
        }

        [Fact]
        public async Task InitializeDatabaseAsync_OnPartialSchema_ShouldConvergeAndPreserveRows()
        {
            await CreateLegacyDatabaseAsync();
            await ExecuteAsync(
                "ALTER TABLE SongScores ADD COLUMN PlaySpeedPercent INTEGER NOT NULL DEFAULT 100");
            await ExecuteAsync(
                "ALTER TABLE PerformanceHistory ADD COLUMN PitchSemitones INTEGER NOT NULL DEFAULT 0");
            await ExecuteAsync(
                "UPDATE PerformanceHistory SET PitchSemitones=-5 WHERE Id=73");
            await ExecuteAsync(
                "DROP INDEX IX_SongScores_ChartId_Instrument");
            await ExecuteAsync(
                "CREATE INDEX IX_SongScores_ChartId_Instrument_PlaySpeedPercent " +
                "ON SongScores(ChartId)");
            await ExecuteAsync(@"
                CREATE TABLE ScoreSaveReceipts (
                    RunId TEXT NOT NULL,
                    ChartId INTEGER NOT NULL,
                    Instrument INTEGER NOT NULL,
                    PlaySpeedPercent INTEGER NOT NULL DEFAULT 100,
                    SongScoreId INTEGER NULL,
                    SavedAtUtc TEXT NOT NULL
                )");
            await ExecuteAsync(
                "INSERT INTO ScoreSaveReceipts (" +
                "RunId, ChartId, Instrument, PlaySpeedPercent, SongScoreId, SavedAtUtc) " +
                "VALUES ('00000000-0000-0000-0000-000000000041', 7, 0, 100, 41, " +
                "'2026-07-23 00:00:00')");

            using var service = new SongDatabaseService(_dbPath);
            await service.InitializeDatabaseAsync();

            Assert.Equal(1, await CorrectSpeedIndexCountAsync());
            Assert.Equal(1, await ReceiptSetNullForeignKeyCountAsync());
            Assert.Equal(1, await CorrectReceiptIndexCountAsync());
            Assert.Equal(1, await ScalarIntAsync(
                "SELECT COUNT(*) FROM pragma_table_info('ScoreSaveReceipts') " +
                "WHERE name='RunId' AND pk=1"));
            Assert.Equal(1, await ScalarIntAsync(
                "SELECT COUNT(*) FROM ScoreSaveReceipts " +
                "WHERE RunId='00000000-0000-0000-0000-000000000041' " +
                "AND ChartId=7 AND Instrument=0 AND PlaySpeedPercent=100 AND SongScoreId=41"));
            Assert.Equal(1, await ScalarIntAsync(
                "SELECT COUNT(*) FROM PerformanceHistory WHERE Id=73 AND PitchSemitones=-5"));
        }

        [Fact]
        public async Task InitializeDatabaseAsync_CalledTwice_ShouldRemainIdempotent()
        {
            await CreateLegacyDatabaseAsync();

            using (var first = new SongDatabaseService(_dbPath))
            {
                await first.InitializeDatabaseAsync();
            }
            using (var second = new SongDatabaseService(_dbPath))
            {
                await second.InitializeDatabaseAsync();
            }

            Assert.Equal(1, await ScalarIntAsync("SELECT COUNT(*) FROM SongScores"));
            Assert.Equal(1, await ScalarIntAsync("SELECT COUNT(*) FROM PerformanceHistory"));
            Assert.Equal(1, await CorrectSpeedIndexCountAsync());
            Assert.Equal(1, await TableCountAsync("ScoreSaveReceipts"));
            Assert.Equal(1, await ReceiptSetNullForeignKeyCountAsync());
        }

        [Fact]
        public async Task InitializeDatabaseAsync_WithDuplicateSpeedIdentity_ShouldFailWithoutDeletingScores()
        {
            await CreateLegacyDatabaseAsync();
            await ExecuteAsync(
                "ALTER TABLE SongScores ADD COLUMN PlaySpeedPercent INTEGER NOT NULL DEFAULT 100");
            await ExecuteAsync(
                "DROP INDEX IX_SongScores_ChartId_Instrument");
            await ExecuteAsync(
                "INSERT INTO SongScores (Id, ChartId, Instrument, PlaySpeedPercent) " +
                "VALUES (42, 7, 0, 100)");

            using var service = new SongDatabaseService(_dbPath);
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.InitializeDatabaseAsync());

            Assert.Contains("no score data was deleted", exception.Message);
            Assert.Equal(2, await ScalarIntAsync(
                "SELECT COUNT(*) FROM SongScores WHERE ChartId=7 AND Instrument=0"));
            Assert.Equal(1, await ScalarIntAsync(
                "SELECT COUNT(*) FROM PerformanceHistory WHERE Id=73 AND SongScoreId=41"));
        }

        [Fact]
        public async Task MigratedConstraints_ShouldScopeScoresAndRetainReceiptAfterScoreDeletion()
        {
            await CreateLegacyDatabaseAsync();
            using (var service = new SongDatabaseService(_dbPath))
            {
                await service.InitializeDatabaseAsync();
            }

            await ExecuteAsync(
                "INSERT INTO SongScores (ChartId, Instrument, PlaySpeedPercent) " +
                "VALUES (7, 0, 125)");
            await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(
                "INSERT INTO SongScores (ChartId, Instrument, PlaySpeedPercent) " +
                "VALUES (7, 0, 125)"));

            const string runId = "00000000-0000-0000-0000-000000000099";
            await ExecuteAsync(
                "INSERT INTO ScoreSaveReceipts (" +
                "RunId, ChartId, Instrument, PlaySpeedPercent, SongScoreId, SavedAtUtc) " +
                $"VALUES ('{runId}', 7, 0, 100, 41, '2026-07-23 00:00:00')");
            for (var displayOrder = 1; displayOrder <= 6; displayOrder++)
            {
                await ExecuteAsync(
                    "INSERT INTO PerformanceHistory (" +
                    "SongId, SongScoreId, PerformedAt, HistoryLine, DisplayOrder, PitchSemitones) " +
                    $"VALUES (1, 41, '2026-07-23 00:00:00', 'Later {displayOrder}', " +
                    $"{displayOrder}, 0)");
            }
            await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(
                "INSERT INTO ScoreSaveReceipts (" +
                "RunId, ChartId, Instrument, PlaySpeedPercent, SongScoreId, SavedAtUtc) " +
                $"VALUES ('{runId}', 7, 0, 100, 41, '2026-07-23 01:00:00')"));

            await ExecuteAsync("DELETE FROM SongScores WHERE Id=41");

            Assert.Equal(1, await ScalarIntAsync(
                $"SELECT COUNT(*) FROM ScoreSaveReceipts WHERE RunId='{runId}' AND SongScoreId IS NULL"));
            Assert.Equal(7, await ScalarIntAsync(
                "SELECT COUNT(*) FROM PerformanceHistory WHERE SongScoreId IS NULL"));
        }

        [Fact]
        public async Task PurgeDatabaseAsync_ShouldRemoveReceiptsWithWholeDatabase()
        {
            using var service = new SongDatabaseService(_dbPath);
            await service.InitializeDatabaseAsync();
            await ExecuteAsync(
                "INSERT INTO ScoreSaveReceipts (" +
                "RunId, ChartId, Instrument, PlaySpeedPercent, SongScoreId, SavedAtUtc) " +
                "VALUES ('00000000-0000-0000-0000-000000000123', 7, 0, 100, NULL, " +
                "'2026-07-23 00:00:00')");

            await service.PurgeDatabaseAsync();

            Assert.False(File.Exists(_dbPath));
        }

        private async Task CreateLegacyDatabaseAsync()
        {
            SqliteConnection.ClearAllPools();
            using var connection = new SqliteConnection(
                $"Data Source={_dbPath};Foreign Keys=True");
            await connection.OpenAsync();

            await ExecuteAsync(connection, @"
                CREATE TABLE __DatabaseVersion (
                    Feature TEXT PRIMARY KEY,
                    Version INTEGER NOT NULL,
                    AppliedAt TEXT NOT NULL
                )");
            await ExecuteAsync(connection, @"
                INSERT INTO __DatabaseVersion (Feature, Version, AppliedAt)
                VALUES ('UnicodeCollation', 2, datetime('now'))");
            await ExecuteAsync(connection, @"
                CREATE TABLE Songs (
                    Id INTEGER NOT NULL CONSTRAINT PK_Songs PRIMARY KEY AUTOINCREMENT,
                    Title TEXT NOT NULL,
                    Artist TEXT NOT NULL,
                    Genre TEXT NOT NULL DEFAULT '',
                    Comment TEXT NOT NULL DEFAULT '',
                    IsBookmarked INTEGER NOT NULL DEFAULT 0,
                    CreatedAt TEXT NOT NULL DEFAULT '2026-07-23 00:00:00',
                    UpdatedAt TEXT NOT NULL DEFAULT '2026-07-23 00:00:00'
                )");
            await ExecuteAsync(connection, @"
                CREATE TABLE SongScores (
                    Id INTEGER NOT NULL CONSTRAINT PK_SongScores PRIMARY KEY AUTOINCREMENT,
                    ChartId INTEGER NOT NULL,
                    Instrument INTEGER NOT NULL
                )");
            await ExecuteAsync(connection,
                "CREATE UNIQUE INDEX IX_SongScores_ChartId_Instrument " +
                "ON SongScores(ChartId, Instrument)");
            await ExecuteAsync(connection, @"
                CREATE TABLE PerformanceHistory (
                    Id INTEGER NOT NULL CONSTRAINT PK_PerformanceHistory PRIMARY KEY AUTOINCREMENT,
                    SongId INTEGER NOT NULL,
                    SongScoreId INTEGER NULL,
                    PerformedAt TEXT NOT NULL,
                    HistoryLine TEXT NOT NULL,
                    DisplayOrder INTEGER NOT NULL,
                    CONSTRAINT FK_PerformanceHistory_Songs_SongId
                        FOREIGN KEY (SongId) REFERENCES Songs (Id) ON DELETE CASCADE,
                    CONSTRAINT FK_PerformanceHistory_SongScores_SongScoreId
                        FOREIGN KEY (SongScoreId) REFERENCES SongScores (Id) ON DELETE SET NULL
                )");
            await ExecuteAsync(connection,
                "CREATE INDEX IX_PerformanceHistory_SongId ON PerformanceHistory(SongId)");
            await ExecuteAsync(connection,
                "CREATE UNIQUE INDEX IX_PerformanceHistory_SongScoreId_DisplayOrder " +
                "ON PerformanceHistory(SongScoreId, DisplayOrder) WHERE SongScoreId IS NOT NULL");
            await ExecuteAsync(connection,
                "INSERT INTO Songs (Id, Title, Artist) VALUES (1, 'Legacy', 'Artist')");
            await ExecuteAsync(connection,
                "INSERT INTO SongScores (Id, ChartId, Instrument) VALUES (41, 7, 0)");
            await ExecuteAsync(connection,
                "INSERT INTO PerformanceHistory (" +
                "Id, SongId, SongScoreId, PerformedAt, HistoryLine, DisplayOrder) " +
                "VALUES (73, 1, 41, '2026-07-22 00:00:00', 'Legacy run', 0)");
        }

        private Task<int> ColumnCountAsync(string table, string column)
        {
            return ScalarIntAsync(
                $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name='{column}'");
        }

        private Task<int> TableCountAsync(string table)
        {
            return ScalarIntAsync(
                $"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{table}'");
        }

        private Task<int> IndexCountAsync(string index)
        {
            return ScalarIntAsync(
                $"SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='{index}'");
        }

        private Task<int> CorrectSpeedIndexCountAsync()
        {
            return ScalarIntAsync(@"
                SELECT COUNT(*)
                FROM pragma_index_list('SongScores') AS index_list
                WHERE index_list.name='IX_SongScores_ChartId_Instrument_PlaySpeedPercent'
                  AND index_list.[unique]=1
                  AND (SELECT COUNT(*) FROM pragma_index_info(index_list.name))=3
                  AND (SELECT name FROM pragma_index_info(index_list.name) WHERE seqno=0)='ChartId'
                  AND (SELECT name FROM pragma_index_info(index_list.name) WHERE seqno=1)='Instrument'
                  AND (SELECT name FROM pragma_index_info(index_list.name) WHERE seqno=2)='PlaySpeedPercent'");
        }

        private Task<int> ReceiptSetNullForeignKeyCountAsync()
        {
            return ScalarIntAsync(
                "SELECT COUNT(*) FROM pragma_foreign_key_list('ScoreSaveReceipts') " +
                "WHERE [table]='SongScores' AND [from]='SongScoreId' AND [on_delete]='SET NULL'");
        }

        private Task<int> CorrectReceiptIndexCountAsync()
        {
            return ScalarIntAsync(@"
                SELECT COUNT(*)
                FROM pragma_index_list('ScoreSaveReceipts') AS index_list
                WHERE index_list.name='IX_ScoreSaveReceipts_SongScoreId'
                  AND index_list.[unique]=0
                  AND (SELECT COUNT(*) FROM pragma_index_info(index_list.name))=1
                  AND (SELECT name FROM pragma_index_info(index_list.name) WHERE seqno=0)='SongScoreId'");
        }

        private async Task<int> ScalarIntAsync(string sql)
        {
            SqliteConnection.ClearAllPools();
            using var connection = new SqliteConnection(
                $"Data Source={_dbPath};Foreign Keys=True");
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            return Convert.ToInt32(await command.ExecuteScalarAsync());
        }

        private async Task ExecuteAsync(string sql)
        {
            SqliteConnection.ClearAllPools();
            using var connection = new SqliteConnection(
                $"Data Source={_dbPath};Foreign Keys=True");
            await connection.OpenAsync();
            await ExecuteAsync(connection, sql);
        }

        private static async Task ExecuteAsync(
            SqliteConnection connection,
            string sql)
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync();
        }
    }
}