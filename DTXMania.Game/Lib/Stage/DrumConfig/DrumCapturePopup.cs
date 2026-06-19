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
    /// reports whether it can be bound to the lane. Provider-driven and intent-only — the popup
    /// owns NO <see cref="KeyBindings"/> and mutates NOTHING. Display data (the buttons currently
    /// bound to the lane) is read on demand from the drum-bindings provider, and required-system-key
    /// conflicts are checked against the system-mapping provider. <see cref="TryCapture"/> only
    /// decides an outcome (Captured / Rejected / Ignored); the STAGE applies the binding through
    /// ConfigManager.SetKeyBindings and evicts the conflicting system key immediately at capture
    /// time (the eviction is permanent — Decision 3). Pure state/geometry; <see cref="Draw"/> is
    /// the only graphics method and is exercised only by the stage.
    /// </summary>
    public class DrumCapturePopup
    {
        private const double ConflictDuration = 2.0;

        private readonly Func<IReadOnlyDictionary<string, int>> _drumBindingsProvider;
        private readonly Func<IReadOnlyDictionary<Keys, InputCommandType>> _systemMappingProvider;
        private double _conflictTimer;

        public DrumCaptureState State { get; private set; } = DrumCaptureState.Closed;
        public int Lane { get; private set; } = -1;
        public string? ConflictMessage { get; private set; }
        public bool IsOpen => State != DrumCaptureState.Closed;

        public DrumCapturePopup(
            Func<IReadOnlyDictionary<string, int>> drumBindingsProvider,
            Func<IReadOnlyDictionary<Keys, InputCommandType>> systemMappingProvider)
        {
            _drumBindingsProvider = drumBindingsProvider ?? throw new ArgumentNullException(nameof(drumBindingsProvider));
            _systemMappingProvider = systemMappingProvider ?? throw new ArgumentNullException(nameof(systemMappingProvider));
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

        /// <summary>
        /// Decides whether a captured button CAN be bound to the current lane. Intent-only: this
        /// method mutates NOTHING. Required system keys are rejected (and surface a conflict
        /// notice); everything else is reported as Captured and the STAGE applies the binding
        /// through ConfigManager.SetKeyBindings, then evicts the conflicting system key immediately
        /// via SetSystemKeyBindings (permanent — Decision 3).
        /// </summary>
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

                // Non-required system keys (e.g. IncreaseScrollSpeed) are NOT evicted here — the
                // popup is intent-only. The STAGE evicts the captured key immediately at capture
                // via SetSystemKeyBindings, and the eviction is permanent: removing the drum
                // binding does NOT restore the system key (Decision 3).
            }

            // Captured is an intent signal only — the caller (the stage) applies the binding. The
            // popup never touches the drum-bindings provider's underlying state.
            return DrumCaptureOutcome.Captured;
        }

        public IReadOnlyList<string> CurrentBindings =>
            _drumBindingsProvider()
                .Where(kvp => kvp.Value == Lane)
                .Select(kvp => kvp.Key)
                .ToList();

        private static bool TryParseKey(string buttonId, out Keys key)
        {
            key = default;
            const string prefix = "Key.";
            if (!buttonId.StartsWith(prefix, StringComparison.Ordinal))
                return false;
            return Enum.TryParse(buttonId.Substring(prefix.Length), out key);
        }

        // ---- Geometry shared by rendering and mouse hit-testing (viewport space) ----

        public const int PopupWidth = 560;
        public const int PopupHeight = 380;

        public Rectangle GetPanelRect(int viewportWidth, int viewportHeight) =>
            new((viewportWidth - PopupWidth) / 2, (viewportHeight - PopupHeight) / 2, PopupWidth, PopupHeight);

        public Rectangle GetDoneRect(int viewportWidth, int viewportHeight)
        {
            var p = GetPanelRect(viewportWidth, viewportHeight);
            return new Rectangle(p.Right - 140, p.Bottom - 60, 122, 42);
        }

        public Rectangle GetClearRect(int viewportWidth, int viewportHeight)
        {
            var p = GetPanelRect(viewportWidth, viewportHeight);
            return new Rectangle(p.Left + 18, p.Bottom - 60, 122, 42);
        }

        /// <summary>One drawn binding chip: the whole chip box and the inner ✕ remove hit-area.</summary>
        public readonly struct DrumBindingChip
        {
            /// <summary>Raw button id (e.g. "Key.A"), used for removal lookups.</summary>
            public string ButtonId { get; }
            /// <summary>Human-readable label (via <see cref="KeyBindings.FormatButtonId"/>), drawn on the chip.</summary>
            public string Label { get; }
            public Rectangle Bounds { get; }
            public Rectangle Remove { get; }
            public DrumBindingChip(string buttonId, string label, Rectangle bounds, Rectangle remove)
            {
                ButtonId = buttonId;
                Label = label;
                Bounds = bounds;
                Remove = remove;
            }
        }

        private const int ChipHeight = 52;      // large box so the corner ✕ reads as clearly inside
        private const int ChipGap = 8;
        private const int ChipPadX = 10;
        private const int ChipCharWidth = 8;    // rough per-char width estimate for layout
        private const int ChipMinWidth = 64;
        private const int RemoveBoxSize = 18;
        private const int RemoveMargin = 9;     // inset of the ✕ box from the chip's top-right corner

        /// <summary>Y of the binding-chips row, just under the "Configure:" header.</summary>
        private int GetChipsRowTop(int viewportWidth, int viewportHeight) =>
            GetPanelRect(viewportWidth, viewportHeight).Y + 18 + 38;

        /// <summary>
        /// Layout (viewport space) of one chip per current binding: a large box with a small ✕
        /// remove hit-area in its top-right corner. Chips flow left-to-right and wrap within the
        /// panel. Deterministic and unit-tested; the stage hit-tests <see cref="DrumBindingChip.Remove"/>
        /// against the mouse and unbinds the chip's button via ConfigManager.SetKeyBindings (the
        /// popup is intent-only and exposes no remove method).
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
                // Chip width and drawn text use the formatted label (e.g. "A", "Space", ";",
                // "MIDI 36") rather than the raw id, so chips match the per-zone labels rendered
                // by DrumKitRenderer (which uses GetLaneDescription / FormatButtonId). Reserve room
                // on the right for the corner ✕ so it never sits on top of the label.
                var label = KeyBindings.FormatButtonId(id);
                int textWidth = label.Length * ChipCharWidth;
                int chipWidth = Math.Max(ChipMinWidth,
                    ChipPadX + textWidth + RemoveMargin + RemoveBoxSize + RemoveMargin);
                if (x + chipWidth > maxRight && x > left)
                {
                    x = left;
                    y += ChipHeight + ChipGap;
                }
                var bounds = new Rectangle(x, y, chipWidth, ChipHeight);
                var remove = new Rectangle(
                    bounds.Right - RemoveMargin - RemoveBoxSize,
                    bounds.Y + RemoveMargin,
                    RemoveBoxSize, RemoveBoxSize);
                chips.Add(new DrumBindingChip(id, label, bounds, remove));
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

                // Draw chip boxes (clearly lighter than the panel so they read as boxes) and
                // their inset top-right ✕ remove squares.
                foreach (var chip in chips)
                {
                    spriteBatch.Draw(whitePixel, chip.Bounds, new Color(66, 74, 92));
                    spriteBatch.Draw(whitePixel, chip.Remove, new Color(176, 72, 78));
                }
            }

            if (font == null)
                return;

            var panelRect = GetPanelRect(viewportWidth, viewportHeight);
            int x = panelRect.X + 20;
            int y = panelRect.Y + 18;

            font.DrawString(spriteBatch, $"Configure: {KeyBindings.GetLaneName(Lane)}",
                new Vector2(x, y), Color.White);

            // Draw binding chips (labels and ✕ markers). ASCII 'X' stands in for ✕ at this font size.
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
                    // Label vertically centered in the tall box; ✕ centered inside its corner square
                    // via MeasureString so the glyph lands within the box (a fixed offset rendered
                    // the glyph below the box, which read as "x outside the box").
                    int labelY = chip.Bounds.Y + ((ChipHeight - 14) / 2);
                    font.DrawString(spriteBatch, chip.Label,
                        new Vector2(chip.Bounds.X + ChipPadX, labelY), Color.White);

                    const string mark = "X";
                    var ms = font.MeasureString(mark);
                    font.DrawString(spriteBatch, mark,
                        new Vector2(chip.Remove.X + ((RemoveBoxSize - ms.X) / 2f),
                                    chip.Remove.Y + ((RemoveBoxSize - ms.Y) / 2f)),
                        Color.White);
                }
                promptY = chips[^1].Bounds.Bottom + 12;
            }

            var prompt = State == DrumCaptureState.ShowingConflict
                ? (ConflictMessage ?? "Conflict")
                : "Listening - hit any key, pad, or MIDI note";
            var promptColor = State == DrumCaptureState.ShowingConflict ? Color.Red : new Color(255, 216, 77);
            font.DrawString(spriteBatch, prompt, new Vector2(x, promptY), promptColor);

            var clearRect = GetClearRect(viewportWidth, viewportHeight);
            var doneRect = GetDoneRect(viewportWidth, viewportHeight);
            font.DrawString(spriteBatch, "Clear", new Vector2(clearRect.X + 38, clearRect.Y + 14), Color.White);
            font.DrawString(spriteBatch, "Done", new Vector2(doneRect.X + 42, doneRect.Y + 14), Color.White);
        }
    }
}
