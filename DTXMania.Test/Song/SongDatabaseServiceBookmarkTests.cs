using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Song.Entities;
using Xunit;

namespace DTXMania.Test.Song
{
    [Trait("Category", "Unit")]
    public class SongDatabaseServiceBookmarkTests : IDisposable
    {
        private readonly SongDatabaseService _db;
        private readonly string _dbPath;

        public SongDatabaseServiceBookmarkTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"bookmark_db_{Guid.NewGuid()}.db");
            _db = new SongDatabaseService(_dbPath);
        }

        public void Dispose()
        {
            _db?.Dispose();
            if (File.Exists(_dbPath))
            {
                try { File.Delete(_dbPath); } catch { /* ignore */ }
            }
        }

        private async Task<int> AddSongAsync(string title)
        {
            var song = new DTXMania.Game.Lib.Song.Entities.Song { Title = title, Artist = "A" };
            var chart = new SongChart { FilePath = $"/c/{Guid.NewGuid():N}.dtx", HasDrumChart = true, DrumLevel = 30 };
            await _db.AddSongAsync(song, chart);
            return (await _db.GetSongsAsync()).Single(s => s.Title == title).Id;
        }

        [Fact]
        public async Task SetBookmarkAsync_TogglesFlagOnAndOff()
        {
            await _db.InitializeDatabaseAsync();
            var id = await AddSongAsync("Song");

            await _db.SetBookmarkAsync(id, true);
            Assert.True((await _db.GetSongsAsync()).Single(s => s.Id == id).IsBookmarked);

            await _db.SetBookmarkAsync(id, false);
            Assert.False((await _db.GetSongsAsync()).Single(s => s.Id == id).IsBookmarked);
        }

        [Fact]
        public async Task SetBookmarkAsync_WithUnknownId_DoesNotThrow()
        {
            await _db.InitializeDatabaseAsync();
            var ex = await Record.ExceptionAsync(() => _db.SetBookmarkAsync(999999, true));
            Assert.Null(ex);
        }

        [Fact]
        public async Task GetBookmarkedSongsAsync_ReturnsOnlyBookmarked_AlphabeticalByTitle()
        {
            await _db.InitializeDatabaseAsync();
            var charlie = await AddSongAsync("Charlie");
            var alpha = await AddSongAsync("Alpha");
            var bravo = await AddSongAsync("Bravo");
            await AddSongAsync("Unmarked");

            await _db.SetBookmarkAsync(charlie, true);
            await _db.SetBookmarkAsync(alpha, true);
            await _db.SetBookmarkAsync(bravo, true);

            var result = await _db.GetBookmarkedSongsAsync();

            Assert.Equal(new[] { "Alpha", "Bravo", "Charlie" }, result.Select(s => s.Title).ToArray());
        }

        [Fact]
        public async Task GetBookmarkedSongsAsync_EagerLoadsChartsAndScores()
        {
            await _db.InitializeDatabaseAsync();
            var id = await AddSongAsync("Song");
            await _db.SetBookmarkAsync(id, true);

            var result = await _db.GetBookmarkedSongsAsync();

            Assert.Single(result);
            Assert.NotNull(result[0].Charts);
            Assert.Single(result[0].Charts);
            // Scores collection is eager-loaded (non-null) even if empty.
            Assert.NotNull(result[0].Charts.Single().Scores);
        }

        [Fact]
        public async Task GetBookmarkedSongsAsync_WhenNoneBookmarked_ReturnsEmptyList()
        {
            await _db.InitializeDatabaseAsync();
            await AddSongAsync("Song");

            var result = await _db.GetBookmarkedSongsAsync();

            Assert.Empty(result);
        }
    }
}
