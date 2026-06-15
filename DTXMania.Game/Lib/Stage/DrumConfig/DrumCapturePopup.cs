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
    /// (mirrors DrumKeyAssignPanel). Pure state/geometry; <see cref="Draw"/> is the only
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

        // ---- Geometry shared by rendering and mouse hit-testing (design space) ----

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

        /// <summary>
        /// Draws the popup. Called only by the stage (graphics). No unit test.
        /// </summary>
        public void Draw(SpriteBatch spriteBatch, IFont? font, Texture2D? whitePixel,
                         int viewportWidth, int viewportHeight)
        {
            if (!IsOpen || spriteBatch == null)
                return;

            if (whitePixel != null)
            {
                spriteBatch.Draw(whitePixel, new Rectangle(0, 0, viewportWidth, viewportHeight),
                    new Color(0, 0, 0, 180));
                var panel = GetPanelRect(viewportWidth, viewportHeight);
                spriteBatch.Draw(whitePixel, panel, new Color(27, 31, 41));
                spriteBatch.Draw(whitePixel, GetClearRect(viewportWidth, viewportHeight), new Color(58, 35, 48));
                spriteBatch.Draw(whitePixel, GetDoneRect(viewportWidth, viewportHeight), new Color(58, 70, 90));
            }

            if (font == null)
                return;

            var panelRect = GetPanelRect(viewportWidth, viewportHeight);
            int x = panelRect.X + 16;
            int y = panelRect.Y + 14;

            font.DrawString(spriteBatch, $"Configure: {KeyBindings.GetLaneName(Lane)}",
                new Vector2(x, y), Color.White);
            y += 30;
            font.DrawString(spriteBatch, $"Bound: {_workingBindings.GetLaneDescription(Lane)}",
                new Vector2(x, y), new Color(180, 200, 220));
            y += 34;

            var prompt = State == DrumCaptureState.ShowingConflict
                ? (ConflictMessage ?? "Conflict")
                : "Listening - hit any key, pad, or MIDI note";
            var promptColor = State == DrumCaptureState.ShowingConflict ? Color.Red : new Color(255, 216, 77);
            font.DrawString(spriteBatch, prompt, new Vector2(x, y), promptColor);

            font.DrawString(spriteBatch, "Clear", new Vector2(GetClearRect(viewportWidth, viewportHeight).X + 14,
                GetClearRect(viewportWidth, viewportHeight).Y + 6), Color.White);
            font.DrawString(spriteBatch, "Done", new Vector2(GetDoneRect(viewportWidth, viewportHeight).X + 18,
                GetDoneRect(viewportWidth, viewportHeight).Y + 6), Color.White);
        }
    }
}
