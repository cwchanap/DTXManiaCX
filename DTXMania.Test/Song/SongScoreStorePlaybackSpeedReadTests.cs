using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Test.TestData;
using Microsoft.Data.Sqlite;
using Xunit;
using SongEntity = DTXMania.Game.Lib.Song.Entities.Song;

namespace DTXMania.Test.Song
{
    [Trait("Category", "Unit")]
    public sealed class SongScoreStorePlaybackSpeedReadTests : IDisposable
    {
        private readonly SongDatabase _service;
        private readonly string _dbPath;

        public SongScoreStorePlaybackSpeedReadTests()
        {
            _dbPath = Path.Combine(
                Path.GetTempPath(),
                $"speed_read_{Guid.NewGuid()}.db");
            _service = new SongDatabase(_dbPath);
        }

        public void Dispose()
        {
            _service.Dispose();
            SqliteTestPools.ClearForFile(_dbPath);
            if (File.Exists(_dbPath))
            {
                try { File.Delete(_dbPath); } catch { /* ignore */ }
            }
        }

        // The recent-play SQL lives on SongQueryService since HPA-801 Task 7.
        private SongQueryService CreateQueryService() => new(
            _service,
            new SongHierarchyBuilder(
                new SongCatalogStore(_service),
                new SongDefinitionReader(),
                SongRootPolicy.ForCurrentPlatform(),
                BundledSongs.Root),
            readsAsNeverInitialized: () => false);

        [Fact]
        public async Task GetScoreWithHistoryAsync_ShouldReturnOnlyRequestedSpeedVariant()
        {
            await _service.InitializeDatabaseAsync();
            var seeded = await SeedSongAsync(
                "Lookup",
                (100, 100000, -2, DateTime.UtcNow.AddMinutes(-2)),
                (75, 750000, 4, DateTime.UtcNow));

            var defaultSpeed = await new SongScoreStore(_service).GetScoreWithHistoryAsync(
                seeded.ChartId,
                EInstrumentPart.DRUMS,
                playSpeedPercent: 100);
            var slowerSpeed = await new SongScoreStore(_service).GetScoreWithHistoryAsync(
                seeded.ChartId,
                EInstrumentPart.DRUMS,
                playSpeedPercent: 75);
            var unplayedSpeed = await new SongScoreStore(_service).GetScoreWithHistoryAsync(
                seeded.ChartId,
                EInstrumentPart.DRUMS,
                playSpeedPercent: 125);

            Assert.NotNull(defaultSpeed);
            Assert.Equal(100, defaultSpeed.PlaySpeedPercent);
            Assert.Equal(100000, defaultSpeed.BestScore);
            Assert.Equal(-2, Assert.Single(defaultSpeed.PerformanceHistory).PitchSemitones);

            Assert.NotNull(slowerSpeed);
            Assert.Equal(75, slowerSpeed.PlaySpeedPercent);
            Assert.Equal(750000, slowerSpeed.BestScore);
            Assert.Equal(4, Assert.Single(slowerSpeed.PerformanceHistory).PitchSemitones);

            Assert.Null(unplayedSpeed);
        }

        [Fact]
        public async Task GetRecentlyPlayedSongsAsync_ShouldOrderAcrossSpeedsAndRetainEveryVariant()
        {
            await _service.InitializeDatabaseAsync();
            var now = DateTime.UtcNow;
            await SeedSongAsync(
                "Variant latest",
                (100, 100000, -3, now.AddMinutes(-10)),
                (75, 750000, 5, now));
            await SeedSongAsync(
                "Default middle",
                (100, 500000, 0, now.AddMinutes(-5)));

            var recent = await CreateQueryService().GetRecentlyPlayedSongsAsync();

            Assert.Equal(
                new[] { "Variant latest", "Default middle" },
                recent.Select(song => song.Title).ToArray());

            var variantChart = recent[0].Charts.Single();
            Assert.Equal(2, variantChart.Scores.Count);
            var variants = variantChart.Scores.ToDictionary(
                score => score.PlaySpeedPercent);
            Assert.Equal(
                -3,
                Assert.Single(variants[100].PerformanceHistory)
                    .PitchSemitones);
            Assert.Equal(
                5,
                Assert.Single(variants[75].PerformanceHistory)
                    .PitchSemitones);
            Assert.True(
                variants[75].LastPlayedAt > variants[100].LastPlayedAt);
        }

        private async Task<(int SongId, int ChartId)> SeedSongAsync(
            string title,
            params (int Speed, int Score, int Pitch, DateTime PlayedAt)[] variants)
        {
            using var context = _service.CreateContext();
            var song = new SongEntity
            {
                Title = title,
                Artist = "Read API"
            };
            var chart = new SongChart
            {
                Song = song,
                FilePath = $"/read/{Guid.NewGuid():N}.dtx",
                HasDrumChart = true,
                DrumLevel = 50
            };
            context.SongCharts.Add(chart);
            await context.SaveChangesAsync();

            foreach (var variant in variants)
            {
                var score = new SongScore
                {
                    ChartId = chart.Id,
                    Instrument = EInstrumentPart.DRUMS,
                    PlaySpeedPercent = variant.Speed,
                    BestScore = variant.Score,
                    LastPlayedAt = variant.PlayedAt
                };
                context.SongScores.Add(score);
                await context.SaveChangesAsync();
                context.PerformanceHistory.Add(new PerformanceHistory
                {
                    SongId = song.Id,
                    SongScoreId = score.Id,
                    PerformedAt = variant.PlayedAt,
                    PitchSemitones = variant.Pitch,
                    HistoryLine = $"{variant.Speed}% / pitch {variant.Pitch}",
                    DisplayOrder = 0
                });
            }

            await context.SaveChangesAsync();
            return (song.Id, chart.Id);
        }
    }
}