#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Resources;

namespace DTXMania.Game.Lib.Stage.KeyAssign
{
    /// <summary>
    /// Sub-panel for remapping keyboard keys to drum lanes (0-9).
    /// Supports multiple keys per lane; edits a working copy until the user saves.
    /// </summary>
    public class DrumKeyAssignPanel : IKeyAssignPanel
    {
        private enum CaptureState { Browsing, AwaitingKey, ShowingConflict }

        private const int LaneCount = 10;
        private const int FooterSave = LaneCount;
        private const int FooterCancel = LaneCount + 1;
        private const int RowCount = LaneCount + 2;
        private const double ConflictDuration = 2.0;

        private readonly ModularInputManager _modularInputManager;

        // Working copy of drum bindings (edited in isolation; committed on Save)
        private KeyBindings _workingBindings = new();
        private int _selectedIndex;
        private CaptureState _state;
        private string? _conflictMessage;
        private double _conflictTimer;

        public bool IsActive { get; private set; }
        public event EventHandler? Closed;
        public event EventHandler? Saved;

        public DrumKeyAssignPanel(ModularInputManager modularInputManager)
        {
            _modularInputManager = modularInputManager ?? throw new ArgumentNullException(nameof(modularInputManager));
        }

        public void Activate()
        {
            _workingBindings = CloneBindings(_workingBindingsProvider?.Invoke() ?? _modularInputManager.KeyBindings);

            _selectedIndex = 0;
            _state = CaptureState.Browsing;
            _conflictMessage = null;
            _conflictTimer = 0;
            IsActive = true;
        }

        public void Deactivate()
        {
            IsActive = false;
        }

        public void Update(double deltaTime, KeyboardState current, KeyboardState previous)
        {
            if (!IsActive) return;

            if (_state == CaptureState.ShowingConflict)
            {
                _conflictTimer -= deltaTime;
                if (_conflictTimer <= 0)
                {
                    _state = CaptureState.Browsing;
                    _conflictMessage = null;
                }
                return;
            }

            if (_state == CaptureState.AwaitingKey)
            {
                HandleKeyCapture(current, previous);
                return;
            }

            // Browsing
            if (IsJustPressed(current, previous, Keys.Up))
                _selectedIndex = (_selectedIndex - 1 + RowCount) % RowCount;
            else if (IsJustPressed(current, previous, Keys.Down))
                _selectedIndex = (_selectedIndex + 1) % RowCount;
            else if (IsJustPressed(current, previous, Keys.Enter))
            {
                if (_selectedIndex == FooterSave)
                    CommitAndClose();
                else if (_selectedIndex == FooterCancel)
                    CancelAndClose();
                else
                    _state = CaptureState.AwaitingKey;
            }
            else if (IsJustPressed(current, previous, Keys.Escape))
                CancelAndClose();
            else if (IsJustPressed(current, previous, Keys.Delete) && _selectedIndex < LaneCount)
                _workingBindings.UnbindLane(_selectedIndex);
        }

        private void HandleKeyCapture(KeyboardState current, KeyboardState previous)
        {
            if (IsJustPressed(current, previous, Keys.Escape))
            {
                _state = CaptureState.Browsing;
                return;
            }

            foreach (var key in current.GetPressedKeys())
            {
                if (!previous.IsKeyDown(key) && key != Keys.Escape)
                {
                    AssignKey(key);
                    return;
                }
            }
        }

        private void AssignKey(Keys key)
        {
            var conflict = KeyConflictChecker.CheckAgainstSystemBindings(
                GetLiveSystemMapping(), key);

            if (conflict != null)
            {
                ShowConflict(conflict);
                return;
            }

            _workingBindings.BindButton(KeyBindings.CreateKeyButtonId(key), _selectedIndex);
            _state = CaptureState.Browsing;
        }

        private void CommitAndClose()
        {
            Deactivate();
            Saved?.Invoke(this, EventArgs.Empty);
            Closed?.Invoke(this, EventArgs.Empty);
        }

        private void CancelAndClose()
        {
            Deactivate();
            Closed?.Invoke(this, EventArgs.Empty);
        }

        private void ShowConflict(string message)
        {
            _conflictMessage = message;
            _conflictTimer = ConflictDuration;
            _state = CaptureState.ShowingConflict;
        }

        /// <summary>
        /// Returns the live system key mapping from InputManagerCompat.
        /// DrumKeyAssignPanel does not hold a reference to InputManager; it reads on demand
        /// via the modular manager's parent chain to avoid tight coupling.
        /// </summary>
        private IReadOnlyDictionary<Keys, InputCommandType> GetLiveSystemMapping()
        {
            // The modular manager doesn't expose InputManager directly; return empty dict as safe default.
            // Conflict checking is done when the panel is wired to ConfigStage which provides a delegate.
            return _liveSystemMappingProvider?.Invoke() ?? new Dictionary<Keys, InputCommandType>();
        }

        // Injected by ConfigStage so this panel can check system key conflicts without
        // needing a direct reference to InputManager.
        internal Func<IReadOnlyDictionary<Keys, InputCommandType>>? _liveSystemMappingProvider;
        internal Func<KeyBindings>? _workingBindingsProvider;

        internal KeyBindings GetWorkingBindingsSnapshot() => CloneBindings(_workingBindings);

        public void Draw(SpriteBatch spriteBatch, BitmapFont? bitmapFont, Texture2D? whitePixel,
                         int viewportWidth, int viewportHeight)
        {
            if (!IsActive) return;

            // Dark overlay
            if (whitePixel != null)
                spriteBatch.Draw(whitePixel,
                    new Rectangle(0, 0, viewportWidth, viewportHeight), new Color(0, 0, 0, 210));

            const int panelX = 80;
            int y = 50;
            const int rowH = 36;

            DrawText(spriteBatch, bitmapFont, "DRUM KEY MAPPING", panelX, y, Color.White, false);
            y += rowH + 8;

            for (int i = 0; i < LaneCount; i++)
            {
                bool sel = i == _selectedIndex;
                DrawRowBackground(spriteBatch, whitePixel, panelX, y, rowH, sel);

                string keyLabel = (sel && _state == CaptureState.AwaitingKey)
                    ? "[Press any key... ESC to cancel]"
                    : _workingBindings.GetLaneDescription(i);

                string rowText = $"{i + 1,2}. {KeyBindings.GetLaneName(i),-30}  {keyLabel}";
                DrawText(spriteBatch, bitmapFont, rowText, panelX, y + 6, sel ? Color.Yellow : Color.White, sel);
                y += rowH;
            }

            y += 8;

            // Footer rows
            DrawFooterRow(spriteBatch, bitmapFont, whitePixel, panelX, y, rowH, "SAVE", _selectedIndex == FooterSave);
            y += rowH;
            DrawFooterRow(spriteBatch, bitmapFont, whitePixel, panelX, y, rowH, "CANCEL (ESC)", _selectedIndex == FooterCancel);
            y += rowH + 8;

            // Conflict message
            if (_conflictMessage != null)
                DrawText(spriteBatch, bitmapFont, $"Conflict: {_conflictMessage}", panelX, y, Color.Red, false);

            // Instructions
            int instrY = viewportHeight - 28;
            DrawText(spriteBatch, bitmapFont,
                "UP/DOWN: Navigate | ENTER: Assign | DELETE: Clear lane | ESC: Cancel",
                panelX, instrY, new Color(180, 180, 180), false);
        }

        private static void DrawRowBackground(SpriteBatch sb, Texture2D? wp, int x, int y, int h, bool selected)
        {
            if (selected && wp != null)
                sb.Draw(wp, new Rectangle(x - 4, y, 860, h - 2), new Color(64, 64, 128, 150));
        }

        private static void DrawFooterRow(SpriteBatch sb, BitmapFont? font, Texture2D? wp,
            int x, int y, int h, string label, bool selected)
        {
            if (selected && wp != null)
                sb.Draw(wp, new Rectangle(x - 4, y, 300, h - 2), new Color(64, 64, 128, 150));
            DrawText(sb, font, label, x, y + 6, selected ? Color.Yellow : Color.White, selected);
        }

        private static void DrawText(SpriteBatch sb, BitmapFont? font, string text, int x, int y,
            Color color, bool thin)
        {
            if (font?.IsLoaded == true)
                font.DrawText(sb, text, x, y, color,
                    thin ? BitmapFont.FontType.Thin : BitmapFont.FontType.Normal);
        }

        private static bool IsJustPressed(KeyboardState cur, KeyboardState prev, Keys key)
            => cur.IsKeyDown(key) && !prev.IsKeyDown(key);

        private static KeyBindings CloneBindings(KeyBindings source)
        {
            var clone = new KeyBindings();
            clone.ClearAllBindings();

            foreach (var kvp in source.ButtonToLane)
            {
                clone.BindButton(kvp.Key, kvp.Value);
            }

            return clone;
        }
    }
}
