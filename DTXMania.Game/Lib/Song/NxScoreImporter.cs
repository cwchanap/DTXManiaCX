#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Song.Entities;
using Microsoft.EntityFrameworkCore;

namespace DTXMania.Game.Lib.Song
{
    /// <summary>
    /// Merges a parsed <see cref="NxScoreData"/> into the DRUMS SongScore for one chart,
    /// plus the per-song PerformanceHistory. Best fields use idempotent max; PlayCount /
    /// ClearCount use snapshot-delta; last-play uses newest-wins. See the design spec.
    /// </summary>
    public sealed class NxScoreImporter
    {
        /// <summary>
        /// Maps an NX ERANK ordinal (SS=0..E=6, UNKNOWN=99) to the CX percentage bucket.
        /// Returns -1 for "no rank" (unknown / out of range) meaning "leave rank unchanged".
        /// </summary>
        public static int MapNxRankToBucket(int ordinal) => ordinal switch
        {
            0 => 95,
            1 => 90,
            2 => 80,
            3 => 70,
            4 => 60,
            5 => 50,
            6 => 40,
            _ => -1,
        };

        /// <summary>
        /// Applies the merge using the caller's tracked context. The chart must be tracked
        /// (its SongId is used for history). Returns true if any row was written.
        /// </summary>
        public async Task<bool> MergeAsync(SongDbContext ctx, SongChart chart, NxScoreData data)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            if (chart == null) throw new ArgumentNullException(nameof(chart));
            if (data == null) throw new ArgumentNullException(nameof(data));

            var score = await ctx.SongScores
                .FirstOrDefaultAsync(s => s.ChartId == chart.Id && s.Instrument == EInstrumentPart.DRUMS);
            if (score == null)
            {
                score = new SongScore { ChartId = chart.Id, Instrument = EInstrumentPart.DRUMS };
                ctx.SongScores.Add(score);
            }

            // Best fields (idempotent max).
            if (data.BestScore > score.BestScore)
            {
                score.BestScore = data.BestScore;
                score.BestPerfect = data.BestPerfect;
                score.BestGreat = data.BestGreat;
                score.BestGood = data.BestGood;
                score.BestPoor = data.BestPoor;
                score.BestMiss = data.BestMiss;
            }
            score.TotalNotes = Math.Max(score.TotalNotes, data.TotalChips);
            score.MaxCombo = Math.Max(score.MaxCombo, data.BestMaxCombo);
            score.BestAchievementRate = Math.Max(score.BestAchievementRate, data.BestAchievementRate);
            score.HighSkill = Math.Max(score.HighSkill, data.HighSkill);

            int existingNorm = SongScore.NormalizeStoredBestRank(score.BestRank);
            int nxBucket = MapNxRankToBucket(data.BestRankOrdinal);
            score.BestRank = nxBucket >= 0 ? Math.Max(existingNorm, nxBucket) : existingNorm;

            bool nxFullCombo = data.BestMaxCombo > 0 &&
                data.BestMaxCombo == data.BestPerfect + data.BestGreat + data.BestGood + data.BestPoor + data.BestMiss;
            score.FullCombo = score.FullCombo || nxFullCombo;

            score.UsedKeyboard |= data.UsedKeyboard;
            score.UsedMidi |= data.UsedMidi;
            score.UsedJoypad |= data.UsedJoypad;
            score.UsedMouse |= data.UsedMouse;

            // Counters (snapshot delta).
            score.PlayCount += Math.Max(0, data.PlayCount - score.NxImportedPlayCount);
            score.NxImportedPlayCount = data.PlayCount;
            score.ClearCount += Math.Max(0, data.ClearCount - score.NxImportedClearCount);
            score.NxImportedClearCount = data.ClearCount;

            // Last play (newest wins).
            if (data.LastPlayedAt.HasValue &&
                (!score.LastPlayedAt.HasValue || data.LastPlayedAt.Value > score.LastPlayedAt.Value))
            {
                score.LastScore = data.LastScore;
                score.LastSkillPoint = data.LastSkill;
                score.LastPlayedAt = data.LastPlayedAt;
                if (!string.IsNullOrEmpty(data.LastProgress))
                    score.ProgressBar = data.LastProgress;
            }

            await MergeHistoryAsync(ctx, chart.SongId, data.History);

            await ctx.SaveChangesAsync();
            return true;
        }

        private static async Task MergeHistoryAsync(SongDbContext ctx, int songId, IReadOnlyList<NxHistoryLine> nxHistory)
        {
            if (nxHistory == null || nxHistory.Count == 0) return;

            var existing = await ctx.PerformanceHistory.Where(p => p.SongId == songId).ToListAsync();

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var candidates = new List<(string Text, DateTime Date)>();
            foreach (var e in existing)
                if (seen.Add(e.HistoryLine)) candidates.Add((e.HistoryLine, e.PerformedAt));
            foreach (var h in nxHistory)
                if (seen.Add(h.Text)) candidates.Add((h.Text, h.Date));

            var top5 = candidates.OrderByDescending(c => c.Date).Take(5).ToList();

            // Delete then re-insert so the (SongId, DisplayOrder) unique index never collides.
            ctx.PerformanceHistory.RemoveRange(existing);
            await ctx.SaveChangesAsync();

            int order = 1;
            foreach (var c in top5)
            {
                ctx.PerformanceHistory.Add(new PerformanceHistory
                {
                    SongId = songId,
                    HistoryLine = c.Text,
                    PerformedAt = c.Date,
                    DisplayOrder = order++,
                });
            }
            // Saved by the caller's SaveChangesAsync.
        }
    }
}
