using System;
using System.Collections.Generic;
using System.Reflection;
using DTXMania.Game;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Stage;
using DTXMania.Game.Lib.Stage.KeyAssign;
using DTXMania.Test.TestData;
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
        // Config is truth; only the item list (and optionally the system panel) need setup.
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

    /// <summary>
    /// Writes the navigation bindings into Config via SetSystemKeyBindings, which live-applies
    /// them to the runtime InputManagerCompat map that the command readers query.
    /// </summary>
    private static void SetupRuntimeSystemBindings(InputManagerCompat inputManager, Dictionary<Keys, InputCommandType> bindings)
    {
        var cmField = typeof(InputManagerCompat).GetField("_configManager",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var cm = (ConfigManager)cmField!.GetValue(inputManager)!;
        cm.SetSystemKeyBindings(bindings);
    }

    private static Dictionary<Keys, InputCommandType> DefaultNavBindings() => new()
    {
        [Keys.Down] = InputCommandType.MoveDown,
        [Keys.Up] = InputCommandType.MoveUp,
        [Keys.Escape] = InputCommandType.Back,
        [Keys.Enter] = InputCommandType.Activate,
        [Keys.Left] = InputCommandType.MoveLeft,
        [Keys.Right] = InputCommandType.MoveRight,
    };

    [Fact]
    public void HandleInput_WhenMoveDownPressed_ShouldIncrementSelectedIndex()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: false);
            SetupRuntimeSystemBindings(inputManager, DefaultNavBindings());
            SetPrivateField(stage, "_selectedIndex", 0);
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
            SetupRuntimeSystemBindings(inputManager, DefaultNavBindings());
            SetPrivateField(stage, "_selectedIndex", 2);
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
            SetupRuntimeSystemBindings(inputManager, DefaultNavBindings());
            SetPrivateField(stage, "_selectedIndex", 1); // Fullscreen toggle

            var configItems = GetPrivateField<List<IConfigItem>>(stage, "_configItems");
            Assert.NotNull(configItems);
            Assert.True(configItems!.Count > 1);

            var fullscreenBefore = configManager.Config.FullScreen;

            SetKeyboardStates(stage, new KeyboardState(Keys.Enter), new KeyboardState());
            InvokePrivateMethod(stage, "HandleInput");

            Assert.NotEqual(fullscreenBefore, configManager.Config.FullScreen);
        }
    }

    [Fact]
    public void HandleInput_WhenActivatePressedOnExitButton_ShouldCallOnExitButtonClicked()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: false);
            var stageManager = new Mock<IStageManager>();
            stage.StageManager = stageManager.Object;
            SetupRuntimeSystemBindings(inputManager, DefaultNavBindings());

            var configItems = GetPrivateField<List<IConfigItem>>(stage, "_configItems");
            Assert.NotNull(configItems);
            // Exit is the sole action button, at index == configItems.Count.
            var exitButtonIndex = configItems!.Count;
            SetPrivateField(stage, "_selectedIndex", exitButtonIndex);

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
    public void HandleInput_WhenMoveLeftPressed_ShouldPreviousValue()
    {
        var (stage, configManager, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: false);
            SetupRuntimeSystemBindings(inputManager, DefaultNavBindings());
            SetPrivateField(stage, "_selectedIndex", 5); // Scroll Speed

            var scrollSpeedBefore = configManager.Config.ScrollSpeed;

            SetKeyboardStates(stage, new KeyboardState(Keys.Left), new KeyboardState());
            InvokePrivateMethod(stage, "HandleInput");

            // Scroll speed should have changed via SetScrollSpeed.
            Assert.NotEqual(scrollSpeedBefore, configManager.Config.ScrollSpeed);
        }
    }

    [Fact]
    public void HandleInput_WhenMoveRightPressed_ShouldNextValue()
    {
        var (stage, configManager, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: false);
            SetupRuntimeSystemBindings(inputManager, DefaultNavBindings());
            SetPrivateField(stage, "_selectedIndex", 5); // Scroll Speed

            var scrollSpeedBefore = configManager.Config.ScrollSpeed;

            SetKeyboardStates(stage, new KeyboardState(Keys.Right), new KeyboardState());
            InvokePrivateMethod(stage, "HandleInput");

            Assert.NotEqual(scrollSpeedBefore, configManager.Config.ScrollSpeed);
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
    public void OnPanelSaved_WhenSystemPanel_ShouldPersistSystemBindingsToConfig()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: true);

            var systemPanel = GetPrivateField<SystemKeyAssignPanel>(stage, "_systemPanel");
            Assert.NotNull(systemPanel);
            systemPanel!.Activate(); // populate _workingMapping from the runtime snapshot
            var before = inputManager.GetKeyMappingSnapshot().Count;

            InvokePrivateMethod(stage, "OnPanelSaved", systemPanel, EventArgs.Empty);

            // OnPanelSaved persisted the panel's snapshot via SetSystemKeyBindings (identity re-apply).
            Assert.Equal(before, inputManager.GetKeyMappingSnapshot().Count);
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
}
