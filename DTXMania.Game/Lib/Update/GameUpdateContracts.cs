#nullable enable

using System.Threading.Tasks;

namespace DTXMania.Game.Lib.Update;

/// <summary>
/// Lifecycle of the process-owned update check. Task 3 (download/launch) will
/// extend this enum additively with downloading/launched states; existing
/// members and their order are stable.
/// </summary>
public enum GameUpdateState
{
    /// <summary>No check has run yet in this process.</summary>
    NotChecked,

    /// <summary>A discovery request is in flight.</summary>
    Checking,

    /// <summary>Discovery finished without a newer installable release (or the offer was dismissed).</summary>
    UpToDate,

    /// <summary>A newer stable release with a verified installer asset is available.</summary>
    Available,

    /// <summary>Discovery could not offer an update; see <see cref="GameUpdateSnapshot.ReasonCode"/>.</summary>
    DiscoveryFailed
}

/// <summary>
/// Immutable snapshot of the update service state. Never mutated; the service
/// publishes a new instance after every state change.
/// </summary>
public sealed record GameUpdateSnapshot(
    GameUpdateState State,
    string? AvailableVersion,
    string? InstallerUrl,
    string? Sha256Digest,
    string? ReasonCode)
{
    /// <summary>Snapshot published by the service before the first <c>CheckOnce</c>.</summary>
    public static GameUpdateSnapshot NotChecked { get; } =
        new(GameUpdateState.NotChecked, null, null, null, null);
}

/// <summary>
/// Stage-facing facade for the Windows auto-update. One process-owned instance;
/// network work runs off the MonoGame loop behind <see cref="CheckOnce"/>.
/// </summary>
public interface IGameUpdateService
{
    /// <summary>Current immutable snapshot; never null.</summary>
    GameUpdateSnapshot GetSnapshot();

    /// <summary>
    /// Runs the one-shot discovery at most once per process. Subsequent calls
    /// (including after title re-entry) return the in-flight/completed task
    /// without another network request.
    /// </summary>
    Task CheckOnce();

    /// <summary>
    /// Begins downloading the available update. Not implemented yet (Task 3);
    /// currently a logged no-op that leaves the snapshot unchanged.
    /// </summary>
    void BeginUpdate();

    /// <summary>
    /// Declines the currently offered update for the rest of the process
    /// (memory only — nothing persisted).
    /// </summary>
    void DismissForProcess();
}
