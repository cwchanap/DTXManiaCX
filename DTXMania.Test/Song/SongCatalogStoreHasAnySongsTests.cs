using System;
using System.IO;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Song.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;
using SongEntity = DTXMania.Game.Lib.Song.Entities.Song;

namespace DTXMania.Test.Song
{
    /// <summary>
    /// Behavioral tests for the narrow has-songs store read used by
    /// NeedsEnumerationAsync (HPA-801 Decision 0): connectivity is decided by
    /// CanConnectAsync and song presence by a single EXISTS query instead of the
    /// five-COUNT aggregate statistics.
    /// </summary>
    [Trait("Category", "Unit")]
    public sealed class SongCatalogStoreHasAnySongsTests : IDisposable
    {
        private readonly SongDatabase _service;
        private readonly string _dbPath;

        public SongCatalogStoreHasAnySongsTests()
        {
            _dbPath = Path.Combine(
                Path.GetTempPath(),
                $"has_any_songs_{Guid.NewGuid()}.db");
            _service = new SongDatabase(_dbPath);
        }

        public void Dispose()
        {
            _service.Dispose();
            SqliteConnection.ClearAllPools();
            if (File.Exists(_dbPath))
            {
                try { File.Delete(_dbPath); } catch { /* ignore */ }
            }
        }

        [Fact]
        public async Task HasAnySongsAsync_OnFreshEmptyDatabase_ShouldReturnFalse()
        {
            await _service.InitializeDatabaseAsync();

            Assert.False(await new SongCatalogStore(_service).HasAnySongsAsync());
        }

        [Fact]
        public async Task HasAnySongsAsync_AfterSeedingASong_ShouldReturnTrue()
        {
            await _service.InitializeDatabaseAsync();
            using (var context = _service.CreateContext())
            {
                context.Songs.Add(new SongEntity
                {
                    Title = "Seeded",
                    Artist = "HasAnySongs"
                });
                await context.SaveChangesAsync();
            }

            Assert.True(await new SongCatalogStore(_service).HasAnySongsAsync());
        }

        [Fact]
        public async Task HasAnySongsAsync_AfterAllSongsRemoved_ShouldReturnFalseAgain()
        {
            await _service.InitializeDatabaseAsync();
            int songId;
            using (var context = _service.CreateContext())
            {
                var song = new SongEntity
                {
                    Title = "Temporary",
                    Artist = "HasAnySongs"
                };
                context.Songs.Add(song);
                await context.SaveChangesAsync();
                songId = song.Id;
            }

            Assert.True(await new SongCatalogStore(_service).HasAnySongsAsync());

            using (var context = _service.CreateContext())
            {
                var song = await context.Songs.SingleAsync(s => s.Id == songId);
                context.Songs.Remove(song);
                await context.SaveChangesAsync();
            }

            Assert.False(await new SongCatalogStore(_service).HasAnySongsAsync());
        }
    }
}
