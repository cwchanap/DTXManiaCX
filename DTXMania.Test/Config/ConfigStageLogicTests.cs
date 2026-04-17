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
    public void MoveUpPressedAtFirstItem_ShouldWrapToSaveButton()
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
    public void MoveDownPressedAtSaveButton_ShouldWrapToFirstItem()
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
    public void MoveRightPressedOnResolution_ShouldUpdateWorkingConfigAndMarkChanges()
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
    public void MoveLeftPressedOnResolution_ShouldUpdateWorkingConfigAndMarkChanges()
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
    public void ActivatePressedOnToggleItem_ShouldToggleWorkingFlag()
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
    public void ActivatePressedOnDrumKeyMapping_ShouldOpenAndActivatePanel()
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
    public void DrumPanelSaves_ShouldCaptureWorkingBindingsAndMarkUnsavedChanges()
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
    public void InputManagerReportsPressed_ShouldReturnTrueForConfigNavigationCommand()
    {
        var configManager = new ConfigManager();
        using var inputManager = new ForcedCommandInputManager(configManager, InputCommandType.MoveUp);
        var game = ReflectionHelpers.CreateGame();
        ReflectionHelpers.SetProperty(game, nameof(BaseGame.ConfigManager), configManager);
        ReflectionHelpers.SetProperty(game, nameof(BaseGame.InputManager), inputManager);
        var stage = new ConfigStage(game);

        InitializeStageMenu(stage, includePanels: false);

        var result = (bool)ReflectionHelpers.InvokePrivateMethod(stage, "IsConfigNavigationCommandPressed", InputCommandType.MoveUp)!;

        Assert.True(result);
    }

    [Fact]
    public void BackCommandPressed_ShouldReturnToTitleStage()
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
    public void BackCommandPressed_WithoutUnsavedChanges_ShouldReturnToTitleStage()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: false);
            var stageManager = new Moq.Mock<IStageManager>();
            stage.StageManager = stageManager.Object;
            ReflectionHelpers.SetPrivateField(stage, "_selectedIndex", 7);
            ReflectionHelpers.SetPrivateField(stage, "_hasUnsavedChanges", false);
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
    public void ActivatePressedOnBackButton_ShouldReturnToTitleStage()
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
    public void ActivatePressedOnSaveButton_ShouldApplyConfigurationAndTransitionToTitle()
    {
        var redirectedSavePath = CreateConfigSavePath();
        var configManager = new RecordingConfigManager(new ConfigData(), redirectedSavePath: redirectedSavePath);

        try
        {
            var (stage, _, inputManager) = CreateStage(configManager);
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
                var workingDrumBindings = CreateWorkingDrumBindings();
                var workingSystemBindings = new Dictionary<Keys, InputCommandType>
                {
                    [Keys.W] = InputCommandType.MoveUp,
                    [Keys.S] = InputCommandType.MoveDown,
                    [Keys.A] = InputCommandType.MoveLeft,
                    [Keys.D] = InputCommandType.MoveRight,
                    [Keys.Enter] = InputCommandType.Activate,
                    [Keys.Escape] = InputCommandType.Back
                };

                stage.StageManager = stageManager.Object;
                ReflectionHelpers.SetPrivateField(stage, "_workingConfig", workingConfig);
                ReflectionHelpers.SetPrivateField(stage, "_workingDrumBindings", workingDrumBindings);
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
                Assert.Equal(4, configManager.Config.KeyBindings["Key.Z"]);
                Assert.DoesNotContain("Key.S", configManager.Config.KeyBindings.Keys);
                Assert.Contains(0, configManager.Config.UnboundDrumLanes);
                Assert.Contains("Key.S", configManager.Config.UnboundDrumButtons);
                Assert.Equal("W", configManager.Config.SystemKeyBindings["SystemKey.MoveUp"]);
                Assert.False(ReflectionHelpers.GetPrivateField<bool>(stage, "_hasUnsavedChanges"));

                Assert.Equal(4, inputManager.ModularInputManager.KeyBindings.GetLane("Key.Z"));
                Assert.Equal(-1, inputManager.ModularInputManager.KeyBindings.GetLane("Key.A"));
                Assert.Equal(-1, inputManager.ModularInputManager.KeyBindings.GetLane("Key.S"));

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
        finally
        {
            DeleteConfigSavePath(redirectedSavePath);
        }
    }

    [Fact]
    public void SaveFailure_ShouldRollBackConfigStateAndReturnFalse()
    {
        var configManager = new RecordingConfigManager(originalConfig: new ConfigData
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
            var workingConfig = new ConfigData
            {
                ScreenWidth = 2560,
                ScreenHeight = 1440,
                FullScreen = true,
                VSyncWait = false,
                NoFail = true,
                AutoPlay = true
            };
            var workingDrumBindings = CreateWorkingDrumBindings();
            var snapshotBefore = new Dictionary<Keys, InputCommandType>(inputManager.GetKeyMappingSnapshot());

            ReflectionHelpers.SetPrivateField(stage, "_workingConfig", workingConfig);
            ReflectionHelpers.SetPrivateField(stage, "_workingDrumBindings", workingDrumBindings);
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
            Assert.DoesNotContain("Key.Z", configManager.Config.KeyBindings.Keys);
            Assert.Contains(3, configManager.Config.UnboundDrumLanes);
            Assert.Contains("Key.B", configManager.Config.UnboundDrumButtons);
            Assert.Equal("Up", configManager.Config.SystemKeyBindings["SystemKey.MoveUp"]);
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
    public void PanelIsActiveOnDeactivate_ShouldDeactivatePanelAndClearKeyboardState()
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

    private static (ConfigStage Stage, ConfigManager ConfigManager, InputManagerCompat InputManager) CreateStage(ConfigManager? configManager = null)
    {
        configManager ??= new ConfigManager();
        var inputManager = new InputManagerCompat(configManager);
        var game = ReflectionHelpers.CreateGame();
        ReflectionHelpers.SetProperty(game, nameof(BaseGame.ConfigManager), configManager);
        ReflectionHelpers.SetProperty(game, nameof(BaseGame.InputManager), inputManager);
        return (new ConfigStage(game), configManager, inputManager);
    }

    private static KeyBindings CreateWorkingDrumBindings()
    {
        var workingDrumBindings = new KeyBindings();
        workingDrumBindings.UnbindLane(0);
        workingDrumBindings.UnbindLane(4);
        workingDrumBindings.BindButton("Key.Z", 4);
        return workingDrumBindings;
    }

    private static string CreateConfigSavePath()
    {
        var directory = Path.Combine(
            AppContext.BaseDirectory,
            "TestResults",
            "config-stage-logic",
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

    private static void CopyConfigData(ConfigData target, ConfigData source)
    {
        target.DTXManiaVersion = source.DTXManiaVersion;
        target.SkinPath = source.SkinPath;
        target.DTXPath = source.DTXPath;
        target.UseBoxDefSkin = source.UseBoxDefSkin;
        target.SystemSkinRoot = source.SystemSkinRoot;
        target.LastUsedSkin = source.LastUsedSkin;
        target.ScreenWidth = source.ScreenWidth;
        target.ScreenHeight = source.ScreenHeight;
        target.FullScreen = source.FullScreen;
        target.VSyncWait = source.VSyncWait;
        target.MasterVolume = source.MasterVolume;
        target.BGMVolume = source.BGMVolume;
        target.SEVolume = source.SEVolume;
        target.BufferSizeMs = source.BufferSizeMs;
        target.ScrollSpeed = source.ScrollSpeed;
        target.AutoPlay = source.AutoPlay;
        target.NoFail = source.NoFail;
        target.EnableGameApi = source.EnableGameApi;
        target.GameApiPort = source.GameApiPort;
        target.GameApiKey = source.GameApiKey;

        target.KeyBindings = new Dictionary<string, int>(source.KeyBindings);
        target.UnboundDrumLanes = new HashSet<int>(source.UnboundDrumLanes);
        target.UnboundDrumButtons = new HashSet<string>(source.UnboundDrumButtons);
        target.SystemKeyBindings = new Dictionary<string, string>(source.SystemKeyBindings);
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
            {
                throw _saveException;
            }

            base.SaveConfig(_redirectedSavePath ?? filePath);
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

    [Fact]
    public void HandleInput_WithNullActivePanel_ShouldHandleInputNormally()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: false);
            ReflectionHelpers.SetPrivateField(stage, "_activePanel", (IKeyAssignPanel?)null);
            ReflectionHelpers.SetPrivateField(stage, "_selectedIndex", 0);
            SetKeyboardStates(stage, new KeyboardState(Keys.Down), new KeyboardState());

            ReflectionHelpers.InvokePrivateMethod(stage, "HandleInput");

            Assert.Equal(1, ReflectionHelpers.GetPrivateField<int>(stage, "_selectedIndex"));
        }
    }

    [Fact]
    public void HandleInput_MoveLeftOnNavigationItem_ShouldNotChangeAnything()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: false);
            ReflectionHelpers.SetPrivateField(stage, "_selectedIndex", 5);
            SetKeyboardStates(stage, new KeyboardState(Keys.Left), new KeyboardState());

            ReflectionHelpers.InvokePrivateMethod(stage, "HandleInput");

            Assert.False(ReflectionHelpers.GetPrivateField<bool>(stage, "_hasUnsavedChanges"));
        }
    }

    [Fact]
    public void HandleInput_MoveRightOnNavigationItem_ShouldNotChangeAnything()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: false);
            ReflectionHelpers.SetPrivateField(stage, "_selectedIndex", 6);
            SetKeyboardStates(stage, new KeyboardState(Keys.Right), new KeyboardState());

            ReflectionHelpers.InvokePrivateMethod(stage, "HandleInput");

            Assert.False(ReflectionHelpers.GetPrivateField<bool>(stage, "_hasUnsavedChanges"));
        }
    }

    [Fact]
    public void HandleInput_ActivateOnSaveButton_ShouldInvokeSaveButtonClicked()
    {
        var redirectedSavePath = CreateConfigSavePath();
        var configManager = new RecordingConfigManager(new ConfigData(), redirectedSavePath: redirectedSavePath);

        try
        {
            var (stage, _, inputManager) = CreateStage(configManager);
            using (inputManager)
            {
                InitializeStageMenu(stage, includePanels: false);
                var configItems = ReflectionHelpers.GetPrivateField<List<IConfigItem>>(stage, "_configItems");
                var stageManager = new Moq.Mock<IStageManager>();
                stage.StageManager = stageManager.Object;
                ReflectionHelpers.SetPrivateField(stage, "_selectedIndex", configItems!.Count + 1);
                SetKeyboardStates(stage, new KeyboardState(Keys.Enter), new KeyboardState());

                ReflectionHelpers.InvokePrivateMethod(stage, "HandleInput");

                stageManager.Verify(
                    manager => manager.ChangeStage(StageType.Title, Moq.It.IsAny<IStageTransition>()),
                    Moq.Times.Once);
            }
        }
        finally
        {
            DeleteConfigSavePath(redirectedSavePath);
        }
    }

    [Fact]
    public void HandleInput_ActivateOnBackButton_ShouldInvokeBackButtonClicked()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: false);
            var configItems = ReflectionHelpers.GetPrivateField<List<IConfigItem>>(stage, "_configItems");
            var stageManager = new Moq.Mock<IStageManager>();
            stage.StageManager = stageManager.Object;
            ReflectionHelpers.SetPrivateField(stage, "_selectedIndex", configItems!.Count);
            SetKeyboardStates(stage, new KeyboardState(Keys.Enter), new KeyboardState());

            ReflectionHelpers.InvokePrivateMethod(stage, "HandleInput");

            stageManager.Verify(
                manager => manager.ChangeStage(StageType.Title, Moq.It.IsAny<IStageTransition>()),
                Moq.Times.Once);
        }
    }

    [Fact]
    public void ApplyConfiguration_WithNullInputManager_ShouldSkipReloadAndReturnTrue()
    {
        var redirectedSavePath = CreateConfigSavePath();
        var configManager = new RecordingConfigManager(new ConfigData(), redirectedSavePath: redirectedSavePath);

        try
        {
            var (stage, _, inputManager) = CreateStage(configManager);
            using (inputManager)
            {
                InitializeStageMenu(stage, includePanels: false);
                var game = ReflectionHelpers.GetPrivateField<BaseGame>(stage, "_game");
                ReflectionHelpers.SetProperty(game!, nameof(BaseGame.InputManager), (InputManagerCompat?)null);

                var workingConfig = new ConfigData
                {
                    ScreenWidth = 1920,
                    ScreenHeight = 1080,
                    FullScreen = true,
                    VSyncWait = false
                };
                ReflectionHelpers.SetPrivateField(stage, "_workingConfig", workingConfig);
                ReflectionHelpers.SetPrivateField(stage, "_hasUnsavedChanges", true);

                var result = (bool)ReflectionHelpers.InvokePrivateMethod(stage, "ApplyConfiguration")!;

                Assert.True(result);
                Assert.Equal(1920, configManager.Config.ScreenWidth);
                Assert.Equal(1080, configManager.Config.ScreenHeight);
                Assert.False(ReflectionHelpers.GetPrivateField<bool>(stage, "_hasUnsavedChanges"));
            }
        }
        finally
        {
            DeleteConfigSavePath(redirectedSavePath);
        }
    }

    [Fact]
    public void ApplyConfiguration_WithInterfaceOnlyConfigManager_ShouldSaveScalarConfigAndSkipBindingPersistence()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: false);

            var interfaceConfig = new ConfigData
            {
                ScreenWidth = 1280,
                ScreenHeight = 720,
                FullScreen = false,
                VSyncWait = true,
                NoFail = false,
                AutoPlay = false,
                KeyBindings = new Dictionary<string, int> { ["Key.A"] = 0 },
                SystemKeyBindings = new Dictionary<string, string> { ["SystemKey.MoveUp"] = "Up" }
            };
            var interfaceConfigManager = new Moq.Mock<IConfigManager>();
            string? savedPath = null;
            interfaceConfigManager.SetupGet(manager => manager.Config).Returns(interfaceConfig);
            interfaceConfigManager
                .Setup(manager => manager.SaveConfig(Moq.It.IsAny<string>()))
                .Callback<string>(path => savedPath = path);
            ReflectionHelpers.SetPrivateField(stage, "_configManager", interfaceConfigManager.Object);

            var workingSystemBindings = new Dictionary<Keys, InputCommandType>
            {
                [Keys.W] = InputCommandType.MoveUp,
                [Keys.S] = InputCommandType.MoveDown,
                [Keys.Enter] = InputCommandType.Activate,
                [Keys.Escape] = InputCommandType.Back
            };

            ReflectionHelpers.SetPrivateField(stage, "_workingConfig", new ConfigData
            {
                ScreenWidth = 1920,
                ScreenHeight = 1080,
                FullScreen = true,
                VSyncWait = false,
                NoFail = true,
                AutoPlay = true
            });
            ReflectionHelpers.SetPrivateField(stage, "_workingDrumBindings", CreateWorkingDrumBindings());
            ReflectionHelpers.SetPrivateField(stage, "_workingSystemBindings", workingSystemBindings);
            ReflectionHelpers.SetPrivateField(stage, "_hasUnsavedChanges", true);

            var result = (bool)ReflectionHelpers.InvokePrivateMethod(stage, "ApplyConfiguration")!;

            Assert.True(result);
            Assert.Equal(AppPaths.GetConfigFilePath(), savedPath);
            Assert.Equal(1920, interfaceConfig.ScreenWidth);
            Assert.Equal(1080, interfaceConfig.ScreenHeight);
            Assert.True(interfaceConfig.FullScreen);
            Assert.False(interfaceConfig.VSyncWait);
            Assert.True(interfaceConfig.NoFail);
            Assert.True(interfaceConfig.AutoPlay);
            Assert.Equal(0, interfaceConfig.KeyBindings["Key.A"]);
            Assert.Equal("Up", interfaceConfig.SystemKeyBindings["SystemKey.MoveUp"]);
            Assert.False(ReflectionHelpers.GetPrivateField<bool>(stage, "_hasUnsavedChanges"));

            var snapshot = inputManager.GetKeyMappingSnapshot();
            Assert.Equal(workingSystemBindings.Count, snapshot.Count);
            foreach (var (key, command) in workingSystemBindings)
            {
                Assert.True(snapshot.ContainsKey(key));
                Assert.Equal(command, snapshot[key]);
            }
        }
    }

    [Fact]
    public void IsConfigNavigationCommandPressed_FallbackBindingPressed_ShouldReturnTrue()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: false);
            var navigationBindings = new Dictionary<Keys, InputCommandType>
            {
                [Keys.W] = InputCommandType.MoveUp,
                [Keys.Escape] = InputCommandType.Back
            };
            ReflectionHelpers.SetPrivateField(stage, "_navigationBindings", navigationBindings);
            SetKeyboardStates(stage, new KeyboardState(Keys.W), new KeyboardState());

            var result = (bool)ReflectionHelpers.InvokePrivateMethod(
                stage, "IsConfigNavigationCommandPressed", InputCommandType.MoveUp)!;

            Assert.True(result);
        }
    }

    [Fact]
    public void IsConfigNavigationCommandPressed_NoPressedKeys_ShouldReturnFalse()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: false);
            SetKeyboardStates(stage, new KeyboardState(), new KeyboardState());

            var result = (bool)ReflectionHelpers.InvokePrivateMethod(
                stage, "IsConfigNavigationCommandPressed", InputCommandType.MoveUp)!;

            Assert.False(result);
        }
    }

    [Fact]
    public void IsPanelCommandPressed_WorkingBindingPressedByKeyboard_ShouldReturnTrue()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: false);
            var workingSystemBindings = new Dictionary<Keys, InputCommandType>
            {
                [Keys.W] = InputCommandType.MoveUp
            };
            ReflectionHelpers.SetPrivateField(stage, "_workingSystemBindings", workingSystemBindings);
            SetKeyboardStates(stage, new KeyboardState(Keys.W), new KeyboardState());

            var result = (bool)ReflectionHelpers.InvokePrivateMethod(
                stage, "IsPanelCommandPressed", InputCommandType.MoveUp)!;

            Assert.True(result);
        }
    }

    [Fact]
    public void IsPanelCommandPressed_NoMatchingBinding_ShouldReturnFalse()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: false);
            var workingSystemBindings = new Dictionary<Keys, InputCommandType>
            {
                [Keys.W] = InputCommandType.MoveDown
            };
            ReflectionHelpers.SetPrivateField(stage, "_workingSystemBindings", workingSystemBindings);
            SetKeyboardStates(stage, new KeyboardState(), new KeyboardState());

            var result = (bool)ReflectionHelpers.InvokePrivateMethod(
                stage, "IsPanelCommandPressed", InputCommandType.MoveUp)!;

            Assert.False(result);
        }
    }

    [Fact]
    public void OnPanelSaved_WithNullSender_ShouldNotCrashButAlsoNotUpdate()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: true);
            var originalBindings = ReflectionHelpers.GetPrivateField<KeyBindings>(stage, "_workingDrumBindings");
            var originalSystemBindings = ReflectionHelpers.GetPrivateField<Dictionary<Keys, InputCommandType>>(
                stage, "_workingSystemBindings");

            ReflectionHelpers.InvokePrivateMethod(stage, "OnPanelSaved", null, EventArgs.Empty);

            Assert.Same(originalBindings, ReflectionHelpers.GetPrivateField<KeyBindings>(stage, "_workingDrumBindings"));
            Assert.Same(originalSystemBindings, ReflectionHelpers.GetPrivateField<Dictionary<Keys, InputCommandType>>(
                stage, "_workingSystemBindings"));
            Assert.True(ReflectionHelpers.GetPrivateField<bool>(stage, "_hasUnsavedChanges"));
        }
    }

    [Fact]
    public void OnPanelSaved_WithUnmatchedSender_ShouldMarkUnsavedChangesButNotUpdate()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: true);
            var originalBindings = ReflectionHelpers.GetPrivateField<KeyBindings>(stage, "_workingDrumBindings");
            var unmatchedSender = new object();

            ReflectionHelpers.InvokePrivateMethod(stage, "OnPanelSaved", unmatchedSender, EventArgs.Empty);

            Assert.Same(originalBindings, ReflectionHelpers.GetPrivateField<KeyBindings>(stage, "_workingDrumBindings"));
            Assert.True(ReflectionHelpers.GetPrivateField<bool>(stage, "_hasUnsavedChanges"));
        }
    }

    [Fact]
    public void OnPanelClosed_WithNullSender_ShouldClearActivePanel()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: true);
            var drumPanel = ReflectionHelpers.GetPrivateField<DrumKeyAssignPanel>(stage, "_drumPanel");
            ReflectionHelpers.SetPrivateField(stage, "_activePanel", drumPanel);

            ReflectionHelpers.InvokePrivateMethod(stage, "OnPanelClosed", null, EventArgs.Empty);

            Assert.Null(ReflectionHelpers.GetPrivateField<IKeyAssignPanel>(stage, "_activePanel"));
        }
    }

    [Fact]
    public void OnPanelClosed_WithSystemPanelSender_ShouldClearActivePanel()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: true);
            var systemPanel = ReflectionHelpers.GetPrivateField<SystemKeyAssignPanel>(stage, "_systemPanel");
            Assert.NotNull(systemPanel);
            ReflectionHelpers.InvokePrivateMethod(stage, "OpenPanel", systemPanel!);
            systemPanel!.Deactivate();

            ReflectionHelpers.InvokePrivateMethod(stage, "OnPanelClosed", systemPanel, EventArgs.Empty);

            Assert.Null(ReflectionHelpers.GetPrivateField<IKeyAssignPanel>(stage, "_activePanel"));
            Assert.False(systemPanel.IsActive);
        }
    }

    [Fact]
    public void OnPanelSaved_WithSystemPanel_ShouldUpdateWorkingSystemBindings()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: true);
            var systemPanel = ReflectionHelpers.GetPrivateField<SystemKeyAssignPanel>(stage, "_systemPanel");
            Assert.NotNull(systemPanel);

            var newWorkingMapping = new Dictionary<Keys, InputCommandType>
            {
                [Keys.F1] = InputCommandType.MoveUp,
                [Keys.F2] = InputCommandType.Activate
            };
            ReflectionHelpers.SetPrivateField(systemPanel!, "_workingMapping", newWorkingMapping);

            ReflectionHelpers.InvokePrivateMethod(stage, "OnPanelSaved", systemPanel!, EventArgs.Empty);

            var workingSystemBindings = ReflectionHelpers.GetPrivateField<Dictionary<Keys, InputCommandType>>(
                stage, "_workingSystemBindings");
            Assert.NotNull(workingSystemBindings);
            Assert.Equal(2, workingSystemBindings!.Count);
            Assert.Equal(InputCommandType.MoveUp, workingSystemBindings[Keys.F1]);
            Assert.Equal(InputCommandType.Activate, workingSystemBindings[Keys.F2]);
            Assert.True(ReflectionHelpers.GetPrivateField<bool>(stage, "_hasUnsavedChanges"));
        }
    }

    [Fact]
    public void OpenPanel_WithNullPanel_ShouldNotCrash()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: false);

            ReflectionHelpers.InvokePrivateMethod(stage, "OpenPanel", (IKeyAssignPanel?)null);

            Assert.Null(ReflectionHelpers.GetPrivateField<IKeyAssignPanel>(stage, "_activePanel"));
        }
    }

    [Fact]
    public void OnSaveButtonClicked_WhenSaveFails_ShouldNotTransition()
    {
        var configManager = new RecordingConfigManager(
            new ConfigData(),
            saveException: new IOException("disk full"));
        var (stage, _, inputManager) = CreateStage(configManager);

        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: false);
            var stageManager = new Moq.Mock<IStageManager>();
            stage.StageManager = stageManager.Object;
            ReflectionHelpers.SetPrivateField(stage, "_hasUnsavedChanges", true);

            ReflectionHelpers.InvokePrivateMethod(stage, "OnSaveButtonClicked", null, EventArgs.Empty);

            stageManager.Verify(
                manager => manager.ChangeStage(Moq.It.IsAny<StageType>(), Moq.It.IsAny<IStageTransition>()),
                Moq.Times.Never);
        }
    }

    [Fact]
    public void OnBackButtonClicked_WithoutUnsavedChanges_ShouldTransition()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: false);
            var stageManager = new Moq.Mock<IStageManager>();
            stage.StageManager = stageManager.Object;
            ReflectionHelpers.SetPrivateField(stage, "_hasUnsavedChanges", false);

            ReflectionHelpers.InvokePrivateMethod(stage, "OnBackButtonClicked", null, EventArgs.Empty);

            stageManager.Verify(
                manager => manager.ChangeStage(
                    StageType.Title,
                    Moq.It.Is<IStageTransition>(t => t is CrossfadeTransition)),
                Moq.Times.Once);
        }
    }

    [Theory]
    [InlineData(2, nameof(ConfigData.VSyncWait))]
    [InlineData(3, nameof(ConfigData.NoFail))]
    [InlineData(4, nameof(ConfigData.AutoPlay))]
    public void ActivatePressedOnToggle_ShouldToggleWorkingFlag(int selectedIndex, string propertyName)
    {
        var (stage, configManager, inputManager) = CreateStage();
        using (inputManager)
        {
            var property = typeof(ConfigData).GetProperty(propertyName);
            Assert.NotNull(property);
            property!.SetValue(configManager.Config, false);
            InitializeStageMenu(stage, includePanels: false);
            ReflectionHelpers.SetPrivateField(stage, "_selectedIndex", selectedIndex);
            SetKeyboardStates(stage, new KeyboardState(Keys.Enter), new KeyboardState());

            ReflectionHelpers.InvokePrivateMethod(stage, "HandleInput");

            var workingConfig = ReflectionHelpers.GetPrivateField<ConfigData>(stage, "_workingConfig");
            Assert.NotNull(workingConfig);
            Assert.True((bool)property.GetValue(workingConfig!)!);
            Assert.True(ReflectionHelpers.GetPrivateField<bool>(stage, "_hasUnsavedChanges"));
        }
    }
}
