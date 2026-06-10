using System;
using System.IO;
using System.Threading.Tasks;
using DTXMania.Game;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.Stage;
using DTXMania.Test.TestData;
using Microsoft.Xna.Framework.Graphics;
using Moq;
using Xunit;

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
        var inputManager = new InputManagerCompat(configManager);
        var game = ReflectionHelpers.CreateGame();
        ReflectionHelpers.SetProperty(game, nameof(BaseGame.ConfigManager), configManager);
        ReflectionHelpers.SetProperty(game, nameof(BaseGame.InputManager), inputManager);
        return (new ConfigStage(game), configManager, inputManager);
    }

    private static void InitializeStageMenu(ConfigStage stage, bool includePanels = false)
    {
        ReflectionHelpers.InvokePrivateMethod(stage, "LoadConfiguration");
        ReflectionHelpers.InvokePrivateMethod(stage, "LoadWorkingInputBindings");
        ReflectionHelpers.InvokePrivateMethod(stage, "SetupConfigItems");
        if (includePanels)
        {
            ReflectionHelpers.InvokePrivateMethod(stage, "InitializePanels");
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

            Assert.Equal("NX import failed (see log)", ReflectionHelpers.GetPrivateField<string>(stage, "_importStatus"));
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

            font.Verify(f => f.DrawString(spriteBatch, "Importing... 1 / 5", It.IsAny<Microsoft.Xna.Framework.Vector2>(), Microsoft.Xna.Framework.Color.Cyan), Times.Once);
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
