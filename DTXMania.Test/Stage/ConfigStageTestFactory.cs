#nullable enable

using System;
using System.Collections.Generic;
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
using Moq;
using Xunit;

namespace DTXMania.Test.Stage;

/// <summary>
/// Shared factory and reflection helpers for ConfigStage test suites.
/// Centralizes the duplicated CreateStage/ApplySongRoots/GetCoordinator/InvokeStatic
/// scaffolding so every ConfigStage test file uses the same construction path.
/// </summary>
internal static class ConfigStageTestFactory
{
    internal static (ConfigStage Stage, InputManagerCompat InputManager) CreateStage(
        IConfigManager? configManager = null,
        ISongLibraryReloadService? reloadService = null,
        Func<IProgress<NxImportProgress>?, CancellationToken, Task<NxImportResult>>? nxImportAsync = null,
        Func<Task>? refreshSongListAsync = null,
        Func<Func<Task<ConfigSongOperationCompletion>>, Task<ConfigSongOperationCompletion>>? backgroundRunner = null)
    {
        configManager ??= new ConfigManager();
        reloadService ??= new DelegateReloadService(SongLibraryReloadOutcome.Published, 0, 0);
        var effectiveNxImportAsync = nxImportAsync
            ?? ((_, _) => Task.FromResult(new NxImportResult()));
        var effectiveRefreshSongListAsync = refreshSongListAsync
            ?? (() => Task.CompletedTask);
        var effectiveBackgroundRunner = backgroundRunner
            ?? (work => Task.Run(work));
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
                effectiveNxImportAsync,
                effectiveRefreshSongListAsync,
                effectiveBackgroundRunner,
                continuationRegistrar: null),
            inputManager);
    }

    internal static SongFolderApplyResult ApplySongRoots(
        ConfigStage stage, IReadOnlyList<string> roots)
    {
        var method = typeof(ConfigStage).GetMethod(
            "ApplySongRoots", BindingFlags.Instance | BindingFlags.NonPublic);
        return Assert.IsType<SongFolderApplyResult>(method!.Invoke(stage, new object[] { roots }));
    }

    internal static ConfigSongOperationCoordinator GetCoordinator(ConfigStage stage) =>
        ReflectionHelpers.GetPrivateField<ConfigSongOperationCoordinator>(
            stage, "_songOperationCoordinator");

    internal static T InvokeStatic<T>(string methodName, params object?[] args)
    {
        var method = typeof(ConfigStage).GetMethod(
            methodName, BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return (T)method!.Invoke(null, args)!;
    }

    /// <summary>
    /// A reload service stub that returns a fixed result without performing any work.
    /// </summary>
    internal sealed class DelegateReloadService : ISongLibraryReloadService
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
}
