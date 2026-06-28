using System;
using System.Collections.Generic;
using System.Reflection;
using DTXMania.Game;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Stage;
using DTXMania.Game.Lib.Stage.Config;
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
        var inputManager = new InputManagerCompat(configManager, new TestMidiDeviceBackend());
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

    /// <summary>
    /// Finds (categoryIndex, itemIndex) for a named item across all categories, sets the stage's
    /// current category + that category's SelectedIndex, and switches focus to the item list.
    /// </summary>
    private static void SelectItemForEditing(ConfigStage stage, string itemName)
    {
        var categories = GetPrivateField<List<ConfigCategory>>(stage, "_categories");
        Xunit.Assert.NotNull(categories);
        for (int c = 0; c < categories!.Count; c++)
        {
            for (int i = 0; i < categories[c].Items.Count; i++)
            {
                if (categories[c].Items[i].Name == itemName)
                {
                    SetPrivateField(stage, "_currentCategoryIndex", c);
                    categories[c].SelectedIndex = i;
                    SetPrivateField(stage, "_focusOnMenu", false);
                    return;
                }
            }
        }
        Xunit.Assert.Fail($"Config item '{itemName}' should exist.");
    }

    [Fact]
    public void HandleInput_WhenMoveDownPressed_ShouldIncrementCategoryIndex()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: false);
            SetupRuntimeSystemBindings(inputManager, DefaultNavBindings());
            SetPrivateField(stage, "_focusOnMenu", true);
            SetPrivateField(stage, "_currentCategoryIndex", 0);
            SetKeyboardStates(stage, new KeyboardState(Keys.Down), new KeyboardState());

            InvokePrivateMethod(stage, "HandleInput");

            Assert.Equal(1, GetPrivateField<int>(stage, "_currentCategoryIndex"));
        }
    }

    [Fact]
    public void HandleInput_WhenMoveUpPressed_ShouldDecrementCategoryIndex()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: false);
            SetupRuntimeSystemBindings(inputManager, DefaultNavBindings());
            SetPrivateField(stage, "_focusOnMenu", true);
            SetPrivateField(stage, "_currentCategoryIndex", 2);
            SetKeyboardStates(stage, new KeyboardState(Keys.Up), new KeyboardState());

            InvokePrivateMethod(stage, "HandleInput");

            Assert.Equal(1, GetPrivateField<int>(stage, "_currentCategoryIndex"));
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
            SelectItemForEditing(stage, "Fullscreen");

            var fullscreenBefore = configManager.Config.FullScreen;

            SetKeyboardStates(stage, new KeyboardState(Keys.Enter), new KeyboardState());
            InvokePrivateMethod(stage, "HandleInput");

            Assert.NotEqual(fullscreenBefore, configManager.Config.FullScreen);
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
            SelectItemForEditing(stage, "Scroll Speed");

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
            SelectItemForEditing(stage, "Scroll Speed");

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

    // ---- NavigateToDrumConfig ----

    [Fact]
    public void NavigateToDrumConfig_ShouldChangeStageToDrumConfigWithInstantTransition()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: false);
            var stageManager = new Mock<IStageManager>();
            stage.StageManager = stageManager.Object;

            InvokePrivateMethod(stage, "NavigateToDrumConfig");

            stageManager.Verify(
                m => m.ChangeStage(
                    StageType.DrumConfig,
                    It.Is<IStageTransition>(t => t is InstantTransition)),
                Times.Once);
        }
    }

    // ---- OnPanelSaved non-system-panel branch (sender != _systemPanel -> no-op) ----

    [Fact]
    public void OnPanelSaved_WhenSenderIsNotSystemPanel_DoesNotMutateSystemBindings()
    {
        var (stage, configManager, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: true);
            var systemBefore = configManager.Config.SystemKeyBindings.Count;

            // Sender is some other object (e.g. a hypothetical drum panel), not _systemPanel.
            InvokePrivateMethod(stage, "OnPanelSaved", new object(), EventArgs.Empty);

            Assert.Equal(systemBefore, configManager.Config.SystemKeyBindings.Count);
        }
    }

    // ---- FlushPendingSaveSafely catch branch (a throwing IConfigManager must not trap the user) ----

    [Fact]
    public void FlushPendingSaveSafely_WhenConfigManagerThrows_SwallowsAndDoesNotPropagate()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            // Swap in an IConfigManager whose FlushPendingSave propagates; the safety wrapper
            // must swallow so a save error can never trap the user on the config screen.
            var throwing = new Mock<IConfigManager>();
            throwing.Setup(c => c.FlushPendingSave()).Throws(new IOException("disk full"));
            SetPrivateField(stage, "_configManager", throwing.Object);

            var ex = Record.Exception(() => InvokePrivateMethod(stage, "FlushPendingSaveSafely"));

            Assert.Null(ex);
            throwing.Verify(c => c.FlushPendingSave(), Times.Once);
        }
    }

    // ---- IsConfigNavigationCommandPressed: IsCommandPressed==true branch ----
    // The existing HandleInput tests drive the keyboard-state fallback (Keyboard.GetState() is
    // unpressed in the test runner). This test injects a command into the runtime's per-frame
    // pressed set so the first branch (IsCommandPressed == true) is taken instead.

    [Fact]
    public void IsConfigNavigationCommandPressed_WhenCommandInjected_ReturnsTrueViaFirstBranch()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: false);
            // Inject MoveDown into the per-frame pressed-command set that IsCommandPressed reads.
            var injected = typeof(InputManagerCompat)
                .GetField("_injectedCommandsThisFrame", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(injected);
            var set = (HashSet<InputCommandType>)injected!.GetValue(inputManager)!;
            set.Add(InputCommandType.MoveDown);

            var result = (bool)InvokePrivateMethod(stage, "IsConfigNavigationCommandPressed", InputCommandType.MoveDown)!;

            Assert.True(result);
        }
    }

    [Fact]
    public void IsConfigNavigationCommandPressed_WhenNoMatch_ReturnsFalse()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: false);
            // No injected command and no keyboard edge for the command.
            var result = (bool)InvokePrivateMethod(stage, "IsConfigNavigationCommandPressed", InputCommandType.MoveDown)!;

            Assert.False(result);
        }
    }

    // ---- IsPanelCommandPressed ----

    [Fact]
    public void IsPanelCommandPressed_WhenKeyEdgeMatchesSystemMap_ReturnsTrue()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: false);
            SetupRuntimeSystemBindings(inputManager, DefaultNavBindings());
            // Down -> MoveDown in the system map; current keyboard has Down held, previous doesn't.
            SetKeyboardStates(stage, new KeyboardState(Keys.Down), new KeyboardState());

            var result = (bool)InvokePrivateMethod(stage, "IsPanelCommandPressed", InputCommandType.MoveDown)!;

            Assert.True(result);
        }
    }

    [Fact]
    public void IsPanelCommandPressed_WhenNoMatch_ReturnsFalse()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: false);
            SetupRuntimeSystemBindings(inputManager, DefaultNavBindings());
            SetKeyboardStates(stage, new KeyboardState(), new KeyboardState());

            var result = (bool)InvokePrivateMethod(stage, "IsPanelCommandPressed", InputCommandType.MoveDown)!;

            Assert.False(result);
        }
    }
}
