#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Stage.KeyAssign;

namespace DTXMania.Game.Lib.Stage.DrumConfig
{
    public enum DrumCaptureState { Closed, Listening, ShowingConflict }
    public enum DrumCaptureOutcome { Ignored, Captured, Rejected }

    /// <summary>
    /// Modal capture for a single drum lane: listens for the next input from any device and
    /// appends it to the lane's bindings. Keyboard keys are checked against system bindings —
    /// required navigation keys are rejected; non-required system keys are auto-evicted
    /// (mirrors the legacy panel's deferred-eviction behavior). Pure state/geometry; <see cref="Draw"/> is the only
    /// graphics method and is exercised only by the stage.
    /// </summary>
    public class DrumCapturePopup
    {
        private const double ConflictDuration = 2.0;

        private readonly KeyBindings _workingBindings;
        private readonly Func<IReadOnlyDictionary<Keys, InputCommandType>> _systemMappingProvider;
        private readonly Action<Keys> _evictSystemBinding;
        private double _conflictTimer;

        public DrumCaptureState State { get; private set; } = DrumCaptureState.Closed;
        public int Lane { get; private set; } = -1;
        public string? ConflictMessage { get; private set; }
        public bool IsOpen => State != DrumCaptureState.Closed;

        public DrumCapturePopup(
            KeyBindings workingBindings,
            Func<IReadOnlyDictionary<Keys, InputCommandType>> systemMappingProvider,
            Action<Keys> evictSystemBinding)
        {
            _workingBindings = workingBindings ?? throw new ArgumentNullException(nameof(workingBindings));
            _systemMappingProvider = systemMappingProvider ?? throw new ArgumentNullException(nameof(systemMappingProvider));
            _evictSystemBinding = evictSystemBinding ?? throw new ArgumentNullException(nameof(evictSystemBinding));
        }

        public void Open(int lane)
        {
            Lane = lane;
            State = DrumCaptureState.Listening;
            ConflictMessage = null;
            _conflictTimer = 0;
        }

        public void Close()
        {
            State = DrumCaptureState.Closed;
            Lane = -1;
            ConflictMessage = null;
        }

        /// <summary>Advances the conflict-notice timer; returns to Listening when it expires.</summary>
        public void Tick(double deltaTime)
        {
            if (State != DrumCaptureState.ShowingConflict)
                return;

            _conflictTimer -= deltaTime;
            if (_conflictTimer <= 0)
            {
                State = DrumCaptureState.Listening;
                ConflictMessage = null;
            }
        }

        /// <summary>Attempts to bind a captured button to the current lane (append model).</summary>
        public DrumCaptureOutcome TryCapture(DTXMania.Game.Lib.Input.ButtonState button)
        {
            if (State != DrumCaptureState.Listening || button == null || string.IsNullOrWhiteSpace(button.Id))
                return DrumCaptureOutcome.Ignored;

            if (TryParseKey(button.Id, out var key))
            {
                var system = _systemMappingProvider();
                var required = KeyConflictChecker.GetRequiredSystemConflict(system, key);
                if (required != null)
                {
                    ConflictMessage = $"{key} is reserved for system action: {required}";
                    State = DrumCaptureState.ShowingConflict;
                    _conflictTimer = ConflictDuration;
                    return DrumCaptureOutcome.Rejected;
                }

                // Non-required system key (e.g. IncreaseScrollSpeed): evict so the lane can claim it.
                if (system.ContainsKey(key))
                    _evictSystemBinding(key);
            }

            _workingBindings.BindButton(button.Id, Lane);
            return DrumCaptureOutcome.Captured;
        }

        public void RemoveBinding(string buttonId) => _workingBindings.UnbindButton(buttonId);

        public void ClearLane() => _workingBindings.UnbindLane(Lane);

        public IReadOnlyList<string> CurrentBindings =>
            _workingBindings.GetButtonsForLane(Lane).ToList();

        private static bool TryParseKey(string buttonId, out Keys key)
        {
            key = default;
            const string prefix = "Key.";
            if (!buttonId.StartsWith(prefix, StringComparison.Ordinal))
                return false;
            return Enum.TryParse(buttonId.Substring(prefix.Length), out key);
        }

        // ---- Geometry shared by rendering and mouse hit-testing (viewport space) ----

        public const int PopupWidth = 380;
        public const int PopupHeight = 230;

        public Rectangle GetPanelRect(int viewportWidth, int viewportHeight) =>
            new((viewportWidth - PopupWidth) / 2, (viewportHeight - PopupHeight) / 2, PopupWidth, PopupHeight);

        public Rectangle GetDoneRect(int viewportWidth, int viewportHeight)
        {
            var p = GetPanelRect(viewportWidth, viewportHeight);
            return new Rectangle(p.Right - 92, p.Bottom - 44, 80, 30);
        }

        public Rectangle GetClearRect(int viewportWidth, int viewportHeight)
        {
            var p = GetPanelRect(viewportWidth, viewportHeight);
            return new Rectangle(p.Left + 12, p.Bottom - 44, 90, 30);
        }

        /// <summary>One drawn binding chip: the whole chip box and the inner ✕ remove hit-area.</summary>
        public readonly struct DrumBindingChip
        {
            public string ButtonId { get; }
            public Rectangle Bounds { get; }
            public Rectangle Remove { get; }
            public DrumBindingChip(string buttonId, Rectangle bounds, Rectangle remove)
            {
                ButtonId = buttonId;
                Bounds = bounds;
                Remove = remove;
            }
        }

        private const int ChipHeight = 22;
        private const int ChipGap = 6;
        private const int ChipPadX = 8;
        private const int ChipCharWidth = 8;   // rough per-char width estimate for layout
        private const int RemoveBoxSize = 14;
        private const int ChipTextRemoveGap = 6; // gap between the label text and the ✕ box

        /// <summary>Y of the binding-chips row, just under the "Configure:" header.</summary>
        private int GetChipsRowTop(int viewportWidth, int viewportHeight) =>
            GetPanelRect(viewportWidth, viewportHeight).Y + 14 + 30;

        /// <summary>
        /// Layout (viewport space) of one chip per current binding, each with an inner ✕ remove
        /// hit-area. Chips flow left-to-right and wrap within the panel. Deterministic and unit-tested;
        /// the stage hit-tests <see cref="DrumBindingChip.Remove"/> against the mouse and calls
        /// <see cref="RemoveBinding"/>.
        /// </summary>
        public IReadOnlyList<DrumBindingChip> GetBindingChips(int viewportWidth, int viewportHeight)
        {
            var panel = GetPanelRect(viewportWidth, viewportHeight);
            int left = panel.X + 16;
            int maxRight = panel.Right - 16;
            int x = left;
            int y = GetChipsRowTop(viewportWidth, viewportHeight);

            var chips = new List<DrumBindingChip>();
            foreach (var id in CurrentBindings)
            {
                int textWidth = id.Length * ChipCharWidth;
                int chipWidth = ChipPadX + textWidth + ChipTextRemoveGap + RemoveBoxSize + ChipPadX;
                if (x + chipWidth > maxRight && x > left)
                {
                    x = left;
                    y += ChipHeight + ChipGap;
                }
                var bounds = new Rectangle(x, y, chipWidth, ChipHeight);
                var remove = new Rectangle(
                    bounds.Right - ChipPadX - RemoveBoxSize,
                    y + ((ChipHeight - RemoveBoxSize) / 2),
                    RemoveBoxSize, RemoveBoxSize);
                chips.Add(new DrumBindingChip(id, bounds, remove));
                x += chipWidth + ChipGap;
            }
            return chips;
        }

        /// <summary>
        /// Draws the popup. Called only by the stage (graphics). No unit test.
        /// </summary>
        public void Draw(SpriteBatch spriteBatch, IFont? font, Texture2D? whitePixel,
                         int viewportWidth, int viewportHeight)
        {
            if (!IsOpen || spriteBatch == null)
                return;

            // Single source of geometry for both the background pass and the label pass below.
            var chips = GetBindingChips(viewportWidth, viewportHeight);

            if (whitePixel != null)
            {
                spriteBatch.Draw(whitePixel, new Rectangle(0, 0, viewportWidth, viewportHeight),
                    new Color(0, 0, 0, 180));
                var panel = GetPanelRect(viewportWidth, viewportHeight);
                spriteBatch.Draw(whitePixel, panel, new Color(27, 31, 41));
                spriteBatch.Draw(whitePixel, GetClearRect(viewportWidth, viewportHeight), new Color(58, 35, 48));
                spriteBatch.Draw(whitePixel, GetDoneRect(viewportWidth, viewportHeight), new Color(58, 70, 90));

                // Draw chips backgrounds
                foreach (var chip in chips)
                {
                    spriteBatch.Draw(whitePixel, chip.Bounds, new Color(35, 42, 54));
                    spriteBatch.Draw(whitePixel, chip.Remove, new Color(120, 50, 60));
                }
            }

            if (font == null)
                return;

            var panelRect = GetPanelRect(viewportWidth, viewportHeight);
            int x = panelRect.X + 16;
            int y = panelRect.Y + 14;

            font.DrawString(spriteBatch, $"Configure: {KeyBindings.GetLaneName(Lane)}",
                new Vector2(x, y), Color.White);

            // Draw binding chips (labels and ✕ markers). ASCII 'x' stands in for ✕ at this font size.
            int promptY;
            if (chips.Count == 0)
            {
                font.DrawString(spriteBatch, "(no bindings)", new Vector2(x, GetChipsRowTop(viewportWidth, viewportHeight)),
                    new Color(120, 130, 140));
                promptY = GetChipsRowTop(viewportWidth, viewportHeight) + ChipHeight + 12;
            }
            else
            {
                foreach (var chip in chips)
                {
                    font.DrawString(spriteBatch, chip.ButtonId,
                        new Vector2(chip.Bounds.X + ChipPadX, chip.Bounds.Y + 3), Color.White);
                    font.DrawString(spriteBatch, "x",
                        new Vector2(chip.Remove.X + 3, chip.Remove.Y + 1), Color.White);
                }
                promptY = chips[^1].Bounds.Bottom + 12;
            }

            var prompt = State == DrumCaptureState.ShowingConflict
                ? (ConflictMessage ?? "Conflict")
                : "Listening - hit any key, pad, or MIDI note";
            var promptColor = State == DrumCaptureState.ShowingConflict ? Color.Red : new Color(255, 216, 77);
            font.DrawString(spriteBatch, prompt, new Vector2(x, promptY), promptColor);

            font.DrawString(spriteBatch, "Clear", new Vector2(GetClearRect(viewportWidth, viewportHeight).X + 14,
                GetClearRect(viewportWidth, viewportHeight).Y + 6), Color.White);
            font.DrawString(spriteBatch, "Done", new Vector2(GetDoneRect(viewportWidth, viewportHeight).X + 18,
                GetDoneRect(viewportWidth, viewportHeight).Y + 6), Color.White);
        }
    }
}
