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
    [Collection("SongManager")]
    [Trait("Category", "Unit")]
    public class SongManagerBookmarkTests : IDisposable
    {
        private readonly SongManager _manager;
        private readonly string _dbPath;

        public SongManagerBookmarkTests()
        {
            SongManager.ResetInstanceForTesting();
            _manager = SongManager.Instance;
            _dbPath = Path.Combine(Path.GetTempPath(), $"mgr_bookmark_{Guid.NewGuid()}.db");
        }

        public void Dispose()
        {
            _manager.Clear();
            SongManager.ResetInstanceForTesting();
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
        private SongDatabaseService? ManagerDatabaseService =>
            ReflectionHelpers.GetPrivateField<SongDatabaseService?>(_manager, "_databaseService");

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
            await _manager.InitializeDatabaseServiceAsync(_dbPath, purgeDatabaseFirst: true);
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
        public async Task GetBookmarkedNodesAsync_WhenDatabaseServiceNull_ReturnsEmptyList()
        {
            // Not initializing the DB service: the null-guard returns empty, no throw.
            var nodes = await _manager.GetBookmarkedNodesAsync();
            Assert.Empty(nodes);
        }

        [Fact]
        public async Task SetBookmarkAsync_PersistsThroughManager()
        {
            await _manager.InitializeDatabaseServiceAsync(_dbPath, purgeDatabaseFirst: true);
            var db = ManagerDatabaseService!;
            var id = await AddSong(db, "Song");

            await _manager.SetBookmarkAsync(id, true);

            Assert.True((await db.GetSongsAsync()).Single(s => s.Id == id).IsBookmarked);
        }

        [Fact]
        public async Task SetBookmarkAsync_WhenDatabaseServiceNull_DoesNotThrow()
        {
            var ex = await Record.ExceptionAsync(() => _manager.SetBookmarkAsync(1, true));
            Assert.Null(ex);
        }
    }
}
