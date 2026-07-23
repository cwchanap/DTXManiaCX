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
using DTXMania.Game.Lib.Stage.Config;
using DTXMania.Game.Lib.Stage.KeyAssign;
using DTXMania.Game.Lib.UI.Layout;
using DTXMania.Game.Lib.Utilities;
using DTXMania.Test.Helpers;
using DTXMania.Test.TestData;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Moq;

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
    public void SetupConfigItems_ShouldShowConfiguredDtxFolder()
    {
        var (stage, configManager, inputManager) = CreateStage();
        using (inputManager)
        {
            configManager.Config.DTXPath = "/tmp/custom dtx";
            InitializeStageMenu(stage, includePanels: false);

            var categories = ReflectionHelpers.GetPrivateField<List<ConfigCategory>>(stage, "_categories");
            var item = categories!.SelectMany(c => c.Items).Single(i => i.Name == "DTX Folder");

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
            SelectItemForEditing(stage, "DTX Folder");
            SetKeyboardStates(stage, new KeyboardState(Keys.Enter), new KeyboardState());

            ReflectionHelpers.InvokePrivateMethod(stage, "HandleInput");

            // DTX Folder is read-only; Config must be unchanged.
            Assert.Equal("/tmp/custom dtx", configManager.Config.DTXPath);
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
            SelectItemForEditing(stage, "Screen Resolution");
            SetKeyboardStates(stage, new KeyboardState(Keys.Right), new KeyboardState());

            ReflectionHelpers.InvokePrivateMethod(stage, "HandleInput");

            Assert.Equal(1920, configManager.Config.ScreenWidth);
            Assert.Equal(1080, configManager.Config.ScreenHeight);
        }
    }

    [Fact]
    public void MoveRightPressedOnPlaySpeed_ShouldMutateOnlyGameplaySpeed()
    {
        var (stage, configManager, inputManager) = CreateStage();
        using (inputManager)
        {
            configManager.Config.PlaySpeedPercent = PlaySpeedRange.Default;
            configManager.Config.ScrollSpeed = ScrollSpeedRange.Default;
            InitializeStageMenu(stage, includePanels: false);
            SelectItemForEditing(stage, "Play Speed");
            SetKeyboardStates(stage, new KeyboardState(Keys.Right), new KeyboardState());

            ReflectionHelpers.InvokePrivateMethod(stage, "HandleInput");

            Assert.Equal(105, configManager.Config.PlaySpeedPercent);
            Assert.Equal(ScrollSpeedRange.Default, configManager.Config.ScrollSpeed);
        }
    }

    [Fact]
    public void MoveRightPressedOnPitch_ShouldMutateOnlyPitch()
    {
        var (stage, configManager, inputManager) = CreateStage();
        using (inputManager)
        {
            configManager.Config.PitchSemitones = PitchRange.Default;
            configManager.Config.PlaySpeedPercent = PlaySpeedRange.Default;
            InitializeStageMenu(stage, includePanels: false);
            SelectItemForEditing(stage, "Pitch");
            SetKeyboardStates(stage, new KeyboardState(Keys.Right), new KeyboardState());

            ReflectionHelpers.InvokePrivateMethod(stage, "HandleInput");

            Assert.Equal(1, configManager.Config.PitchSemitones);
            Assert.Equal(PlaySpeedRange.Default, configManager.Config.PlaySpeedPercent);
        }
    }

    [Fact]
    public void MoveRightPressedOnPlaySpeed_WhenFfmpegUnavailable_ShouldKeepDefault()
    {
        var (stage, configManager, inputManager) = CreateStage(ffmpegAvailable: false);
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: false);
            SelectItemForEditing(stage, "Play Speed");
            SetKeyboardStates(stage, new KeyboardState(Keys.Right), new KeyboardState());

            ReflectionHelpers.InvokePrivateMethod(stage, "HandleInput");

            Assert.Equal(PlaySpeedRange.Default, configManager.Config.PlaySpeedPercent);
        }
    }

    [Fact]
    public void MoveRightPressedOnPitch_WhenFfmpegUnavailable_ShouldKeepDefault()
    {
        var (stage, configManager, inputManager) = CreateStage(ffmpegAvailable: false);
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: false);
            SelectItemForEditing(stage, "Pitch");
            SetKeyboardStates(stage, new KeyboardState(Keys.Right), new KeyboardState());

            ReflectionHelpers.InvokePrivateMethod(stage, "HandleInput");

            Assert.Equal(PitchRange.Default, configManager.Config.PitchSemitones);
        }
    }

    [Fact]
    public void PlaybackModifiers_WhenFfmpegUnavailable_ShouldAllowResetToDefaults()
    {
        var (stage, configManager, inputManager) = CreateStage(ffmpegAvailable: false);
        using (inputManager)
        {
            configManager.Config.PlaySpeedPercent = PlaySpeedRange.Max;
            configManager.Config.PitchSemitones = PitchRange.Min;
            InitializeStageMenu(stage, includePanels: false);

            SelectItemForEditing(stage, "Play Speed");
            SetKeyboardStates(stage, new KeyboardState(Keys.Left), new KeyboardState());
            ReflectionHelpers.InvokePrivateMethod(stage, "HandleInput");

            SelectItemForEditing(stage, "Pitch");
            SetKeyboardStates(stage, new KeyboardState(Keys.Right), new KeyboardState());
            ReflectionHelpers.InvokePrivateMethod(stage, "HandleInput");

            Assert.Equal(PlaySpeedRange.Default, configManager.Config.PlaySpeedPercent);
            Assert.Equal(PitchRange.Default, configManager.Config.PitchSemitones);
        }
    }

    [Fact]
    public void PlaybackModifierDescriptions_WhenUnavailableProfileIsNonDefault_ShouldWarnToReset()
    {
        var (stage, configManager, inputManager) = CreateStage(ffmpegAvailable: false);
        using (inputManager)
        {
            configManager.Config.PlaySpeedPercent = 105;
            InitializeStageMenu(stage, includePanels: false);
            var categories =
                ReflectionHelpers.GetPrivateField<List<ConfigCategory>>(stage, "_categories")!;
            var drums = categories.Single(category => category.Name == "Drums");

            var playSpeed = drums.Items.Single(item => item.Name == "Play Speed");
            var pitch = drums.Items.Single(item => item.Name == "Pitch");

            Assert.Contains("FFmpeg unavailable", playSpeed.Description);
            Assert.Contains("reset Play Speed to 1.00x and Pitch to 0 st", playSpeed.Description);
            Assert.Contains("FFmpeg unavailable", pitch.Description);
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
            SelectItemForEditing(stage, "Screen Resolution");
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
            SelectItemForEditing(stage, "Fullscreen");
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

            SelectItemForEditing(stage, "Drum Key Mapping");
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
            ReflectionHelpers.SetPrivateField(stage, "_focusOnMenu", true);
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
            ReflectionHelpers.SetPrivateField(stage, "_currentCategoryIndex", 1);

            ReflectionHelpers.InvokePrivateMethod(stage, "OnUpdate", 0.25);

            Assert.Equal(1, activePanel.UpdateCallCount);
            Assert.Equal(0.25, activePanel.LastDeltaTime, 3);
            Assert.Equal(1, ReflectionHelpers.GetPrivateField<int>(stage, "_currentCategoryIndex"));
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
        ReflectionHelpers.SetPrivateField(stage, "_focusOnMenu", true);
        ReflectionHelpers.SetPrivateField(stage, "_currentCategoryIndex", 0);

        ReflectionHelpers.InvokePrivateMethod(stage, "OnUpdate", 0.25);

        Assert.Equal(1, ReflectionHelpers.GetPrivateField<int>(stage, "_currentCategoryIndex"));
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

            var categories = ReflectionHelpers.GetPrivateField<List<ConfigCategory>>(stage, "_categories");

            // Config is the single source of truth; getters read it directly.
            Assert.NotNull(ReflectionHelpers.GetPrivateField<SpriteBatch>(stage, "_spriteBatch"));
            Assert.NotNull(ReflectionHelpers.GetPrivateField<Texture2D>(stage, "_whitePixel"));
            Assert.NotNull(categories);
            Assert.Equal(3, categories!.Count);
            Assert.NotNull(ReflectionHelpers.GetPrivateField<SystemKeyAssignPanel>(stage, "_systemPanel"));

            // The resolution item reflects Config (truth).
            Assert.Equal("Screen Resolution: 1920x1080", categories[0].Items[0].GetDisplayText());
        }
    }

    [Fact]
    public void OnActivate_ShouldClearStaleImportStatus()
    {
        // StageManager reuses this instance, so an import status left by a previous visit must be
        // cleared on re-entry (otherwise a stale "Imported N scores" survives leaving/re-entering).
        var configManager = new ConfigManager();
        var (stage, inputManager) = CreateLifecycleStage(configManager);
        using (inputManager)
        {
            ReflectionHelpers.SetPrivateField(stage, "_importStatus", "Imported 5 scores");

            ReflectionHelpers.InvokePrivateMethod(stage, "OnActivate");

            Assert.Equal("", ReflectionHelpers.GetPrivateField<string>(stage, "_importStatus"));
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
    public void DrawBackground_ShouldFillFixedVirtualRectRegardlessOfViewport()
    {
        // The background is drawn at the fixed 1280x720 virtual rect; BaseGame letterboxes the
        // 1280x720 render target to the window once. Drawing at raw viewport size would stretch it
        // out of aspect, so the fill must always be the (0,0,1280,720) virtual rect.
        var (stage, inputManager) = CreateRenderSpyStageWithGraphicsDevice();
        using (inputManager)
        {
            stage.InitializeDrawingState();

            _ = Record.Exception(() => ReflectionHelpers.InvokePrivateMethod(stage, "DrawConfigBackground"));

            var drawCall = Assert.Single(stage.RectangleDrawCalls);
            Assert.Equal(ConfigUILayout.BackgroundRect, drawCall.Rectangle);
            // Must match ConfigStage.FallbackBackgroundColor: dark fill keeps LightText legible
            // when the background texture is unavailable (light-on-dark is the NX aesthetic).
            Assert.Equal(new Color(18, 20, 34), drawCall.Color);
        }
    }

    [Fact]
    public void DrawInnerBoard_ShouldFillBorderThenBoardRects()
    {
        // The inner board is a dark translucent frame over the background that contains the
        // config content so it stays readable against the busy GALAXY WAVE background.
        var (stage, inputManager) = CreateRenderSpyStageWithGraphicsDevice();
        using (inputManager)
        {
            stage.InitializeDrawingState();

            ReflectionHelpers.InvokePrivateMethod(stage, "DrawInnerBoard");

            Assert.Contains(stage.RectangleDrawCalls,
                c => c.Rectangle == ConfigUILayout.InnerBoardBorderRect && c.Color == new Color(74, 62, 150, 224));
            Assert.Contains(stage.RectangleDrawCalls,
                c => c.Rectangle == ConfigUILayout.InnerBoardRect && c.Color == new Color(8, 10, 22, 196));
        }
    }

    [Fact]
    public void DrawItemList_ForNavigationItem_ShouldUseNormalItemBoxWidth()
    {
        // Navigation items (System Key Mapping / Import NX Scores / Drum Key Mapping) must use the
        // same normal itembox as value items so the list reads uniformly — never the narrower
        // "other" box, which made them look out of place.
        var (stage, inputManager) = CreateRenderSpyStageWithGraphicsDevice();
        using (inputManager)
        {
            stage.InitializeDrawingState();
            ReflectionHelpers.InvokePrivateMethod(stage, "SetupConfigItems");
            ReflectionHelpers.SetPrivateField(stage, "_currentCategoryIndex", 0); // System (index 5 = System Key Mapping)

            ReflectionHelpers.InvokePrivateMethod(stage, "DrawItemList");

            var navRowTopY = ConfigUILayout.RowTopY(5, 0.0);
            var normalRect = ConfigUILayout.ItemBoxRect(navRowTopY, ConfigUILayout.ItemBoxNormalWidth);
            Assert.Contains(stage.RectangleDrawCalls,
                c => c.Rectangle == normalRect && c.Color == new Color(34, 40, 68, 200));
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
            ReflectionHelpers.SetPrivateField(stage, "_focusOnMenu", true);
            ReflectionHelpers.SetPrivateField(stage, "_currentCategoryIndex", 0);
            SetKeyboardStates(stage, new KeyboardState(Keys.Down), new KeyboardState());

            ReflectionHelpers.InvokePrivateMethod(stage, "HandleInput");

            Assert.Equal(1, ReflectionHelpers.GetPrivateField<int>(stage, "_currentCategoryIndex"));
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
            SelectItemForEditing(stage, "Drum Key Mapping");
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
            SelectItemForEditing(stage, "System Key Mapping");
            var autoPlayBefore = configManager.Config.AutoPlay;
            SetKeyboardStates(stage, new KeyboardState(Keys.Right), new KeyboardState());

            ReflectionHelpers.InvokePrivateMethod(stage, "HandleInput");

            Assert.Equal(autoPlayBefore, configManager.Config.AutoPlay);
        }
    }

    [Fact]
    public void HandleInput_MoveLeftOnNavigationItem_ShouldNotChangeStage()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: true);
            var stageManager = new Moq.Mock<IStageManager>();
            stage.StageManager = stageManager.Object;

            // Drum Key Mapping navigates to DrumConfig via an InstantTransition. Left is a
            // value-adjust key and must never trigger that stage change.
            SelectItemForEditing(stage, "Drum Key Mapping");
            SetKeyboardStates(stage, new KeyboardState(Keys.Left), new KeyboardState());

            ReflectionHelpers.InvokePrivateMethod(stage, "HandleInput");

            stageManager.Verify(
                manager => manager.ChangeStage(
                    Moq.It.IsAny<StageType>(),
                    Moq.It.IsAny<IStageTransition>()),
                Moq.Times.Never);
        }
    }

    [Fact]
    public void HandleInput_MoveRightOnNavigationItem_ShouldNotOpenPanel()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: true);

            // System Key Mapping opens the key-assign panel. Right is a value-adjust key and
            // must never pop that overlay open.
            SelectItemForEditing(stage, "System Key Mapping");
            SetKeyboardStates(stage, new KeyboardState(Keys.Right), new KeyboardState());

            ReflectionHelpers.InvokePrivateMethod(stage, "HandleInput");

            var activePanel = ReflectionHelpers.GetPrivateField<IKeyAssignPanel>(stage, "_activePanel");
            Assert.Null(activePanel);
        }
    }

    [Fact]
    public void HandleInput_ActivateOnSystemKeyMapping_ShouldOpenPanel()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: true);

            // Activate is the supported path for navigation items; it must still open the panel.
            SelectItemForEditing(stage, "System Key Mapping");
            SetKeyboardStates(stage, new KeyboardState(Keys.Enter), new KeyboardState());

            ReflectionHelpers.InvokePrivateMethod(stage, "HandleInput");

            var activePanel = ReflectionHelpers.GetPrivateField<IKeyAssignPanel>(stage, "_activePanel");
            Assert.NotNull(activePanel);
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
            SelectItemForEditing(stage, "System Key Mapping");
            ReflectionHelpers.InvokePrivateMethod(stage, "OpenPanel", systemPanel!);
            var before = inputManager.GetKeyMappingSnapshot().Count;

            systemPanel!.Update(0.016, new KeyboardState(Keys.Escape), new KeyboardState());

            Assert.False(systemPanel.IsActive);
            Assert.Null(ReflectionHelpers.GetPrivateField<IKeyAssignPanel>(stage, "_activePanel"));
            // Still in System category with System Key Mapping selected (index 5 in System items).
            Assert.Equal(0, ReflectionHelpers.GetPrivateField<int>(stage, "_currentCategoryIndex"));
            var categories = ReflectionHelpers.GetPrivateField<List<ConfigCategory>>(stage, "_categories");
            Assert.Equal(5, categories![0].SelectedIndex);
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
    [InlineData("VSync Wait", nameof(ConfigData.VSyncWait))]
    [InlineData("No Fail", nameof(ConfigData.NoFail))]
    [InlineData("Auto Play", nameof(ConfigData.AutoPlay))]
    public void ActivatePressedOnToggle_ShouldMutateConfigViaSetter(string itemName, string propertyName)
    {
        var (stage, configManager, inputManager) = CreateStage();
        using (inputManager)
        {
            var property = typeof(ConfigData).GetProperty(propertyName);
            Assert.NotNull(property);
            property!.SetValue(configManager.Config, false);
            InitializeStageMenu(stage, includePanels: false);
            SelectItemForEditing(stage, itemName);
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

            SelectItemForEditing(stage, "Audio Latency Offset");
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

            SelectItemForEditing(stage, "Audio Latency Offset");
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

            SelectItemForEditing(stage, "Audio Latency Offset");
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

                // Graphics fields are uninitialized stand-ins (no real GraphicsDevice in headless
                // tests); null them so OnDeactivate's dispose path is a no-op, mirroring the
                // DrumConfigStage lifecycle test.
                ReflectionHelpers.SetPrivateField(stage, "_spriteBatch", null);
                ReflectionHelpers.SetPrivateField(stage, "_whitePixel", null);

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

    // ---- New NX master-detail behavior tests (Task 5) ----

    [Fact]
    public void SetupConfigItems_ShouldBuildSystemDrumsExitCategories()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: false);
            var categories = ReflectionHelpers.GetPrivateField<List<ConfigCategory>>(stage, "_categories");

            Assert.Collection(categories!,
                c => Assert.Equal("System", c.Name),
                c => Assert.Equal("Drums", c.Name),
                c => Assert.Equal("Exit", c.Name));

            Assert.Collection(categories![0].Items,
                i => Assert.Equal("Screen Resolution", i.Name),
                i => Assert.Equal("Fullscreen", i.Name),
                i => Assert.Equal("VSync Wait", i.Name),
                i => Assert.Equal("Audio Latency Offset", i.Name),
                i => Assert.Equal("DTX Folder", i.Name),
                i => Assert.Equal("System Key Mapping", i.Name),
                i => Assert.Equal("Import NX Scores", i.Name));

            Assert.Collection(categories[1].Items,
                i => Assert.Equal("Scroll Speed", i.Name),
                i => Assert.Equal("Play Speed", i.Name),
                i => Assert.Equal("Pitch", i.Name),
                i => Assert.Equal("Auto Play", i.Name),
                i => Assert.Equal("No Fail", i.Name),
                i => Assert.Equal("Drum Key Mapping", i.Name));

            Assert.False(categories[2].HasItems);
        }
    }

    [Fact]
    public void EveryConfigCategoryAndItem_ShouldHaveNonEmptyDescription()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: false);
            var categories = ReflectionHelpers.GetPrivateField<List<ConfigCategory>>(stage, "_categories");

            foreach (var category in categories!)
            {
                Assert.False(string.IsNullOrWhiteSpace(category.Description));
                foreach (var item in category.Items)
                    Assert.False(string.IsNullOrWhiteSpace(item.Description), $"{item.Name} needs a description");
            }
        }
    }

    [Fact]
    public void MenuActivateOnSettingsCategory_ShouldMoveFocusToItems()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: true);
            ReflectionHelpers.SetPrivateField(stage, "_currentCategoryIndex", 0); // System
            ReflectionHelpers.SetPrivateField(stage, "_focusOnMenu", true);
            SetKeyboardStates(stage, new KeyboardState(Keys.Enter), new KeyboardState());

            ReflectionHelpers.InvokePrivateMethod(stage, "HandleInput");

            Assert.False(ReflectionHelpers.GetPrivateField<bool>(stage, "_focusOnMenu"));
        }
    }

    [Fact]
    public void MenuActivateOnExitCategory_ShouldFlushAndTransitionToTitle()
    {
        using var inputManager = new InputManagerCompat(new ConfigManager(), new TestMidiDeviceBackend());
        var (stage, mockConfig) = CreateStageWithMockConfig(inputManager);
        InitializeStageMenu(stage, includePanels: false);
        var stageManager = new Moq.Mock<IStageManager>();
        stage.StageManager = stageManager.Object;
        ReflectionHelpers.SetPrivateField(stage, "_currentCategoryIndex", 2); // Exit
        ReflectionHelpers.SetPrivateField(stage, "_focusOnMenu", true);
        SetKeyboardStates(stage, new KeyboardState(Keys.Enter), new KeyboardState());

        ReflectionHelpers.InvokePrivateMethod(stage, "HandleInput");

        mockConfig.Verify(c => c.FlushPendingSave(), Moq.Times.Once);
        stageManager.Verify(
            m => m.ChangeStage(StageType.Title, Moq.It.Is<IStageTransition>(t => t is CrossfadeTransition)),
            Moq.Times.Once);
    }

    [Fact]
    public void ItemsBackCommand_ShouldReturnFocusToMenu_WithoutLeavingStage()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: false);
            var stageManager = new Moq.Mock<IStageManager>();
            stage.StageManager = stageManager.Object;
            ReflectionHelpers.SetPrivateField(stage, "_currentCategoryIndex", 0);
            ReflectionHelpers.SetPrivateField(stage, "_focusOnMenu", false);
            SetKeyboardStates(stage, new KeyboardState(Keys.Escape), new KeyboardState());

            ReflectionHelpers.InvokePrivateMethod(stage, "HandleInput");

            Assert.True(ReflectionHelpers.GetPrivateField<bool>(stage, "_focusOnMenu"));
            stageManager.Verify(
                m => m.ChangeStage(Moq.It.IsAny<StageType>(), Moq.It.IsAny<IStageTransition>()),
                Moq.Times.Never);
        }
    }

    [Fact]
    public void MenuMoveDown_ShouldWrapAcrossCategories()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: false);
            ReflectionHelpers.SetPrivateField(stage, "_focusOnMenu", true);
            ReflectionHelpers.SetPrivateField(stage, "_currentCategoryIndex", 2); // Exit (last)
            SetKeyboardStates(stage, new KeyboardState(Keys.Down), new KeyboardState());

            ReflectionHelpers.InvokePrivateMethod(stage, "HandleInput");

            Assert.Equal(0, ReflectionHelpers.GetPrivateField<int>(stage, "_currentCategoryIndex"));
        }
    }

    [Fact]
    public void DrawCategoryMenu_ShouldHighlightCurrentCategory()
    {
        var (stage, inputManager) = CreateRenderSpyStageWithGraphicsDevice();
        using (inputManager)
        {
            stage.InitializeDrawingState();
            ReflectionHelpers.InvokePrivateMethod(stage, "SetupConfigItems");
            ReflectionHelpers.SetPrivateField(stage, "_currentCategoryIndex", 1);

            ReflectionHelpers.InvokePrivateMethod(stage, "DrawCategoryMenu");

            Assert.Contains(stage.RectangleDrawCalls, c => c.Rectangle == ConfigUILayout.MenuCursorRect(1));
        }
    }

    [Fact]
    public void DrawItemList_WhenFocusOnItems_ShouldDrawFixedItemCursor()
    {
        // The cursor is locked to the focus row (items scroll under it), so it always renders at
        // the fixed ItemCursorRect regardless of which item is selected.
        var (stage, inputManager) = CreateRenderSpyStageWithGraphicsDevice();
        using (inputManager)
        {
            stage.InitializeDrawingState();
            ReflectionHelpers.InvokePrivateMethod(stage, "SetupConfigItems");
            ReflectionHelpers.SetPrivateField(stage, "_currentCategoryIndex", 0);
            ReflectionHelpers.SetPrivateField(stage, "_focusOnMenu", false);
            var categories = ReflectionHelpers.GetPrivateField<List<ConfigCategory>>(stage, "_categories");
            categories![0].SelectedIndex = 2;

            ReflectionHelpers.InvokePrivateMethod(stage, "DrawItemList");

            Assert.Contains(stage.RectangleDrawCalls, c => c.Rectangle == ConfigUILayout.ItemCursorRect);
        }
    }

    [Fact]
    public void DrawItemList_ShouldWrapTheListInASingleClipRegion()
    {
        // Rows near the top/bottom of the scroll extend past the board edges (IsRowVisible only
        // requires the box to intersect the header→footer band), so the list must be drawn inside
        // one balanced clip region that confines the overflow to the inner board.
        var (stage, inputManager) = CreateRenderSpyStageWithGraphicsDevice();
        using (inputManager)
        {
            stage.InitializeDrawingState();
            ReflectionHelpers.InvokePrivateMethod(stage, "SetupConfigItems");
            ReflectionHelpers.SetPrivateField(stage, "_currentCategoryIndex", 0);
            ReflectionHelpers.SetPrivateField(stage, "_focusOnMenu", false);

            ReflectionHelpers.InvokePrivateMethod(stage, "DrawItemList");

            Assert.Equal(1, stage.BeginItemClipCount);
            Assert.Equal(1, stage.EndItemClipCount);
        }
    }

    [Fact]
    public void DrawItemList_ShouldClipToTheInnerBoardRectangle()
    {
        // Regression guard for the "zero spill past the board" invariant: the scissor rect
        // applied during the item-list clip must equal the inner board the rows slide under.
        // A future change to the clip rect (or to the board rect it must match) would otherwise
        // pass silently with green tests.
        var (stage, inputManager) = CreateRenderSpyStageWithGraphicsDevice();
        using (inputManager)
        {
            stage.InitializeDrawingState();
            ReflectionHelpers.InvokePrivateMethod(stage, "SetupConfigItems");
            ReflectionHelpers.SetPrivateField(stage, "_currentCategoryIndex", 0);
            ReflectionHelpers.SetPrivateField(stage, "_focusOnMenu", false);

            ReflectionHelpers.InvokePrivateMethod(stage, "DrawItemList");

            Assert.NotNull(stage.LastAppliedScissorRectangle);
            Assert.Equal(ConfigUILayout.InnerBoardRect, stage.LastAppliedScissorRectangle!.Value);
        }
    }

    [Fact]
    public void ItemClipRasterizer_ShouldEnableScissorTest()
    {
        // Regression guard for the scissor-test invariant: the rasterizer used for the clipped
        // item-list batch must have ScissorTestEnable = true, otherwise the clip rect has no
        // effect and rows spill past the board. CullMode.None keeps SpriteBatch quads visible
        // regardless of winding.
        var (stage, inputManager) = CreateRenderSpyStageWithGraphicsDevice();
        using (inputManager)
        {
            var rasterizer = stage.GetItemClipRasterizerForTest();

            Assert.True(rasterizer.ScissorTestEnable);
            Assert.Equal(CullMode.None, rasterizer.CullMode);
        }
    }

    [Fact]
    public void DrawItemList_WhenFocusOnMenu_ShouldNotDrawItemCursor()
    {
        var (stage, inputManager) = CreateRenderSpyStageWithGraphicsDevice();
        using (inputManager)
        {
            stage.InitializeDrawingState();
            ReflectionHelpers.InvokePrivateMethod(stage, "SetupConfigItems");
            ReflectionHelpers.SetPrivateField(stage, "_currentCategoryIndex", 0);
            ReflectionHelpers.SetPrivateField(stage, "_focusOnMenu", true);

            ReflectionHelpers.InvokePrivateMethod(stage, "DrawItemList");

            Assert.DoesNotContain(stage.RectangleDrawCalls,
                c => c.Rectangle == ConfigUILayout.ItemCursorRect);
        }
    }

    // ---- End of new NX master-detail behavior tests ----

    // ---- Coverage gaps flagged in code review ----

    [Fact]
    public void SelectedIndex_ShouldBeRememberedAcrossCategorySwitches()
    {
        // Spec-required: the item selection within each category must persist when the player
        // navigates away and returns. Each ConfigCategory owns its own SelectedIndex which is
        // never reset on focus/category changes.
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: false);
            var categories = ReflectionHelpers.GetPrivateField<List<ConfigCategory>>(stage, "_categories");

            // Enter System items, move selection down twice (index 0 -> 2).
            ReflectionHelpers.SetPrivateField(stage, "_currentCategoryIndex", 0);
            ReflectionHelpers.SetPrivateField(stage, "_focusOnMenu", false);
            categories![0].MoveSelectionDown();
            categories[0].MoveSelectionDown();
            Assert.Equal(2, categories[0].SelectedIndex);

            // Switch focus back to menu, move to Drums category.
            ReflectionHelpers.SetPrivateField(stage, "_focusOnMenu", true);
            ReflectionHelpers.SetPrivateField(stage, "_currentCategoryIndex", 1);

            // Navigate back to System category.
            ReflectionHelpers.SetPrivateField(stage, "_currentCategoryIndex", 0);

            // The System category must still remember index 2.
            Assert.Equal(2, categories[0].SelectedIndex);
        }
    }

    [Fact]
    public void MenuMoveRightOnSettingsCategory_ShouldMoveFocusToItems()
    {
        // HandleMenuInput accepts both Activate and MoveRight to enter the item list.
        // Activate is covered above; this verifies the MoveRight path.
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: true);
            ReflectionHelpers.SetPrivateField(stage, "_currentCategoryIndex", 0); // System
            ReflectionHelpers.SetPrivateField(stage, "_focusOnMenu", true);
            SetKeyboardStates(stage, new KeyboardState(Keys.Right), new KeyboardState());

            ReflectionHelpers.InvokePrivateMethod(stage, "HandleInput");

            Assert.False(ReflectionHelpers.GetPrivateField<bool>(stage, "_focusOnMenu"));
        }
    }

    [Fact]
    public void MenuMoveUp_ShouldWrapAcrossCategories()
    {
        // MoveDown wrap is covered above; this verifies MoveUp wraps from first to last.
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: false);
            ReflectionHelpers.SetPrivateField(stage, "_focusOnMenu", true);
            ReflectionHelpers.SetPrivateField(stage, "_currentCategoryIndex", 0); // System (first)
            SetKeyboardStates(stage, new KeyboardState(Keys.Up), new KeyboardState());

            ReflectionHelpers.InvokePrivateMethod(stage, "HandleInput");

            Assert.Equal(2, ReflectionHelpers.GetPrivateField<int>(stage, "_currentCategoryIndex"));
        }
    }

    [Fact]
    public void DrawItemBar_WhenTextureMissing_ShouldFallbackToPanelFill()
    {
        var (stage, inputManager) = CreateRenderSpyStageWithGraphicsDevice();
        using (inputManager)
        {
            stage.InitializeDrawingState();

            ReflectionHelpers.InvokePrivateMethod(stage, "DrawItemBar");

            Assert.Contains(stage.RectangleDrawCalls,
                c => c.Rectangle == ConfigUILayout.ItemBarRect && c.Color == new Color(28, 32, 54, 220));
        }
    }

    // ---- TryLoadTexture regression: the 1×1 white-fallback must be treated as "missing" ----
    // ResourceManager.LoadTexture never throws for an absent file — it returns a 1×1 white
    // fallback (see ResourceManager.CreateFallbackTexture). The old TryLoadTexture only caught
    // exceptions, so it returned that 1×1 texture, every draw took its texture branch, and a
    // single white texel was stretched across every panel (white-box rendering). The fix rejects
    // Width/Height <= 1 and disposes the throwaway fallback so it isn't leaked, then returns
    // null so the documented fallback fills engage. Disposing (rather than RemoveReference) is
    // required because the 1×1 object is an UNCACHED ManagedTexture and ManagedTexture.
    // RemoveReference deliberately does not auto-dispose at zero refs — so RemoveReference would
    // leak one GPU texture per missing asset on every Config re-entry / skin switch.

    [Fact]
    public void TryLoadTexture_WhenResourceManagerReturns1x1Fallback_ShouldReturnNullAndDisposeFallback()
    {
        var (stage, inputManager) = CreateRenderSpyStageWithGraphicsDevice();
        using (inputManager)
        {
            stage.InitializeDrawingState();

            var fallbackTexture = new Mock<ITexture>();
            fallbackTexture.SetupGet(t => t.Width).Returns(1);
            fallbackTexture.SetupGet(t => t.Height).Returns(1);

            var mockResources = new Mock<IResourceManager>();
            mockResources.Setup(r => r.LoadTexture(It.IsAny<string>())).Returns(fallbackTexture.Object);
            ReflectionHelpers.SetPrivateField(stage, "_resourceManager", mockResources.Object);

            var result = ReflectionHelpers.InvokePrivateMethod<ITexture>(stage, "TryLoadTexture", TexturePath.ConfigBackground);

            Assert.Null(result);
            // The uncached 1×1 fallback must be disposed so the GPU texture isn't
            // leaked (RemoveReference does not auto-dispose ManagedTextures).
            fallbackTexture.Verify(t => t.Dispose(), Times.Once);
        }
    }

    [Fact]
    public void TryLoadTexture_WhenAssetIsValid_ShouldReturnTextureWithoutReleasing()
    {
        var (stage, inputManager) = CreateRenderSpyStageWithGraphicsDevice();
        using (inputManager)
        {
            stage.InitializeDrawingState();

            var realTexture = new Mock<ITexture>();
            realTexture.SetupGet(t => t.Width).Returns(256);
            realTexture.SetupGet(t => t.Height).Returns(128);

            var mockResources = new Mock<IResourceManager>();
            mockResources.Setup(r => r.LoadTexture(It.IsAny<string>())).Returns(realTexture.Object);
            ReflectionHelpers.SetPrivateField(stage, "_resourceManager", mockResources.Object);

            var result = ReflectionHelpers.InvokePrivateMethod<ITexture>(stage, "TryLoadTexture", TexturePath.ConfigBackground);

            Assert.Same(realTexture.Object, result);
            realTexture.Verify(t => t.RemoveReference(), Times.Never);
        }
    }

    [Fact]
    public void TryLoadTexture_WhenLoadTextureThrows_ShouldReturnNull()
    {
        var (stage, inputManager) = CreateRenderSpyStageWithGraphicsDevice();
        using (inputManager)
        {
            stage.InitializeDrawingState();

            var mockResources = new Mock<IResourceManager>();
            mockResources.Setup(r => r.LoadTexture(It.IsAny<string>()))
                .Throws(new InvalidOperationException("boom"));
            ReflectionHelpers.SetPrivateField(stage, "_resourceManager", mockResources.Object);

            var result = ReflectionHelpers.InvokePrivateMethod<ITexture>(stage, "TryLoadTexture", TexturePath.ConfigBackground);

            Assert.Null(result);
        }
    }

    // ---- GetItemValueText: value-column extraction (private static, reached via reflection) ----

    [Fact]
    public void GetItemValueText_ForValueItem_ShouldStripNamePrefix()
    {
        var item = new DropdownConfigItem("Volume", () => "75", new[] { "75", "100" }, _ => { });

        Assert.Equal("75", InvokeGetItemValueText(item));
    }

    [Fact]
    public void GetItemValueText_ForReadOnlyItem_ShouldStripNamePrefix()
    {
        var item = new ReadOnlyConfigItem("DTX Folder", () => "/songs");

        Assert.Equal("/songs", InvokeGetItemValueText(item));
    }

    [Fact]
    public void GetItemValueText_ForNavigationItem_ShouldReturnArrowIndicator()
    {
        var item = new NavigationConfigItem("Drum Mapping", () => { });

        Assert.Equal(">", InvokeGetItemValueText(item));
    }

#if DEBUG
    [Fact]
    public void GetItemValueText_WhenDisplayTextDoesNotStartWithPrefix_ShouldFailDebugAssertion()
    {
        // A future item type whose GetDisplayText omits the "{Name}: " prefix is a bug.
        // GetItemValueText fires a Debug.Assert so the mismatch is caught at development
        // time. The throwing trace listener converts the assertion into an exception so it
        // surfaces as a test failure instead of a modal dialog that hangs the run.
        // InvokeGetItemValueText uses reflection, so the assertion exception is wrapped in a
        // TargetInvocationException — unwrap and assert the inner exception type.
        var item = new Mock<IConfigItem>();
        item.SetupGet(i => i.Name).Returns("X");
        item.Setup(i => i.GetDisplayText()).Returns("no prefix here");

        using (ThrowingTraceListener.Install())
        {
            var wrapped = Assert.Throws<TargetInvocationException>(
                () => InvokeGetItemValueText(item.Object));
            Assert.IsType<DebugAssertFailedException>(wrapped.InnerException);
        }
    }
#else
    [Fact]
    public void GetItemValueText_WhenDisplayTextDoesNotStartWithPrefix_ShouldReturnEmpty()
    {
        // In release builds the Debug.Assert is compiled out, so the defensive empty-string
        // fallback is the only behavior.
        var item = new Mock<IConfigItem>();
        item.SetupGet(i => i.Name).Returns("X");
        item.Setup(i => i.GetDisplayText()).Returns("no prefix here");

        Assert.Equal(string.Empty, InvokeGetItemValueText(item.Object));
    }
#endif

    private static string InvokeGetItemValueText(IConfigItem item)
    {
        var method = typeof(ConfigStage).GetMethod("GetItemValueText",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return (string)method!.Invoke(null, new object[] { item })!;
    }

    [Fact]
    public void DrawItemList_ShouldDrawValuesAtTheValueColumn()
    {
        // Values left-align at the fixed value column (ItemListX + ItemValueOffsetX = 680), which
        // sits on the itembox's white value cell. A fixed-width mock font keeps the value short so
        // no truncation occurs and we can assert the exact left-aligned x.
        var (stage, inputManager) = CreateRenderSpyStageWithGraphicsDevice();
        using (inputManager)
        {
            stage.InitializeDrawingState();
            ReflectionHelpers.InvokePrivateMethod(stage, "SetupConfigItems");
            ReflectionHelpers.SetPrivateField(stage, "_currentCategoryIndex", 0);
            ReflectionHelpers.SetPrivateField(stage, "_focusOnMenu", false);

            var font = new Mock<IFont>();
            font.Setup(f => f.MeasureString(It.IsAny<string>())).Returns(new Vector2(40f, 14f));
            ReflectionHelpers.SetPrivateField(stage, "_font", font.Object);
            ReflectionHelpers.SetPrivateField(stage, "_boldFont", font.Object);

            ReflectionHelpers.InvokePrivateMethod(stage, "DrawItemList");

            var expectedValueX = ConfigUILayout.ItemListX + ConfigUILayout.ItemValueOffsetX; // 680
            font.Verify(f => f.DrawString(It.IsAny<SpriteBatch>(), It.IsAny<string>(),
                It.Is<Vector2>(p => Math.Abs(p.X - expectedValueX) < 0.01f), It.IsAny<Color>()),
                Times.AtLeastOnce,
                "value text should be left-aligned at the value column on the white cell");
        }
    }

    [Fact]
    public void DrawItemList_WhenValueExceedsAvailableWidth_ShouldEllipsizeToFitTheValueColumn()
    {
        // A value wider than the value column's budget (ItemValueMaxWidth) must be ellipsized so it
        // stays on the white value cell and clear of the description panel (x=800). The
        // per-character mock font makes the overflow deterministic: 8px/char.
        var longPath = "/Users/testuser/Library/Application Support/DTXManiaCX/DTXFiles";
        var configManager = new ConfigManager { Config = { DTXPath = longPath } };
        var (stage, inputManager) = CreateRenderSpyStageWithGraphicsDevice(configManager);
        using (inputManager)
        {
            stage.InitializeDrawingState();
            ReflectionHelpers.InvokePrivateMethod(stage, "SetupConfigItems");
            ReflectionHelpers.SetPrivateField(stage, "_currentCategoryIndex", 0);
            ReflectionHelpers.SetPrivateField(stage, "_focusOnMenu", false);

            const float charWidth = 8f;
            var font = new Mock<IFont>();
            font.Setup(f => f.MeasureString(It.IsAny<string>()))
                .Returns<string>(s => new Vector2(s.Length * charWidth, 14f));
            ReflectionHelpers.SetPrivateField(stage, "_font", font.Object);
            ReflectionHelpers.SetPrivateField(stage, "_boldFont", font.Object);

            var draws = new List<(string Text, Vector2 Position)>();
            font.Setup(f => f.DrawString(It.IsAny<SpriteBatch>(), It.IsAny<string>(),
                    It.IsAny<Vector2>(), It.IsAny<Color>()))
                .Callback<SpriteBatch, string, Vector2, Color>((_, text, pos, _) => draws.Add((text, pos)));

            ReflectionHelpers.InvokePrivateMethod(stage, "DrawItemList");

            // The long DTX path value must be ellipsized (ends with "...") and fit the value column.
            var pathDraw = draws.Single(d => d.Text.StartsWith("/Users/", StringComparison.Ordinal));
            Assert.EndsWith("...", pathDraw.Text);
            Assert.True(font.Object.MeasureString(pathDraw.Text).X <= ConfigUILayout.ItemValueMaxWidth + 0.01f,
                $"ellipsized value width must fit ItemValueMaxWidth ({ConfigUILayout.ItemValueMaxWidth})");

            // The value is left-aligned at the value column (680), well clear of the name column (440).
            var expectedValueX = ConfigUILayout.ItemListX + ConfigUILayout.ItemValueOffsetX;
            Assert.True(Math.Abs(pathDraw.Position.X - expectedValueX) < 0.01f,
                $"value drawn at x={pathDraw.Position.X}; expected the value column at x={expectedValueX}");
        }
    }

    [Fact]
    public void DrawHeaderFooter_WhenTexturesMissing_ShouldFallbackToPanelFills()
    {
        var (stage, inputManager) = CreateRenderSpyStageWithGraphicsDevice();
        using (inputManager)
        {
            stage.InitializeDrawingState();

            ReflectionHelpers.InvokePrivateMethod(stage, "DrawHeaderFooter");

            Assert.Contains(stage.RectangleDrawCalls,
                c => c.Rectangle == ConfigUILayout.HeaderRect && c.Color == new Color(28, 32, 54, 220));
            Assert.Contains(stage.RectangleDrawCalls,
                c => c.Rectangle == ConfigUILayout.FooterRect && c.Color == new Color(28, 32, 54, 220));
        }
    }

    [Fact]
    public void DrawItemList_WhenItemBoxTexturesMissing_ShouldFallbackToItemBoxFill()
    {
        var (stage, inputManager) = CreateRenderSpyStageWithGraphicsDevice();
        using (inputManager)
        {
            stage.InitializeDrawingState();
            ReflectionHelpers.InvokePrivateMethod(stage, "SetupConfigItems");
            ReflectionHelpers.SetPrivateField(stage, "_currentCategoryIndex", 0); // System has 7 items

            ReflectionHelpers.InvokePrivateMethod(stage, "DrawItemList");

            // Every item row should get a fallback fill when its box texture is unavailable.
            // Item 0 (Screen Resolution) uses the normal box at the settled focus row.
            var row0Rect = ConfigUILayout.ItemBoxRect(
                ConfigUILayout.RowTopY(0, 0.0), ConfigUILayout.ItemBoxNormalWidth);
            Assert.Contains(stage.RectangleDrawCalls,
                c => c.Rectangle == row0Rect && c.Color == new Color(34, 40, 68, 200));
        }
    }

    [Fact]
    public void DrawCategoryMenu_WhenPanelTextureMissing_ShouldFallbackToPanelFill()
    {
        var (stage, inputManager) = CreateRenderSpyStageWithGraphicsDevice();
        using (inputManager)
        {
            stage.InitializeDrawingState();
            ReflectionHelpers.InvokePrivateMethod(stage, "SetupConfigItems");
            ReflectionHelpers.SetPrivateField(stage, "_currentCategoryIndex", 1);

            ReflectionHelpers.InvokePrivateMethod(stage, "DrawCategoryMenu");

            // Menu panel fallback fill plus the cursor fallback fill.
            Assert.Contains(stage.RectangleDrawCalls,
                c => c.Rectangle == ConfigUILayout.MenuPanelRect && c.Color == new Color(28, 32, 54, 220));
        }
    }

    [Fact]
    public void DrawDescriptionPanel_WhenTextureMissing_ShouldFallbackToPanelFill()
    {
        var (stage, inputManager) = CreateRenderSpyStageWithGraphicsDevice();
        using (inputManager)
        {
            stage.InitializeDrawingState();
            ReflectionHelpers.InvokePrivateMethod(stage, "SetupConfigItems");
            ReflectionHelpers.SetPrivateField(stage, "_currentCategoryIndex", 0);
            // Panel only renders on item focus (matches NX); menu focus draws nothing.
            ReflectionHelpers.SetPrivateField(stage, "_focusOnMenu", false);

            ReflectionHelpers.InvokePrivateMethod(stage, "DrawDescriptionPanel");

            Assert.Contains(stage.RectangleDrawCalls,
                c => c.Rectangle == ConfigUILayout.DescriptionPanelRect && c.Color == new Color(28, 32, 54, 220));
        }
    }

    // ---- Integration flow + defensive-guard tests (code review follow-up) ----

    [Fact]
    public void HandleInput_FullMenuItemsEditBackExitFlow_ShouldTransitionThroughAllFocusStates()
    {
        // Integration-style: drives the complete focus-state machine end-to-end through HandleInput
        // (Menu -> enter Items -> edit a toggle -> Back to Menu -> navigate to Exit -> activate Exit).
        // Each step asserts the state carried over from the previous one, catching interaction
        // bugs that the per-transition unit tests above can miss in isolation.
        var (stage, configManager, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: true);
            var stageManager = new Moq.Mock<IStageManager>();
            stage.StageManager = stageManager.Object;

            var categories = ReflectionHelpers.GetPrivateField<List<ConfigCategory>>(stage, "_categories");
            var fullscreenBefore = configManager.Config.FullScreen;

            // Start: System category, focus on menu.
            ReflectionHelpers.SetPrivateField(stage, "_currentCategoryIndex", 0);
            ReflectionHelpers.SetPrivateField(stage, "_focusOnMenu", true);

            // 1. Menu: Activate -> enter System items (SelectedIndex starts at 0).
            SetKeyboardStates(stage, new KeyboardState(Keys.Enter), new KeyboardState());
            ReflectionHelpers.InvokePrivateMethod(stage, "HandleInput");
            Assert.False(ReflectionHelpers.GetPrivateField<bool>(stage, "_focusOnMenu"));

            // 2. Items: MoveDown -> select Fullscreen (index 1).
            SetKeyboardStates(stage, new KeyboardState(Keys.Down), new KeyboardState());
            ReflectionHelpers.InvokePrivateMethod(stage, "HandleInput");
            Assert.Equal(1, categories![0].SelectedIndex);

            // 3. Items: Activate -> toggle Fullscreen via the typed setter.
            SetKeyboardStates(stage, new KeyboardState(Keys.Enter), new KeyboardState());
            ReflectionHelpers.InvokePrivateMethod(stage, "HandleInput");
            Assert.NotEqual(fullscreenBefore, configManager.Config.FullScreen);

            // 4. Items: Back -> return focus to menu WITHOUT leaving the stage.
            SetKeyboardStates(stage, new KeyboardState(Keys.Escape), new KeyboardState());
            ReflectionHelpers.InvokePrivateMethod(stage, "HandleInput");
            Assert.True(ReflectionHelpers.GetPrivateField<bool>(stage, "_focusOnMenu"));
            stageManager.Verify(m => m.ChangeStage(
                Moq.It.IsAny<StageType>(), Moq.It.IsAny<IStageTransition>()), Moq.Times.Never);

            // 5. Menu: MoveDown -> Drums.
            SetKeyboardStates(stage, new KeyboardState(Keys.Down), new KeyboardState());
            ReflectionHelpers.InvokePrivateMethod(stage, "HandleInput");
            Assert.Equal(1, ReflectionHelpers.GetPrivateField<int>(stage, "_currentCategoryIndex"));

            // 6. Menu: MoveDown -> Exit.
            SetKeyboardStates(stage, new KeyboardState(Keys.Down), new KeyboardState());
            ReflectionHelpers.InvokePrivateMethod(stage, "HandleInput");
            Assert.Equal(2, ReflectionHelpers.GetPrivateField<int>(stage, "_currentCategoryIndex"));

            // 7. Menu: Activate on Exit (no items) -> flush + transition to Title.
            SetKeyboardStates(stage, new KeyboardState(Keys.Enter), new KeyboardState());
            ReflectionHelpers.InvokePrivateMethod(stage, "HandleInput");
            stageManager.Verify(m => m.ChangeStage(
                StageType.Title, Moq.It.Is<IStageTransition>(t => t is CrossfadeTransition)), Moq.Times.Once);
        }
    }

    [Fact]
    public void HandleInput_WithEmptyCategories_ShouldNotThrowInEitherFocusState()
    {
        // Defensive guard: SetupConfigItems always populates _categories, but if that invariant
        // ever breaks the modular-arithmetic (% Count) and index access in the handlers would
        // throw DivideByZero/ArgumentOutOfRange. The HandleInput guard must swallow both.
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: false);
            SetupRuntimeSystemBindings(inputManager, new Dictionary<Keys, InputCommandType>
            {
                [Keys.Down] = InputCommandType.MoveDown,
                [Keys.Enter] = InputCommandType.Activate,
                [Keys.Escape] = InputCommandType.Back
            });
            ReflectionHelpers.SetPrivateField(stage, "_categories", new List<ConfigCategory>());

            // Menu focus: would hit % _categories.Count == 0 (DivideByZero) without the guard.
            ReflectionHelpers.SetPrivateField(stage, "_focusOnMenu", true);
            SetKeyboardStates(stage, new KeyboardState(Keys.Down), new KeyboardState());
            Assert.Null(Record.Exception(() =>
                ReflectionHelpers.InvokePrivateMethod(stage, "HandleInput")));

            // Item focus: would hit _categories[_currentCategoryIndex] (ArgumentOutOfRange).
            ReflectionHelpers.SetPrivateField(stage, "_focusOnMenu", false);
            SetKeyboardStates(stage, new KeyboardState(Keys.Enter), new KeyboardState());
            Assert.Null(Record.Exception(() =>
                ReflectionHelpers.InvokePrivateMethod(stage, "HandleInput")));
        }
    }

    [Fact]
    public void DrawDescriptionPanel_WithEmptyDescription_ShouldStillRenderPanelBackground()
    {
        // The description panel is a fixed UI region; its background must render even when the
        // selected item's description is empty, so the layout never shows a surprise blank gap.
        var (stage, inputManager) = CreateRenderSpyStageWithGraphicsDevice();
        using (inputManager)
        {
            stage.InitializeDrawingState();
            var emptyDescItem = new ReadOnlyConfigItem("X", () => "v") { Description = "" };
            var category = new ConfigCategory("Cat", "category desc",
                new List<IConfigItem> { emptyDescItem });
            category.SelectedIndex = 0;
            ReflectionHelpers.SetPrivateField(stage, "_categories",
                new List<ConfigCategory> { category });
            ReflectionHelpers.SetPrivateField(stage, "_currentCategoryIndex", 0);
            ReflectionHelpers.SetPrivateField(stage, "_focusOnMenu", false); // item desc path (empty)

            ReflectionHelpers.InvokePrivateMethod(stage, "DrawDescriptionPanel");

            Assert.Contains(stage.RectangleDrawCalls,
                c => c.Rectangle == ConfigUILayout.DescriptionPanelRect && c.Color == new Color(28, 32, 54, 220));
        }
    }

    [Fact]
    public void DrawDescriptionPanel_WhenFocusOnMenu_ShouldNotDrawPanel()
    {
        // NX only shows the description panel while focus is on the item list, not while browsing
        // the category menu (CStageConfig.cs:260). On menu focus nothing is drawn — no panel
        // background and no text — so the entry view stays clear and the panel never overlaps the
        // item boxes.
        var (stage, inputManager) = CreateRenderSpyStageWithGraphicsDevice();
        using (inputManager)
        {
            stage.InitializeDrawingState();
            ReflectionHelpers.InvokePrivateMethod(stage, "SetupConfigItems");
            var mockFont = new Moq.Mock<IFont>();
            mockFont.Setup(f => f.MeasureString(Moq.It.IsAny<string>())).Returns(new Vector2(1, 1));
            ReflectionHelpers.SetPrivateField(stage, "_font", mockFont.Object);
            ReflectionHelpers.SetPrivateField(stage, "_boldFont", mockFont.Object);

            ReflectionHelpers.SetPrivateField(stage, "_currentCategoryIndex", 0); // System
            ReflectionHelpers.SetPrivateField(stage, "_focusOnMenu", true);

            ReflectionHelpers.InvokePrivateMethod(stage, "DrawDescriptionPanel");

            Assert.DoesNotContain(stage.RectangleDrawCalls,
                c => c.Rectangle == ConfigUILayout.DescriptionPanelRect);
            mockFont.Verify(
                f => f.DrawString(Moq.It.IsAny<SpriteBatch>(), Moq.It.IsAny<string>(),
                    Moq.It.IsAny<Vector2>(), Moq.It.IsAny<Color>()),
                Moq.Times.Never);
        }
    }

    [Fact]
    public void DrawDescriptionPanel_WhenFocusOnItems_ShouldDrawSelectedItemDescription()
    {
        // Spec requirement: while focus = Items, the description panel shows the focused
        // item's Description (not the category's). Paired with the Menu-focus test above
        // to prove the focus-driven text switch.
        var (stage, inputManager) = CreateRenderSpyStageWithGraphicsDevice();
        using (inputManager)
        {
            stage.InitializeDrawingState();
            ReflectionHelpers.InvokePrivateMethod(stage, "SetupConfigItems");
            var categories = ReflectionHelpers.GetPrivateField<List<ConfigCategory>>(stage, "_categories");
            categories![0].SelectedIndex = 0; // Screen Resolution
            var mockFont = new Moq.Mock<IFont>();
            mockFont.Setup(f => f.MeasureString(Moq.It.IsAny<string>())).Returns(new Vector2(1, 1));
            ReflectionHelpers.SetPrivateField(stage, "_font", mockFont.Object);

            ReflectionHelpers.SetPrivateField(stage, "_currentCategoryIndex", 0); // System
            ReflectionHelpers.SetPrivateField(stage, "_focusOnMenu", false);

            ReflectionHelpers.InvokePrivateMethod(stage, "DrawDescriptionPanel");

            var expected = categories[0].SelectedItem!.Description;
            mockFont.Verify(
                f => f.DrawString(Moq.It.IsAny<SpriteBatch>(), expected,
                    Moq.It.IsAny<Vector2>(), Moq.It.IsAny<Color>()),
                Moq.Times.Once);
        }
    }

    [Fact]
    public void UpdateItemScroll_EmptyCategories_ShouldEarlyReturnAndLeaveScrollUnchanged()
    {
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            // No SetupConfigItems: leave _categories empty so the guard returns immediately.
            ReflectionHelpers.SetPrivateField(stage, "_categories", new List<ConfigCategory>());
            ReflectionHelpers.SetPrivateField(stage, "_itemScroll", 5.0);

            ReflectionHelpers.InvokePrivateMethod(stage, "UpdateItemScroll", 0.016);

            Assert.Equal(5.0, ReflectionHelpers.GetPrivateField<double>(stage, "_itemScroll"));
        }
    }

    [Fact]
    public void UpdateItemScroll_LargeDelta_ShouldSnapToTarget()
    {
        // A wrap/multi-row jump (|delta| > 1.5) snaps instead of scrolling the whole list.
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: false);
            var categories = ReflectionHelpers.GetPrivateField<List<ConfigCategory>>(stage, "_categories")!;
            ReflectionHelpers.SetPrivateField(stage, "_currentCategoryIndex", 0);
            categories[0].SelectedIndex = categories[0].Items.Count - 1; // a far row
            ReflectionHelpers.SetPrivateField(stage, "_itemScroll", 0.0);

            ReflectionHelpers.InvokePrivateMethod(stage, "UpdateItemScroll", 0.016);

            Assert.Equal((double)categories[0].SelectedIndex,
                ReflectionHelpers.GetPrivateField<double>(stage, "_itemScroll"));
        }
    }

    [Fact]
    public void UpdateItemScroll_SmallDelta_SmallDeltaTime_ShouldEasePartially()
    {
        // |delta| <= 1.5 eases: factor = min(1, dt*15). dt=0.04 -> factor=0.6, delta=1.0 ->
        // scroll advances by 0.6 and does NOT yet reach the target.
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: false);
            var categories = ReflectionHelpers.GetPrivateField<List<ConfigCategory>>(stage, "_categories")!;
            ReflectionHelpers.SetPrivateField(stage, "_currentCategoryIndex", 0);
            categories[0].SelectedIndex = 1;
            ReflectionHelpers.SetPrivateField(stage, "_itemScroll", 0.0);

            ReflectionHelpers.InvokePrivateMethod(stage, "UpdateItemScroll", 0.04);

            Assert.Equal(0.6, ReflectionHelpers.GetPrivateField<double>(stage, "_itemScroll"), precision: 6);
        }
    }

    [Fact]
    public void UpdateItemScroll_SmallDelta_LargeDeltaTime_ShouldReachAndSnapToTarget()
    {
        // dt=1.0 -> factor=min(1,15)=1.0, so the full delta is applied in one step, then the
        // sub-0.01 residual snaps to the exact target.
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: false);
            var categories = ReflectionHelpers.GetPrivateField<List<ConfigCategory>>(stage, "_categories")!;
            ReflectionHelpers.SetPrivateField(stage, "_currentCategoryIndex", 0);
            categories[0].SelectedIndex = 1;
            ReflectionHelpers.SetPrivateField(stage, "_itemScroll", 0.0);

            ReflectionHelpers.InvokePrivateMethod(stage, "UpdateItemScroll", 1.0);

            Assert.Equal(1.0, ReflectionHelpers.GetPrivateField<double>(stage, "_itemScroll"));
        }
    }

    [Fact]
    public void UpdateItemScroll_WithinSnapThreshold_ShouldClampToTarget()
    {
        // After the ease step the residual is < 0.01, so the value clamps to the exact target
        // rather than leaving a tiny fractional drift.
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: false);
            var categories = ReflectionHelpers.GetPrivateField<List<ConfigCategory>>(stage, "_categories")!;
            ReflectionHelpers.SetPrivateField(stage, "_currentCategoryIndex", 0);
            categories[0].SelectedIndex = 1;
            ReflectionHelpers.SetPrivateField(stage, "_itemScroll", 0.999);

            ReflectionHelpers.InvokePrivateMethod(stage, "UpdateItemScroll", 0.001);

            Assert.Equal(1.0, ReflectionHelpers.GetPrivateField<double>(stage, "_itemScroll"));
        }
    }

    [Fact]
    public void UpdateItemScroll_EmptyCategory_ShouldTargetZero()
    {
        // A category with no items (e.g. Exit) targets 0 regardless of SelectedIndex.
        var (stage, _, inputManager) = CreateStage();
        using (inputManager)
        {
            InitializeStageMenu(stage, includePanels: false);
            var categories = ReflectionHelpers.GetPrivateField<List<ConfigCategory>>(stage, "_categories")!;
            var emptyCategory = categories.FirstOrDefault(c => !c.HasItems);
            Assert.NotNull(emptyCategory);
            var emptyIndex = categories.IndexOf(emptyCategory!);
            ReflectionHelpers.SetPrivateField(stage, "_currentCategoryIndex", emptyIndex);
            ReflectionHelpers.SetPrivateField(stage, "_itemScroll", 3.0);

            ReflectionHelpers.InvokePrivateMethod(stage, "UpdateItemScroll", 1.0);

            Assert.Equal(0.0, ReflectionHelpers.GetPrivateField<double>(stage, "_itemScroll"));
        }
    }

    // ---- Patch coverage: DrawCategoryMenu text loop, DrawItemList texture/scroll paths ----

    [Fact]
    public void DrawCategoryMenu_WithFonts_ShouldDrawSelectedCategoryLabelInSelectedColor()
    {
        // The text loop (lines after the null-font guard) centers each category label on the
        // menu panel and vertically within the cursor band. The selected category uses
        // SelectedMenuText; unselected uses LightText. This covers the font.MeasureString /
        // font.DrawString path that the null-font RenderSpy tests skip.
        var (stage, inputManager) = CreateRenderSpyStageWithGraphicsDevice();
        using (inputManager)
        {
            stage.InitializeDrawingState();
            ReflectionHelpers.InvokePrivateMethod(stage, "SetupConfigItems");
            ReflectionHelpers.SetPrivateField(stage, "_currentCategoryIndex", 1); // Drums (selected)

            var font = new Mock<IFont>();
            font.Setup(f => f.MeasureString(It.IsAny<string>())).Returns(new Vector2(60f, 16f));
            ReflectionHelpers.SetPrivateField(stage, "_font", font.Object);
            ReflectionHelpers.SetPrivateField(stage, "_boldFont", font.Object);

            ReflectionHelpers.InvokePrivateMethod(stage, "DrawCategoryMenu");

            // The selected category (Drums, index 1) is drawn with SelectedMenuText.
            var cursor = ConfigUILayout.MenuCursorRect(1);
            var expectedPos = new Vector2(
                ConfigUILayout.MenuLabelCenterX - 60f / 2f,
                cursor.Y + (ConfigUILayout.MenuCursorHeight - 16f) / 2f);
            font.Verify(
                f => f.DrawString(It.IsAny<SpriteBatch>(), "Drums",
                    It.Is<Vector2>(p => Math.Abs(p.X - expectedPos.X) < 0.01f && Math.Abs(p.Y - expectedPos.Y) < 0.01f),
                    It.Is<Color>(c => c == new Color(36, 24, 72))),
                Times.Once,
                "selected category label should be centered in the cursor band with SelectedMenuText");
        }
    }

    [Fact]
    public void DrawItemList_WithItemBoxTexture_ShouldDrawTextureInsteadOfFallback()
    {
        // When _itemBoxTexture has a valid Texture, the box pass draws it via spriteBatch.Draw
        // instead of the fallback fill. The RenderSpy's SpriteBatch is uninitialized so
        // spriteBatch.Draw throws; we catch that and verify the fallback fill was NOT called,
        // proving the texture-present branch was taken.
        var (stage, inputManager) = CreateRenderSpyStageWithGraphicsDevice();
        using (inputManager)
        {
            stage.InitializeDrawingState();
            ReflectionHelpers.InvokePrivateMethod(stage, "SetupConfigItems");
            ReflectionHelpers.SetPrivateField(stage, "_currentCategoryIndex", 0);

            var mockTexture = new Mock<ITexture>();
            mockTexture.SetupGet(t => t.Width).Returns(256);
            mockTexture.SetupGet(t => t.Height).Returns(32);
            mockTexture.SetupGet(t => t.Texture).Returns(CreateFakeTexture2D());
            ReflectionHelpers.SetPrivateField(stage, "_itemBoxTexture", mockTexture.Object);

            _ = Record.Exception(() => ReflectionHelpers.InvokePrivateMethod(stage, "DrawItemList"));

            // The fallback fill must NOT be used for row 0 when the texture is present.
            var row0Rect = ConfigUILayout.ItemBoxRect(
                ConfigUILayout.RowTopY(0, 0.0), ConfigUILayout.ItemBoxNormalWidth);
            Assert.DoesNotContain(stage.RectangleDrawCalls,
                c => c.Rectangle == row0Rect && c.Color == new Color(34, 40, 68, 200));
        }
    }

    [Fact]
    public void DrawItemList_WithItemBoxCursorTexture_ShouldDrawTextureInsteadOfFallback()
    {
        // When _itemBoxCursorTexture has a valid Texture and focus is on items, the fixed cursor
        // draws the texture instead of the fallback fill. Same uninitialized-SpriteBatch approach
        // as the box-texture test above.
        var (stage, inputManager) = CreateRenderSpyStageWithGraphicsDevice();
        using (inputManager)
        {
            stage.InitializeDrawingState();
            ReflectionHelpers.InvokePrivateMethod(stage, "SetupConfigItems");
            ReflectionHelpers.SetPrivateField(stage, "_currentCategoryIndex", 0);
            ReflectionHelpers.SetPrivateField(stage, "_focusOnMenu", false);

            var mockTexture = new Mock<ITexture>();
            mockTexture.SetupGet(t => t.Width).Returns(256);
            mockTexture.SetupGet(t => t.Height).Returns(32);
            mockTexture.SetupGet(t => t.Texture).Returns(CreateFakeTexture2D());
            ReflectionHelpers.SetPrivateField(stage, "_itemBoxCursorTexture", mockTexture.Object);

            _ = Record.Exception(() => ReflectionHelpers.InvokePrivateMethod(stage, "DrawItemList"));

            // The cursor fallback fill must NOT appear when the texture is present.
            Assert.DoesNotContain(stage.RectangleDrawCalls,
                c => c.Rectangle == ConfigUILayout.ItemCursorRect && c.Color == new Color(96, 96, 160, 180));
        }
    }

    [Fact]
    public void DrawItemList_WithScrolledList_ShouldSkipOffscreenRows()
    {
        // When _itemScroll is large (e.g. scrolled to the last item), early rows fall outside the
        // visible band and IsRowVisible returns false -> continue. This covers the skip branch in
        // both the box pass and the text pass. Mock fonts are set so the text pass is reached
        // (it early-returns when _font/_boldFont are null).
        var (stage, inputManager) = CreateRenderSpyStageWithGraphicsDevice();
        using (inputManager)
        {
            stage.InitializeDrawingState();
            ReflectionHelpers.InvokePrivateMethod(stage, "SetupConfigItems");
            ReflectionHelpers.SetPrivateField(stage, "_currentCategoryIndex", 0);
            var categories = ReflectionHelpers.GetPrivateField<List<ConfigCategory>>(stage, "_categories")!;
            // Scroll to the last item so row 0 is well above the visible band.
            var lastIndex = categories[0].Items.Count - 1;
            categories[0].SelectedIndex = lastIndex;
            ReflectionHelpers.SetPrivateField(stage, "_itemScroll", (double)lastIndex);

            // Set mock fonts so the text pass is reached (not short-circuited by the null guard).
            var font = new Mock<IFont>();
            font.Setup(f => f.MeasureString(It.IsAny<string>())).Returns(new Vector2(40f, 14f));
            ReflectionHelpers.SetPrivateField(stage, "_font", font.Object);
            ReflectionHelpers.SetPrivateField(stage, "_boldFont", font.Object);

            ReflectionHelpers.InvokePrivateMethod(stage, "DrawItemList");

            // Row 0's box rect (at the scrolled position) must not be drawn — it's offscreen.
            var row0TopY = ConfigUILayout.RowTopY(0, (double)lastIndex);
            var row0Rect = ConfigUILayout.ItemBoxRect(row0TopY, ConfigUILayout.ItemBoxNormalWidth);
            Assert.DoesNotContain(stage.RectangleDrawCalls, c => c.Rectangle == row0Rect);
        }
    }

    [Fact]
    public void DrawDescriptionPanel_WithBoldFont_ShouldDrawSelectedItemTitle()
    {
        // The title (selected item's name) is drawn on the white upper cell of the description
        // panel with DescriptionTitleText via _boldFont. This covers the boldFont.DrawString title
        // path that the null-boldFont tests skip.
        var (stage, inputManager) = CreateRenderSpyStageWithGraphicsDevice();
        using (inputManager)
        {
            stage.InitializeDrawingState();
            ReflectionHelpers.InvokePrivateMethod(stage, "SetupConfigItems");
            var categories = ReflectionHelpers.GetPrivateField<List<ConfigCategory>>(stage, "_categories")!;
            categories![0].SelectedIndex = 0; // Screen Resolution
            ReflectionHelpers.SetPrivateField(stage, "_currentCategoryIndex", 0);
            ReflectionHelpers.SetPrivateField(stage, "_focusOnMenu", false);

            var boldFont = new Mock<IFont>();
            boldFont.Setup(f => f.MeasureString(It.IsAny<string>())).Returns(new Vector2(100f, 16f));
            ReflectionHelpers.SetPrivateField(stage, "_boldFont", boldFont.Object);
            // _font must be non-null too so the body text path doesn't NRE.
            var font = new Mock<IFont>();
            font.Setup(f => f.MeasureString(It.IsAny<string>())).Returns(new Vector2(1f, 1f));
            ReflectionHelpers.SetPrivateField(stage, "_font", font.Object);

            ReflectionHelpers.InvokePrivateMethod(stage, "DrawDescriptionPanel");

            var expectedTitle = categories[0].SelectedItem!.Name;
            boldFont.Verify(
                f => f.DrawString(It.IsAny<SpriteBatch>(), expectedTitle,
                    ConfigUILayout.DescriptionTitlePos,
                    It.Is<Color>(c => c == new Color(24, 24, 32))),
                Times.Once,
                "title should be drawn with DescriptionTitleText via boldFont");
        }
    }

    private static Texture2D CreateFakeTexture2D()
    {
#pragma warning disable SYSLIB0050
        var tex = (Texture2D)FormatterServices.GetUninitializedObject(typeof(Texture2D));
#pragma warning restore SYSLIB0050
        GC.SuppressFinalize(tex);
        return tex;
    }

    private static (ConfigStage Stage, ConfigManager ConfigManager, InputManagerCompat InputManager) CreateStage(
        ConfigManager? configManager = null,
        bool ffmpegAvailable = true)
    {
        configManager ??= new ConfigManager();
        var inputManager = new InputManagerCompat(configManager, new TestMidiDeviceBackend());
        var game = ReflectionHelpers.CreateGame();
        ReflectionHelpers.SetProperty(game, nameof(BaseGame.ConfigManager), configManager);
        ReflectionHelpers.SetProperty(game, nameof(BaseGame.InputManager), inputManager);
        var availability = new FfmpegRuntimeAvailability(
            ffmpegAvailable,
            ffmpegAvailable ? null : "test runtime unavailable",
            BinaryFolder: null);
        return (new ConfigStage(game, () => availability), configManager, inputManager);
    }

    private static (RenderSpyConfigStage Stage, InputManagerCompat InputManager) CreateRenderSpyStageWithGraphicsDevice(ConfigManager? configManager = null)
    {
        configManager ??= new ConfigManager();
        var inputManager = new InputManagerCompat(configManager, new TestMidiDeviceBackend());
        var game = ReflectionHelpers.CreateGame();
        ReflectionHelpers.SetProperty(game, nameof(BaseGame.ConfigManager), configManager);
        ReflectionHelpers.SetProperty(game, nameof(BaseGame.InputManager), inputManager);
        return (new RenderSpyConfigStage(game), inputManager);
    }

    private static (ConfigStage Stage, InputManagerCompat InputManager) CreateLifecycleStage(ConfigManager? configManager = null)
    {
        configManager ??= new ConfigManager();
        var inputManager = new InputManagerCompat(configManager, new TestMidiDeviceBackend());
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

    /// <summary>
    /// Finds (categoryIndex, itemIndex) for a named item across all categories, sets the stage's
    /// current category + that category's SelectedIndex, and switches focus to the item list so a
    /// subsequent HandleInput acts on the item. Mirrors the new master-detail navigation.
    /// </summary>
    private static void SelectItemForEditing(ConfigStage stage, string itemName)
    {
        var categories = ReflectionHelpers.GetPrivateField<List<ConfigCategory>>(stage, "_categories");
        Assert.NotNull(categories);
        for (int c = 0; c < categories!.Count; c++)
        {
            for (int i = 0; i < categories[c].Items.Count; i++)
            {
                if (categories[c].Items[i].Name == itemName)
                {
                    ReflectionHelpers.SetPrivateField(stage, "_currentCategoryIndex", c);
                    categories[c].SelectedIndex = i;
                    ReflectionHelpers.SetPrivateField(stage, "_focusOnMenu", false);
                    return;
                }
            }
        }
        Assert.Fail($"Config item '{itemName}' should exist.");
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
            : base(configManager, new TestMidiDeviceBackend())
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

        public void Draw(SpriteBatch spriteBatch, IFont? font, IFont? boldFont, Texture2D? whitePixel, int virtualWidth, int virtualHeight)
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
        public RenderSpyConfigStage(BaseGame game)
            : base(game)
        {
        }

        public List<(Rectangle Rectangle, Color Color)> RectangleDrawCalls { get; } = [];

        public int BeginItemClipCount { get; private set; }
        public int EndItemClipCount { get; private set; }

        // The scissor rect applied by BeginItemClip, recorded via the ApplyScissorRectangle seam
        // so a test can assert it matches the inner board (the "zero spill past the board"
        // invariant) without a real GraphicsDevice.
        public Rectangle? LastAppliedScissorRectangle { get; private set; }

        // Exposes the pure rasterizer seam so a test can assert ScissorTestEnable is set.
        public RasterizerState GetItemClipRasterizerForTest() => CreateItemClipRasterizer();

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

        // The item-list clip flushes and reopens the real SpriteBatch and touches the
        // GraphicsDevice scissor state; the spy uses an uninitialized SpriteBatch and has no
        // GraphicsDevice, so no-op the SpriteBatch.End/Begin seams but still let production's
        // BeginItemClip/EndItemClip run for real — exercising the ApplyScissorRectangle(
        // GetItemClipRectangle()) wiring that confines rows to the board. Count the flush calls so
        // a test can assert the list is wrapped in a single balanced clip region.
        protected override void FlushCurrentBatch()
        {
            BeginItemClipCount++;
        }

        protected override void BeginClippedBatch(RasterizerState rasterizer)
        {
        }

        protected override void FlushClippedBatch()
        {
            EndItemClipCount++;
        }

        protected override void BeginDefaultBatch()
        {
        }

        // Record the rect instead of touching a (null) GraphicsDevice.
        protected override void ApplyScissorRectangle(Rectangle rect)
        {
            LastAppliedScissorRectangle = rect;
        }

        // No GraphicsDevice to restore in the spy.
        protected override void RestoreScissorRectangle()
        {
        }

        protected override void DrawFilledRectangle(Rectangle destinationRectangle, Color color)
        {
            RectangleDrawCalls.Add((destinationRectangle, color));
        }
    }
}
