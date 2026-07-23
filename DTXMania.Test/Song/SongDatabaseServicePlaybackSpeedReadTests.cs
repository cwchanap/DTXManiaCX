using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Song.Entities;
using Microsoft.Data.Sqlite;
using Xunit;
using SongEntity = DTXMania.Game.Lib.Song.Entities.Song;

namespace DTXMania.Test.Song
{
    [Trait("Category", "Unit")]
    public sealed class SongDatabaseServicePlaybackSpeedReadTests : IDisposable
    {
        private readonly SongDatabaseService _service;
        private readonly string _dbPath;

        public SongDatabaseServicePlaybackSpeedReadTests()
        {
            _dbPath = Path.Combine(
                Path.GetTempPath(),
                $"speed_read_{Guid.NewGuid()}.db");
            _service = new SongDatabaseService(_dbPath);
        }

        public void Dispose()
        {
            _service.Dispose();
            SqliteConnection.ClearAllPools();
            if (File.Exists(_dbPath))
            {
                try { File.Delete(_dbPath); } catch { /* ignore */ }
            }
        }

        [Fact]
        public async Task GetScoreWithHistoryAsync_ShouldReturnOnlyRequestedSpeedVariant()
        {
            await _service.InitializeDatabaseAsync();
            var seeded = await SeedSongAsync(
                "Lookup",
                (100, 100000, -2, DateTime.UtcNow.AddMinutes(-2)),
                (75, 750000, 4, DateTime.UtcNow));

            var defaultSpeed = await _service.GetScoreWithHistoryAsync(
                seeded.ChartId,
                EInstrumentPart.DRUMS,
                playSpeedPercent: 100);
            var slowerSpeed = await _service.GetScoreWithHistoryAsync(
                seeded.ChartId,
                EInstrumentPart.DRUMS,
                playSpeedPercent: 75);
            var unplayedSpeed = await _service.GetScoreWithHistoryAsync(
                seeded.ChartId,
                EInstrumentPart.DRUMS,
                playSpeedPercent: 125);
            var compatibilityDefault = await _service.GetScoreWithHistoryAsync(
                seeded.ChartId,
                EInstrumentPart.DRUMS);

            Assert.NotNull(defaultSpeed);
            Assert.Equal(100, defaultSpeed.PlaySpeedPercent);
            Assert.Equal(100000, defaultSpeed.BestScore);
            Assert.Equal(-2, Assert.Single(defaultSpeed.PerformanceHistory).PitchSemitones);

            Assert.NotNull(slowerSpeed);
            Assert.Equal(75, slowerSpeed.PlaySpeedPercent);
            Assert.Equal(750000, slowerSpeed.BestScore);
            Assert.Equal(4, Assert.Single(slowerSpeed.PerformanceHistory).PitchSemitones);

            Assert.Null(unplayedSpeed);
            Assert.NotNull(compatibilityDefault);
            Assert.Equal(100, compatibilityDefault.PlaySpeedPercent);
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

            var recent = await _service.GetRecentlyPlayedSongsAsync();

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

        [Fact]
        public async Task GetTopScores_ShouldRequireExplicitSpeedOrUseDefaultCompatibility()
        {
            await _service.InitializeDatabaseAsync();
            await SeedSongAsync(
                "Top variants",
                (100, 100000, 0, DateTime.UtcNow.AddMinutes(-1)),
                (75, 900000, 0, DateTime.UtcNow));

            var compatibilityDefault = await _service.GetTopScoresAsync(
                EInstrumentPart.DRUMS);
            var slower = await _service.GetTopScoresForSpeedAsync(
                EInstrumentPart.DRUMS,
                playSpeedPercent: 75);

            Assert.Single(compatibilityDefault);
            Assert.Equal(100, compatibilityDefault[0].PlaySpeedPercent);
            Assert.Equal(100000, compatibilityDefault[0].BestScore);
            Assert.Single(slower);
            Assert.Equal(75, slower[0].PlaySpeedPercent);
            Assert.Equal(900000, slower[0].BestScore);
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