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
        private ITexture? _background;
        private ITexture? _skeleton;
        private ITexture? _bassDrum;   // decorative only; the bass pedal (lane 6) is the click target

        // Dark UI text, for legibility on the bright background image.
        private static readonly Color DarkText = new(26, 30, 46);

        private int _focusIndex;
        // Keyboard focus is only *shown* once the user navigates with arrows/Tab, and is hidden
        // again as soon as they move the mouse. Without this the default focus (lane 0) would
        // light up permanently on stage entry even though no one is using the keyboard.
        private bool _keyboardFocusActive;
        private int _hoveredLane = -1;
        private int _selectedLane = -1;
        private bool _skipCaptureThisFrame;

        // Set when a Save() disk write fails; rendered in red so the user knows their
        // changes were not persisted and they can retry (Back saves again). Cleared on
        // the next successful save. Null when there is no outstanding error.
        private string? _saveError;

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
            _renderer = new DrumKitRenderer(graphicsDevice, _resourceManager);

            // Reuse the startup stage's bright artwork as this stage's background.
            try { _background = _resourceManager.LoadTexture(TexturePath.StartupBackground); }
            catch { _background = null; }

            // Bare drum-kit hardware drawn behind the pieces so the stage reads as a whole kit.
            try { _skeleton = _resourceManager.LoadTexture(TexturePath.DrumKitSkeleton); }
            catch { _skeleton = null; }

            // Decorative bass drum (the clickable target for lane 6 is the bass pedal in front of it).
            try { _bassDrum = _resourceManager.LoadTexture(TexturePath.DrumPadKick); }
            catch { _bassDrum = null; }

            _input = _game.InputManager; // BaseGame.InputManager is concretely InputManagerCompat

            // Working copies (committed on Save), mirroring ConfigStage.
            _workingBindings = _input?.ModularInputManager.KeyBindings.Clone() ?? new KeyBindings();
            _workingSystemBindings = _input != null
                ? new Dictionary<Keys, InputCommandType>(_input.GetKeyMappingSnapshot())
                : new Dictionary<Keys, InputCommandType>();

            _popup = new DrumCapturePopup(
                _workingBindings,
                () => _workingSystemBindings);

            _focusIndex = 0;
            _keyboardFocusActive = false;
            _selectedLane = -1;
            _hoveredLane = -1;
            _skipCaptureThisFrame = false;
            _saveError = null;
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

            // Moving the mouse is a clear signal the user is pointing, not keyboard-navigating, so
            // hide the keyboard focus highlight and let hover drive the highlight instead.
            if (mouse.X != _previousMouse.X || mouse.Y != _previousMouse.Y)
                _keyboardFocusActive = false;

            // Keyboard focus navigation: arrows or Tab cycle through the zones and the Reset
            // action (design: "arrows/Tab focus + Enter" over zones + Reset). Tab is read via
            // IsKeyPressed so MCP/E2E-injected keys register (raw Keyboard.GetState would not).
            int focusDelta = 0;
            if (_input?.IsCommandPressed(InputCommandType.MoveRight) == true
                || _input?.IsKeyPressed((int)Keys.Tab) == true)
                focusDelta = 1;
            else if (_input?.IsCommandPressed(InputCommandType.MoveLeft) == true)
                focusDelta = -1;

            if (focusDelta != 0)
            {
                _focusIndex = DrumKitLayout.AdvanceFocus(_focusIndex, focusDelta);
                _keyboardFocusActive = true; // user is now navigating by keyboard: show the focus ring
            }

            // Back exits the stage (Back = Save: commit the working copy).
            if (_input?.IsBackActionTriggered() == true)
            {
                CommitAndExit();
                return;
            }

            // Reset-to-defaults button: check before zone hit-test so a click there
            // doesn't also open a lane popup. Move focus onto the action so the highlight
            // reflects the click (matches keyboard focus landing here).
            if (leftClick && GetResetButtonRect(vp.Width, vp.Height).Contains(mouse.X, mouse.Y))
            {
                _focusIndex = DrumKitLayout.ResetActionIndex;
                _workingBindings.LoadDefaultBindings();
                return;
            }

            // Open popup via click on a zone, or Activate on the focused element (zone or Reset).
            if (leftClick && _hoveredLane >= 0)
                OpenPopup(_hoveredLane);
            else if (_input?.IsCommandPressed(InputCommandType.Activate) == true)
                ActivateFocusedElement();
        }

        private void OpenPopup(int lane)
        {
            _selectedLane = lane;
            _focusIndex = lane;
            _popup!.Open(lane);
            _skipCaptureThisFrame = true;
        }

        /// <summary>
        /// Dispatches Activate (Enter) on the focused element: the Reset action restores default
        /// bindings on the working copy; a zone opens its capture popup. GraphicsDevice-free so
        /// the dispatch is unit-testable headlessly — <see cref="UpdateSelection"/> calls only this.
        /// </summary>
        private void ActivateFocusedElement()
        {
            if (DrumKitLayout.IsResetAction(_focusIndex))
                _workingBindings.LoadDefaultBindings();
            else
                OpenPopup(_focusIndex);
        }

        // "Reset to defaults" button (viewport space), top-right of the screen.
        // viewportHeight is unused (button is top-anchored); kept for call-symmetry with the
        // other (viewportWidth, viewportHeight) rect getters.
        private static Rectangle GetResetButtonRect(int viewportWidth, int viewportHeight) =>
            new Rectangle(viewportWidth - 210, 12, 190, 30);

        protected override void OnDraw(double deltaTime)
        {
            if (_spriteBatch == null || _renderer == null)
                return;

            var vp = _game.GraphicsDevice.Viewport;
            _spriteBatch.Begin();

            // Background: the bright startup artwork, calmed by a translucent light scrim so the
            // dark UI text and the kit read clearly against it.
            if (_background?.Texture != null && _whitePixel != null)
            {
                var full = new Rectangle(0, 0, vp.Width, vp.Height);
                _spriteBatch.Draw(_background.Texture, full, Color.White);
                // Premultiplied light scrim (Color.White * a scales RGB and alpha together) so it
                // reads as a ~25% white wash under the default premultiplied AlphaBlend, not a wipe.
                _spriteBatch.Draw(_whitePixel, full, Color.White * 0.25f);
            }

            // Drum-kit hardware skeleton behind the pieces, so the kit reads as one assembled set.
            if (_skeleton?.Texture != null)
                _spriteBatch.Draw(_skeleton.Texture, new Rectangle(0, 0, vp.Width, vp.Height), Color.White * 0.9f);

            // Decorative bass drum at the kit's center, behind the (clickable) bass pedals.
            if (_bassDrum?.Texture != null)
            {
                float dsx = vp.Width / 1280f, dsy = vp.Height / 720f;
                int bw = (int)(240 * dsx), bh = (int)(220 * dsy);
                _spriteBatch.Draw(_bassDrum.Texture,
                    new Rectangle((int)(640 * dsx) - (bw / 2), (int)(486 * dsy) - (bh / 2), bw, bh), Color.White);
            }

            if (_font != null)
                _font.DrawString(_spriteBatch, "DRUM MAPPING  -  click a piece, then hit your input.  Back: save & exit",
                    new Vector2(20, 16), DarkText);

            // Only surface the keyboard focus highlight while keyboard navigation is active, and
            // never for the Reset action (it is not a lane). Otherwise the default focus would keep
            // a zone lit even when the user is only using the mouse.
            int focusedLaneForRender =
                (_keyboardFocusActive && !DrumKitLayout.IsResetAction(_focusIndex)) ? _focusIndex : -1;
            _renderer.Draw(_spriteBatch, _font, _whitePixel, _workingBindings,
                vp.Width, vp.Height, _selectedLane, focusedLaneForRender, _hoveredLane);

            var resetRect = GetResetButtonRect(vp.Width, vp.Height);
            if (_whitePixel != null)
            {
                // Focus ring first (yellow, inflated) so the fill sits inside it, mirroring the
                // zone focus highlight and giving keyboard users a visible "Reset is focused" cue.
                if (_keyboardFocusActive && DrumKitLayout.IsResetAction(_focusIndex))
                {
                    var ring = resetRect;
                    ring.Inflate(4, 4);
                    _spriteBatch.Draw(_whitePixel, ring, new Color(255, 216, 77));
                }
                _spriteBatch.Draw(_whitePixel, resetRect, new Color(58, 70, 90));
            }
            if (_font != null)
                _font.DrawString(_spriteBatch, "Reset to defaults",
                    new Vector2(resetRect.X + 10, resetRect.Y + 6), Color.White);

            // Surface a save failure so the user knows changes were not persisted and can retry.
            // Matches the popup's conflict-notice color so error states read consistently.
            if (_font != null && !string.IsNullOrEmpty(_saveError))
                _font.DrawString(_spriteBatch, _saveError,
                    new Vector2(20, vp.Height - 34), new Color(220, 70, 70));

            _popup?.Draw(_spriteBatch, _font, _whitePixel, vp.Width, vp.Height);

            _spriteBatch.End();
        }

        /// <summary>
        /// Persists the working bindings and returns to ConfigStage. Back = Save: pressing Back
        /// commits changes, like other config screens. Returns true if the stage exited (success
        /// or nothing-to-do); returns false if the disk write failed — in that case the stage
        /// stays open, the working copy is preserved, and <see cref="_saveError"/> is set so the
        /// user can retry by pressing Back again.
        /// </summary>
        private bool Save()
        {
            // Without a live input manager the working copy is just defaults, not the
            // user's bindings — committing it would clobber the real config. Skip the save.
            if (_input == null)
            {
                ChangeStage(StageType.Config, new InstantTransition());
                return true;
            }

            // Non-concrete ConfigManager (e.g. a test stub): nothing to persist and nothing to
            // apply to live input. Exit without touching disk or live bindings.
            if (_configManager is not ConfigManager concrete)
            {
                ChangeStage(StageType.Config, new InstantTransition());
                return true;
            }

            var config = _configManager.Config;

            // Deferred eviction: drop non-required system keys now claimed by a drum lane. Done at
            // commit (not at capture) so removing/clearing/resetting the binding before Save leaves
            // the system binding intact — mirroring the legacy deferred-eviction behavior. Required
            // keys can never reach a drum lane (rejected at capture), so any system key that IS
            // bound to a drum lane here is, by construction, a safe-to-evict non-required key.
            //
            // Snapshot the pre-eviction working copy first: if the disk write below fails, the
            // stage stays open for retry and we must restore the evicted system keys, otherwise
            // the user could only recover them by re-binding each shortcut manually (undoing the
            // drum binding would no longer bring the system shortcut back).
            var prevWorkingSystemBindings = new Dictionary<Keys, InputCommandType>(_workingSystemBindings);
            EvictSystemKeysClaimedByDrumLanes();

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

                // Undo the eviction in the working copy so a retry starts from the pre-failure
                // system mapping. Clear+re-add preserves the dictionary identity relied on by
                // external observers (and existing tests) that hold a reference to it.
                _workingSystemBindings.Clear();
                foreach (var kvp in prevWorkingSystemBindings) _workingSystemBindings[kvp.Key] = kvp.Value;

                // Stay on stage: preserve the working copy and surface the error so the user
                // knows the changes were not persisted and can retry (Back saves again).
                _saveError = $"Failed to save config: {ex.Message}";
                return false;
            }

            // Disk write succeeded: apply to live input state, clear any prior error, and exit.
            _input.ModularInputManager.ReloadKeyBindings();
            ApplySystemBindings(_input, _workingSystemBindings);
            _saveError = null;
            ChangeStage(StageType.Config, new InstantTransition());
            return true;
        }

        /// <summary>Back = Save &amp; exit. Save() transitions only on success; on failure it
        /// stays on the stage and surfaces an error so the user can retry.</summary>
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

        /// <summary>
        /// Removes from <see cref="_workingSystemBindings"/> any keyboard key that is currently
        /// bound to a drum lane in <see cref="_workingBindings"/>. Commit-time counterpart to the
        /// popup's deferred eviction: the system mapping is mutated only when the binding is
        /// actually about to be persisted, so an undone capture never loses a system shortcut.
        /// </summary>
        private void EvictSystemKeysClaimedByDrumLanes()
        {
            foreach (var kvp in _workingBindings.ButtonToLane)
            {
                if (!KeyBindings.IsKeyboardButtonId(kvp.Key))
                    continue;

                // "Key.PageUp" -> Keys.PageUp
                if (Enum.TryParse(kvp.Key.Substring(4), out Keys sysKey))
                    _workingSystemBindings.Remove(sysKey);
            }
        }

        protected override void OnDeactivate()
        {
            _renderer?.Dispose();
            _renderer = null;
            _popup = null;
            _background?.RemoveReference();
            _background = null;
            _skeleton?.RemoveReference();
            _skeleton = null;
            _bassDrum?.RemoveReference();
            _bassDrum = null;
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
                _background?.RemoveReference();
                _background = null;
                _skeleton?.RemoveReference();
                _skeleton = null;
                _bassDrum?.RemoveReference();
                _bassDrum = null;
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
