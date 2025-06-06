using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using DTX.UI;
using DTX.Song;
using DTX.Input;
using DTX.Resources;
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
        private const int CENTER_INDEX = 5; // DTXManiaNX uses index 5 as center (0-based)
        private const int SCROLL_UNIT = 100;
        private const float SCROLL_ACCELERATION_THRESHOLD_1 = 100f;
        private const float SCROLL_ACCELERATION_THRESHOLD_2 = 300f;
        private const float SCROLL_ACCELERATION_THRESHOLD_3 = 500f;

        // DTXManiaNX Curved Layout Coordinates (ptバーの基本座標)
        // These are the exact coordinates from DTXManiaNX for authentic curved layout
        private static readonly Point[] CurvedBarCoordinates = new Point[]
        {
            new Point(708, 5),      // Bar 0 (top)
            new Point(626, 56),     // Bar 1
            new Point(578, 107),    // Bar 2
            new Point(546, 158),    // Bar 3
            new Point(528, 209),    // Bar 4
            new Point(464, 270),    // Bar 5 (CENTER/SELECTED) ← KEY POSITION
            new Point(548, 362),    // Bar 6
            new Point(578, 413),    // Bar 7
            new Point(624, 464),    // Bar 8
            new Point(686, 515),    // Bar 9
            new Point(788, 566),    // Bar 10
            new Point(996, 617),    // Bar 11
            new Point(1280, 668)    // Bar 12 (bottom)
        };

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
        private IFont _managedFont;
        private Texture2D _whitePixel;

        // Visual properties
        private Color _backgroundColor = Color.Black * 0.7f;
        private Color _selectedItemColor = Color.Blue * 0.8f;
        private Color _textColor = Color.White;
        private Color _selectedTextColor = Color.Yellow;
        private float _itemHeight = 30f;

        // Enhanced Phase 4 components
        private SongBarRenderer _barRenderer;
        private readonly Dictionary<int, SongBar> _songBarCache;
        private bool _useEnhancedRendering = true;
        private DefaultGraphicsGenerator _graphicsGenerator;

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
            set
            {
                _font = value;
                _barRenderer?.SetFont(value);
            }
        }

        /// <summary>
        /// White pixel texture for backgrounds
        /// </summary>
        public Texture2D WhitePixel
        {
            get => _whitePixel;
            set => _whitePixel = value;
        }

        /// <summary>
        /// Managed font for advanced text rendering
        /// </summary>
        public IFont ManagedFont
        {
            get => _managedFont;
            set
            {
                _managedFont = value;
                _font = value?.SpriteFont; // Update SpriteFont reference
                _barRenderer?.SetFont(_font);
            }
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

            // Initialize Phase 4 enhanced components
            _songBarCache = new Dictionary<int, SongBar>();

            Size = new Vector2(700, VISIBLE_ITEMS * _itemHeight);
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Move selection to next song (DTXManiaNX curved layout style)
        /// </summary>
        public void MoveNext()
        {
            if (_currentList == null || _currentList.Count == 0)
                return;

            // Move to next song with proper wrap-around
            _selectedIndex = (_selectedIndex + 1) % _currentList.Count;

            // Update scroll target immediately for responsive feel
            _targetScrollCounter = _selectedIndex * SCROLL_UNIT;

            UpdateSelection();
        }

        /// <summary>
        /// Move selection to previous song (DTXManiaNX curved layout style)
        /// </summary>
        public void MovePrevious()
        {
            if (_currentList == null || _currentList.Count == 0)
                return;

            // Move to previous song with proper wrap-around
            _selectedIndex = (_selectedIndex - 1 + _currentList.Count) % _currentList.Count;

            // Update scroll target immediately for responsive feel
            _targetScrollCounter = _selectedIndex * SCROLL_UNIT;

            UpdateSelection();
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
            _songBarCache.Clear();
            _barRenderer?.ClearCache();
        }

        /// <summary>
        /// Initialize enhanced rendering with SongBarRenderer
        /// </summary>
        public void InitializeEnhancedRendering(GraphicsDevice graphicsDevice, IResourceManager resourceManager)
        {
            _barRenderer?.Dispose();
            _barRenderer = new SongBarRenderer(graphicsDevice, resourceManager);

            if (_font != null)
            {
                _barRenderer.SetFont(_font);
            }

            // Initialize graphics generator for default styling
            _graphicsGenerator?.Dispose();
            _graphicsGenerator = new DefaultGraphicsGenerator(graphicsDevice);

            // Initialize graphics generators for cached song bars
            foreach (var songBar in _songBarCache.Values)
            {
                songBar.InitializeGraphicsGenerator(graphicsDevice);
            }
        }

        /// <summary>
        /// Enable or disable enhanced rendering
        /// </summary>
        public void SetEnhancedRendering(bool enabled)
        {
            _useEnhancedRendering = enabled;
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
            // In DTXManiaNX curved layout, the target scroll counter directly corresponds to the selected song
            // The center position (bar 5) always shows the selected song
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
                var message = "No songs found";

                if (_font != null)
                {
                    var messageSize = _font.MeasureString(message);
                    var messagePos = new Vector2(
                        bounds.X + (bounds.Width - messageSize.X) / 2,
                        bounds.Y + (bounds.Height - messageSize.Y) / 2
                    );
                    spriteBatch.DrawString(_font, message, messagePos, _textColor);
                }
                else if (_managedFont != null)
                {
                    var messageSize = _managedFont.MeasureString(message);
                    var messagePos = new Vector2(
                        bounds.X + (bounds.Width - messageSize.X) / 2,
                        bounds.Y + (bounds.Height - messageSize.Y) / 2
                    );
                    _managedFont.DrawString(spriteBatch, message, messagePos, _textColor);
                }
                return;
            }

            // Calculate center song index based on scroll position
            int centerSongIndex = _currentScrollCounter / SCROLL_UNIT;

            // Draw 13 visible bars using DTXManiaNX curved coordinates
            for (int barIndex = 0; barIndex < VISIBLE_ITEMS; barIndex++)
            {
                // Calculate which song should be displayed at this bar position
                int songIndex = centerSongIndex + (barIndex - CENTER_INDEX);

                // Skip if song index is out of bounds
                if (songIndex < 0 || songIndex >= _currentList.Count)
                    continue;

                var node = _currentList[songIndex];
                var barCoords = CurvedBarCoordinates[barIndex];

                // Create item bounds using curved coordinates
                var itemBounds = new Rectangle(
                    barCoords.X,
                    barCoords.Y,
                    500, // Wider bar width for better coverage
                    (int)_itemHeight
                );

                // Bar 5 (CENTER_INDEX) is always the selected position in DTXManiaNX
                bool isSelected = (barIndex == CENTER_INDEX);
                bool isCenter = isSelected; // Center bar is the selected bar

                DrawSongItem(spriteBatch, node, itemBounds, isSelected, isCenter, barIndex);
            }
        }

        private void DrawSongItem(SpriteBatch spriteBatch, SongListNode node, Rectangle itemBounds, bool isSelected, bool isCenter = false, int barIndex = 0)
        {
            // Use enhanced rendering only if we have both a renderer and a SpriteFont
            if (_useEnhancedRendering && _barRenderer != null && _font != null)
            {
                DrawEnhancedSongItem(spriteBatch, node, itemBounds, isSelected, isCenter, barIndex);
            }
            else
            {
                DrawBasicSongItem(spriteBatch, node, itemBounds, isSelected, isCenter, barIndex);
            }
        }

        private void DrawEnhancedSongItem(SpriteBatch spriteBatch, SongListNode node, Rectangle itemBounds, bool isSelected, bool isCenter, int barIndex)
        {
            // Get or create song bar for this item
            var songBar = GetOrCreateSongBar(node, itemBounds, isSelected);

            // Update song bar state with DTXManiaNX curved layout information
            songBar.Position = new Vector2(itemBounds.X, itemBounds.Y);
            songBar.Size = new Vector2(itemBounds.Width, itemBounds.Height);
            songBar.IsSelected = isSelected;
            songBar.IsCenter = isCenter;
            songBar.CurrentDifficulty = _currentDifficulty;

            // Generate textures if needed
            var titleTexture = _barRenderer.GenerateTitleTexture(node);
            var previewTexture = _barRenderer.GeneratePreviewImageTexture(node);
            var clearLampTexture = _barRenderer.GenerateClearLampTexture(node, _currentDifficulty);

            songBar.SetTextures(titleTexture, previewTexture, clearLampTexture);

            // Draw the song bar
            songBar.Draw(spriteBatch, 0);
        }

        private void DrawBasicSongItem(SpriteBatch spriteBatch, SongListNode node, Rectangle itemBounds, bool isSelected, bool isCenter, int barIndex)
        {
            // Draw item background with DTXManiaNX curved layout styling
            if (_whitePixel != null)
            {
                // Use different colors for center vs selected vs normal bars
                Color backgroundColor;
                if (isCenter)
                {
                    backgroundColor = Color.Gold * 0.8f; // Center bar gets gold highlight
                }
                else if (isSelected)
                {
                    backgroundColor = _selectedItemColor;
                }
                else
                {
                    backgroundColor = Color.DarkBlue * 0.3f; // Normal bars get subtle background
                }

                spriteBatch.Draw(_whitePixel, itemBounds, backgroundColor);

                // Draw border for center bar
                if (isCenter && _whitePixel != null)
                {
                    var borderColor = Color.Yellow;
                    var borderThickness = 2;

                    // Top border
                    spriteBatch.Draw(_whitePixel, new Rectangle(itemBounds.X, itemBounds.Y, itemBounds.Width, borderThickness), borderColor);
                    // Bottom border
                    spriteBatch.Draw(_whitePixel, new Rectangle(itemBounds.X, itemBounds.Bottom - borderThickness, itemBounds.Width, borderThickness), borderColor);
                    // Left border
                    spriteBatch.Draw(_whitePixel, new Rectangle(itemBounds.X, itemBounds.Y, borderThickness, itemBounds.Height), borderColor);
                    // Right border
                    spriteBatch.Draw(_whitePixel, new Rectangle(itemBounds.Right - borderThickness, itemBounds.Y, borderThickness, itemBounds.Height), borderColor);
                }
            }

            // Draw text
            var text = GetDisplayText(node);
            var textColor = isCenter ? Color.White : (isSelected ? _selectedTextColor : _textColor);
            var textPos = new Vector2(itemBounds.X + 10, itemBounds.Y + 5);

            if (_font != null)
            {
                spriteBatch.DrawString(_font, text, textPos, textColor);
            }
            else if (_managedFont != null)
            {
                // Use managed font's drawing method
                _managedFont.DrawString(spriteBatch, text, textPos, textColor);
            }
            else
            {
                // Fallback: draw a simple rectangle to show the item exists
                if (_whitePixel != null)
                {
                    var fallbackColor = isSelected ? Color.Yellow : Color.Gray;
                    var textBounds = new Rectangle(itemBounds.X + 10, itemBounds.Y + 5, itemBounds.Width - 20, itemBounds.Height - 10);
                    spriteBatch.Draw(_whitePixel, textBounds, fallbackColor * 0.5f);
                }
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

        private SongBar GetOrCreateSongBar(SongListNode node, Rectangle bounds, bool isSelected)
        {
            var cacheKey = node.GetHashCode();

            if (!_songBarCache.TryGetValue(cacheKey, out var songBar))
            {
                songBar = new SongBar
                {
                    SongNode = node,
                    Font = _font,
                    WhitePixel = _whitePixel
                };
                _songBarCache[cacheKey] = songBar;
            }

            return songBar;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _barRenderer?.Dispose();
                _graphicsGenerator?.Dispose();

                foreach (var songBar in _songBarCache.Values)
                {
                    songBar?.Dispose();
                }
                _songBarCache.Clear();
            }

            base.Dispose(disposing);
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
