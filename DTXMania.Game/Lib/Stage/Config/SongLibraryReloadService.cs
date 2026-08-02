#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Song;

namespace DTXMania.Game.Lib.Stage.Config;

/// <summary>
/// Config-scoped adapter over HPA-192's one-pass enumerate/import/publication
/// operation. It maps terminal outcomes without attempting a second hierarchy
/// refresh or publishing an empty library for unavailable configured roots.
/// </summary>
internal sealed class SongLibraryReloadService : ISongLibraryReloadService
{
    private const string EnumerationBusyMessage =
        "Song enumeration is already in progress.";

    private readonly Func<
        IReadOnlyList<string>,
        IProgress<EnumerationProgress>?,
        CancellationToken,
        Task<SongEnumerationResult>> _enumerateAndImportAsync;

    internal SongLibraryReloadService()
        : this((roots, progress, token) =>
            SongManager.Instance.EnumerateAndImportSongsAsync(
                roots.ToArray(),
                progress,
                token))
    {
    }

    internal SongLibraryReloadService(
        Func<
            IReadOnlyList<string>,
            IProgress<EnumerationProgress>?,
            CancellationToken,
            Task<SongEnumerationResult>> enumerateAndImportAsync)
    {
        _enumerateAndImportAsync = enumerateAndImportAsync
            ?? throw new ArgumentNullException(nameof(enumerateAndImportAsync));
    }

    public async Task<SongLibraryReloadResult> ReloadAsync(
        IReadOnlyList<string> configuredRoots,
        IProgress<SongLibraryReloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuredRoots);

        var enumerationProgress = progress == null
            ? null
            : new ReloadProgressAdapter(progress);

        try
        {
            // This is intentionally the only HPA-192 call. Its successful
            // return already means the database commit, hierarchy finalization,
            // and immutable snapshot publication all completed in order.
            var result = await _enumerateAndImportAsync(
                configuredRoots,
                enumerationProgress,
                cancellationToken).ConfigureAwait(false);
            var unavailableRootCount = result.Batch.Errors.Count(
                error => error.IsRootFailure);

            return result.Outcome switch
            {
                SongEnumerationOutcome.ImportedAndPublished => new SongLibraryReloadResult(
                    SongLibraryReloadOutcome.Published,
                    unavailableRootCount,
                    result.Batch.DiscoveredChartPaths.Count,
                    result.Batch.Candidates.Count),
                SongEnumerationOutcome.NoActiveRoots => new SongLibraryReloadResult(
                    SongLibraryReloadOutcome.NoActiveRoots,
                    unavailableRootCount,
                    result.Batch.DiscoveredChartPaths.Count,
                    result.Batch.Candidates.Count),
                _ => new SongLibraryReloadResult(
                    SongLibraryReloadOutcome.Failed,
                    unavailableRootCount,
                    result.Batch.DiscoveredChartPaths.Count,
                    result.Batch.Candidates.Count,
                    "The song library reload ended with an unknown outcome."),
            };
        }
        catch (InvalidOperationException exception) when (
            string.Equals(
                exception.Message,
                EnumerationBusyMessage,
                StringComparison.Ordinal))
        {
            return new SongLibraryReloadResult(
                SongLibraryReloadOutcome.Busy,
                UnavailableRootCount: 0,
                EnumeratedFileCount: 0,
                DiscoveredScoreCount: 0,
                exception.Message);
        }
        catch (OperationCanceledException)
        {
            return new SongLibraryReloadResult(
                SongLibraryReloadOutcome.Cancelled,
                UnavailableRootCount: 0,
                EnumeratedFileCount: 0,
                DiscoveredScoreCount: 0);
        }
        catch (SongLibraryReloadPostCommitPublicationException exception)
        {
            return new SongLibraryReloadResult(
                SongLibraryReloadOutcome.PartialSuccessRestartRequired,
                UnavailableRootCount: 0,
                EnumeratedFileCount: 0,
                DiscoveredScoreCount: 0,
                exception.GetBaseException().Message);
        }
        catch (Exception exception)
        {
            return new SongLibraryReloadResult(
                SongLibraryReloadOutcome.Failed,
                UnavailableRootCount: 0,
                EnumeratedFileCount: 0,
                DiscoveredScoreCount: 0,
                exception.GetBaseException().Message);
        }
    }

    private sealed class ReloadProgressAdapter : IProgress<EnumerationProgress>
    {
        private readonly IProgress<SongLibraryReloadProgress> _progress;

        public ReloadProgressAdapter(IProgress<SongLibraryReloadProgress> progress)
        {
            _progress = progress;
        }

        public void Report(EnumerationProgress value)
        {
            ArgumentNullException.ThrowIfNull(value);
            _progress.Report(new SongLibraryReloadProgress(
                value.CurrentOperation,
                value.ProcessedCount,
                value.DiscoveredSongs,
                value.CurrentFile,
                value.CurrentDirectory));
        }
    }
}
