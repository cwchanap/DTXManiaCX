#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;

namespace DTXMania.Game.Lib.Stage.Config;

internal enum ConfigSongOperationKind
{
    NxScoreImport,
    SongFolderReload
}

/// <summary>
/// The exclusive ownership token for one Config-stage song operation.
/// Only the coordinator that granted the lease can release it, and repeated
/// disposal is intentionally harmless so every terminal path can use finally.
/// </summary>
internal sealed class ConfigSongOperationLease : IDisposable
{
    private Action<ConfigSongOperationLease>? _release;
    private int _disposed;

    internal ConfigSongOperationLease(
        ConfigSongOperationKind kind,
        Action<ConfigSongOperationLease> release)
    {
        Kind = kind;
        _release = release ?? throw new ArgumentNullException(nameof(release));
    }

    public ConfigSongOperationKind Kind { get; }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        var release = Interlocked.Exchange(ref _release, null);
        release?.Invoke(this);
    }
}

/// <summary>
/// Serializes long-running song operations initiated by Config. The coordinator
/// deliberately owns only mutual exclusion; persistence and UI state remain
/// owned by <see cref="ConfigStage"/>.
/// </summary>
internal sealed class ConfigSongOperationCoordinator
{
    private ConfigSongOperationLease? _activeLease;

    internal bool IsBusy => Volatile.Read(ref _activeLease) != null;

    internal ConfigSongOperationLease? TryAcquire(ConfigSongOperationKind kind)
    {
        var lease = new ConfigSongOperationLease(kind, Release);
        return Interlocked.CompareExchange(ref _activeLease, lease, null) == null
            ? lease
            : null;
    }

    /// <summary>
    /// Transfers a lease to an operation's terminal continuation. If a custom
    /// registrar fails synchronously, a fallback continuation still retains the
    /// lease until the already-created operation task actually terminates.
    /// </summary>
    internal void RegisterTerminalContinuation(
        ConfigSongOperationLease lease,
        Task operation,
        Action<Task> terminalObservation,
        Func<Task, Action<Task>, Task>? continuationRegistrar = null)
    {
        ArgumentNullException.ThrowIfNull(lease);
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(terminalObservation);

        Action<Task> complete = completed =>
        {
            try
            {
                terminalObservation(completed);
            }
            finally
            {
                lease.Dispose();
            }
        };

        try
        {
            if (continuationRegistrar == null)
            {
                _ = operation.ContinueWith(
                    complete,
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
            else
            {
                _ = continuationRegistrar(operation, complete);
            }
        }
        catch
        {
            // Registration normally cannot fail, but a task may already be
            // executing when an injected/specialized registrar does. Keep its
            // lease until that task ends; releasing here would allow overlap.
            _ = operation.ContinueWith(
                complete,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            throw;
        }
    }

    private void Release(ConfigSongOperationLease lease)
    {
        _ = Interlocked.CompareExchange(ref _activeLease, null, lease);
    }
}
