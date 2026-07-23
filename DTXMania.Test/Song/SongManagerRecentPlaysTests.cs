using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.Stage.Performance;
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

        [Fact]
        public async Task GetRecentlyPlayedNodesAsync_WhenDatabaseServiceNull_ReturnsEmptyList()
        {
            // After ResetInstanceForTesting, the singleton has no DatabaseService until
            // InitializeDatabaseServiceAsync is called. The null-guard should return an
            // empty list without throwing.
            var manager = SongManager.Instance;
            // Deliberately not initializing the database service.

            var nodes = await manager.GetRecentlyPlayedNodesAsync(20);

            Assert.Empty(nodes);
        }

        [Fact]
        public async Task GetRecentlyPlayedNodesAsync_WhenDatabaseThrows_PropagatesException()
        {
            var manager = SongManager.Instance;
            await manager.InitializeDatabaseServiceAsync(_dbPath, purgeDatabaseFirst: true);
            var db = manager.DatabaseService!;

            // Purging the database resets the service's _isInitialized flag, which makes
            // subsequent CreateContext() calls throw InvalidOperationException. The exception
            // must propagate to the caller so BeginRecentPlaysLoad can set _recentPlaysLoadFailed
            // and the UI can show the "Could not load recent plays" message.
            await db.PurgeDatabaseAsync();

            await Assert.ThrowsAsync<InvalidOperationException>(() => manager.GetRecentlyPlayedNodesAsync(20));
        }

        [Fact]
        public async Task GetRecentlyPlayedNodesAsync_HydratesAllSpeedsAndIdentifiesLatestVariant()
        {
            await _manager.InitializeDatabaseServiceAsync(
                _dbPath,
                purgeDatabaseFirst: true);
            var db = _manager.DatabaseService!;
            var song = new DTXMania.Game.Lib.Song.Entities.Song
            {
                Title = "Speed History",
                Artist = "A",
            };
            var chart = new SongChart
            {
                FilePath = $"/c/{Guid.NewGuid():N}.dtx",
                HasDrumChart = true,
                DrumLevel = 60,
            };
            await db.AddSongAsync(song, chart);
            var storedChart = Assert.Single(
                Assert.Single(await db.GetSongsAsync()).Charts);

            await db.UpdateScoreAsync(
                storedChart.Id,
                EInstrumentPart.DRUMS,
                CreateSummary(100, pitchSemitones: 2, score: 800000));
            await Task.Delay(20);
            await db.UpdateScoreAsync(
                storedChart.Id,
                EInstrumentPart.DRUMS,
                CreateSummary(75, pitchSemitones: -4, score: 900000));

            var node = Assert.Single(
                await _manager.GetRecentlyPlayedNodesAsync(20));
            var normal = node.GetScore(0, 100);
            var slow = node.GetScore(0, 75);

            Assert.Equal(75, node.RecentPlaySpeedPercent);
            Assert.NotNull(normal);
            Assert.NotNull(slow);
            Assert.Equal(800000, normal!.BestScore);
            Assert.Equal(900000, slow!.BestScore);
            Assert.Equal(2, Assert.Single(normal.PerformanceHistory).PitchSemitones);
            Assert.Equal(-4, Assert.Single(slow.PerformanceHistory).PitchSemitones);
            Assert.Equal(100, node.Scores[0].PlaySpeedPercent);
        }

        private static PerformanceSummary CreateSummary(
            int playSpeedPercent,
            int pitchSemitones,
            int score)
        {
            return new PerformanceSummary
            {
                RunId = Guid.NewGuid(),
                PlaySpeedPercent = playSpeedPercent,
                PitchSemitones = pitchSemitones,
                Score = score,
                MaxCombo = 100,
                ClearFlag = true,
                PerfectCount = 100,
                TotalNotes = 100,
                PlayingSkill = 100,
                GameSkill = 150,
                ChartLevel = 60,
                CompletionReason = CompletionReason.SongComplete,
            };
        }
    }
}
