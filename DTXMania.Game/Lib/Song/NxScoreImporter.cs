#nullable enable
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Song.Entities;
using Microsoft.EntityFrameworkCore;

namespace DTXMania.Game.Lib.Song
{
    /// <summary>
    /// Merges a parsed <see cref="NxScoreData"/> into the DRUMS SongScore for one chart,
    /// plus the per-score PerformanceHistory. Best fields use idempotent max; PlayCount /
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

            var transaction = ctx.Database.CurrentTransaction == null
                ? await ctx.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false)
                : null;

            try
            {
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

                // Counters (snapshot delta).  Watermarks are kept monotonic so that
                // importing an older/restored .score.ini whose counts are lower does
                // not reset the watermark; re-importing the newer file would then
                // add the same plays/clears a second time (idempotent counter merge).
                score.PlayCount += Math.Max(0, data.PlayCount - score.NxImportedPlayCount);
                score.NxImportedPlayCount = Math.Max(score.NxImportedPlayCount, data.PlayCount);
                score.ClearCount += Math.Max(0, data.ClearCount - score.NxImportedClearCount);
                score.NxImportedClearCount = Math.Max(score.NxImportedClearCount, data.ClearCount);

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

                if (score.Id == 0)
                {
                    await ctx.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                }

                await PerformanceHistoryMerger.MergeAsync(
                    ctx,
                    chart.SongId,
                    score.Id,
                    data.History.Select(h => new PerformanceHistoryCandidate(h.Text, h.Date)),
                    cancellationToken).ConfigureAwait(false);

                await ctx.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                if (transaction != null)
                {
                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                if (transaction != null)
                {
                    await transaction.DisposeAsync().ConfigureAwait(false);
                }
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
