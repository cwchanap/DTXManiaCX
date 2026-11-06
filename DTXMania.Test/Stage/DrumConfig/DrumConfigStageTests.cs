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
using Moq;
using Xunit;
using ButtonState = DTXMania.Game.Lib.Input.ButtonState;
using L = DTXMania.Game.Lib.UI.Layout.DrumConfigUILayout;
using XnaButtonState = Microsoft.Xna.Framework.Input.ButtonState;

namespace DTXMania.Test.Stage.DrumConfig
{
    [Trait("Category", "Unit")]
    [Collection("AppPaths")]
    public class DrumConfigStageTests
    {
        private const int ResetFocus = 10;

        // ---- Harness ----

        /// <summary>
        /// Stage wired like OnActivate (minus graphics): a real ConfigManager and an input manager
        /// whose runtime bindings mirror Config through the change events. Commands come from
        /// <see cref="FakeInput"/>; pressed buttons are passed straight to the step methods.
        /// </summary>
        private static (DrumConfigStage stage, ConfigManager cm, FakeInput input) CreateStage()
        {
            var cm = new ConfigManager();
            var input = new FakeInput(cm);
            var game = ReflectionHelpers.CreateGame();
            var device = ReflectionHelpers.CreateUninitialized<GraphicsDevice>();
            ReflectionHelpers.SetPrivateField(device, "_viewport", new Viewport(0, 0, 1280, 720));
            GC.SuppressFinalize(device);
            ReflectionHelpers.SetPrivateField(game, "_graphicsDeviceService", new StubGraphicsDeviceService(device));
            ReflectionHelpers.SetProperty(game, nameof(BaseGame.ConfigManager), cm);
            ReflectionHelpers.SetProperty(game, nameof(BaseGame.InputManager), input);
            var stage = new DrumConfigStage(game);
            ReflectionHelpers.SetPrivateField(stage, "_input", input);
            ReflectionHelpers.InvokePrivateMethod(stage, "SelectLane", 0, 0);
            ReflectionHelpers.InvokePrivateMethod(stage, "RebuildRows");
            return (stage, cm, input);
        }

        private static void Browse(DrumConfigStage stage, params string[] pressed) =>
            ReflectionHelpers.InvokePrivateMethod(stage, "StepBrowsing", Buttons(pressed), false, null!);

        private static void Listen(DrumConfigStage stage, params string[] pressed) =>
            ReflectionHelpers.InvokePrivateMethod(stage, "StepListening", Buttons(pressed), false, false, null!);

        private static void Click(DrumConfigStage stage, Point p)
        {
            ReflectionHelpers.SetPrivateField(stage, "_previousMouse", MouseAt(p.X, p.Y, false));
            ReflectionHelpers.InvokePrivateMethod(stage, "Step", 0.016, MouseAt(p.X, p.Y, true));
        }

        private static IReadOnlyList<ButtonState> Buttons(params string[] ids)
        {
            var list = new List<ButtonState>();
            foreach (var id in ids)
                list.Add(new ButtonState(id, true, 0.75f));
            return list;
        }

        private static MouseState MouseAt(int x, int y, bool leftDown, bool rightDown = false) =>
            new MouseState(x, y, 0,
                leftDown ? XnaButtonState.Pressed : XnaButtonState.Released,
                XnaButtonState.Released,
                rightDown ? XnaButtonState.Pressed : XnaButtonState.Released,
                XnaButtonState.Released,
                XnaButtonState.Released);

        private static int Focus(DrumConfigStage stage) => ReflectionHelpers.GetPrivateField<int>(stage, "_focusIndex");
        private static int Chip(DrumConfigStage stage) => ReflectionHelpers.GetPrivateField<int>(stage, "_chipIndex");
        private static DrumCaptureSession Capture(DrumConfigStage stage) =>
            ReflectionHelpers.GetPrivateField<DrumCaptureSession>(stage, "_capture")!;
        private static string? Notice(DrumConfigStage stage) => ReflectionHelpers.GetPrivateField<string?>(stage, "_notice");
        private static DrumStatusLine Status(DrumConfigStage stage) =>
            ReflectionHelpers.InvokePrivateMethod<DrumStatusLine>(stage, "BuildStatusLine");
        private static double[] FlashTimers(DrumConfigStage stage) =>
            ReflectionHelpers.GetPrivateField<double[]>(stage, "_flashTimers")!;
        private static string?[] FlashTexts(DrumConfigStage stage) =>
            ReflectionHelpers.GetPrivateField<string?[]>(stage, "_flashTexts")!;

        private static void StartListening(DrumConfigStage stage, int lane)
        {
            ReflectionHelpers.InvokePrivateMethod(stage, "SelectLane", lane, 0);
            ReflectionHelpers.InvokePrivateMethod(stage, "StartListening", lane);
        }

        // ---- Focus and navigation ----

        [Fact]
        public void MoveDown_FollowsLaneOrder_AndFocusesTheFirstChip()
        {
            var (stage, _, input) = CreateStage();
            input.ActiveCommand = InputCommandType.MoveDown;

            Browse(stage);

            Assert.Equal(1, Focus(stage));
            Assert.Equal(0, Chip(stage));
        }

        [Fact]
        public void MoveUp_FromTheFirstLane_WrapsToReset()
        {
            var (stage, _, input) = CreateStage();
            input.ActiveCommand = InputCommandType.MoveUp;

            Browse(stage);

            Assert.Equal(ResetFocus, Focus(stage));
            Assert.Equal(-1, Chip(stage));
        }

        [Fact]
        public void TenTabsInOneFrame_WalkFromTheFirstLaneToReset()
        {
            // A slow E2E frame can drain several injected Tabs at once; each one is a step.
            var (stage, _, _) = CreateStage();

            Browse(stage, "Key.Tab", "Key.Tab", "Key.Tab", "Key.Tab", "Key.Tab",
                "Key.Tab", "Key.Tab", "Key.Tab", "Key.Tab", "Key.Tab");

            Assert.Equal(ResetFocus, Focus(stage));
        }

        [Fact]
        public void TabFromTheDrainedFeed_AdvancesFocus()
        {
            var (stage, _, input) = CreateStage();
            input.ModularInputManager.InjectButton("Key.Tab", isPressed: true);
            input.ModularInputManager.InjectButton("Key.Tab", isPressed: false);
            input.ModularInputManager.Update(); // press and release drain in one frame

            ReflectionHelpers.InvokePrivateMethod(stage, "Step", 0.016, MouseAt(0, 0, false));

            Assert.Equal(1, Focus(stage));
        }

        [Fact]
        public void LeftRight_MoveTheChipFocusWithinTheLane()
        {
            var (stage, _, input) = CreateStage();
            ReflectionHelpers.InvokePrivateMethod(stage, "ApplyCapture", "MIDI.49", 0);
            ReflectionHelpers.InvokePrivateMethod(stage, "SelectLane", 0, 0);

            input.ActiveCommand = InputCommandType.MoveRight;
            Browse(stage);
            Browse(stage); // clamps at the last chip
            Assert.Equal(1, Chip(stage));

            input.ActiveCommand = InputCommandType.MoveLeft;
            Browse(stage);
            Browse(stage);
            Assert.Equal(0, Chip(stage));
        }

        [Fact]
        public void Activate_OnALane_StartsListeningThere()
        {
            var (stage, _, input) = CreateStage();
            ReflectionHelpers.InvokePrivateMethod(stage, "SelectLane", 4, 0);
            input.ActiveCommand = InputCommandType.Activate;

            Browse(stage);

            Assert.True(Capture(stage).IsListening);
            Assert.Equal(4, Capture(stage).Lane);
        }

        [Fact]
        public void Activate_OnReset_RestoresDefaultBindings()
        {
            var (stage, cm, input) = CreateStage();
            ReflectionHelpers.InvokePrivateMethod(stage, "ApplyCapture", "Key.Z", 3);
            ReflectionHelpers.SetPrivateField(stage, "_focusIndex", ResetFocus);
            input.ActiveCommand = InputCommandType.Activate;

            Browse(stage);

            Assert.False(cm.Config.KeyBindings.ContainsKey("Key.Z"));
            Assert.Equal(3, cm.Config.KeyBindings["Key.S"]);
            Assert.False(Capture(stage).IsListening);
        }

        [Fact]
        public void Back_WhileBrowsing_ReturnsToConfig()
        {
            var (stage, _, input) = CreateStage();
            var stageManager = new Mock<IStageManager>();
            stage.StageManager = stageManager.Object;
            input.BackTriggered = true;

            Browse(stage);

            stageManager.Verify(m => m.ChangeStage(StageType.Config, It.IsAny<IStageTransition>()), Times.Once);
        }

        // ---- Chip edits ----

        [Fact]
        public void Delete_RemovesTheFocusedChip()
        {
            var (stage, cm, _) = CreateStage();
            ReflectionHelpers.InvokePrivateMethod(stage, "ApplyCapture", "MIDI.38", 3);
            ReflectionHelpers.InvokePrivateMethod(stage, "SelectLane", 3, 1); // KEY S, [MIDI 38]

            Browse(stage, "Key.Delete");

            Assert.False(cm.Config.KeyBindings.ContainsKey("MIDI.38"));
            Assert.Equal(3, cm.Config.KeyBindings["Key.S"]);
            Assert.Equal(0, Chip(stage));
            Assert.Contains("Removed MIDI 38 from Snare", Notice(stage));
        }

        [Fact]
        public void PlusMinus_AdjustTheFocusedMidiChipsVelocityThreshold()
        {
            var (stage, cm, _) = CreateStage();
            ReflectionHelpers.InvokePrivateMethod(stage, "ApplyCapture", "MIDI.38", 3);
            ReflectionHelpers.InvokePrivateMethod(stage, "SelectLane", 3, 1);

            Browse(stage, "Key.OemPlus", "Key.Add", "Key.OemPlus");
            Assert.Equal(3, cm.GetMidiVelocityThreshold(38));

            Browse(stage, "Key.OemMinus", "Key.Subtract", "Key.OemMinus", "Key.OemMinus");
            Assert.Equal(0, cm.GetMidiVelocityThreshold(38)); // clamps at 0
        }

        [Fact]
        public void PlusMinus_OnAKeyChip_DoesNothing()
        {
            var (stage, cm, _) = CreateStage();
            ReflectionHelpers.InvokePrivateMethod(stage, "SelectLane", 3, 0); // KEY S

            Browse(stage, "Key.OemPlus");

            Assert.Empty(cm.Config.MidiVelocityThresholds);
        }

        [Fact]
        public void ABoundKey_OnlyFlashesItsLane_NeverEdits()
        {
            // Delete bound as a drum key must light its lane, not remove the focused chip.
            var (stage, cm, _) = CreateStage();
            ReflectionHelpers.InvokePrivateMethod(stage, "ApplyCapture", "Key.Delete", 2);
            ReflectionHelpers.InvokePrivateMethod(stage, "SelectLane", 3, 0);

            Browse(stage, "Key.Delete");

            Assert.Equal(3, cm.Config.KeyBindings["Key.S"]);
            Assert.True(FlashTimers(stage)[2] > 0);
            Assert.Equal("HIT  KEY Delete", FlashTexts(stage)[2]);
        }

        [Fact]
        public void ABoundMidiNote_FlashesWithItsVelocity_AndTheFlashExpires()
        {
            var (stage, _, _) = CreateStage();
            ReflectionHelpers.InvokePrivateMethod(stage, "ApplyCapture", "MIDI.38", 3);

            Browse(stage, "MIDI.38"); // velocity 0.75 -> 95

            Assert.True(FlashTimers(stage)[3] > 0);
            Assert.Equal("HIT  MIDI 38  VEL 95", FlashTexts(stage)[3]);

            ReflectionHelpers.InvokePrivateMethod(stage, "TickTimers", L.HitFlashSeconds + 0.01);
            Assert.Equal(0, FlashTimers(stage)[3]);
        }

        [Fact]
        public void PadsHitInTheSameUpdate_EachFlashTheirOwnLane()
        {
            var (stage, _, _) = CreateStage();
            ReflectionHelpers.InvokePrivateMethod(stage, "ApplyCapture", "MIDI.36", 5);
            ReflectionHelpers.InvokePrivateMethod(stage, "ApplyCapture", "MIDI.42", 1);

            Browse(stage, "MIDI.36", "MIDI.42"); // kick + hi-hat together

            Assert.True(FlashTimers(stage)[5] > 0);
            Assert.True(FlashTimers(stage)[1] > 0);
            Assert.Equal("HIT  MIDI 36  VEL 95", FlashTexts(stage)[5]);
            Assert.Equal("HIT  MIDI 42  VEL 95", FlashTexts(stage)[1]);
        }

        [Fact]
        public void AnUnboundMidiNote_SaysSoInTheStatusLine()
        {
            var (stage, _, _) = CreateStage();

            Browse(stage, "MIDI.40");

            Assert.Equal(new DrumStatusLine("MIDI 40 is not bound to a lane.", DrumStatusTone.Hint), Status(stage));
        }

        // ---- Listening ----

        [Fact]
        public void Listening_AppendsEveryHitToTheLane_UntilBack()
        {
            var (stage, cm, input) = CreateStage();
            StartListening(stage, 1);

            Listen(stage, "MIDI.42");
            Listen(stage, "MIDI.46");

            Assert.Equal(1, cm.Config.KeyBindings["MIDI.42"]);
            Assert.Equal(1, cm.Config.KeyBindings["MIDI.46"]);
            Assert.True(Capture(stage).IsListening);
            Assert.Equal(2, Chip(stage)); // KEY J, MIDI 42, [MIDI 46]
            Assert.Contains("Added MIDI 46 to Hi-Hat", Notice(stage));

            input.BackTriggered = true;
            Listen(stage);

            Assert.False(Capture(stage).IsListening);
        }

        [Fact]
        public void Listening_ARequiredSystemKey_IsRejected()
        {
            var (stage, cm, _) = CreateStage();
            StartListening(stage, 3);

            Listen(stage, "Key.Enter");

            Assert.False(cm.Config.KeyBindings.ContainsKey("Key.Enter"));
            var status = Status(stage);
            Assert.Equal(DrumStatusTone.Danger, status.Tone);
            Assert.Contains("reserved", status.Text);
        }

        [Fact]
        public void Listening_AButtonFromAnotherLane_MovesIt()
        {
            var (stage, cm, _) = CreateStage();
            StartListening(stage, 1);

            Listen(stage, "Key.S"); // default Snare key

            Assert.Equal(1, cm.Config.KeyBindings["Key.S"]);
            Assert.Contains("Moved KEY S from Snare to Hi-Hat", Notice(stage));
        }

        [Fact]
        public void Listening_CapturesThePressRightAfterActivate()
        {
            var (stage, cm, input) = CreateStage();
            ReflectionHelpers.InvokePrivateMethod(stage, "SelectLane", 2, 0);

            // The activating Enter is spent by the browsing update that starts listening...
            input.ActiveCommand = InputCommandType.Activate;
            Browse(stage, "Key.Enter");
            input.ActiveCommand = null;
            Assert.True(Capture(stage).IsListening);
            Assert.False(cm.Config.KeyBindings.ContainsKey("Key.Enter"));

            // ...so the very next update captures.
            Listen(stage, "Key.Q");
            Assert.Equal(2, cm.Config.KeyBindings["Key.Q"]);
        }

        [Fact]
        public void Listening_RightClickStops()
        {
            var (stage, _, _) = CreateStage();
            StartListening(stage, 2);

            ReflectionHelpers.InvokePrivateMethod(stage, "StepListening", Buttons(), false, true, null!);

            Assert.False(Capture(stage).IsListening);
        }

        [Fact]
        public void StatusLine_WhileListening_NamesTheLane()
        {
            var (stage, _, _) = CreateStage();
            StartListening(stage, 0);

            var status = Status(stage);

            Assert.Equal(DrumStatusTone.Accent, status.Tone);
            Assert.Equal("Left Cymbal", status.Emphasis);
        }

        // ---- Mouse ----

        [Fact]
        public void ClickOnARow_SelectsItAndListens()
        {
            var (stage, _, _) = CreateStage();
            var row = L.GetRowRect(6);

            Click(stage, new Point(row.X + 100, row.Center.Y)); // the name, not a chip

            Assert.Equal(6, Focus(stage));
            Assert.True(Capture(stage).IsListening);
            Assert.Equal(6, Capture(stage).Lane);
        }

        [Fact]
        public void ClickOnAPiece_SelectsItsLaneAndListens()
        {
            var (stage, _, _) = CreateStage();
            var kick = DrumKitLayout.Pieces[5].Bounds;

            Click(stage, kick.Center);

            Assert.Equal(5, Focus(stage));
            Assert.Equal(5, Capture(stage).Lane);
        }

        [Fact]
        public void ClickOnAChip_FocusesIt_ThenItsRemoveControlRemovesIt()
        {
            var (stage, cm, _) = CreateStage();
            ReflectionHelpers.InvokePrivateMethod(stage, "ApplyCapture", "MIDI.38", 3);
            ReflectionHelpers.InvokePrivateMethod(stage, "RebuildRows");
            var rows = ReflectionHelpers.GetPrivateField<IReadOnlyList<DrumBindingRow>>(stage, "_rows")!;

            Click(stage, rows[3].Chips[1].Bounds.Center);

            Assert.Equal(3, Focus(stage));
            Assert.Equal(1, Chip(stage));
            Assert.False(Capture(stage).IsListening);

            rows = ReflectionHelpers.GetPrivateField<IReadOnlyList<DrumBindingRow>>(stage, "_rows")!;
            Click(stage, rows[3].Chips[1].Remove.Center);

            Assert.False(cm.Config.KeyBindings.ContainsKey("MIDI.38"));
        }

        [Fact]
        public void ClickOnReset_RestoresDefaults()
        {
            var (stage, cm, _) = CreateStage();
            ReflectionHelpers.InvokePrivateMethod(stage, "ApplyCapture", "Key.Z", 3);

            Click(stage, L.ResetButton.Center);

            Assert.False(cm.Config.KeyBindings.ContainsKey("Key.Z"));
            Assert.Equal(ResetFocus, Focus(stage));
        }

        // ---- MIDI device status ----

        [Theory]
        [InlineData(new string[0], "No device (keyboard works)", false)]
        [InlineData(new[] { "Roland TD-17" }, "Roland TD-17", true)]
        [InlineData(new[] { "Alpha Kit", "Beta Kit", "Zeta Kit" }, "Alpha Kit +2 more", true)]
        public void FormatMidiDeviceStatus_SummarizesDevices(string[] names, string text, bool connected)
        {
            var method = typeof(DrumConfigStage).GetMethod("FormatMidiDeviceStatus", BindingFlags.NonPublic | BindingFlags.Static)!;

            var result = ((string Text, bool Connected))method.Invoke(null, new object[] { names })!;

            Assert.Equal((text, connected), result);
        }

        // ---- Edit helpers (persist-on-edit: edits mutate Config via the live-apply setters) ----

        [Fact]
        public void ApplyCapture_BindsButtonToLaneInConfig()
        {
            var (stage, cm, input) = CreateStage();

            ReflectionHelpers.InvokePrivateMethod(stage, "ApplyCapture", "Key.Q", 5);

            Assert.Equal(5, cm.Config.KeyBindings["Key.Q"]);
            Assert.Equal(5, input.ModularInputManager.KeyBindings.GetLane("Key.Q"));
        }

        [Fact]
        public void ApplyCapture_EvictsTheCapturedSystemKeyOnly()
        {
            // Capture-time eviction (Decision 3): a keyboard key claimed by a drum lane leaves the
            // system map now; other system keys stay.
            var (stage, cm, _) = CreateStage();
            cm.SetSystemKeyBindings(new Dictionary<Keys, InputCommandType>
            {
                [Keys.PageUp] = InputCommandType.IncreaseScrollSpeed,
                [Keys.PageDown] = InputCommandType.DecreaseScrollSpeed,
            });

            ReflectionHelpers.InvokePrivateMethod(stage, "ApplyCapture", "Key.PageUp", 7);

            Assert.Equal("", cm.Config.SystemKeyBindings["SystemKey.IncreaseScrollSpeed"]);
            Assert.Equal("PageDown", cm.Config.SystemKeyBindings["SystemKey.DecreaseScrollSpeed"]);
            Assert.Equal(7, cm.Config.KeyBindings["Key.PageUp"]);
        }

        [Fact]
        public void RemoveBindingFromConfig_DropsTheMidiVelocityThreshold_SoTheNoteCanBeCapturedAgain()
        {
            // A 127 threshold filters every hit; left on an unbound note (no chip, so no control)
            // it would block recapturing the pad.
            var (stage, cm, input) = CreateStage();
            ReflectionHelpers.InvokePrivateMethod(stage, "ApplyCapture", "MIDI.36", 4);
            cm.SetMidiVelocityThreshold(36, 127);

            ReflectionHelpers.InvokePrivateMethod(stage, "RemoveBindingFromConfig", "MIDI.36");

            Assert.False(cm.Config.KeyBindings.ContainsKey("MIDI.36"));
            Assert.Equal(-1, input.ModularInputManager.KeyBindings.GetLane("MIDI.36"));
            Assert.Equal(0, cm.GetMidiVelocityThreshold(36));
        }

        [Fact]
        public void Reset_DropsThresholdsOfTheNotesItUnbinds()
        {
            var (stage, cm, _) = CreateStage();
            ReflectionHelpers.InvokePrivateMethod(stage, "ApplyCapture", "MIDI.99", 4);
            cm.SetMidiVelocityThreshold(99, 127);

            ReflectionHelpers.InvokePrivateMethod(stage, "ResetDrumBindingsToDefault");

            Assert.False(cm.Config.KeyBindings.ContainsKey("MIDI.99"));
            Assert.Equal(0, cm.GetMidiVelocityThreshold(99));
        }

        [Fact]
        public void AdjustMidiVelocityThreshold_ClampsTo127()
        {
            var (stage, cm, _) = CreateStage();
            cm.SetMidiVelocityThreshold(36, 126);

            ReflectionHelpers.InvokePrivateMethod(stage, "AdjustMidiVelocityThreshold", 36, 1);
            ReflectionHelpers.InvokePrivateMethod(stage, "AdjustMidiVelocityThreshold", 36, 1);

            Assert.Equal(127, cm.GetMidiVelocityThreshold(36));
        }

        [Fact]
        public void ExitStage_FlushesPendingSaveOnConcreteConfigManager()
        {
            // Point the config store at a real temp dir BEFORE creating the manager (its default
            // paths resolve eagerly in the constructor).
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            var previousRoot = Environment.GetEnvironmentVariable("DTXMANIA_APPDATA_ROOT");
            Environment.SetEnvironmentVariable("DTXMANIA_APPDATA_ROOT", tempDir);
            try
            {
                var (stage, cm, _) = CreateStage();
                cm.LoadConfig();
                ReflectionHelpers.InvokePrivateMethod(stage, "ApplyCapture", "Key.Q", 5); // marks dirty
                Assert.True(ReflectionHelpers.GetPrivateField<bool>(cm, "_hasPendingSave"));

                ReflectionHelpers.InvokePrivateMethod(stage, "ExitStage");

                Assert.False(ReflectionHelpers.GetPrivateField<bool>(cm, "_hasPendingSave"));
                Assert.True(File.Exists(AppPaths.GetConfigDatabasePath()));
            }
            finally
            {
                Environment.SetEnvironmentVariable("DTXMANIA_APPDATA_ROOT", previousRoot);
                // Release pooled config.db handles before deleting the temp directory (Windows
                // keeps the file locked otherwise).
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                Directory.Delete(tempDir, recursive: true);
            }
        }

        [Fact]
        public void CapturingAKey_NeverDropsAnUnrelatedRequiredSystemKey()
        {
            // Comment-1 regression: capturing one key must not evict another system key, and a
            // required key (here Z = MoveUp) can never be captured, so it can never evict itself.
            var (stage, cm, _) = CreateStage();
            cm.SetSystemKeyBindings(new Dictionary<Keys, InputCommandType> { [Keys.Z] = InputCommandType.MoveUp });
            StartListening(stage, 5);

            Listen(stage, "Key.Q");
            Listen(stage, "Key.Z");

            Assert.Equal("Z", cm.Config.SystemKeyBindings["SystemKey.MoveUp"]);
            Assert.Equal(5, cm.Config.KeyBindings["Key.Q"]);
            Assert.False(cm.Config.KeyBindings.ContainsKey("Key.Z"));
        }

        // ---- Fakes ----

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

        /// <summary>InputManagerCompat with canned command answers (no live keyboard).</summary>
        private sealed class FakeInput : InputManagerCompat
        {
            public bool BackTriggered;
            public InputCommandType? ActiveCommand;
            public FakeInput(ConfigManager config) : base(config, new TestMidiDeviceBackend()) { }
            public override bool IsBackActionTriggered() => BackTriggered;
            public override bool IsCommandPressed(InputCommandType command) => command == ActiveCommand;
        }
    }
}
