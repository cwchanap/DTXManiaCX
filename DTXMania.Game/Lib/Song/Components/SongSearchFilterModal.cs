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
        private readonly ITextInputSource _textSource;
        private SongFilterCriteria _draft = SongFilterCriteria.Default;
        private bool _isOpen;

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

        public void Open(SongFilterCriteria initial)
        {
            _draft = initial ?? SongFilterCriteria.Default;
            _isOpen = true;
            Visible = true;
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
