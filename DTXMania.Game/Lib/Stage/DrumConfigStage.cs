#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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
    /// and hits any input device to bind it to that lane. Reads ConfigManager as the single source
    /// of truth; edits apply immediately through SetKeyBindings/SetSystemKeyBindings, which
    /// live-apply to the runtime via the Phase 2 events and mark a deferred save dirty. Back exits
    /// and flushes the pending save. There is no working copy.
    /// </summary>
    public class DrumConfigStage : BaseStage
    {
        private IConfigManager _configManager = null!;
        private InputManagerCompat? _input;

        // NullLogger until OnActivate swaps in the game's factory. Lets reflection-based tests
        // (which skip OnActivate and leave _game.LoggerFactory null) exercise the edit helpers
        // without NullReferenceException, while real runtime gets structured logging that survives
        // Release builds.
        private ILogger<DrumConfigStage> _logger = NullLogger<DrumConfigStage>.Instance;

        private SpriteBatch _spriteBatch = null!;
        private Texture2D _whitePixel = null!;
        private IFont? _font;
        private IResourceManager _resourceManager = null!;
        private DrumKitRenderer? _renderer;
        private DrumCapturePopup? _popup;
        private ITexture? _background;
        private ITexture? _skeleton;
        // The decorative bass-drum body reuses the renderer's KickTexture (same asset as the
        // Kick zone) instead of a second ref-counted load of TexturePath.DrumPadKick.

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

        private MouseState _previousMouse;

        public override StageType Type => StageType.DrumConfig;

        public DrumConfigStage(BaseGame game) : base(game)
        {
            _configManager = game.ConfigManager ?? throw new InvalidOperationException("ConfigManager not found");
        }

        protected override void OnActivate()
        {
            _logger = _game.LoggerFactory?.CreateLogger<DrumConfigStage>() ?? _logger;

            var graphicsDevice = _game.GraphicsDevice;
            _spriteBatch = new SpriteBatch(graphicsDevice);
            _whitePixel = new Texture2D(graphicsDevice, 1, 1);
            _whitePixel.SetData(new[] { Color.White });
            _resourceManager = _game.ResourceManager;
            _font = _resourceManager.LoadFont("NotoSerifJP", 14);
            _renderer = new DrumKitRenderer(graphicsDevice, _resourceManager);

            // Optional skin art: each is a best-effort load (a missing/invalid asset falls back to
            // a plain background). Logged at Debug — missing optional art is an expected fallback,
            // not a warning — but no longer fully silent so a broken skin leaves a diagnostic trail.
            try { _background = _resourceManager.LoadTexture(TexturePath.StartupBackground); }
            catch (Exception ex) { _logger.LogDebug(ex, "DrumConfigStage: optional background texture unavailable"); _background = null; }

            // Bare drum-kit hardware drawn behind the pieces so the stage reads as a whole kit.
            try { _skeleton = _resourceManager.LoadTexture(TexturePath.DrumKitSkeleton); }
            catch (Exception ex) { _logger.LogDebug(ex, "DrumConfigStage: optional kit-skeleton texture unavailable"); _skeleton = null; }

            // The decorative bass-drum body is drawn from _renderer.KickTexture (loaded once by
            // DrumKitRenderer above), so no separate TexturePath.DrumPadKick load is needed here.

            _input = _game.InputManager; // BaseGame.InputManager is concretely InputManagerCompat

            // The popup providers read the RUNTIME, which always mirrors Config via the Phase 2
            // events (ConfigManager.KeyBindingsChanged/SystemKeyBindingsChanged -> InputManagerCompat
            // reloads). Config is truth, so there is no working copy to clone or hand off.
            _popup = new DrumCapturePopup(
                () => _input!.ModularInputManager.KeyBindings.ButtonToLane,  // drum map (= Config)
                () => _input!.GetKeyMappingSnapshot());                       // system map (= Config)

            _focusIndex = 0;
            _keyboardFocusActive = false;
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
                        // Popup is intent-only (no RemoveBinding): the stage unbinds via Config.
                        RemoveBindingFromConfig(chip.ButtonId);
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
                    // Popup is intent-only (no ClearLane): the stage clears the lane via Config.
                    ClearLaneInConfig(_popup.Lane);
                    return;
                }
            }

            // Skip the frame the popup was opened so the activating key isn't captured.
            if (_skipCaptureThisFrame)
            {
                _skipCaptureThisFrame = false;
                return;
            }

            ProcessPopupCapture();
        }

        /// <summary>
        /// Resolves at most one pressed button per frame against the open popup: the first non-Ignored
        /// outcome (Captured or Rejected) ends processing for the frame, so a valid key pressed in the
        /// same frame as a reserved key is dropped if the reserved key is enumerated first. Extracted
        /// from <see cref="UpdatePopup"/> because it is GraphicsDevice-free, making the one-binding-per-
        /// press semantics unit-testable headlessly.
        /// </summary>
        private void ProcessPopupCapture()
        {
            if (_input == null)
                return;

            foreach (var button in _input.ModularInputManager.ConsumePressedButtons())
            {
                var outcome = _popup!.TryCapture(button);
                if (outcome != DrumCaptureOutcome.Ignored)
                {
                    // Captured is an intent signal (the popup mutates nothing): the STAGE applies the
                    // binding to Config via SetKeyBindings (live-apply + dirty). Rejected (a reserved
                    // key) did not mutate. Either way, one binding resolution per press.
                    if (outcome == DrumCaptureOutcome.Captured)
                        ApplyCapture(button.Id, _popup.Lane);
                    break;
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

            // Back exits the stage (persist-on-edit: flush any pending deferred save on the way out).
            if (_input?.IsBackActionTriggered() == true)
            {
                ExitStage();
                return;
            }

            // Reset-to-defaults button: check before zone hit-test so a click there
            // doesn't also open a lane popup. Move focus onto the action so the highlight
            // reflects the click (matches keyboard focus landing here).
            if (leftClick && GetResetButtonRect(vp.Width, vp.Height).Contains(mouse.X, mouse.Y))
            {
                _focusIndex = DrumKitLayout.ResetActionIndex;
                ResetDrumBindingsToDefault();
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
        /// bindings in Config; a zone opens its capture popup. GraphicsDevice-free so the dispatch
        /// is unit-testable headlessly — <see cref="UpdateSelection"/> calls only this.
        /// </summary>
        private void ActivateFocusedElement()
        {
            if (DrumKitLayout.IsResetAction(_focusIndex))
                ResetDrumBindingsToDefault();
            else
                OpenPopup(_focusIndex);
        }

        // "Reset to defaults" button (viewport space), top-right of the screen. Named rather than
        // magic numbers so the geometry is discoverable; matches the popup's centralized-constant
        // pattern. viewportHeight is unused (button is top-anchored); kept for call-symmetry with
        // the other (viewportWidth, viewportHeight) rect getters.
        private const int ResetButtonWidth = 190;
        private const int ResetButtonHeight = 30;
        private const int ResetButtonRightInset = 210; // right viewport edge -> button's left edge
        private const int ResetButtonTopInset = 12;

        private static Rectangle GetResetButtonRect(int viewportWidth, int viewportHeight) =>
            new Rectangle(viewportWidth - ResetButtonRightInset, ResetButtonTopInset,
                          ResetButtonWidth, ResetButtonHeight);

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
            // Reuses the renderer's KickTexture (same asset as the Kick zone) — one load, one owner.
            var bassDrum = _renderer?.KickTexture;
            if (bassDrum?.Texture != null)
            {
                float dsx = vp.Width / 1280f, dsy = vp.Height / 720f;
                int bw = (int)(240 * dsx), bh = (int)(220 * dsy);
                _spriteBatch.Draw(bassDrum.Texture,
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
            // Named initializer (not positional args) so the three highlight lanes can't be swapped
            // silently at the call site — the transposition hazard LaneHighlights exists to remove.
            var highlights = new LaneHighlights
            {
                SelectedLane = _selectedLane,
                FocusedLane = focusedLaneForRender,
                HoveredLane = _hoveredLane
            };
            // Display reads the runtime, which always mirrors Config (persist-on-edit truth).
            _renderer.Draw(_spriteBatch, _input?.ModularInputManager.KeyBindings ?? new KeyBindings(),
                _font, _whitePixel,
                vp.Width, vp.Height, in highlights);

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

            _popup?.Draw(_spriteBatch, _font, _whitePixel, vp.Width, vp.Height);

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
        /// capture-time eviction is permanent — no restore on undo).</summary>
        private void ApplyCapture(string buttonId, int lane)
        {
            if (_input == null) return;
            var kb = _input.ModularInputManager.KeyBindings.Clone();
            kb.BindButton(buttonId, lane);
            _configManager.SetKeyBindings(kb);
            // Immediate eviction (Decision 3): a keyboard key claimed by a drum lane leaves the
            // system map now.
            EvictSystemKey(buttonId);
        }

        private void RemoveBindingFromConfig(string buttonId)
        {
            if (_input == null) return;
            var kb = _input.ModularInputManager.KeyBindings.Clone();
            kb.UnbindButton(buttonId);
            _configManager.SetKeyBindings(kb);
            // No system-key restore (Decision 3): eviction was permanent.
        }

        private void ClearLaneInConfig(int lane)
        {
            if (_input == null) return;
            var kb = _input.ModularInputManager.KeyBindings.Clone();
            kb.UnbindLane(lane);
            _configManager.SetKeyBindings(kb);
        }

        private void ResetDrumBindingsToDefault()
        {
            if (_input == null) return;
            var kb = new KeyBindings();
            kb.LoadDefaultBindings();
            _configManager.SetKeyBindings(kb);
            EvictSystemKeysForDrumBindings(kb);
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
            // Back already flushes via ExitStage; this covers other deactivation paths so an edited
            // Config is never left dirty in memory only.
            _configManager.FlushPendingSave();

            _renderer?.Dispose();
            _renderer = null;
            _popup = null;
            _background?.RemoveReference();
            _background = null;
            _skeleton?.RemoveReference();
            _skeleton = null;
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
