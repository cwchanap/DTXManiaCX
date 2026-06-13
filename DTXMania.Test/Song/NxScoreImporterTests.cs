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

        /// <summary>
        /// Same chart and BestMaxCombo as <see cref="Mas"/>, but TotalChips is smaller
        /// than BestMaxCombo, so the play did not cover the whole chart and must
        /// not be flagged as a full combo.  Mirrors the partial-play scenario the
        /// old "sum of judgments" formula failed to reject.
        /// </summary>
        private static NxScoreData PartialPlay() => new()
        {
            BestScore = 500000, BestPerfect = 500, BestGreat = 0, BestGood = 0,
            BestPoor = 0, BestMiss = 0, BestMaxCombo = 500, TotalChips = 1000,
            BestAchievementRate = 50.0, PlayCount = 1, ClearCount = 0,
        };

        private async Task Merge(SongChart chart, NxScoreData data)
        {
            using var ctx = new SongDbContext(_options);
            var tracked = ctx.SongCharts.Include(c => c.Song).First(c => c.Id == chart.Id);
            await _importer.MergeAsync(ctx, tracked, data);
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
            // Mas() does not set these flags, so the OR-merge must leave them at their default.
            Assert.False(s.UsedKeyboard);
            Assert.False(s.UsedJoypad);
            Assert.False(s.UsedMouse);
            // Mas() satisfies BestMaxCombo == TotalChips (2575 == 2575), so FullCombo must be set.
            Assert.True(s.FullCombo);
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
        public async Task TwoChartsSameSong_ShouldKeepHistoryPerChartScore()
        {
            var chart1 = SeedChart(title: "Shared", file: "mas.dtx");
            var chart2 = SeedChart(file: "ext.dtx", songId: chart1.SongId);

            var d1 = Mas();
            d1.History = new[]
            {
                new NxHistoryLine { Text = "4.26/5/15 Cleared (S: 90)", Date = new DateTime(2026, 5, 15) },
                new NxHistoryLine { Text = "3.26/5/10 Cleared (A: 80)", Date = new DateTime(2026, 5, 10) },
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
            var score1 = ctx.SongScores.AsNoTracking().Single(s => s.ChartId == chart1.Id);
            var score2 = ctx.SongScores.AsNoTracking().Single(s => s.ChartId == chart2.Id);

            var rows1 = ctx.PerformanceHistory.AsNoTracking()
                .Where(p => p.SongScoreId == score1.Id)
                .OrderBy(p => p.DisplayOrder)
                .ToList();
            var rows2 = ctx.PerformanceHistory.AsNoTracking()
                .Where(p => p.SongScoreId == score2.Id)
                .OrderBy(p => p.DisplayOrder)
                .ToList();

            Assert.Equal(new[] { "4.26/5/15 Cleared (S: 90)", "3.26/5/10 Cleared (A: 80)" },
                rows1.Select(r => r.HistoryLine).ToArray());
            Assert.Equal(new[] { "2.26/6/5 Cleared (B: 70)", "1.26/6/1 Cleared (B: 68)" },
                rows2.Select(r => r.HistoryLine).ToArray());
            Assert.All(rows1, row => Assert.Equal(score1.Id, row.SongScoreId));
            Assert.All(rows2, row => Assert.Equal(score2.Id, row.SongScoreId));
        }

        [Fact]
        public async Task SixHistoryRowsForOneScore_ShouldKeepNewestFive()
        {
            var chart = SeedChart();
            var data = Mas();
            data.History = Enumerable.Range(1, 6)
                .Select(i => new NxHistoryLine
                {
                    Text = $"{i}.26/6/{i} Cleared (A: {70 + i})",
                    Date = new DateTime(2026, 6, i)
                })
                .ToArray();

            await Merge(chart, data);

            using var ctx = new SongDbContext(_options);
            var score = ctx.SongScores.AsNoTracking().Single(s => s.ChartId == chart.Id);
            var rows = ctx.PerformanceHistory.AsNoTracking()
                .Where(p => p.SongScoreId == score.Id)
                .OrderBy(p => p.DisplayOrder)
                .ToList();

            Assert.Equal(5, rows.Count);
            Assert.Equal("6.26/6/6 Cleared (A: 76)", rows[0].HistoryLine);
            Assert.Equal(1, rows[0].DisplayOrder);
            Assert.Equal(5, rows[4].DisplayOrder);
            Assert.DoesNotContain(rows, r => r.HistoryLine == "1.26/6/1 Cleared (A: 71)");
            Assert.All(rows, row => Assert.Equal(score.Id, row.SongScoreId));
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
            var score = ctx.SongScores.AsNoTracking().Single(s => s.ChartId == chart.Id);
            var rows = ctx.PerformanceHistory.AsNoTracking()
                .Where(p => p.SongScoreId == score.Id)
                .ToList();

            Assert.Single(rows);
            Assert.Equal(score.Id, rows[0].SongScoreId);
        }

        [Fact]
        public async Task ExistingScoreScopedHistoryWithSameLine_ShouldWinOverIncomingNxLine()
        {
            var chart = SeedChart();
            var historyLine = "1.26/5/15 Cleared (S: 90)";
            int scoreId;

            using (var ctx = new SongDbContext(_options))
            {
                var score = new SongScore { ChartId = chart.Id, Instrument = EInstrumentPart.DRUMS };
                ctx.SongScores.Add(score);
                ctx.SaveChanges();

                scoreId = score.Id;
                ctx.PerformanceHistory.Add(new PerformanceHistory
                {
                    SongId = chart.SongId,
                    SongScoreId = scoreId,
                    HistoryLine = historyLine,
                    PerformedAt = new DateTime(2026, 5, 1),
                    DisplayOrder = 1,
                });
                ctx.SaveChanges();
            }

            var data = Mas();
            data.History = new[]
            {
                new NxHistoryLine { Text = historyLine, Date = new DateTime(2026, 6, 1) },
            };

            await Merge(chart, data);

            using var verifyCtx = new SongDbContext(_options);
            var row = verifyCtx.PerformanceHistory.AsNoTracking()
                .Single(p => p.SongScoreId == scoreId);

            Assert.Equal(historyLine, row.HistoryLine);
            Assert.Equal(new DateTime(2026, 5, 1), row.PerformedAt);
        }

        [Fact]
        public async Task LegacySongLevelHistory_ShouldNotMergeIntoScoreScopedHistory()
        {
            var chart = SeedChart();
            using (var ctx = new SongDbContext(_options))
            {
                ctx.PerformanceHistory.Add(new PerformanceHistory
                {
                    SongId = chart.SongId,
                    SongScoreId = null,
                    HistoryLine = "legacy song-level row",
                    PerformedAt = new DateTime(2030, 1, 1),
                    DisplayOrder = 1,
                });
                ctx.SaveChanges();
            }

            var data = Mas();
            data.History = new[]
            {
                new NxHistoryLine { Text = "1.26/5/15 Cleared (S: 90)", Date = new DateTime(2026, 5, 15) },
            };

            await Merge(chart, data);

            using var verifyCtx = new SongDbContext(_options);
            var score = verifyCtx.SongScores.AsNoTracking().Single(s => s.ChartId == chart.Id);
            var scopedRows = verifyCtx.PerformanceHistory.AsNoTracking()
                .Where(p => p.SongScoreId == score.Id)
                .Select(p => p.HistoryLine)
                .ToList();
            var legacyRows = verifyCtx.PerformanceHistory.AsNoTracking()
                .Where(p => p.SongScoreId == null)
                .Select(p => p.HistoryLine)
                .ToList();

            Assert.Equal(new[] { "1.26/5/15 Cleared (S: 90)" }, scopedRows);
            Assert.Equal(new[] { "legacy song-level row" }, legacyRows);
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

        [Fact]
        public async Task NullContext_ShouldThrowArgumentNullException()
        {
            var chart = SeedChart();
            await Assert.ThrowsAsync<ArgumentNullException>(
                () => _importer.MergeAsync(null!, chart, Mas()));
        }

        [Fact]
        public async Task NullChart_ShouldThrowArgumentNullException()
        {
            using var ctx = new SongDbContext(_options);
            await Assert.ThrowsAsync<ArgumentNullException>(
                () => _importer.MergeAsync(ctx, null!, Mas()));
        }

        [Fact]
        public async Task NullData_ShouldThrowArgumentNullException()
        {
            var chart = SeedChart();
            using var ctx = new SongDbContext(_options);
            var tracked = ctx.SongCharts.Include(c => c.Song).First(c => c.Id == chart.Id);
            await Assert.ThrowsAsync<ArgumentNullException>(
                () => _importer.MergeAsync(ctx, tracked, null!));
        }

        [Fact]
        public async Task NoLastPlayedAt_ShouldNotUpdateLastPlay()
        {
            var chart = SeedChart();
            var data = Mas();
            data.LastPlayedAt = null;
            await Merge(chart, data);

            var s = Load(chart.Id);
            Assert.Null(s.LastPlayedAt);
        }

        [Fact]
        public async Task EmptyLastProgress_ShouldNotUpdateProgress()
        {
            var chart = SeedChart();
            var data = Mas();
            data.LastProgress = "";
            await Merge(chart, data);

            var s = Load(chart.Id);
            Assert.Empty(s.ProgressBar);
        }

        [Fact]
        public async Task NxFullComboFalse_ShouldNotSetFullCombo()
        {
            var chart = SeedChart();
            var data = Mas();
            data.BestMaxCombo = 10; // less than total notes (2575)
            await Merge(chart, data);

            var s = Load(chart.Id);
            Assert.False(s.FullCombo);
        }

        [Fact]
        public async Task NxFullComboTrue_ShouldSetFullCombo()
        {
            // Mas() satisfies the new formula: BestMaxCombo (2575) == TotalChips (2575)
            // and the BestPerfect + BestGreat + BestGood (2293+271+11 == 2575) is the
            // whole chart, so the result is a legitimate full combo.
            var chart = SeedChart();
            await Merge(chart, Mas());

            var s = Load(chart.Id);
            Assert.True(s.FullCombo);
        }

        [Fact]
        public async Task NxPartialPlayWithoutMisses_ShouldNotSetFullCombo()
        {
            // Regression guard: the old "sum of judgments" formula wrongly flagged
            // a partial play (e.g. player quits with no misses) as full combo,
            // because BestMaxCombo (500) equalled BestPerfect+BestGreat+...+BestMiss
            // (500) while TotalChips was 1000.  The new formula rejects this.
            var chart = SeedChart();
            await Merge(chart, PartialPlay());

            var s = Load(chart.Id);
            Assert.False(s.FullCombo);
            Assert.Equal(500, s.MaxCombo);    // NX combo is still recorded
            Assert.Equal(1000, s.TotalNotes); // TotalChips still wins (it is the only TotalNotes source)
        }

        [Fact]
        public async Task CxHigherScoreWithHigherNxAchievementRate_ShouldKeepCxRate()
        {
            // Regression guard for the BestAchievementRate consistency fix:
            // achievement rate is a function of the note breakdown, so when CX's
            // best play is retained, CX's rate must be retained too — we must not
            // pair CX's note stats with NX's rate from a different play.
            var chart = SeedChart();
            using (var ctx = new SongDbContext(_options))
            {
                ctx.SongScores.Add(new SongScore
                {
                    ChartId = chart.Id, Instrument = EInstrumentPart.DRUMS,
                    BestScore = 999999, BestPerfect = 9, BestAchievementRate = 50.0,
                });
                ctx.SaveChanges();
            }

            // NX reports a lower score but a higher rate from a different play.
            var data = Mas();
            data.BestScore = 100000;
            data.BestAchievementRate = 99.9;
            await Merge(chart, data);

            var s = Load(chart.Id);
            Assert.Equal(999999, s.BestScore);   // CX best kept
            Assert.Equal(9, s.BestPerfect);      // CX best block kept
            Assert.Equal(50.0, s.BestAchievementRate, 4); // CX rate kept (NX's higher rate not independently maxed)
        }

        [Fact]
        public async Task NxHigherScore_ShouldOverwriteAchievementRateAlongsideNoteStats()
        {
            // The other half of the consistency contract: when NX wins on BestScore,
            // its BestAchievementRate replaces the CX rate, not the max of the two.
            var chart = SeedChart();
            using (var ctx = new SongDbContext(_options))
            {
                ctx.SongScores.Add(new SongScore
                {
                    ChartId = chart.Id, Instrument = EInstrumentPart.DRUMS,
                    BestScore = 100, BestAchievementRate = 99.0,
                });
                ctx.SaveChanges();
            }

            // NX has the higher BestScore, so its rate must replace (not max with) CX's.
            var data = Mas(); // BestScore=958247, BestAchievementRate=94.37
            await Merge(chart, data);

            var s = Load(chart.Id);
            Assert.Equal(958247, s.BestScore);
            Assert.Equal(94.37, s.BestAchievementRate, 4);
        }

        [Fact]
        public async Task InputFlags_ShouldOrMergeAcrossImports()
        {
            // Each import OR-merges the four flags: a flag set in any source stays set.
            var chart = SeedChart();
            var data1 = Mas();
            data1.UsedKeyboard = true;
            await Merge(chart, data1);

            var s1 = Load(chart.Id);
            Assert.True(s1.UsedKeyboard);
            Assert.True(s1.UsedMidi);
            Assert.False(s1.UsedJoypad);
            Assert.False(s1.UsedMouse);

            // A second import that sets a different flag should not clear the first.
            var data2 = Mas();
            data2.UsedKeyboard = false; // already set in CX; OR-merge must keep it
            data2.UsedJoypad = true;
            data2.UsedMouse = true;
            await Merge(chart, data2);

            var s2 = Load(chart.Id);
            Assert.True(s2.UsedKeyboard);
            Assert.True(s2.UsedMidi);
            Assert.True(s2.UsedJoypad);
            Assert.True(s2.UsedMouse);
        }

        [Fact]
        public async Task UtcTimestamp_ShouldRemainUtc()
        {
            var chart = SeedChart();
            var utcTime = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);
            var data = Mas();
            data.LastPlayedAt = utcTime;
            await Merge(chart, data);

            var s = Load(chart.Id);
            // UTC input should be stored as-is (ticks match).
            Assert.Equal(utcTime.Ticks, s.LastPlayedAt!.Value.Ticks);
        }

        [Fact]
        public async Task DecreasedNxSnapshot_ShouldClampToZeroDeltaAndPreserveWatermark()
        {
            // If the NX file now reports fewer plays than the last import snapshot
            // (e.g. the user deleted and re-ran with an older backup), Math.Max(0,...)
            // should prevent PlayCount from decreasing AND the watermark must stay
            // monotonic so re-importing the newer file does not double-count.
            var chart = SeedChart();

            // First import: NX says 79 plays, 72 clears.
            await Merge(chart, Mas());

            // Second import: NX file was replaced with an older backup (fewer plays).
            var older = Mas();
            older.PlayCount = 50;
            older.ClearCount = 40;
            await Merge(chart, older);

            var s = Load(chart.Id);
            // PlayCount must not go down — delta is max(0, 50-79) = 0.
            Assert.Equal(79, s.PlayCount);
            Assert.Equal(72, s.ClearCount);
            // Watermark stays at the highest value seen so far so that re-importing the
            // newer file does not double-count the delta.
            Assert.Equal(79, s.NxImportedPlayCount);
            Assert.Equal(72, s.NxImportedClearCount);
        }

        [Fact]
        public async Task ReimportNewerAfterOlder_ShouldNotDoubleCount()
        {
            // Regression: import newer (79), then older (50), then newer again (82).
            // The watermark must stay monotonic so the final delta is 82-79=3, not
            // 82-50=32 (which would double-count 29 plays already accounted for).
            var chart = SeedChart();

            // Import the latest NX file (79 plays).
            await Merge(chart, Mas());

            // Import an older backup (50 plays). Watermark must not drop.
            var older = Mas();
            older.PlayCount = 50;
            older.ClearCount = 40;
            await Merge(chart, older);

            // Re-import the latest NX file with fresh data (82 plays).
            var newer = Mas();
            newer.PlayCount = 82;
            newer.ClearCount = 76;
            await Merge(chart, newer);

            var s = Load(chart.Id);
            Assert.Equal(82, s.PlayCount);   // 79 + 3 (not 79 + 32)
            Assert.Equal(76, s.ClearCount);  // 72 + 4 (not 72 + 36)
            Assert.Equal(82, s.NxImportedPlayCount);
            Assert.Equal(76, s.NxImportedClearCount);
        }

        [Fact]
        public async Task HistorySaveFailureForNewScore_ShouldRollbackScoreImport()
        {
            var chart = SeedChart();
            var invalidSongChart = new SongChart { Id = chart.Id, SongId = 999999, FilePath = chart.FilePath };
            var data = Mas();
            data.History = new[]
            {
                new NxHistoryLine { Text = "1.26/6/13 Cleared (S: 90)", Date = new DateTime(2026, 6, 13) },
            };

            using (var ctx = new SongDbContext(_options))
            {
                await Assert.ThrowsAsync<DbUpdateException>(
                    () => _importer.MergeAsync(ctx, invalidSongChart, data));
            }

            using (var verifyCtx = new SongDbContext(_options))
            {
                var score = verifyCtx.SongScores.AsNoTracking()
                    .FirstOrDefault(s => s.ChartId == chart.Id && s.Instrument == EInstrumentPart.DRUMS);
                Assert.Null(score);
            }
        }

        [Fact]
        public async Task ErrorOnChartN_ShouldNotAffectChartN1()
        {
            // Seed two charts for the same song.
            var chart1 = SeedChart(title: "Shared", file: "chart1.dtx");
            var chart2 = SeedChart(file: "chart2.dtx", songId: chart1.SongId);

            // Merge chart1 successfully.
            await Merge(chart1, Mas());

            // Force chart2's merge to fail by passing null data.
            using (var ctx = new SongDbContext(_options))
            {
                var tracked2 = ctx.SongCharts.Include(c => c.Song).First(c => c.Id == chart2.Id);
                await Assert.ThrowsAsync<ArgumentNullException>(
                    () => _importer.MergeAsync(ctx, tracked2, null!));
            }

            // Verify chart1's persisted data is unchanged.
            var s1 = Load(chart1.Id);
            Assert.Equal(958247, s1.BestScore);
            Assert.Equal(79, s1.PlayCount);

            // Verify chart2 has no persisted score.
            using (var verifyCtx = new SongDbContext(_options))
            {
                var chart2Score = verifyCtx.SongScores.AsNoTracking()
                    .FirstOrDefault(s => s.ChartId == chart2.Id && s.Instrument == EInstrumentPart.DRUMS);
                Assert.Null(chart2Score);
            }
        }
    }
}
