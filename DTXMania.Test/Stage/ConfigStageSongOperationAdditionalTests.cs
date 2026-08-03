#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using DTXMania.Game;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Stage;
using DTXMania.Game.Lib.Stage.Config;
using DTXMania.Test.TestData;
using Microsoft.Xna.Framework.Input;
using Moq;
using Xunit;

namespace DTXMania.Test.Stage;

[Trait("Category", "Unit")]
public sealed class ConfigStageSongOperationAdditionalTests
{
    [Theory]
    [InlineData(1, "1 folder")]
    [InlineData(0, "0 folders")]
    [InlineData(3, "3 folders")]
    public void FormatSongFolderCount_ShouldSingularizeOneAndPluralizeOthers(
        int count, string expected)
    {
        Assert.Equal(expected, InvokeStatic("FormatSongFolderCount", count));
    }

    [Fact]
    public void ApplySongRoots_WhenCoordinatorIsBusy_ShouldReturnBusyWithoutPersisting()
    {
        var configData = new ConfigData();
        configData.SongRoots.Clear();
        configData.SongRoots.Add("/old");
        var config = new Mock<IConfigManager>();
        config.SetupGet(m => m.Config).Returns(configData);
        config.Setup(m => m.SetSongRoots(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>()))
            .Returns(new SongRootUpdateResult(
                SongRootUpdateStatus.Updated,
                new[] { "/new" },
                Array.Empty<SongRootDiagnostic>()));
        var (stage, inputManager) = CreateStage(config.Object);
        using (inputManager)
        {
            // Pre-acquire the coordinator lease so ApplySongRoots sees a busy state.
            var coordinator = GetCoordinator(stage);
            var lease = coordinator.TryAcquire(ConfigSongOperationKind.NxScoreImport);
            Assert.NotNull(lease);
            using (lease)
            {
                var result = ApplySongRoots(stage, new[] { "/new" });

                Assert.Equal(SongFolderApplyStatus.Busy, result.Status);
                Assert.Contains(result.Diagnostics, d =>
                    d.Message.Contains("Another song operation", StringComparison.Ordinal));
            }

            // SetSongRoots must not have been called while busy.
            config.Verify(m => m.SetSongRoots(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<string>>()), Times.Never);
        }
    }

    [Fact]
    public async Task ApplySongRoots_WhenSetSongRootsReturnsUpdated_ShouldStartReloadAndReturnStarted()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"dtx-config-started-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var configData = new ConfigData();
            configData.SongRoots.Clear();
            configData.SongRoots.Add("/old");
            var config = new Mock<IConfigManager>();
            config.SetupGet(m => m.Config).Returns(configData);
            config.Setup(m => m.SetSongRoots(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>()))
                .Callback<string, IReadOnlyList<string>>((_, roots) =>
                {
                    configData.SongRoots.Clear();
                    configData.SongRoots.AddRange(roots);
                })
                .Returns(new SongRootUpdateResult(
                    SongRootUpdateStatus.Updated,
                    new[] { tempRoot },
                    Array.Empty<SongRootDiagnostic>()));
            var reloadService = new DelegateReloadService(
                SongLibraryReloadOutcome.Published,
                unavailableRootCount: 0,
                discoveredScoreCount: 5);
            var (stage, inputManager) = CreateStage(
                config.Object,
                reloadService);
            using (inputManager)
            {
                var result = ApplySongRoots(stage, new[] { tempRoot });

                Assert.Equal(SongFolderApplyStatus.Started, result.Status);

                // Wait for the background operation to complete and drain its update.
                await WaitForTerminalUpdateAsync(stage);
                DrainSongOperationUpdates(stage);

                var folderStatus = GetPrivateField<string>(stage, "_songFolderStatus");
                Assert.Contains("Reloaded", folderStatus, StringComparison.Ordinal);
                Assert.False(GetCoordinator(stage).IsBusy);
            }
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void ApplySongRoots_WhenBackgroundRunnerReturnsNullTask_ShouldReturnPersistenceFailed()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"dtx-config-null-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var configData = new ConfigData();
            configData.SongRoots.Clear();
            configData.SongRoots.Add("/old");
            var config = new Mock<IConfigManager>();
            config.SetupGet(m => m.Config).Returns(configData);
            config.Setup(m => m.SetSongRoots(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>()))
                .Returns(new SongRootUpdateResult(
                    SongRootUpdateStatus.Updated,
                    new[] { tempRoot },
                    Array.Empty<SongRootDiagnostic>()));
            // A runner that returns null causes StartSongOperation to return false.
            var (stage, inputManager) = CreateStage(
                config.Object,
                new DelegateReloadService(SongLibraryReloadOutcome.Published, 0, 0),
                backgroundRunner: _ => null!);
            using (inputManager)
            {
                var result = ApplySongRoots(stage, new[] { tempRoot });

                Assert.Equal(SongFolderApplyStatus.PersistenceFailed, result.Status);
                Assert.Contains(result.Diagnostics, d =>
                    d.Message.Contains("reload could not be started", StringComparison.Ordinal));
                Assert.False(GetCoordinator(stage).IsBusy);
            }
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void CompleteSongOperation_WhenTaskIsCanceled_ShouldEnqueueCancelledStatus()
    {
        var (stage, inputManager) = CreateStage();
        using (inputManager)
        {
            var coordinator = GetCoordinator(stage);
            var lease = coordinator.TryAcquire(ConfigSongOperationKind.SongFolderReload)!;
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var canceledTask = Task.FromCanceled<ConfigSongOperationCompletion>(cts.Token);

            InvokePrivate(
                stage,
                "CompleteSongOperation",
                canceledTask,
                0,
                lease,
                cts,
                ConfigSongOperationKind.SongFolderReload);

            DrainSongOperationUpdates(stage);
            var status = GetPrivateField<string>(stage, "_songFolderStatus");
            Assert.Contains("cancelled", status, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void CompleteSongOperation_WhenTaskIsFaulted_ShouldEnqueueFailureStatus()
    {
        var (stage, inputManager) = CreateStage();
        using (inputManager)
        {
            var coordinator = GetCoordinator(stage);
            var lease = coordinator.TryAcquire(ConfigSongOperationKind.SongFolderReload)!;
            using var cts = new CancellationTokenSource();
            var faultedTask = Task.FromException<ConfigSongOperationCompletion>(
                new InvalidOperationException("reload exploded"));

            InvokePrivate(
                stage,
                "CompleteSongOperation",
                faultedTask,
                0,
                lease,
                cts,
                ConfigSongOperationKind.SongFolderReload);

            DrainSongOperationUpdates(stage);
            var status = GetPrivateField<string>(stage, "_songFolderStatus");
            Assert.Contains("reload exploded", status, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void CompleteSongOperation_WhenTaskSucceeds_ShouldEnqueueCompletionStatus()
    {
        var (stage, inputManager) = CreateStage();
        using (inputManager)
        {
            var coordinator = GetCoordinator(stage);
            var lease = coordinator.TryAcquire(ConfigSongOperationKind.SongFolderReload)!;
            using var cts = new CancellationTokenSource();
            var successTask = Task.FromResult(
                new ConfigSongOperationCompletion("Reloaded 3 song charts."));

            InvokePrivate(
                stage,
                "CompleteSongOperation",
                successTask,
                0,
                lease,
                cts,
                ConfigSongOperationKind.SongFolderReload);

            DrainSongOperationUpdates(stage);
            var status = GetPrivateField<string>(stage, "_songFolderStatus");
            Assert.Equal("Reloaded 3 song charts.", status);
        }
    }

    [Fact]
    public void CompleteSongOperation_WhenTaskIsCanceledForNxImport_ShouldEnqueueNxCancelledStatus()
    {
        var (stage, inputManager) = CreateStage();
        using (inputManager)
        {
            var coordinator = GetCoordinator(stage);
            var lease = coordinator.TryAcquire(ConfigSongOperationKind.NxScoreImport)!;
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var canceledTask = Task.FromCanceled<ConfigSongOperationCompletion>(cts.Token);

            InvokePrivate(
                stage,
                "CompleteSongOperation",
                canceledTask,
                0,
                lease,
                cts,
                ConfigSongOperationKind.NxScoreImport);

            DrainSongOperationUpdates(stage);
            var importStatus = GetPrivateField<string>(stage, "_importStatus");
            Assert.Contains("NX import cancelled", importStatus, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void DrainSongOperationUpdates_WhenUpdateGenerationDiffers_ShouldDiscardStaleUpdate()
    {
        var (stage, inputManager) = CreateStage();
        using (inputManager)
        {
            var coordinator = GetCoordinator(stage);
            var lease = coordinator.TryAcquire(ConfigSongOperationKind.SongFolderReload)!;
            using var cts = new CancellationTokenSource();

            // Enqueue an update from a prior activation generation.
            EnqueueSongOperationUpdate(
                stage,
                activationGeneration: 0,
                lease,
                ConfigSongOperationKind.SongFolderReload,
                "stale status",
                isTerminal: false);

            // Bump the activation generation so the stale update is discarded.
            SetPrivateField(stage, "_activationGeneration", 1);
            DrainSongOperationUpdates(stage);

            var status = GetPrivateField<string>(stage, "_songFolderStatus");
            Assert.DoesNotContain("stale status", status);

            lease.Dispose();
        }
    }

    [Fact]
    public void CancelSongOperationForDeactivation_WhenOperationIsActive_ShouldCancelItsToken()
    {
        var (stage, inputManager) = CreateStage();
        using (inputManager)
        {
            using var cts = new CancellationTokenSource();
            SetPrivateField(stage, "_songOperationCts", cts);
            SetPrivateField(stage, "_activationGeneration", 0);

            InvokePrivate(stage, "CancelSongOperationForDeactivation");

            Assert.True(cts.IsCancellationRequested);
            // The activation generation is bumped to invalidate stale updates.
            Assert.Equal(1, GetPrivateField<int>(stage, "_activationGeneration"));
        }
    }

    [Fact]
    public void CancelSongOperationForDeactivation_WhenCtsIsAlreadyDisposed_ShouldNotThrow()
    {
        var (stage, inputManager) = CreateStage();
        using (inputManager)
        {
            using var cts = new CancellationTokenSource();
            cts.Dispose();
            SetPrivateField(stage, "_songOperationCts", cts);

            // The ObjectDisposedException catch must suppress the error.
            var exception = Record.Exception(() =>
                InvokePrivate(stage, "CancelSongOperationForDeactivation"));
            Assert.Null(exception);
        }
    }

    #region Helpers

    private static (ConfigStage Stage, InputManagerCompat InputManager) CreateStage(
        IConfigManager? configManager = null,
        ISongLibraryReloadService? reloadService = null,
        Func<Func<Task<ConfigSongOperationCompletion>>, Task<ConfigSongOperationCompletion>>? backgroundRunner = null)
    {
        configManager ??= new ConfigManager();
        reloadService ??= new DelegateReloadService(SongLibraryReloadOutcome.Published, 0, 0);
        var inputManager = new InputManagerCompat(new ConfigManager(), new TestMidiDeviceBackend());
        var game = ReflectionHelpers.CreateGame();
        ReflectionHelpers.SetProperty(game, nameof(BaseGame.ConfigManager), configManager);
        ReflectionHelpers.SetProperty(game, nameof(BaseGame.InputManager), inputManager);
        var availability = new FfmpegRuntimeAvailability(
            IsAvailable: true,
            DiagnosticReason: null,
            BinaryFolder: null);
        return (
            new ConfigStage(
                game,
                () => availability,
                () => new Mock<IFolderPickerService>().Object,
                reloadService,
                (_, _) => Task.FromResult(new NxImportResult()),
                () => Task.CompletedTask,
                backgroundRunner ?? (work => Task.Run(work)),
                continuationRegistrar: null),
            inputManager);
    }

    private static SongFolderApplyResult ApplySongRoots(
        ConfigStage stage, IReadOnlyList<string> roots)
    {
        var method = typeof(ConfigStage).GetMethod(
            "ApplySongRoots", BindingFlags.Instance | BindingFlags.NonPublic);
        return Assert.IsType<SongFolderApplyResult>(method!.Invoke(stage, new object[] { roots }));
    }

    private static ConfigSongOperationCoordinator GetCoordinator(ConfigStage stage) =>
        ReflectionHelpers.GetPrivateField<ConfigSongOperationCoordinator>(
            stage, "_songOperationCoordinator");

    private static void DrainSongOperationUpdates(ConfigStage stage) =>
        InvokePrivate(stage, "DrainSongOperationUpdates");

    private static void EnqueueSongOperationUpdate(
        ConfigStage stage,
        int activationGeneration,
        ConfigSongOperationLease lease,
        ConfigSongOperationKind kind,
        string status,
        bool isTerminal)
    {
        var method = typeof(ConfigStage).GetMethod(
            "EnqueueSongOperationUpdate", BindingFlags.Instance | BindingFlags.NonPublic);
        method!.Invoke(stage,
            new object[] { activationGeneration, lease, kind, status, isTerminal });
    }

    private static async Task WaitForTerminalUpdateAsync(ConfigStage stage, int timeoutMs = 3000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (GetCoordinator(stage).IsBusy)
        {
            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException("The song operation did not complete in time.");
            await Task.Yield();
        }
    }

    private static string InvokeStatic(string name, params object[] args)
    {
        var method = typeof(ConfigStage).GetMethod(
            name, BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return (string)method!.Invoke(null, args)!;
    }

    private static void InvokePrivate(ConfigStage stage, string name, params object?[] args)
    {
        var method = typeof(ConfigStage).GetMethod(
            name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(stage, args);
    }

    private static T GetPrivateField<T>(object target, string name) =>
        ReflectionHelpers.GetPrivateField<T>(target, name);

    private static void SetPrivateField(object target, string name, object? value) =>
        ReflectionHelpers.SetPrivateField(target, name, value);

    private sealed class DelegateReloadService : ISongLibraryReloadService
    {
        private readonly SongLibraryReloadOutcome _outcome;
        private readonly int _unavailableRootCount;
        private readonly int _discoveredScoreCount;

        public DelegateReloadService(
            SongLibraryReloadOutcome outcome,
            int unavailableRootCount,
            int discoveredScoreCount)
        {
            _outcome = outcome;
            _unavailableRootCount = unavailableRootCount;
            _discoveredScoreCount = discoveredScoreCount;
        }

        public Task<SongLibraryReloadResult> ReloadAsync(
            IReadOnlyList<string> configuredRoots,
            IProgress<SongLibraryReloadProgress>? progress,
            CancellationToken cancellationToken) =>
            Task.FromResult(new SongLibraryReloadResult(
                _outcome,
                _unavailableRootCount,
                EnumeratedFileCount: 0,
                _discoveredScoreCount));
    }

    #endregion
}
