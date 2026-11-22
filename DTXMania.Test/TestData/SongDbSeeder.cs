using System;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Song.Entities;
using Microsoft.EntityFrameworkCore;
using SongEntity = DTXMania.Game.Lib.Song.Entities.Song;
using SongScoreEntity = DTXMania.Game.Lib.Song.Entities.SongScore;

namespace DTXMania.Test.TestData
{
    /// <summary>
    /// Test-owned SQLite/EF seeding for SongDatabaseService fixtures. Replaces the
    /// removed production seeding helper <c>SongDatabaseService.AddSongAsync(Song, SongChart)</c>
    /// (HPA-801 Decision 0): inserts one new song and its chart, links them, and creates
    /// an empty initial score row per instrument the chart declares (level &gt; 0),
    /// matching the seeding semantics the migrated tests relied on.
    /// </summary>
    internal static class SongDbSeeder
    {
        public static async Task<int> SeedSongAsync(
            this SongDatabaseService databaseService,
            SongEntity song,
            SongChart chart)
        {
            ArgumentNullException.ThrowIfNull(databaseService);
            ArgumentNullException.ThrowIfNull(song);
            ArgumentNullException.ThrowIfNull(chart);

            using var context = databaseService.CreateContext();
            chart.Song = song;
            context.Songs.Add(song);
            context.SongCharts.Add(chart);
            await context.SaveChangesAsync();

            foreach (var instrument in EnumerateDeclaredInstruments(chart))
            {
                context.SongScores.Add(CreateInitialScore(chart.Id, instrument));
            }

            await context.SaveChangesAsync();

            return song.Id;
        }

        public static async Task<(int SongId, SongChart Chart)> SeedSongReturningChartAsync(
            this SongDatabaseService databaseService,
            SongEntity song,
            SongChart chart)
        {
            var songId = await databaseService.SeedSongAsync(song, chart);
            return (songId, chart);
        }

        private static System.Collections.Generic.IEnumerable<EInstrumentPart> EnumerateDeclaredInstruments(SongChart chart)
        {
            if (chart.HasDrumChart && chart.DrumLevel > 0)
            {
                yield return EInstrumentPart.DRUMS;
            }
            if (chart.HasGuitarChart && chart.GuitarLevel > 0)
            {
                yield return EInstrumentPart.GUITAR;
            }
            if (chart.HasBassChart && chart.BassLevel > 0)
            {
                yield return EInstrumentPart.BASS;
            }
        }

        private static SongScoreEntity CreateInitialScore(int chartId, EInstrumentPart instrument) => new()
        {
            ChartId = chartId,
            Instrument = instrument,
            BestScore = 0,
            BestRank = 0,
            BestSkillPoint = 0.0,
            BestAchievementRate = 0.0,
            FullCombo = false,
            Excellent = false,
            PlayCount = 0,
            ClearCount = 0,
            MaxCombo = 0,
            ProgressBar = "",
            LastScore = 0,
            LastSkillPoint = 0.0
        };
    }
}
