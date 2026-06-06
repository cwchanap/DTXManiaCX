using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Song.Entities;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DTXMania.Test.Song
{
    [Trait("Category", "Unit")]
    public class SongDatabaseServiceBookmarkMigrationTests : IDisposable
    {
        private readonly string _dbPath;

        public SongDatabaseServiceBookmarkMigrationTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"bookmark_mig_{Guid.NewGuid()}.db");
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(_dbPath))
            {
                try { File.Delete(_dbPath); } catch { /* ignore */ }
            }
        }

        private async Task<int> ColumnCountAsync()
        {
            SqliteConnection.ClearAllPools();
            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Songs') WHERE name='IsBookmarked'";
            return Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        private async Task<int> IndexCountAsync()
        {
            SqliteConnection.ClearAllPools();
            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='IX_Songs_IsBookmarked'";
            return Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        [Fact]
        public async Task InitializeDatabaseAsync_OnLegacyDbMissingColumn_AddsColumn()
        {
            // Create a normal DB (has the column + Unicode version table), then drop the
            // column to simulate a legacy database created before this feature.
            var first = new SongDatabaseService(_dbPath);
            await first.InitializeDatabaseAsync();
            first.Dispose();

            SqliteConnection.ClearAllPools();
            using (var conn = new SqliteConnection($"Data Source={_dbPath}"))
            {
                await conn.OpenAsync();
                // Drop the index first (SQLite requires this before dropping the indexed column)
                using var dropIdx = conn.CreateCommand();
                dropIdx.CommandText = "DROP INDEX IF EXISTS IX_Songs_IsBookmarked";
                await dropIdx.ExecuteNonQueryAsync();

                using var drop = conn.CreateCommand();
                drop.CommandText = "ALTER TABLE Songs DROP COLUMN IsBookmarked";
                await drop.ExecuteNonQueryAsync();
            }
            Assert.Equal(0, await ColumnCountAsync()); // confirm the legacy state

            // Re-initialize: the Unicode version table is present so the DB is NOT wiped;
            // the migration must re-add the column.
            var second = new SongDatabaseService(_dbPath);
            await second.InitializeDatabaseAsync();
            second.Dispose();

            Assert.Equal(1, await ColumnCountAsync());
        }

        [Fact]
        public async Task InitializeDatabaseAsync_OnDbWithColumnButNoIndex_CreatesIndex()
        {
            // Simulate a partially migrated DB: the column exists (from an older upgrade) but
            // the supporting index was never created. Re-initialization must add the index even
            // though the column is already present (the early-return used to skip index creation).
            var first = new SongDatabaseService(_dbPath);
            await first.InitializeDatabaseAsync();
            first.Dispose();

            SqliteConnection.ClearAllPools();
            using (var conn = new SqliteConnection($"Data Source={_dbPath}"))
            {
                await conn.OpenAsync();
                using var dropIdx = conn.CreateCommand();
                dropIdx.CommandText = "DROP INDEX IF EXISTS IX_Songs_IsBookmarked";
                await dropIdx.ExecuteNonQueryAsync();
            }
            Assert.Equal(0, await IndexCountAsync()); // confirm the partially-migrated state

            // Re-initialize: the column is present so only the index should be (re)created.
            var second = new SongDatabaseService(_dbPath);
            await second.InitializeDatabaseAsync();
            second.Dispose();

            Assert.Equal(1, await ColumnCountAsync()); // unchanged
            Assert.Equal(1, await IndexCountAsync());  // index now enforced
        }

        [Fact]
        public async Task InitializeDatabaseAsync_OnFreshDb_IsIdempotentAndKeepsSingleColumn()
        {
            var svc = new SongDatabaseService(_dbPath);
            await svc.InitializeDatabaseAsync();
            svc.Dispose();

            // A second service initializing the same file must not error or duplicate.
            var again = new SongDatabaseService(_dbPath);
            await again.InitializeDatabaseAsync();
            again.Dispose();

            Assert.Equal(1, await ColumnCountAsync());
        }
    }
}
