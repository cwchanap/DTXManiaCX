using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
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
                () => new Dictionary<Keys, InputCommandType>());
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
        public void Save_EvictsSystemKeysClaimedByDrumLanes()
        {
            // Deferred eviction at commit: a non-required system key bound to a drum lane is evicted
            // from the system mapping when (and only when) the binding is persisted.
            var configManager = new ConfigManager();
            using var input = new InputManagerCompat(configManager);
            var game = ReflectionHelpers.CreateGame();
            ReflectionHelpers.SetProperty(game, nameof(BaseGame.ConfigManager), configManager);
            ReflectionHelpers.SetProperty(game, nameof(BaseGame.InputManager), input);

            var stage = new DrumConfigStage(game);
            ReflectionHelpers.SetPrivateField(stage, "_input", input);

            var working = input.ModularInputManager.KeyBindings.Clone();
            working.BindButton("Key.PageUp", 7); // PageUp is a non-required system key
            ReflectionHelpers.SetPrivateField(stage, "_workingBindings", working);

            var sys = new Dictionary<Keys, InputCommandType>
            {
                [Keys.PageUp] = InputCommandType.IncreaseScrollSpeed
            };
            ReflectionHelpers.SetPrivateField(stage, "_workingSystemBindings", sys);

            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            var previousRoot = Environment.GetEnvironmentVariable("DTXMANIA_APPDATA_ROOT");
            Environment.SetEnvironmentVariable("DTXMANIA_APPDATA_ROOT", tempDir);
            try
            {
                ReflectionHelpers.InvokePrivateMethod<bool>(stage, "Save");

                // PageUp is now claimed by lane 7 -> evicted from the system mapping at commit.
                Assert.False(sys.ContainsKey(Keys.PageUp));
                Assert.Equal(string.Empty, configManager.Config.SystemKeyBindings["SystemKey.IncreaseScrollSpeed"]);
            }
            finally
            {
                Environment.SetEnvironmentVariable("DTXMANIA_APPDATA_ROOT", previousRoot);
                Directory.Delete(tempDir, recursive: true);
            }
        }

        [Fact]
        public void Save_PreservesSystemKeyWhenDrumBindingRemovedBeforeCommit()
        {
            // The undo path the deferred-eviction fix protects: capture then remove a non-required
            // system key before Save. Since the key is no longer bound to any drum lane at commit,
            // the system shortcut must survive.
            var configManager = new ConfigManager();
            using var input = new InputManagerCompat(configManager);
            var game = ReflectionHelpers.CreateGame();
            ReflectionHelpers.SetProperty(game, nameof(BaseGame.ConfigManager), configManager);
            ReflectionHelpers.SetProperty(game, nameof(BaseGame.InputManager), input);

            var stage = new DrumConfigStage(game);
            ReflectionHelpers.SetPrivateField(stage, "_input", input);

            var working = input.ModularInputManager.KeyBindings.Clone();
            working.BindButton("Key.PageUp", 7);
            working.UnbindButton("Key.PageUp"); // undone before Save
            ReflectionHelpers.SetPrivateField(stage, "_workingBindings", working);

            var sys = new Dictionary<Keys, InputCommandType>
            {
                [Keys.PageUp] = InputCommandType.IncreaseScrollSpeed
            };
            ReflectionHelpers.SetPrivateField(stage, "_workingSystemBindings", sys);

            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            var previousRoot = Environment.GetEnvironmentVariable("DTXMANIA_APPDATA_ROOT");
            Environment.SetEnvironmentVariable("DTXMANIA_APPDATA_ROOT", tempDir);
            try
            {
                ReflectionHelpers.InvokePrivateMethod<bool>(stage, "Save");

                // PageUp is not bound to any drum lane -> system shortcut preserved.
                Assert.True(sys.ContainsKey(Keys.PageUp));
                Assert.Equal("PageUp", configManager.Config.SystemKeyBindings["SystemKey.IncreaseScrollSpeed"]);
            }
            finally
            {
                Environment.SetEnvironmentVariable("DTXMANIA_APPDATA_ROOT", previousRoot);
                Directory.Delete(tempDir, recursive: true);
            }
        }

        [Fact]
        public void Save_WhenSaveConfigThrowsAfterEviction_RestoresWorkingSystemBindingsForRetry()
        {
            // Regression for the failure-after-eviction path: a non-required system key claimed by
            // a drum lane is evicted from _workingSystemBindings at commit time. If the disk write
            // then fails, the stage stays open for retry and the eviction must be rolled back,
            // otherwise the user can never recover that system shortcut (undoing the drum binding
            // wouldn't bring it back, since it has already been removed from the working copy).
            var configManager = new ConfigManager();
            using var input = new InputManagerCompat(configManager);
            var game = ReflectionHelpers.CreateGame();
            ReflectionHelpers.SetProperty(game, nameof(BaseGame.ConfigManager), configManager);
            ReflectionHelpers.SetProperty(game, nameof(BaseGame.InputManager), input);

            var stage = new DrumConfigStage(game);
            ReflectionHelpers.SetPrivateField(stage, "_input", input);

            // Pre-populate the committed config so we can verify the rollback restores it
            // exactly (an empty default would mask a half-applied eviction on retry elsewhere).
            configManager.Config.SystemKeyBindings["SystemKey.IncreaseScrollSpeed"] = "PageUp";

            var working = input.ModularInputManager.KeyBindings.Clone();
            working.BindButton("Key.PageUp", 7); // PageUp is a non-required system key
            ReflectionHelpers.SetPrivateField(stage, "_workingBindings", working);

            var sys = new Dictionary<Keys, InputCommandType>
            {
                [Keys.PageUp] = InputCommandType.IncreaseScrollSpeed
            };
            ReflectionHelpers.SetPrivateField(stage, "_workingSystemBindings", sys);

            // Point DTXMANIA_APPDATA_ROOT at a file (not a directory) so SaveConfig throws when
            // it tries to write Config.ini underneath it.
            var tempFile = Path.GetTempFileName();
            var previousRoot = Environment.GetEnvironmentVariable("DTXMANIA_APPDATA_ROOT");
            Environment.SetEnvironmentVariable("DTXMANIA_APPDATA_ROOT", tempFile);
            try
            {
                var exited = ReflectionHelpers.InvokePrivateMethod<bool>(stage, "Save");

                // Stage must stay open and surface the failure.
                Assert.False(exited);
                var saveError = ReflectionHelpers.GetPrivateField<string?>(stage, "_saveError");
                Assert.False(string.IsNullOrEmpty(saveError));

                // The eviction must be rolled back so a retry starts with PageUp still bound to
                // the system shortcut. The user can then undo the drum binding to recover it.
                Assert.True(sys.ContainsKey(Keys.PageUp));
                Assert.Equal(InputCommandType.IncreaseScrollSpeed, sys[Keys.PageUp]);

                // And the config manager's system bindings must also be rolled back to their
                // pre-Save state (no half-applied eviction leaking into a later save elsewhere).
                Assert.Equal("PageUp", configManager.Config.SystemKeyBindings["SystemKey.IncreaseScrollSpeed"]);
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
                () => new Dictionary<Keys, InputCommandType>());
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
                () => new Dictionary<Keys, InputCommandType>());
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
                () => new Dictionary<Keys, InputCommandType>());
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
                () => new Dictionary<Keys, InputCommandType>());
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
                () => new Dictionary<Keys, InputCommandType>());
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
                () => new Dictionary<Keys, InputCommandType>());
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
                () => new Dictionary<Keys, InputCommandType>());
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
                () => new Dictionary<Keys, InputCommandType>());
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
                () => new Dictionary<Keys, InputCommandType>());
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
                () => new Dictionary<Keys, InputCommandType>());
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
                () => new Dictionary<Keys, InputCommandType>());
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
                () => new Dictionary<Keys, InputCommandType>());
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

        // ---- ResolvePopupSystemMapping (pending-aware conflict source) ----
        // Pins the Comment-1 fix: the capture popup must reject against ConfigStage's PENDING
        // system edits when present (so a key just assigned to a required command — e.g. MoveUp on
        // Z — is rejected even though the live mapping still has the old key), and otherwise fall
        // back to the live snapshot. GraphicsDevice-free, so this is unit-testable headlessly.

        private static IReadOnlyDictionary<Keys, InputCommandType> ResolvePopupSystemMapping(
            Dictionary<Keys, InputCommandType>? pending,
            Dictionary<Keys, InputCommandType> live)
        {
            var method = typeof(DrumConfigStage).GetMethod("ResolvePopupSystemMapping",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);
            return (IReadOnlyDictionary<Keys, InputCommandType>)method!.Invoke(null,
                new object?[] { pending, live })!;
        }

        [Fact]
        public void ResolvePopupSystemMapping_WithPending_ReturnsPendingOverLive()
        {
            // Live still maps MoveUp to Up; the user (pending) just moved it to Z. The popup must
            // consult the pending map so Z is seen as a required key and rejected at capture.
            var pending = new Dictionary<Keys, InputCommandType> { [Keys.Z] = InputCommandType.MoveUp };
            var live = new Dictionary<Keys, InputCommandType> { [Keys.Up] = InputCommandType.MoveUp };

            var resolved = ResolvePopupSystemMapping(pending, live);

            Assert.Same(pending, resolved);
            Assert.True(resolved.ContainsKey(Keys.Z));
            Assert.False(resolved.ContainsKey(Keys.Up));
        }

        [Fact]
        public void ResolvePopupSystemMapping_WithoutPending_FallsBackToLive()
        {
            // No pending edits (e.g. DrumConfig entered directly, or Config has nothing unsaved):
            // the popup rejects against the live mapping, preserving the pre-fix behaviour.
            var live = new Dictionary<Keys, InputCommandType> { [Keys.Up] = InputCommandType.MoveUp };

            var resolved = ResolvePopupSystemMapping(pending: null, live);

            Assert.Same(live, resolved);
        }

        [Fact]
        public void PendingSystemBindingsKey_IsStableIdentifier()
        {
            // ConfigStage references this constant to populate the shared-data payload; renaming it
            // silently would break the cross-stage contract. Pin the value.
            Assert.Equal("PendingSystemBindings", DrumConfigStage.PendingSystemBindingsKey);
        }

        [Fact]
        public void ProcessPopupCapture_ReservedKeyBeforeValidKey_DropsValidKeySameFrame()
        {
            // Pins the stage-level one-binding-per-frame rule: ConsumePressedButtons is drained in
            // enumeration order, and the first non-Ignored outcome (here a reserved key -> Rejected)
            // breaks the loop. A valid key pressed in that same frame is therefore NOT captured. The
            // popup-level Rejected path is covered in DrumCapturePopupTests; this pins the stage's
            // foreach+break interaction that the popup test cannot reach.
            var configManager = new ConfigManager();
            using var input = new InputManagerCompat(configManager);
            var game = ReflectionHelpers.CreateGame();
            ReflectionHelpers.SetProperty(game, nameof(BaseGame.ConfigManager), configManager);
            ReflectionHelpers.SetProperty(game, nameof(BaseGame.InputManager), input);
            var stage = new DrumConfigStage(game);
            ReflectionHelpers.SetPrivateField(stage, "_input", input);

            var working = new KeyBindings();
            var popup = new DrumCapturePopup(working, () => new Dictionary<Keys, InputCommandType>());
            popup.Open(4); // Snare
            ReflectionHelpers.SetPrivateField(stage, "_popup", popup);

            // Seed this frame's press buffer directly (skipping Update()/keyboard): reserved Enter
            // first, then a valid unbound key Q. Order determines which is resolved.
            var pressedField = typeof(ModularInputManager)
                .GetField("_pressedThisFrame", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(pressedField);
            var pressed = (List<DTXMania.Game.Lib.Input.ButtonState>)pressedField!.GetValue(input.ModularInputManager)!;
            pressed.Add(new DTXMania.Game.Lib.Input.ButtonState("Key.Enter", true)); // required nav -> Rejected
            pressed.Add(new DTXMania.Game.Lib.Input.ButtonState("Key.Q", true));     // valid unbound -> would Capture

            ReflectionHelpers.InvokePrivateMethod(stage, "ProcessPopupCapture");

            // Enter was Rejected (non-Ignored) -> break before Q was tried, so Q is dropped this frame.
            Assert.DoesNotContain("Key.Q", working.GetButtonsForLane(4));
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

            public event EventHandler<EventArgs>? KeyBindingsChanged;

            public event EventHandler<EventArgs>? SystemKeyBindingsChanged;

            public void LoadConfig(string filePath) { }

            public void SaveConfig(string filePath) { }

            public void ResetToDefaults() { }

            public void SetScrollSpeed(string configFilePath, int percent) { }

            public void AdjustScrollSpeed(string configFilePath, int stepDelta) { }

            public void SetKeyBindings(KeyBindings keyBindings) { }

            public void SetSystemKeyBindings(IReadOnlyDictionary<Keys, InputCommandType> workingBindings) { }

            public void SetAutoPlay(bool value) { }

            public void SetNoFail(bool value) { }

            public void SetAudioLatency(int value) { }

            public void SetResolution(int width, int height) { }

            public void SetFullscreen(bool value) { }

            public void SetVSync(bool value) { }

            public void FlushPendingSave() { }
        }
    }
}
