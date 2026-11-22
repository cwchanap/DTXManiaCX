using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Test.TestData;
using Xunit;

namespace DTXMania.Test.Song
{
    [Trait("Category", "Unit")]
    public class SongQueryServiceBookmarkTests : IDisposable
    {
        private readonly SongDatabase _db;
        private readonly string _dbPath;

        public SongQueryServiceBookmarkTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"bookmark_db_{Guid.NewGuid()}.db");
            _db = new SongDatabase(_dbPath);
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
            await _db.SeedSongAsync(song, chart);
            return (await new SongCatalogStore(_db).GetSongsAsync()).Single(s => s.Title == title).Id;
        }

        // Bookmark read/toggle SQL lives on SongQueryService since HPA-801
        // Task 7; these tests exercise it directly over a real SQLite database.
        private SongQueryService CreateQueryService() => new(
            _db,
            new SongHierarchyBuilder(
                new SongCatalogStore(_db),
                new SongDefinitionReader(),
                SongRootPolicy.ForCurrentPlatform(),
                BundledSongs.Root),
            readsAsNeverInitialized: () => false);

        [Fact]
        public async Task SetBookmarkAsync_TogglesFlagOnAndOff()
        {
            await _db.InitializeDatabaseAsync();
            var id = await AddSongAsync("Song");

            await CreateQueryService().SetBookmarkAsync(id, true);
            Assert.True((await new SongCatalogStore(_db).GetSongsAsync()).Single(s => s.Id == id).IsBookmarked);

            await CreateQueryService().SetBookmarkAsync(id, false);
            Assert.False((await new SongCatalogStore(_db).GetSongsAsync()).Single(s => s.Id == id).IsBookmarked);
        }

        [Fact]
        public async Task SetBookmarkAsync_WithUnknownId_DoesNotThrow()
        {
            await _db.InitializeDatabaseAsync();
            var ex = await Record.ExceptionAsync(() => CreateQueryService().SetBookmarkAsync(999999, true));
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

            await CreateQueryService().SetBookmarkAsync(charlie, true);
            await CreateQueryService().SetBookmarkAsync(alpha, true);
            await CreateQueryService().SetBookmarkAsync(bravo, true);

            var result = await CreateQueryService().GetBookmarkedSongsAsync();

            Assert.Equal(new[] { "Alpha", "Bravo", "Charlie" }, result.Select(s => s.Title).ToArray());
        }

        [Fact]
        public async Task GetBookmarkedSongsAsync_EagerLoadsChartsAndScores()
        {
            await _db.InitializeDatabaseAsync();
            var id = await AddSongAsync("Song");
            await CreateQueryService().SetBookmarkAsync(id, true);

            var result = await CreateQueryService().GetBookmarkedSongsAsync();

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

            var result = await CreateQueryService().GetBookmarkedSongsAsync();

            Assert.Empty(result);
        }
    }
}
