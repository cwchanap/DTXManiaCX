#nullable enable

using System;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Stage.Config;
using Xunit;

namespace DTXMania.Test.Config;

[Trait("Category", "Unit")]
public sealed class ConfigSongOperationCoordinatorTests
{
    [Fact]
    public void TryAcquire_WhenIdle_ShouldGrantSingleLeaseUntilDisposed()
    {
        var coordinator = new ConfigSongOperationCoordinator();

        var lease = coordinator.TryAcquire(ConfigSongOperationKind.NxScoreImport);

        Assert.NotNull(lease);
        Assert.Equal(ConfigSongOperationKind.NxScoreImport, lease!.Kind);
        Assert.True(coordinator.IsBusy);

        lease.Dispose();

        Assert.False(coordinator.IsBusy);
    }

    [Fact]
    public void TryAcquire_WhenAnyOperationOwnsLease_ShouldRejectOtherKind()
    {
        var coordinator = new ConfigSongOperationCoordinator();
        using var nxLease = coordinator.TryAcquire(ConfigSongOperationKind.NxScoreImport);

        var reloadLease = coordinator.TryAcquire(ConfigSongOperationKind.SongFolderReload);

        Assert.NotNull(nxLease);
        Assert.Null(reloadLease);
        Assert.True(coordinator.IsBusy);
    }

    [Fact]
    public void Lease_DisposeRepeatedly_ShouldReleaseExactlyOnce()
    {
        var coordinator = new ConfigSongOperationCoordinator();
        var lease = Assert.IsType<ConfigSongOperationLease>(
            coordinator.TryAcquire(ConfigSongOperationKind.SongFolderReload));

        lease.Dispose();
        lease.Dispose();

        Assert.False(coordinator.IsBusy);
        Assert.NotNull(coordinator.TryAcquire(ConfigSongOperationKind.NxScoreImport));
    }

    [Fact]
    public void RegisterTerminal_WhenContinuationRegistrationThrows_ShouldKeepLeaseUntilTaskTerminates()
    {
        var coordinator = new ConfigSongOperationCoordinator();
        var lease = Assert.IsType<ConfigSongOperationLease>(
            coordinator.TryAcquire(ConfigSongOperationKind.SongFolderReload));
        var completion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            coordinator.RegisterTerminalContinuation(
                lease,
                completion.Task,
                _ => { },
                (_, _) => throw new InvalidOperationException("registration failed")));

        Assert.Equal("registration failed", exception.Message);
        Assert.True(coordinator.IsBusy);

        completion.SetResult(true);
        SpinWaitUntil(() => !coordinator.IsBusy);

        Assert.False(coordinator.IsBusy);
    }

    [Fact]
    public void ConstructOperation_WhenConstructionThrows_ShouldReleaseTheScopedLease()
    {
        var coordinator = new ConfigSongOperationCoordinator();
        var lease = Assert.IsType<ConfigSongOperationLease>(
            coordinator.TryAcquire(ConfigSongOperationKind.NxScoreImport));

        try
        {
            Assert.Throws<InvalidOperationException>(ThrowDuringTaskConstruction);
        }
        finally
        {
            lease.Dispose();
        }

        Assert.False(coordinator.IsBusy);
    }

    private static void ThrowDuringTaskConstruction() =>
        throw new InvalidOperationException("construction failed");

    private static void SpinWaitUntil(Func<bool> condition)
    {
        Assert.True(System.Threading.SpinWait.SpinUntil(condition, TimeSpan.FromSeconds(2)),
            "The operation lease was not released after the task terminated.");
    }
}
