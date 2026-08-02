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
/// An unsuccessful pre-commit result always retains the previously published
/// song hierarchy; only a completed HPA-192 publication reports Published.
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
        SongLibraryReloadOutcome.Failed or
        SongLibraryReloadOutcome.PartialSuccessRestartRequired;

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

/// <summary>
/// Marks a failure that happened after HPA-192 committed the database but
/// before its live publication could be completed. Config must not describe
/// this as a rollback: the next restart can rebuild from the committed rows.
/// </summary>
internal sealed class SongLibraryReloadPostCommitPublicationException : Exception
{
    internal SongLibraryReloadPostCommitPublicationException(string message)
        : base(message)
    {
    }

    internal SongLibraryReloadPostCommitPublicationException(
        string message,
        Exception innerException)
        : base(message, innerException)
    {
    }
}

internal interface ISongLibraryReloadService
{
    Task<SongLibraryReloadResult> ReloadAsync(
        IReadOnlyList<string> configuredRoots,
        IProgress<SongLibraryReloadProgress>? progress,
        System.Threading.CancellationToken cancellationToken);
}
