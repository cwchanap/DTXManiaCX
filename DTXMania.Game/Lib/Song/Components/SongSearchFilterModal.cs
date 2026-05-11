#nullable enable

using System;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Filtering;
using DTXMania.Game.Lib.UI;
using DTXMania.Game.Lib.UI.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using static DTXMania.Game.Lib.UI.Layout.SongSelectionUILayout;

namespace DTXMania.Game.Lib.Song.Components
{
    public class SongSearchFilterModal : UIElement
    {
        public enum Field
        {
            SearchBox = 0,
            MinLevel = 1,
            MaxLevel = 2,
            PlayedStatus = 3,
            SortBy = 4,
            SortDirection = 5,
            ResetButton = 6,
            ApplyButton = 7
        }

        private readonly ITextInputSource _textSource;
        private SongFilterCriteria _draft = SongFilterCriteria.Default;
        private bool _isOpen;
        private bool _subscribedToText;
        private const int FieldCount = 8;
        private Field _focusedField = Field.SearchBox;

        public SongSearchFilterModal(ITextInputSource textSource)
        {
            _textSource = textSource ?? throw new ArgumentNullException(nameof(textSource));
            Visible = false;
        }

        public event EventHandler<SongFilterCriteria>? FilterApplied;
        public event EventHandler? FilterReset;
        public event EventHandler? Cancelled;

        public bool IsOpen => _isOpen;
        public SongFilterCriteria CurrentDraft => _draft;
        public Field FocusedField => _focusedField;

        public bool IsLibraryReady { get; set; } = true;

        public string LoadingHintText { get; set; } = "Library still loading…";

        public void Open(SongFilterCriteria? initial)
        {
            _draft = initial ?? SongFilterCriteria.Default;
            _focusedField = Field.SearchBox;
            _isOpen = true;
            Visible = true;
            // Sync UIElement bounds to the layout constants so HitTest/click routing works
            var layout = SearchFilterModal.Bounds;
            Position = new Microsoft.Xna.Framework.Vector2(layout.X, layout.Y);
            Size = new Microsoft.Xna.Framework.Vector2(layout.Width, layout.Height);
            SubscribeText();
        }

        public void FocusNext()
        {
            int next = ((int)_focusedField + 1) % FieldCount;
            _focusedField = (Field)next;
        }

        public void FocusPrev()
        {
            int prev = ((int)_focusedField - 1 + FieldCount) % FieldCount;
            _focusedField = (Field)prev;
        }

        public void Close()
        {
            UnsubscribeText();
            _isOpen = false;
            Visible = false;
        }

        private void SubscribeText()
        {
            if (_subscribedToText) return;
            _textSource.TextInput += OnTextInput;
            _subscribedToText = true;
        }

        private void UnsubscribeText()
        {
            if (!_subscribedToText) return;
            _textSource.TextInput -= OnTextInput;
            _subscribedToText = false;
        }

        private void OnTextInput(object? sender, TextInputEventArgs e)
        {
            if (!_isOpen) return;
            if (_focusedField != Field.SearchBox) return;

            char c = e.Character;
            if (c == '\b' || c == '\r' || c == '\n' || c == '\t') return;
            if (char.IsControl(c)) return;

            _draft = _draft with { SearchQuery = (_draft.SearchQuery ?? "") + c };
        }

        private void HandleBackspace()
        {
            if (_focusedField != Field.SearchBox) return;
            var q = _draft.SearchQuery ?? "";
            if (q.Length == 0) return;
            _draft = _draft with { SearchQuery = q.Substring(0, q.Length - 1) };
        }

        public void Cancel()
        {
            Cancelled?.Invoke(this, EventArgs.Empty);
            Close();
        }

        public void Apply()
        {
            if (!IsLibraryReady) return;
            FilterApplied?.Invoke(this, _draft);
            Close();
        }

        public void Reset()
        {
            FilterReset?.Invoke(this, EventArgs.Empty);
            Close();
        }

        public void UpdateDraft(SongFilterCriteria? newDraft)
        {
            _draft = newDraft ?? SongFilterCriteria.Default;
        }

        public void SubmitFromSearchBox()
        {
            if (!IsLibraryReady) return;
            if (string.Equals(_draft.SearchQuery, "/q", StringComparison.OrdinalIgnoreCase))
            {
                Reset();
                return;
            }
            Apply();
        }

        public void HandleKey(Microsoft.Xna.Framework.Input.Keys key)
        {
            if (!_isOpen) return;

            switch (key)
            {
                case Microsoft.Xna.Framework.Input.Keys.Escape:
                    Cancel();
                    return;

                case Microsoft.Xna.Framework.Input.Keys.Tab:
                case Microsoft.Xna.Framework.Input.Keys.Down:
                    FocusNext();
                    return;

                case Microsoft.Xna.Framework.Input.Keys.Up:
                    FocusPrev();
                    return;

                case Microsoft.Xna.Framework.Input.Keys.Enter:
                    HandleEnter();
                    return;

                case Microsoft.Xna.Framework.Input.Keys.Left:
                    AdjustFocusedField(-1);
                    return;

                case Microsoft.Xna.Framework.Input.Keys.Right:
                    AdjustFocusedField(+1);
                    return;

                case Microsoft.Xna.Framework.Input.Keys.Back:
                    HandleBackspace();
                    return;
            }
        }

        /// <summary>
        /// Handle a navigational InputCommandType (respects remapped key bindings).
        /// Falls back to the same logic as HandleKey for directional and action commands.
        /// Tab and Backspace are NOT handled here — they are text-editing keys polled
        /// as raw keys because they are not in the InputCommandType system.
        /// </summary>
        public void HandleCommand(InputCommandType command)
        {
            if (!_isOpen) return;

            switch (command)
            {
                case InputCommandType.MoveUp:
                    FocusPrev();
                    return;
                case InputCommandType.MoveDown:
                    FocusNext();
                    return;
                case InputCommandType.MoveLeft:
                    AdjustFocusedField(-1);
                    return;
                case InputCommandType.MoveRight:
                    AdjustFocusedField(+1);
                    return;
                case InputCommandType.Activate:
                    HandleEnter();
                    return;
                case InputCommandType.Back:
                    Cancel();
                    return;
            }
        }

        private void HandleEnter()
        {
            switch (_focusedField)
            {
                case Field.SearchBox:
                    SubmitFromSearchBox();
                    return;
                case Field.ResetButton:
                    Reset();
                    return;
                case Field.ApplyButton:
                    Apply();
                    return;
                default:
                    // On numeric/cycle fields, Enter applies (default action)
                    Apply();
                    return;
            }
        }

        private const int LevelStep = 5;
        private const int LevelMin = 0;
        private const int LevelMax = 99;

        private void AdjustFocusedField(int dir)
        {
            switch (_focusedField)
            {
                case Field.MinLevel:
                    _draft = _draft with { MinLevel = AdjustLevel(_draft.MinLevel, dir) };
                    return;
                case Field.MaxLevel:
                    _draft = _draft with { MaxLevel = AdjustLevel(_draft.MaxLevel, dir) };
                    return;
                case Field.PlayedStatus:
                    _draft = _draft with { PlayedStatus = CycleEnum(_draft.PlayedStatus, dir) };
                    return;
                case Field.SortBy:
                    _draft = _draft with { SortBy = CycleSort(_draft.SortBy, dir) };
                    return;
                case Field.SortDirection:
                    _draft = _draft with { SortDescending = !_draft.SortDescending };
                    return;
            }
        }

        private static int? AdjustLevel(int? current, int dir)
        {
            int next = (current ?? 0) + (dir * LevelStep);
            if (next < LevelMin) next = LevelMin;
            if (next > LevelMax) next = LevelMax;
            return next == 0 ? (int?)null : next;
        }

        private static PlayedStatus CycleEnum(PlayedStatus current, int dir)
        {
            int count = System.Enum.GetValues(typeof(PlayedStatus)).Length;
            int idx = ((int)current + dir + count) % count;
            return (PlayedStatus)idx;
        }

        private static SongSortCriteria CycleSort(SongSortCriteria current, int dir)
        {
            // Modal exposes only Title / Artist / Level (no Genre)
            var allowed = new[]
            {
                SongSortCriteria.Title,
                SongSortCriteria.Artist,
                SongSortCriteria.Level
            };
            int currentIdx = System.Array.IndexOf(allowed, current);
            if (currentIdx < 0) currentIdx = 0;
            int next = (currentIdx + dir + allowed.Length) % allowed.Length;
            return allowed[next];
        }

        /// <summary>
        /// Handle a mouse click at the given position (screen coordinates).
        /// Detects clicks on the Reset/Apply buttons and focuses fields.
        /// Returns true if the click was inside the modal.
        /// </summary>
        public bool HandleClick(Microsoft.Xna.Framework.Point position)
        {
            if (!_isOpen) return false;

            var modalBounds = SearchFilterModal.Bounds;
            if (!modalBounds.Contains(position))
                return false;

            // Check buttons first (they sit on top of other content)
            var resetRect = new Microsoft.Xna.Framework.Rectangle(
                modalBounds.X + SearchFilterModal.ResetButtonX, modalBounds.Y + SearchFilterModal.ButtonRowY,
                SearchFilterModal.ButtonWidth, SearchFilterModal.ButtonHeight);
            var applyRect = new Microsoft.Xna.Framework.Rectangle(
                modalBounds.X + SearchFilterModal.ApplyButtonX, modalBounds.Y + SearchFilterModal.ButtonRowY,
                SearchFilterModal.ButtonWidth, SearchFilterModal.ButtonHeight);

            if (resetRect.Contains(position))
            {
                _focusedField = Field.ResetButton;
                Reset();
                return true;
            }
            if (applyRect.Contains(position))
            {
                _focusedField = Field.ApplyButton;
                Apply();
                return true;
            }

            // Determine focused field from vertical position
            int relY = position.Y - modalBounds.Y;
            _focusedField = FieldFromRelY(relY);
            return true;
        }

        private Field FieldFromRelY(int relY)
        {
            if (relY < SearchFilterModal.FirstRowY + SearchFilterModal.SearchBoxHeight)
                return Field.SearchBox;
            if (relY < SearchFilterModal.LevelRowY + SearchFilterModal.LevelInputHeight)
                return Field.MinLevel;
            if (relY < SearchFilterModal.PlayedRowY + 30)
                return Field.PlayedStatus;
            if (relY < SearchFilterModal.SortRowY + 30)
                return Field.SortBy;
            return Field.SortDirection;
        }

        public Texture2D? WhitePixel { get; set; }
        public SpriteFont? Font { get; set; }

        protected override void OnDraw(SpriteBatch spriteBatch, double deltaTime)
        {
            if (!_isOpen || WhitePixel == null || Font == null) return;

            var modalBounds = SearchFilterModal.Bounds;

            // Dim the screen behind the modal
            spriteBatch.Draw(WhitePixel,
                new Microsoft.Xna.Framework.Rectangle(0, 0,
                    DTXMania.Game.Lib.UI.Layout.SongSelectionUILayout.SongListDisplay.Width,
                    DTXMania.Game.Lib.UI.Layout.SongSelectionUILayout.SongListDisplay.Height),
                new Microsoft.Xna.Framework.Color(0, 0, 0, 160));

            // Modal panel background
            spriteBatch.Draw(WhitePixel, modalBounds,
                new Microsoft.Xna.Framework.Color(28, 28, 32));

            // Title
            DrawText(spriteBatch, "SEARCH & FILTER",
                new Microsoft.Xna.Framework.Vector2(
                    modalBounds.X + (modalBounds.Width - Font.MeasureString("SEARCH & FILTER").X) / 2,
                    modalBounds.Y + 12),
                Microsoft.Xna.Framework.Color.White);

            DrawSearchRow(spriteBatch, modalBounds);
            DrawLevelRow(spriteBatch, modalBounds);
            DrawPlayedRow(spriteBatch, modalBounds);
            DrawSortRow(spriteBatch, modalBounds);
            DrawButtons(spriteBatch, modalBounds);

            if (!IsLibraryReady)
            {
                DrawText(spriteBatch, LoadingHintText,
                    new Microsoft.Xna.Framework.Vector2(modalBounds.X + 24, modalBounds.Y + modalBounds.Height - 24),
                    Microsoft.Xna.Framework.Color.Yellow);
            }
        }

        private void DrawText(SpriteBatch spriteBatch, string text, Microsoft.Xna.Framework.Vector2 pos, Microsoft.Xna.Framework.Color color)
        {
            spriteBatch.DrawString(Font!, text, pos, color);
        }

        private static readonly Microsoft.Xna.Framework.Color FocusedBg = new(60, 60, 80);
        private static readonly Microsoft.Xna.Framework.Color FieldBg   = new(40, 40, 50);

        private void DrawSearchRow(SpriteBatch sb, Microsoft.Xna.Framework.Rectangle modal)
        {
            DrawText(sb, "Search:", new Microsoft.Xna.Framework.Vector2(modal.X + SearchFilterModal.LabelX, modal.Y + SearchFilterModal.SearchBoxY + 6),
                Microsoft.Xna.Framework.Color.LightGray);
            var box = new Microsoft.Xna.Framework.Rectangle(
                modal.X + SearchFilterModal.FieldX, modal.Y + SearchFilterModal.SearchBoxY, SearchFilterModal.SearchBoxWidth, SearchFilterModal.SearchBoxHeight);
            sb.Draw(WhitePixel,
                box,
                _focusedField == Field.SearchBox ? FocusedBg : FieldBg);
            DrawText(sb, _draft.SearchQuery ?? "",
                new Microsoft.Xna.Framework.Vector2(box.X + 6, box.Y + 6),
                Microsoft.Xna.Framework.Color.White);
        }

        private void DrawLevelRow(SpriteBatch sb, Microsoft.Xna.Framework.Rectangle modal)
        {
            DrawText(sb, "Level:", new Microsoft.Xna.Framework.Vector2(modal.X + SearchFilterModal.LabelX, modal.Y + SearchFilterModal.LevelRowY + 6),
                Microsoft.Xna.Framework.Color.LightGray);
            DrawNumeric(sb, modal.X + SearchFilterModal.LevelMinX, modal.Y + SearchFilterModal.LevelRowY,
                SearchFilterModal.LevelInputWidth, SearchFilterModal.LevelInputHeight,
                _draft.MinLevel, _focusedField == Field.MinLevel, "Min");
            DrawNumeric(sb, modal.X + SearchFilterModal.LevelMaxX, modal.Y + SearchFilterModal.LevelRowY,
                SearchFilterModal.LevelInputWidth, SearchFilterModal.LevelInputHeight,
                _draft.MaxLevel, _focusedField == Field.MaxLevel, "Max");
        }

        private void DrawNumeric(SpriteBatch sb, int x, int y, int w, int h, int? value, bool focused, string label)
        {
            sb.Draw(WhitePixel, new Microsoft.Xna.Framework.Rectangle(x, y, w, h),
                focused ? FocusedBg : FieldBg);
            string text = value?.ToString() ?? label;
            DrawText(sb, text, new Microsoft.Xna.Framework.Vector2(x + 6, y + 6),
                value is null ? Microsoft.Xna.Framework.Color.Gray : Microsoft.Xna.Framework.Color.White);
        }

        private void DrawPlayedRow(SpriteBatch sb, Microsoft.Xna.Framework.Rectangle modal)
        {
            DrawText(sb, "Played:", new Microsoft.Xna.Framework.Vector2(modal.X + SearchFilterModal.LabelX, modal.Y + SearchFilterModal.PlayedRowY + 6),
                Microsoft.Xna.Framework.Color.LightGray);
            var rowBg = _focusedField == Field.PlayedStatus ? FocusedBg : FieldBg;
            var box = new Microsoft.Xna.Framework.Rectangle(
                modal.X + SearchFilterModal.FieldX, modal.Y + SearchFilterModal.PlayedRowY, 380, 30);
            sb.Draw(WhitePixel, box, rowBg);
            DrawText(sb, "(< >) " + _draft.PlayedStatus,
                new Microsoft.Xna.Framework.Vector2(box.X + 6, box.Y + 6),
                Microsoft.Xna.Framework.Color.White);
        }

        private void DrawSortRow(SpriteBatch sb, Microsoft.Xna.Framework.Rectangle modal)
        {
            DrawText(sb, "Sort by:", new Microsoft.Xna.Framework.Vector2(modal.X + SearchFilterModal.LabelX, modal.Y + SearchFilterModal.SortRowY + 6),
                Microsoft.Xna.Framework.Color.LightGray);
            var sortBox = new Microsoft.Xna.Framework.Rectangle(
                modal.X + SearchFilterModal.FieldX, modal.Y + SearchFilterModal.SortRowY, 160, 30);
            sb.Draw(WhitePixel, sortBox,
                _focusedField == Field.SortBy ? FocusedBg : FieldBg);
            DrawText(sb, _draft.SortBy.ToString(),
                new Microsoft.Xna.Framework.Vector2(sortBox.X + 6, sortBox.Y + 6),
                Microsoft.Xna.Framework.Color.White);

            var dirBox = new Microsoft.Xna.Framework.Rectangle(
                sortBox.X + sortBox.Width + 20, sortBox.Y, 100, 30);
            sb.Draw(WhitePixel, dirBox,
                _focusedField == Field.SortDirection ? FocusedBg : FieldBg);
            DrawText(sb, _draft.SortDescending ? "Desc" : "Asc",
                new Microsoft.Xna.Framework.Vector2(dirBox.X + 6, dirBox.Y + 6),
                Microsoft.Xna.Framework.Color.White);
        }

        private void DrawButtons(SpriteBatch sb, Microsoft.Xna.Framework.Rectangle modal)
        {
            var resetRect = new Microsoft.Xna.Framework.Rectangle(
                modal.X + SearchFilterModal.ResetButtonX, modal.Y + SearchFilterModal.ButtonRowY, SearchFilterModal.ButtonWidth, SearchFilterModal.ButtonHeight);
            var applyRect = new Microsoft.Xna.Framework.Rectangle(
                modal.X + SearchFilterModal.ApplyButtonX, modal.Y + SearchFilterModal.ButtonRowY, SearchFilterModal.ButtonWidth, SearchFilterModal.ButtonHeight);
            sb.Draw(WhitePixel, resetRect,
                _focusedField == Field.ResetButton ? FocusedBg : FieldBg);
            sb.Draw(WhitePixel, applyRect,
                _focusedField == Field.ApplyButton ? FocusedBg : FieldBg);
            DrawText(sb, "Reset",
                new Microsoft.Xna.Framework.Vector2(resetRect.X + 36, resetRect.Y + 8),
                Microsoft.Xna.Framework.Color.White);
            DrawText(sb, "Apply",
                new Microsoft.Xna.Framework.Vector2(applyRect.X + 36, applyRect.Y + 8),
                Microsoft.Xna.Framework.Color.White);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                UnsubscribeText();
            base.Dispose(disposing);
        }
    }
}
