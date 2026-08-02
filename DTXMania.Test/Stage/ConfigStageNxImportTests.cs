using System;
using System.Collections.Generic;
using System.Diagnostics;
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
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.Stage;
using DTXMania.Game.Lib.Stage.Config;
using DTXMania.Test.TestData;
using Microsoft.EntityFrameworkCore;
using Microsoft.Xna.Framework.Graphics;
using Moq;
using Xunit;
using SongEntity = DTXMania.Game.Lib.Song.Entities.Song;

namespace DTXMania.Test.Stage;

[Trait("Category", "Unit")]
[Collection("SongManager")]
public class ConfigStageNxImportTests : IDisposable
{
    private readonly string _dbPath;

    public ConfigStageNxImportTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"nxconfig_{Guid.NewGuid()}.db");
        SongManager.ResetInstanceForTesting();
    }

    public void Dispose()
    {
        SongManager.ResetInstanceForTesting();
        try { File.Delete(_dbPath); } catch { }
    }

    private static (ConfigStage Stage, ConfigManager ConfigManager, InputManagerCompat InputManager) CreateStage(ConfigManager? configManager = null)
    {
        configManager ??= new ConfigManager();
        var inputManager = new InputManagerCompat(configManager, new TestMidiDeviceBackend());
        var game = ReflectionHelpers.CreateGame();
        ReflectionHelpers.SetProperty(game, nameof(BaseGame.ConfigManager), configManager);
        ReflectionHelpers.SetProperty(game, nameof(BaseGame.InputManager), inputManager);
        var availability = new FfmpegRuntimeAvailability(
            IsAvailable: true,
            DiagnosticReason: null,
            BinaryFolder: null);
        return (new ConfigStage(game, () => availability), configManager, inputManager);
    }

    private static (ConfigStage Stage, InputManagerCompat InputManager) CreateStage(
        IConfigManager configManager,
        ISongLibraryReloadService reloadService,
        Func<IProgress<NxImportProgress>?, CancellationToken, Task<NxImportResult>> nxImportAsync,
        Func<Task> refreshSongListAsync,
        Func<Func<Task<ConfigSongOperationCompletion>>, Task<ConfigSongOperationCompletion>>? backgroundRunner = null,
        Func<Task, Action<Task>, Task>? continuationRegistrar = null,
        Func<CancellationTokenSource>? cancellationTokenSourceFactory = null)
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
                backgroundRunner ?? (work => Task.Run(work)),
                continuationRegistrar,
                cancellationTokenSourceFactory),
            inputManager);
    }

    private static void InitializeStageMenu(ConfigStage stage, bool includePanels = false)
    {
        // Config is truth; only the item list (and optionally the system panel) need setup.
        ReflectionHelpers.InvokePrivateMethod(stage, "SetupConfigItems");
        if (includePanels)
        {
            ReflectionHelpers.InvokePrivateMethod(stage, "InitializePanels");
        }
    }

    [Fact]
    public void SetupConfigItems_NxImportHelp_ShouldExplainLegacySpeedBucket()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: false);
            var categories = ReflectionHelpers.GetPrivateField<List<ConfigCategory>>(
                stage,
                "_categories");
            var importItem = categories
                .SelectMany(category => category.Items)
                .Single(item => item.Name == "Import NX Scores");

            Assert.Contains(
                "legacy 1.00x score bucket",
                importItem.Description);
        }
    }

    [Fact]
    public async Task Deactivate_ShouldCancelWithoutBlocking()
    {
        var config = new ConfigManager();
        var importStarted = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var importCompletion = new TaskCompletionSource<NxImportResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var (stage, inputManager) = CreateStage(
            config,
            new DelegateSongLibraryReloadService(),
            (_, _) =>
            {
                importStarted.TrySetResult(true);
                return importCompletion.Task;
            },
            () => Task.CompletedTask);
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: false);
            ReflectionHelpers.InvokePrivateMethod(stage, "StartNxScoreImport");
            await importStarted.Task;

            var cts = ReflectionHelpers.GetPrivateField<CancellationTokenSource>(stage, "_songOperationCts");
            Assert.NotNull(cts);

            var stopwatch = Stopwatch.StartNew();
            ReflectionHelpers.InvokePrivateMethod(stage, "OnDeactivate");
            stopwatch.Stop();

            Assert.True(cts.IsCancellationRequested);
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1));

            importCompletion.TrySetResult(new NxImportResult());
        }
    }

    [Fact]
    public void StartNxScoreImport_ShouldCreateCancellationTokenSource()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: false);

            Assert.Null(ReflectionHelpers.GetPrivateField<CancellationTokenSource>(stage, "_songOperationCts"));

            ReflectionHelpers.InvokePrivateMethod(stage, "StartNxScoreImport");

            var cts = ReflectionHelpers.GetPrivateField<CancellationTokenSource>(stage, "_songOperationCts");
            Assert.NotNull(cts);
            Assert.False(cts.IsCancellationRequested);
        }
    }

    [Fact]
    public void StartNxScoreImport_WhenCtsConstructionFails_ShouldReleaseLeaseWithoutStartingWorker()
    {
        var importCalls = 0;
        var (stage, inputManager) = CreateStage(
            new ConfigManager(),
            new DelegateSongLibraryReloadService(),
            (_, _) =>
            {
                importCalls++;
                return Task.FromResult(new NxImportResult());
            },
            () => Task.CompletedTask,
            cancellationTokenSourceFactory: () =>
                throw new InvalidOperationException("cts setup failed"));
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: false);

            ReflectionHelpers.InvokePrivateMethod(stage, "StartNxScoreImport");

            Assert.False(GetCoordinator(stage).IsBusy);
            Assert.Null(ReflectionHelpers.GetPrivateField<CancellationTokenSource>(
                stage,
                "_songOperationCts"));
            Assert.Equal("NX import could not be started.",
                ReflectionHelpers.GetPrivateField<string>(stage, "_importStatus"));
            Assert.Equal(0, importCalls);
        }
    }

    [Fact]
    public async Task NxImportAndReload_ShouldNotOverlap()
    {
        var configData = new ConfigData();
        configData.SongRoots.Clear();
        configData.SongRoots.Add("/old");
        var config = new Mock<IConfigManager>();
        config.SetupGet(manager => manager.Config).Returns(configData);
        var importStarted = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var importCompletion = new TaskCompletionSource<NxImportResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var (stage, inputManager) = CreateStage(
            config.Object,
            new DelegateSongLibraryReloadService(),
            (_, _) =>
            {
                importStarted.TrySetResult(true);
                return importCompletion.Task;
            },
            () => Task.CompletedTask);
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: false);
            ReflectionHelpers.InvokePrivateMethod(stage, "StartNxScoreImport");
            await importStarted.Task;

            var result = ApplySongRoots(stage, new[] { "/new" });

            Assert.Equal(SongFolderApplyStatus.Busy, result.Status);
            config.Verify(manager => manager.SetSongRoots(
                It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>()), Times.Never);

            importCompletion.TrySetResult(new NxImportResult());
            Assert.True(await WaitUntilOperationCompletesAsync(stage, timeoutMs: 5000));
        }
    }

    [Fact]
    public void UnchangedApply_ShouldNotAcquirePersistOrScan()
    {
        var root = Path.Combine(Path.GetTempPath(), "hpa191-unchanged");
        var configData = new ConfigData();
        configData.SongRoots.Clear();
        configData.SongRoots.Add(root);
        var config = new Mock<IConfigManager>();
        config.SetupGet(manager => manager.Config).Returns(configData);
        var reloadCalls = 0;
        var (stage, inputManager) = CreateStage(
            config.Object,
            new DelegateSongLibraryReloadService((_, _, _) =>
            {
                reloadCalls++;
                return Task.FromResult(new SongLibraryReloadResult(
                    SongLibraryReloadOutcome.Published, 0, 0, 0));
            }),
            (_, _) => Task.FromResult(new NxImportResult()),
            () => Task.CompletedTask);
        using (inputManager)
        {
            var result = ApplySongRoots(stage, new[] { root });

            Assert.Equal(SongFolderApplyStatus.Unchanged, result.Status);
            Assert.False(GetCoordinator(stage).IsBusy);
            Assert.Equal(0, reloadCalls);
            config.Verify(manager => manager.SetSongRoots(
                It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>()), Times.Never);
        }
    }

    [Fact]
    public async Task ReorderOnlyApply_ShouldImportOnce()
    {
        var first = Path.Combine(Path.GetTempPath(), "hpa191-first");
        var second = Path.Combine(Path.GetTempPath(), "hpa191-second");
        var configData = new ConfigData();
        configData.SongRoots.Clear();
        configData.SongRoots.Add(first);
        configData.SongRoots.Add(second);
        var config = new Mock<IConfigManager>();
        config.SetupGet(manager => manager.Config).Returns(configData);
        config.Setup(manager => manager.SetSongRoots(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<string>>()))
            .Returns(new SongRootUpdateResult(
                SongRootUpdateStatus.Updated,
                new[] { second, first },
                Array.Empty<SongRootDiagnostic>()));
        var reloadCalls = 0;
        var (stage, inputManager) = CreateStage(
            config.Object,
            new DelegateSongLibraryReloadService((_, _, _) =>
            {
                reloadCalls++;
                return Task.FromResult(new SongLibraryReloadResult(
                    SongLibraryReloadOutcome.Published, 0, 2, 2));
            }),
            (_, _) => Task.FromResult(new NxImportResult()),
            () => Task.CompletedTask);
        using (inputManager)
        {
            var result = ApplySongRoots(stage, new[] { second, first });

            Assert.Equal(SongFolderApplyStatus.Started, result.Status);
            Assert.True(await WaitUntilOperationCompletesAsync(stage, timeoutMs: 5000));
            Assert.Equal(1, reloadCalls);
            config.Verify(manager => manager.SetSongRoots(
                It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>()), Times.Once);
        }
    }

    [Fact]
    public async Task StartNxScoreImport_WhenNotRunning_ShouldSetStatusAndCompleteImport()
    {
        var manager = SongManager.Instance;
        await manager.InitializeDatabaseServiceAsync(_dbPath);

        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: false);

            ReflectionHelpers.InvokePrivateMethod(stage, "StartNxScoreImport");

            Assert.Equal("Importing NX scores...", ReflectionHelpers.GetPrivateField<string>(stage, "_importStatus"));

            var completed = await WaitUntilOperationCompletesAsync(stage, timeoutMs: 5000);
            Assert.True(completed, "Import did not complete within timeout");

            var status = ReflectionHelpers.GetPrivateField<string>(stage, "_importStatus");
            Assert.Contains("scanned", status);
        }
    }

    [Fact]
    public async Task StartNxScoreImport_WhenImportThrows_ShouldSetErrorStatus()
    {
        // Seed an uninitialized database service so CreateContext() throws,
        // causing ImportNxScoresAsync to fail before its internal try/catch.
        var manager = SongManager.Instance;
        ReflectionHelpers.SetPrivateField(manager, "_databaseService", new SongDatabaseService(_dbPath));
        // Do NOT initialize it — CreateContext will throw InvalidOperationException.

        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: false);

            ReflectionHelpers.InvokePrivateMethod(stage, "StartNxScoreImport");

            var completed = await WaitUntilOperationCompletesAsync(stage, timeoutMs: 5000);
            Assert.True(completed, "Import did not complete within timeout");

            Assert.StartsWith("NX import failed:", ReflectionHelpers.GetPrivateField<string>(stage, "_importStatus"));
        }
    }

    [Fact]
    public async Task StartNxScoreImport_WhenDatabaseUnavailable_ShouldSetUnavailableStatus()
    {
        // No InitializeDatabaseServiceAsync call: _databaseService stays null, so
        // ImportNxScoresAsync returns DbUnavailable=true (no throw). The status must reflect this
        // most common real-world path rather than reporting a misleading success/error.
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: false);

            ReflectionHelpers.InvokePrivateMethod(stage, "StartNxScoreImport");

            var completed = await WaitUntilOperationCompletesAsync(stage, timeoutMs: 5000);
            Assert.True(completed, "Import did not complete within timeout");

            Assert.Equal("NX import unavailable (no database)",
                ReflectionHelpers.GetPrivateField<string>(stage, "_importStatus"));
        }
    }

    [Fact]
    public void DrawImportStatus_WhenStatusPresent_ShouldDrawString()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: false);
            var font = new Mock<IFont>();
            var spriteBatch = CreateUninitializedSpriteBatch();
            ReflectionHelpers.SetPrivateField(stage, "_font", font.Object);
            ReflectionHelpers.SetPrivateField(stage, "_spriteBatch", spriteBatch);
            ReflectionHelpers.SetPrivateField(stage, "_importStatus", "Importing... 1 / 5");

            ReflectionHelpers.InvokePrivateMethod(stage, "DrawImportStatus");

            font.Verify(f => f.DrawString(spriteBatch, "Importing... 1 / 5", It.IsAny<Microsoft.Xna.Framework.Vector2>(), new Microsoft.Xna.Framework.Color(180, 220, 255)), Times.Once);
        }
    }

    [Fact]
    public void DrawImportStatus_WhenStatusEmpty_ShouldNotDraw()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: false);
            var font = new Mock<IFont>();
            var spriteBatch = CreateUninitializedSpriteBatch();
            ReflectionHelpers.SetPrivateField(stage, "_font", font.Object);
            ReflectionHelpers.SetPrivateField(stage, "_spriteBatch", spriteBatch);
            ReflectionHelpers.SetPrivateField(stage, "_importStatus", "");

            ReflectionHelpers.InvokePrivateMethod(stage, "DrawImportStatus");

            font.Verify(f => f.DrawString(It.IsAny<SpriteBatch>(), It.IsAny<string>(), It.IsAny<Microsoft.Xna.Framework.Vector2>(), It.IsAny<Microsoft.Xna.Framework.Color>()), Times.Never);
        }
    }

    [Fact]
    public void DrawImportStatus_WhenFontNull_ShouldNotThrow()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: false);
            ReflectionHelpers.SetPrivateField(stage, "_font", null);
            ReflectionHelpers.SetPrivateField(stage, "_importStatus", "some status");

            var exception = Record.Exception(() => ReflectionHelpers.InvokePrivateMethod(stage, "DrawImportStatus"));

            Assert.Null(exception);
        }
    }

    [Fact]
    public async Task StartNxScoreImport_WhenScoresImported_ShouldRefreshRootSongs()
    {
        // Seed a drum chart + score.ini so the importer has something to import,
        // then verify that RootSongs is rebuilt from the database afterward.
        var songRoot = Path.Combine(Path.GetDirectoryName(_dbPath)!, "nxrefresh");
        Directory.CreateDirectory(songRoot);

        var dtxPath = Path.Combine(songRoot, "refresh.dtx");
        var iniPath = dtxPath + ".score.ini";
        await File.WriteAllTextAsync(dtxPath, "; dummy chart\n");
        await File.WriteAllTextAsync(iniPath,
            "[File]\nPlayCountDrums=5\nClearCountDrums=5\nBestRankDrums=1\n" +
            "[HiScore.Drums]\nScore=100000\nPerfect=10\nMaxCombo=10\nTotalChips=10\nUseMIDIIN=1\n" +
            "[HiSkill.Drums]\nSkill=100.0\n" +
            "[LastPlay.Drums]\nScore=100000\nSkill=100.0\nDateTime=5/15/2026 5:54:24 PM\n");

        var manager = SongManager.Instance;
        Assert.True(await manager.InitializeDatabaseServiceAsync(_dbPath));

        using (var ctx = manager.DatabaseService!.CreateContext())
        {
            ctx.SongCharts.Add(new SongChart
            {
                Song = new SongEntity { Title = "Refresh Test" },
                FilePath = dtxPath, HasDrumChart = true, DrumLevel = 50
            });
            await ctx.SaveChangesAsync();
        }

        // Set search paths so RefreshSongListFromDatabaseAsync can find the chart.
        ReflectionHelpers.SetPrivateField(manager, "_currentSearchPaths", new[] { songRoot });

        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: false);

            Assert.Empty(manager.RootSongs);

            ReflectionHelpers.InvokePrivateMethod(stage, "StartNxScoreImport");
            var completed = await WaitUntilOperationCompletesAsync(stage, timeoutMs: 5000);
            Assert.True(completed, "Import did not complete within timeout");

            // After import + refresh, RootSongs should contain the chart.
            Assert.NotEmpty(manager.RootSongs);
            var node = Assert.Single(manager.RootSongs);
            Assert.Equal("Refresh Test", node.Title);
            Assert.Equal(NodeType.Score, node.Type);
            Assert.Equal(5, node.Scores[0].PlayCount);
        }
    }

    [Fact]
    public async Task WorkerProgress_ShouldUpdateOnlyWhenDrained()
    {
        var config = new ConfigManager();
        IProgress<NxImportProgress>? progress = null;
        var importStarted = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var importCompletion = new TaskCompletionSource<NxImportResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var (stage, inputManager) = CreateStage(
            config,
            new DelegateSongLibraryReloadService(),
            (reportedProgress, _) =>
            {
                progress = reportedProgress;
                importStarted.TrySetResult(true);
                return importCompletion.Task;
            },
            () => Task.CompletedTask);
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: false);
            ReflectionHelpers.InvokePrivateMethod(stage, "StartNxScoreImport");
            await importStarted.Task;

            progress!.Report(new NxImportProgress { Imported = 2, Scanned = 3 });

            Assert.Equal("Importing NX scores...",
                ReflectionHelpers.GetPrivateField<string>(stage, "_importStatus"));

            DrainOperationUpdates(stage);

            Assert.Equal("Importing... 2 imported / 3 scanned",
                ReflectionHelpers.GetPrivateField<string>(stage, "_importStatus"));

            importCompletion.TrySetResult(new NxImportResult());
            Assert.True(await WaitUntilOperationCompletesAsync(stage, timeoutMs: 5000));
        }
    }

    [Fact]
    public async Task EveryTerminalPath_ShouldDisposeAndReleaseOnce()
    {
        var config = new ConfigManager();
        var (constructionStage, constructionInput) = CreateStage(
            config,
            new DelegateSongLibraryReloadService(),
            (_, _) => Task.FromResult(new NxImportResult()),
            () => Task.CompletedTask,
            _ => throw new InvalidOperationException("construction failed"));
        using (constructionInput)
        {
            InitializeStageMenu(constructionStage, includePanels: false);
            ReflectionHelpers.InvokePrivateMethod(constructionStage, "StartNxScoreImport");
            Assert.False(GetCoordinator(constructionStage).IsBusy);
        }

        var terminal = new TaskCompletionSource<ConfigSongOperationCompletion>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var (registrationStage, registrationInput) = CreateStage(
            new ConfigManager(),
            new DelegateSongLibraryReloadService(),
            (_, _) => Task.FromResult(new NxImportResult()),
            () => Task.CompletedTask,
            _ => terminal.Task,
            (_, _) => throw new InvalidOperationException("registration failed"));
        using (registrationInput)
        {
            InitializeStageMenu(registrationStage, includePanels: false);
            ReflectionHelpers.InvokePrivateMethod(registrationStage, "StartNxScoreImport");
            Assert.True(GetCoordinator(registrationStage).IsBusy);

            terminal.TrySetResult(new ConfigSongOperationCompletion("done"));
            Assert.True(await WaitUntilOperationCompletesAsync(registrationStage, timeoutMs: 5000));
            Assert.False(GetCoordinator(registrationStage).IsBusy);
        }
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

    private static void DrainOperationUpdates(ConfigStage stage) =>
        ReflectionHelpers.InvokePrivateMethod(stage, "OnUpdate", 0d);

    private static async Task<bool> WaitUntilOperationCompletesAsync(ConfigStage stage, int timeoutMs)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (!GetCoordinator(stage).IsBusy)
            {
                DrainOperationUpdates(stage);
                return true;
            }
            await Task.Delay(50);
        }
        return false;
    }

    private sealed class DelegateSongLibraryReloadService : ISongLibraryReloadService
    {
        private readonly Func<
            IReadOnlyList<string>,
            IProgress<SongLibraryReloadProgress>?,
            CancellationToken,
            Task<SongLibraryReloadResult>> _reload;

        public DelegateSongLibraryReloadService(
            Func<
                IReadOnlyList<string>,
                IProgress<SongLibraryReloadProgress>?,
                CancellationToken,
                Task<SongLibraryReloadResult>>? reload = null)
        {
            _reload = reload ?? ((_, _, _) => Task.FromResult(
                new SongLibraryReloadResult(
                    SongLibraryReloadOutcome.Published,
                    UnavailableRootCount: 0,
                    EnumeratedFileCount: 0,
                    DiscoveredScoreCount: 0)));
        }

        public Task<SongLibraryReloadResult> ReloadAsync(
            IReadOnlyList<string> configuredRoots,
            IProgress<SongLibraryReloadProgress>? progress,
            CancellationToken cancellationToken) =>
            _reload(configuredRoots, progress, cancellationToken);
    }

    private static SpriteBatch CreateUninitializedSpriteBatch()
    {
#pragma warning disable SYSLIB0050
        var sb = (SpriteBatch)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(SpriteBatch));
#pragma warning restore SYSLIB0050
        GC.SuppressFinalize(sb);
        return sb;
    }
}
