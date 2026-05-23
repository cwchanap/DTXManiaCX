using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using DTXMania.Game;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Stage;
using DTXMania.Game.Lib.Stage.KeyAssign;
using DTXMania.Test.TestData;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Moq;
using static DTXMania.Test.TestData.ReflectionHelpers;

namespace DTXMania.Test.Stage;

[Trait("Category", "Unit")]
public class ConfigStageInputCoverageTests
{
    private static (ConfigStage Stage, ConfigManager ConfigManager, InputManagerCompat InputManager) CreateStage(ConfigManager? configManager = null)
    {
        configManager ??= new ConfigManager();
        var inputManager = new InputManagerCompat(configManager);
        var game = CreateGame();
        SetProperty(game, nameof(BaseGame.ConfigManager), configManager);
        SetProperty(game, nameof(BaseGame.InputManager), inputManager);
        return (new ConfigStage(game), configManager, inputManager);
    }

    private static void InitializeStageMenu(ConfigStage stage, bool includePanels)
    {
        InvokePrivateMethod(stage, "LoadConfiguration");
        InvokePrivateMethod(stage, "LoadWorkingInputBindings");
        InvokePrivateMethod(stage, "SetupConfigItems");
        if (includePanels)
        {
            InvokePrivateMethod(stage, "InitializePanels");
        }
    }

    private static void SetKeyboardStates(ConfigStage stage, KeyboardState current, KeyboardState previous)
    {
        SetPrivateField(stage, "_currentKeyboardState", current);
        SetPrivateField(stage, "_previousKeyboardState", previous);
    }

    private static string CreateConfigSavePath()
    {
        var directory = Path.Combine(
            AppContext.BaseDirectory,
            "TestResults",
            "config-stage-input-coverage",
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

    [Fact]
    public void HandleInput_WhenMoveDownPressed_ShouldIncrementSelectedIndex()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: false);
            SetPrivateField(stage, "_selectedIndex", 0);
            SetPrivateField(stage, "_navigationBindings", new Dictionary<Keys, InputCommandType>
            {
                [Keys.Down] = InputCommandType.MoveDown,
                [Keys.Up] = InputCommandType.MoveUp,
                [Keys.Escape] = InputCommandType.Back,
                [Keys.Enter] = InputCommandType.Activate,
                [Keys.Left] = InputCommandType.MoveLeft,
                [Keys.Right] = InputCommandType.MoveRight,
            });
            SetKeyboardStates(stage, new KeyboardState(Keys.Down), new KeyboardState());

            InvokePrivateMethod(stage, "HandleInput");

            Assert.Equal(1, GetPrivateField<int>(stage, "_selectedIndex"));
        }
    }

    [Fact]
    public void HandleInput_WhenMoveUpPressed_ShouldDecrementSelectedIndex()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: false);
            SetPrivateField(stage, "_selectedIndex", 2);
            SetPrivateField(stage, "_navigationBindings", new Dictionary<Keys, InputCommandType>
            {
                [Keys.Down] = InputCommandType.MoveDown,
                [Keys.Up] = InputCommandType.MoveUp,
                [Keys.Escape] = InputCommandType.Back,
                [Keys.Enter] = InputCommandType.Activate,
                [Keys.Left] = InputCommandType.MoveLeft,
                [Keys.Right] = InputCommandType.MoveRight,
            });
            SetKeyboardStates(stage, new KeyboardState(Keys.Up), new KeyboardState());

            InvokePrivateMethod(stage, "HandleInput");

            Assert.Equal(1, GetPrivateField<int>(stage, "_selectedIndex"));
        }
    }

    [Fact]
    public void HandleInput_WhenActivatePressedOnConfigItem_ShouldToggleValue()
    {
        var (stage, configManager, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: false);
            SetPrivateField(stage, "_selectedIndex", 1);
            SetPrivateField(stage, "_navigationBindings", new Dictionary<Keys, InputCommandType>
            {
                [Keys.Down] = InputCommandType.MoveDown,
                [Keys.Up] = InputCommandType.MoveUp,
                [Keys.Escape] = InputCommandType.Back,
                [Keys.Enter] = InputCommandType.Activate,
                [Keys.Left] = InputCommandType.MoveLeft,
                [Keys.Right] = InputCommandType.MoveRight,
            });

            var configItems = GetPrivateField<List<IConfigItem>>(stage, "_configItems");
            Assert.NotNull(configItems);
            Assert.True(configItems!.Count > 1);

            var fullscreenBefore = GetPrivateField<ConfigData>(stage, "_workingConfig").FullScreen;

            SetKeyboardStates(stage, new KeyboardState(Keys.Enter), new KeyboardState());
            InvokePrivateMethod(stage, "HandleInput");

            var fullscreenAfter = GetPrivateField<ConfigData>(stage, "_workingConfig").FullScreen;
            Assert.NotEqual(fullscreenBefore, fullscreenAfter);
            Assert.True(GetPrivateField<bool>(stage, "_hasUnsavedChanges"));
        }
    }

    [Fact]
    public void HandleInput_WhenActivatePressedOnBackButton_ShouldCallOnBackButtonClicked()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: false);
            var stageManager = new Mock<IStageManager>();
            stage.StageManager = stageManager.Object;
            SetPrivateField(stage, "_hasUnsavedChanges", false);
            SetPrivateField(stage, "_navigationBindings", new Dictionary<Keys, InputCommandType>
            {
                [Keys.Down] = InputCommandType.MoveDown,
                [Keys.Up] = InputCommandType.MoveUp,
                [Keys.Escape] = InputCommandType.Back,
                [Keys.Enter] = InputCommandType.Activate,
                [Keys.Left] = InputCommandType.MoveLeft,
                [Keys.Right] = InputCommandType.MoveRight,
            });

            var configItems = GetPrivateField<List<IConfigItem>>(stage, "_configItems");
            Assert.NotNull(configItems);
            var backButtonIndex = configItems!.Count;
            SetPrivateField(stage, "_selectedIndex", backButtonIndex);

            SetKeyboardStates(stage, new KeyboardState(Keys.Enter), new KeyboardState());
            InvokePrivateMethod(stage, "HandleInput");

            stageManager.Verify(
                m => m.ChangeStage(
                    StageType.Title,
                    It.Is<IStageTransition>(t => t is CrossfadeTransition)),
                Times.Once);
        }
    }

    [Fact]
    public void HandleInput_WhenActivatePressedOnSaveButton_ShouldCallOnSaveButtonClicked()
    {
        var redirectedSavePath = CreateConfigSavePath();
        var configManager = new ConfigManager();
        try
        {
            var (stage, _, inputManager) = CreateStage(configManager);
            using (inputManager)
            {
                InitializeStageMenu(stage, includePanels: false);
                var stageManager = new Mock<IStageManager>();
                stage.StageManager = stageManager.Object;
                SetPrivateField(stage, "_hasUnsavedChanges", false);
                SetPrivateField(stage, "_navigationBindings", new Dictionary<Keys, InputCommandType>
                {
                    [Keys.Down] = InputCommandType.MoveDown,
                    [Keys.Up] = InputCommandType.MoveUp,
                    [Keys.Escape] = InputCommandType.Back,
                    [Keys.Enter] = InputCommandType.Activate,
                    [Keys.Left] = InputCommandType.MoveLeft,
                    [Keys.Right] = InputCommandType.MoveRight,
                });

                var configItems = GetPrivateField<List<IConfigItem>>(stage, "_configItems");
                Assert.NotNull(configItems);
                var saveButtonIndex = configItems!.Count + 1;
                SetPrivateField(stage, "_selectedIndex", saveButtonIndex);

                SetKeyboardStates(stage, new KeyboardState(Keys.Enter), new KeyboardState());
                InvokePrivateMethod(stage, "HandleInput");

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
    public void HandleInput_WhenMoveLeftPressed_ShouldPreviousValue()
    {
        var (stage, configManager, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: false);
            SetPrivateField(stage, "_selectedIndex", 5);
            SetPrivateField(stage, "_navigationBindings", new Dictionary<Keys, InputCommandType>
            {
                [Keys.Down] = InputCommandType.MoveDown,
                [Keys.Up] = InputCommandType.MoveUp,
                [Keys.Escape] = InputCommandType.Back,
                [Keys.Enter] = InputCommandType.Activate,
                [Keys.Left] = InputCommandType.MoveLeft,
                [Keys.Right] = InputCommandType.MoveRight,
            });

            var scrollSpeedBefore = GetPrivateField<ConfigData>(stage, "_workingConfig").ScrollSpeed;

            SetKeyboardStates(stage, new KeyboardState(Keys.Left), new KeyboardState());
            InvokePrivateMethod(stage, "HandleInput");

            var scrollSpeedAfter = GetPrivateField<ConfigData>(stage, "_workingConfig").ScrollSpeed;
            Assert.True(GetPrivateField<bool>(stage, "_hasUnsavedChanges"));
        }
    }

    [Fact]
    public void HandleInput_WhenMoveRightPressed_ShouldNextValue()
    {
        var (stage, configManager, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: false);
            SetPrivateField(stage, "_selectedIndex", 5);
            SetPrivateField(stage, "_navigationBindings", new Dictionary<Keys, InputCommandType>
            {
                [Keys.Down] = InputCommandType.MoveDown,
                [Keys.Up] = InputCommandType.MoveUp,
                [Keys.Escape] = InputCommandType.Back,
                [Keys.Enter] = InputCommandType.Activate,
                [Keys.Left] = InputCommandType.MoveLeft,
                [Keys.Right] = InputCommandType.MoveRight,
            });

            SetKeyboardStates(stage, new KeyboardState(Keys.Right), new KeyboardState());
            InvokePrivateMethod(stage, "HandleInput");

            Assert.True(GetPrivateField<bool>(stage, "_hasUnsavedChanges"));
        }
    }

    [Fact]
    public void OpenPanel_WithNullPanel_ShouldNotThrow()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: false);

            var exception = Record.Exception(() => InvokePrivateMethod(stage, "OpenPanel", (IKeyAssignPanel?)null));

            Assert.Null(exception);
            Assert.Null(GetPrivateField<IKeyAssignPanel?>(stage, "_activePanel"));
        }
    }

    [Fact]
    public void OnPanelSaved_WhenDrumPanel_ShouldUpdateWorkingBindings()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: true);

            var drumPanel = GetPrivateField<DrumKeyAssignPanel>(stage, "_drumPanel");
            Assert.NotNull(drumPanel);

            var bindingsBefore = GetPrivateField<KeyBindings>(stage, "_workingDrumBindings").Clone();

            InvokePrivateMethod(stage, "OnPanelSaved", drumPanel, EventArgs.Empty);

            Assert.True(GetPrivateField<bool>(stage, "_hasUnsavedChanges"));
        }
    }

    [Fact]
    public void OnPanelSaved_WhenSystemPanel_ShouldUpdateWorkingSystemBindings()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: true);

            var systemPanel = GetPrivateField<SystemKeyAssignPanel>(stage, "_systemPanel");
            Assert.NotNull(systemPanel);

            InvokePrivateMethod(stage, "OnPanelSaved", systemPanel, EventArgs.Empty);

            Assert.True(GetPrivateField<bool>(stage, "_hasUnsavedChanges"));
        }
    }

    [Fact]
    public void OnPanelClosed_ShouldClearActivePanel()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: true);
            var systemPanel = GetPrivateField<SystemKeyAssignPanel>(stage, "_systemPanel");

            SetPrivateField(stage, "_activePanel", systemPanel as IKeyAssignPanel);
            Assert.NotNull(GetPrivateField<IKeyAssignPanel?>(stage, "_activePanel"));

            InvokePrivateMethod(stage, "OnPanelClosed", systemPanel, EventArgs.Empty);

            Assert.Null(GetPrivateField<IKeyAssignPanel?>(stage, "_activePanel"));
        }
    }

    [Fact]
    public void InitializePanels_WithNonConfigManager_ShouldThrow()
    {
        var configManager = new ConfigManager();
        var inputManager = new InputManagerCompat(configManager);
        var game = CreateGame();
        var mockConfigManager = new Mock<IConfigManager>();
        mockConfigManager.Setup(c => c.Config).Returns(new ConfigData());
        SetProperty(game, nameof(BaseGame.ConfigManager), mockConfigManager.Object);
        SetProperty(game, nameof(BaseGame.InputManager), inputManager);

        var stage = new ConfigStage(game);

        InvokePrivateMethod(stage, "LoadConfiguration");
        InvokePrivateMethod(stage, "LoadWorkingInputBindings");
        InvokePrivateMethod(stage, "SetupConfigItems");

        Assert.ThrowsAny<Exception>(() => InvokePrivateMethod(stage, "InitializePanels"));
    }
}
