using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.Stage.Performance;
using DTXMania.Test.TestData;
using Xunit;

namespace DTXMania.Test.Song
{
    [Trait("Category", "Unit")]
    public class SongQueryServiceRecentPlaysTests : IDisposable
    {
        private readonly SongDatabase _db;
        private readonly string _dbPath;

        public SongQueryServiceRecentPlaysTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"recent_db_{Guid.NewGuid()}.db");
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

        // One played PerformanceSummary: a cleared run whose save stamps
        // LastPlayedAt = UtcNow through the live score-save path.
        private static PerformanceSummary CreatePlaySummary(int score)
        {
            return new PerformanceSummary
            {
                RunId = Guid.NewGuid(),
                PlaySpeedPercent = 100,
                Score = score,
                MaxCombo = 100,
                ClearFlag = true,
                PerfectCount = 100,
                TotalNotes = 100,
                PlayingSkill = 90.0,
                GameSkill = 90.0,
                ChartLevel = 30,
                CompletionReason = CompletionReason.SongComplete,
            };
        }

        // The recent-play SQL lives on SongQueryService since HPA-801 Task 7;
        // these tests exercise it directly over a real SQLite database.
        private SongQueryService CreateQueryService() => new(
            _db,
            new SongHierarchyBuilder(
                new SongCatalogStore(_db),
                new SongDefinitionReader(),
                SongRootPolicy.ForCurrentPlatform(),
                BundledSongs.Root),
            readsAsNeverInitialized: () => false);

        // Adds a song with one drum chart and records a play (sets LastPlayedAt = UtcNow).
        private async Task<int> AddAndPlayAsync(string title)
        {
            var song = new DTXMania.Game.Lib.Song.Entities.Song { Title = title, Artist = "A" };
            var chart = new SongChart { FilePath = $"/c/{Guid.NewGuid():N}.dtx", HasDrumChart = true, DrumLevel = 30 };
            await _db.SeedSongAsync(song, chart);
            var stored = (await new SongCatalogStore(_db).GetSongsAsync()).Single(s => s.Title == title);
            var chartId = stored.Charts.Single().Id;
            await new SongScoreStore(_db).UpdateScoreAsync(chartId, EInstrumentPart.DRUMS, CreatePlaySummary(100000));
            return stored.Id;
        }

        [Fact]
        public async Task GetRecentlyPlayedSongsAsync_OrdersByMostRecentFirst()
        {
            await _db.InitializeDatabaseAsync();
            await AddAndPlayAsync("First");
            await Task.Delay(50);
            await AddAndPlayAsync("Second");
            await Task.Delay(50);
            await AddAndPlayAsync("Third");

            var recent = await CreateQueryService().GetRecentlyPlayedSongsAsync(20);

            Assert.Equal(new[] { "Third", "Second", "First" }, recent.Select(s => s.Title).ToArray());
        }

        // Stronger ordering guard: insertion order diverges from recency order.
        // Inserts A,B,C (ascending recency), then replays A so it becomes most recent.
        // Expected: A,C,B — the client-side reorder loop (which compensates for the
        // IN query's unordered delivery) is essential for this to pass.
        [Fact]
        public async Task GetRecentlyPlayedSongsAsync_WhenRecencyDivergesFromInsertionOrder_StillCorrect()
        {
            await _db.InitializeDatabaseAsync();
            var idA = await AddAndPlayAsync("A");
            await Task.Delay(50);
            var idB = await AddAndPlayAsync("B");
            await Task.Delay(50);
            var idC = await AddAndPlayAsync("C");
            await Task.Delay(50);

            // Replay A so it becomes the most recent, diverging from insertion order.
            await new SongScoreStore(_db).UpdateScoreAsync(
                (await new SongCatalogStore(_db).GetSongsAsync()).Single(s => s.Id == idA).Charts.Single().Id,
                EInstrumentPart.DRUMS, CreatePlaySummary(200000));

            var recent = await CreateQueryService().GetRecentlyPlayedSongsAsync(20);

            Assert.Equal(new[] { "A", "C", "B" }, recent.Select(s => s.Title).ToArray());
        }

        [Fact]
        public async Task GetRecentlyPlayedSongsAsync_ExcludesNeverPlayedSongs()
        {
            await _db.InitializeDatabaseAsync();
            await AddAndPlayAsync("Played");
            // A song with a chart but no recorded play:
            var song = new DTXMania.Game.Lib.Song.Entities.Song { Title = "Unplayed", Artist = "A" };
            await _db.SeedSongAsync(song, new SongChart { FilePath = "/c/unplayed.dtx", HasDrumChart = true, DrumLevel = 20 });

            var recent = await CreateQueryService().GetRecentlyPlayedSongsAsync(20);

            Assert.Single(recent);
            Assert.Equal("Played", recent[0].Title);
        }

        [Fact]
        public async Task GetRecentlyPlayedSongsAsync_RespectsLimit()
        {
            await _db.InitializeDatabaseAsync();
            for (int i = 0; i < 25; i++)
            {
                await AddAndPlayAsync($"Song {i:D2}");
                await Task.Delay(2);
            }

            var recent = await CreateQueryService().GetRecentlyPlayedSongsAsync(20);

            Assert.Equal(20, recent.Count);
        }

        [Fact]
        public async Task GetRecentlyPlayedSongsAsync_GroupsMultiChartSongIntoSingleRow()
        {
            await _db.InitializeDatabaseAsync();
            // One song with two drum charts (two difficulties of the same song).
            var song = new DTXMania.Game.Lib.Song.Entities.Song { Title = "Multi", Artist = "A" };
            var bas = new SongChart { Song = song, FilePath = "/c/multi-bas.dtx", HasDrumChart = true, DrumLevel = 30 };
            var adv = new SongChart { Song = song, FilePath = "/c/multi-adv.dtx", HasDrumChart = true, DrumLevel = 60 };
            using (var ctx = _db.CreateContext())
            {
                ctx.Songs.Add(song);
                ctx.SongCharts.AddRange(bas, adv);
                await ctx.SaveChangesAsync();
            }
            var charts = (await new SongCatalogStore(_db).GetSongsAsync()).Single(s => s.Title == "Multi")
                .Charts.OrderBy(c => c.DrumLevel).ToArray();

            // Play the easier chart, then later the harder chart.
            await new SongScoreStore(_db).UpdateScoreAsync(charts[0].Id, EInstrumentPart.DRUMS, CreatePlaySummary(50000));
            await Task.Delay(50);
            await new SongScoreStore(_db).UpdateScoreAsync(charts[1].Id, EInstrumentPart.DRUMS, CreatePlaySummary(90000));

            var recent = await CreateQueryService().GetRecentlyPlayedSongsAsync(20);

            Assert.Single(recent);
            Assert.Equal("Multi", recent[0].Title);
            Assert.Equal(2, recent[0].Charts.Count);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(-100)]
        public async Task GetRecentlyPlayedSongsAsync_WithNonPositiveLimit_ReturnsEmptyList(int limit)
        {
            await _db.InitializeDatabaseAsync();
            // Add a played song so the database is non-empty; the guard should still
            // short-circuit before any query is issued.
            await AddAndPlayAsync("Played");

            var recent = await CreateQueryService().GetRecentlyPlayedSongsAsync(limit);

            Assert.Empty(recent);
        }
    }
}
