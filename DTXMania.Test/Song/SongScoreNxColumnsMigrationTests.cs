using System;
using System.IO;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Song.Entities;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DTXMania.Test.Song
{
    [Trait("Category", "Unit")]
    public class SongScoreNxColumnsMigrationTests : IDisposable
    {
        private readonly string _dbPath;

        public SongScoreNxColumnsMigrationTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"nxcols_mig_{Guid.NewGuid()}.db");
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(_dbPath)) { try { File.Delete(_dbPath); } catch { } }
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
        public async Task LegacyDbMissingColumns_ShouldAddColumns()
        {
            var first = new SongDatabaseService(_dbPath);
            await first.InitializeDatabaseAsync();
            first.Dispose();

            SqliteConnection.ClearAllPools();
            using (var conn = new SqliteConnection($"Data Source={_dbPath}"))
            {
                await conn.OpenAsync();
                foreach (var col in new[] { "NxImportedPlayCount", "NxImportedClearCount" })
                {
                    using var drop = conn.CreateCommand();
                    drop.CommandText = $"ALTER TABLE SongScores DROP COLUMN {col}";
                    await drop.ExecuteNonQueryAsync();
                }
            }
            Assert.Equal(0, await ColumnCountAsync("NxImportedPlayCount"));
            Assert.Equal(0, await ColumnCountAsync("NxImportedClearCount"));

            var second = new SongDatabaseService(_dbPath);
            await second.InitializeDatabaseAsync();
            second.Dispose();

            Assert.Equal(1, await ColumnCountAsync("NxImportedPlayCount"));
            Assert.Equal(1, await ColumnCountAsync("NxImportedClearCount"));
        }
    }
}
