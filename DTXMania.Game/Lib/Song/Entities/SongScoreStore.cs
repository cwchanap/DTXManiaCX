using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Stage.Performance;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SongEntity = DTXMania.Game.Lib.Song.Entities.Song;
using SongScoreEntity = DTXMania.Game.Lib.Song.Entities.SongScore;

namespace DTXMania.Game.Lib.Song.Entities
{
    /// <summary>
    /// Score-side persistence over one shared <see cref="SongDatabase"/>:
    /// transactional score saves, RunId receipt idempotency/race resolution
    /// and score/history reads (HPA-801). Bulk import and chart inventory
    /// live on <see cref="SongCatalogStore"/>.
    /// </summary>
    public class SongScoreStore
    {
        private readonly SongDatabase _database;

        public SongScoreStore(SongDatabase database)
        {
            _database = database ?? throw new ArgumentNullException(nameof(database));
        }

        /// <summary>
        /// Persists a complete PerformanceSummary into the SongScore for the given chart+instrument.
        /// Updates best fields only when the new score exceeds the existing best. Always updates the
        /// "last play" fields and increments PlayCount. The pre-computed summary.GameSkill is assigned
        /// directly (not re-derived via score.CalculateSkill()), preserving level-decimal precision.
        /// </summary>
        public async Task<ScoreSaveResult> UpdateScoreAsync(
            int chartId,
            EInstrumentPart instrument,
            PerformanceSummary summary,
            CancellationToken cancellationToken = default)
        {
            if (summary == null) throw new ArgumentNullException(nameof(summary));
            if (summary.PlaySpeedPercent < PlaySpeedRange.Min
                || summary.PlaySpeedPercent > PlaySpeedRange.Max
                || summary.PlaySpeedPercent % PlaySpeedRange.Step != 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(summary),
                    summary.PlaySpeedPercent,
                    "PerformanceSummary.PlaySpeedPercent must be a canonical gameplay speed.");
            }

            // Pre-feature callers did not carry a run id. Preserve those direct service
            // calls as non-idempotent legacy writes while all modern gameplay summaries
            // use their frozen, non-empty RunId.
            var runId = summary.RunId == Guid.Empty
                ? Guid.NewGuid()
                : summary.RunId;

            try
            {
                return await SaveScoreTransactionAsync(
                    chartId,
                    instrument,
                    summary,
                    runId,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsSqliteWriteRace(ex))
            {
                // First, check whether this run was already saved (same-run
                // retry/idempotency). The winner may share this RunId if the
                // caller retried the same performance.
                var resolved = await ResolveReceiptAfterRaceAsync(
                    runId,
                    chartId,
                    instrument,
                    summary.PlaySpeedPercent,
                    cancellationToken).ConfigureAwait(false);
                if (resolved != null)
                    return resolved;

                // The race was against a different run (the winner has a different
                // RunId, so the receipt lookup above found nothing). Retry the
                // transaction: the winner's score now exists in the database, so
                // the retry will find and update it instead of inserting a
                // duplicate. The receipt check at the top of
                // SaveScoreTransactionAsync preserves same-run idempotency.
                // Limit retries to avoid unbounded loops under sustained
                // contention.
                const int maxRetries = 3;
                for (int attempt = 0; ; attempt++)
                {
                    try
                    {
                        return await SaveScoreTransactionAsync(
                            chartId,
                            instrument,
                            summary,
                            runId,
                            cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception retryEx) when (IsSqliteWriteRace(retryEx) && attempt < maxRetries)
                    {
                        await Task.Delay(
                            10 * (attempt + 1),
                            cancellationToken).ConfigureAwait(false);
                    }
                }
            }
        }

        private async Task<ScoreSaveResult> SaveScoreTransactionAsync(
            int chartId,
            EInstrumentPart instrument,
            PerformanceSummary summary,
            Guid runId,
            CancellationToken cancellationToken)
        {
            using var context = _database.CreateContext();
            await using var transaction = await context.Database.BeginTransactionAsync(
                cancellationToken);

            // Receipt identity is deliberately checked before chart or score lookup.
            // This keeps retries valid after stale cleanup removes the related score
            // and nulls the optional SongScoreId foreign key.
            var existingReceipt = await context.ScoreSaveReceipts
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    receipt => receipt.RunId == runId,
                    cancellationToken)
                .ConfigureAwait(false);
            if (existingReceipt != null)
            {
                return ResolveExistingReceipt(
                    existingReceipt,
                    chartId,
                    instrument,
                    summary.PlaySpeedPercent);
            }

            var score = await context.SongScores
                .Include(s => s.Chart)
                .FirstOrDefaultAsync(
                    s => s.ChartId == chartId
                        && s.Instrument == instrument
                        && s.PlaySpeedPercent == summary.PlaySpeedPercent,
                    cancellationToken)
                .ConfigureAwait(false);
            bool wasNewlyCreated = false;
            if (score == null)
            {
                var chart = await context.SongCharts.FirstOrDefaultAsync(
                    chart => chart.Id == chartId,
                    cancellationToken)
                    .ConfigureAwait(false)
                    ?? throw new KeyNotFoundException($"Chart with Id {chartId} was not found.");
                score = new SongScoreEntity
                {
                    ChartId = chartId,
                    Chart = chart,
                    Instrument = instrument,
                    PlaySpeedPercent = summary.PlaySpeedPercent
                };
                context.SongScores.Add(score);
                wasNewlyCreated = true;
            }

            // Only force-overwrite the best fields when the score row is
            // genuinely empty — either created in this transaction, or a
            // placeholder row (PlayCount == 0 AND BestScore == 0).
            // NX import can persist a BestScore > 0 with
            // PlayCount == 0 (NxScoreData.HasDrumData allows that combination,
            // and NxScoreImporter writes best fields without requiring
            // PlayCount > 0). Using PlayCount == 0 alone as the "first play"
            // signal would treat that imported row as empty and overwrite the
            // imported best even when the new CX result is worse. Compare each
            // best field normally when the row already carries imported data.
            var isFirstPlay = wasNewlyCreated
                || (score.PlayCount == 0 && score.BestScore == 0);
            if (isFirstPlay || summary.Score > score.BestScore)
            {
                score.BestScore = summary.Score;
                score.BestPerfect = summary.PerfectCount;
                score.BestGreat = summary.GreatCount;
                score.BestGood = summary.GoodCount;
                score.BestPoor = summary.PoorCount;
                score.BestMiss = summary.MissCount;
                score.TotalNotes = summary.TotalNotes;
            }

            var normalizedRunRank = SongScoreEntity.RunRankBucket(summary.PlayingSkill);
            var normalizedBestRank = isFirstPlay
                ? 0
                : SongScoreEntity.NormalizeRankPercentage(score.BestRank);
            if (isFirstPlay || normalizedRunRank > normalizedBestRank)
                score.BestRank = normalizedRunRank;
            else
                score.BestRank = normalizedBestRank;

            score.MaxCombo = Math.Max(score.MaxCombo, summary.MaxCombo);
            score.FullCombo = score.FullCombo ||
                (summary.ClearFlag &&
                 summary.MissCount == 0 &&
                 summary.PoorCount == 0);

            if (summary.GameSkill > score.HighSkill)
            {
                score.HighSkill = summary.GameSkill;
                // NX stores the achievement rate (達成率 / playing skill, 0-100) on a new skill record
                // (CStageResult: HighSkill[m] = dbPerformanceSkill when bNewRecordSkill). The song-select
                // panel reads this as the per-cell percentage, so it must be persisted alongside HighSkill.
                score.BestAchievementRate = summary.PlayingSkill;
            }
            score.SongSkill = summary.GameSkill;

            score.LastScore = summary.Score;
            score.LastSkillPoint = summary.GameSkill;
            var nowUtc = DateTime.UtcNow;
            score.LastPlayedAt = nowUtc;
            score.PlayCount++;
            if (summary.ClearFlag) score.ClearCount++;

            await context.SaveChangesAsync(cancellationToken)
                .ConfigureAwait(false);

            var localDate = nowUtc.ToLocalTime();
            var rank = SongScoreEntity.RankString(normalizedRunRank);
            var status = summary.ClearFlag ? "Cleared" : "Failed";
            var line = string.Format(
                CultureInfo.InvariantCulture,
                "{0}.{1:yy/M/d} {2} ({3}: {4}) [{5}, {6}]",
                score.PlayCount,
                localDate,
                status,
                rank,
                SkillValueFormatter.Format(summary.PlayingSkill),
                PlaySpeedRange.Format(summary.PlaySpeedPercent),
                PitchRange.Format(summary.PitchSemitones));

            await PerformanceHistoryMerger.MergeAsync(
                context,
                score.Chart.SongId,
                score.Id,
                new[]
                {
                    new PerformanceHistoryCandidate(
                        line,
                        nowUtc,
                        summary.PitchSemitones)
                },
                cancellationToken).ConfigureAwait(false);

            context.ScoreSaveReceipts.Add(new ScoreSaveReceipt
            {
                RunId = runId,
                ChartId = chartId,
                Instrument = instrument,
                PlaySpeedPercent = summary.PlaySpeedPercent,
                SongScoreId = score.Id,
                SavedAtUtc = nowUtc
            });

            await context.SaveChangesAsync(cancellationToken)
                .ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken)
                .ConfigureAwait(false);
            return ScoreSaveResult.Saved(score.Id);
        }

        private static ScoreSaveResult ResolveExistingReceipt(
            ScoreSaveReceipt receipt,
            int chartId,
            EInstrumentPart instrument,
            int playSpeedPercent)
        {
            if (receipt.ChartId == chartId
                && receipt.Instrument == instrument
                && receipt.PlaySpeedPercent == playSpeedPercent)
            {
                return ScoreSaveResult.AlreadySaved(receipt.SongScoreId);
            }

            return ScoreSaveResult.Failed(
                $"RunId collision: {receipt.RunId} is already stored for " +
                $"chart {receipt.ChartId}, instrument {receipt.Instrument}, " +
                $"speed {receipt.PlaySpeedPercent}, not chart {chartId}, " +
                $"instrument {instrument}, speed {playSpeedPercent}.");
        }

        private async Task<ScoreSaveResult> ResolveReceiptAfterRaceAsync(
            Guid runId,
            int chartId,
            EInstrumentPart instrument,
            int playSpeedPercent,
            CancellationToken cancellationToken)
        {
            const int maxAttempts = 20;
            for (var attempt = 0; attempt < maxAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    using var context = _database.CreateContext();
                    var receipt = await context.ScoreSaveReceipts
                        .AsNoTracking()
                        .FirstOrDefaultAsync(
                            candidate => candidate.RunId == runId,
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (receipt != null)
                    {
                        return ResolveExistingReceipt(
                            receipt,
                            chartId,
                            instrument,
                            playSpeedPercent);
                    }
                }
                catch (SqliteException ex) when (
                    ex.SqliteErrorCode == 5
                    || ex.SqliteErrorCode == 6)
                {
                    // The winning transaction still owns the database lock.
                }

                await Task.Delay(
                    TimeSpan.FromMilliseconds(10 + (attempt * 5)),
                    cancellationToken).ConfigureAwait(false);
            }

            return null;
        }

        private static bool IsSqliteWriteRace(Exception exception)
        {
            for (var current = exception; current != null; current = current.InnerException)
            {
                if (current is SqliteException sqlite
                    && (sqlite.SqliteErrorCode == 5
                        || sqlite.SqliteErrorCode == 6
                        || sqlite.SqliteExtendedErrorCode == 1555
                        || sqlite.SqliteExtendedErrorCode == 2067))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Loads one exact chart/instrument/speed score variant with its own history.
        /// Returns null when that specific speed has not been played; another speed's
        /// score is never substituted.
        /// </summary>
        public async Task<SongScoreEntity> GetScoreWithHistoryAsync(
            int chartId,
            EInstrumentPart instrument,
            int playSpeedPercent,
            CancellationToken cancellationToken = default)
        {
            using var context = _database.CreateContext();

            return await context.SongScores
                .Include(s => s.Chart)
                .Include(s => s.PerformanceHistory)
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    s => s.ChartId == chartId
                        && s.Instrument == instrument
                        && s.PlaySpeedPercent == playSpeedPercent,
                    cancellationToken)
                .ConfigureAwait(false);
        }

    }
}
