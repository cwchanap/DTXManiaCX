using System.Collections.Generic;
using System.IO;
using System.Reflection;
using DTXMania.Game;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Stage;
using DTXMania.Game.Lib.Stage.KeyAssign;
using DTXMania.Game.Lib.Utilities;
using DTXMania.Test.TestData;
using Microsoft.Xna.Framework.Input;

namespace DTXMania.Test.Config;

[Trait("Category", "Unit")]
public class ConfigStageLogicTests
{
    [Fact]
    public void LoadConfiguration_ShouldCloneConfigAndResetUnsavedChanges()
    {
        var (stage, configManager, inputManager) = CreateStage();
        using (inputManager)
        {
            configManager.Config.ScreenWidth = 1920;
            configManager.Config.ScreenHeight = 1080;
            configManager.Config.FullScreen = true;
            configManager.Config.VSyncWait = false;
            configManager.Config.NoFail = true;
            configManager.Config.AutoPlay = true;
            ReflectionHelpers.SetPrivateField(stage, "_hasUnsavedChanges", true);

            ReflectionHelpers.InvokePrivateMethod(stage, "LoadConfiguration");

            var workingConfig = ReflectionHelpers.GetPrivateField<ConfigData>(stage, "_workingConfig");
            Assert.NotNull(workingConfig);
            Assert.NotSame(configManager.Config, workingConfig);
            Assert.Equal(1920, workingConfig!.ScreenWidth);
            Assert.Equal(1080, workingConfig.ScreenHeight);
            Assert.True(workingConfig.FullScreen);
            Assert.False(workingConfig.VSyncWait);
            Assert.True(workingConfig.NoFail);
            Assert.True(workingConfig.AutoPlay);
            Assert.False(ReflectionHelpers.GetPrivateField<bool>(stage, "_hasUnsavedChanges"));
        }
    }

    [Fact]
    public void SetupConfigItems_ShouldCreateExpectedItemsAndSelectFirstItem()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: false);

            var configItems = ReflectionHelpers.GetPrivateField<List<IConfigItem>>(stage, "_configItems");
            Assert.NotNull(configItems);
            Assert.Equal(7, configItems!.Count);
            Assert.Collection(configItems,
                item => Assert.Equal("Screen Resolution", item.Name),
                item => Assert.Equal("Fullscreen", item.Name),
                item => Assert.Equal("VSync Wait", item.Name),
                item => Assert.Equal("No Fail", item.Name),
                item => Assert.Equal("Auto Play", item.Name),
                item => Assert.Equal("Drum Key Mapping", item.Name),
                item => Assert.Equal("System Key Mapping", item.Name));
            Assert.Equal(0, ReflectionHelpers.GetPrivateField<int>(stage, "_selectedIndex"));
        }
    }

    [Fact]
    public void HandleInput_WhenMoveUpPressedAtFirstItem_WrapsToSaveButton()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: false);
            ReflectionHelpers.SetPrivateField(stage, "_selectedIndex", 0);
            SetKeyboardStates(stage, new KeyboardState(Keys.Up), new KeyboardState());

            ReflectionHelpers.InvokePrivateMethod(stage, "HandleInput");

            var configItems = ReflectionHelpers.GetPrivateField<List<IConfigItem>>(stage, "_configItems");
            Assert.Equal(configItems!.Count + 1, ReflectionHelpers.GetPrivateField<int>(stage, "_selectedIndex"));
        }
    }

    [Fact]
    public void HandleInput_WhenMoveDownPressedAtSaveButton_WrapsToFirstItem()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: false);
            var configItems = ReflectionHelpers.GetPrivateField<List<IConfigItem>>(stage, "_configItems");
            ReflectionHelpers.SetPrivateField(stage, "_selectedIndex", configItems!.Count + 1);
            SetKeyboardStates(stage, new KeyboardState(Keys.Down), new KeyboardState());

            ReflectionHelpers.InvokePrivateMethod(stage, "HandleInput");

            Assert.Equal(0, ReflectionHelpers.GetPrivateField<int>(stage, "_selectedIndex"));
        }
    }

    [Fact]
    public void HandleInput_WhenMoveRightPressedOnResolution_UpdatesWorkingConfigAndMarksChanges()
    {
        var (stage, configManager, inputManager) = CreateStage();
        using (inputManager)
        {
            configManager.Config.ScreenWidth = 1280;
            configManager.Config.ScreenHeight = 720;
            InitializeStageMenu(stage, includePanels: false);
            ReflectionHelpers.SetPrivateField(stage, "_selectedIndex", 0);
            SetKeyboardStates(stage, new KeyboardState(Keys.Right), new KeyboardState());

            ReflectionHelpers.InvokePrivateMethod(stage, "HandleInput");

            var workingConfig = ReflectionHelpers.GetPrivateField<ConfigData>(stage, "_workingConfig");
            Assert.NotNull(workingConfig);
            Assert.Equal(1920, workingConfig!.ScreenWidth);
            Assert.Equal(1080, workingConfig.ScreenHeight);
            Assert.True(ReflectionHelpers.GetPrivateField<bool>(stage, "_hasUnsavedChanges"));
        }
    }

    [Fact]
    public void HandleInput_WhenMoveLeftPressedOnResolution_UpdatesWorkingConfigAndMarksChanges()
    {
        var (stage, configManager, inputManager) = CreateStage();
        using (inputManager)
        {
            configManager.Config.ScreenWidth = 1920;
            configManager.Config.ScreenHeight = 1080;
            InitializeStageMenu(stage, includePanels: false);
            ReflectionHelpers.SetPrivateField(stage, "_selectedIndex", 0);
            SetKeyboardStates(stage, new KeyboardState(Keys.Left), new KeyboardState());

            ReflectionHelpers.InvokePrivateMethod(stage, "HandleInput");

            var workingConfig = ReflectionHelpers.GetPrivateField<ConfigData>(stage, "_workingConfig");
            Assert.NotNull(workingConfig);
            Assert.Equal(1280, workingConfig!.ScreenWidth);
            Assert.Equal(720, workingConfig.ScreenHeight);
            Assert.True(ReflectionHelpers.GetPrivateField<bool>(stage, "_hasUnsavedChanges"));
        }
    }

    [Fact]
    public void HandleInput_WhenActivatePressedOnToggleItem_TogglesWorkingFlag()
    {
        var (stage, configManager, inputManager) = CreateStage();
        using (inputManager)
        {
            configManager.Config.FullScreen = false;
            InitializeStageMenu(stage, includePanels: false);
            ReflectionHelpers.SetPrivateField(stage, "_selectedIndex", 1);
            SetKeyboardStates(stage, new KeyboardState(Keys.Enter), new KeyboardState());

            ReflectionHelpers.InvokePrivateMethod(stage, "HandleInput");

            var workingConfig = ReflectionHelpers.GetPrivateField<ConfigData>(stage, "_workingConfig");
            Assert.NotNull(workingConfig);
            Assert.True(workingConfig!.FullScreen);
            Assert.True(ReflectionHelpers.GetPrivateField<bool>(stage, "_hasUnsavedChanges"));
        }
    }

    [Fact]
    public void HandleInput_WhenActivatePressedOnDrumKeyMapping_OpensAndActivatesPanel()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: true);
            ReflectionHelpers.SetPrivateField(stage, "_selectedIndex", 5);
            SetKeyboardStates(stage, new KeyboardState(Keys.Enter), new KeyboardState());

            ReflectionHelpers.InvokePrivateMethod(stage, "HandleInput");

            var activePanel = ReflectionHelpers.GetPrivateField<IKeyAssignPanel>(stage, "_activePanel");
            var drumPanel = ReflectionHelpers.GetPrivateField<DrumKeyAssignPanel>(stage, "_drumPanel");
            Assert.NotNull(activePanel);
            Assert.NotNull(drumPanel);
            Assert.Same(drumPanel, activePanel);
            Assert.True(activePanel!.IsActive);
        }
    }

    [Fact]
    public void OnPanelSaved_WhenDrumPanelSaves_ShouldCaptureWorkingBindingsAndMarkUnsavedChanges()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: true);
            var drumPanel = ReflectionHelpers.GetPrivateField<DrumKeyAssignPanel>(stage, "_drumPanel");
            Assert.NotNull(drumPanel);

            var updatedBindings = new KeyBindings();
            updatedBindings.ClearAllBindings();
            updatedBindings.BindButton("Key.Z", 6);
            ReflectionHelpers.SetPrivateField(drumPanel!, "_workingBindings", updatedBindings);

            ReflectionHelpers.InvokePrivateMethod(stage, "OnPanelSaved", drumPanel!, EventArgs.Empty);

            var workingBindings = ReflectionHelpers.GetPrivateField<KeyBindings>(stage, "_workingDrumBindings");
            Assert.NotNull(workingBindings);
            Assert.Equal(6, workingBindings!.GetLane("Key.Z"));
            Assert.True(ReflectionHelpers.GetPrivateField<bool>(stage, "_hasUnsavedChanges"));
        }
    }

    [Fact]
    public void IsConfigNavigationCommandPressed_WhenInputManagerReportsPressed_ShouldReturnTrue()
    {
        var configManager = new ConfigManager();
        using var inputManager = new ForcedCommandInputManager(configManager, InputCommandType.MoveUp);
        var game = ReflectionHelpers.CreateGame();
        ReflectionHelpers.SetPrivateField(game, "<ConfigManager>k__BackingField", configManager);
        ReflectionHelpers.SetPrivateField(game, "<InputManager>k__BackingField", inputManager);
        var stage = new ConfigStage(game);

        InitializeStageMenu(stage, includePanels: false);

        var result = (bool)ReflectionHelpers.InvokePrivateMethod(stage, "IsConfigNavigationCommandPressed", InputCommandType.MoveUp)!;

        Assert.True(result);
    }

    [Fact]
    public void HandleInput_WhenBackCommandPressed_ShouldReturnToTitleStage()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: false);
            var stageManager = new Moq.Mock<IStageManager>();
            stage.StageManager = stageManager.Object;
            ReflectionHelpers.SetPrivateField(stage, "_hasUnsavedChanges", true);
            SetKeyboardStates(stage, new KeyboardState(Keys.Escape), new KeyboardState());

            ReflectionHelpers.InvokePrivateMethod(stage, "HandleInput");

            stageManager.Verify(
                manager => manager.ChangeStage(
                    StageType.Title,
                    Moq.It.Is<IStageTransition>(transition => transition is CrossfadeTransition)),
                Moq.Times.Once);
        }
    }

    [Fact]
    public void HandleInput_WhenActivatePressedOnBackButton_ShouldReturnToTitleStage()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: false);
            var stageManager = new Moq.Mock<IStageManager>();
            stage.StageManager = stageManager.Object;
            var configItems = ReflectionHelpers.GetPrivateField<List<IConfigItem>>(stage, "_configItems");
            ReflectionHelpers.SetPrivateField(stage, "_selectedIndex", configItems!.Count);
            ReflectionHelpers.SetPrivateField(stage, "_hasUnsavedChanges", true);
            SetKeyboardStates(stage, new KeyboardState(Keys.Enter), new KeyboardState());

            ReflectionHelpers.InvokePrivateMethod(stage, "HandleInput");

            stageManager.Verify(
                manager => manager.ChangeStage(
                    StageType.Title,
                    Moq.It.Is<IStageTransition>(transition => transition is CrossfadeTransition)),
                Moq.Times.Once);
        }
    }

    [Fact]
    public void HandleInput_WhenActivatePressedOnSaveButton_ShouldApplyConfigurationAndTransitionToTitle()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: false);
            var stageManager = new Moq.Mock<IStageManager>();
            var configItems = ReflectionHelpers.GetPrivateField<List<IConfigItem>>(stage, "_configItems");
            var workingConfig = new ConfigData
            {
                ScreenWidth = 1920,
                ScreenHeight = 1080,
                FullScreen = true,
                VSyncWait = false,
                NoFail = true,
                AutoPlay = true
            };
            var workingSystemBindings = new Dictionary<Keys, InputCommandType>
            {
                [Keys.W] = InputCommandType.MoveUp,
                [Keys.S] = InputCommandType.MoveDown,
                [Keys.A] = InputCommandType.MoveLeft,
                [Keys.D] = InputCommandType.MoveRight,
                [Keys.Enter] = InputCommandType.Activate,
                [Keys.Escape] = InputCommandType.Back
            };
            var configManager = new RecordingConfigManager(new ConfigData());

            stage.StageManager = stageManager.Object;
            ReflectionHelpers.SetPrivateField(stage, "_configManager", configManager);
            ReflectionHelpers.SetPrivateField(stage, "_workingConfig", workingConfig);
            ReflectionHelpers.SetPrivateField(stage, "_workingDrumBindings", new KeyBindings());
            ReflectionHelpers.SetPrivateField(stage, "_workingSystemBindings", workingSystemBindings);
            ReflectionHelpers.SetPrivateField(stage, "_hasUnsavedChanges", true);
            ReflectionHelpers.SetPrivateField(stage, "_selectedIndex", configItems!.Count + 1);
            SetKeyboardStates(stage, new KeyboardState(Keys.Enter), new KeyboardState());

            ReflectionHelpers.InvokePrivateMethod(stage, "HandleInput");

            Assert.Equal(AppPaths.GetConfigFilePath(), configManager.LastSavePath);
            Assert.Equal(1920, configManager.Config.ScreenWidth);
            Assert.Equal(1080, configManager.Config.ScreenHeight);
            Assert.True(configManager.Config.FullScreen);
            Assert.False(configManager.Config.VSyncWait);
            Assert.True(configManager.Config.NoFail);
            Assert.True(configManager.Config.AutoPlay);
            Assert.False(ReflectionHelpers.GetPrivateField<bool>(stage, "_hasUnsavedChanges"));

            var snapshot = inputManager.GetKeyMappingSnapshot();
            Assert.Equal(workingSystemBindings.Count, snapshot.Count);
            foreach (var (key, command) in workingSystemBindings)
            {
                Assert.True(snapshot.ContainsKey(key));
                Assert.Equal(command, snapshot[key]);
            }

            stageManager.Verify(
                manager => manager.ChangeStage(
                    StageType.Title,
                    Moq.It.Is<IStageTransition>(transition => transition is CrossfadeTransition)),
                Moq.Times.Once);
        }
    }

    [Fact]
    public void ApplyConfiguration_WhenSaveFails_ShouldRollBackConfigStateAndReturnFalse()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: false);
            var originalConfig = new ConfigData
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
                SystemKeyBindings = new Dictionary<string, string> { ["MoveUp"] = "Up" }
            };
            var workingConfig = new ConfigData
            {
                ScreenWidth = 2560,
                ScreenHeight = 1440,
                FullScreen = true,
                VSyncWait = false,
                NoFail = true,
                AutoPlay = true
            };
            var configManager = new RecordingConfigManager(originalConfig, new IOException("disk full"));
            var snapshotBefore = new Dictionary<Keys, InputCommandType>(inputManager.GetKeyMappingSnapshot());

            ReflectionHelpers.SetPrivateField(stage, "_configManager", configManager);
            ReflectionHelpers.SetPrivateField(stage, "_workingConfig", workingConfig);
            ReflectionHelpers.SetPrivateField(stage, "_workingDrumBindings", new KeyBindings());
            ReflectionHelpers.SetPrivateField(stage, "_workingSystemBindings", new Dictionary<Keys, InputCommandType>
            {
                [Keys.W] = InputCommandType.MoveUp,
                [Keys.S] = InputCommandType.MoveDown
            });
            ReflectionHelpers.SetPrivateField(stage, "_hasUnsavedChanges", true);

            var result = (bool)ReflectionHelpers.InvokePrivateMethod(stage, "ApplyConfiguration")!;

            Assert.False(result);
            Assert.Equal(AppPaths.GetConfigFilePath(), configManager.LastSavePath);
            Assert.Equal(1280, configManager.Config.ScreenWidth);
            Assert.Equal(720, configManager.Config.ScreenHeight);
            Assert.False(configManager.Config.FullScreen);
            Assert.True(configManager.Config.VSyncWait);
            Assert.False(configManager.Config.NoFail);
            Assert.False(configManager.Config.AutoPlay);
            Assert.Equal(0, configManager.Config.KeyBindings["Key.A"]);
            Assert.Contains(3, configManager.Config.UnboundDrumLanes);
            Assert.Contains("Key.B", configManager.Config.UnboundDrumButtons);
            Assert.Equal("Up", configManager.Config.SystemKeyBindings["MoveUp"]);
            Assert.True(ReflectionHelpers.GetPrivateField<bool>(stage, "_hasUnsavedChanges"));

            var snapshotAfter = inputManager.GetKeyMappingSnapshot();
            Assert.Equal(snapshotBefore.Count, snapshotAfter.Count);
            foreach (var (key, command) in snapshotBefore)
            {
                Assert.True(snapshotAfter.ContainsKey(key));
                Assert.Equal(command, snapshotAfter[key]);
            }
        }
    }

    [Fact]
    public void OnDeactivate_WhenPanelIsActive_DeactivatesPanelAndClearsKeyboardState()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: true);

            var drumPanel = ReflectionHelpers.GetPrivateField<DrumKeyAssignPanel>(stage, "_drumPanel");
            Assert.NotNull(drumPanel);
            ReflectionHelpers.InvokePrivateMethod(stage, "OpenPanel", drumPanel!);
            SetKeyboardStates(stage, new KeyboardState(Keys.Enter), new KeyboardState(Keys.Down));

            ReflectionHelpers.InvokePrivateMethod(stage, "OnDeactivate");

            Assert.False(drumPanel!.IsActive);
            Assert.Null(ReflectionHelpers.GetPrivateField<IKeyAssignPanel>(stage, "_activePanel"));
            Assert.False(ReflectionHelpers.GetPrivateField<KeyboardState>(stage, "_currentKeyboardState").IsKeyDown(Keys.Enter));
            Assert.False(ReflectionHelpers.GetPrivateField<KeyboardState>(stage, "_previousKeyboardState").IsKeyDown(Keys.Down));
        }
    }

    [Fact]
    public void ApplySystemBindings_ShouldReplaceExistingMappings()
    {
        var inputManager = new InputManager();
        var bindings = new Dictionary<Keys, InputCommandType>
        {
            [Keys.W] = InputCommandType.MoveUp,
            [Keys.S] = InputCommandType.MoveDown,
            [Keys.F] = InputCommandType.Activate
        };

        var method = typeof(ConfigStage).GetMethod("ApplySystemBindings", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        method!.Invoke(null, new object[] { inputManager, bindings });

        var snapshot = inputManager.GetKeyMappingSnapshot();
        Assert.Equal(bindings.Count, snapshot.Count);
        Assert.DoesNotContain(Keys.Up, snapshot.Keys);
        Assert.DoesNotContain(Keys.Escape, snapshot.Keys);
        Assert.Equal(InputCommandType.MoveUp, snapshot[Keys.W]);
        Assert.Equal(InputCommandType.MoveDown, snapshot[Keys.S]);
        Assert.Equal(InputCommandType.Activate, snapshot[Keys.F]);
    }

    private static (ConfigStage Stage, ConfigManager ConfigManager, InputManagerCompat InputManager) CreateStage()
    {
        var configManager = new ConfigManager();
        var inputManager = new InputManagerCompat(configManager);
        var game = ReflectionHelpers.CreateGame();
        ReflectionHelpers.SetPrivateField(game, "<ConfigManager>k__BackingField", configManager);
        ReflectionHelpers.SetPrivateField(game, "<InputManager>k__BackingField", inputManager);
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

    private sealed class RecordingConfigManager : IConfigManager
    {
        private readonly Exception? _saveException;

        public RecordingConfigManager(ConfigData config, Exception? saveException = null)
        {
            Config = config;
            _saveException = saveException;
        }

        public ConfigData Config { get; }

        public string? LastSavePath { get; private set; }

        public void LoadConfig(string filePath)
        {
        }

        public void SaveConfig(string filePath)
        {
            LastSavePath = filePath;
            if (_saveException != null)
            {
                throw _saveException;
            }
        }

        public void ResetToDefaults()
        {
        }
    }

    private sealed class ForcedCommandInputManager : InputManagerCompat
    {
        private readonly InputCommandType _pressedCommand;

        public ForcedCommandInputManager(ConfigManager configManager, InputCommandType pressedCommand)
            : base(configManager)
        {
            _pressedCommand = pressedCommand;
        }

        public override bool IsCommandPressed(InputCommandType command)
        {
            return command == _pressedCommand;
        }
    }
}
