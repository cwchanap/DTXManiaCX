using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Entities;
using Microsoft.EntityFrameworkCore;
using DTXMania.Game.Lib.Stage.Performance;
using DTXMania.Test.TestData;
using Xunit;

namespace DTXMania.Test.Song
{
    [Trait("Category", "Unit")]
    public class SongManagerRecentPlaysTests : IDisposable
    {
        private readonly SongLibrary _manager;
        private readonly string _dbPath;

        public SongManagerRecentPlaysTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"mgr_recent_{Guid.NewGuid()}.db");
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

        [Fact]
        public async Task GetRecentlyPlayedNodesAsync_ReturnsScoreNodesNewestFirst()
        {
            var manager = new SongLibrary(new SongDatabaseService(_dbPath));
            await manager.InitializeAsync(purgeDatabaseFirst: true);
            var db = manager.Database;

            async Task AddAndPlay(string title)
            {
                var song = new DTXMania.Game.Lib.Song.Entities.Song { Title = title, Artist = "A" };
                var chart = new SongChart { FilePath = $"/c/{Guid.NewGuid():N}.dtx", HasDrumChart = true, DrumLevel = 30 };
                await db.SeedSongAsync(song, chart);
                var stored = (await db.GetSongsAsync()).Single(s => s.Title == title);
                await db.UpdateScoreAsync(stored.Charts.Single().Id, EInstrumentPart.DRUMS, CreateSummary(100, pitchSemitones: 0, score: 100000));
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
            var manager = new SongLibrary(new SongDatabaseService(_dbPath));
            await manager.InitializeAsync(purgeDatabaseFirst: true);

            var nodes = await manager.GetRecentlyPlayedNodesAsync(20);

            Assert.Empty(nodes);
        }

        [Fact]
        public async Task GetRecentlyPlayedNodesAsync_WhenDatabaseUninitialized_ReturnsEmptyList()
        {
            // HPA-801: the library's database exists from construction but is
            // unusable until Startup's InitializeAsync runs; queries read as
            // empty instead of throwing (pre-rename contract).
            var manager = new SongLibrary(new SongDatabaseService(_dbPath));
            // Deliberately not initializing the database.

            var nodes = await manager.GetRecentlyPlayedNodesAsync(20);

            Assert.Empty(nodes);
        }

        [Fact]
        public async Task GetRecentlyPlayedNodesAsync_WhenDatabaseThrows_PropagatesException()
        {
            // A broken initialized database (context creation faults) must
            // propagate: BeginRecentPlaysLoad sets _recentPlaysLoadFailed and the
            // UI shows the "Could not load recent plays" message. Note purging is
            // NOT this state anymore (HPA-801): pre-initialization queries read
            // as empty.
            var manager = CreateFaultingManager();

            await Assert.ThrowsAsync<InvalidOperationException>(() => manager.GetRecentlyPlayedNodesAsync(20));
        }

        [Fact]
        public async Task GetRecentlyPlayedNodesAsync_WhenInitializationAttemptFailed_ThrowsLikeBrokenDatabase()
        {
            // HPA-801 fix round 1: a failed initialization is a failure state,
            // not availability. The pre-rename service reference was already
            // non-null, so the query reached CreateContext and threw; the
            // never-initialized short-circuit must not swallow that as an
            // empty "No recent plays yet" result.
            var manager = new SongLibrary(new ScriptedInitializationService(
                new DbContextOptionsBuilder<SongDbContext>()
                    .UseSqlite($"Data Source={_dbPath};Cache=Shared;").Options));

            Assert.False(await manager.InitializeAsync());

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => manager.GetRecentlyPlayedNodesAsync(20));
        }

        [Fact]
        public async Task GetRecentlyPlayedNodesAsync_WhenPurgeCannotReinitialize_ThrowsLikeBrokenDatabase()
        {
            // Purging an initialized database was also a throwing state
            // pre-rename: the service reference stayed non-null, so the query
            // reached CreateContext and threw instead of reading as empty.
            var manager = new SongLibrary(new ScriptedInitializationService(
                new DbContextOptionsBuilder<SongDbContext>()
                    .UseSqlite($"Data Source={_dbPath};Cache=Shared;").Options,
                ensureCreated: (context, attempt) => attempt == 1
                    ? context.Database.EnsureCreatedAsync()
                    : throw new InvalidOperationException("re-initialization faulted")));

            Assert.True(await manager.InitializeAsync());
            Assert.False(await manager.InitializeAsync(purgeDatabaseFirst: true));

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => manager.GetRecentlyPlayedNodesAsync(20));
        }

        private static SongLibrary CreateFaultingManager() => new(
            new SongDatabaseService(
                new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<
                    DTXMania.Game.Lib.Song.Entities.SongDbContext>()
                    .UseSqlite("Data Source=:memory:")
                    .Options,
                () => throw new InvalidOperationException("database unavailable"),
                initialized: true));

        [Fact]
        public async Task GetRecentlyPlayedNodesAsync_HydratesAllSpeedsAndIdentifiesLatestVariant()
        {
            await _manager.InitializeAsync(purgeDatabaseFirst: true);
            var db = _manager.Database;
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
            await db.SeedSongAsync(song, chart);
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

        /// <summary>
        /// The initialized database service is stored in a private field swapped
        /// under the manager lock; tests read it via the established reflection
        /// seam after the test-only DatabaseService accessor was removed
        /// (HPA-801 Decision 0).
        /// </summary>
        private static SongDatabaseService ManagerDatabaseService(SongLibrary manager) =>
            manager.Database;

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
