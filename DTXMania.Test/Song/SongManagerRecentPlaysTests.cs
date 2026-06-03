using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Entities;
using Xunit;

namespace DTXMania.Test.Song
{
    [Collection("SongManager")]
    [Trait("Category", "Unit")]
    public class SongManagerRecentPlaysTests : IDisposable
    {
        private readonly SongManager _manager;
        private readonly string _dbPath;

        public SongManagerRecentPlaysTests()
        {
            SongManager.ResetInstanceForTesting();
            _manager = SongManager.Instance;
            _dbPath = Path.Combine(Path.GetTempPath(), $"mgr_recent_{Guid.NewGuid()}.db");
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

        [Fact]
        public async Task GetRecentlyPlayedNodesAsync_ReturnsScoreNodesNewestFirst()
        {
            var manager = SongManager.Instance;
            await manager.InitializeDatabaseServiceAsync(_dbPath, purgeDatabaseFirst: true);
            var db = manager.DatabaseService!;

            async Task AddAndPlay(string title)
            {
                var song = new DTXMania.Game.Lib.Song.Entities.Song { Title = title, Artist = "A" };
                var chart = new SongChart { FilePath = $"/c/{Guid.NewGuid():N}.dtx", HasDrumChart = true, DrumLevel = 30 };
                await db.AddSongAsync(song, chart);
                var stored = (await db.GetSongsAsync()).Single(s => s.Title == title);
                await db.UpdateScoreAsync(stored.Charts.Single().Id, EInstrumentPart.DRUMS, 100000, 0.9, true);
            }

            await AddAndPlay("Older");
            await Task.Delay(50);
            await AddAndPlay("Newer");

            var nodes = await manager.GetRecentlyPlayedNodesAsync(20);

            Assert.Equal(2, nodes.Count);
            Assert.All(nodes, n => Assert.Equal(NodeType.Score, n.Type));
            Assert.Equal("Newer", nodes[0].DisplayTitle);
            Assert.Equal("Older", nodes[1].DisplayTitle);
        }

        [Fact]
        public async Task GetRecentlyPlayedNodesAsync_WhenNothingPlayed_ReturnsEmptyList()
        {
            var manager = SongManager.Instance;
            await manager.InitializeDatabaseServiceAsync(_dbPath, purgeDatabaseFirst: true);

            var nodes = await manager.GetRecentlyPlayedNodesAsync(20);

            Assert.Empty(nodes);
        }
    }
}
