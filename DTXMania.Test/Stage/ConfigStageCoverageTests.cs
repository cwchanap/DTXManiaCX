using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
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
}
