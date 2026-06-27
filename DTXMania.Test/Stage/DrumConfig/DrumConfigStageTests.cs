using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using DTXMania.Game;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Stage;
using DTXMania.Game.Lib.Stage.DrumConfig;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Utilities;
using DTXMania.Test.TestData;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Xunit;
using Moq;
using ButtonState = DTXMania.Game.Lib.Input.ButtonState;
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

        // ---- Edit helpers (persist-on-edit: edits mutate Config via the live-apply setters) ----

        [Fact]
        public void ApplyCapture_BindsButtonToLaneInConfig()
        {
            var (stage, cm, input) = CreateWiredStage();

            ReflectionHelpers.InvokePrivateMethod(stage, "ApplyCapture", "Key.Q", 5);

            // Config is the source of truth; the runtime mirrors it via the Phase 2 events.
            Assert.Equal(5, cm.Config.KeyBindings["Key.Q"]);
            Assert.Equal(5, input.ModularInputManager.KeyBindings.GetLane("Key.Q"));
        }

        [Fact]
        public void ApplyCapture_EvictsSystemKeyImmediately()
        {
            // Capture-time eviction (Decision 3): a keyboard key claimed by a drum lane leaves the
            // system map NOW (no deferred eviction, no restore on undo).
            var (stage, cm, _) = CreateWiredStage();
            // PageUp is a non-required system key.
            cm.SetSystemKeyBindings(new Dictionary<Keys, InputCommandType>
            {
                [Keys.PageUp] = InputCommandType.IncreaseScrollSpeed
            });
            Assert.Equal("PageUp", cm.Config.SystemKeyBindings["SystemKey.IncreaseScrollSpeed"]);

            ReflectionHelpers.InvokePrivateMethod(stage, "ApplyCapture", "Key.PageUp", 7);

            // PageUp is now a drum lane key -> evicted from the system mapping immediately.
            Assert.Equal("", cm.Config.SystemKeyBindings["SystemKey.IncreaseScrollSpeed"]);
            Assert.Equal(7, cm.Config.KeyBindings["Key.PageUp"]);
        }

        [Fact]
        public void ApplyCapture_DoesNotEvictUnrelatedSystemKey()
        {
            // Capturing an unrelated key must not touch an existing system mapping.
            var (stage, cm, _) = CreateWiredStage();
            cm.SetSystemKeyBindings(new Dictionary<Keys, InputCommandType>
            {
                [Keys.PageUp] = InputCommandType.IncreaseScrollSpeed
            });

            ReflectionHelpers.InvokePrivateMethod(stage, "ApplyCapture", "Key.Q", 5);

            Assert.Equal("PageUp", cm.Config.SystemKeyBindings["SystemKey.IncreaseScrollSpeed"]);
            Assert.Equal(5, cm.Config.KeyBindings["Key.Q"]);
        }

        [Fact]
        public void RemoveBindingFromConfig_RemovesBindingFromConfig()
        {
            var (stage, cm, input) = CreateWiredStage();
            ReflectionHelpers.InvokePrivateMethod(stage, "ApplyCapture", "Key.Q", 4);

            ReflectionHelpers.InvokePrivateMethod(stage, "RemoveBindingFromConfig", "Key.Q");

            Assert.False(cm.Config.KeyBindings.ContainsKey("Key.Q"));
            Assert.Equal(-1, input.ModularInputManager.KeyBindings.GetLane("Key.Q"));
        }

        [Fact]
        public void ClearLaneInConfig_ClearsAllButtonsForLane()
        {
            var (stage, cm, input) = CreateWiredStage();
            // Lane 4 has the default "Key.S" in the runtime; add a second binding too.
            ReflectionHelpers.InvokePrivateMethod(stage, "ApplyCapture", "Key.Q", 4);

            ReflectionHelpers.InvokePrivateMethod(stage, "ClearLaneInConfig", 4);

            Assert.Empty(input.ModularInputManager.KeyBindings.GetButtonsForLane(4));
            Assert.False(cm.Config.KeyBindings.ContainsKey("Key.S"));
            Assert.False(cm.Config.KeyBindings.ContainsKey("Key.Q"));
        }

        [Fact]
        public void ResetDrumBindingsToDefault_RestoresDefaultsInConfig()
        {
            var (stage, cm, _) = CreateWiredStage();
            // Add a non-default binding that Reset must clear.
            ReflectionHelpers.InvokePrivateMethod(stage, "ApplyCapture", "Key.Z", 3);
            Assert.True(cm.Config.KeyBindings.ContainsKey("Key.Z"));

            ReflectionHelpers.InvokePrivateMethod(stage, "ResetDrumBindingsToDefault");

            // "Z" is not a default key, so resetting drops it; defaults restored.
            Assert.False(cm.Config.KeyBindings.ContainsKey("Key.Z"));
            Assert.Equal(4, cm.Config.KeyBindings["Key.S"]); // default Snare binding restored
        }

        [Fact]
        public void ExitStage_FlushesPendingSaveOnConcreteConfigManager()
        {
            var (stage, cm, _) = CreateWiredStage();
            // Point the deferred-save path at a real temp dir so FlushPendingSave can write.
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            var previousRoot = Environment.GetEnvironmentVariable("DTXMANIA_APPDATA_ROOT");
            Environment.SetEnvironmentVariable("DTXMANIA_APPDATA_ROOT", tempDir);
            try
            {
                cm.LoadConfig(AppPaths.GetConfigFilePath());
                ReflectionHelpers.InvokePrivateMethod(stage, "ApplyCapture", "Key.Q", 5); // marks dirty
                Assert.NotNull(GetPendingSavePath(cm));

                ReflectionHelpers.InvokePrivateMethod(stage, "ExitStage");

                // Exit flushed the deferred save to disk.
                Assert.Null(GetPendingSavePath(cm));
                var configPath = AppPaths.GetConfigFilePath();
                Assert.True(File.Exists(configPath));
            }
            finally
            {
                Environment.SetEnvironmentVariable("DTXMANIA_APPDATA_ROOT", previousRoot);
                Directory.Delete(tempDir, recursive: true);
            }
        }

        // ---- Comment-1 round-trip regression (Task 4.3) ----

        [Fact]
        public void Comment1_CaptureDoesNotDropUnrelatedSystemKey_OnRoundTrip()
        {
            // The original Comment-1 bug: entering DrumConfig and capturing a key could silently
            // evict/drop an unrelated system key on return. Under persist-on-edit (Config = truth,
            // capture-time eviction only for the captured key), this is structurally impossible.
            var (stage, cm, input) = CreateWiredStage();
            var popup = new DrumCapturePopup(
                () => input.ModularInputManager.KeyBindings.ButtonToLane,
                () => input.GetKeyMappingSnapshot());
            ReflectionHelpers.SetPrivateField(stage, "_popup", popup);

            // Pre-condition: Z is a required system key (MoveUp) in Config.
            cm.SetSystemKeyBindings(new Dictionary<Keys, InputCommandType>
            {
                [Keys.Z] = InputCommandType.MoveUp
            });
            Assert.Equal("Z", cm.Config.SystemKeyBindings["SystemKey.MoveUp"]);

            // Simulate entering DrumConfig and capturing a DIFFERENT key (Q) to a lane.
            ReflectionHelpers.InvokePrivateMethod(stage, "ApplyCapture", "Key.Q", 5);

            // The unrelated required system key Z survives — no pending state, no deferred eviction.
            Assert.Equal("Z", cm.Config.SystemKeyBindings["SystemKey.MoveUp"]);
            // And Q is bound to the drum lane in Config.
            Assert.Equal(5, cm.Config.KeyBindings["Key.Q"]);

            // Capturing Z itself (the required key) is REJECTED by the popup, so it can never reach
            // a drum lane and therefore can never evict itself from the system map.
            popup.Open(4);
            var outcome = popup.TryCapture(new ButtonState("Key.Z", true));
            Assert.Equal(DrumCaptureOutcome.Rejected, outcome);
            Assert.False(cm.Config.KeyBindings.ContainsKey("Key.Z"));
        }

        // ---- ActivateFocusedElement dispatch (headless, GraphicsDevice-free) ----

        [Fact]
        public void ActivateFocusedElement_WhenResetFocused_ResetsDrumBindingsToDefaultInConfig()
        {
            // Keyboard-reachable Reset (design: zones + Reset in one focus order). Activate while
            // Reset holds focus must restore defaults in Config. GraphicsDevice-free, so this
            // exercises the dispatch headlessly without OnActivate.
            var (stage, cm, _) = CreateWiredStage();
            ReflectionHelpers.InvokePrivateMethod(stage, "ApplyCapture", "Key.Z", 3); // non-default
            ReflectionHelpers.SetPrivateField(stage, "_focusIndex", DrumKitLayout.ResetActionIndex);

            ReflectionHelpers.InvokePrivateMethod(stage, "ActivateFocusedElement");

            // "Z" is not a default key, so resetting drops it from Config.
            Assert.False(cm.Config.KeyBindings.ContainsKey("Key.Z"));
        }

        [Fact]
        public void ActivateFocusedElement_WhenZoneFocused_OpensPopupForThatLane()
        {
            var game = CreateGameWithViewport(1280, 720);
            var stage = new DrumConfigStage(game);
            var drum = new Dictionary<string, int>();
            var popup = new DrumCapturePopup(() => drum, () => new Dictionary<Keys, InputCommandType>());
            ReflectionHelpers.SetPrivateField(stage, "_popup", popup);
            ReflectionHelpers.SetPrivateField(stage, "_focusIndex", 4); // Snare Drum

            ReflectionHelpers.InvokePrivateMethod(stage, "ActivateFocusedElement");

            Assert.True(popup.IsOpen);
            Assert.Equal(4, popup.Lane);
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

        // Wired stage: real ConfigManager + InputManagerCompat (Phase 2 events connected) + a
        // graphics viewport + a popup reading the runtime (which mirrors Config). Drives the edit
        // helpers so Config mutation is observable, mirroring the production OnActivate wiring.
        private static (DrumConfigStage stage, ConfigManager cm, InputManagerCompat input) CreateWiredStage(
            int vw = 1280, int vh = 720)
        {
            var cm = new ConfigManager();
            var input = new InputManagerCompat(cm);
            var game = ReflectionHelpers.CreateGame();
            var device = ReflectionHelpers.CreateUninitialized<GraphicsDevice>();
            ReflectionHelpers.SetPrivateField(device, "_viewport", new Viewport(0, 0, vw, vh));
            GC.SuppressFinalize(device);
            ReflectionHelpers.SetPrivateField(game, "_graphicsDeviceService", new StubGraphicsDeviceService(device));
            ReflectionHelpers.SetProperty(game, nameof(BaseGame.ConfigManager), cm);
            ReflectionHelpers.SetProperty(game, nameof(BaseGame.InputManager), input);
            var stage = new DrumConfigStage(game);
            ReflectionHelpers.SetPrivateField(stage, "_input", input);
            var popup = new DrumCapturePopup(
                () => input.ModularInputManager.KeyBindings.ButtonToLane,
                () => input.GetKeyMappingSnapshot());
            ReflectionHelpers.SetPrivateField(stage, "_popup", popup);
            ReflectionHelpers.SetPrivateField(stage, "_previousMouse", default(MouseState));
            return (stage, cm, input);
        }

        private static string? GetPendingSavePath(ConfigManager cm)
            => ReflectionHelpers.GetPrivateField<string?>(cm, "_pendingSavePath");

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
            var drum = new Dictionary<string, int>();
            var popup = new DrumCapturePopup(() => drum, () => new Dictionary<Keys, InputCommandType>());
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
            var drum = new Dictionary<string, int>();
            var popup = new DrumCapturePopup(() => drum, () => new Dictionary<Keys, InputCommandType>());
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
            var drum = new Dictionary<string, int>();
            var popup = new DrumCapturePopup(() => drum, () => new Dictionary<Keys, InputCommandType>());
            ReflectionHelpers.SetPrivateField(stage, "_popup", popup);
            // Snare zone center is (380, 430) in the 1280x720 design space; viewport == design space.
            ReflectionHelpers.SetPrivateField(stage, "_previousMouse", MouseAt(0, 0, false));

            ReflectionHelpers.InvokePrivateMethod(stage, "UpdateSelection", MouseAt(380, 430, true), true);

            Assert.True(popup.IsOpen);
            Assert.Equal(4, popup.Lane);
            Assert.Equal(4, ReflectionHelpers.GetPrivateField<int>(stage, "_focusIndex"));
        }

        [Fact]
        public void UpdateSelection_LeftClickOnResetButton_ResetsDrumBindingsToDefaultInConfig()
        {
            var (stage, cm, _) = CreateWiredStage(1280, 720);
            // Add a non-default binding Reset must clear from Config.
            ReflectionHelpers.InvokePrivateMethod(stage, "ApplyCapture", "Key.Z", 3);
            ReflectionHelpers.SetPrivateField(stage, "_previousMouse", MouseAt(0, 0, false));

            // Reset button rect = (1070, 12, 190, 30); click its center.
            ReflectionHelpers.InvokePrivateMethod(stage, "UpdateSelection", MouseAt(1165, 27, true), true);

            Assert.Equal(DrumKitLayout.ResetActionIndex, ReflectionHelpers.GetPrivateField<int>(stage, "_focusIndex"));
            Assert.False(cm.Config.KeyBindings.ContainsKey("Key.Z")); // non-default binding cleared
            Assert.False(ReflectionHelpers.GetPrivateField<DrumCapturePopup>(stage, "_popup")!.IsOpen);
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
        public void UpdateSelection_MoveDownCommand_AdvancesFocusForward()
        {
            // Down maps to the same forward (+1) delta as Right/Tab: the focus sequence is a single
            // linear order (zones 0..9 then Reset), so Down/Right both advance and Up/Left go back.
            var game = CreateGameWithViewport(1280, 720);
            var stage = new DrumConfigStage(game);
            using var input = new FakeInput(new ConfigManager()) { ActiveCommand = InputCommandType.MoveDown };
            ReflectionHelpers.SetPrivateField(stage, "_input", input);
            ReflectionHelpers.SetPrivateField(stage, "_focusIndex", 3);
            ReflectionHelpers.SetPrivateField(stage, "_previousMouse", MouseAt(5, 5, false));

            ReflectionHelpers.InvokePrivateMethod(stage, "UpdateSelection", MouseAt(5, 5, false), false);

            Assert.Equal(4, ReflectionHelpers.GetPrivateField<int>(stage, "_focusIndex"));
            Assert.True(ReflectionHelpers.GetPrivateField<bool>(stage, "_keyboardFocusActive"));
        }

        [Fact]
        public void UpdateSelection_MoveUpCommand_AdvancesFocusBackward()
        {
            // Up maps to the same backward (-1) delta as Left.
            var game = CreateGameWithViewport(1280, 720);
            var stage = new DrumConfigStage(game);
            using var input = new FakeInput(new ConfigManager()) { ActiveCommand = InputCommandType.MoveUp };
            ReflectionHelpers.SetPrivateField(stage, "_input", input);
            ReflectionHelpers.SetPrivateField(stage, "_focusIndex", 3);
            ReflectionHelpers.SetPrivateField(stage, "_previousMouse", MouseAt(5, 5, false));

            ReflectionHelpers.InvokePrivateMethod(stage, "UpdateSelection", MouseAt(5, 5, false), false);

            Assert.Equal(2, ReflectionHelpers.GetPrivateField<int>(stage, "_focusIndex"));
            Assert.True(ReflectionHelpers.GetPrivateField<bool>(stage, "_keyboardFocusActive"));
        }

        [Fact]
        public void UpdateSelection_TabKey_AdvancesFocus()
        {
            var game = CreateGameWithViewport(1280, 720);
            var stage = new DrumConfigStage(game);
            using var input = new FakeInput(new ConfigManager());
            // Seed the per-frame pressed-button feed with a Tab press — the path the stage now
            // reads (ConsumePressedButtons), which is robust to MCP/E2E injection timing.
            input.ModularInputManager.InjectButton("Key.Tab", isPressed: true);
            input.ModularInputManager.Update();
            ReflectionHelpers.SetPrivateField(stage, "_input", input);
            ReflectionHelpers.SetPrivateField(stage, "_focusIndex", 0);
            ReflectionHelpers.SetPrivateField(stage, "_previousMouse", MouseAt(5, 5, false));

            ReflectionHelpers.InvokePrivateMethod(stage, "UpdateSelection", MouseAt(5, 5, false), false);

            Assert.Equal(1, ReflectionHelpers.GetPrivateField<int>(stage, "_focusIndex"));
        }

        [Fact]
        public void UpdateSelection_InjectedTabPressAndReleaseSameFrame_AdvancesFocus()
        {
            // The E2E/slow-frame flake: an injected Tab press AND release both queue before a
            // single Update() runs (a CI frame whose update exceeds the 40ms injected hold).
            // ProcessInjectedInputs drains both in one pass: the release clears the injected
            // key-state overlay before IsKeyPressed could see it, so the old IsKeyPressed path
            // missed the edge entirely and focus stalled. ConsumePressedButtons records the
            // press event regardless of a subsequent release this frame, so focus still advances.
            var game = CreateGameWithViewport(1280, 720);
            var stage = new DrumConfigStage(game);
            using var input = new FakeInput(new ConfigManager());
            input.ModularInputManager.InjectButton("Key.Tab", isPressed: true);
            input.ModularInputManager.InjectButton("Key.Tab", isPressed: false);
            input.ModularInputManager.Update(); // both drain inside one ProcessInjectedInputs
            ReflectionHelpers.SetPrivateField(stage, "_input", input);
            ReflectionHelpers.SetPrivateField(stage, "_focusIndex", 0);
            ReflectionHelpers.SetPrivateField(stage, "_previousMouse", MouseAt(5, 5, false));

            ReflectionHelpers.InvokePrivateMethod(stage, "UpdateSelection", MouseAt(5, 5, false), false);

            Assert.Equal(1, ReflectionHelpers.GetPrivateField<int>(stage, "_focusIndex"));
        }

        [Fact]
        public void UpdateSelection_MultipleInjectedTabsSameFrame_AdvancesFocusPerTab()
        {
            // The slow-CI-frame regression: when several injected Tab press/release pairs drain
            // inside one ProcessInjectedInputs (a frame whose update exceeds the inter-press
            // spacing of the E2E reset loop), ConsumePressedButtons returns every Tab press but
            // the navigation loop must advance focus once per Tab, not collapse them to a single
            // step. Otherwise focus stalls short of the Reset action and the next Enter activates
            // a lane instead of resetting bindings.
            var game = CreateGameWithViewport(1280, 720);
            var stage = new DrumConfigStage(game);
            using var input = new FakeInput(new ConfigManager());
            // Ten Tab press/release pairs all drain inside one Update(), matching the E2E reset
            // sequence (lane 0 -> 1 -> ... -> 9 -> Reset, index 10).
            for (int i = 0; i < 10; i++)
            {
                input.ModularInputManager.InjectButton("Key.Tab", isPressed: true);
                input.ModularInputManager.InjectButton("Key.Tab", isPressed: false);
            }
            input.ModularInputManager.Update();
            ReflectionHelpers.SetPrivateField(stage, "_input", input);
            ReflectionHelpers.SetPrivateField(stage, "_focusIndex", 0);
            ReflectionHelpers.SetPrivateField(stage, "_previousMouse", MouseAt(5, 5, false));

            ReflectionHelpers.InvokePrivateMethod(stage, "UpdateSelection", MouseAt(5, 5, false), false);

            Assert.Equal(10, ReflectionHelpers.GetPrivateField<int>(stage, "_focusIndex"));
        }

        [Fact]
        public void UpdateSelection_BackAction_ExitsStage()
        {
            var game = CreateGameWithViewport(1280, 720);
            var stage = new DrumConfigStage(game);
            // Non-concrete config + live input: ExitStage flushes (no-op on stub) and transitions
            // (StageManager null -> ChangeStage no-op) cleanly.
            using var input = new FakeInput(new ConfigManager()) { BackTriggered = true };
            ReflectionHelpers.SetPrivateField(stage, "_input", input);
            ReflectionHelpers.SetPrivateField(stage, "_previousMouse", MouseAt(5, 5, false));

            var ex = Record.Exception(() =>
                ReflectionHelpers.InvokePrivateMethod(stage, "UpdateSelection", MouseAt(5, 5, false), false));

            Assert.Null(ex); // ExitStage -> ChangeStage (StageManager null -> no-op) returns cleanly
        }

        [Fact]
        public void UpdateSelection_ActivateCommandOnZone_OpensPopup()
        {
            var game = CreateGameWithViewport(1280, 720);
            var stage = new DrumConfigStage(game);
            using var input = new FakeInput(new ConfigManager()) { ActiveCommand = InputCommandType.Activate };
            var drum = new Dictionary<string, int>();
            var popup = new DrumCapturePopup(() => drum, () => new Dictionary<Keys, InputCommandType>());
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
            var drum = new Dictionary<string, int>();
            var popup = new DrumCapturePopup(() => drum, () => new Dictionary<Keys, InputCommandType>());
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
            var drum = new Dictionary<string, int>();
            var popup = new DrumCapturePopup(() => drum, () => new Dictionary<Keys, InputCommandType>());
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
        public void UpdatePopup_LeftClickOnClearRect_ClearsLaneInConfig()
        {
            var (stage, cm, input) = CreateWiredStage(1280, 720);
            var popup = new DrumCapturePopup(
                () => input.ModularInputManager.KeyBindings.ButtonToLane,
                () => input.GetKeyMappingSnapshot());
            ReflectionHelpers.SetPrivateField(stage, "_popup", popup);
            popup.Open(4); // lane 4 has the default "Key.S" binding in the runtime
            ReflectionHelpers.SetPrivateField(stage, "_selectedLane", 4);

            var clearCenter = popup.GetClearRect(1280, 720).Center;
            ReflectionHelpers.InvokePrivateMethod(stage, "UpdatePopup",
                0.0, MouseAt(clearCenter.X, clearCenter.Y, true), true, false);

            Assert.Empty(input.ModularInputManager.KeyBindings.GetButtonsForLane(4));
            Assert.False(cm.Config.KeyBindings.ContainsKey("Key.S"));
        }

        [Fact]
        public void UpdatePopup_LeftClickOnChipRemove_RemovesOnlyThatBindingFromConfig()
        {
            var (stage, cm, input) = CreateWiredStage(1280, 720);
            // Lane 4 defaults to "Key.S"; add "Key.Q" to the same lane via the edit helper.
            ReflectionHelpers.InvokePrivateMethod(stage, "ApplyCapture", "Key.Q", 4);
            var popup = new DrumCapturePopup(
                () => input.ModularInputManager.KeyBindings.ButtonToLane,
                () => input.GetKeyMappingSnapshot());
            ReflectionHelpers.SetPrivateField(stage, "_popup", popup);
            popup.Open(4);
            ReflectionHelpers.SetPrivateField(stage, "_selectedLane", 4);

            var removeRect = popup.GetBindingChips(1280, 720)
                .Single(c => c.ButtonId == "Key.S").Remove;
            ReflectionHelpers.InvokePrivateMethod(stage, "UpdatePopup",
                0.0, MouseAt(removeRect.Center.X, removeRect.Center.Y, true), true, false);

            Assert.False(cm.Config.KeyBindings.ContainsKey("Key.S"));
            Assert.True(cm.Config.KeyBindings.ContainsKey("Key.Q"));
            Assert.Equal(4, cm.Config.KeyBindings["Key.Q"]);
        }

        [Fact]
        public void UpdatePopup_SkipCaptureThisFrame_DoesNotCapture()
        {
            var (stage, cm, input) = CreateWiredStage(1280, 720);
            var popup = new DrumCapturePopup(
                () => input.ModularInputManager.KeyBindings.ButtonToLane,
                () => input.GetKeyMappingSnapshot());
            popup.Open(4);
            ReflectionHelpers.SetPrivateField(stage, "_popup", popup);
            ReflectionHelpers.SetPrivateField(stage, "_selectedLane", 4);
            ReflectionHelpers.SetPrivateField(stage, "_skipCaptureThisFrame", true);

            ReflectionHelpers.InvokePrivateMethod(stage, "UpdatePopup",
                0.0, MouseAt(10, 10, false), false, false);

            Assert.True(popup.IsOpen); // still listening
            Assert.False(ReflectionHelpers.GetPrivateField<bool>(stage, "_skipCaptureThisFrame")); // flag consumed
            // The activating press was suppressed: no new binding reached Config this frame.
            Assert.False(cm.Config.KeyBindings.ContainsKey("Key.Q"));
        }

        [Fact]
        public void UpdatePopup_PressedButton_CapturesIntoConfig()
        {
            var (stage, cm, input) = CreateWiredStage(1280, 720);
            var popup = new DrumCapturePopup(
                () => input.ModularInputManager.KeyBindings.ButtonToLane,
                () => input.GetKeyMappingSnapshot());
            popup.Open(7);
            ReflectionHelpers.SetPrivateField(stage, "_popup", popup);
            ReflectionHelpers.SetPrivateField(stage, "_selectedLane", 7);
            // A live input manager whose pressed-button feed yields one captured button this frame.
            input.ModularInputManager.InjectButton("Key.Q", true);
            input.ModularInputManager.Update();

            ReflectionHelpers.InvokePrivateMethod(stage, "UpdatePopup",
                0.0, MouseAt(10, 10, false), false, false);

            Assert.Equal(7, cm.Config.KeyBindings["Key.Q"]);
            Assert.Contains("Key.Q", input.ModularInputManager.KeyBindings.GetButtonsForLane(7));
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
            var drum = new Dictionary<string, int>();
            var popup = new DrumCapturePopup(() => drum, () => new Dictionary<Keys, InputCommandType>());
            ReflectionHelpers.SetPrivateField(stage, "_popup", popup);

            ReflectionHelpers.InvokePrivateMethod(stage, "OpenPopup", 5);

            Assert.True(popup.IsOpen);
            Assert.Equal(5, popup.Lane);
            Assert.Equal(5, ReflectionHelpers.GetPrivateField<int>(stage, "_selectedLane"));
            Assert.Equal(5, ReflectionHelpers.GetPrivateField<int>(stage, "_focusIndex"));
            Assert.True(ReflectionHelpers.GetPrivateField<bool>(stage, "_skipCaptureThisFrame"));
        }

        [Fact]
        public void ProcessPopupCapture_ReservedKeyBeforeValidKey_DropsValidKeySameFrame()
        {
            // Pins the stage-level one-binding-per-frame rule: ConsumePressedButtons is drained in
            // enumeration order, and the first non-Ignored outcome (here a reserved key -> Rejected)
            // breaks the loop. A valid key pressed in that same frame is therefore NOT captured. The
            // popup-level Rejected path is covered in DrumCapturePopupTests; this pins the stage's
            // foreach+break interaction that the popup test cannot reach.
            var (stage, cm, _) = CreateWiredStage(1280, 720);
            var popup = ReflectionHelpers.GetPrivateField<DrumCapturePopup>(stage, "_popup")!;
            popup.Open(4); // Snare

            // Seed this frame's press buffer directly (skipping Update()/keyboard): reserved Enter
            // first, then a valid unbound key Q. Order determines which is resolved.
            var pressedField = typeof(ModularInputManager)
                .GetField("_pressedThisFrame", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(pressedField);
            var input = ReflectionHelpers.GetPrivateField<InputManagerCompat>(stage, "_input")!;
            var pressed = (List<DTXMania.Game.Lib.Input.ButtonState>)pressedField!.GetValue(input.ModularInputManager)!;
            pressed.Add(new DTXMania.Game.Lib.Input.ButtonState("Key.Enter", true)); // required nav -> Rejected
            pressed.Add(new DTXMania.Game.Lib.Input.ButtonState("Key.Q", true));     // valid unbound -> would Capture

            ReflectionHelpers.InvokePrivateMethod(stage, "ProcessPopupCapture");

            // Enter was Rejected (non-Ignored) -> break before Q was tried, so Q is dropped this frame.
            Assert.False(cm.Config.KeyBindings.ContainsKey("Key.Q"));
        }

        // ---- LoadOptionalTexture (1x1 fallback + exception path; headless via mock resources) ----

        private static DrumConfigStage NewStageWithResources(out Mock<IResourceManager> resources)
        {
            var game = CreateGameWithViewport(1280, 720);
            var stage = new DrumConfigStage(game);
            resources = new Mock<IResourceManager>();
            ReflectionHelpers.SetPrivateField(stage, "_resourceManager", resources.Object);
            return stage;
        }

        [Fact]
        public void LoadOptionalTexture_WhenResourceManagerReturnsNull_ReturnsNull()
        {
            var stage = NewStageWithResources(out var resources);
            resources.Setup(r => r.LoadTexture(It.IsAny<string>())).Returns((ITexture?)null);

            var result = ReflectionHelpers.InvokePrivateMethod<ITexture?>(stage, "LoadOptionalTexture",
                TexturePath.StartupBackground, "unavailable");

            Assert.Null(result);
        }

        [Fact]
        public void LoadOptionalTexture_WhenResourceManagerReturns1x1Fallback_ReturnsNullAndReleasesRef()
        {
            // ResourceManager.LoadTexture returns a 1x1 white fallback instead of throwing for a
            // missing asset. LoadOptionalTexture must treat that as "not present" (a real asset is
            // always larger than 1x1) and release the ref the load added before discarding it.
            var stage = NewStageWithResources(out var resources);
            var fallback = new Mock<ITexture>();
            fallback.SetupGet(t => t.Width).Returns(1);
            fallback.SetupGet(t => t.Height).Returns(1);
            resources.Setup(r => r.LoadTexture(It.IsAny<string>())).Returns(fallback.Object);

            var result = ReflectionHelpers.InvokePrivateMethod<ITexture?>(stage, "LoadOptionalTexture",
                TexturePath.StartupBackground, "unavailable");

            Assert.Null(result);
            fallback.Verify(t => t.RemoveReference(), Times.Once);
        }

        [Fact]
        public void LoadOptionalTexture_WhenResourceManagerThrows_ReturnsNull()
        {
            // Missing/invalid art is non-fatal: the exception is logged and null returned so the
            // stage falls back to the plain fill instead of crashing.
            var stage = NewStageWithResources(out var resources);
            resources.Setup(r => r.LoadTexture(It.IsAny<string>())).Throws(new IOException("missing"));

            var result = ReflectionHelpers.InvokePrivateMethod<ITexture?>(stage, "LoadOptionalTexture",
                TexturePath.StartupBackground, "unavailable");

            Assert.Null(result);
        }

        [Fact]
        public void LoadOptionalTexture_WhenResourceManagerReturnsRealAsset_ReturnsIt()
        {
            var stage = NewStageWithResources(out var resources);
            var texture = new Mock<ITexture>();
            texture.SetupGet(t => t.Width).Returns(128);
            texture.SetupGet(t => t.Height).Returns(720);
            resources.Setup(r => r.LoadTexture(It.IsAny<string>())).Returns(texture.Object);

            var result = ReflectionHelpers.InvokePrivateMethod<ITexture?>(stage, "LoadOptionalTexture",
                TexturePath.StartupBackground, "unavailable");

            Assert.Same(texture.Object, result);
            texture.Verify(t => t.RemoveReference(), Times.Never);
        }

        // ---- EvictSystemKey early-return paths (capture-time eviction) ----

        [Fact]
        public void ApplyCapture_NonKeyboardButton_BindsButDoesNotEvictSystemKey()
        {
            // EvictSystemKey short-circuits for non-keyboard buttons (MIDI/pad): no system key to
            // evict. The drum binding still applies to Config.
            var (stage, cm, _) = CreateWiredStage();
            cm.SetSystemKeyBindings(new Dictionary<Keys, InputCommandType>
            {
                [Keys.PageUp] = InputCommandType.IncreaseScrollSpeed
            });

            ReflectionHelpers.InvokePrivateMethod(stage, "ApplyCapture", "MIDI.36", 7);

            Assert.Equal(7, cm.Config.KeyBindings["MIDI.36"]);
            // Unrelated system key untouched (non-keyboard capture never reaches eviction).
            Assert.Equal("PageUp", cm.Config.SystemKeyBindings["SystemKey.IncreaseScrollSpeed"]);
        }

        [Fact]
        public void ApplyCapture_KeyboardButtonIdThatDoesNotParse_DoesNotEvictOrThrow()
        {
            // "Key." prefix passes IsKeyboardButtonId, but an unparseable key name must not evict
            // anything and must not throw (Enum.TryParse fails -> early return).
            var (stage, cm, _) = CreateWiredStage();
            cm.SetSystemKeyBindings(new Dictionary<Keys, InputCommandType>
            {
                [Keys.PageUp] = InputCommandType.IncreaseScrollSpeed
            });

            var ex = Record.Exception(() =>
                ReflectionHelpers.InvokePrivateMethod(stage, "ApplyCapture", "Key.NotARealKey", 7));

            Assert.Null(ex);
            // The drum binding for the unparseable id still applied via BindButton.
            Assert.Equal(7, cm.Config.KeyBindings["Key.NotARealKey"]);
            Assert.Equal("PageUp", cm.Config.SystemKeyBindings["SystemKey.IncreaseScrollSpeed"]);
        }

        [Fact]
        public void ResetDrumBindingsToDefault_WhenNoSystemKeysConflict_DoesNotRewriteSystemMap()
        {
            // EvictSystemKeysForDrumBindings only calls SetSystemKeyBindings when something was
            // actually removed (changed == true). With no overlapping system keys, the system map
            // is left untouched (the changed=false branch).
            var (stage, cm, input) = CreateWiredStage();
            // A system key on a non-drum key (PageUp) — default drum bindings are letters/space,
            // so nothing overlaps and reset must not rewrite the system map.
            cm.SetSystemKeyBindings(new Dictionary<Keys, InputCommandType>
            {
                [Keys.PageUp] = InputCommandType.IncreaseScrollSpeed
            });
            var systemBefore = input.GetKeyMappingSnapshot();

            ReflectionHelpers.InvokePrivateMethod(stage, "ResetDrumBindingsToDefault");

            // Defaults restored; system map unchanged because no eviction occurred.
            Assert.Equal(4, cm.Config.KeyBindings["Key.S"]);
            Assert.Equal(systemBefore.Count, input.GetKeyMappingSnapshot().Count);
            Assert.Equal("PageUp", cm.Config.SystemKeyBindings["SystemKey.IncreaseScrollSpeed"]);
        }

        // ---- OnDeactivate / Dispose (resource release; graphics fields nulled to stay headless) ----

        [Fact]
        public void OnDeactivate_FlushesPendingSaveAndReleasesResourceReferences()
        {
            var (stage, cm, _) = CreateWiredStage();
            // Mark a pending save so OnDeactivate's FlushPendingSave has something to flush.
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            var previousRoot = Environment.GetEnvironmentVariable("DTXMANIA_APPDATA_ROOT");
            Environment.SetEnvironmentVariable("DTXMANIA_APPDATA_ROOT", tempDir);
            try
            {
                cm.LoadConfig(AppPaths.GetConfigFilePath());
                ReflectionHelpers.InvokePrivateMethod(stage, "ApplyCapture", "Key.Q", 5); // marks dirty
                Assert.NotNull(GetPendingSavePath(cm));

                // Mocked managed resources + nulled graphics fields (SpriteBatch/Texture2D need a
                // GraphicsDevice); the null-conditional release paths are still exercised.
                var background = new Mock<ITexture>();
                var skeleton = new Mock<ITexture>();
                var font = new Mock<IFont>();
                ReflectionHelpers.SetPrivateField(stage, "_background", background.Object);
                ReflectionHelpers.SetPrivateField(stage, "_skeleton", skeleton.Object);
                ReflectionHelpers.SetPrivateField(stage, "_font", font.Object);
                ReflectionHelpers.SetPrivateField(stage, "_renderer", null);
                ReflectionHelpers.SetPrivateField(stage, "_spriteBatch", null);
                ReflectionHelpers.SetPrivateField(stage, "_whitePixel", null);

                ReflectionHelpers.InvokePrivateMethod(stage, "OnDeactivate");

                // Pending save flushed to disk.
                Assert.Null(GetPendingSavePath(cm));
                Assert.True(File.Exists(AppPaths.GetConfigFilePath()));
                // Managed resource references released.
                background.Verify(t => t.RemoveReference(), Times.Once);
                skeleton.Verify(t => t.RemoveReference(), Times.Once);
                font.Verify(f => f.RemoveReference(), Times.Once);
                Assert.Null(ReflectionHelpers.GetPrivateField<ITexture?>(stage, "_background"));
                Assert.Null(ReflectionHelpers.GetPrivateField<ITexture?>(stage, "_skeleton"));
                Assert.Null(ReflectionHelpers.GetPrivateField<IFont?>(stage, "_font"));
            }
            finally
            {
                Environment.SetEnvironmentVariable("DTXMANIA_APPDATA_ROOT", previousRoot);
                Directory.Delete(tempDir, recursive: true);
            }
        }

        [Fact]
        public void Dispose_DisposingTrue_ReleasesResourceReferences()
        {
            var (stage, _, _) = CreateWiredStage();
            var background = new Mock<ITexture>();
            var skeleton = new Mock<ITexture>();
            var font = new Mock<IFont>();
            ReflectionHelpers.SetPrivateField(stage, "_background", background.Object);
            ReflectionHelpers.SetPrivateField(stage, "_skeleton", skeleton.Object);
            ReflectionHelpers.SetPrivateField(stage, "_font", font.Object);
            // Graphics fields nulled (need a GraphicsDevice to construct); their null-conditional
            // dispose paths are exercised via the null check.
            ReflectionHelpers.SetPrivateField(stage, "_renderer", null);
            ReflectionHelpers.SetPrivateField(stage, "_whitePixel", null);
            ReflectionHelpers.SetPrivateField(stage, "_spriteBatch", null);

            stage.Dispose();

            background.Verify(t => t.RemoveReference(), Times.Once);
            skeleton.Verify(t => t.RemoveReference(), Times.Once);
            font.Verify(f => f.RemoveReference(), Times.Once);
            Assert.Null(ReflectionHelpers.GetPrivateField<ITexture?>(stage, "_background"));
            Assert.Null(ReflectionHelpers.GetPrivateField<ITexture?>(stage, "_skeleton"));
            Assert.Null(ReflectionHelpers.GetPrivateField<IFont?>(stage, "_font"));
            Assert.Null(ReflectionHelpers.GetPrivateField<IResourceManager>(stage, "_resourceManager"));
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

            public int GetMidiVelocityThreshold(int noteNumber) => 0;

            public void SetMidiVelocityThreshold(int noteNumber, int threshold) { }

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
