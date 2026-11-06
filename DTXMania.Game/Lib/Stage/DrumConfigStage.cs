#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Stage.DrumConfig;
using L = DTXMania.Game.Lib.UI.Layout.DrumConfigUILayout;
using InputButtonState = DTXMania.Game.Lib.Input.ButtonState;
using XnaButtonState = Microsoft.Xna.Framework.Input.ButtonState;

namespace DTXMania.Game.Lib.Stage
{
    /// <summary>
    /// Drum Key Mapping page: the kit on the left, one bindings row per lane on the right.
    /// Enter (or a click on a row or piece) listens on the selected lane and every key, pad or
    /// MIDI note hit is appended to it until Esc. Left/Right focus a binding chip, Del removes
    /// it, -/+ change a MIDI chip's velocity threshold. While not listening, a hit on a bound
    /// input lights its lane (a live hit test). Config is the single source of truth: every
    /// edit goes straight through ConfigManager (live-apply + deferred save) and Back flushes
    /// the save on the way out. There is no working copy.
    /// </summary>
    public class DrumConfigStage : BaseStage
    {
        private const int ResetFocusIndex = L.LaneCount;
        private const int FocusCount = L.LaneCount + 1;

        private readonly IConfigManager _configManager;
        private readonly DrumCaptureSession _capture;
        private InputManagerCompat? _input;

        // NullLogger until OnActivate swaps in the game's factory, so reflection-based tests that
        // skip OnActivate can drive the stage.
        private ILogger<DrumConfigStage> _logger = NullLogger<DrumConfigStage>.Instance;

        private SpriteBatch? _spriteBatch;
        private DrumConfigRenderer? _renderer;

        // Focus: 0..9 = lane rows (gameplay order), 10 = Reset. _chipIndex is the focused binding
        // in the focused lane's sorted list (-1 when the lane has none).
        private int _focusIndex;
        private int _chipIndex = -1;
        private int _hoveredLane = -1;
        private bool _resetHovered;

        // Live hit test: each lane lights with its own last hit for HitFlashSeconds, so pads hit
        // together (kick + hi-hat) all show.
        private readonly double[] _flashTimers = new double[L.LaneCount];
        private readonly string?[] _flashTexts = new string?[L.LaneCount];

        // Status-line notice (added, removed, reset, unbound note), shown for NoticeSeconds.
        private string? _notice;
        private DrumStatusTone _noticeTone;
        private double _noticeTimer;

        private MouseState _previousMouse;
        private IReadOnlyList<DrumBindingRow> _rows = Array.Empty<DrumBindingRow>();

        public override StageType Type => StageType.DrumConfig;

        public DrumConfigStage(IStageGame game) : base(game)
        {
            _configManager = game.ConfigManager ?? throw new InvalidOperationException("ConfigManager not found");
            // The capture checks reserved keys against the RUNTIME system map, which always
            // mirrors Config (ConfigManager.SystemKeyBindingsChanged -> InputManagerCompat reload).
            _capture = new DrumCaptureSession(() =>
                _input?.GetKeyMappingSnapshot() ?? new Dictionary<Keys, InputCommandType>());
        }

        [ExcludeFromCodeCoverage]
        protected override void OnActivate()
        {
            _logger = _game.LoggerFactory?.CreateLogger<DrumConfigStage>() ?? _logger;
            _spriteBatch = new SpriteBatch(_game.GraphicsDevice);
            _renderer = new DrumConfigRenderer(_game.GraphicsDevice, _game.ResourceManager, _logger);
            _input = _game.InputManager; // IStageGame.InputManager is concretely InputManagerCompat

            _capture.Stop();
            SelectLane(0);
            _hoveredLane = -1;
            _resetHovered = false;
            Array.Clear(_flashTimers);
            Array.Clear(_flashTexts);
            _notice = null;
            _noticeTimer = 0;
            _previousMouse = Mouse.GetState();
            RebuildRows();
        }

        [ExcludeFromCodeCoverage]
        protected override void OnUpdate(double deltaTime) => Step(deltaTime, Mouse.GetState());

        /// <summary>
        /// One frame of input. GraphicsDevice-free (the mouse state is passed in) so the whole
        /// page flow is unit-testable headlessly.
        /// </summary>
        private void Step(double deltaTime, MouseState mouse)
        {
            TickTimers(deltaTime);

            bool leftClick = mouse.LeftButton == XnaButtonState.Pressed
                             && _previousMouse.LeftButton == XnaButtonState.Released;
            bool rightClick = mouse.RightButton == XnaButtonState.Pressed
                              && _previousMouse.RightButton == XnaButtonState.Released;
            _previousMouse = mouse;

            // The page is drawn into the 1280x720 virtual target and letterboxed, so map the window
            // mouse into virtual space. A point on the letterbox bars maps to null (no hit).
            var point = _game.MapMouseToVirtual(mouse.Position);
            _hoveredLane = point is { } hp ? HitTestLane(hp) : -1;
            _resetHovered = point is { } rp && L.ResetButton.Contains(rp);

            // Every press this frame (keys, pads, MIDI), including MCP/E2E-injected ones whose
            // release lands in the same frame. Drained once and shared by both modes.
            var pressed = _input?.ModularInputManager.ConsumePressedButtons()
                          ?? Array.Empty<InputButtonState>();

            if (_capture.IsListening)
                StepListening(pressed, leftClick, rightClick, point);
            else
                StepBrowsing(pressed, leftClick, point);

            RebuildRows();
        }

        private void StepBrowsing(IReadOnlyList<InputButtonState> pressed, bool leftClick, Point? point)
        {
            int tabs = 0;
            int velocityDelta = 0;
            bool remove = false;
            var drumMap = _input?.ModularInputManager.KeyBindings.ButtonToLane;
            foreach (var button in pressed)
            {
                // A bound input only lights its lane, so a drum key never doubles as an edit key.
                if (drumMap != null && drumMap.TryGetValue(button.Id, out var lane))
                {
                    Flash(lane, button);
                    continue;
                }

                switch (button.Id)
                {
                    // Tab is read from the pressed feed (not IsKeyPressed) so every injected
                    // press counts even when its release drains in the same frame; each press is
                    // one focus step (the E2E reset walk sends ten in a row).
                    case "Key.Tab": tabs++; break;
                    case "Key.Delete": remove = true; break;
                    case "Key.OemMinus": case "Key.Subtract": velocityDelta--; break;
                    case "Key.OemPlus": case "Key.Add": velocityDelta++; break;
                    default:
                        if (KeyBindings.TryParseMidiButtonId(button.Id, out var note))
                            ShowNotice($"MIDI {note} is not bound to a lane.", DrumStatusTone.Hint);
                        break;
                }
            }

            if (_input?.IsBackActionTriggered() == true)
            {
                ExitStage();
                return;
            }

            if (_input?.IsCommandPressed(InputCommandType.MoveDown) == true)
                MoveFocus(1);
            else if (_input?.IsCommandPressed(InputCommandType.MoveUp) == true)
                MoveFocus(-1);
            for (int i = 0; i < tabs; i++)
                MoveFocus(1);

            if (_input?.IsCommandPressed(InputCommandType.MoveRight) == true)
                MoveChip(1);
            else if (_input?.IsCommandPressed(InputCommandType.MoveLeft) == true)
                MoveChip(-1);

            if (remove)
                RemoveFocusedChip();
            if (velocityDelta != 0)
                AdjustFocusedChipVelocity(velocityDelta);

            if (leftClick && point is { } p && HandleBrowseClick(p))
                return;

            if (_input?.IsCommandPressed(InputCommandType.Activate) == true)
            {
                if (_focusIndex == ResetFocusIndex)
                    ResetDrumBindingsToDefault();
                else
                    StartListening(_focusIndex);
            }
        }

        /// <summary>Mouse click while browsing; true when the click hit something.</summary>
        private bool HandleBrowseClick(Point p)
        {
            if (L.ResetButton.Contains(p))
            {
                _focusIndex = ResetFocusIndex;
                ResetDrumBindingsToDefault();
                return true;
            }

            int rowLane = DrumBindingList.HitTestRow(p.X, p.Y);
            if (rowLane >= 0)
            {
                // Hit-test the row as it was last drawn, so a click lands on what the user saw.
                if (rowLane < _rows.Count && DrumBindingList.HitTestChip(_rows[rowLane], p.X, p.Y) is { } chip
                    && chip.Kind != DrumChipKind.Listening)
                {
                    SelectLane(rowLane, chip.Index);
                    if (chip.Remove.Contains(p))
                        RemoveFocusedChip();
                    else if (chip.Decrement.Contains(p))
                        AdjustFocusedChipVelocity(-1);
                    else if (chip.Increment.Contains(p))
                        AdjustFocusedChipVelocity(1);
                    return true;
                }

                SelectLane(rowLane);
                StartListening(rowLane);
                return true;
            }

            int pieceLane = DrumKitLayout.HitTest(p.X, p.Y);
            if (pieceLane >= 0)
            {
                SelectLane(pieceLane);
                StartListening(pieceLane);
                return true;
            }
            return false;
        }

        private void StepListening(IReadOnlyList<InputButtonState> pressed, bool leftClick, bool rightClick, Point? point)
        {
            if (rightClick || _input?.IsBackActionTriggered() == true)
            {
                StopListening();
                return;
            }

            if (leftClick && point is { } p)
            {
                if (L.ResetButton.Contains(p))
                {
                    StopListening();
                    _focusIndex = ResetFocusIndex;
                    ResetDrumBindingsToDefault();
                    return;
                }

                int lane = HitTestLane(p);
                if (lane >= 0 && lane != _capture.Lane)
                {
                    SelectLane(lane);
                    StartListening(lane);
                    return;
                }
            }

            foreach (var button in pressed)
            {
                int lane = _capture.Lane;
                var outcome = _capture.TryCapture(button);
                if (outcome == DrumCaptureOutcome.Captured)
                    OnCaptured(button, lane);
            }
        }

        private void OnCaptured(InputButtonState button, int lane)
        {
            int previousLane = _input?.ModularInputManager.KeyBindings.GetLane(button.Id) ?? -1;
            ApplyCapture(button.Id, lane);
            Flash(lane, button);

            var label = FormatButton(button.Id);
            if (previousLane == lane)
                ShowNotice($"{label} is already on {KeyBindings.GetLaneName(lane)}.", DrumStatusTone.Hint);
            else if (previousLane >= 0)
                ShowNotice($"Moved {label} from {KeyBindings.GetLaneName(previousLane)} to {KeyBindings.GetLaneName(lane)}.", DrumStatusTone.Success);
            else
                ShowNotice($"Added {label} to {KeyBindings.GetLaneName(lane)}.", DrumStatusTone.Success);

            var bindings = GetBindings(lane);
            _chipIndex = IndexOf(bindings, button.Id);
        }

        // ---- Focus ----

        private void SelectLane(int lane, int chipIndex = 0)
        {
            _focusIndex = lane;
            int count = GetBindings(lane).Count;
            _chipIndex = count == 0 ? -1 : Math.Clamp(chipIndex, 0, count - 1);
        }

        private void MoveFocus(int delta)
        {
            int next = ((_focusIndex + delta) % FocusCount + FocusCount) % FocusCount;
            if (next == ResetFocusIndex)
            {
                _focusIndex = ResetFocusIndex;
                _chipIndex = -1;
            }
            else
            {
                SelectLane(next);
            }
        }

        private void MoveChip(int delta)
        {
            if (_focusIndex == ResetFocusIndex)
                return;
            int count = GetBindings(_focusIndex).Count;
            if (count > 0)
                _chipIndex = Math.Clamp(_chipIndex + delta, 0, count - 1);
        }

        private string? FocusedButtonId()
        {
            if (_focusIndex == ResetFocusIndex || _chipIndex < 0)
                return null;
            var bindings = GetBindings(_focusIndex);
            return _chipIndex < bindings.Count ? bindings[_chipIndex] : null;
        }

        private void StartListening(int lane)
        {
            _capture.Start(lane);
            _notice = null;
        }

        private void StopListening()
        {
            _capture.Stop();
        }

        private int HitTestLane(Point p)
        {
            int lane = DrumBindingList.HitTestRow(p.X, p.Y);
            return lane >= 0 ? lane : DrumKitLayout.HitTest(p.X, p.Y);
        }

        // ---- Chip edits ----

        private void RemoveFocusedChip()
        {
            var id = FocusedButtonId();
            if (id == null)
                return;
            int lane = _focusIndex;
            RemoveBindingFromConfig(id);
            ShowNotice($"Removed {FormatButton(id)} from {KeyBindings.GetLaneName(lane)}.", DrumStatusTone.Hint);
            SelectLane(lane, _chipIndex);
        }

        private void AdjustFocusedChipVelocity(int delta)
        {
            var id = FocusedButtonId();
            if (id != null && KeyBindings.TryParseMidiButtonId(id, out var note))
                AdjustMidiVelocityThreshold(note, delta);
        }

        // ---- Live hit test and notices ----

        private void Flash(int lane, InputButtonState button)
        {
            if (lane < 0 || lane >= L.LaneCount)
                return;
            _flashTimers[lane] = L.HitFlashSeconds;
            _flashTexts[lane] = FormatHit(button);
        }

        /// <summary>"HIT  KEY A" or "HIT  MIDI 38  VEL 96" (velocity is 0..1 in the feed).</summary>
        private static string FormatHit(InputButtonState button)
        {
            var text = "HIT  " + FormatButton(button.Id);
            if (KeyBindings.TryParseMidiButtonId(button.Id, out _))
                text += $"  VEL {(int)MathF.Round(Math.Clamp(button.Velocity, 0f, 1f) * 127f)}";
            return text;
        }

        private static string FormatButton(string buttonId)
        {
            var (prefix, value) = DrumBindingList.ChipText(buttonId);
            return $"{prefix} {value}";
        }

        private void ShowNotice(string text, DrumStatusTone tone)
        {
            _notice = text;
            _noticeTone = tone;
            _noticeTimer = L.NoticeSeconds;
        }

        private void TickTimers(double deltaTime)
        {
            _capture.Tick(deltaTime);
            for (int lane = 0; lane < L.LaneCount; lane++)
            {
                if (_flashTimers[lane] > 0)
                    _flashTimers[lane] = Math.Max(0, _flashTimers[lane] - deltaTime);
            }
            if (_noticeTimer > 0)
            {
                _noticeTimer = Math.Max(0, _noticeTimer - deltaTime);
                if (_noticeTimer == 0)
                    _notice = null;
            }
        }

        /// <summary>The status line under the bindings list for the current state.</summary>
        private DrumStatusLine BuildStatusLine()
        {
            if (_capture.ConflictMessage is { } conflict)
                return new DrumStatusLine(conflict, DrumStatusTone.Danger);
            if (_notice != null)
                return new DrumStatusLine(_notice, _noticeTone);
            if (_capture.IsListening)
                return new DrumStatusLine("Listening for ", DrumStatusTone.Accent,
                    KeyBindings.GetLaneName(_capture.Lane), ": hit a pad or key to add it. Esc stops.");
            return new DrumStatusLine("Not listening: hit any pad or key to see which lane it lands on.", DrumStatusTone.Hint);
        }

        /// <summary>Header MIDI status: the first device name (plus a count), or none.</summary>
        private static (string Text, bool Connected) FormatMidiDeviceStatus(IReadOnlyList<string>? deviceNames)
        {
            if (deviceNames == null || deviceNames.Count == 0)
                return ("No device (keyboard works)", false);
            if (deviceNames.Count == 1)
                return (deviceNames[0], true);
            return ($"{deviceNames[0]} +{deviceNames.Count - 1} more", true);
        }

        // ---- Rows ----

        private IReadOnlyList<string> GetBindings(int lane) =>
            _input == null
                ? Array.Empty<string>()
                : DrumBindingList.GetBindings(_input.ModularInputManager.KeyBindings.ButtonToLane, lane);

        private static int IndexOf(IReadOnlyList<string> list, string id)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == id)
                    return i;
            }
            return list.Count > 0 ? 0 : -1;
        }

        /// <summary>Lays out all ten rows from the live bindings (shared by Draw and click hit-tests).</summary>
        private void RebuildRows()
        {
            var rows = new DrumBindingRow[L.LaneCount];
            for (int lane = 0; lane < L.LaneCount; lane++)
            {
                rows[lane] = DrumBindingList.LayoutRow(
                    lane,
                    GetBindings(lane),
                    note => _configManager.GetMidiVelocityThreshold(note),
                    lane == _focusIndex ? _chipIndex : -1,
                    _capture.IsListening && _capture.Lane == lane,
                    MeasureMono);
            }
            _rows = rows;
        }

        private float MeasureMono(string text) => _renderer?.MeasureMono(text) ?? text.Length * 8f;

        [ExcludeFromCodeCoverage]
        protected override void OnDraw(double deltaTime)
        {
            if (_spriteBatch == null || _renderer == null)
                return;

            var view = new DrumConfigView
            {
                Rows = _rows,
                SelectedLane = _focusIndex == ResetFocusIndex ? -1 : _focusIndex,
                ResetFocused = _focusIndex == ResetFocusIndex,
                HoveredLane = _hoveredLane,
                ResetHovered = _resetHovered,
                ListeningLane = _capture.IsListening ? _capture.Lane : -1,
                Flash = Array.ConvertAll(_flashTimers, t => (float)(t / L.HitFlashSeconds)),
                FlashText = _flashTexts,
                Status = BuildStatusLine(),
                Midi = FormatMidiDeviceStatus(_input?.ModularInputManager.ConnectedMidiDeviceNames),
            };

            _spriteBatch.Begin();
            _renderer.Draw(_spriteBatch, view);
            _spriteBatch.End();
        }

        // ---- Edit helpers (persist-on-edit: Config is truth, edits apply immediately via setters) ----

        /// <summary>Back = exit. Flushes any pending deferred save, then returns to ConfigStage.</summary>
        private void ExitStage()
        {
            _configManager.FlushPendingSave();
            ChangeStage(StageType.Config, new InstantTransition());
        }

        /// <summary>Binds <paramref name="buttonId"/> to <paramref name="lane"/> in Config, then
        /// immediately evicts that key from the system map if it is a keyboard key (Decision 3:
        /// capture-time eviction is permanent, no restore on undo).</summary>
        private void ApplyCapture(string buttonId, int lane)
        {
            if (_input == null) return;
            var kb = _input.ModularInputManager.KeyBindings.Clone();
            kb.BindButton(buttonId, lane);
            _configManager.SetKeyBindings(kb);
            EvictSystemKey(buttonId);
        }

        private void RemoveBindingFromConfig(string buttonId)
        {
            if (_input == null) return;
            var kb = _input.ModularInputManager.KeyBindings.Clone();
            kb.UnbindButton(buttonId);
            _configManager.SetKeyBindings(kb);
            // No system-key restore (Decision 3): eviction was permanent.
            // An unbound note has no velocity control left, so drop its threshold; otherwise a
            // high one would filter the note out of the next capture.
            if (KeyBindings.TryParseMidiButtonId(buttonId, out var note))
                _configManager.SetMidiVelocityThreshold(note, 0);
        }

        private void AdjustMidiVelocityThreshold(int noteNumber, int delta)
        {
            if (noteNumber < 0 || noteNumber > 127)
                return;

            var current = _configManager.GetMidiVelocityThreshold(noteNumber);
            var next = Math.Clamp(current + delta, 0, 127);
            _configManager.SetMidiVelocityThreshold(noteNumber, next);
        }

        private void ResetDrumBindingsToDefault()
        {
            if (_input == null) return;
            var kb = new KeyBindings();
            kb.LoadDefaultBindings();
            _configManager.SetKeyBindings(kb);
            EvictSystemKeysForDrumBindings(kb);
            ShowNotice("Drum bindings reset to defaults.", DrumStatusTone.Success);
        }

        /// <summary>Evicts a single keyboard key from the system map if it was just claimed by a drum lane.</summary>
        private void EvictSystemKey(string buttonId)
        {
            if (!KeyBindings.IsKeyboardButtonId(buttonId)) return;
            if (!Enum.TryParse(buttonId.Substring(4), out Keys k)) return;
            var snap = new Dictionary<Keys, InputCommandType>(_input!.GetKeyMappingSnapshot());
            if (snap.Remove(k)) _configManager.SetSystemKeyBindings(snap);
        }

        /// <summary>Evicts any system keys claimed by the given drum bindings (used after reset-to-defaults).</summary>
        private void EvictSystemKeysForDrumBindings(KeyBindings kb)
        {
            var snap = new Dictionary<Keys, InputCommandType>(_input!.GetKeyMappingSnapshot());
            bool changed = false;
            foreach (var id in kb.ButtonToLane.Keys)
            {
                if (KeyBindings.IsKeyboardButtonId(id) && Enum.TryParse(id.Substring(4), out Keys k))
                    changed |= snap.Remove(k);
            }
            if (changed) _configManager.SetSystemKeyBindings(snap);
        }

        protected override void OnDeactivate()
        {
            // Persist-on-edit safety net: flush any pending deferred save when leaving the stage.
            // Back already flushes via ExitStage; this covers other deactivation paths.
            _configManager.FlushPendingSave();
            _capture.Stop();
            ReleaseGraphics();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                ReleaseGraphics();
            base.Dispose(disposing);
        }

        private void ReleaseGraphics()
        {
            _renderer?.Dispose();
            _renderer = null;
            _spriteBatch?.Dispose();
            _spriteBatch = null;
        }
    }
}
