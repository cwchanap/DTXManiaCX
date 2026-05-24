using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using DTXMania.Game;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Stage;
using DTXMania.Game.Lib.Stage.KeyAssign;
using DTXMania.Game.Lib.Utilities;
using DTXMania.Test.TestData;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Moq;

namespace DTXMania.Test.Stage;

[Trait("Category", "Unit")]
public class ConfigStageCoverageTests
{
    private static (ConfigStage Stage, ConfigManager ConfigManager, InputManagerCompat InputManager) CreateStage(ConfigManager? configManager = null)
    {
        configManager ??= new ConfigManager();
        var inputManager = new InputManagerCompat(configManager);
        var game = ReflectionHelpers.CreateGame();
        ReflectionHelpers.SetProperty(game, nameof(BaseGame.ConfigManager), configManager);
        ReflectionHelpers.SetProperty(game, nameof(BaseGame.InputManager), inputManager);
        return (new ConfigStage(game), configManager, inputManager);
    }

    private static (RenderSpyConfigStage Stage, ConfigManager ConfigManager, InputManagerCompat InputManager) CreateRenderSpyStage(ConfigManager? configManager = null)
    {
        configManager ??= new ConfigManager();
        var inputManager = new InputManagerCompat(configManager);
        var game = ReflectionHelpers.CreateGame();
        ReflectionHelpers.SetProperty(game, nameof(BaseGame.ConfigManager), configManager);
        ReflectionHelpers.SetProperty(game, nameof(BaseGame.InputManager), inputManager);
        return (new RenderSpyConfigStage(game), configManager, inputManager);
    }

    private static void InitializeStageMenu(ConfigStage stage, bool includePanels)
    {
        ReflectionHelpers.InvokePrivateMethod(stage, "LoadConfiguration");
        ReflectionHelpers.InvokePrivateMethod(stage, "LoadWorkingInputBindings");
        ReflectionHelpers.InvokePrivateMethod(stage, "SetupConfigItems");
        if (includePanels)
        {
            ReflectionHelpers.InvokePrivateMethod(stage, "InitializePanels");
        }
    }

    private static void SetKeyboardStates(ConfigStage stage, KeyboardState current, KeyboardState previous)
    {
        ReflectionHelpers.SetPrivateField(stage, "_currentKeyboardState", current);
        ReflectionHelpers.SetPrivateField(stage, "_previousKeyboardState", previous);
    }

    private static string CreateConfigSavePath()
    {
        var directory = Path.Combine(
            AppContext.BaseDirectory,
            "TestResults",
            "config-stage-coverage",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "Config.ini");
    }

    private static void DeleteConfigSavePath(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static KeyBindings CreateWorkingDrumBindings()
    {
        var bindings = new KeyBindings();
        bindings.UnbindLane(0);
        bindings.UnbindLane(4);
        bindings.BindButton("Key.Z", 4);
        return bindings;
    }

    private sealed class RecordingConfigManager : ConfigManager, IConfigManager
    {
        private readonly Exception? _saveException;
        private readonly string? _redirectedSavePath;

        public RecordingConfigManager(ConfigData originalConfig, Exception? saveException = null, string? redirectedSavePath = null)
        {
            _saveException = saveException;
            _redirectedSavePath = redirectedSavePath;
            CopyConfigData(Config, originalConfig);
        }

        public string? LastSavePath { get; private set; }

        public new void SaveConfig(string filePath)
            => ((IConfigManager)this).SaveConfig(filePath);

        void IConfigManager.SaveConfig(string filePath)
        {
            LastSavePath = filePath;
            if (_saveException != null)
                throw _saveException;
            base.SaveConfig(_redirectedSavePath ?? filePath);
        }

        private static void CopyConfigData(ConfigData target, ConfigData source)
        {
            target.ScreenWidth = source.ScreenWidth;
            target.ScreenHeight = source.ScreenHeight;
            target.FullScreen = source.FullScreen;
            target.VSyncWait = source.VSyncWait;
            target.NoFail = source.NoFail;
            target.AutoPlay = source.AutoPlay;
            target.ScrollSpeed = source.ScrollSpeed;
            target.DTXManiaVersion = source.DTXManiaVersion;
            target.SkinPath = source.SkinPath;
            target.DTXPath = source.DTXPath;
            target.UseBoxDefSkin = source.UseBoxDefSkin;
            target.SystemSkinRoot = source.SystemSkinRoot;
            target.LastUsedSkin = source.LastUsedSkin;
            target.MasterVolume = source.MasterVolume;
            target.BGMVolume = source.BGMVolume;
            target.SEVolume = source.SEVolume;
            target.BufferSizeMs = source.BufferSizeMs;
            target.KeyBindings = new Dictionary<string, int>(source.KeyBindings);
            target.UnboundDrumLanes = new HashSet<int>(source.UnboundDrumLanes);
            target.UnboundDrumButtons = new HashSet<string>(source.UnboundDrumButtons);
            target.SystemKeyBindings = new Dictionary<string, string>(source.SystemKeyBindings);
        }
    }

    [Fact]
    public void ApplyConfiguration_WhenSaveSucceedsAndInputManagerAvailable_ShouldReloadBindingsAndApplySystemBindings()
    {
        var redirectedSavePath = CreateConfigSavePath();
        var configManager = new RecordingConfigManager(new ConfigData(), redirectedSavePath: redirectedSavePath);
        try
        {
            var (stage, _, inputManager) = CreateStage(configManager);
            using (inputManager)
            {
                InitializeStageMenu(stage, includePanels: false);
                var workingSystemBindings = new Dictionary<Keys, InputCommandType>
                {
                    [Keys.W] = InputCommandType.MoveUp,
                    [Keys.S] = InputCommandType.MoveDown,
                    [Keys.A] = InputCommandType.MoveLeft,
                    [Keys.D] = InputCommandType.MoveRight,
                    [Keys.Enter] = InputCommandType.Activate,
                    [Keys.Escape] = InputCommandType.Back
                };
                ReflectionHelpers.SetPrivateField(stage, "_workingConfig", new ConfigData
                {
                    ScreenWidth = 1920,
                    ScreenHeight = 1080,
                    FullScreen = true,
                    VSyncWait = false
                });
                ReflectionHelpers.SetPrivateField(stage, "_workingDrumBindings", CreateWorkingDrumBindings());
                ReflectionHelpers.SetPrivateField(stage, "_workingSystemBindings", workingSystemBindings);
                ReflectionHelpers.SetPrivateField(stage, "_hasUnsavedChanges", true);

                var result = (bool)ReflectionHelpers.InvokePrivateMethod(stage, "ApplyConfiguration")!;

                Assert.True(result);
                Assert.False(ReflectionHelpers.GetPrivateField<bool>(stage, "_hasUnsavedChanges"));
                Assert.Equal(4, inputManager.ModularInputManager.KeyBindings.GetLane("Key.Z"));
                var snapshot = inputManager.GetKeyMappingSnapshot();
                Assert.Equal(InputCommandType.MoveUp, snapshot[Keys.W]);
                Assert.Equal(InputCommandType.Back, snapshot[Keys.Escape]);
            }
        }
        finally
        {
            DeleteConfigSavePath(redirectedSavePath);
        }
    }

    [Fact]
    public void ApplyConfiguration_WhenSaveThrows_ShouldRollbackAndReturnFalse()
    {
        var configManager = new RecordingConfigManager(new ConfigData
        {
            ScreenWidth = 1280,
            ScreenHeight = 720,
            FullScreen = false,
            VSyncWait = true,
            NoFail = false,
            AutoPlay = false,
            KeyBindings = new Dictionary<string, int> { ["Key.A"] = 0 },
            UnboundDrumLanes = new HashSet<int> { 3 },
            UnboundDrumButtons = new HashSet<string> { "Key.B" },
            SystemKeyBindings = new Dictionary<string, string> { ["SystemKey.MoveUp"] = "Up" }
        }, saveException: new IOException("disk full"));
        var (stage, _, inputManager) = CreateStage(configManager);
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: false);
            ReflectionHelpers.SetPrivateField(stage, "_workingConfig", new ConfigData
            {
                ScreenWidth = 2560,
                ScreenHeight = 1440,
                FullScreen = true,
                VSyncWait = false,
                NoFail = true,
                AutoPlay = true
            });
            ReflectionHelpers.SetPrivateField(stage, "_workingDrumBindings", CreateWorkingDrumBindings());
            ReflectionHelpers.SetPrivateField(stage, "_workingSystemBindings", new Dictionary<Keys, InputCommandType>
            {
                [Keys.W] = InputCommandType.MoveUp
            });
            ReflectionHelpers.SetPrivateField(stage, "_hasUnsavedChanges", true);

            var result = (bool)ReflectionHelpers.InvokePrivateMethod(stage, "ApplyConfiguration")!;

            Assert.False(result);
            Assert.Equal(1280, configManager.Config.ScreenWidth);
            Assert.Equal(720, configManager.Config.ScreenHeight);
            Assert.False(configManager.Config.FullScreen);
            Assert.True(configManager.Config.VSyncWait);
            Assert.False(configManager.Config.NoFail);
            Assert.False(configManager.Config.AutoPlay);
            Assert.Equal(0, configManager.Config.KeyBindings["Key.A"]);
            Assert.Contains(3, configManager.Config.UnboundDrumLanes);
            Assert.Contains("Key.B", configManager.Config.UnboundDrumButtons);
            Assert.Equal("Up", configManager.Config.SystemKeyBindings["SystemKey.MoveUp"]);
            Assert.True(ReflectionHelpers.GetPrivateField<bool>(stage, "_hasUnsavedChanges"));
        }
    }

    [Fact]
    public void OnSaveButtonClicked_WhenApplySucceeds_ShouldChangeStage()
    {
        var redirectedSavePath = CreateConfigSavePath();
        var configManager = new RecordingConfigManager(new ConfigData(), redirectedSavePath: redirectedSavePath);
        try
        {
            var (stage, _, inputManager) = CreateStage(configManager);
            using (inputManager)
            {
                InitializeStageMenu(stage, includePanels: false);
                var stageManager = new Mock<IStageManager>();
                stage.StageManager = stageManager.Object;
                ReflectionHelpers.SetPrivateField(stage, "_hasUnsavedChanges", true);

                ReflectionHelpers.InvokePrivateMethod(stage, "OnSaveButtonClicked", null, EventArgs.Empty);

                stageManager.Verify(
                    m => m.ChangeStage(
                        StageType.Title,
                        It.Is<IStageTransition>(t => t is CrossfadeTransition)),
                    Times.Once);
            }
        }
        finally
        {
            DeleteConfigSavePath(redirectedSavePath);
        }
    }

    [Fact]
    public void OnSaveButtonClicked_WhenApplyFails_ShouldStayOnConfigStage()
    {
        var configManager = new RecordingConfigManager(
            new ConfigData(),
            saveException: new IOException("disk full"));
        var (stage, _, inputManager) = CreateStage(configManager);
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: false);
            var stageManager = new Mock<IStageManager>();
            stage.StageManager = stageManager.Object;
            ReflectionHelpers.SetPrivateField(stage, "_hasUnsavedChanges", true);

            ReflectionHelpers.InvokePrivateMethod(stage, "OnSaveButtonClicked", null, EventArgs.Empty);

            stageManager.Verify(
                m => m.ChangeStage(It.IsAny<StageType>(), It.IsAny<IStageTransition>()),
                Times.Never);
        }
    }

    [Fact]
    public void OnBackButtonClicked_ShouldChangeStageToTitle()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: false);
            var stageManager = new Mock<IStageManager>();
            stage.StageManager = stageManager.Object;

            ReflectionHelpers.InvokePrivateMethod(stage, "OnBackButtonClicked", null, EventArgs.Empty);

            stageManager.Verify(
                m => m.ChangeStage(
                    StageType.Title,
                    It.Is<IStageTransition>(t => t is CrossfadeTransition)),
                Times.Once);
        }
    }

    [Fact]
    public void ApplyConfiguration_WithConcreteConfigManager_ShouldSaveKeyBindings()
    {
        var redirectedSavePath = CreateConfigSavePath();
        var configManager = new RecordingConfigManager(new ConfigData(), redirectedSavePath: redirectedSavePath);
        try
        {
            var (stage, _, inputManager) = CreateStage(configManager);
            using (inputManager)
            {
                InitializeStageMenu(stage, includePanels: false);
                var workingDrumBindings = CreateWorkingDrumBindings();
                var workingSystemBindings = new Dictionary<Keys, InputCommandType>
                {
                    [Keys.W] = InputCommandType.MoveUp,
                    [Keys.Escape] = InputCommandType.Back
                };
                ReflectionHelpers.SetPrivateField(stage, "_workingConfig", new ConfigData
                {
                    ScreenWidth = 1920,
                    ScreenHeight = 1080
                });
                ReflectionHelpers.SetPrivateField(stage, "_workingDrumBindings", workingDrumBindings);
                ReflectionHelpers.SetPrivateField(stage, "_workingSystemBindings", workingSystemBindings);
                ReflectionHelpers.SetPrivateField(stage, "_hasUnsavedChanges", true);

                var result = (bool)ReflectionHelpers.InvokePrivateMethod(stage, "ApplyConfiguration")!;

                Assert.True(result);
                Assert.Equal(4, configManager.Config.KeyBindings["Key.Z"]);
                Assert.Contains(0, configManager.Config.UnboundDrumLanes);
                Assert.Equal("W", configManager.Config.SystemKeyBindings["SystemKey.MoveUp"]);
                Assert.Equal("Escape", configManager.Config.SystemKeyBindings["SystemKey.Back"]);
            }
        }
        finally
        {
            DeleteConfigSavePath(redirectedSavePath);
        }
    }

    [Fact]
    public void HandleInput_WhenBackCommandPressed_ShouldChangeStage()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: false);
            var stageManager = new Mock<IStageManager>();
            stage.StageManager = stageManager.Object;
            ReflectionHelpers.SetPrivateField(stage, "_hasUnsavedChanges", false);
            SetKeyboardStates(stage, new KeyboardState(Keys.Escape), new KeyboardState());

            ReflectionHelpers.InvokePrivateMethod(stage, "HandleInput");

            stageManager.Verify(
                m => m.ChangeStage(
                    StageType.Title,
                    It.Is<IStageTransition>(t => t is CrossfadeTransition)),
                Times.Once);
        }
    }

    [Fact]
    public void OnDeactivate_ShouldReleaseFontReferences()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            var font = new Mock<IFont>();
            var boldFont = new Mock<IFont>();
            ReflectionHelpers.SetPrivateField(stage, "_font", font.Object);
            ReflectionHelpers.SetPrivateField(stage, "_boldFont", boldFont.Object);

            ReflectionHelpers.InvokePrivateMethod(stage, "OnDeactivate");

            font.Verify(f => f.RemoveReference(), Times.Once);
            boldFont.Verify(f => f.RemoveReference(), Times.Once);
            Assert.Null(ReflectionHelpers.GetPrivateField<IFont>(stage, "_font"));
            Assert.Null(ReflectionHelpers.GetPrivateField<IFont>(stage, "_boldFont"));
        }
    }

    [Fact]
    public void Dispose_WhenDisposing_ShouldReleaseFontReferences()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            var font = new Mock<IFont>();
            var boldFont = new Mock<IFont>();
            ReflectionHelpers.SetPrivateField(stage, "_font", font.Object);
            ReflectionHelpers.SetPrivateField(stage, "_boldFont", boldFont.Object);
            ReflectionHelpers.SetPrivateField(stage, "_disposed", false);

            var method = typeof(ConfigStage).GetMethod(
                "Dispose",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                binder: null,
                types: new[] { typeof(bool) },
                modifiers: null);
            Assert.NotNull(method);
            method!.Invoke(stage, new object[] { true });

            font.Verify(f => f.RemoveReference(), Times.Once);
            boldFont.Verify(f => f.RemoveReference(), Times.Once);
        }
    }

    [Fact]
    public void DrawTitle_WhenFontExists_ShouldUseMeasureStringAndDrawString()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            var font = new Mock<IFont>();
            font.Setup(f => f.MeasureString(It.IsAny<string>())).Returns(new Vector2(200, 20));
            var spriteBatch = CreateUninitializedSpriteBatch();
            ReflectionHelpers.SetPrivateField(stage, "_font", font.Object);
            ReflectionHelpers.SetPrivateField(stage, "_spriteBatch", spriteBatch);

            ReflectionHelpers.InvokePrivateMethod(stage, "DrawTitle");

            font.Verify(f => f.MeasureString("CONFIGURATION"), Times.Once);
            font.Verify(f => f.DrawString(spriteBatch, "CONFIGURATION", new Vector2(540, 50), Color.White), Times.Once);
        }
    }

    [Fact]
    public void DrawTitle_WhenFontNull_ShouldNotThrow()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            ReflectionHelpers.SetPrivateField(stage, "_font", null);

            var exception = Record.Exception(() => ReflectionHelpers.InvokePrivateMethod(stage, "DrawTitle"));

            Assert.Null(exception);
        }
    }

    [Fact]
    public void DrawConfigItems_WhenSelected_ShouldUseBoldFont()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: false);
            var font = new Mock<IFont>();
            var boldFont = new Mock<IFont>();
            var spriteBatch = CreateUninitializedSpriteBatch();
            ReflectionHelpers.SetPrivateField(stage, "_font", font.Object);
            ReflectionHelpers.SetPrivateField(stage, "_boldFont", boldFont.Object);
            ReflectionHelpers.SetPrivateField(stage, "_spriteBatch", spriteBatch);
            ReflectionHelpers.SetPrivateField(stage, "_selectedIndex", 0);

            ReflectionHelpers.InvokePrivateMethod(stage, "DrawConfigItems");

            boldFont.Verify(f => f.DrawString(spriteBatch, It.IsAny<string>(), It.IsAny<Vector2>(), Color.Yellow), Times.AtLeastOnce);
        }
    }

    [Fact]
    public void DrawConfigItems_WhenFontIsNull_ShouldUseRectangleFallbackWithoutThrowing()
    {
        var (stage, _, inputManager) = CreateRenderSpyStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: false);
            var configItems = ReflectionHelpers.GetPrivateField<List<IConfigItem>>(stage, "_configItems");
            Assert.NotNull(configItems);
            ReflectionHelpers.SetPrivateField(stage, "_selectedIndex", configItems!.Count);
            SetFallbackDrawingState(stage);

            var exception = Record.Exception(() => ReflectionHelpers.InvokePrivateMethod(stage, "DrawConfigItems"));

            Assert.Null(exception);
            var expectedWidths = configItems
                .ConvertAll(item => item.GetDisplayText().Length * 8)
                .OrderBy(width => width)
                .ToArray();
            var actualWidths = stage.RectangleDrawCalls
                .FindAll(call => call.Rectangle.Height == 16 && call.Color == Color.White)
                .ConvertAll(call => call.Rectangle.Width)
                .OrderBy(width => width)
                .ToArray();
            Assert.Equal(expectedWidths, actualWidths);
        }
    }

    [Fact]
    public void DrawButtons_WhenFontIsNull_ShouldUseRectangleFallbackWithoutThrowing()
    {
        var (stage, _, inputManager) = CreateRenderSpyStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: false);
            ReflectionHelpers.SetPrivateField(stage, "_selectedIndex", 0);
            SetFallbackDrawingState(stage);

            var exception = Record.Exception(() => ReflectionHelpers.InvokePrivateMethod(stage, "DrawButtons"));

            Assert.Null(exception);
            var actualButtonRects = stage.RectangleDrawCalls
                .FindAll(call => call.Rectangle.Height == 16)
                .ConvertAll(call => (Width: call.Rectangle.Width, call.Color))
                .OrderBy(call => call.Width)
                .ToArray();
            var expectedButtonRects = new[]
            {
                (Width: "BACK".Length * 8, Color: Color.Gray),
                (Width: "SAVE & EXIT".Length * 8, Color: Color.Green)
            }
            .OrderBy(call => call.Width)
            .ToArray();
            Assert.Equal(expectedButtonRects, actualButtonRects);
        }
    }

    [Fact]
    public void DrawInstructions_WhenFontIsNull_ShouldUseRectangleFallbackWithoutThrowing()
    {
        var (stage, _, inputManager) = CreateRenderSpyStage();
        using (inputManager)
        {
            SetFallbackDrawingState(stage);

            var exception = Record.Exception(() => ReflectionHelpers.InvokePrivateMethod(stage, "DrawInstructions"));

            Assert.Null(exception);
            Assert.Contains(
                stage.RectangleDrawCalls,
                call => call.Rectangle.Height == 12 && call.Rectangle.Width > 0 && call.Color == Color.Gray);
        }
    }

    [Fact]
    public void DrawButtons_WhenBackSelectedAndFontPresent_ShouldUseBoldFont()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: false);
            var configItems = ReflectionHelpers.GetPrivateField<List<IConfigItem>>(stage, "_configItems");
            Assert.NotNull(configItems);
            var boldFont = new Mock<IFont>();
            var spriteBatch = CreateUninitializedSpriteBatch();
            ReflectionHelpers.SetPrivateField(stage, "_font", new Mock<IFont>().Object);
            ReflectionHelpers.SetPrivateField(stage, "_boldFont", boldFont.Object);
            ReflectionHelpers.SetPrivateField(stage, "_spriteBatch", spriteBatch);
            ReflectionHelpers.SetPrivateField(stage, "_selectedIndex", configItems!.Count);

            ReflectionHelpers.InvokePrivateMethod(stage, "DrawButtons");

            boldFont.Verify(f => f.DrawString(spriteBatch, "BACK", It.IsAny<Vector2>(), Color.Yellow), Times.Once);
        }
    }

    [Fact]
    public void DrawButtons_WhenSaveSelectedAndFontPresent_ShouldUseBoldFont()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: false);
            var configItems = ReflectionHelpers.GetPrivateField<List<IConfigItem>>(stage, "_configItems");
            Assert.NotNull(configItems);
            var boldFont = new Mock<IFont>();
            var spriteBatch = CreateUninitializedSpriteBatch();
            ReflectionHelpers.SetPrivateField(stage, "_font", new Mock<IFont>().Object);
            ReflectionHelpers.SetPrivateField(stage, "_boldFont", boldFont.Object);
            ReflectionHelpers.SetPrivateField(stage, "_spriteBatch", spriteBatch);
            ReflectionHelpers.SetPrivateField(stage, "_selectedIndex", configItems!.Count + 1);

            ReflectionHelpers.InvokePrivateMethod(stage, "DrawButtons");

            boldFont.Verify(f => f.DrawString(spriteBatch, "SAVE & EXIT", It.IsAny<Vector2>(), Color.Yellow), Times.Once);
        }
    }

    [Fact]
    public void DrawInstructions_WhenFontPresent_ShouldDrawText()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            var font = new Mock<IFont>();
            var spriteBatch = CreateUninitializedSpriteBatch();
            ReflectionHelpers.SetPrivateField(stage, "_font", font.Object);
            ReflectionHelpers.SetPrivateField(stage, "_spriteBatch", spriteBatch);

            ReflectionHelpers.InvokePrivateMethod(stage, "DrawInstructions");

            font.Verify(f => f.DrawString(spriteBatch, It.Is<string>(s => s.Contains("UP/DOWN")), It.IsAny<Vector2>(), Color.White), Times.Once);
        }
    }

    private static void SetFallbackDrawingState(ConfigStage stage)
    {
        ReflectionHelpers.SetPrivateField(stage, "_font", null);
        ReflectionHelpers.SetPrivateField(stage, "_boldFont", null);
        ReflectionHelpers.SetPrivateField(stage, "_spriteBatch", CreateUninitializedSpriteBatch());
        ReflectionHelpers.SetPrivateField(stage, "_whitePixel", CreateUninitializedTexture());
    }

    private static SpriteBatch CreateUninitializedSpriteBatch()
    {
#pragma warning disable SYSLIB0050
        var sb = (SpriteBatch)FormatterServices.GetUninitializedObject(typeof(SpriteBatch));
#pragma warning restore SYSLIB0050
        GC.SuppressFinalize(sb);
        return sb;
    }

    private static Texture2D CreateUninitializedTexture()
    {
#pragma warning disable SYSLIB0050
        var texture = (Texture2D)FormatterServices.GetUninitializedObject(typeof(Texture2D));
#pragma warning restore SYSLIB0050
        GC.SuppressFinalize(texture);
        return texture;
    }

    private sealed class RenderSpyConfigStage : ConfigStage
    {
        public RenderSpyConfigStage(BaseGame game)
            : base(game)
        {
        }

        public List<(Rectangle Rectangle, Color Color)> RectangleDrawCalls { get; } = [];

        protected override void DrawFilledRectangle(Rectangle destinationRectangle, Color color)
        {
            RectangleDrawCalls.Add((destinationRectangle, color));
        }
    }
}
