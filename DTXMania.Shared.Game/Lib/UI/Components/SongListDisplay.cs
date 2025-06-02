using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using DTX.UI;
using DTX.Song;
using DTX.Input;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DTX.UI.Components
{
    /// <summary>
    /// DTXManiaNX-compatible song list display with smooth scrolling and 13-item window
    /// Equivalent to CActSelectSongList from DTXManiaNX
    /// </summary>
    public class SongListDisplay : UIElement
    {
        #region Constants

        private const int VISIBLE_ITEMS = 13;
        private const int CENTER_INDEX = 6;
        private const int SCROLL_UNIT = 100;
        private const float SCROLL_ACCELERATION_THRESHOLD_1 = 100f;
        private const float SCROLL_ACCELERATION_THRESHOLD_2 = 300f;
        private const float SCROLL_ACCELERATION_THRESHOLD_3 = 500f;

        #endregion

        #region Fields

        private List<SongListNode> _currentList;
        private int _selectedIndex;
        private int _targetScrollCounter;
        private int _currentScrollCounter;
        private Dictionary<int, Texture2D> _titleBarCache;
        private Dictionary<int, Texture2D> _previewImageCache;
        private int _currentDifficulty;
        private SpriteFont _font;
        private Texture2D _whitePixel;

        // Visual properties
        private Color _backgroundColor = Color.Black * 0.7f;
        private Color _selectedItemColor = Color.Blue * 0.8f;
        private Color _textColor = Color.White;
        private Color _selectedTextColor = Color.Yellow;
        private float _itemHeight = 30f;

        #endregion

        #region Properties

        /// <summary>
        /// Currently selected song node
        /// </summary>
        public SongListNode SelectedSong { get; private set; }

        /// <summary>
        /// Current difficulty level (0-4)
        /// </summary>
        public int CurrentDifficulty
        {
            get => _currentDifficulty;
            set => _currentDifficulty = Math.Max(0, Math.Min(4, value));
        }

        /// <summary>
        /// Whether the list is currently scrolling
        /// </summary>
        public bool IsScrolling => _targetScrollCounter != 0 || _currentScrollCounter != 0;

        /// <summary>
        /// Current song list
        /// </summary>
        public List<SongListNode> CurrentList
        {
            get => _currentList;
            set
            {
                _currentList = value ?? new List<SongListNode>();
                _selectedIndex = 0;
                _targetScrollCounter = 0;
                _currentScrollCounter = 0;
                UpdateSelection();
            }
        }

        /// <summary>
        /// Selected index in the current list
        /// </summary>
        public int SelectedIndex
        {
            get => _selectedIndex;
            set
            {
                if (_currentList == null || _currentList.Count == 0)
                    return;

                var newIndex = Math.Max(0, Math.Min(_currentList.Count - 1, value));
                if (newIndex != _selectedIndex)
                {
                    _selectedIndex = newIndex;
                    UpdateScrollTarget();
                    UpdateSelection();
                }
            }
        }

        /// <summary>
        /// Font for text rendering
        /// </summary>
        public SpriteFont Font
        {
            get => _font;
            set => _font = value;
        }

        /// <summary>
        /// White pixel texture for backgrounds
        /// </summary>
        public Texture2D WhitePixel
        {
            get => _whitePixel;
            set => _whitePixel = value;
        }

        #endregion

        #region Events

        /// <summary>
        /// Fired when the selected song changes
        /// </summary>
        public event EventHandler<SongSelectionChangedEventArgs> SelectionChanged;

        /// <summary>
        /// Fired when the difficulty changes
        /// </summary>
        public event EventHandler<DifficultyChangedEventArgs> DifficultyChanged;

        /// <summary>
        /// Fired when a song is activated (Enter pressed)
        /// </summary>
        public event EventHandler<SongActivatedEventArgs> SongActivated;

        #endregion

        #region Constructor

        public SongListDisplay()
        {
            _currentList = new List<SongListNode>();
            _titleBarCache = new Dictionary<int, Texture2D>();
            _previewImageCache = new Dictionary<int, Texture2D>();
            _selectedIndex = 0;
            _currentDifficulty = 0;

            Size = new Vector2(700, VISIBLE_ITEMS * _itemHeight);
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Move selection to next song
        /// </summary>
        public void MoveNext()
        {
            if (_currentList == null || _currentList.Count == 0)
                return;

            SelectedIndex = (_selectedIndex + 1) % _currentList.Count;
        }

        /// <summary>
        /// Move selection to previous song
        /// </summary>
        public void MovePrevious()
        {
            if (_currentList == null || _currentList.Count == 0)
                return;

            SelectedIndex = (_selectedIndex - 1 + _currentList.Count) % _currentList.Count;
        }

        /// <summary>
        /// Cycle through available difficulties
        /// </summary>
        public void CycleDifficulty()
        {
            if (SelectedSong?.Scores == null)
                return;

            // Find next available difficulty
            int startDifficulty = _currentDifficulty;
            do
            {
                _currentDifficulty = (_currentDifficulty + 1) % 5;
            }
            while (_currentDifficulty != startDifficulty && 
                   (SelectedSong.Scores.Length <= _currentDifficulty || SelectedSong.Scores[_currentDifficulty] == null));

            DifficultyChanged?.Invoke(this, new DifficultyChangedEventArgs(SelectedSong, _currentDifficulty));
        }

        /// <summary>
        /// Activate the currently selected song
        /// </summary>
        public void ActivateSelected()
        {
            if (SelectedSong != null)
            {
                SongActivated?.Invoke(this, new SongActivatedEventArgs(SelectedSong, _currentDifficulty));
            }
        }

        /// <summary>
        /// Refresh the display (clear caches)
        /// </summary>
        public void RefreshDisplay()
        {
            _titleBarCache.Clear();
            _previewImageCache.Clear();
        }

        #endregion

        #region Protected Methods

        protected override void OnUpdate(double deltaTime)
        {
            base.OnUpdate(deltaTime);

            // Update smooth scrolling animation
            UpdateScrollAnimation(deltaTime);
        }

        protected override void OnDraw(SpriteBatch spriteBatch, double deltaTime)
        {
            if (!Visible || _currentList == null)
                return;

            var bounds = Bounds;

            // Draw background
            if (_whitePixel != null)
            {
                spriteBatch.Draw(_whitePixel, bounds, _backgroundColor);
            }

            // Draw song items
            DrawSongItems(spriteBatch, bounds);

            base.OnDraw(spriteBatch, deltaTime);
        }

        #endregion

        #region Private Methods

        private void UpdateScrollTarget()
        {
            // Calculate target scroll position to center selected item
            _targetScrollCounter = _selectedIndex * SCROLL_UNIT;
        }

        private void UpdateScrollAnimation(double deltaTime)
        {
            if (_targetScrollCounter == _currentScrollCounter)
                return;

            // Calculate scroll distance and acceleration
            int distance = Math.Abs(_targetScrollCounter - _currentScrollCounter);
            int acceleration = GetScrollAcceleration(distance);

            // Move towards target
            if (_targetScrollCounter > _currentScrollCounter)
            {
                _currentScrollCounter = Math.Min(_targetScrollCounter, _currentScrollCounter + acceleration);
            }
            else
            {
                _currentScrollCounter = Math.Max(_targetScrollCounter, _currentScrollCounter - acceleration);
            }
        }

        private int GetScrollAcceleration(int distance)
        {
            // DTXManiaNX-style acceleration based on distance
            if (distance <= SCROLL_ACCELERATION_THRESHOLD_1)
                return 2;
            else if (distance <= SCROLL_ACCELERATION_THRESHOLD_2)
                return 3;
            else if (distance <= SCROLL_ACCELERATION_THRESHOLD_3)
                return 4;
            else
                return 8;
        }

        private void DrawSongItems(SpriteBatch spriteBatch, Rectangle bounds)
        {
            if (_currentList.Count == 0)
            {
                // Draw "No songs" message
                if (_font != null)
                {
                    var message = "No songs found";
                    var messageSize = _font.MeasureString(message);
                    var messagePos = new Vector2(
                        bounds.X + (bounds.Width - messageSize.X) / 2,
                        bounds.Y + (bounds.Height - messageSize.Y) / 2
                    );
                    spriteBatch.DrawString(_font, message, messagePos, _textColor);
                }
                return;
            }

            // Calculate visible range based on scroll position
            int centerItem = _currentScrollCounter / SCROLL_UNIT;
            int startItem = Math.Max(0, centerItem - CENTER_INDEX);
            int endItem = Math.Min(_currentList.Count, startItem + VISIBLE_ITEMS);

            // Draw visible items
            for (int i = startItem; i < endItem; i++)
            {
                var node = _currentList[i];
                var itemBounds = new Rectangle(
                    bounds.X,
                    bounds.Y + (i - startItem) * (int)_itemHeight,
                    bounds.Width,
                    (int)_itemHeight
                );

                DrawSongItem(spriteBatch, node, itemBounds, i == _selectedIndex);
            }
        }

        private void DrawSongItem(SpriteBatch spriteBatch, SongListNode node, Rectangle itemBounds, bool isSelected)
        {
            // Draw item background
            if (_whitePixel != null && isSelected)
            {
                spriteBatch.Draw(_whitePixel, itemBounds, _selectedItemColor);
            }

            // Draw text
            if (_font != null)
            {
                var text = GetDisplayText(node);
                var textColor = isSelected ? _selectedTextColor : _textColor;
                var textPos = new Vector2(itemBounds.X + 10, itemBounds.Y + 5);
                
                spriteBatch.DrawString(_font, text, textPos, textColor);
            }
        }

        private string GetDisplayText(SongListNode node)
        {
            switch (node.Type)
            {
                case NodeType.BackBox:
                    return ".. (Back)";
                case NodeType.Box:
                    return $"[{node.DisplayTitle}]";
                case NodeType.Random:
                    return "*** RANDOM SELECT ***";
                case NodeType.Score:
                default:
                    return node.DisplayTitle ?? "Unknown Song";
            }
        }

        private void UpdateSelection()
        {
            var previousSong = SelectedSong;
            SelectedSong = (_currentList != null && _selectedIndex >= 0 && _selectedIndex < _currentList.Count) 
                ? _currentList[_selectedIndex] 
                : null;

            if (SelectedSong != previousSong)
            {
                SelectionChanged?.Invoke(this, new SongSelectionChangedEventArgs(SelectedSong, _currentDifficulty));
            }
        }

        #endregion
    }

    #region Event Args

    public class SongSelectionChangedEventArgs : EventArgs
    {
        public SongListNode SelectedSong { get; }
        public int CurrentDifficulty { get; }

        public SongSelectionChangedEventArgs(SongListNode selectedSong, int currentDifficulty)
        {
            SelectedSong = selectedSong;
            CurrentDifficulty = currentDifficulty;
        }
    }

    public class DifficultyChangedEventArgs : EventArgs
    {
        public SongListNode Song { get; }
        public int NewDifficulty { get; }

        public DifficultyChangedEventArgs(SongListNode song, int newDifficulty)
        {
            Song = song;
            NewDifficulty = newDifficulty;
        }
    }

    public class SongActivatedEventArgs : EventArgs
    {
        public SongListNode Song { get; }
        public int Difficulty { get; }

        public SongActivatedEventArgs(SongListNode song, int difficulty)
        {
            Song = song;
            Difficulty = difficulty;
        }
    }

    #endregion
}
