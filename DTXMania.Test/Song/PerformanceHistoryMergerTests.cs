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
    /// <summary>
    /// Direct unit tests for <see cref="PerformanceHistoryMerger"/>.
    /// The merger is also exercised indirectly through NxScoreImporter and
    /// SongDatabaseService.UpdateScoreAsync, but these tests target edge cases
    /// (arg validation, empty incoming, whitespace rejection, date tie-breaks)
    /// that are hard to reach through the higher-level callers.
    /// </summary>
    [Trait("Category", "Unit")]
    public class PerformanceHistoryMergerTests : IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly DbContextOptions<SongDbContext> _options;

        public PerformanceHistoryMergerTests()
        {
            _connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
            _connection.Open();
            _options = new DbContextOptionsBuilder<SongDbContext>().UseSqlite(_connection).Options;
            using var setup = new SongDbContext(_options);
            setup.Database.EnsureCreated();
        }

        public void Dispose() => _connection.Dispose();

        private SongDbContext CreateContext() => new(_options);

        private static async Task<(int songId, int scoreId)> SeedAsync(SongDbContext ctx)
        {
            var song = new SongEntity { Title = "Merger Test" };
            var chart = new SongChart { Song = song, FilePath = "merger.dtx", DrumLevel = 50 };
            ctx.SongCharts.Add(chart);
            await ctx.SaveChangesAsync();
            var score = new SongScore { ChartId = chart.Id, Instrument = EInstrumentPart.DRUMS };
            ctx.SongScores.Add(score);
            await ctx.SaveChangesAsync();
            return (song.Id, score.Id);
        }

        private static PerformanceHistoryCandidate C(string text, DateTime date) => new(text, date);

        // ── Arg validation ──────────────────────────────────────────────

        [Fact]
        public async Task NullContext_ShouldThrowArgumentNullException()
        {
            await Assert.ThrowsAsync<ArgumentNullException>(() =>
                PerformanceHistoryMerger.MergeAsync(null!, 1, 1, Array.Empty<PerformanceHistoryCandidate>()));
        }

        [Fact]
        public async Task InvalidSongId_ShouldThrowArgumentOutOfRangeException()
        {
            using var ctx = CreateContext();
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                PerformanceHistoryMerger.MergeAsync(ctx, 0, 1, Array.Empty<PerformanceHistoryCandidate>()));
        }

        [Fact]
        public async Task InvalidSongScoreId_ShouldThrowArgumentOutOfRangeException()
        {
            using var ctx = CreateContext();
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                PerformanceHistoryMerger.MergeAsync(ctx, 1, 0, Array.Empty<PerformanceHistoryCandidate>()));
        }

        [Fact]
        public async Task NullIncoming_ShouldThrowArgumentNullException()
        {
            using var ctx = CreateContext();
            await Assert.ThrowsAsync<ArgumentNullException>(() =>
                PerformanceHistoryMerger.MergeAsync(ctx, 1, 1, null!));
        }

        // ── Empty incoming no-op ────────────────────────────────────────

        [Fact]
        public async Task EmptyIncoming_WithNoExistingRows_ShouldNotInsertAnything()
        {
            using var ctx = CreateContext();
            var (songId, scoreId) = await SeedAsync(ctx);

            await PerformanceHistoryMerger.MergeAsync(ctx, songId, scoreId, Array.Empty<PerformanceHistoryCandidate>());
            await ctx.SaveChangesAsync();

            Assert.Empty(ctx.PerformanceHistory.Where(h => h.SongScoreId == scoreId));
        }

        [Fact]
        public async Task EmptyIncoming_WithExistingRows_ShouldPreserveExistingRows()
        {
            using var ctx = CreateContext();
            var (songId, scoreId) = await SeedAsync(ctx);

            // Seed two existing rows.
            await PerformanceHistoryMerger.MergeAsync(ctx, songId, scoreId, new[]
            {
                C("1.26/6/13 Cleared (S: 90)", new DateTime(2026, 6, 13, 0, 0, 0, DateTimeKind.Utc)),
                C("2.26/6/12 Cleared (A: 80)", new DateTime(2026, 6, 12, 0, 0, 0, DateTimeKind.Utc)),
            });
            await ctx.SaveChangesAsync();
            Assert.Equal(2, ctx.PerformanceHistory.Count(h => h.SongScoreId == scoreId));

            // Re-merge with empty incoming — existing rows must survive.
            await PerformanceHistoryMerger.MergeAsync(ctx, songId, scoreId, Array.Empty<PerformanceHistoryCandidate>());
            await ctx.SaveChangesAsync();

            var rows = ctx.PerformanceHistory.Where(h => h.SongScoreId == scoreId).OrderBy(h => h.DisplayOrder).ToList();
            Assert.Equal(2, rows.Count);
            Assert.Equal("1.26/6/13 Cleared (S: 90)", rows[0].HistoryLine);
            Assert.Equal("2.26/6/12 Cleared (A: 80)", rows[1].HistoryLine);
        }

        // ── Whitespace rejection ────────────────────────────────────────

        [Fact]
        public async Task WhitespaceOnlyLines_ShouldBeRejected()
        {
            using var ctx = CreateContext();
            var (songId, scoreId) = await SeedAsync(ctx);

            await PerformanceHistoryMerger.MergeAsync(ctx, songId, scoreId, new[]
            {
                C("   ", new DateTime(2026, 6, 13)),
                C("", new DateTime(2026, 6, 12)),
                C("1.26/6/13 Cleared (S: 90)", new DateTime(2026, 6, 13)),
            });
            await ctx.SaveChangesAsync();

            var rows = ctx.PerformanceHistory.Where(h => h.SongScoreId == scoreId).ToList();
            Assert.Single(rows);
            Assert.Equal("1.26/6/13 Cleared (S: 90)", rows[0].HistoryLine);
        }

        // ── Equal-date tie-break ────────────────────────────────────────

        [Fact]
        public async Task EqualDates_ShouldPreserveBothRowsInDisplayOrder()
        {
            using var ctx = CreateContext();
            var (songId, scoreId) = await SeedAsync(ctx);

            var sameDate = new DateTime(2026, 6, 13, 10, 0, 0, DateTimeKind.Utc);
            await PerformanceHistoryMerger.MergeAsync(ctx, songId, scoreId, new[]
            {
                C("1.26/6/13 Cleared (S: 90)", sameDate),
                C("2.26/6/13 Cleared (A: 80)", sameDate),
            });
            await ctx.SaveChangesAsync();

            // Both rows should be kept (no dedup because text differs).
            var rows = ctx.PerformanceHistory.Where(h => h.SongScoreId == scoreId).OrderBy(h => h.DisplayOrder).ToList();
            Assert.Equal(2, rows.Count);
            // When dates are equal, the original iteration order (existing-first, then incoming)
            // determines the relative ordering — both survive and get distinct DisplayOrders.
            Assert.Equal(1, rows[0].DisplayOrder);
            Assert.Equal(2, rows[1].DisplayOrder);
        }

        // ── Dedup by text ───────────────────────────────────────────────

        [Fact]
        public async Task DuplicateText_ShouldCollapseToSingleRow()
        {
            using var ctx = CreateContext();
            var (songId, scoreId) = await SeedAsync(ctx);

            // Seed an existing row.
            await PerformanceHistoryMerger.MergeAsync(ctx, songId, scoreId, new[]
            {
                C("1.26/6/13 Cleared (S: 90)", new DateTime(2026, 6, 13)),
            });
            await ctx.SaveChangesAsync();

            // Re-import the same text plus a new play.
            await PerformanceHistoryMerger.MergeAsync(ctx, songId, scoreId, new[]
            {
                C("1.26/6/13 Cleared (S: 90)", new DateTime(2026, 6, 13)),
                C("2.26/6/14 Cleared (SS: 98)", new DateTime(2026, 6, 14)),
            });
            await ctx.SaveChangesAsync();

            var rows = ctx.PerformanceHistory.Where(h => h.SongScoreId == scoreId).ToList();
            Assert.Equal(2, rows.Count); // dedup collapsed the duplicate
        }
    }
}
