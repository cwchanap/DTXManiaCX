using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        return (new ConfigStage(game), configManager, inputManager);
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
        public void OnDeactivate_WhileImportRunning_ShouldCancelImport()
        {
            var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: false);

            // Start the import — this creates and stores a CancellationTokenSource.
            ReflectionHelpers.InvokePrivateMethod(stage, "StartNxScoreImport");

            // The CTS should have been created.
            var cts = ReflectionHelpers.GetPrivateField<System.Threading.CancellationTokenSource>(stage, "_importCts");
            Assert.NotNull(cts);

            // Deactivate should cancel the token.
            ReflectionHelpers.InvokePrivateMethod(stage, "OnDeactivate");

            Assert.True(cts.IsCancellationRequested);
        }
    }

    [Fact]
    public void StartNxScoreImport_ShouldCreateCancellationTokenSource()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: false);

            // Before starting, no CTS.
            Assert.Null(ReflectionHelpers.GetPrivateField<System.Threading.CancellationTokenSource>(stage, "_importCts"));

            ReflectionHelpers.InvokePrivateMethod(stage, "StartNxScoreImport");

            // After starting, CTS should exist and not be cancelled.
            var cts = ReflectionHelpers.GetPrivateField<System.Threading.CancellationTokenSource>(stage, "_importCts");
            Assert.NotNull(cts);
            Assert.False(cts.IsCancellationRequested);
        }
    }

    [Fact]
    public void StartNxScoreImport_WhenAlreadyRunning_ShouldReturnEarly()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: false);
            ReflectionHelpers.SetPrivateField(stage, "_importRunning", true);
            ReflectionHelpers.SetPrivateField(stage, "_importStatus", "Existing status");

            ReflectionHelpers.InvokePrivateMethod(stage, "StartNxScoreImport");

            Assert.True(ReflectionHelpers.GetPrivateField<bool>(stage, "_importRunning"));
            Assert.Equal("Existing status", ReflectionHelpers.GetPrivateField<string>(stage, "_importStatus"));
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

            Assert.True(ReflectionHelpers.GetPrivateField<bool>(stage, "_importRunning"));
            Assert.Equal("Importing NX scores...", ReflectionHelpers.GetPrivateField<string>(stage, "_importStatus"));

            // Poll until the fire-and-forget task completes.
            var completed = await WaitUntilImportCompletesAsync(stage, timeoutMs: 5000);
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

            var completed = await WaitUntilImportCompletesAsync(stage, timeoutMs: 5000);
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

            var completed = await WaitUntilImportCompletesAsync(stage, timeoutMs: 5000);
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
            var completed = await WaitUntilImportCompletesAsync(stage, timeoutMs: 5000);
            Assert.True(completed, "Import did not complete within timeout");

            // After import + refresh, RootSongs should contain the chart.
            Assert.NotEmpty(manager.RootSongs);
            var node = Assert.Single(manager.RootSongs);
            Assert.Equal("Refresh Test", node.Title);
            Assert.Equal(NodeType.Score, node.Type);
            Assert.Equal(5, node.Scores[0].PlayCount);
        }
    }

    private static async Task<bool> WaitUntilImportCompletesAsync(ConfigStage stage, int timeoutMs)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (!ReflectionHelpers.GetPrivateField<bool>(stage, "_importRunning"))
                return true;
            await Task.Delay(50);
        }
        return false;
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
