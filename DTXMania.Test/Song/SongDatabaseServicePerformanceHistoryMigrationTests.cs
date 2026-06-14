using System;
using System.IO;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Song.Entities;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DTXMania.Test.Song
{
    [Trait("Category", "Integration")]
    public class SongDatabaseServicePerformanceHistoryMigrationTests : IDisposable
    {
        private readonly string _dbPath;

        public SongDatabaseServicePerformanceHistoryMigrationTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"history_scope_mig_{Guid.NewGuid()}.db");
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
        public async Task InitializeDatabaseAsync_OnLegacyPerformanceHistorySchema_AddsScoreScopeAndPreservesRows()
        {
            await CreateLegacyDatabaseAsync();

            var service = new SongDatabaseService(_dbPath);
            await service.InitializeDatabaseAsync();
            service.Dispose();

            Assert.Equal(1, await ScalarAsync(
                "SELECT COUNT(*) FROM pragma_table_info('PerformanceHistory') WHERE name='SongScoreId'"));
            Assert.Equal(1, await ScalarAsync(
                "SELECT COUNT(*) FROM PerformanceHistory WHERE SongId=1 AND SongScoreId IS NULL AND HistoryLine='Legacy run'"));
            Assert.Equal(0, await ScalarAsync(
                "SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='IX_PerformanceHistory_SongId_DisplayOrder'"));
            Assert.Equal(1, await ScalarAsync(
                "SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='IX_PerformanceHistory_SongId'"));
            Assert.Equal(1, await ScalarAsync(
                "SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='IX_PerformanceHistory_SongScoreId_DisplayOrder'"));
            Assert.Equal(1, await ScalarAsync(
                "SELECT COUNT(*) FROM pragma_foreign_key_list('PerformanceHistory') WHERE [table]='SongScores' AND [on_delete]='SET NULL'"));

            await ExecuteAsync(
                "INSERT INTO PerformanceHistory (SongId, SongScoreId, PerformedAt, HistoryLine, DisplayOrder) " +
                "VALUES (1, NULL, '2026-06-14 00:00:00', 'Legacy same order', 0)");

            await ExecuteAsync(
                "INSERT INTO PerformanceHistory (SongId, SongScoreId, PerformedAt, HistoryLine, DisplayOrder) " +
                "VALUES (1, 1, '2026-06-15 00:00:00', 'Scoped run', 1)");

            await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(
                "INSERT INTO PerformanceHistory (SongId, SongScoreId, PerformedAt, HistoryLine, DisplayOrder) " +
                "VALUES (1, 1, '2026-06-16 00:00:00', 'Duplicate scoped run', 1)"));
        }

        [Fact]
        public async Task InitializeDatabaseAsync_CalledTwiceOnMigratedDatabase_IsIdempotent()
        {
            // First pass migrates a legacy schema into the score-scoped shape.
            await CreateLegacyDatabaseAsync();
            var service = new SongDatabaseService(_dbPath);
            await service.InitializeDatabaseAsync();
            service.Dispose();

            // Seed post-migration rows so we can detect data loss on the second pass.
            await ExecuteAsync(
                "INSERT INTO PerformanceHistory (SongId, SongScoreId, PerformedAt, HistoryLine, DisplayOrder) " +
                "VALUES (1, 1, '2026-06-15 00:00:00', 'Scoped run', 1)");

            const int expectedRows = 2; // 'Legacy run' + 'Scoped run'

            // Capture the schema fingerprint after the first migration.
            int songScoreIdColumnBefore = await ScalarAsync(
                "SELECT COUNT(*) FROM pragma_table_info('PerformanceHistory') WHERE name='SongScoreId'");
            int setNullFkBefore = await ScalarAsync(
                "SELECT COUNT(*) FROM pragma_foreign_key_list('PerformanceHistory') " +
                "WHERE [table]='SongScores' AND [from]='SongScoreId' AND [on_delete]='SET NULL'");
            int scopedUniqueIndexBefore = await ScalarAsync(
                "SELECT COUNT(*) FROM sqlite_master WHERE type='index' " +
                "AND name='IX_PerformanceHistory_SongScoreId_DisplayOrder'");
            int legacyIndexDroppedBefore = await ScalarAsync(
                "SELECT COUNT(*) FROM sqlite_master WHERE type='index' " +
                "AND name='IX_PerformanceHistory_SongId_DisplayOrder'");

            // Second pass on an already-migrated database must be a no-op.
            var second = new SongDatabaseService(_dbPath);
            await second.InitializeDatabaseAsync();
            second.Dispose();

            // Row count unchanged: no duplication, no truncation, no throw.
            Assert.Equal(expectedRows, await ScalarAsync("SELECT COUNT(*) FROM PerformanceHistory"));

            // Schema unchanged.
            Assert.Equal(songScoreIdColumnBefore, await ScalarAsync(
                "SELECT COUNT(*) FROM pragma_table_info('PerformanceHistory') WHERE name='SongScoreId'"));
            Assert.Equal(setNullFkBefore, await ScalarAsync(
                "SELECT COUNT(*) FROM pragma_foreign_key_list('PerformanceHistory') " +
                "WHERE [table]='SongScores' AND [from]='SongScoreId' AND [on_delete]='SET NULL'"));
            Assert.Equal(scopedUniqueIndexBefore, await ScalarAsync(
                "SELECT COUNT(*) FROM sqlite_master WHERE type='index' " +
                "AND name='IX_PerformanceHistory_SongScoreId_DisplayOrder'"));
            Assert.Equal(legacyIndexDroppedBefore, await ScalarAsync(
                "SELECT COUNT(*) FROM sqlite_master WHERE type='index' " +
                "AND name='IX_PerformanceHistory_SongId_DisplayOrder'"));

            // The scoped uniqueness invariant still holds after the second pass.
            await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(
                "INSERT INTO PerformanceHistory (SongId, SongScoreId, PerformedAt, HistoryLine, DisplayOrder) " +
                "VALUES (1, 1, '2026-06-17 00:00:00', 'Duplicate scoped run', 1)"));
        }

        private async Task CreateLegacyDatabaseAsync()
        {
            SqliteConnection.ClearAllPools();
            using var conn = new SqliteConnection($"Data Source={_dbPath};Foreign Keys=True");
            await conn.OpenAsync();

            await ExecuteAsync(conn, @"
                CREATE TABLE __DatabaseVersion (
                    Feature TEXT PRIMARY KEY,
                    Version INTEGER NOT NULL,
                    AppliedAt TEXT NOT NULL
                )");
            await ExecuteAsync(conn, @"
                INSERT INTO __DatabaseVersion (Feature, Version, AppliedAt)
                VALUES ('UnicodeCollation', 2, datetime('now'))");
            await ExecuteAsync(conn, @"
                CREATE TABLE Songs (
                    Id INTEGER NOT NULL CONSTRAINT PK_Songs PRIMARY KEY AUTOINCREMENT,
                    Title TEXT NOT NULL,
                    Artist TEXT NOT NULL,
                    Genre TEXT NOT NULL DEFAULT '',
                    Comment TEXT NOT NULL DEFAULT '',
                    CreatedAt TEXT NOT NULL DEFAULT '2026-06-13 00:00:00',
                    UpdatedAt TEXT NOT NULL DEFAULT '2026-06-13 00:00:00'
                )");
            await ExecuteAsync(conn, @"
                CREATE TABLE SongScores (
                    Id INTEGER NOT NULL CONSTRAINT PK_SongScores PRIMARY KEY AUTOINCREMENT,
                    ChartId INTEGER NOT NULL,
                    Instrument INTEGER NOT NULL
                )");
            await ExecuteAsync(conn, @"
                CREATE TABLE PerformanceHistory (
                    Id INTEGER NOT NULL CONSTRAINT PK_PerformanceHistory PRIMARY KEY AUTOINCREMENT,
                    SongId INTEGER NOT NULL,
                    PerformedAt TEXT NOT NULL,
                    HistoryLine TEXT NOT NULL,
                    DisplayOrder INTEGER NOT NULL,
                    CONSTRAINT FK_PerformanceHistory_Songs_SongId
                        FOREIGN KEY (SongId) REFERENCES Songs (Id) ON DELETE CASCADE
                )");
            await ExecuteAsync(conn,
                "CREATE UNIQUE INDEX IX_PerformanceHistory_SongId_DisplayOrder ON PerformanceHistory(SongId, DisplayOrder)");
            await ExecuteAsync(conn,
                "INSERT INTO Songs (Id, Title, Artist) VALUES (1, 'Legacy Song', 'Legacy Artist')");
            await ExecuteAsync(conn,
                "INSERT INTO SongScores (Id, ChartId, Instrument) VALUES (1, 1, 0)");
            await ExecuteAsync(conn,
                "INSERT INTO PerformanceHistory (SongId, PerformedAt, HistoryLine, DisplayOrder) " +
                "VALUES (1, '2026-06-13 00:00:00', 'Legacy run', 0)");
        }

        private async Task<int> ScalarAsync(string sql)
        {
            SqliteConnection.ClearAllPools();
            using var conn = new SqliteConnection($"Data Source={_dbPath};Foreign Keys=True");
            await conn.OpenAsync();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            return Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        private async Task ExecuteAsync(string sql)
        {
            SqliteConnection.ClearAllPools();
            using var conn = new SqliteConnection($"Data Source={_dbPath};Foreign Keys=True");
            await conn.OpenAsync();

            await ExecuteAsync(conn, sql);
        }

        private static async Task ExecuteAsync(SqliteConnection conn, string sql)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
