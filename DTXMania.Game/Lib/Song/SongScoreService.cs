#nullable enable

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.Stage.Performance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DTXMania.Game.Lib.Song
{
    /// <summary>
    /// Score persistence orchestration over the game-owned database (HPA-801
    /// Decision D): persists a Result summary through
    /// <see cref="SongScoreStore"/> — idempotent via <c>RunId</c> receipts —
    /// and then refreshes the matching chart/instrument/speed variant through
    /// the library's locked in-memory refresh delegate. Also owns the
    /// DTXManiaNX score.ini import. Constructed by <see cref="SongLibrary"/>,
    /// which supplies the tree-walking refresh; publication locks and the
    /// published hierarchy stay with the library.
    /// </summary>
    internal sealed class SongScoreService
    {
        public SongScoreService(
            SongDatabase database,
            SongScoreStore scoreStore,
            Func<int, EInstrumentPart, int, Task<ScoreRefreshOutcome>> refreshInMemoryAsync,
            ILogger? logger = null)
        {
            _database = database ?? throw new ArgumentNullException(nameof(database));
            _scoreStore = scoreStore ?? throw new ArgumentNullException(nameof(scoreStore));
            _refreshInMemoryAsync = refreshInMemoryAsync
                ?? throw new ArgumentNullException(nameof(refreshInMemoryAsync));
            _logger = logger ?? NullLogger.Instance;
        }

        private readonly SongDatabase _database;
        private readonly SongScoreStore _scoreStore;
        private readonly Func<int, EInstrumentPart, int, Task<ScoreRefreshOutcome>> _refreshInMemoryAsync;
        private readonly ILogger _logger;

        /// <summary>
        /// Persists a complete PerformanceSummary (score + skill + judgement counts) for a
        /// specific chart and instrument. Forwards to SongScoreStore, then refreshes
        /// the in-memory score cache for that chart so the play-history badge and score
        /// fields on the song-list node reflect the just-saved play without a full
        /// song-database reload.
        /// </summary>
        /// <remarks>
        /// Per the accepted implementation plan (invariant 8): "The Result stage cannot
        /// report a save as successful until the database write and matching in-memory
        /// refresh have completed." A save that commits to the database but cannot
        /// refresh at least one matching in-memory node is therefore reported as
        /// <see cref="ScoreSaveStatus.Failed"/> so the UI does not display SAVED over a
        /// stale song-selection cache. The player can retry; the database write is
        /// idempotent (RunId receipt), so a retry that succeeds at refresh will surface
        /// <see cref="ScoreSaveStatus.AlreadySaved"/> as a successful save.
        /// </remarks>
        public async Task<ScoreSaveResult> UpdateScoreAsync(
            int chartId,
            EInstrumentPart instrument,
            PerformanceSummary summary)
        {
            var result = await _scoreStore
                .UpdateScoreAsync(chartId, instrument, summary)
                .ConfigureAwait(false);

            if (!result.IsSuccess)
                return result;

            // The save committed. The plan's contract requires the matching in-memory
            // refresh to complete before the UI may report success. A refresh failure
            // (transient DB read error) or a no-op refresh (no matching node found,
            // e.g. the song list was rebuilt/cleared between save and refresh) must
            // not be masked by returning the original successful result. A refresh
            // that finds a newer snapshot already published for the same score key
            // (chart+instrument+speed) by a concurrent save is treated as success:
            // the in-memory state is already consistent with (or ahead of) this
            // caller's snapshot, so the publication is intentionally skipped.
            ScoreRefreshOutcome outcome;
            try
            {
                outcome = await _refreshInMemoryAsync(
                    chartId,
                    instrument,
                    summary.PlaySpeedPercent).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "In-memory score refresh failed after successful save for chart {ChartId}", chartId);
                outcome = ScoreRefreshOutcome.NoMatch;
            }

            if (outcome == ScoreRefreshOutcome.NoMatch)
            {
                return ScoreSaveResult.Failed(
                    "The score was saved to the database, but the song list could not be "
                    + "refreshed. Please retry to update the displayed score.");
            }

            return result;
        }

        /// <summary>
        /// Imports DTXManiaNX drum scores from sibling &lt;chart&gt;.score.ini files for every
        /// drum chart already in the database. Best-of merge with snapshot-delta counters;
        /// safe to run repeatedly. Reports progress per chart. See the design spec.
        /// </summary>
        public async Task<NxImportResult> ImportNxScoresAsync(
            IProgress<NxImportProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            var result = new NxImportResult();
            var db = _database;
            try
            {
                using var probe = db.CreateContext();
            }
            catch (InvalidOperationException exception)
            {
                // The game-owned database exists from launch but Startup has not
                // initialized it yet (or initialization failed): report the explicit
                // unavailable result the Config stage maps to its status text.
                _logger.LogWarning(
                    exception,
                    "NX import unavailable: song database is not initialized");
                result.DbUnavailable = true;
                return result;
            }

            var importer = new NxScoreImporter();
            using var context = db.CreateContext();

            var charts = await context.SongCharts
                .Include(c => c.Song)
                .Where(c => c.HasDrumChart)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (var chart in charts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                result.Scanned++;
                try
                {
                    if (string.IsNullOrEmpty(chart.FilePath) || !File.Exists(chart.FilePath))
                    {
                        result.Skipped++;
                    }
                    else
                    {
                        var iniPath = chart.FilePath + ".score.ini";
                        var data = NxScoreIniParser.Parse(iniPath);
                        if (data == null)
                        {
                            result.Skipped++;
                        }
                        else
                        {
                            await importer.MergeAsync(context, chart, data, cancellationToken).ConfigureAwait(false);
                            result.Imported++;
                            // Clear tracked entities after each successful merge to prevent
                            // memory accumulation when importing large libraries.
                            context.ChangeTracker.Clear();
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    result.Errors++;
                    // Clear tracked entities so a failed merge does not poison the next chart.
                    context.ChangeTracker.Clear();
                    _logger.LogWarning(ex, "NX import error for {ChartPath}", chart.FilePath);
                }

                progress?.Report(new NxImportProgress
                {
                    Scanned = result.Scanned,
                    Imported = result.Imported,
                    Skipped = result.Skipped,
                    Errors = result.Errors,
                    CurrentFile = Path.GetFileName(chart.FilePath)
                });
            }

            return result;
        }
    }
}
