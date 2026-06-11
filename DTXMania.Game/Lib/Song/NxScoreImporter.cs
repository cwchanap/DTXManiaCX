#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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
        /// Applies the merge using the caller's tracked context. The chart's Id and
        /// SongId are read for score lookup and history merge. Persists all changes
        /// via SaveChangesAsync.
        /// </summary>
        public async Task MergeAsync(SongDbContext ctx, SongChart chart, NxScoreData data,
            CancellationToken cancellationToken = default)
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

            // Best fields (idempotent max).  Note stat block + BestAchievementRate are
            // written together: achievement rate is a function of the note breakdown,
            // so pairing a different play's rate with these note stats would produce
            // an inconsistent "best".  HighSkill stays as Math.Max because it is
            // computed from a different formula and is not derivable from the
            // note breakdown.
            if (data.BestScore > score.BestScore)
            {
                score.BestScore = data.BestScore;
                score.BestPerfect = data.BestPerfect;
                score.BestGreat = data.BestGreat;
                score.BestGood = data.BestGood;
                score.BestPoor = data.BestPoor;
                score.BestMiss = data.BestMiss;
                score.BestAchievementRate = data.BestAchievementRate;
            }
            score.TotalNotes = Math.Max(score.TotalNotes, data.TotalChips);
            score.MaxCombo = Math.Max(score.MaxCombo, data.BestMaxCombo);
            score.HighSkill = Math.Max(score.HighSkill, data.HighSkill);

            int existingNorm = SongScore.NormalizeStoredBestRank(score.BestRank);
            int nxBucket = MapNxRankToBucket(data.BestRankOrdinal);
            score.BestRank = nxBucket >= 0 ? Math.Max(existingNorm, nxBucket) : existingNorm;

            // Full combo means the player hit every chip in the chart without missing.
            // Comparing BestMaxCombo against the sum of the judgment counts is wrong:
            // a partial play (e.g. 500/1000 chips with no misses) would satisfy
            // BestMaxCombo == (Perfect+Great+Good+Poor+Miss) even though TotalChips
            // is larger, incorrectly flagging the chart as full-comboed.
            bool nxFullCombo = data.BestMaxCombo > 0 && data.BestMaxCombo == data.TotalChips;
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

            // Last play (newest wins).  NX timestamps are local wall-clock (Unspecified);
            // CX timestamps are UTC.  Normalize both to UTC before comparing / storing so
            // that the comparison is not skewed by the local UTC offset.
            if (data.LastPlayedAt.HasValue)
            {
                var nxUtc = ToUtc(data.LastPlayedAt.Value);
                if (!score.LastPlayedAt.HasValue || nxUtc > score.LastPlayedAt.Value)
                {
                    score.LastScore = data.LastScore;
                    score.LastSkillPoint = data.LastSkill;
                    score.SongSkill = data.LastSkill;
                    score.LastPlayedAt = nxUtc;
                    if (!string.IsNullOrEmpty(data.LastProgress))
                        score.ProgressBar = data.LastProgress;
                }
            }

            await MergeHistoryAsync(ctx, chart.SongId, data.History);

            await ctx.SaveChangesAsync(cancellationToken);
        }

        private static async Task MergeHistoryAsync(SongDbContext ctx, int songId, IReadOnlyList<NxHistoryLine> nxHistory)
        {
            if (nxHistory == null || nxHistory.Count == 0) return;

            var existing = await ctx.PerformanceHistory.Where(p => p.SongId == songId).ToListAsync();

            // Dedup strategy: existing DB rows are added first, then NX rows. On a
            // HistoryLine text collision the existing (CX) entry wins because the
            // NX text is the same, so the NX add is a no-op rather than a date
            // overwrite. In practice the NX text encodes a date and a difficulty
            // bucket, so collisions are rare; if a collision ever does occur the
            // result is "first-seen wins" with no warning.
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var candidates = new List<(string Text, DateTime Date)>();
            foreach (var e in existing)
                if (seen.Add(e.HistoryLine)) candidates.Add((e.HistoryLine, e.PerformedAt));
            foreach (var h in nxHistory)
                if (seen.Add(h.Text)) candidates.Add((h.Text, h.Date));

            var top5 = candidates.OrderByDescending(c => c.Date).Take(5).ToList();

            // Delete then re-insert so the (SongId, DisplayOrder) unique index never collides.
            // Deletions and inserts are staged in the tracked context; the caller's single
            // SaveChangesAsync persists everything atomically.
            ctx.PerformanceHistory.RemoveRange(existing);

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
        }

        /// <summary>
        /// Converts an NX-sourced <see cref="DateTime"/> to UTC for comparison with CX
        /// timestamps (which are always UTC).  NX .score.ini files store local wall-clock
        /// times parsed as <see cref="DateTimeKind.Unspecified"/>; we treat those as local.
        /// </summary>
        private static DateTime ToUtc(DateTime dt)
        {
            return dt.Kind switch
            {
                DateTimeKind.Utc => dt,
                DateTimeKind.Local => dt.ToUniversalTime(),
                _ => DateTime.SpecifyKind(dt, DateTimeKind.Local).ToUniversalTime(),
            };
        }
    }
}
