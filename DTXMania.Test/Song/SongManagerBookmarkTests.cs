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
    public class SongManagerBookmarkTests : IDisposable
    {
        private readonly SongLibrary _manager;
        private readonly string _dbPath;

        public SongManagerBookmarkTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"mgr_bookmark_{Guid.NewGuid()}.db");
            _manager = new SongLibrary(new SongDatabaseService(_dbPath));
        }

        public void Dispose()
        {
            _manager.Dispose();
            if (File.Exists(_dbPath))
            {
                try { File.Delete(_dbPath); } catch { /* ignore */ }
            }
        }

        /// <summary>
        /// The initialized database service is stored in a private field swapped
        /// under the manager lock; tests read it via the established reflection
        /// seam after the test-only DatabaseService accessor was removed
        /// (HPA-801 Decision 0).
        /// </summary>
        private SongDatabaseService ManagerDatabaseService => _manager.Database;

        private async Task<int> AddSong(SongDatabaseService db, string title)
        {
            var song = new DTXMania.Game.Lib.Song.Entities.Song { Title = title, Artist = "A" };
            var chart = new SongChart { FilePath = $"/c/{Guid.NewGuid():N}.dtx", HasDrumChart = true, DrumLevel = 30 };
            await db.SeedSongAsync(song, chart);
            return (await db.GetSongsAsync()).Single(s => s.Title == title).Id;
        }

        [Fact]
        public async Task GetBookmarkedNodesAsync_ReturnsScoreNodesAlphabetically()
        {
            await _manager.InitializeAsync(purgeDatabaseFirst: true);
            var db = ManagerDatabaseService!;
            var b = await AddSong(db, "Bravo");
            var a = await AddSong(db, "Alpha");
            await db.SetBookmarkAsync(b, true);
            await db.SetBookmarkAsync(a, true);

            var nodes = await _manager.GetBookmarkedNodesAsync();

            Assert.Equal(2, nodes.Count);
            Assert.All(nodes, n => Assert.Equal(NodeType.Score, n.Type));
            Assert.Equal("Alpha", nodes[0].DisplayTitle);
            Assert.Equal("Bravo", nodes[1].DisplayTitle);
        }

        [Fact]
        public async Task GetBookmarkedNodesAsync_WhenDatabaseUninitialized_ReturnsEmptyList()
        {
            // HPA-801: the database is never initialized in this fixture; queries
            // read as empty instead of throwing (pre-rename contract).
            var nodes = await _manager.GetBookmarkedNodesAsync();
            Assert.Empty(nodes);
        }

        [Fact]
        public async Task SetBookmarkAsync_PersistsThroughManager()
        {
            await _manager.InitializeAsync(purgeDatabaseFirst: true);
            var db = ManagerDatabaseService!;
            var id = await AddSong(db, "Song");

            await _manager.SetBookmarkAsync(id, true);

            Assert.True((await db.GetSongsAsync()).Single(s => s.Id == id).IsBookmarked);
        }

        [Fact]
        public async Task SetBookmarkAsync_WhenDatabaseUninitialized_ThrowsClearError()
        {
            // HPA-801: writes fail clearly before Startup's initialization so the
            // caller's fault/rollback paths still engage.
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => _manager.SetBookmarkAsync(1, true));
        }
    }
}
