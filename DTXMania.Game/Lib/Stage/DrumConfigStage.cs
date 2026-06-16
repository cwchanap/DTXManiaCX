#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Stage.DrumConfig;
using DTXMania.Game.Lib.Utilities;
using XnaButtonState = Microsoft.Xna.Framework.Input.ButtonState;

namespace DTXMania.Game.Lib.Stage
{
    /// <summary>
    /// Visual drum-mapping stage. Shows a drawn kit; the user selects a piece (mouse or keyboard)
    /// and hits any input device to bind it to that lane. Edits a working copy; commits on Save.
    /// </summary>
    public class DrumConfigStage : BaseStage
    {
        private const int LaneCount = 10;

        private IConfigManager _configManager = null!;
        private InputManagerCompat? _input;

        private KeyBindings _workingBindings = new();
        private Dictionary<Keys, InputCommandType> _workingSystemBindings = new();

        private SpriteBatch _spriteBatch = null!;
        private Texture2D _whitePixel = null!;
        private IFont? _font;
        private IResourceManager _resourceManager = null!;
        private DrumKitRenderer? _renderer;
        private DrumCapturePopup? _popup;

        private int _focusedLane;
        private int _hoveredLane = -1;
        private int _selectedLane = -1;
        private bool _skipCaptureThisFrame;

        private MouseState _previousMouse;

        public override StageType Type => StageType.DrumConfig;

        public DrumConfigStage(BaseGame game) : base(game)
        {
            _configManager = game.ConfigManager ?? throw new InvalidOperationException("ConfigManager not found");
        }

        protected override void OnActivate()
        {
            var graphicsDevice = _game.GraphicsDevice;
            _spriteBatch = new SpriteBatch(graphicsDevice);
            _whitePixel = new Texture2D(graphicsDevice, 1, 1);
            _whitePixel.SetData(new[] { Color.White });
            _resourceManager = _game.ResourceManager;
            _font = _resourceManager.LoadFont("NotoSerifJP", 14);
            _renderer = new DrumKitRenderer(graphicsDevice);

            _input = _game.InputManager; // BaseGame.InputManager is concretely InputManagerCompat

            // Working copies (committed on Save), mirroring ConfigStage.
            _workingBindings = _input?.ModularInputManager.KeyBindings.Clone() ?? new KeyBindings();
            _workingSystemBindings = _input != null
                ? new Dictionary<Keys, InputCommandType>(_input.GetKeyMappingSnapshot())
                : new Dictionary<Keys, InputCommandType>();

            _popup = new DrumCapturePopup(
                _workingBindings,
                () => _workingSystemBindings,
                key => _workingSystemBindings.Remove(key));

            _focusedLane = 0;
            _selectedLane = -1;
            _hoveredLane = -1;
            _skipCaptureThisFrame = false;
            _previousMouse = Mouse.GetState();
        }

        protected override void OnUpdate(double deltaTime)
        {
            var mouse = Mouse.GetState();
            bool leftClick = mouse.LeftButton == XnaButtonState.Pressed
                             && _previousMouse.LeftButton == XnaButtonState.Released;
            bool rightClick = mouse.RightButton == XnaButtonState.Pressed
                              && _previousMouse.RightButton == XnaButtonState.Released;

            if (_popup != null && _popup.IsOpen)
                UpdatePopup(deltaTime, mouse, leftClick, rightClick);
            else
                UpdateSelection(mouse, leftClick);

            _previousMouse = mouse;
        }

        private void UpdatePopup(double deltaTime, MouseState mouse, bool leftClick, bool rightClick)
        {
            _popup!.Tick(deltaTime);

            // Esc/Back or right-click closes the popup (acts as "Done").
            if (rightClick || _input?.IsBackActionTriggered() == true)
            {
                _popup.Close();
                _selectedLane = -1;
                return;
            }

            var vp = _game.GraphicsDevice.Viewport;
            if (leftClick)
            {
                foreach (var chip in _popup.GetBindingChips(vp.Width, vp.Height))
                {
                    if (chip.Remove.Contains(mouse.X, mouse.Y))
                    {
                        _popup.RemoveBinding(chip.ButtonId);
                        return;
                    }
                }
                if (_popup.GetDoneRect(vp.Width, vp.Height).Contains(mouse.X, mouse.Y))
                {
                    _popup.Close();
                    _selectedLane = -1;
                    return;
                }
                if (_popup.GetClearRect(vp.Width, vp.Height).Contains(mouse.X, mouse.Y))
                {
                    _popup.ClearLane();
                    return;
                }
            }

            // Skip the frame the popup was opened so the activating key isn't captured.
            if (_skipCaptureThisFrame)
            {
                _skipCaptureThisFrame = false;
                return;
            }

            if (_input != null)
            {
                foreach (var button in _input.ModularInputManager.ConsumePressedButtons())
                {
                    if (_popup.TryCapture(button) != DrumCaptureOutcome.Ignored)
                        break; // one binding per press
                }
            }
        }

        private void UpdateSelection(MouseState mouse, bool leftClick)
        {
            var vp = _game.GraphicsDevice.Viewport;
            float designX = mouse.X * DrumKitLayout.DesignWidth / (float)vp.Width;
            float designY = mouse.Y * DrumKitLayout.DesignHeight / (float)vp.Height;
            _hoveredLane = DrumKitLayout.HitTest(designX, designY);

            // Keyboard focus navigation (left/right cycles lanes).
            if (_input?.IsCommandPressed(InputCommandType.MoveRight) == true)
                _focusedLane = (_focusedLane + 1) % LaneCount;
            else if (_input?.IsCommandPressed(InputCommandType.MoveLeft) == true)
                _focusedLane = (_focusedLane - 1 + LaneCount) % LaneCount;

            // Back exits the stage (Back = Save: commit the working copy).
            if (_input?.IsBackActionTriggered() == true)
            {
                CommitAndExit();
                return;
            }

            // Open popup via click on a zone, or Activate on the focused lane.
            if (leftClick && _hoveredLane >= 0)
                OpenPopup(_hoveredLane);
            else if (_input?.IsCommandPressed(InputCommandType.Activate) == true)
                OpenPopup(_focusedLane);
        }

        private void OpenPopup(int lane)
        {
            _selectedLane = lane;
            _focusedLane = lane;
            _popup!.Open(lane);
            _skipCaptureThisFrame = true;
        }

        protected override void OnDraw(double deltaTime)
        {
            if (_spriteBatch == null || _renderer == null)
                return;

            var vp = _game.GraphicsDevice.Viewport;
            _spriteBatch.Begin();

            if (_font != null)
                _font.DrawString(_spriteBatch, "DRUM MAPPING  -  click a piece, then hit your input.  Back: save & exit",
                    new Vector2(20, 16), Color.White);

            _renderer.Draw(_spriteBatch, _font, _whitePixel, _workingBindings,
                vp.Width, vp.Height, _selectedLane, _focusedLane, _hoveredLane);

            _popup?.Draw(_spriteBatch, _font, _whitePixel, vp.Width, vp.Height);

            _spriteBatch.End();
        }

        /// <summary>Persists the working bindings and returns to ConfigStage.</summary>
        private void Save()
        {
            // Without a live input manager the working copy is just defaults, not the
            // user's bindings — committing it would clobber the real config. Skip the save.
            if (_input == null)
            {
                ChangeStage(StageType.Config, new InstantTransition());
                return;
            }

            bool persisted = true;
            if (_configManager is ConfigManager concrete)
            {
                var config = _configManager.Config;

                // Snapshot the binding-related config so we can roll back if the disk write fails,
                // keeping live input state consistent with what is on disk (mirrors ConfigStage).
                var prevKeyBindings = new Dictionary<string, int>(config.KeyBindings);
                var prevUnboundLanes = new HashSet<int>(config.UnboundDrumLanes);
                var prevUnboundButtons = new HashSet<string>(config.UnboundDrumButtons);
                var prevSystemBindings = new Dictionary<string, string>(config.SystemKeyBindings);

                concrete.SaveKeyBindings(_workingBindings);
                concrete.SaveSystemKeyBindings(_workingSystemBindings);

                try
                {
                    _configManager.SaveConfig(AppPaths.GetConfigFilePath());
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"DrumConfigStage: save failed: {ex.Message}");

                    config.KeyBindings.Clear();
                    foreach (var kvp in prevKeyBindings) config.KeyBindings[kvp.Key] = kvp.Value;
                    config.UnboundDrumLanes.Clear();
                    foreach (var lane in prevUnboundLanes) config.UnboundDrumLanes.Add(lane);
                    config.UnboundDrumButtons.Clear();
                    foreach (var buttonId in prevUnboundButtons) config.UnboundDrumButtons.Add(buttonId);
                    config.SystemKeyBindings.Clear();
                    foreach (var kvp in prevSystemBindings) config.SystemKeyBindings[kvp.Key] = kvp.Value;

                    persisted = false;
                }
            }

            // Apply to live input state only if the disk write succeeded.
            if (persisted)
            {
                _input.ModularInputManager.ReloadKeyBindings();
                ApplySystemBindings(_input, _workingSystemBindings);
            }

            ChangeStage(StageType.Config, new InstantTransition());
        }

        /// <summary>Commits the working bindings and exits to ConfigStage. Back = Save: pressing Back commits changes, like other config screens.</summary>
        private void CommitAndExit()
        {
            Save();
        }

        private static void ApplySystemBindings(InputManager inputManager,
            IReadOnlyDictionary<Keys, InputCommandType> bindings)
        {
            var snapshot = inputManager.GetKeyMappingSnapshot();
            foreach (var kvp in snapshot)
                inputManager.RemoveKeyMapping(kvp.Key);
            foreach (var kvp in bindings)
                inputManager.AddKeyMapping(kvp.Key, kvp.Value);
        }

        protected override void OnDeactivate()
        {
            _renderer?.Dispose();
            _renderer = null;
            _popup = null;
            _font?.RemoveReference();
            _font = null;
            _spriteBatch?.Dispose();
            _spriteBatch = null!;
            _whitePixel?.Dispose();
            _whitePixel = null!;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _renderer?.Dispose();
                _whitePixel?.Dispose();
                _spriteBatch?.Dispose();
                _font?.RemoveReference();
                _renderer = null;
                _whitePixel = null!;
                _spriteBatch = null!;
                _font = null;
                _resourceManager = null!;
            }
            base.Dispose(disposing);
        }
    }
}
