#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Resources;

namespace DTXMania.Game.Lib.Stage.KeyAssign
{
    /// <summary>
    /// Sub-panel for remapping keyboard keys to system navigation actions (MoveUp, MoveDown, etc.).
    /// One key per action; edits a working copy until the user saves.
    /// </summary>
    public class SystemKeyAssignPanel : IKeyAssignPanel
    {
        private enum CaptureState { Browsing, AwaitingKey, ShowingConflict }

        // Only Backspace cancels capture; all other keys (including Escape, arrows, and Enter) are bindable.
        private static bool IsCancelKey(Keys key) => key == Keys.Back;

        private static readonly InputCommandType[] Actions =
        {
            InputCommandType.MoveUp,
            InputCommandType.MoveDown,
            InputCommandType.MoveLeft,
            InputCommandType.MoveRight,
            InputCommandType.Activate,
            InputCommandType.Back,
        };

        private static readonly int ActionCount = Actions.Length;
        private static readonly int FooterSave = ActionCount;
        private static readonly int FooterCancel = ActionCount + 1;
        private static readonly int RowCount = ActionCount + 2;
        private const double ConflictDuration = 2.0;

        private readonly InputManager _inputManager;

        // Working copy: action -> key (one-to-one)
        private Dictionary<Keys, InputCommandType> _workingMapping = new();
        private Dictionary<Keys, InputCommandType> _navigationMapping = new();
        private int _selectedIndex;
        private CaptureState _state;
        private string? _conflictMessage;
        private double _conflictTimer;

        public bool IsActive { get; private set; }
        public event EventHandler? Closed;
        public event EventHandler? Saved;

        public SystemKeyAssignPanel(InputManager inputManager)
        {
            _inputManager = inputManager ?? throw new ArgumentNullException(nameof(inputManager));
        }

        public void Activate()
        {
            // Clone current live system mapping to working copy
            _workingMapping = CollapseToSingleBindingPerAction(
                _workingMappingProvider?.Invoke() ?? _inputManager.GetKeyMappingSnapshot());
            _navigationMapping = new Dictionary<Keys, InputCommandType>(
                _navigationMappingProvider?.Invoke() ?? _inputManager.GetKeyMappingSnapshot());

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
            if (IsNavigationCommandPressed(current, previous, InputCommandType.MoveUp))
                _selectedIndex = (_selectedIndex - 1 + RowCount) % RowCount;
            else if (IsNavigationCommandPressed(current, previous, InputCommandType.MoveDown))
                _selectedIndex = (_selectedIndex + 1) % RowCount;
            else if (IsNavigationCommandPressed(current, previous, InputCommandType.Activate))
            {
                if (_selectedIndex == FooterSave)
                    CommitAndClose();
                else if (_selectedIndex == FooterCancel)
                    CancelAndClose();
                else
                    _state = CaptureState.AwaitingKey;
            }
            else if (IsNavigationCommandPressed(current, previous, InputCommandType.Back))
                CancelAndClose();
            else if (IsUnbindPressed(current, previous) && _selectedIndex < ActionCount)
                TryUnbindSelectedAction();
        }

        private void TryUnbindSelectedAction()
        {
            var action = Actions[_selectedIndex];
            if (Actions.Contains(action))
            {
                ShowConflict($"{action} must remain bound");
                return;
            }

            RemoveBindingsForAction(action);
        }

        private void HandleKeyCapture(KeyboardState current, KeyboardState previous)
        {
            foreach (var key in current.GetPressedKeys())
            {
                if (!previous.IsKeyDown(key))
                {
                    if (IsCancelKey(key))
                    {
                        _state = CaptureState.Browsing;
                        return;
                    }
                    AssignKey(key);
                    return;
                }
            }
        }

        private void AssignKey(Keys key)
        {
            var targetAction = Actions[_selectedIndex];

            // Check conflicts
            var conflict = KeyConflictChecker.CheckSystemAssignConflict(
                GetLiveDrumBindings(),
                WorkingMappingAsKeyDict(),
                key,
                targetAction);

            if (conflict != null)
            {
                ShowConflict(conflict);
                return;
            }

            RemoveBindingsForAction(targetAction);

            _workingMapping[key] = targetAction;
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

        private IReadOnlyDictionary<Keys, InputCommandType> WorkingMappingAsKeyDict()
        {
            return new Dictionary<Keys, InputCommandType>(_workingMapping);
        }

        private static Dictionary<Keys, InputCommandType> CollapseToSingleBindingPerAction(
            IReadOnlyDictionary<Keys, InputCommandType> source)
        {
            var collapsed = new Dictionary<Keys, InputCommandType>();
            var seenActions = new HashSet<InputCommandType>();

            foreach (var kvp in source)
            {
                if (seenActions.Add(kvp.Value))
                {
                    collapsed[kvp.Key] = kvp.Value;
                }
            }

            return collapsed;
        }

        // Injected by ConfigStage so this panel can check drum key conflicts.
        internal Func<IReadOnlyDictionary<string, int>>? _liveDrumBindingsProvider;
        internal Func<IReadOnlyDictionary<Keys, InputCommandType>>? _workingMappingProvider;
        internal Func<IReadOnlyDictionary<Keys, InputCommandType>>? _navigationMappingProvider;
        internal Func<InputCommandType, bool>? _commandPressedProvider;

        private IReadOnlyDictionary<string, int> GetLiveDrumBindings()
            => _liveDrumBindingsProvider?.Invoke() ?? new Dictionary<string, int>();

        internal IReadOnlyDictionary<Keys, InputCommandType> GetWorkingMappingSnapshot()
            => new Dictionary<Keys, InputCommandType>(WorkingMappingAsKeyDict());

        private bool TryGetDisplayKey(InputCommandType action, out Keys key)
        {
            foreach (var kvp in _workingMapping)
            {
                if (kvp.Value == action)
                {
                    key = kvp.Key;
                    return true;
                }
            }

            key = default;
            return false;
        }

        private void RemoveBindingsForAction(InputCommandType action)
        {
            var keysToRemove = _workingMapping
                .Where(kvp => kvp.Value == action)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in keysToRemove)
            {
                _workingMapping.Remove(key);
            }
        }

        private bool IsNavigationCommandPressed(KeyboardState current, KeyboardState previous, InputCommandType command)
        {
            if (_commandPressedProvider?.Invoke(command) == true)
            {
                return true;
            }

            foreach (var kvp in _navigationMapping)
            {
                if (kvp.Value == command && IsJustPressed(current, previous, kvp.Key))
                    return true;
            }

            return false;
        }

        private bool IsUnbindPressed(KeyboardState current, KeyboardState previous)
        {
            return IsJustPressed(current, previous, Keys.Delete)
                || IsNavigationCommandPressed(current, previous, InputCommandType.MoveLeft);
        }

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

            DrawText(spriteBatch, bitmapFont, "SYSTEM KEY MAPPING", panelX, y, Color.White, false);
            y += rowH + 8;

            for (int i = 0; i < ActionCount; i++)
            {
                var action = Actions[i];
                bool sel = i == _selectedIndex;

                if (sel && whitePixel != null)
                    spriteBatch.Draw(whitePixel, new Rectangle(panelX - 4, y, 600, rowH - 2),
                        new Color(64, 64, 128, 150));

                string keyLabel = (sel && _state == CaptureState.AwaitingKey)
                    ? "[Press any key... Backspace to cancel]"
                    : (TryGetDisplayKey(action, out var k) ? k.ToString() : "(unbound)");

                string rowText = $"{i + 1}. {action,-18}  {keyLabel}";
                DrawText(spriteBatch, bitmapFont, rowText, panelX, y + 6,
                    sel ? Color.Yellow : Color.White, sel);
                y += rowH;
            }

            y += 8;

            // Footer
            DrawFooterRow(spriteBatch, bitmapFont, whitePixel, panelX, y, rowH, "SAVE",
                _selectedIndex == FooterSave);
            y += rowH;
            DrawFooterRow(spriteBatch, bitmapFont, whitePixel, panelX, y, rowH, "CANCEL (ESC)",
                _selectedIndex == FooterCancel);
            y += rowH + 8;

            if (_conflictMessage != null)
                DrawText(spriteBatch, bitmapFont, $"Conflict: {_conflictMessage}",
                    panelX, y, Color.Red, false);

            int instrY = viewportHeight - 28;
            DrawText(spriteBatch, bitmapFont,
                "UP/DOWN: Navigate | ENTER: Assign | DELETE: Unbind | ESC: Cancel",
                panelX, instrY, new Color(180, 180, 180), false);
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
    }
}
