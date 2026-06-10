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
    public class SongDatabaseServiceNxImportMigrationTests : IDisposable
    {
        private readonly string _dbPath;

        public SongDatabaseServiceNxImportMigrationTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"nx_mig_{Guid.NewGuid()}.db");
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(_dbPath))
            {
                try { File.Delete(_dbPath); } catch { /* ignore */ }
            }
        }

        private async Task<int> ColumnCountAsync(string column)
        {
            SqliteConnection.ClearAllPools();
            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('SongScores') WHERE name='{column}'";
            return Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        [Fact]
        public async Task InitializeDatabaseAsync_OnLegacyDbMissingColumns_AddsColumns()
        {
            // Create a normal DB (has the columns via EnsureCreated), then drop them to
            // simulate a legacy database created before the NX import feature.
            var first = new SongDatabaseService(_dbPath);
            await first.InitializeDatabaseAsync();
            first.Dispose();

            SqliteConnection.ClearAllPools();
            using (var conn = new SqliteConnection($"Data Source={_dbPath}"))
            {
                await conn.OpenAsync();
                using var drop1 = conn.CreateCommand();
                drop1.CommandText = "ALTER TABLE SongScores DROP COLUMN NxImportedPlayCount";
                await drop1.ExecuteNonQueryAsync();

                using var drop2 = conn.CreateCommand();
                drop2.CommandText = "ALTER TABLE SongScores DROP COLUMN NxImportedClearCount";
                await drop2.ExecuteNonQueryAsync();
            }
            Assert.Equal(0, await ColumnCountAsync("NxImportedPlayCount"));
            Assert.Equal(0, await ColumnCountAsync("NxImportedClearCount"));

            // Re-initialize: migration must re-add both columns.
            var second = new SongDatabaseService(_dbPath);
            await second.InitializeDatabaseAsync();
            second.Dispose();

            Assert.Equal(1, await ColumnCountAsync("NxImportedPlayCount"));
            Assert.Equal(1, await ColumnCountAsync("NxImportedClearCount"));
        }

        [Fact]
        public async Task InitializeDatabaseAsync_OnFreshDb_IsIdempotentAndKeepsColumns()
        {
            var svc = new SongDatabaseService(_dbPath);
            await svc.InitializeDatabaseAsync();
            svc.Dispose();

            // A second service initializing the same file must not error or duplicate.
            var again = new SongDatabaseService(_dbPath);
            await again.InitializeDatabaseAsync();
            again.Dispose();

            Assert.Equal(1, await ColumnCountAsync("NxImportedPlayCount"));
            Assert.Equal(1, await ColumnCountAsync("NxImportedClearCount"));
        }

        // Guards the fail-fast rethrow in EnsureNxImportColumnsAsync: a genuine ALTER failure
        // (not the tolerated "duplicate column" race) must propagate as an
        // InvalidOperationException instead of being swallowed and leaving the schema broken.
        // We drop the SongScores table (keeping the version table so EnsureCreated is skipped)
        // so the ADD COLUMN fails with "no such table".
        [Fact]
        public async Task EnsureNxImportColumnsAsync_OnGenuineAlterFailure_RethrowsAsInvalidOperationException()
        {
            var first = new SongDatabaseService(_dbPath);
            await first.InitializeDatabaseAsync();
            first.Dispose();

            SqliteConnection.ClearAllPools();
            using (var conn = new SqliteConnection($"Data Source={_dbPath}"))
            {
                await conn.OpenAsync();
                using var drop = conn.CreateCommand();
                drop.CommandText = "DROP TABLE SongScores";
                await drop.ExecuteNonQueryAsync();
            }

            var second = new SongDatabaseService(_dbPath);
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => second.InitializeDatabaseAsync());
            Assert.Contains("NxImportedPlayCount", ex.Message);
            second.Dispose();
        }
    }
}
