using System;
using System.Linq;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SongEntity = DTXMania.Game.Lib.Song.Entities.Song;
using Xunit;

namespace DTXMania.Test.Song
{
    [Trait("Category", "Unit")]
    public class NxScoreImporterTests : IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly DbContextOptions<SongDbContext> _options;
        private readonly NxScoreImporter _importer = new();

        public NxScoreImporterTests()
        {
            _connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
            _connection.Open();
            _options = new DbContextOptionsBuilder<SongDbContext>().UseSqlite(_connection).Options;
            using var setup = new SongDbContext(_options);
            setup.Database.EnsureCreated();
        }

        public void Dispose() => _connection.Dispose();

        private SongChart SeedChart(string title = "Song", string file = "a.dtx", int? songId = null)
        {
            using var ctx = new SongDbContext(_options);
            var song = songId.HasValue
                ? ctx.Songs.First(s => s.Id == songId.Value)
                : new SongEntity { Title = title };
            var chart = new SongChart { Song = song, FilePath = file, HasDrumChart = true, DrumLevel = 78 };
            ctx.SongCharts.Add(chart);
            ctx.SaveChanges();
            return chart;
        }

        private SongScore Load(int chartId)
        {
            using var ctx = new SongDbContext(_options);
            return ctx.SongScores.AsNoTracking().First(s => s.ChartId == chartId && s.Instrument == EInstrumentPart.DRUMS);
        }

        private static NxScoreData Mas() => new()
        {
            BestScore = 958247, BestPerfect = 2293, BestGreat = 271, BestGood = 11,
            BestMaxCombo = 2575, TotalChips = 2575, BestAchievementRate = 94.37,
            HighSkill = 154.77, PlayCount = 79, ClearCount = 72, BestRankOrdinal = 1,
            LastScore = 958247, LastSkill = 154.77,
            LastPlayedAt = new DateTime(2026, 5, 15, 17, 54, 24), LastProgress = "2222",
            UsedMidi = true,
        };

        private async Task<bool> Merge(SongChart chart, NxScoreData data)
        {
            using var ctx = new SongDbContext(_options);
            var tracked = ctx.SongCharts.Include(c => c.Song).First(c => c.Id == chart.Id);
            var wrote = await _importer.MergeAsync(ctx, tracked, data);
            return wrote;
        }

        [Fact]
        public async Task FirstImport_ShouldSeedScoreAndSnapshot()
        {
            var chart = SeedChart();
            await Merge(chart, Mas());

            var s = Load(chart.Id);
            Assert.Equal(958247, s.BestScore);
            Assert.Equal(2293, s.BestPerfect);
            Assert.Equal(2575, s.MaxCombo);
            Assert.Equal(79, s.PlayCount);
            Assert.Equal(72, s.ClearCount);
            Assert.Equal(79, s.NxImportedPlayCount);
            Assert.Equal(72, s.NxImportedClearCount);
            Assert.Equal(90, s.BestRank);            // ordinal 1 -> bucket 90
            Assert.Equal("S", SongScore.RankString(s.BestRank));
            Assert.Equal(154.77, s.SongSkill, 2);    // mirrors CX: SongSkill = last skill
            Assert.True(s.UsedMidi);
        }

        [Fact]
        public async Task RepeatedUnchanged_ShouldNotInflateCounts()
        {
            var chart = SeedChart();
            await Merge(chart, Mas());
            await Merge(chart, Mas());

            var s = Load(chart.Id);
            Assert.Equal(79, s.PlayCount);
            Assert.Equal(72, s.ClearCount);
        }

        [Fact]
        public async Task IncreasedNxCounts_ShouldAddOnlyDelta()
        {
            var chart = SeedChart();
            await Merge(chart, Mas());

            var bumped = Mas();
            bumped.PlayCount = 82; bumped.ClearCount = 74;
            await Merge(chart, bumped);

            var s = Load(chart.Id);
            Assert.Equal(82, s.PlayCount);   // 79 + 3
            Assert.Equal(74, s.ClearCount);  // 72 + 2
        }

        [Fact]
        public async Task CxHigherScore_ShouldRetainCxBestAndAddCounts()
        {
            var chart = SeedChart();
            using (var ctx = new SongDbContext(_options))
            {
                ctx.SongScores.Add(new SongScore
                {
                    ChartId = chart.Id, Instrument = EInstrumentPart.DRUMS,
                    BestScore = 999999, BestPerfect = 9, PlayCount = 4, ClearCount = 4,
                    HighSkill = 200.0
                });
                ctx.SaveChanges();
            }

            await Merge(chart, Mas());

            var s = Load(chart.Id);
            Assert.Equal(999999, s.BestScore);   // CX best kept
            Assert.Equal(9, s.BestPerfect);      // CX best block kept
            Assert.Equal(200.0, s.HighSkill, 4); // max kept
            Assert.Equal(83, s.PlayCount);       // 4 + 79
            Assert.Equal(76, s.ClearCount);      // 4 + 72
        }

        [Theory]
        [InlineData(0, 95, "SS")]
        [InlineData(1, 90, "S")]
        [InlineData(2, 80, "A")]
        [InlineData(6, 40, "E")]
        public async Task MapsNxRankOrdinal_ShouldMapToBucket(int ordinal, int bucket, string label)
        {
            var chart = SeedChart(file: $"r{ordinal}.dtx");
            var data = Mas();
            data.BestRankOrdinal = ordinal;
            await Merge(chart, data);

            var s = Load(chart.Id);
            Assert.Equal(bucket, s.BestRank);
            Assert.Equal(label, SongScore.RankString(s.BestRank));
        }

        [Fact]
        public async Task UnknownRank_ShouldLeaveRankUnchanged()
        {
            var chart = SeedChart();
            var data = Mas();
            data.BestRankOrdinal = 99;
            await Merge(chart, data);

            var s = Load(chart.Id);
            Assert.Equal(0, s.BestRank); // never raised from default
        }

        [Fact]
        public async Task CxLastPlayNewer_ShouldKeepCxLastPlay()
        {
            var chart = SeedChart();
            using (var ctx = new SongDbContext(_options))
            {
                ctx.SongScores.Add(new SongScore
                {
                    ChartId = chart.Id, Instrument = EInstrumentPart.DRUMS,
                    LastScore = 123, LastPlayedAt = new DateTime(2030, 1, 1)
                });
                ctx.SaveChanges();
            }

            await Merge(chart, Mas());

            var s = Load(chart.Id);
            Assert.Equal(123, s.LastScore); // NX last-play is older -> not applied
        }

        [Fact]
        public async Task TwoChartsSameSong_ShouldMergeNewestFiveHistoryAcrossCharts()
        {
            var chart1 = SeedChart(title: "Shared", file: "mas.dtx");
            var chart2 = SeedChart(file: "ext.dtx", songId: chart1.SongId);

            // 4 lines from chart1 + 2 from chart2 = 6 distinct candidates -> top 5 drops the oldest.
            var d1 = Mas();
            d1.History = new[]
            {
                new NxHistoryLine { Text = "4.26/5/15 Cleared (S: 90)", Date = new DateTime(2026, 5, 15) },
                new NxHistoryLine { Text = "3.26/5/10 Cleared (A: 80)", Date = new DateTime(2026, 5, 10) },
                new NxHistoryLine { Text = "2.26/5/5 Cleared (A: 79)", Date = new DateTime(2026, 5, 5) },
                new NxHistoryLine { Text = "1.26/5/1 Cleared (A: 78)", Date = new DateTime(2026, 5, 1) },
            };
            var d2 = Mas();
            d2.History = new[]
            {
                new NxHistoryLine { Text = "2.26/6/5 Cleared (B: 70)", Date = new DateTime(2026, 6, 5) },
                new NxHistoryLine { Text = "1.26/6/1 Cleared (B: 68)", Date = new DateTime(2026, 6, 1) },
            };

            await Merge(chart1, d1);
            await Merge(chart2, d2);

            using var ctx = new SongDbContext(_options);
            var rows = ctx.PerformanceHistory.AsNoTracking()
                .Where(p => p.SongId == chart1.SongId)
                .OrderBy(p => p.DisplayOrder).ToList();

            Assert.Equal(5, rows.Count);                       // capped at 5 of 6
            Assert.Equal("2.26/6/5 Cleared (B: 70)", rows[0].HistoryLine); // newest first
            Assert.Equal(1, rows[0].DisplayOrder);
            Assert.Equal(5, rows[4].DisplayOrder);
            Assert.DoesNotContain(rows, r => r.HistoryLine == "1.26/5/1 Cleared (A: 78)"); // oldest dropped
        }

        [Fact]
        public async Task RepeatedHistory_ShouldBeDeduped()
        {
            var chart = SeedChart();
            var data = Mas();
            data.History = new[]
            {
                new NxHistoryLine { Text = "1.26/5/15 Cleared (S: 90)", Date = new DateTime(2026, 5, 15) },
            };

            await Merge(chart, data);
            await Merge(chart, data);

            using var ctx = new SongDbContext(_options);
            var rows = ctx.PerformanceHistory.AsNoTracking().Where(p => p.SongId == chart.SongId).ToList();
            Assert.Single(rows);
        }

        [Fact]
        public async Task NxLastPlayWins_ShouldSetSongSkill()
        {
            var chart = SeedChart();
            await Merge(chart, Mas());

            var s = Load(chart.Id);
            Assert.Equal(154.77, s.SongSkill, 2);  // mirrors CX behaviour (SongSkill = last skill)
            Assert.Equal(154.77, s.LastSkillPoint, 2);
        }

        [Fact]
        public async Task NxLastPlayWins_ShouldNormalizeTimestampToUtc()
        {
            var chart = SeedChart();
            // Simulate an NX local wall-clock timestamp (Unspecified kind).
            var localTime = new DateTime(2026, 6, 1, 20, 0, 0, DateTimeKind.Unspecified);
            var data = Mas();
            data.LastPlayedAt = localTime;
            await Merge(chart, data);

            var s = Load(chart.Id);
            // The stored value must be the UTC-equivalent of the local time.
            // SQLite/EF Core strips DateTimeKind on round-trip, so we compare ticks only.
            var expectedUtc = DateTime.SpecifyKind(localTime, DateTimeKind.Local).ToUniversalTime();
            Assert.Equal(expectedUtc.Ticks, s.LastPlayedAt!.Value.Ticks);
        }

        [Fact]
        public async Task CxLastPlayNewerInUtc_ShouldKeepCxLastPlay()
        {
            var chart = SeedChart();
            // CX stores UTC.  Seed a CX score with a recent UTC timestamp.
            // Use DateTimeKind.Utc so the intent is clear; SQLite strips Kind on round-trip.
            var cxUtc = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);
            using (var ctx = new SongDbContext(_options))
            {
                ctx.SongScores.Add(new SongScore
                {
                    ChartId = chart.Id, Instrument = EInstrumentPart.DRUMS,
                    LastScore = 500, LastPlayedAt = cxUtc
                });
                ctx.SaveChanges();
            }

            // NX has a wall-clock time from many years before — clearly older regardless of timezone.
            var nxLocal = new DateTime(2020, 1, 1, 8, 0, 0, DateTimeKind.Unspecified);
            var data = Mas();
            data.LastPlayedAt = nxLocal;
            data.LastScore = 999;
            await Merge(chart, data);

            var s = Load(chart.Id);
            Assert.Equal(500, s.LastScore);  // CX kept; NX was older in UTC terms
        }
    }
}
