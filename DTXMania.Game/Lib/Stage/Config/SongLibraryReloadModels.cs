#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DTXMania.Game.Lib.Stage.Config;

/// <summary>Immutable progress produced while rebuilding configured song roots.</summary>
internal sealed record SongLibraryReloadProgress(
    string CurrentOperation,
    int ProcessedCount,
    int DiscoveredSongs,
    string CurrentFile,
    string CurrentDirectory);

internal enum SongLibraryReloadOutcome
{
    Busy,
    Published,
    NoActiveRoots,
    Cancelled,
    Failed,
    PartialSuccessRestartRequired,
}

/// <summary>
/// Immutable Config-facing summary of an HPA-192 enumeration/import attempt.
/// An unsuccessful pre-commit result retains the previously published song
/// hierarchy. A post-commit failure must instead require recovery/restart,
/// because the database and live snapshot may no longer agree.
/// </summary>
internal sealed record SongLibraryReloadResult(
    SongLibraryReloadOutcome Outcome,
    int UnavailableRootCount,
    int EnumeratedFileCount,
    int DiscoveredScoreCount,
    string? FailureMessage = null)
{
    internal bool RetainsCurrentSnapshot => Outcome is
        SongLibraryReloadOutcome.Busy or
        SongLibraryReloadOutcome.NoActiveRoots or
        SongLibraryReloadOutcome.Cancelled or
        SongLibraryReloadOutcome.Failed;

    internal bool RequiresRestart =>
        Outcome == SongLibraryReloadOutcome.PartialSuccessRestartRequired;
}

/// <summary>Immutable worker result consumed by ConfigStage on its update thread.</summary>
internal sealed record ConfigSongOperationCompletion(string Status);

/// <summary>
/// Immutable status handoff from a background song operation to ConfigStage's
/// update thread. Holding the lease lets the update thread avoid clearing a
/// newer operation that started after an older terminal callback queued.
/// </summary>
internal sealed record ConfigSongOperationUpdate(
    int ActivationGeneration,
    ConfigSongOperationLease Lease,
    ConfigSongOperationKind Kind,
    string Status,
    bool IsTerminal);

internal interface ISongLibraryReloadService
{
    Task<SongLibraryReloadResult> ReloadAsync(
        IReadOnlyList<string> configuredRoots,
        IProgress<SongLibraryReloadProgress>? progress,
        System.Threading.CancellationToken cancellationToken);
}
