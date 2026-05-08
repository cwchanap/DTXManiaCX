#nullable enable

using System;
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
            _isOpen = false;
            Visible = false;
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

        protected override void OnDraw(SpriteBatch spriteBatch, double deltaTime)
        {
            // Drawing implemented in Task 28.
        }
    }
}
