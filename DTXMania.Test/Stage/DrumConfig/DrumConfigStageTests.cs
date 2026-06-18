using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using DTXMania.Game;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Stage;
using DTXMania.Game.Lib.Stage.DrumConfig;
using DTXMania.Game.Lib.Utilities;
using DTXMania.Test.TestData;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Xunit;
using XnaButtonState = Microsoft.Xna.Framework.Input.ButtonState;

namespace DTXMania.Test.Stage.DrumConfig
{
    [Trait("Category", "Unit")]
    [Collection("AppPaths")]
    public class DrumConfigStageTests
    {
        [Fact]
        public void GetResetButtonRect_ReturnsCorrectPosition()
        {
            // Act - use reflection to test the private static method
            var method = typeof(DrumConfigStage).GetMethod("GetResetButtonRect",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);

            var rect = (Rectangle)method!.Invoke(null, new object[] { 1280, 720 });

            // Assert
            Assert.Equal(1280 - 210, rect.X);
            Assert.Equal(12, rect.Y);
            Assert.Equal(190, rect.Width);
            Assert.Equal(30, rect.Height);
        }

        [Fact]
        public void ActivateFocusedElement_WhenResetFocused_ResetsWorkingBindingsToDefault()
        {
            // Keyboard-reachable Reset (design: zones + Reset in one focus order). Activate while
            // Reset holds focus must restore defaults on the working copy. GraphicsDevice-free, so
            // this exercises the dispatch headlessly without OnActivate.
            var game = ReflectionHelpers.CreateGame();
            ReflectionHelpers.SetProperty(game, nameof(BaseGame.ConfigManager), new StubConfigManager());
            var stage = new DrumConfigStage(game);

            var working = new KeyBindings();
            working.BindButton("Key.Z", 3); // non-default binding that Reset must clear
            ReflectionHelpers.SetPrivateField(stage, "_workingBindings", working);
            ReflectionHelpers.SetPrivateField(stage, "_focusIndex", DrumKitLayout.ResetActionIndex);

            ReflectionHelpers.InvokePrivateMethod(stage, "ActivateFocusedElement");

            // "Z" is not a default key, so resetting the working copy drops it.
            Assert.Equal(-1, working.GetLane("Key.Z"));
        }

        [Fact]
        public void ActivateFocusedElement_WhenZoneFocused_OpensPopupForThatLane()
        {
            var game = ReflectionHelpers.CreateGame();
            ReflectionHelpers.SetProperty(game, nameof(BaseGame.ConfigManager), new StubConfigManager());
            var stage = new DrumConfigStage(game);

            var working = new KeyBindings();
            var popup = new DrumCapturePopup(
                working,
                () => new Dictionary<Keys, InputCommandType>(),
                _ => { });
            ReflectionHelpers.SetPrivateField(stage, "_workingBindings", working);
            ReflectionHelpers.SetPrivateField(stage, "_popup", popup);
            ReflectionHelpers.SetPrivateField(stage, "_focusIndex", 4); // Snare Drum

            ReflectionHelpers.InvokePrivateMethod(stage, "ActivateFocusedElement");

            Assert.True(popup.IsOpen);
            Assert.Equal(4, popup.Lane);
        }

        [Fact]
        public void Save_WithNonConfigManager_ShouldNotApplyLiveBindings()
        {
            var inputConfig = new ConfigManager();
            using var input = new InputManagerCompat(inputConfig);
            var stubConfig = new StubConfigManager();
            var game = ReflectionHelpers.CreateGame();
            ReflectionHelpers.SetProperty(game, nameof(BaseGame.ConfigManager), stubConfig);
            ReflectionHelpers.SetProperty(game, nameof(BaseGame.InputManager), input);

            var stage = new DrumConfigStage(game);
            ReflectionHelpers.SetPrivateField(stage, "_input", input);

            var working = input.ModularInputManager.KeyBindings.Clone();
            working.BindButton("Key.X", 2);
            ReflectionHelpers.SetPrivateField(stage, "_workingBindings", working);
            ReflectionHelpers.SetPrivateField(stage, "_workingSystemBindings", new Dictionary<Keys, InputCommandType>());

            ReflectionHelpers.InvokePrivateMethod(stage, "Save");

            Assert.Equal(-1, input.ModularInputManager.KeyBindings.GetLane("Key.X"));
        }

        [Fact]
        public void Save_WithConfigManager_ShouldPersistAndApplyLiveBindings()
        {
            var configManager = new ConfigManager();
            using var input = new InputManagerCompat(configManager);
            var game = ReflectionHelpers.CreateGame();
            ReflectionHelpers.SetProperty(game, nameof(BaseGame.ConfigManager), configManager);
            ReflectionHelpers.SetProperty(game, nameof(BaseGame.InputManager), input);

            var stage = new DrumConfigStage(game);
            ReflectionHelpers.SetPrivateField(stage, "_input", input);

            var working = input.ModularInputManager.KeyBindings.Clone();
            working.BindButton("Key.X", 2);
            ReflectionHelpers.SetPrivateField(stage, "_workingBindings", working);
            ReflectionHelpers.SetPrivateField(stage, "_workingSystemBindings", new Dictionary<Keys, InputCommandType>());

            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            var previousRoot = Environment.GetEnvironmentVariable("DTXMANIA_APPDATA_ROOT");
            Environment.SetEnvironmentVariable("DTXMANIA_APPDATA_ROOT", tempDir);
            try
            {
                var exited = ReflectionHelpers.InvokePrivateMethod<bool>(stage, "Save");

                // A successful save exits the stage (Back = Save & exit) and clears any prior error.
                Assert.True(exited);
                var configPath = AppPaths.GetConfigFilePath();
                Assert.True(File.Exists(configPath));
                Assert.Equal(2, input.ModularInputManager.KeyBindings.GetLane("Key.X"));
                Assert.Null(ReflectionHelpers.GetPrivateField<string?>(stage, "_saveError"));
            }
            finally
            {
                Environment.SetEnvironmentVariable("DTXMANIA_APPDATA_ROOT", previousRoot);
                Directory.Delete(tempDir, recursive: true);
            }
        }

        [Fact]
        public void Save_WhenSaveConfigThrows_ShouldRollbackConfigAndNotApplyLiveBindings()
        {
            var configManager = new ConfigManager();
            using var input = new InputManagerCompat(configManager);
            var game = ReflectionHelpers.CreateGame();
            ReflectionHelpers.SetProperty(game, nameof(BaseGame.ConfigManager), configManager);
            ReflectionHelpers.SetProperty(game, nameof(BaseGame.InputManager), input);

            var stage = new DrumConfigStage(game);
            ReflectionHelpers.SetPrivateField(stage, "_input", input);

            var working = input.ModularInputManager.KeyBindings.Clone();
            working.BindButton("Key.X", 2);
            ReflectionHelpers.SetPrivateField(stage, "_workingBindings", working);
            ReflectionHelpers.SetPrivateField(stage, "_workingSystemBindings", new Dictionary<Keys, InputCommandType>());

            var tempFile = Path.GetTempFileName();
            var previousRoot = Environment.GetEnvironmentVariable("DTXMANIA_APPDATA_ROOT");
            Environment.SetEnvironmentVariable("DTXMANIA_APPDATA_ROOT", tempFile);
            try
            {
                var exited = ReflectionHelpers.InvokePrivateMethod<bool>(stage, "Save");

                // On failure the stage must NOT exit, the config must be rolled back, and the live
                // bindings must not reflect the unsaved working copy.
                Assert.False(exited);
                Assert.Equal(-1, input.ModularInputManager.KeyBindings.GetLane("Key.X"));
                Assert.False(configManager.Config.KeyBindings.ContainsKey("Key.X"));

                // The stage surfaces the failure so the user knows nothing was persisted, and it
                // keeps the working copy intact so a retry (Back again) can succeed.
                var saveError = ReflectionHelpers.GetPrivateField<string?>(stage, "_saveError");
                Assert.False(string.IsNullOrEmpty(saveError));
                var retainedWorking = ReflectionHelpers.GetPrivateField<KeyBindings>(stage, "_workingBindings");
                Assert.Equal(2, retainedWorking.GetLane("Key.X"));
            }
            finally
            {
                Environment.SetEnvironmentVariable("DTXMANIA_APPDATA_ROOT", previousRoot);
                File.Delete(tempFile);
            }
        }

        // ---- OnUpdate / UpdateSelection / UpdatePopup routing (headless, via graphics stub) ----

        private static BaseGame CreateGameWithViewport(int width, int height)
        {
            var game = ReflectionHelpers.CreateGame();
            var device = ReflectionHelpers.CreateUninitialized<GraphicsDevice>();
            ReflectionHelpers.SetPrivateField(device, "_viewport", new Viewport(0, 0, width, height));
            // The device was never properly constructed; suppress its finalizer so teardown can't crash.
            GC.SuppressFinalize(device);
            ReflectionHelpers.SetPrivateField(game, "_graphicsDeviceService", new StubGraphicsDeviceService(device));
            ReflectionHelpers.SetProperty(game, nameof(BaseGame.ConfigManager), new StubConfigManager());
            return game;
        }

        private static MouseState MouseAt(int x, int y, bool leftDown, bool rightDown = false) =>
            new MouseState(x, y, 0,
                leftDown ? XnaButtonState.Pressed : XnaButtonState.Released,
                XnaButtonState.Released,
                rightDown ? XnaButtonState.Pressed : XnaButtonState.Released,
                XnaButtonState.Released,
                XnaButtonState.Released);

        [Fact]
        public void OnUpdate_WhenPopupClosed_RoutesToUpdateSelection()
        {
            var game = CreateGameWithViewport(1280, 720);
            var stage = new DrumConfigStage(game);
            var popup = new DrumCapturePopup(new KeyBindings(),
                () => new Dictionary<Keys, InputCommandType>(), _ => { });
            ReflectionHelpers.SetPrivateField(stage, "_popup", popup); // closed
            ReflectionHelpers.SetPrivateField(stage, "_previousMouse", default(MouseState));

            var ex = Record.Exception(() => ReflectionHelpers.InvokePrivateMethod(stage, "OnUpdate", 0.0));

            Assert.Null(ex);
            // Routed through UpdateSelection (no crash, no popup opened).
            Assert.False(popup.IsOpen);
        }

        [Fact]
        public void OnUpdate_WhenPopupOpen_RoutesToUpdatePopup()
        {
            var game = CreateGameWithViewport(1280, 720);
            var stage = new DrumConfigStage(game);
            var popup = new DrumCapturePopup(new KeyBindings(),
                () => new Dictionary<Keys, InputCommandType>(), _ => { });
            popup.Open(4);
            ReflectionHelpers.SetPrivateField(stage, "_popup", popup);
            ReflectionHelpers.SetPrivateField(stage, "_selectedLane", 4);
            ReflectionHelpers.SetPrivateField(stage, "_previousMouse", default(MouseState));

            var ex = Record.Exception(() => ReflectionHelpers.InvokePrivateMethod(stage, "OnUpdate", 0.0));

            Assert.Null(ex);
            Assert.True(popup.IsOpen); // no click/back occurred, popup stays open
        }

        [Fact]
        public void UpdateSelection_LeftClickOnZone_OpensPopupForHoveredLane()
        {
            var game = CreateGameWithViewport(1280, 720);
            var stage = new DrumConfigStage(game);
            var working = new KeyBindings();
            var popup = new DrumCapturePopup(working,
                () => new Dictionary<Keys, InputCommandType>(), _ => { });
            ReflectionHelpers.SetPrivateField(stage, "_popup", popup);
            ReflectionHelpers.SetPrivateField(stage, "_workingBindings", working);
            // Snare zone center is (380, 430) in the 1280x720 design space; viewport == design space.
            ReflectionHelpers.SetPrivateField(stage, "_previousMouse", MouseAt(0, 0, false));

            ReflectionHelpers.InvokePrivateMethod(stage, "UpdateSelection", MouseAt(380, 430, true), true);

            Assert.True(popup.IsOpen);
            Assert.Equal(4, popup.Lane);
            Assert.Equal(4, ReflectionHelpers.GetPrivateField<int>(stage, "_focusIndex"));
        }

        [Fact]
        public void UpdateSelection_LeftClickOnResetButton_RestoresDefaultBindings()
        {
            var game = CreateGameWithViewport(1280, 720);
            var stage = new DrumConfigStage(game);
            var working = new KeyBindings();
            working.BindButton("Key.Z", 3); // non-default binding Reset must clear
            var popup = new DrumCapturePopup(working,
                () => new Dictionary<Keys, InputCommandType>(), _ => { });
            ReflectionHelpers.SetPrivateField(stage, "_popup", popup);
            ReflectionHelpers.SetPrivateField(stage, "_workingBindings", working);
            ReflectionHelpers.SetPrivateField(stage, "_previousMouse", MouseAt(0, 0, false));

            // Reset button rect = (1070, 12, 190, 30); click its center.
            ReflectionHelpers.InvokePrivateMethod(stage, "UpdateSelection", MouseAt(1165, 27, true), true);

            Assert.Equal(DrumKitLayout.ResetActionIndex, ReflectionHelpers.GetPrivateField<int>(stage, "_focusIndex"));
            Assert.Equal(-1, working.GetLane("Key.Z"));
            Assert.False(popup.IsOpen); // reset does not open a popup
        }

        [Fact]
        public void UpdateSelection_MoveRightCommand_AdvancesFocusAndShowsKeyboardFocus()
        {
            var game = CreateGameWithViewport(1280, 720);
            var stage = new DrumConfigStage(game);
            using var input = new FakeInput(new ConfigManager()) { ActiveCommand = InputCommandType.MoveRight };
            ReflectionHelpers.SetPrivateField(stage, "_input", input);
            ReflectionHelpers.SetPrivateField(stage, "_focusIndex", 0);
            ReflectionHelpers.SetPrivateField(stage, "_previousMouse", MouseAt(5, 5, false));

            ReflectionHelpers.InvokePrivateMethod(stage, "UpdateSelection", MouseAt(5, 5, false), false);

            Assert.Equal(1, ReflectionHelpers.GetPrivateField<int>(stage, "_focusIndex"));
            Assert.True(ReflectionHelpers.GetPrivateField<bool>(stage, "_keyboardFocusActive"));
        }

        [Fact]
        public void UpdateSelection_TabKey_AdvancesFocus()
        {
            var game = CreateGameWithViewport(1280, 720);
            var stage = new DrumConfigStage(game);
            using var input = new FakeInput(new ConfigManager()) { PressedKey = (int)Keys.Tab };
            ReflectionHelpers.SetPrivateField(stage, "_input", input);
            ReflectionHelpers.SetPrivateField(stage, "_focusIndex", 0);
            ReflectionHelpers.SetPrivateField(stage, "_previousMouse", MouseAt(5, 5, false));

            ReflectionHelpers.InvokePrivateMethod(stage, "UpdateSelection", MouseAt(5, 5, false), false);

            Assert.Equal(1, ReflectionHelpers.GetPrivateField<int>(stage, "_focusIndex"));
        }

        [Fact]
        public void UpdateSelection_BackAction_CommitsAndExits()
        {
            var game = CreateGameWithViewport(1280, 720);
            var stage = new DrumConfigStage(game);
            // Non-concrete config + live input: Save exits without touching disk or live bindings.
            using var input = new FakeInput(new ConfigManager()) { BackTriggered = true };
            ReflectionHelpers.SetPrivateField(stage, "_input", input);
            ReflectionHelpers.SetPrivateField(stage, "_workingSystemBindings", new Dictionary<Keys, InputCommandType>());
            ReflectionHelpers.SetPrivateField(stage, "_previousMouse", MouseAt(5, 5, false));

            var ex = Record.Exception(() =>
                ReflectionHelpers.InvokePrivateMethod(stage, "UpdateSelection", MouseAt(5, 5, false), false));

            Assert.Null(ex); // CommitAndExit -> Save -> ChangeStage (StageManager null -> no-op) returns cleanly
        }

        [Fact]
        public void UpdateSelection_ActivateCommandOnZone_OpensPopup()
        {
            var game = CreateGameWithViewport(1280, 720);
            var stage = new DrumConfigStage(game);
            using var input = new FakeInput(new ConfigManager()) { ActiveCommand = InputCommandType.Activate };
            var popup = new DrumCapturePopup(new KeyBindings(),
                () => new Dictionary<Keys, InputCommandType>(), _ => { });
            ReflectionHelpers.SetPrivateField(stage, "_input", input);
            ReflectionHelpers.SetPrivateField(stage, "_popup", popup);
            ReflectionHelpers.SetPrivateField(stage, "_focusIndex", 4); // Snare zone focused
            ReflectionHelpers.SetPrivateField(stage, "_previousMouse", MouseAt(5, 5, false));

            ReflectionHelpers.InvokePrivateMethod(stage, "UpdateSelection", MouseAt(5, 5, false), false);

            Assert.True(popup.IsOpen);
            Assert.Equal(4, popup.Lane);
        }

        [Fact]
        public void UpdateSelection_MouseMovement_HidesKeyboardFocus()
        {
            var game = CreateGameWithViewport(1280, 720);
            var stage = new DrumConfigStage(game);
            ReflectionHelpers.SetPrivateField(stage, "_keyboardFocusActive", true);
            ReflectionHelpers.SetPrivateField(stage, "_previousMouse", MouseAt(0, 0, false)); // differs from current

            ReflectionHelpers.InvokePrivateMethod(stage, "UpdateSelection", MouseAt(400, 400, false), false);

            Assert.False(ReflectionHelpers.GetPrivateField<bool>(stage, "_keyboardFocusActive"));
        }

        [Fact]
        public void UpdatePopup_RightClick_ClosesPopup()
        {
            var game = CreateGameWithViewport(1280, 720);
            var stage = new DrumConfigStage(game);
            var working = new KeyBindings();
            var popup = new DrumCapturePopup(working,
                () => new Dictionary<Keys, InputCommandType>(), _ => { });
            popup.Open(4);
            ReflectionHelpers.SetPrivateField(stage, "_popup", popup);
            ReflectionHelpers.SetPrivateField(stage, "_selectedLane", 4);

            ReflectionHelpers.InvokePrivateMethod(stage, "UpdatePopup", 0.0, MouseAt(10, 10, false, rightDown: true), false, true);

            Assert.False(popup.IsOpen);
            Assert.Equal(-1, ReflectionHelpers.GetPrivateField<int>(stage, "_selectedLane"));
        }

        [Fact]
        public void UpdatePopup_LeftClickOnDoneRect_ClosesPopup()
        {
            var game = CreateGameWithViewport(1280, 720);
            var stage = new DrumConfigStage(game);
            var popup = new DrumCapturePopup(new KeyBindings(),
                () => new Dictionary<Keys, InputCommandType>(), _ => { });
            popup.Open(4);
            ReflectionHelpers.SetPrivateField(stage, "_popup", popup);
            ReflectionHelpers.SetPrivateField(stage, "_selectedLane", 4);

            var doneCenter = popup.GetDoneRect(1280, 720).Center;
            ReflectionHelpers.InvokePrivateMethod(stage, "UpdatePopup",
                0.0, MouseAt(doneCenter.X, doneCenter.Y, true), true, false);

            Assert.False(popup.IsOpen);
            Assert.Equal(-1, ReflectionHelpers.GetPrivateField<int>(stage, "_selectedLane"));
        }

        [Fact]
        public void UpdatePopup_LeftClickOnClearRect_ClearsLaneBindings()
        {
            var game = CreateGameWithViewport(1280, 720);
            var stage = new DrumConfigStage(game);
            var working = new KeyBindings();
            var popup = new DrumCapturePopup(working,
                () => new Dictionary<Keys, InputCommandType>(), _ => { });
            popup.Open(4); // lane 4 has the default "Key.S" binding
            ReflectionHelpers.SetPrivateField(stage, "_popup", popup);
            ReflectionHelpers.SetPrivateField(stage, "_selectedLane", 4);

            var clearCenter = popup.GetClearRect(1280, 720).Center;
            ReflectionHelpers.InvokePrivateMethod(stage, "UpdatePopup",
                0.0, MouseAt(clearCenter.X, clearCenter.Y, true), true, false);

            Assert.Empty(working.GetButtonsForLane(4));
        }

        [Fact]
        public void UpdatePopup_LeftClickOnChipRemove_RemovesOnlyThatBinding()
        {
            var game = CreateGameWithViewport(1280, 720);
            var stage = new DrumConfigStage(game);
            var working = new KeyBindings();
            var popup = new DrumCapturePopup(working,
                () => new Dictionary<Keys, InputCommandType>(), _ => { });
            popup.Open(4);
            popup.TryCapture(new DTXMania.Game.Lib.Input.ButtonState("Key.Q", true)); // "Key.S" + "Key.Q"
            ReflectionHelpers.SetPrivateField(stage, "_popup", popup);
            ReflectionHelpers.SetPrivateField(stage, "_selectedLane", 4);

            var removeRect = popup.GetBindingChips(1280, 720)
                .Single(c => c.ButtonId == "Key.S").Remove;
            ReflectionHelpers.InvokePrivateMethod(stage, "UpdatePopup",
                0.0, MouseAt(removeRect.Center.X, removeRect.Center.Y, true), true, false);

            Assert.DoesNotContain("Key.S", working.GetButtonsForLane(4));
            Assert.Contains("Key.Q", working.GetButtonsForLane(4));
        }

        [Fact]
        public void UpdatePopup_SkipCaptureThisFrame_DoesNotCapture()
        {
            var game = CreateGameWithViewport(1280, 720);
            var stage = new DrumConfigStage(game);
            var working = new KeyBindings();
            var popup = new DrumCapturePopup(working,
                () => new Dictionary<Keys, InputCommandType>(), _ => { });
            popup.Open(4);
            ReflectionHelpers.SetPrivateField(stage, "_popup", popup);
            ReflectionHelpers.SetPrivateField(stage, "_selectedLane", 4);
            ReflectionHelpers.SetPrivateField(stage, "_skipCaptureThisFrame", true);

            ReflectionHelpers.InvokePrivateMethod(stage, "UpdatePopup",
                0.0, MouseAt(10, 10, false), false, false);

            Assert.True(popup.IsOpen); // still listening
            Assert.False(ReflectionHelpers.GetPrivateField<bool>(stage, "_skipCaptureThisFrame")); // flag consumed
            // Only the lane's pre-existing default binding remains; the activating press was suppressed.
            Assert.Equal(new[] { "Key.S" }, working.GetButtonsForLane(4));
        }

        [Fact]
        public void UpdatePopup_PressedButton_CapturesIntoLane()
        {
            var game = CreateGameWithViewport(1280, 720);
            var stage = new DrumConfigStage(game);
            var working = new KeyBindings();
            var popup = new DrumCapturePopup(working,
                () => new Dictionary<Keys, InputCommandType>(), _ => { });
            popup.Open(7);
            ReflectionHelpers.SetPrivateField(stage, "_popup", popup);
            ReflectionHelpers.SetPrivateField(stage, "_selectedLane", 7);
            // A live input manager whose pressed-button feed yields one captured button this frame.
            using var input = new InputManagerCompat(new ConfigManager());
            input.ModularInputManager.InjectButton("Key.J", true);
            input.ModularInputManager.Update();
            ReflectionHelpers.SetPrivateField(stage, "_input", input);

            ReflectionHelpers.InvokePrivateMethod(stage, "UpdatePopup",
                0.0, MouseAt(10, 10, false), false, false);

            Assert.Contains("Key.J", working.GetButtonsForLane(7));
        }

        // ---- Other previously-uncovered members ----

        [Fact]
        public void Type_ReturnsDrumConfigStageType()
        {
            var game = ReflectionHelpers.CreateGame();
            ReflectionHelpers.SetProperty(game, nameof(BaseGame.ConfigManager), new StubConfigManager());
            var stage = new DrumConfigStage(game);

            Assert.Equal(StageType.DrumConfig, stage.Type);
        }

        [Fact]
        public void OpenPopup_OpensPopupAndSetsFocusAndSkipFlag()
        {
            var game = ReflectionHelpers.CreateGame();
            ReflectionHelpers.SetProperty(game, nameof(BaseGame.ConfigManager), new StubConfigManager());
            var stage = new DrumConfigStage(game);
            var popup = new DrumCapturePopup(new KeyBindings(),
                () => new Dictionary<Keys, InputCommandType>(), _ => { });
            ReflectionHelpers.SetPrivateField(stage, "_popup", popup);

            ReflectionHelpers.InvokePrivateMethod(stage, "OpenPopup", 5);

            Assert.True(popup.IsOpen);
            Assert.Equal(5, popup.Lane);
            Assert.Equal(5, ReflectionHelpers.GetPrivateField<int>(stage, "_selectedLane"));
            Assert.Equal(5, ReflectionHelpers.GetPrivateField<int>(stage, "_focusIndex"));
            Assert.True(ReflectionHelpers.GetPrivateField<bool>(stage, "_skipCaptureThisFrame"));
        }

        [Fact]
        public void CommitAndExit_InvokesSaveAndExitsWhenConfigIsNonConcrete()
        {
            var game = ReflectionHelpers.CreateGame();
            ReflectionHelpers.SetProperty(game, nameof(BaseGame.ConfigManager), new StubConfigManager());
            var stage = new DrumConfigStage(game);
            using var input = new FakeInput(new ConfigManager()); // non-null input -> Save proceeds
            ReflectionHelpers.SetPrivateField(stage, "_input", input);
            ReflectionHelpers.SetPrivateField(stage, "_workingSystemBindings", new Dictionary<Keys, InputCommandType>());

            var ex = Record.Exception(() => ReflectionHelpers.InvokePrivateMethod(stage, "CommitAndExit"));

            Assert.Null(ex); // Save exits via the non-concrete-config branch (StageManager null -> no-op)
        }

        [Fact]
        public void Save_WithNullInput_ExitsImmediatelyWithoutTouchingConfig()
        {
            var game = ReflectionHelpers.CreateGame();
            ReflectionHelpers.SetProperty(game, nameof(BaseGame.ConfigManager), new StubConfigManager());
            var stage = new DrumConfigStage(game);
            ReflectionHelpers.SetPrivateField(stage, "_input", null); // no live input -> skip persisting

            var exited = ReflectionHelpers.InvokePrivateMethod<bool>(stage, "Save");

            Assert.True(exited);
        }

        [Fact]
        public void ApplySystemBindings_ReplacesAllMappingsOnInputManager()
        {
            var input = new InputManager();
            input.AddKeyMapping(Keys.Up, InputCommandType.MoveUp); // pre-existing, must be removed

            var method = typeof(DrumConfigStage).GetMethod("ApplySystemBindings",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);

            var bindings = new Dictionary<Keys, InputCommandType>
            {
                [Keys.Down] = InputCommandType.MoveDown,
                [Keys.Space] = InputCommandType.Activate,
            };
            method!.Invoke(null, new object[] { input, bindings });

            var snapshot = input.GetKeyMappingSnapshot();
            Assert.False(snapshot.ContainsKey(Keys.Up));            // old mapping removed
            Assert.Equal(InputCommandType.MoveDown, snapshot[Keys.Down]);
            Assert.Equal(InputCommandType.Activate, snapshot[Keys.Space]);
        }

        private sealed class StubGraphicsDeviceService : IGraphicsDeviceService
        {
            private readonly GraphicsDevice _device;
            public StubGraphicsDeviceService(GraphicsDevice device) => _device = device;
            public GraphicsDevice GraphicsDevice => _device;
            public event EventHandler<EventArgs>? DeviceCreated;
            public event EventHandler<EventArgs>? DeviceDisposing;
            public event EventHandler<EventArgs>? DeviceReset;
            public event EventHandler<EventArgs>? DeviceResetting;
        }

        // InputManagerCompat subclass that returns canned values for the query methods the stage
        // reads during keyboard-driven navigation, without needing a live keyboard.
        private sealed class FakeInput : InputManagerCompat
        {
            public bool BackTriggered;
            public InputCommandType? ActiveCommand;
            public int? PressedKey;
            public FakeInput(ConfigManager config) : base(config) { }
            public override bool IsBackActionTriggered() => BackTriggered;
            public override bool IsCommandPressed(InputCommandType command) => command == ActiveCommand;
            public override bool IsKeyPressed(int keyCode) => keyCode == PressedKey;
        }

        private sealed class StubConfigManager : IConfigManager
        {
            public ConfigData Config { get; } = new ConfigData();

            public event EventHandler<ScrollSpeedChangedEventArgs>? ScrollSpeedChanged;

            public void LoadConfig(string filePath) { }

            public void SaveConfig(string filePath) { }

            public void ResetToDefaults() { }

            public void SetScrollSpeed(string configFilePath, int percent) { }

            public void AdjustScrollSpeed(string configFilePath, int stepDelta) { }

            public void FlushPendingSave() { }
        }
    }
}
