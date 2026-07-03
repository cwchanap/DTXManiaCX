#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
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
            InputCommandType.IncreaseScrollSpeed,
            InputCommandType.DecreaseScrollSpeed,
        };

        private static readonly int ActionCount = Actions.Length;
        private static readonly int FooterSave = ActionCount;
        private static readonly int FooterCancel = ActionCount + 1;
        private static readonly int RowCount = ActionCount + 2;
        private const double ConflictDuration = 2.0;

        // Config-aligned palette (mirrors ConfigStage / ConfigUILayout) so the sub-panel reads as
        // part of the same stage: a dark framed board with a purple border, light text, and a warm
        // highlight on the selected row.
        private static readonly Color BackdropColor = new(0, 0, 0, 200);
        private static readonly Color BoardColor = new(14, 16, 34, 236);
        private static readonly Color BoardBorderColor = new(74, 62, 150, 235);
        private static readonly Color RowFillColor = new(30, 34, 60, 220);
        private static readonly Color SelectionBarColor = new(120, 92, 30, 190);
        private static readonly Color TitleColor = new(235, 238, 248);
        private static readonly Color RowNameColor = new(235, 238, 248);
        private static readonly Color RowKeyColor = new(190, 205, 232);
        private static readonly Color SelectedTextColor = new(255, 238, 120);
        private static readonly Color AwaitingKeyColor = new(120, 220, 255);
        private static readonly Color ConflictColor = new(255, 96, 96);
        private static readonly Color InstructionColor = new(170, 180, 205);

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
            _workingMapping = new Dictionary<Keys, InputCommandType>(
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

            if (KeyConflictChecker.IsRequiredCommand(action))
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

            if (_workingMapping.TryGetValue(key, out var existingAction) && existingAction == targetAction)
            {
                _state = CaptureState.Browsing;
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

        // Injected by ConfigStage so this panel can check drum key conflicts.
        internal Func<IReadOnlyDictionary<string, int>>? _liveDrumBindingsProvider;
        internal Func<IReadOnlyDictionary<Keys, InputCommandType>>? _workingMappingProvider;
        internal Func<IReadOnlyDictionary<Keys, InputCommandType>>? _navigationMappingProvider;
        internal Func<InputCommandType, bool>? _commandPressedProvider;

        private IReadOnlyDictionary<string, int> GetLiveDrumBindings()
            => _liveDrumBindingsProvider?.Invoke() ?? new Dictionary<string, int>();

        internal IReadOnlyDictionary<Keys, InputCommandType> GetWorkingMappingSnapshot()
            => new Dictionary<Keys, InputCommandType>(WorkingMappingAsKeyDict());

        private string GetDisplayKeyLabel(InputCommandType action)
        {
            var keys = _workingMapping
                .Where(kvp => kvp.Value == action)
                .Select(kvp => kvp.Key.ToString())
                .Distinct()
                .ToArray();

            return keys.Length == 0 ? "(unbound)" : string.Join(", ", keys);
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

        internal string GetFooterCancelLabel()
        {
            var cancelBindingLabel = GetCancelBindingLabel();
            return cancelBindingLabel.Length == 0 ? "CANCEL" : $"CANCEL ({cancelBindingLabel})";
        }

        internal string GetInstructionText()
        {
            var navigateUpBindingLabel = GetNavigationBindingLabel(InputCommandType.MoveUp);
            var navigateDownBindingLabel = GetNavigationBindingLabel(InputCommandType.MoveDown);
            var assignBindingLabel = GetNavigationBindingLabel(InputCommandType.Activate);
            var cancelBindingLabel = GetCancelBindingLabel();
            var navigateInstruction = $"{(navigateUpBindingLabel.Length == 0 ? "UP" : navigateUpBindingLabel)}/{(navigateDownBindingLabel.Length == 0 ? "DOWN" : navigateDownBindingLabel)}: Navigate";
            var assignInstruction = $"{(assignBindingLabel.Length == 0 ? "ENTER" : assignBindingLabel)}: Assign";
            var cancelInstruction = cancelBindingLabel.Length == 0 ? "BACK: Cancel" : $"{cancelBindingLabel}: Cancel";
            return $"{navigateInstruction} | {assignInstruction} | {cancelInstruction}";
        }

        private string GetCancelBindingLabel()
        {
            return GetNavigationBindingLabel(InputCommandType.Back);
        }

        private string GetNavigationBindingLabel(InputCommandType command)
        {
            return string.Join("/", _navigationMapping
                .Where(kvp => kvp.Value == command)
                .Select(kvp => kvp.Key.ToString().ToUpperInvariant())
                .Distinct());
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

        public void Draw(SpriteBatch spriteBatch, IFont? font, IFont? boldFont, Texture2D? whitePixel,
                         int viewportWidth, int viewportHeight)
        {
            if (!IsActive || spriteBatch == null) return;

            // Dim the whole stage behind the panel.
            if (whitePixel != null)
                DrawWhitePixel(spriteBatch, whitePixel, new Rectangle(0, 0, viewportWidth, viewportHeight), BackdropColor);

            // Centered framed board (all coordinates in the same scaled space as the config stage).
            const int boardW = 720;
            const int boardH = 540;
            int boardX = (viewportWidth - boardW) / 2;
            int boardY = (viewportHeight - boardH) / 2;

            if (whitePixel != null)
            {
                DrawWhitePixel(spriteBatch, whitePixel, new Rectangle(boardX - 4, boardY - 4, boardW + 8, boardH + 8), BoardBorderColor);
                DrawWhitePixel(spriteBatch, whitePixel, new Rectangle(boardX, boardY, boardW, boardH), BoardColor);
            }

            // Title, centered on the board.
            const string title = "SYSTEM KEY MAPPING";
            var titleFont = boldFont ?? font;
            if (titleFont != null)
            {
                var size = titleFont.MeasureString(title);
                titleFont.DrawString(spriteBatch, title,
                    new Vector2(boardX + (boardW - size.X) / 2f, boardY + 20), TitleColor);
            }

            const int rowH = 40;
            int nameX = boardX + 40;
            int keyX = boardX + 360;
            int y = boardY + 78;

            for (int i = 0; i < ActionCount; i++)
            {
                var action = Actions[i];
                bool sel = i == _selectedIndex;

                if (sel && whitePixel != null)
                    DrawWhitePixel(spriteBatch, whitePixel, new Rectangle(boardX + 20, y, boardW - 40, rowH - 6), SelectionBarColor);

                bool awaiting = sel && _state == CaptureState.AwaitingKey;
                string keyLabel = awaiting
                    ? "[Press a key... Backspace cancels]"
                    : GetDisplayKeyLabel(action);

                DrawText(spriteBatch, font, boldFont, FormatActionName(action), nameX, y + 8,
                    sel ? SelectedTextColor : RowNameColor, sel);
                DrawText(spriteBatch, font, boldFont, keyLabel, keyX, y + 8,
                    awaiting ? AwaitingKeyColor : (sel ? SelectedTextColor : RowKeyColor), sel);
                y += rowH;
            }

            y += 14;

            // Footer buttons: SAVE and CANCEL, side by side and centered.
            const int btnW = 210;
            const int btnGap = 40;
            int totalW = btnW * 2 + btnGap;
            int saveX = boardX + (boardW - totalW) / 2;
            DrawFooterButton(spriteBatch, font, boldFont, whitePixel, saveX, y, btnW, rowH, "SAVE",
                _selectedIndex == FooterSave);
            DrawFooterButton(spriteBatch, font, boldFont, whitePixel, saveX + btnW + btnGap, y, btnW, rowH,
                GetFooterCancelLabel(), _selectedIndex == FooterCancel);
            y += rowH + 16;

            if (_conflictMessage != null && font != null)
            {
                var msg = $"Conflict: {_conflictMessage}";
                var size = font.MeasureString(msg);
                font.DrawString(spriteBatch, msg, new Vector2(boardX + (boardW - size.X) / 2f, y), ConflictColor);
            }

            // Instruction line pinned near the board bottom.
            if (font != null)
            {
                var instr = GetInstructionText();
                var size = font.MeasureString(instr);
                font.DrawString(spriteBatch, instr,
                    new Vector2(boardX + (boardW - size.X) / 2f, boardY + boardH - 34), InstructionColor);
            }
        }

        private void DrawFooterButton(SpriteBatch sb, IFont? font, IFont? boldFont, Texture2D? wp,
            int x, int y, int w, int h, string label, bool selected)
        {
            if (wp != null)
            {
                DrawWhitePixel(sb, wp, new Rectangle(x - 3, y - 3, w + 6, h + 6), BoardBorderColor);
                DrawWhitePixel(sb, wp, new Rectangle(x, y, w, h), selected ? SelectionBarColor : RowFillColor);
            }

            var picked = boldFont ?? font;
            if (picked == null) return;
            var size = picked.MeasureString(label);
            picked.DrawString(sb, label,
                new Vector2(x + (w - size.X) / 2f, y + (h - size.Y) / 2f),
                selected ? SelectedTextColor : RowNameColor);
        }

        /// <summary>
        /// Draws a <paramref name="whitePixel"/> filled rectangle. Extracted as a virtual seam so
        /// headless tests can override it to record draw calls instead of requiring a live
        /// <see cref="SpriteBatch"/> (which needs a <see cref="GraphicsDevice"/>).
        /// </summary>
        [ExcludeFromCodeCoverage]
        protected virtual void DrawWhitePixel(SpriteBatch spriteBatch, Texture2D whitePixel,
            Rectangle rectangle, Color color)
        {
            spriteBatch.Draw(whitePixel, rectangle, color);
        }

        // Humanizes an action enum name for display: "MoveUp" -> "Move Up".
        private static string FormatActionName(InputCommandType action)
        {
            var s = action.ToString();
            var sb = new System.Text.StringBuilder(s.Length + 4);
            for (int i = 0; i < s.Length; i++)
            {
                if (i > 0 && char.IsUpper(s[i]) && !char.IsUpper(s[i - 1]))
                    sb.Append(' ');
                sb.Append(s[i]);
            }
            return sb.ToString();
        }

        private static void DrawText(SpriteBatch sb, IFont? font, IFont? boldFont, string text, int x, int y,
            Color color, bool bold)
        {
            var picked = bold ? (boldFont ?? font) : font;
            if (picked == null) return;
            picked.DrawString(sb, text, new Vector2(x, y), color);
        }

        private static bool IsJustPressed(KeyboardState cur, KeyboardState prev, Keys key)
            => cur.IsKeyDown(key) && !prev.IsKeyDown(key);
    }
}
