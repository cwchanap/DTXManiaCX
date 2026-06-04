using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Song.Entities;
using Xunit;

namespace DTXMania.Test.Song
{
    [Trait("Category", "Unit")]
    public class SongDatabaseServiceRecentPlaysTests : IDisposable
    {
        private readonly SongDatabaseService _db;
        private readonly string _dbPath;

        public SongDatabaseServiceRecentPlaysTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"recent_db_{Guid.NewGuid()}.db");
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

        // Adds a song with one drum chart and records a play (sets LastPlayedAt = UtcNow).
        private async Task<int> AddAndPlayAsync(string title)
        {
            var song = new DTXMania.Game.Lib.Song.Entities.Song { Title = title, Artist = "A" };
            var chart = new SongChart { FilePath = $"/c/{Guid.NewGuid():N}.dtx", HasDrumChart = true, DrumLevel = 30 };
            await _db.AddSongAsync(song, chart);
            var stored = (await _db.GetSongsAsync()).Single(s => s.Title == title);
            var chartId = stored.Charts.Single().Id;
            await _db.UpdateScoreAsync(chartId, EInstrumentPart.DRUMS, 100000, 0.9, true);
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

            var recent = await _db.GetRecentlyPlayedSongsAsync(20);

            Assert.Equal(new[] { "Third", "Second", "First" }, recent.Select(s => s.Title).ToArray());
        }

        [Fact]
        public async Task GetRecentlyPlayedSongsAsync_ExcludesNeverPlayedSongs()
        {
            await _db.InitializeDatabaseAsync();
            await AddAndPlayAsync("Played");
            // A song with a chart but no recorded play:
            var song = new DTXMania.Game.Lib.Song.Entities.Song { Title = "Unplayed", Artist = "A" };
            await _db.AddSongAsync(song, new SongChart { FilePath = "/c/unplayed.dtx", HasDrumChart = true, DrumLevel = 20 });

            var recent = await _db.GetRecentlyPlayedSongsAsync(20);

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

            var recent = await _db.GetRecentlyPlayedSongsAsync(20);

            Assert.Equal(20, recent.Count);
        }

        [Fact]
        public async Task GetRecentlyPlayedSongsAsync_GroupsMultiChartSongIntoSingleRow()
        {
            await _db.InitializeDatabaseAsync();
            // Same title+artist => grouped into one Song with two charts.
            var s1 = new DTXMania.Game.Lib.Song.Entities.Song { Title = "Multi", Artist = "A" };
            var s2 = new DTXMania.Game.Lib.Song.Entities.Song { Title = "Multi", Artist = "A" };
            await _db.AddSongAsync(s1, new SongChart { FilePath = "/c/multi-bas.dtx", HasDrumChart = true, DrumLevel = 30 });
            await _db.AddSongAsync(s2, new SongChart { FilePath = "/c/multi-adv.dtx", HasDrumChart = true, DrumLevel = 60 });
            var stored = (await _db.GetSongsAsync()).Single(s => s.Title == "Multi");
            var charts = stored.Charts.OrderBy(c => c.DrumLevel).ToArray();

            // Play the easier chart, then later the harder chart.
            await _db.UpdateScoreAsync(charts[0].Id, EInstrumentPart.DRUMS, 50000, 0.5, false);
            await Task.Delay(50);
            await _db.UpdateScoreAsync(charts[1].Id, EInstrumentPart.DRUMS, 90000, 0.9, true);

            var recent = await _db.GetRecentlyPlayedSongsAsync(20);

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

            var recent = await _db.GetRecentlyPlayedSongsAsync(limit);

            Assert.Empty(recent);
        }
    }
}
