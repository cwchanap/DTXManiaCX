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

namespace DTXMania.Test.Config;

[Trait("Category", "Unit")]
public class ConfigStageLogicTests
{
    [Fact]
    public void Constructor_WithoutConfigManager_ShouldThrowInvalidOperationException()
    {
        var game = ReflectionHelpers.CreateGame();

        var exception = Assert.Throws<InvalidOperationException>(() => new ConfigStage(game));

        Assert.Equal("ConfigManager not found", exception.Message);
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
            Assert.Equal(11, configItems!.Count);
            Assert.Collection(configItems,
                item => Assert.Equal("Screen Resolution", item.Name),
                item => Assert.Equal("Fullscreen", item.Name),
                item => Assert.Equal("VSync Wait", item.Name),
                item => Assert.Equal("No Fail", item.Name),
                item => Assert.Equal("Auto Play", item.Name),
                item => Assert.Equal("Scroll Speed", item.Name),
                item => Assert.Equal("Audio Latency Offset", item.Name),
                item => Assert.Equal("DTX Folder", item.Name),
                item => Assert.Equal("Drum Key Mapping", item.Name),
                item => Assert.Equal("System Key Mapping", item.Name),
                item => Assert.Equal("Import NX Scores", item.Name));
            Assert.Equal(0, ReflectionHelpers.GetPrivateField<int>(stage, "_selectedIndex"));
        }
    }

    [Fact]
    public void SetupConfigItems_ShouldShowConfiguredDtxFolder()
    {
        var (stage, configManager, inputManager) = CreateStage();
        using (inputManager)
        {
            configManager.Config.DTXPath = "/tmp/custom dtx";
            InitializeStageMenu(stage, includePanels: false);

            var configItems = ReflectionHelpers.GetPrivateField<List<IConfigItem>>(stage, "_configItems");
            var item = Assert.Single(configItems!, item => item.Name == "DTX Folder");

            Assert.Equal("DTX Folder: /tmp/custom dtx", item.GetDisplayText());
        }
    }

    [Fact]
    public void DtxFolderItem_WhenActivated_ShouldNotMutateConfig()
    {
        var (stage, configManager, inputManager) = CreateStage();
        using (inputManager)
        {
            configManager.Config.DTXPath = "/tmp/custom dtx";
            InitializeStageMenu(stage, includePanels: false);
            var dtxFolderIndex = GetConfigItemIndex(stage, "DTX Folder");
            ReflectionHelpers.SetPrivateField(stage, "_selectedIndex", dtxFolderIndex);
            SetKeyboardStates(stage, new KeyboardState(Keys.Enter), new KeyboardState());

            ReflectionHelpers.InvokePrivateMethod(stage, "HandleInput");

            // DTX Folder is read-only; Config must be unchanged.
            Assert.Equal("/tmp/custom dtx", configManager.Config.DTXPath);
        }
    }

    [Fact]
    public void MoveUpPressedAtFirstItem_ShouldWrapToExitButton()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: false);
            ReflectionHelpers.SetPrivateField(stage, "_selectedIndex", 0);
            SetKeyboardStates(stage, new KeyboardState(Keys.Up), new KeyboardState());

            ReflectionHelpers.InvokePrivateMethod(stage, "HandleInput");

            var configItems = ReflectionHelpers.GetPrivateField<List<IConfigItem>>(stage, "_configItems");
            Assert.Equal(configItems!.Count, ReflectionHelpers.GetPrivateField<int>(stage, "_selectedIndex"));
        }
    }

    [Fact]
    public void MoveDownPressedAtExitButton_ShouldWrapToFirstItem()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: false);
            var configItems = ReflectionHelpers.GetPrivateField<List<IConfigItem>>(stage, "_configItems");
            ReflectionHelpers.SetPrivateField(stage, "_selectedIndex", configItems!.Count);
            SetKeyboardStates(stage, new KeyboardState(Keys.Down), new KeyboardState());

            ReflectionHelpers.InvokePrivateMethod(stage, "HandleInput");

            Assert.Equal(0, ReflectionHelpers.GetPrivateField<int>(stage, "_selectedIndex"));
        }
    }

    [Fact]
    public void MoveRightPressedOnResolution_ShouldMutateConfigViaSetter()
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

            Assert.Equal(1920, configManager.Config.ScreenWidth);
            Assert.Equal(1080, configManager.Config.ScreenHeight);
        }
    }

    [Fact]
    public void MoveLeftPressedOnResolution_ShouldMutateConfigViaSetter()
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

            Assert.Equal(1280, configManager.Config.ScreenWidth);
            Assert.Equal(720, configManager.Config.ScreenHeight);
        }
    }

    [Fact]
    public void ActivatePressedOnToggleItem_ShouldMutateConfigViaSetter()
    {
        var (stage, configManager, inputManager) = CreateStage();
        using (inputManager)
        {
            configManager.Config.FullScreen = false;
            InitializeStageMenu(stage, includePanels: false);
            ReflectionHelpers.SetPrivateField(stage, "_selectedIndex", 1);
            SetKeyboardStates(stage, new KeyboardState(Keys.Enter), new KeyboardState());

            ReflectionHelpers.InvokePrivateMethod(stage, "HandleInput");

            Assert.True(configManager.Config.FullScreen);
        }
    }

    [Fact]
    public void ActivatePressedOnDrumKeyMapping_ShouldChangeToDrumConfigStage()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: true);
            var stageManager = new Moq.Mock<IStageManager>();
            stage.StageManager = stageManager.Object;

            ReflectionHelpers.SetPrivateField(stage, "_selectedIndex", GetConfigItemIndex(stage, "Drum Key Mapping"));
            SetKeyboardStates(stage, new KeyboardState(Keys.Enter), new KeyboardState());

            ReflectionHelpers.InvokePrivateMethod(stage, "HandleInput");

            // Drum Key Mapping navigates to DrumConfigStage. Config is the single source of truth
            // now, so the navigation carries no pending system-key handoff.
            stageManager.Verify(
                manager => manager.ChangeStage(
                    StageType.DrumConfig,
                    Moq.It.Is<IStageTransition>(transition => transition is InstantTransition)),
                Moq.Times.Once);

            var activePanel = ReflectionHelpers.GetPrivateField<IKeyAssignPanel>(stage, "_activePanel");
            Assert.Null(activePanel);
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
    public void BackCommandPressed_ShouldFlushAndReturnToTitleStage()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: false);
            var stageManager = new Moq.Mock<IStageManager>();
            stage.StageManager = stageManager.Object;
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
    public void BackCommandPressed_ShouldReturnToTitleStage()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: false);
            var stageManager = new Moq.Mock<IStageManager>();
            stage.StageManager = stageManager.Object;
            ReflectionHelpers.SetPrivateField(stage, "_selectedIndex", 7);
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
    public void ActivatePressedOnExitButton_ShouldReturnToTitleStage()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: false);
            var stageManager = new Moq.Mock<IStageManager>();
            stage.StageManager = stageManager.Object;
            var configItems = ReflectionHelpers.GetPrivateField<List<IConfigItem>>(stage, "_configItems");
            ReflectionHelpers.SetPrivateField(stage, "_selectedIndex", configItems!.Count);
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
    public void ExitButton_ShouldCallFlushPendingSaveAndTransitionToTitle()
    {
        // Spy on FlushPendingSave via a Mock<IConfigManager> to PROVE the exit path flushes,
        // independent of internal ConfigManager state (the call must happen regardless).
        using var inputManager = new InputManagerCompat(new ConfigManager());
        var (stage, mockConfig) = CreateStageWithMockConfig(inputManager);
        InitializeStageMenu(stage, includePanels: false);
        var stageManager = new Moq.Mock<IStageManager>();
        stage.StageManager = stageManager.Object;
        var configItems = ReflectionHelpers.GetPrivateField<List<IConfigItem>>(stage, "_configItems");
        ReflectionHelpers.SetPrivateField(stage, "_selectedIndex", configItems!.Count); // Exit button
        SetKeyboardStates(stage, new KeyboardState(Keys.Enter), new KeyboardState());

        ReflectionHelpers.InvokePrivateMethod(stage, "HandleInput");

        // EXIT must flush the pending save (persist-on-edit) and leave for Title.
        mockConfig.Verify(c => c.FlushPendingSave(), Moq.Times.Once);
        stageManager.Verify(
            manager => manager.ChangeStage(
                StageType.Title,
                Moq.It.Is<IStageTransition>(transition => transition is CrossfadeTransition)),
            Moq.Times.Once);
    }

    [Fact]
    public void ExitButton_WhenFlushThrows_ShouldStillTransitionToTitle()
    {
        // Persist-on-edit contract: a flush failure must not trap the user on the config screen.
        // The stage logs the failure, keeps the dirty flag for retry, and still transitions.
        using var inputManager = new InputManagerCompat(new ConfigManager());
        var (stage, mockConfig) = CreateStageWithMockConfig(inputManager);
        mockConfig.Setup(c => c.FlushPendingSave()).Throws(new IOException("disk full"));
        InitializeStageMenu(stage, includePanels: false);
        var stageManager = new Moq.Mock<IStageManager>();
        stage.StageManager = stageManager.Object;
        var configItems = ReflectionHelpers.GetPrivateField<List<IConfigItem>>(stage, "_configItems");
        ReflectionHelpers.SetPrivateField(stage, "_selectedIndex", configItems!.Count); // Exit button
        SetKeyboardStates(stage, new KeyboardState(Keys.Enter), new KeyboardState());

        var exception = Record.Exception(() => ReflectionHelpers.InvokePrivateMethod(stage, "HandleInput"));

        Assert.Null(exception);
        mockConfig.Verify(c => c.FlushPendingSave(), Moq.Times.Once);
        stageManager.Verify(
            manager => manager.ChangeStage(StageType.Title, Moq.It.IsAny<IStageTransition>()),
            Moq.Times.Once);
    }

    [Fact]
    public void PanelIsActiveOnDeactivate_ShouldDeactivatePanelAndClearKeyboardState()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: true);

            var systemPanel = ReflectionHelpers.GetPrivateField<SystemKeyAssignPanel>(stage, "_systemPanel");
            Assert.NotNull(systemPanel);
            ReflectionHelpers.InvokePrivateMethod(stage, "OpenPanel", systemPanel!);
            SetKeyboardStates(stage, new KeyboardState(Keys.Enter), new KeyboardState(Keys.Down));

            ReflectionHelpers.InvokePrivateMethod(stage, "OnDeactivate");

            Assert.False(systemPanel!.IsActive);
            Assert.Null(ReflectionHelpers.GetPrivateField<IKeyAssignPanel>(stage, "_activePanel"));
            Assert.False(ReflectionHelpers.GetPrivateField<KeyboardState>(stage, "_currentKeyboardState").IsKeyDown(Keys.Enter));
            Assert.False(ReflectionHelpers.GetPrivateField<KeyboardState>(stage, "_previousKeyboardState").IsKeyDown(Keys.Down));
        }
    }

    [Fact]
    public void OnUpdate_WithActivePanel_ShouldForwardKeyboardStatesToPanelAndSkipMenuHandling()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: false);
            var activePanel = new TrackingKeyAssignPanel { IsActive = true };
            ReflectionHelpers.SetPrivateField(stage, "_activePanel", activePanel);
            ReflectionHelpers.SetPrivateField(stage, "_selectedIndex", 4);

            ReflectionHelpers.InvokePrivateMethod(stage, "OnUpdate", 0.25);

            Assert.Equal(1, activePanel.UpdateCallCount);
            Assert.Equal(0.25, activePanel.LastDeltaTime, 3);
            Assert.Equal(4, ReflectionHelpers.GetPrivateField<int>(stage, "_selectedIndex"));
        }
    }

    [Fact]
    public void OnUpdate_WithoutActivePanel_ShouldHandleMenuInput()
    {
        var configManager = new ConfigManager();
        using var inputManager = new ForcedCommandInputManager(configManager, InputCommandType.MoveDown);
        var game = ReflectionHelpers.CreateGame();
        ReflectionHelpers.SetProperty(game, nameof(BaseGame.ConfigManager), configManager);
        ReflectionHelpers.SetProperty(game, nameof(BaseGame.InputManager), inputManager);
        var stage = new ConfigStage(game);

        InitializeStageMenu(stage, includePanels: false);
        ReflectionHelpers.SetPrivateField(stage, "_activePanel", (IKeyAssignPanel?)null);
        ReflectionHelpers.SetPrivateField(stage, "_selectedIndex", 0);

        ReflectionHelpers.InvokePrivateMethod(stage, "OnUpdate", 0.25);

        Assert.Equal(1, ReflectionHelpers.GetPrivateField<int>(stage, "_selectedIndex"));
    }

    [Fact]
    public void OnActivate_ShouldInitializeConfigItemsAndPanels()
    {
        var configManager = new ConfigManager();
        var (stage, inputManager) = CreateLifecycleStage(configManager);
        using (inputManager)
        {
            configManager.Config.ScreenWidth = 1920;
            configManager.Config.ScreenHeight = 1080;
            configManager.Config.FullScreen = true;
            configManager.Config.VSyncWait = false;

            ReflectionHelpers.InvokePrivateMethod(stage, "OnActivate");

            var configItems = ReflectionHelpers.GetPrivateField<List<IConfigItem>>(stage, "_configItems");

            // Config is the single source of truth; getters read it directly.
            Assert.NotNull(ReflectionHelpers.GetPrivateField<SpriteBatch>(stage, "_spriteBatch"));
            Assert.NotNull(ReflectionHelpers.GetPrivateField<Texture2D>(stage, "_whitePixel"));
            Assert.NotNull(configItems);
            Assert.Equal(11, configItems!.Count);
            Assert.NotNull(ReflectionHelpers.GetPrivateField<SystemKeyAssignPanel>(stage, "_systemPanel"));

            // The resolution item reflects Config (truth).
            Assert.Equal("Screen Resolution: 1920x1080", configItems[0].GetDisplayText());
        }
    }

    [Fact]
    public void OnDraw_WhenSpriteBatchMissing_ShouldReturnWithoutThrowing()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            ReflectionHelpers.SetPrivateField(stage, "_spriteBatch", null);

            var exception = Record.Exception(() => ReflectionHelpers.InvokePrivateMethod(stage, "OnDraw", 0.0));

            Assert.Null(exception);
        }
    }

    [Fact]
    public void OnDraw_WithActivePanel_ShouldDrawOverlayBeforeCompletingFrame()
    {
        var (stage, inputManager) = CreateRenderSpyStageWithGraphicsDevice();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: false);
            stage.InitializeDrawingState();
            var activePanel = new TrackingKeyAssignPanel { IsActive = true };
            ReflectionHelpers.SetPrivateField(stage, "_activePanel", activePanel);

            _ = Record.Exception(() => ReflectionHelpers.InvokePrivateMethod(stage, "OnDraw", 0.0));

            Assert.Equal(1, activePanel.DrawCallCount);
        }
    }

    [Fact]
    public void DrawBackground_ShouldFillViewportWithBackgroundColor()
    {
        var viewport = new Viewport(0, 0, 1280, 720);
        var (stage, inputManager) = CreateRenderSpyStage(viewport);
        using (inputManager)
        {
            stage.InitializeDrawingState();

            _ = Record.Exception(() => ReflectionHelpers.InvokePrivateMethod(stage, "DrawBackground"));

            var drawCall = Assert.Single(stage.RectangleDrawCalls);
            Assert.Equal(new Rectangle(0, 0, viewport.Width, viewport.Height), drawCall.Rectangle);
            // Must match ConfigStage.FallbackBackgroundColor: a light fill keeps DarkText legible
            // when the background texture is unavailable (dark-on-dark was the old failure mode).
            Assert.Equal(new Color(220, 222, 230), drawCall.Color);
        }
    }

    [Fact]
    public void DrawTitle_WhenFontMissing_ShouldFallbackToRectangleDrawing()
    {
        var (stage, inputManager) = CreateRenderSpyStage(new Viewport(0, 0, 1280, 720));
        using (inputManager)
        {
            stage.InitializeDrawingState();
            ReflectionHelpers.SetPrivateField(stage, "_font", null);
            ReflectionHelpers.SetPrivateField(stage, "_boldFont", null);

            _ = Record.Exception(() => ReflectionHelpers.InvokePrivateMethod(stage, "DrawTitle"));

            var drawCall = Assert.Single(stage.RectangleDrawCalls);
            Assert.Equal(new Rectangle(100, 50, "CONFIGURATION".Length * 12, 20), drawCall.Rectangle);
            Assert.Equal(new Color(26, 30, 46), drawCall.Color);
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
    public void HandleInput_MoveLeftOnNavigationItem_ShouldNotMutateConfig()
    {
        var (stage, configManager, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: false);
            // Drum Key Mapping is a NavigationConfigItem; Left should not change config values.
            var drumIndex = GetConfigItemIndex(stage, "Drum Key Mapping");
            ReflectionHelpers.SetPrivateField(stage, "_selectedIndex", drumIndex);
            var autoPlayBefore = configManager.Config.AutoPlay;
            SetKeyboardStates(stage, new KeyboardState(Keys.Left), new KeyboardState());

            ReflectionHelpers.InvokePrivateMethod(stage, "HandleInput");

            Assert.Equal(autoPlayBefore, configManager.Config.AutoPlay);
        }
    }

    [Fact]
    public void HandleInput_MoveRightOnNavigationItem_ShouldNotMutateConfig()
    {
        var (stage, configManager, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: false);
            // System Key Mapping is a NavigationConfigItem; Right should not change config values.
            ReflectionHelpers.SetPrivateField(stage, "_selectedIndex", GetConfigItemIndex(stage, "System Key Mapping"));
            var autoPlayBefore = configManager.Config.AutoPlay;
            SetKeyboardStates(stage, new KeyboardState(Keys.Right), new KeyboardState());

            ReflectionHelpers.InvokePrivateMethod(stage, "HandleInput");

            Assert.Equal(autoPlayBefore, configManager.Config.AutoPlay);
        }
    }

    [Fact]
    public void HandleInput_ActivateOnExitButton_ShouldInvokeExitButtonClicked()
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
    public void IsConfigNavigationCommandPressed_RuntimeBindingPressed_ShouldReturnTrue()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: false);
            // SetSystemKeyBindings live-applies to the runtime map which the command reader queries.
            SetupRuntimeSystemBindings(inputManager, new Dictionary<Keys, InputCommandType>
            {
                [Keys.W] = InputCommandType.MoveUp,
                [Keys.Escape] = InputCommandType.Back
            });
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
    public void IsPanelCommandPressed_RuntimeBindingPressedByKeyboard_ShouldReturnTrue()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: false);
            SetupRuntimeSystemBindings(inputManager, new Dictionary<Keys, InputCommandType>
            {
                [Keys.W] = InputCommandType.MoveUp
            });
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
            SetupRuntimeSystemBindings(inputManager, new Dictionary<Keys, InputCommandType>
            {
                [Keys.W] = InputCommandType.MoveDown
            });
            SetKeyboardStates(stage, new KeyboardState(), new KeyboardState());

            var result = (bool)ReflectionHelpers.InvokePrivateMethod(
                stage, "IsPanelCommandPressed", InputCommandType.MoveUp)!;

            Assert.False(result);
        }
    }

    [Fact]
    public void OnPanelSaved_WithNullSender_ShouldNotMutateSystemBindings()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: true);
            var before = inputManager.GetKeyMappingSnapshot().Count;

            ReflectionHelpers.InvokePrivateMethod(stage, "OnPanelSaved", null, EventArgs.Empty);

            // Null sender is not the system panel; SetSystemKeyBindings must not be called.
            Assert.Equal(before, inputManager.GetKeyMappingSnapshot().Count);
        }
    }

    [Fact]
    public void OnPanelSaved_WithUnmatchedSender_ShouldNotMutateSystemBindings()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: true);
            var before = inputManager.GetKeyMappingSnapshot().Count;

            ReflectionHelpers.InvokePrivateMethod(stage, "OnPanelSaved", new object(), EventArgs.Empty);

            Assert.Equal(before, inputManager.GetKeyMappingSnapshot().Count);
        }
    }

    [Fact]
    public void OnPanelClosed_WithNullSender_ShouldClearActivePanel()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: true);
            var systemPanel = ReflectionHelpers.GetPrivateField<SystemKeyAssignPanel>(stage, "_systemPanel");
            ReflectionHelpers.SetPrivateField(stage, "_activePanel", systemPanel);

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
    public void SystemPanelBackCommand_ShouldClosePanelWithoutMutatingSystemBindings()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: true);
            var systemPanel = ReflectionHelpers.GetPrivateField<SystemKeyAssignPanel>(stage, "_systemPanel");
            Assert.NotNull(systemPanel);
            var systemKeyMappingIndex = GetConfigItemIndex(stage, "System Key Mapping");
            ReflectionHelpers.SetPrivateField(stage, "_selectedIndex", systemKeyMappingIndex);
            ReflectionHelpers.InvokePrivateMethod(stage, "OpenPanel", systemPanel!);
            var before = inputManager.GetKeyMappingSnapshot().Count;

            systemPanel!.Update(0.016, new KeyboardState(Keys.Escape), new KeyboardState());

            Assert.False(systemPanel.IsActive);
            Assert.Null(ReflectionHelpers.GetPrivateField<IKeyAssignPanel>(stage, "_activePanel"));
            Assert.Equal(systemKeyMappingIndex, ReflectionHelpers.GetPrivateField<int>(stage, "_selectedIndex"));
            // Cancel (Back) does not persist; system bindings unchanged.
            Assert.Equal(before, inputManager.GetKeyMappingSnapshot().Count);
        }
    }

    [Fact]
    public void SystemPanelSaveCommand_ShouldPersistSystemBindingsAndClosePanel()
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
                [Keys.F2] = InputCommandType.MoveDown,
                [Keys.F3] = InputCommandType.MoveLeft,
                [Keys.F4] = InputCommandType.MoveRight,
                [Keys.F5] = InputCommandType.Activate,
                [Keys.F6] = InputCommandType.Back,
                [Keys.F7] = InputCommandType.IncreaseScrollSpeed,
                [Keys.F8] = InputCommandType.DecreaseScrollSpeed
            };

            ReflectionHelpers.InvokePrivateMethod(stage, "OpenPanel", systemPanel!);
            ReflectionHelpers.SetPrivateField(systemPanel!, "_workingMapping", newWorkingMapping);
            ReflectionHelpers.SetPrivateField(systemPanel!, "_selectedIndex", 8);

            systemPanel.Update(0.016, new KeyboardState(Keys.Enter), new KeyboardState());

            // Saved -> OnPanelSaved persisted the snapshot to Config and live-applied it.
            Assert.False(systemPanel.IsActive);
            Assert.Null(ReflectionHelpers.GetPrivateField<IKeyAssignPanel>(stage, "_activePanel"));

            var snapshot = inputManager.GetKeyMappingSnapshot();
            Assert.Equal(newWorkingMapping.Count, snapshot.Count);
            foreach (var (key, command) in newWorkingMapping)
            {
                Assert.True(snapshot.ContainsKey(key));
                Assert.Equal(command, snapshot[key]);
            }
        }
    }

    [Fact]
    public void OnPanelSaved_WithSystemPanel_ShouldPersistSystemBindingsToConfig()
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

            var snapshot = inputManager.GetKeyMappingSnapshot();
            Assert.Equal(InputCommandType.MoveUp, snapshot[Keys.F1]);
            Assert.Equal(InputCommandType.Activate, snapshot[Keys.F2]);
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

    [Theory]
    [InlineData(2, nameof(ConfigData.VSyncWait))]
    [InlineData(3, nameof(ConfigData.NoFail))]
    [InlineData(4, nameof(ConfigData.AutoPlay))]
    public void ActivatePressedOnToggle_ShouldMutateConfigViaSetter(int selectedIndex, string propertyName)
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

            Assert.True((bool)property.GetValue(configManager.Config)!);
        }
    }

    [Fact]
    public void AudioLatencyConfigItem_ShouldIncrementBy10Ms()
    {
        var (stage, configManager, inputManager) = CreateStage();
        using (inputManager)
        {
            configManager.Config.AudioLatencyOffsetMs = 200;
            InitializeStageMenu(stage, includePanels: false);

            // Audio Latency Offset is at index 6 (after Resolution, Fullscreen, VSync, NoFail, AutoPlay, ScrollSpeed)
            ReflectionHelpers.SetPrivateField(stage, "_selectedIndex", 6);
            SetKeyboardStates(stage, new KeyboardState(Keys.Right), new KeyboardState());
            ReflectionHelpers.InvokePrivateMethod(stage, "HandleInput");

            Assert.Equal(210, configManager.Config.AudioLatencyOffsetMs);
        }
    }

    [Fact]
    public void AudioLatencyConfigItem_AtZero_ShouldNotDecrementBelowMin()
    {
        var (stage, configManager, inputManager) = CreateStage();
        using (inputManager)
        {
            configManager.Config.AudioLatencyOffsetMs = 0;
            InitializeStageMenu(stage, includePanels: false);

            ReflectionHelpers.SetPrivateField(stage, "_selectedIndex", 6);
            SetKeyboardStates(stage, new KeyboardState(Keys.Left), new KeyboardState());
            ReflectionHelpers.InvokePrivateMethod(stage, "HandleInput");

            Assert.Equal(0, configManager.Config.AudioLatencyOffsetMs);
        }
    }

    [Fact]
    public void AudioLatencyConfigItem_At500_ShouldNotIncrementAboveMax()
    {
        var (stage, configManager, inputManager) = CreateStage();
        using (inputManager)
        {
            configManager.Config.AudioLatencyOffsetMs = 500;
            InitializeStageMenu(stage, includePanels: false);

            ReflectionHelpers.SetPrivateField(stage, "_selectedIndex", 6);
            SetKeyboardStates(stage, new KeyboardState(Keys.Right), new KeyboardState());
            ReflectionHelpers.InvokePrivateMethod(stage, "HandleInput");

            Assert.Equal(500, configManager.Config.AudioLatencyOffsetMs);
        }
    }

    [Fact]
    public void OnDeactivate_ShouldFlushDirtyConfigToDisk()
    {
        // Persist-on-edit: OnDeactivate must actually WRITE the dirty edit to disk, not just
        // call a no-op flush. A real ConfigManager with LoadConfig establishes the pending-save
        // path; a setter marks it dirty; OnDeactivate flushes. The file on disk must then reflect
        // the edit (verified by reloading), proving a real write — not a null-path no-op.
        var tempDir = Path.Combine(
            AppContext.BaseDirectory, "TestResults", "config-flush-deactivate",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var configPath = Path.Combine(tempDir, "Config.ini");
        try
        {
            var configManager = new ConfigManager();
            configManager.LoadConfig(configPath); // writes default file + records the save path

            // Baseline: the loaded file on disk has the default AutoPlay=false.
            Assert.False(ReadAutoPlayFromDisk(configPath));

            var (stage, inputManager) = CreateLifecycleStage(configManager);
            using (inputManager)
            {
                ReflectionHelpers.InvokePrivateMethod(stage, "OnActivate");

                // Dirty edit: live-applied to Config, deferred disk write (file still shows false).
                configManager.SetAutoPlay(true);
                Assert.False(ReadAutoPlayFromDisk(configPath));

                // OnDeactivate must flush the dirty edit to disk.
                ReflectionHelpers.InvokePrivateMethod(stage, "OnDeactivate");
            }

            // The dirty edit is now persisted on disk.
            Assert.True(ReadAutoPlayFromDisk(configPath));
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    private static bool ReadAutoPlayFromDisk(string configPath)
    {
        var reloaded = new ConfigManager();
        reloaded.LoadConfig(configPath);
        return reloaded.Config.AutoPlay;
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

    private static (RenderSpyConfigStage Stage, InputManagerCompat InputManager) CreateRenderSpyStageWithGraphicsDevice(ConfigManager? configManager = null)
        => CreateRenderSpyStage(new Viewport(0, 0, 1280, 720), configManager);

    private static (RenderSpyConfigStage Stage, InputManagerCompat InputManager) CreateRenderSpyStage(Viewport viewport, ConfigManager? configManager = null)
    {
        configManager ??= new ConfigManager();
        var inputManager = new InputManagerCompat(configManager);
        var game = ReflectionHelpers.CreateGame();
        ReflectionHelpers.SetProperty(game, nameof(BaseGame.ConfigManager), configManager);
        ReflectionHelpers.SetProperty(game, nameof(BaseGame.InputManager), inputManager);
        return (new RenderSpyConfigStage(game, viewport), inputManager);
    }

    private static (ConfigStage Stage, InputManagerCompat InputManager) CreateLifecycleStage(ConfigManager? configManager = null)
    {
        configManager ??= new ConfigManager();
        var inputManager = new InputManagerCompat(configManager);
        var game = ReflectionHelpers.CreateGame();
        ReflectionHelpers.SetProperty(game, nameof(BaseGame.ConfigManager), configManager);
        ReflectionHelpers.SetProperty(game, nameof(BaseGame.InputManager), inputManager);
        return (new LifecycleConfigStage(game), inputManager);
    }

    /// <summary>
    /// Wires a <see cref="Moq.Mock{IConfigManager}"/> spy onto the game's ConfigManager (so
    /// <see cref="ConfigStage"/> flush/exit calls can be verified) while keeping a real
    /// InputManagerCompat for navigation. The two are intentionally independent: ConfigStage's
    /// _configManager is the spy; the InputManagerCompat owns its own ConfigManager for the
    /// runtime input map.
    /// </summary>
    private static (ConfigStage Stage, Moq.Mock<IConfigManager> MockConfig) CreateStageWithMockConfig(InputManagerCompat inputManager)
    {
        var mockConfig = new Moq.Mock<IConfigManager>();
        mockConfig.SetupGet(c => c.Config).Returns(new ConfigData());
        var game = ReflectionHelpers.CreateGame();
        ReflectionHelpers.SetProperty(game, nameof(BaseGame.ConfigManager), mockConfig.Object);
        ReflectionHelpers.SetProperty(game, nameof(BaseGame.InputManager), inputManager);
        return (new ConfigStage(game), mockConfig);
    }

    private static void InitializeStageMenu(ConfigStage stage, bool includePanels)
    {
        // Config is truth; config items read it directly. Only the item list (and optionally the
        // system panel) need initialization — there is no working copy to load.
        ReflectionHelpers.InvokePrivateMethod(stage, "SetupConfigItems");

        if (includePanels)
        {
            ReflectionHelpers.InvokePrivateMethod(stage, "InitializePanels");
        }
    }

    /// <summary>
    /// Writes the given system bindings into Config via the typed setter, which live-applies them
    /// to the runtime InputManagerCompat map (the source that the command readers query). Mirrors
    /// how a real edit propagates in the Phase 2 event architecture.
    /// </summary>
    private static void SetupRuntimeSystemBindings(InputManagerCompat inputManager, Dictionary<Keys, InputCommandType> bindings)
    {
        // InputManagerCompat is wired to its ConfigManager via the Phase 2 events; reach the
        // ConfigManager through the binding's owner via reflection of the private field.
        var cmField = typeof(InputManagerCompat).GetField("_configManager",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var cm = (ConfigManager)cmField!.GetValue(inputManager)!;
        cm.SetSystemKeyBindings(bindings);
    }

    private static int GetConfigItemIndex(ConfigStage stage, string itemName)
    {
        var configItems = ReflectionHelpers.GetPrivateField<List<IConfigItem>>(stage, "_configItems");
        Assert.NotNull(configItems);
        var index = configItems!.FindIndex(item => item.Name == itemName);
        Assert.True(index >= 0, $"Config item '{itemName}' should exist.");
        return index;
    }

    private static void SetKeyboardStates(ConfigStage stage, KeyboardState current, KeyboardState previous)
    {
        ReflectionHelpers.SetPrivateField(stage, "_currentKeyboardState", current);
        ReflectionHelpers.SetPrivateField(stage, "_previousKeyboardState", previous);
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

    private sealed class TrackingKeyAssignPanel : IKeyAssignPanel
    {
        public bool IsActive { get; set; }

        public event EventHandler? Closed;
        public event EventHandler? Saved;

        public int UpdateCallCount { get; private set; }
        public int DrawCallCount { get; private set; }
        public double LastDeltaTime { get; private set; }

        public void Activate() => IsActive = true;

        public void Deactivate() => IsActive = false;

        public void Update(double deltaTime, KeyboardState current, KeyboardState previous)
        {
            UpdateCallCount++;
            LastDeltaTime = deltaTime;
        }

        public void Draw(SpriteBatch spriteBatch, IFont? font, IFont? boldFont, Texture2D? whitePixel, int viewportWidth, int viewportHeight)
        {
            DrawCallCount++;
        }
    }

    private sealed class LifecycleConfigStage : ConfigStage
    {
        public LifecycleConfigStage(BaseGame game)
            : base(game)
        {
        }

        protected override void InitializeGraphics()
        {
            var spriteBatch = ReflectionHelpers.CreateUninitialized<SpriteBatch>();
            var whitePixel = ReflectionHelpers.CreateUninitialized<Texture2D>();
            GC.SuppressFinalize(spriteBatch);
            GC.SuppressFinalize(whitePixel);
            ReflectionHelpers.SetPrivateField(this, "_spriteBatch", spriteBatch);
            ReflectionHelpers.SetPrivateField(this, "_whitePixel", whitePixel);
            ReflectionHelpers.SetPrivateField(this, "_resourceManager", _game.ResourceManager);
            ReflectionHelpers.SetPrivateField(this, "_font", null);
            ReflectionHelpers.SetPrivateField(this, "_boldFont", null);
        }
    }

    private sealed class RenderSpyConfigStage : ConfigStage
    {
        private readonly Viewport _viewport;

        public RenderSpyConfigStage(BaseGame game, Viewport viewport)
            : base(game)
        {
            _viewport = viewport;
        }

        public List<(Rectangle Rectangle, Color Color)> RectangleDrawCalls { get; } = [];

        public void InitializeDrawingState()
        {
            var spriteBatch = ReflectionHelpers.CreateUninitialized<SpriteBatch>();
            var whitePixel = ReflectionHelpers.CreateUninitialized<Texture2D>();
            GC.SuppressFinalize(spriteBatch);
            GC.SuppressFinalize(whitePixel);
            ReflectionHelpers.SetPrivateField(this, "_spriteBatch", spriteBatch);
            ReflectionHelpers.SetPrivateField(this, "_whitePixel", whitePixel);
            ReflectionHelpers.SetPrivateField(this, "_font", null);
            ReflectionHelpers.SetPrivateField(this, "_boldFont", null);
        }

        protected override void BeginDrawFrame()
        {
        }

        protected override void EndDrawFrame()
        {
        }

        protected override void DrawFilledRectangle(Rectangle destinationRectangle, Color color)
        {
            RectangleDrawCalls.Add((destinationRectangle, color));
        }

        protected override Viewport GetViewport()
        {
            return _viewport;
        }
    }
}
