#nullable enable

using System;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Filtering;
using DTXMania.Game.Lib.UI;
using DTXMania.Game.Lib.UI.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

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

        public void Open(SongFilterCriteria initial)
        {
            _draft = initial ?? SongFilterCriteria.Default;
            _focusedField = Field.SearchBox;
            _isOpen = true;
            Visible = true;
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
            FilterApplied?.Invoke(this, _draft);
            Close();
        }

        public void Reset()
        {
            FilterReset?.Invoke(this, EventArgs.Empty);
            Close();
        }

        public void UpdateDraft(SongFilterCriteria newDraft)
        {
            _draft = newDraft ?? SongFilterCriteria.Default;
        }

        public void SubmitFromSearchBox()
        {
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
            int count = 4; // All, Unplayed, Played, Cleared
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

        protected override void OnDraw(SpriteBatch spriteBatch, double deltaTime)
        {
            // Drawing implemented in Task 28.
        }
    }
}
