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
public sealed class ConfigStageSongFolderFormatTests
{
    [Fact]
    public void FormatSongLibraryReloadProgress_WithOperation_ShouldIncludeOperationAndCounts()
    {
        var progress = new SongLibraryReloadProgress(
            CurrentOperation: "Scanning",
            ProcessedCount: 5,
            DiscoveredSongs: 10,
            CurrentFile: null,
            CurrentDirectory: null);

        var result = InvokeStatic<string>("FormatSongLibraryReloadProgress", progress);

        Assert.Equal("Reloading songs: Scanning (5 processed / 10 discovered)", result);
    }

    [Fact]
    public void FormatSongLibraryReloadProgress_WithBlankOperation_ShouldShowCountsOnly()
    {
        var progress = new SongLibraryReloadProgress(
            CurrentOperation: "   ",
            ProcessedCount: 5,
            DiscoveredSongs: 10,
            CurrentFile: null,
            CurrentDirectory: null);

        var result = InvokeStatic<string>("FormatSongLibraryReloadProgress", progress);

        Assert.Equal("Reloading songs: 5 processed / 10 discovered", result);
    }

    [Fact]
    public void FormatSongLibraryReloadProgress_WithNullOperation_ShouldShowCountsOnly()
    {
        var progress = new SongLibraryReloadProgress(
            CurrentOperation: null,
            ProcessedCount: 0,
            DiscoveredSongs: 0,
            CurrentFile: null,
            CurrentDirectory: null);

        var result = InvokeStatic<string>("FormatSongLibraryReloadProgress", progress);

        Assert.Equal("Reloading songs: 0 processed / 0 discovered", result);
    }

    [Fact]
    public void FormatSongLibraryReloadResult_Published_ShouldShowChartCount()
    {
        var result = new SongLibraryReloadResult(
            SongLibraryReloadOutcome.Published,
            UnavailableRootCount: 0,
            EnumeratedFileCount: 100,
            DiscoveredScoreCount: 50);

        var status = InvokeStatic<string>("FormatSongLibraryReloadResult", result);

        Assert.Equal("Reloaded 50 song charts.", status);
    }

    [Fact]
    public void FormatSongLibraryReloadResult_PublishedWithSingleUnavailableRoot_ShouldWarnSingular()
    {
        var result = new SongLibraryReloadResult(
            SongLibraryReloadOutcome.Published,
            UnavailableRootCount: 1,
            EnumeratedFileCount: 100,
            DiscoveredScoreCount: 50);

        var status = InvokeStatic<string>("FormatSongLibraryReloadResult", result);

        Assert.Equal("Reloaded 50 song charts. (1 configured root unavailable)", status);
    }

    [Fact]
    public void FormatSongLibraryReloadResult_PublishedWithMultipleUnavailableRoots_ShouldWarnPlural()
    {
        var result = new SongLibraryReloadResult(
            SongLibraryReloadOutcome.Published,
            UnavailableRootCount: 2,
            EnumeratedFileCount: 100,
            DiscoveredScoreCount: 50);

        var status = InvokeStatic<string>("FormatSongLibraryReloadResult", result);

        Assert.Equal("Reloaded 50 song charts. (2 configured roots unavailable)", status);
    }

    [Fact]
    public void FormatSongLibraryReloadResult_NoActiveRoots_ShouldExplainKeepingCurrentList()
    {
        var result = new SongLibraryReloadResult(
            SongLibraryReloadOutcome.NoActiveRoots,
            UnavailableRootCount: 0,
            EnumeratedFileCount: 0,
            DiscoveredScoreCount: 0);

        var status = InvokeStatic<string>("FormatSongLibraryReloadResult", result);

        Assert.Equal("No configured song folders are available; keeping the current song list.", status);
    }

    [Fact]
    public void FormatSongLibraryReloadResult_Busy_ShouldExplainKeepingCurrentList()
    {
        var result = new SongLibraryReloadResult(
            SongLibraryReloadOutcome.Busy,
            UnavailableRootCount: 0,
            EnumeratedFileCount: 0,
            DiscoveredScoreCount: 0);

        var status = InvokeStatic<string>("FormatSongLibraryReloadResult", result);

        Assert.Equal("Song library reload is busy; keeping the current song list.", status);
    }

    [Fact]
    public void FormatSongLibraryReloadResult_Cancelled_ShouldExplainKeepingCurrentList()
    {
        var result = new SongLibraryReloadResult(
            SongLibraryReloadOutcome.Cancelled,
            UnavailableRootCount: 0,
            EnumeratedFileCount: 0,
            DiscoveredScoreCount: 0);

        var status = InvokeStatic<string>("FormatSongLibraryReloadResult", result);

        Assert.Equal("Song folder reload cancelled; keeping the current song list.", status);
    }

    [Fact]
    public void FormatSongLibraryReloadResult_PartialSuccessRestartRequired_ShouldExplainRestart()
    {
        var result = new SongLibraryReloadResult(
            SongLibraryReloadOutcome.PartialSuccessRestartRequired,
            UnavailableRootCount: 0,
            EnumeratedFileCount: 0,
            DiscoveredScoreCount: 0);

        var status = InvokeStatic<string>("FormatSongLibraryReloadResult", result);

        Assert.Equal("Song folders were saved, but publication needs a restart.", status);
    }

    [Fact]
    public void FormatSongLibraryReloadResult_FailedWithMessage_ShouldIncludeMessage()
    {
        var result = new SongLibraryReloadResult(
            SongLibraryReloadOutcome.Failed,
            UnavailableRootCount: 0,
            EnumeratedFileCount: 0,
            DiscoveredScoreCount: 0,
            "Permission denied");

        var status = InvokeStatic<string>("FormatSongLibraryReloadResult", result);

        Assert.Equal("Song folder reload failed; keeping the current song list: Permission denied", status);
    }

    [Fact]
    public void FormatSongLibraryReloadResult_FailedWithoutMessage_ShouldEndWithPeriod()
    {
        var result = new SongLibraryReloadResult(
            SongLibraryReloadOutcome.Failed,
            UnavailableRootCount: 0,
            EnumeratedFileCount: 0,
            DiscoveredScoreCount: 0,
            FailureMessage: null);

        var status = InvokeStatic<string>("FormatSongLibraryReloadResult", result);

        Assert.Equal("Song folder reload failed; keeping the current song list.", status);
    }

    [Fact]
    public void FormatSongLibraryReloadResult_UnknownOutcome_ShouldUseFallbackMessage()
    {
        var result = new SongLibraryReloadResult(
            (SongLibraryReloadOutcome)999,
            UnavailableRootCount: 0,
            EnumeratedFileCount: 0,
            DiscoveredScoreCount: 0);

        var status = InvokeStatic<string>("FormatSongLibraryReloadResult", result);

        Assert.Equal("Song folder reload failed; keeping the current song list.", status);
    }

    [Fact]
    public void MapSongFolderApplyStatus_ShouldMapAllKnownStatuses()
    {
        Assert.Equal(
            SongFolderApplyStatus.Updated,
            InvokeStatic<SongFolderApplyStatus>("MapSongFolderApplyStatus", SongRootUpdateStatus.Updated));
        Assert.Equal(
            SongFolderApplyStatus.Unchanged,
            InvokeStatic<SongFolderApplyStatus>("MapSongFolderApplyStatus", SongRootUpdateStatus.Unchanged));
        Assert.Equal(
            SongFolderApplyStatus.ValidationFailed,
            InvokeStatic<SongFolderApplyStatus>("MapSongFolderApplyStatus", SongRootUpdateStatus.ValidationFailed));
        Assert.Equal(
            SongFolderApplyStatus.PersistenceFailed,
            InvokeStatic<SongFolderApplyStatus>("MapSongFolderApplyStatus", SongRootUpdateStatus.PersistenceFailed));
    }

    [Fact]
    public void MapSongFolderApplyStatus_WhenUnknown_ShouldFallBackToPersistenceFailed()
    {
        Assert.Equal(
            SongFolderApplyStatus.PersistenceFailed,
            InvokeStatic<SongFolderApplyStatus>("MapSongFolderApplyStatus", (SongRootUpdateStatus)999));
    }

    [Fact]
    public void ApplySongRoots_WhenConfigManagerThrows_ShouldReturnPersistenceFailedWithMessage()
    {
        var configData = new ConfigData();
        configData.SongRoots.Clear();
        configData.SongRoots.Add("/old");
        var config = new Mock<IConfigManager>();
        config.SetupGet(manager => manager.Config).Returns(configData);
        config.Setup(manager => manager.SetSongRoots(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<string>>()))
            .Throws(new InvalidOperationException("Config write failed"));
        var (stage, inputManager) = CreateStage(
            config.Object,
            new DelegateSongLibraryReloadService(),
            (_, _) => Task.FromResult(new NxImportResult()),
            () => Task.CompletedTask);
        using (inputManager)
        {
            var result = ApplySongRoots(stage, new[] { "/new" });

            Assert.Equal(SongFolderApplyStatus.PersistenceFailed, result.Status);
            Assert.Contains(result.Diagnostics, d => d.Message.Contains("Unable to save song folders"));
            Assert.Contains(result.Diagnostics, d => d.Message.Contains("Config write failed"));
            Assert.False(GetCoordinator(stage).IsBusy);
        }
    }

    [Fact]
    public void ApplySongRoots_WhenSetSongRootsReturnsUnchanged_ShouldMapToUnchanged()
    {
        var configData = new ConfigData();
        configData.SongRoots.Clear();
        configData.SongRoots.Add("/same");
        var config = new Mock<IConfigManager>();
        config.SetupGet(manager => manager.Config).Returns(configData);
        config.Setup(manager => manager.SetSongRoots(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<string>>()))
            .Returns(new SongRootUpdateResult(
                SongRootUpdateStatus.Unchanged,
                new[] { "/same" },
                Array.Empty<SongRootDiagnostic>()));
        var (stage, inputManager) = CreateStage(
            config.Object,
            new DelegateSongLibraryReloadService(),
            (_, _) => Task.FromResult(new NxImportResult()),
            () => Task.CompletedTask);
        using (inputManager)
        {
            var result = ApplySongRoots(stage, new[] { "/other" });

            Assert.Equal(SongFolderApplyStatus.Unchanged, result.Status);
            Assert.False(GetCoordinator(stage).IsBusy);
        }
    }

    [Fact]
    public void ApplySongRoots_WhenSetSongRootsReturnsValidationFailed_ShouldMapToValidationFailed()
    {
        var configData = new ConfigData();
        configData.SongRoots.Clear();
        configData.SongRoots.Add("/old");
        var config = new Mock<IConfigManager>();
        config.SetupGet(manager => manager.Config).Returns(configData);
        config.Setup(manager => manager.SetSongRoots(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<string>>()))
            .Returns(new SongRootUpdateResult(
                SongRootUpdateStatus.ValidationFailed,
                new[] { "/new" },
                new[] { new SongRootDiagnostic("/new", "Invalid path", IsWarning: false) }));
        var (stage, inputManager) = CreateStage(
            config.Object,
            new DelegateSongLibraryReloadService(),
            (_, _) => Task.FromResult(new NxImportResult()),
            () => Task.CompletedTask);
        using (inputManager)
        {
            var result = ApplySongRoots(stage, new[] { "/new" });

            Assert.Equal(SongFolderApplyStatus.ValidationFailed, result.Status);
            Assert.False(GetCoordinator(stage).IsBusy);
        }
    }

    private static (ConfigStage Stage, InputManagerCompat InputManager) CreateStage(
        IConfigManager configManager,
        ISongLibraryReloadService reloadService,
        Func<IProgress<NxImportProgress>?, CancellationToken, Task<NxImportResult>> nxImportAsync,
        Func<Task> refreshSongListAsync)
    {
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
                nxImportAsync,
                refreshSongListAsync,
                work => Task.Run(work),
                continuationRegistrar: null),
            inputManager);
    }

    private static SongFolderApplyResult ApplySongRoots(
        ConfigStage stage,
        IReadOnlyList<string> roots)
    {
        var method = typeof(ConfigStage).GetMethod(
            "ApplySongRoots",
            BindingFlags.Instance | BindingFlags.NonPublic);
        return Assert.IsType<SongFolderApplyResult>(method!.Invoke(stage, new object[] { roots }));
    }

    private static ConfigSongOperationCoordinator GetCoordinator(ConfigStage stage) =>
        ReflectionHelpers.GetPrivateField<ConfigSongOperationCoordinator>(
            stage,
            "_songOperationCoordinator");

    private static T InvokeStatic<T>(string methodName, params object?[] args)
    {
        var method = typeof(ConfigStage).GetMethod(
            methodName,
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return (T)method!.Invoke(null, args)!;
    }

    private sealed class DelegateSongLibraryReloadService : ISongLibraryReloadService
    {
        public Task<SongLibraryReloadResult> ReloadAsync(
            IReadOnlyList<string> configuredRoots,
            IProgress<SongLibraryReloadProgress>? progress,
            CancellationToken cancellationToken) =>
            Task.FromResult(new SongLibraryReloadResult(
                SongLibraryReloadOutcome.Published,
                UnavailableRootCount: 0,
                EnumeratedFileCount: 0,
                DiscoveredScoreCount: 0));
    }
}
