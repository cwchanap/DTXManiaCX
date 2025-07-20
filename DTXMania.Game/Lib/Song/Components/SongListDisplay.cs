using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using DTX.UI;
using DTX.UI.Layout;
using DTX.Song;
using DTX.Input;
using DTX.Resources;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DTX.Song.Components
{
    /// <summary>
    /// DTXManiaNX-compatible song list display with smooth scrolling and 13-item window
    /// Equivalent to CActSelectSongList from DTXManiaNX
    ///
    /// CRITICAL FIX: Texture generation moved to Update phase to prevent screen blackouts
    /// - RenderTarget switching during Draw phase causes screen blackouts
    /// - Texture generation now queued during scroll and processed in Update phase only
    /// - Circular buffer pattern: only regenerates textures for bars entering view
    /// </summary>
    public class SongListDisplay : UIElement
    {
        #region Constants

        // Use centralized layout constants from SongSelectionUILayout
        private const int VISIBLE_ITEMS = SongSelectionUILayout.SongBars.VisibleItems;
        private const int CENTER_INDEX = SongSelectionUILayout.SongBars.CenterIndex;
        private const int SCROLL_UNIT = SongSelectionUILayout.SongBars.ScrollUnit;
        private const float SCROLL_ACCELERATION_THRESHOLD_1 = 200f;  // Increased from 100f
        private const float SCROLL_ACCELERATION_THRESHOLD_2 = 600f;  // Increased from 300f  
        private const float SCROLL_ACCELERATION_THRESHOLD_3 = 1000f; // Increased from 500f

        // DTXManiaNX Current Implementation Coordinates - now centralized in SongSelectionUILayout
        // NOTE: Original curved X coordinates are present but DISABLED in current DTXManiaNX
        // Current implementation uses vertical list layout with fixed X positions
        private static readonly Point[] OriginalCurvedCoordinates = SongSelectionUILayout.SongBars.BarCoordinates;

        // DTXManiaNX Current Implementation: Vertical List Layout - now centralized
        // Selected bar (index 5): X:665, Y:269 (special position, curves out from list)
        // Unselected bars: Fixed X:673 (vertical list formation)
        private const int SELECTED_BAR_X = SongSelectionUILayout.SongBars.SelectedBarX;
        private const int SELECTED_BAR_Y = SongSelectionUILayout.SongBars.SelectedBarY;
        private const int UNSELECTED_BAR_X = SongSelectionUILayout.SongBars.UnselectedBarX;
        private const int BAR_WIDTH = SongSelectionUILayout.SongBars.BarWidth;

        // DTXManiaNX Current Implementation: Simplified Visual Effects
        // Current implementation uses minimal perspective effects for vertical list
        private static readonly float[] BarScaleFactors = new float[]
        {
            1.0f,   // Bar 0 (top) - normal size
            1.0f,   // Bar 1
            1.0f,   // Bar 2
            1.0f,   // Bar 3
            1.0f,   // Bar 4
            1.0f,   // Bar 5 (CENTER/SELECTED) - normal size (no scaling in current impl)
            1.0f,   // Bar 6
            1.0f,   // Bar 7
            1.0f,   // Bar 8
            1.0f,   // Bar 9
            1.0f,   // Bar 10
            1.0f,   // Bar 11
            1.0f    // Bar 12 (bottom) - normal size
        };

        private static readonly float[] BarOpacityFactors = new float[]
        {
            1.0f,   // Bar 0 (top) - full opacity
            1.0f,   // Bar 1
            1.0f,   // Bar 2
            1.0f,   // Bar 3
            1.0f,   // Bar 4
            1.0f,   // Bar 5 (CENTER/SELECTED) - full opacity
            1.0f,   // Bar 6
            1.0f,   // Bar 7
            1.0f,   // Bar 8
            1.0f,   // Bar 9
            1.0f,   // Bar 10
            1.0f,   // Bar 11
            1.0f    // Bar 12 (bottom) - full opacity
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

        // Comment bar components for song comments
        private ITexture _commentBarTexture;
        private IResourceManager _resourceManager;

        // Phase 2 enhancements: Bar information caching
        private readonly Dictionary<string, SongBarInfo> _barInfoCache;        // Texture generation priority queue to prevent draw phase generation
        private readonly List<TextureGenerationRequest> _textureGenerationQueue;
        private readonly HashSet<int> _visibleBarIndices;

        // Fast scroll optimization
        private DateTime _lastInputTime = DateTime.MinValue;
        private int _consecutiveInputCount = 0;
        private const double FAST_SCROLL_WINDOW_MS = 500.0; // Time window for detecting rapid inputs
        private const int FAST_SCROLL_THRESHOLD = 3; // Number of inputs to trigger fast scroll

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
        }        /// <summary>
                 /// Whether the list is currently scrolling
                 /// </summary>
        public bool IsScrolling => _targetScrollCounter != 0 || _currentScrollCounter != 0;

        /// <summary>
        /// Whether scrolling has completed (target matches current position)
        /// </summary>
        public bool IsScrollComplete => _targetScrollCounter == _currentScrollCounter;        /// <summary>
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

                // Allow infinite navigation - don't clamp the index
                if (value != _selectedIndex)
                {
                    _selectedIndex = value;
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
        }        /// <summary>
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

        /// <summary>
        /// Set the resource manager for loading textures
        /// </summary>
        public void SetResourceManager(IResourceManager resourceManager)
        {
            _resourceManager = resourceManager;
            // Try to load comment bar texture if we don't have it yet
            if (_commentBarTexture == null && _resourceManager != null)
            {
                LoadCommentBarTexture();
            }
        }

        /// <summary>
        /// Scroll speed multiplier (1.0 = normal, 2.0 = 2x faster, etc.)
        /// </summary>
        public float ScrollSpeedMultiplier { get; set; } = 1.0f;

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

            // Initialize Phase 2 enhanced bar information caching
            _barInfoCache = new Dictionary<string, SongBarInfo>();

            // Initialize texture generation queue and tracking
            _textureGenerationQueue = new List<TextureGenerationRequest>();
            _visibleBarIndices = new HashSet<int>();

            Size = new Vector2(700, VISIBLE_ITEMS * _itemHeight);
        }

        #endregion

        #region Public Methods        /// <summary>
        /// Move selection to next song (DTXManiaNX curved layout style)
        /// </summary>
        public void MoveNext()
        {
            if (_currentList == null || _currentList.Count == 0)
                return;

            // Track rapid input for fast scroll optimization
            TrackRapidInput();

            // Move to next song without wrapping (infinite visual looping handled in display)
            _selectedIndex = _selectedIndex + 1;

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

            // Track rapid input for fast scroll optimization
            TrackRapidInput();

            // Move to previous song without wrapping (infinite visual looping handled in display)
            _selectedIndex = _selectedIndex - 1;

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

            // Phase 2: Clear bar information cache
            foreach (var barInfo in _barInfoCache.Values)
            {
                barInfo?.Dispose();
            }
            _barInfoCache.Clear();
        }

        /// <summary>
        /// Get or create bar information for a song node (Phase 2 enhancement)
        /// Equivalent to DTXManiaNX bar reconstruction system
        /// </summary>
        private SongBarInfo GetOrCreateBarInfo(SongListNode node, int difficulty, bool isSelected)
        {
            if (node == null || _barRenderer == null)
                return null;

            // Optimize cache key: exclude selection state to improve cache hit rate
            var cacheKey = $"{node.GetHashCode()}_{difficulty}";

            if (_barInfoCache.TryGetValue(cacheKey, out var cachedInfo))
            {
                // Update state if needed (this is fast since textures are cached)
                _barRenderer.UpdateBarInfo(cachedInfo, difficulty, isSelected);
                return cachedInfo;
            }

            // Generate new bar information
            var barInfo = _barRenderer.GenerateBarInfo(node, difficulty, isSelected);
            if (barInfo != null)
            {
                _barInfoCache[cacheKey] = barInfo;
            }

            return barInfo;
        }        /// <summary>
                 /// Initialize enhanced rendering with SongBarRenderer
                 /// </summary>
        public void InitializeEnhancedRendering(GraphicsDevice graphicsDevice, IResourceManager resourceManager,
            RenderTarget2D sharedRenderTarget)
        {
            _barRenderer?.Dispose();
            _resourceManager = resourceManager;

            if (sharedRenderTarget != null)
            {
                _barRenderer = new SongBarRenderer(graphicsDevice, resourceManager, sharedRenderTarget);
            }
            else
            {
                // Cannot create without RenderTarget - log error and disable enhanced rendering
                System.Diagnostics.Debug.WriteLine("SongListDisplay: Cannot initialize SongBarRenderer without RenderTarget. Enhanced rendering disabled.");
                _barRenderer = null;
                return;
            }

            if (_font != null)
            {
                _barRenderer.SetFont(_font);
            }            // Initialize graphics generator for default styling
            _graphicsGenerator?.Dispose();
            _graphicsGenerator = new DefaultGraphicsGenerator(graphicsDevice, sharedRenderTarget);

            // Initialize graphics generators for cached song bars
            foreach (var songBar in _songBarCache.Values)
            {
                songBar.InitializeGraphicsGenerator(graphicsDevice, sharedRenderTarget);
            }

            // Load comment bar texture
            LoadCommentBarTexture();
        }

        /// <summary>
        /// Enable or disable enhanced rendering
        /// </summary>
        public void SetEnhancedRendering(bool enabled)
        {
            _useEnhancedRendering = enabled;
        }

        /// <summary>
        /// Force immediate visual invalidation and redraw
        /// Used when selection changes to ensure immediate UI response
        /// </summary>
        public void InvalidateVisuals()
        {
            // Clear visible bar indices to force regeneration
            _visibleBarIndices.Clear();

            // Queue immediate texture generation for all currently visible items
            QueueTextureGenerationForNewBars();
        }

        /// <summary>
        /// Load the comment bar background texture
        /// </summary>
        private void LoadCommentBarTexture()
        {
            if (_resourceManager == null)
                return;

            try
            {
                _commentBarTexture = _resourceManager.LoadTexture(TexturePath.CommentBar);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SongListDisplay: Failed to load comment bar texture: {ex.Message}");
                _commentBarTexture = null;
            }
        }

        #endregion

        #region Protected Methods

        protected override void OnUpdate(double deltaTime)
        {
            base.OnUpdate(deltaTime);

            // Update smooth scrolling animation
            UpdateScrollAnimation(deltaTime);

            // Process pending texture generation requests (CRITICAL: Only in Update phase)
            UpdatePendingTextures();
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

            // Draw comment bar (behind song bars)
            DrawCommentBar(spriteBatch);

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

        /// <summary>
        /// Track rapid input for fast scroll optimization
        /// </summary>
        private void TrackRapidInput()
        {
            var currentTime = DateTime.UtcNow;
            var timeSinceLastInput = (currentTime - _lastInputTime).TotalMilliseconds;

            if (timeSinceLastInput <= FAST_SCROLL_WINDOW_MS)
            {
                _consecutiveInputCount++;
            }
            else
            {
                _consecutiveInputCount = 1; // Reset counter
            }

            _lastInputTime = currentTime;
        }

        /// <summary>
        /// Check if we're in fast scroll mode (rapid consecutive inputs)
        /// </summary>
        private bool IsFastScrollMode()
        {
            var timeSinceLastInput = (DateTime.UtcNow - _lastInputTime).TotalMilliseconds;
            return _consecutiveInputCount >= FAST_SCROLL_THRESHOLD && timeSinceLastInput <= FAST_SCROLL_WINDOW_MS;
        }

        private void UpdateScrollAnimation(double deltaTime)
        {
            if (_targetScrollCounter == _currentScrollCounter)
                return;

            // Store previous scroll position to detect changes
            int previousScrollPosition = _currentScrollCounter;

            // Calculate scroll distance and acceleration
            int distance = Math.Abs(_targetScrollCounter - _currentScrollCounter);
            int acceleration = GetScrollAcceleration(distance);

            // Apply frame-rate independent acceleration multiplier for smooth 60fps
            var frameMultiplier = Math.Max(1.0, deltaTime * 60.0); // Ensure consistent speed at 60fps
            acceleration = (int)(acceleration * frameMultiplier);

            // Move towards target
            if (_targetScrollCounter > _currentScrollCounter)
            {
                _currentScrollCounter = Math.Min(_targetScrollCounter, _currentScrollCounter + acceleration);
            }
            else
            {
                _currentScrollCounter = Math.Max(_targetScrollCounter, _currentScrollCounter - acceleration);
            }

            // Optimized texture generation: Only trigger when crossing significant boundaries
            int scrollDelta = Math.Abs(_currentScrollCounter - previousScrollPosition);
            if (scrollDelta >= SCROLL_UNIT * 2) // Only trigger every 2 songs instead of every song
            {
                QueueTextureGenerationForNewBars();
            }
        }
        private int GetScrollAcceleration(int distance)
        {
            // Base acceleration values - significantly increased for responsive feel
            int baseAcceleration;
            if (distance <= SCROLL_ACCELERATION_THRESHOLD_1)
                baseAcceleration = 15;  // Increased from 4 (3.75x faster for short distances)
            else if (distance <= SCROLL_ACCELERATION_THRESHOLD_2)
                baseAcceleration = 25;  // Increased from 6 (4x faster for medium distances)
            else if (distance <= SCROLL_ACCELERATION_THRESHOLD_3)
                baseAcceleration = 40;  // Increased from 8 (5x faster for long distances)
            else
                baseAcceleration = 60;  // Increased from 16 (3.75x faster for very long distances)

            // Apply fast scroll multiplier for rapid consecutive inputs
            if (IsFastScrollMode())
            {
                baseAcceleration = (int)(baseAcceleration * 2.0f); // 2x faster during rapid input
            }

            // Apply external scroll speed multiplier
            baseAcceleration = (int)(baseAcceleration * ScrollSpeedMultiplier);

            return Math.Max(1, baseAcceleration); // Ensure minimum acceleration of 1
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

            // Draw 13 visible bars using DTXManiaNX current implementation (vertical list layout)
            for (int barIndex = 0; barIndex < VISIBLE_ITEMS; barIndex++)
            {
                // Calculate which song should be displayed at this bar position
                int songIndex = centerSongIndex + (barIndex - CENTER_INDEX);

                // Implement infinite looping: wrap song index using modulo arithmetic
                songIndex = ((songIndex % _currentList.Count) + _currentList.Count) % _currentList.Count;

                var node = _currentList[songIndex];

                // DTXManiaNX Current Implementation: Vertical List Layout
                // Selected bar (index 5): X:665, Y:269 (special position, curves out from list)
                // Unselected bars: Fixed X:673 (vertical list formation)
                int barX, barY;
                if (barIndex == CENTER_INDEX)
                {
                    // Selected bar uses special position
                    barX = SELECTED_BAR_X;
                    barY = SELECTED_BAR_Y;
                }
                else
                {
                    // Unselected bars use fixed X position with Y from original coordinates
                    barX = UNSELECTED_BAR_X;
                    barY = OriginalCurvedCoordinates[barIndex].Y;
                }

                // Apply minimal visual effects (current implementation uses simplified effects)
                var scaleFactor = BarScaleFactors[barIndex];
                var opacityFactor = BarOpacityFactors[barIndex];

                // Calculate dimensions (current implementation uses fixed width)
                var barWidth = BAR_WIDTH;
                var barHeight = (int)_itemHeight;                // Create item bounds using vertical list coordinates
                var itemBounds = new Rectangle(barX, barY, barWidth, barHeight);                // Bar 5 (CENTER_INDEX) is always the selected position in DTXManiaNX
                bool isSelected = (barIndex == CENTER_INDEX);
                bool isCenter = isSelected; // Center bar is the selected bar

                DrawSongItemWithPerspective(spriteBatch, node, itemBounds, isSelected, isCenter, barIndex, scaleFactor, opacityFactor);
            }
        }

        private void DrawSongItemWithPerspective(SpriteBatch spriteBatch, SongListNode node, Rectangle itemBounds, bool isSelected, bool isCenter, int barIndex, float scaleFactor, float opacityFactor)
        {
            // Use enhanced rendering with perspective effects
            if (_useEnhancedRendering && _barRenderer != null && _font != null)
            {
                DrawEnhancedSongItemWithPerspective(spriteBatch, node, itemBounds, isSelected, isCenter, barIndex, scaleFactor, opacityFactor);
            }
            else
            {
                DrawBasicSongItemWithPerspective(spriteBatch, node, itemBounds, isSelected, isCenter, barIndex, scaleFactor, opacityFactor);
            }
        }

        private void DrawEnhancedSongItemWithPerspective(SpriteBatch spriteBatch, SongListNode node, Rectangle itemBounds, bool isSelected, bool isCenter, int barIndex, float scaleFactor, float opacityFactor)
        {
            // Phase 2 Enhancement: Use bar information caching system with perspective effects
            var barInfo = GetOrCreateBarInfo(node, _currentDifficulty, isSelected);

            if (barInfo != null)
            {
                // Draw using cached bar information with perspective effects
                DrawBarInfoWithPerspective(spriteBatch, barInfo, itemBounds, isSelected, isCenter, scaleFactor, opacityFactor);
            }
            else
            {
                // Fallback to basic perspective rendering
                DrawBasicSongItemWithPerspective(spriteBatch, node, itemBounds, isSelected, isCenter, barIndex, scaleFactor, opacityFactor);
            }
        }

        /// <summary>
        /// Draw song bar using cached bar information with DTXManiaNX perspective effects
        /// </summary>
        private void DrawBarInfoWithPerspective(SpriteBatch spriteBatch, SongBarInfo barInfo, Rectangle itemBounds, bool isSelected, bool isCenter, float scaleFactor, float opacityFactor)
        {
            // Apply opacity to all colors
            var opacity = Color.White * opacityFactor;

            // Draw background using Phase 2 bar type specific graphics generator with perspective
            if (_graphicsGenerator != null)
            {
                var backgroundTexture = _graphicsGenerator.GenerateBarTypeBackground(itemBounds.Width, itemBounds.Height, barInfo.BarType, isSelected, isCenter);
                if (backgroundTexture != null)
                {
                    backgroundTexture.Draw(spriteBatch, new Vector2(itemBounds.X, itemBounds.Y), null);
                }
            }

            // Draw clear lamp with perspective
            if (barInfo.ClearLamp != null)
            {
                var lampWidth = (int)(DTXManiaVisualTheme.Layout.ClearLampWidth * scaleFactor);
                var lampDestRect = new Rectangle(itemBounds.X, itemBounds.Y, lampWidth, itemBounds.Height);
                barInfo.ClearLamp.Draw(spriteBatch, lampDestRect, null, opacity, 0f, Vector2.Zero, SpriteEffects.None, 0f);
            }

            // Draw preview image with perspective
            if (barInfo.PreviewImage != null)
            {
                var imageSize = (int)(DTXManiaVisualTheme.Layout.PreviewImageSize * scaleFactor);
                var imageX = itemBounds.X + (int)(DTXManiaVisualTheme.Layout.ClearLampWidth * scaleFactor) + 5;
                var imageY = itemBounds.Y + (itemBounds.Height - imageSize) / 2;
                var imageDestRect = new Rectangle(imageX, imageY, imageSize, imageSize);
                barInfo.PreviewImage.Draw(spriteBatch, imageDestRect, null, opacity, 0f, Vector2.Zero, SpriteEffects.None, 0f);
            }

            // Draw title with perspective
            if (barInfo.TitleTexture != null)
            {
                var textX = itemBounds.X + (int)(DTXManiaVisualTheme.Layout.ClearLampWidth * scaleFactor) + (barInfo.PreviewImage != null ? (int)(DTXManiaVisualTheme.Layout.PreviewImageSize * scaleFactor) + 10 : 5);
                var textY = itemBounds.Y + (itemBounds.Height - (int)(barInfo.TitleTexture.Height * scaleFactor)) / 2;
                var textPosition = new Vector2(textX, textY);
                var textScale = new Vector2(scaleFactor, scaleFactor);
                barInfo.TitleTexture.Draw(spriteBatch, textPosition, textScale, 0f, Vector2.Zero);
            }
            else if (_font != null)
            {
                // Fallback to direct text rendering with perspective
                var textX = itemBounds.X + (int)(DTXManiaVisualTheme.Layout.ClearLampWidth * scaleFactor) + (barInfo.PreviewImage != null ? (int)(DTXManiaVisualTheme.Layout.PreviewImageSize * scaleFactor) + 10 : 5);
                var textY = itemBounds.Y + (itemBounds.Height - (int)(_font.LineSpacing * scaleFactor)) / 2;
                var textPosition = new Vector2(textX, textY);
                var textScale = new Vector2(scaleFactor, scaleFactor);

                // Draw shadow first with perspective
                var shadowPosition = textPosition + DTXManiaVisualTheme.FontEffects.SongTextShadowOffset * scaleFactor;
                var shadowColor = DTXManiaVisualTheme.FontEffects.SongTextShadowColor * opacityFactor;
                spriteBatch.DrawString(_font, barInfo.TitleString, shadowPosition, shadowColor, 0f, Vector2.Zero, textScale, SpriteEffects.None, 0f);

                // Draw main text with perspective
                var textColor = barInfo.TextColor * opacityFactor;
                spriteBatch.DrawString(_font, barInfo.TitleString, textPosition, textColor, 0f, Vector2.Zero, textScale, SpriteEffects.None, 0f);
            }

            // Draw artist name for score nodes
            if (isCenter && barInfo.SongNode.Type == NodeType.Score && !string.IsNullOrEmpty(barInfo.SongNode.DatabaseSong?.Artist))
            {
                DrawArtistName(spriteBatch, barInfo.SongNode.DatabaseSong.Artist, itemBounds, new Vector2(scaleFactor, scaleFactor), opacityFactor);
            }
        }

        private void DrawBasicSongItemWithPerspective(SpriteBatch spriteBatch, SongListNode node, Rectangle itemBounds, bool isSelected, bool isCenter, int barIndex, float scaleFactor, float opacityFactor)
        {
            // Draw item background with DTXManiaNX curved layout styling and perspective effects
            if (_whitePixel != null)
            {
                // Use different colors for center vs selected vs normal bars with opacity
                Color backgroundColor;
                if (isCenter)
                {
                    backgroundColor = Color.Gold * 0.8f * opacityFactor; // Center bar gets gold highlight
                }
                else if (isSelected)
                {
                    backgroundColor = _selectedItemColor * opacityFactor;
                }
                else
                {
                    backgroundColor = Color.DarkBlue * 0.3f * opacityFactor; // Normal bars get subtle background
                }

                spriteBatch.Draw(_whitePixel, itemBounds, backgroundColor);

                // Draw border for center bar with perspective
                if (isCenter && _whitePixel != null)
                {
                    var borderColor = Color.Yellow * opacityFactor;
                    var borderThickness = Math.Max(1, (int)(2 * scaleFactor));

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

            // Draw song title text with perspective scaling and opacity
            var text = GetDisplayText(node);
            var baseTextColor = isCenter ? Color.White : (isSelected ? _selectedTextColor : _textColor);
            var textColor = baseTextColor * opacityFactor;
            var textPos = new Vector2(itemBounds.X + (int)(10 * scaleFactor), itemBounds.Y + (int)(5 * scaleFactor));
            var textScale = new Vector2(scaleFactor, scaleFactor);

            if (_font != null)
            {
                spriteBatch.DrawString(_font, text, textPos, textColor, 0f, Vector2.Zero, textScale, SpriteEffects.None, 0f);
                
                // Draw artist name for center (selected) song
                if (isCenter && node.Type == NodeType.Score && !string.IsNullOrEmpty(node.DatabaseSong?.Artist))
                {
                    DrawArtistName(spriteBatch, node.DatabaseSong.Artist, itemBounds, textScale, opacityFactor);
                }
            }
            else if (_managedFont != null)
            {
                // Use managed font's drawing method with scaling (if supported)
                _managedFont.DrawString(spriteBatch, text, textPos, textColor);
                
                // Draw artist name for center (selected) song
                if (isCenter && node.Type == NodeType.Score && !string.IsNullOrEmpty(node.DatabaseSong?.Artist))
                {
                    DrawArtistNameWithManagedFont(spriteBatch, node.DatabaseSong.Artist, itemBounds, textScale, opacityFactor);
                }
            }
            else
            {
                // Fallback: draw a simple rectangle to show the item exists with perspective
                if (_whitePixel != null)
                {
                    var fallbackColor = (isSelected ? Color.Yellow : Color.Gray) * 0.5f * opacityFactor;
                    var textBounds = new Rectangle(
                        itemBounds.X + (int)(10 * scaleFactor),
                        itemBounds.Y + (int)(5 * scaleFactor),
                        itemBounds.Width - (int)(20 * scaleFactor),
                        itemBounds.Height - (int)(10 * scaleFactor)
                    );
                    spriteBatch.Draw(_whitePixel, textBounds, fallbackColor);
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

        /// <summary>
        /// Draw artist name for the currently selected song using SpriteFont
        /// </summary>
        private void DrawArtistName(SpriteBatch spriteBatch, string artistName, Rectangle itemBounds, Vector2 textScale, float opacityFactor)
        {
            if (string.IsNullOrEmpty(artistName) || _font == null)
                return;

            // Measure the artist text size
            var artistTextSize = _font.MeasureString(artistName);

            // Calculate position using layout constants - right-aligned with padding
            // Position artist name BELOW the song bar, not at the bottom edge of the bar
            var artistX = itemBounds.Right - SongSelectionUILayout.SongBars.ArtistNameRightMargin - (artistTextSize.X * textScale.X);
            var artistY = itemBounds.Bottom + (8 * textScale.Y); // Position below the bar with 8px spacing
            var artistPosition = new Vector2(artistX, artistY);

            // Ensure artist name doesn't exceed maximum width
            var maxWidth = SongSelectionUILayout.SongBars.ArtistNameMaxWidth;
            var scaledWidth = artistTextSize.X * textScale.X;
            var finalTextScale = textScale;

            if (scaledWidth > maxWidth)
            {
                // Scale down to fit within maximum width
                var widthScale = maxWidth / scaledWidth;
                finalTextScale = new Vector2(textScale.X * widthScale, textScale.Y * widthScale);
                
                // Recalculate position with new scale
                artistX = itemBounds.Right - SongSelectionUILayout.SongBars.ArtistNameRightMargin - (artistTextSize.X * finalTextScale.X);
                artistY = itemBounds.Bottom + (8 * finalTextScale.Y); // Keep consistent spacing below the bar
                artistPosition = new Vector2(artistX, artistY);
            }

            // Use subtle gray color for artist name with opacity
            var artistColor = Color.LightGray * 0.8f * opacityFactor;

            // Draw artist name
            spriteBatch.DrawString(_font, artistName, artistPosition, artistColor, 0f, Vector2.Zero, finalTextScale, SpriteEffects.None, 0f);
        }

        /// <summary>
        /// Draw artist name for the currently selected song using ManagedFont
        /// </summary>
        private void DrawArtistNameWithManagedFont(SpriteBatch spriteBatch, string artistName, Rectangle itemBounds, Vector2 textScale, float opacityFactor)
        {
            if (string.IsNullOrEmpty(artistName) || _managedFont == null)
                return;

            // Measure the artist text size
            var artistTextSize = _managedFont.MeasureString(artistName);

            // Calculate position using layout constants - right-aligned with padding
            // Position artist name BELOW the song bar, not at the bottom edge of the bar
            var artistX = itemBounds.Right - SongSelectionUILayout.SongBars.ArtistNameRightMargin - artistTextSize.X;
            var artistY = itemBounds.Bottom + 8; // Position below the bar with 8px spacing
            var artistPosition = new Vector2(artistX, artistY);

            // Ensure artist name doesn't exceed maximum width
            var maxWidth = SongSelectionUILayout.SongBars.ArtistNameMaxWidth;
            if (artistTextSize.X > maxWidth)
            {
                // For ManagedFont, we'll truncate the text if it's too long
                // Note: ManagedFont scaling may not be supported, so we truncate instead
                var truncatedText = TruncateTextToWidth(artistName, maxWidth, _managedFont);
                artistName = truncatedText;
                
                // Recalculate position with truncated text
                artistTextSize = _managedFont.MeasureString(artistName);
                artistX = itemBounds.Right - SongSelectionUILayout.SongBars.ArtistNameRightMargin - artistTextSize.X;
                artistPosition = new Vector2(artistX, artistY);
            }

            // Use subtle gray color for artist name with opacity
            var artistColor = Color.LightGray * 0.8f * opacityFactor;

            // Draw artist name using ManagedFont
            _managedFont.DrawString(spriteBatch, artistName, artistPosition, artistColor);
        }

        /// <summary>
        /// Truncate text to fit within specified width using binary search
        /// </summary>
        private string TruncateTextToWidth(string text, float maxWidth, IFont font)
        {
            if (string.IsNullOrEmpty(text) || font == null)
                return text;

            // If the full text fits, return it as-is
            if (font.MeasureString(text).X <= maxWidth)
                return text;

            // Binary search for the longest text that fits within maxWidth
            int left = 0;
            int right = text.Length;
            string bestFit = "";

            while (left <= right)
            {
                int mid = left + (right - left) / 2;
                string candidate = text.Substring(0, mid) + "...";
                
                if (font.MeasureString(candidate).X <= maxWidth)
                {
                    bestFit = candidate;
                    left = mid + 1;
                }
                else
                {
                    right = mid - 1;
                }
            }

            return bestFit;
        }

        /// <summary>
        /// Draw comment bar background and comment text for the currently selected song
        /// Renders behind song bars as per DTXManiaNX design
        /// Note: Artist names are handled by the existing song bar rendering system
        /// </summary>
        private void DrawCommentBar(SpriteBatch spriteBatch)
        {
            if (SelectedSong == null || SelectedSong.Type != NodeType.Score)
                return;

            // Try to load texture if we don't have it yet and resource manager is available
            if (_commentBarTexture == null && _resourceManager != null)
            {
                LoadCommentBarTexture();
            }

            // Draw comment bar background texture - using original DTXManiaNX coordinates
            // The texture has built-in padding to align with the selected song bar
            var commentBarX = 560; // Original DTXManiaNX X coordinate
            var commentBarY = 257; // Original DTXManiaNX Y coordinate (behind selected song bar at Y:269)
            var commentBarPosition = new Vector2(commentBarX, commentBarY);

            if (_commentBarTexture != null)
            {
                _commentBarTexture.Draw(spriteBatch, commentBarPosition);
            }
            else
            {
                // Fallback: Draw a simple rectangle aligned with the selected song bar
                if (_whitePixel != null)
                {
                    var fallbackRect = new Rectangle(commentBarX, commentBarY, BAR_WIDTH, 80);
                    var fallbackColor = Color.Blue * 0.3f; // Semi-transparent blue
                    spriteBatch.Draw(_whitePixel, fallbackRect, fallbackColor);
                }
            }

            // Draw comment text (using original DTXManiaNX coordinates)
            if (!string.IsNullOrEmpty(SelectedSong.DatabaseSong?.Comment))
            {
                DrawCommentBarCommentText(spriteBatch, SelectedSong.DatabaseSong.Comment);
            }
        }



        /// <summary>
        /// Draw comment text for comment bar (using original DTXManiaNX coordinates)
        /// </summary>
        private void DrawCommentBarCommentText(SpriteBatch spriteBatch, string commentText)
        {
            if (string.IsNullOrEmpty(commentText) || _font == null)
                return;

            // Use font scaling and original DTXManiaNX position
            var textScale = new Vector2(SongSelectionUILayout.CommentBar.FontScale);
            var commentPosition = new Vector2(683, 339); // Original DTXManiaNX coordinates
            var maxWidth = 510; // Maximum width constraint (same as song bars)

            // Handle multi-line text wrapping
            var wrappedText = WrapTextToWidth(commentText, maxWidth, _font, textScale.X);

            // Draw each line of wrapped text
            var lineHeight = _font.LineSpacing * textScale.Y;
            var currentY = commentPosition.Y;

            foreach (var line in wrappedText)
            {
                var linePosition = new Vector2(commentPosition.X, currentY);
                var commentColor = Color.LightGray * 0.8f;
                spriteBatch.DrawString(_font, line, linePosition, commentColor, 0f, Vector2.Zero, textScale, SpriteEffects.None, 0f);
                currentY += lineHeight;
            }
        }

        /// <summary>
        /// Wrap text to fit within specified width, returning array of lines
        /// </summary>
        private string[] WrapTextToWidth(string text, float maxWidth, SpriteFont font, float scale)
        {
            if (string.IsNullOrEmpty(text) || font == null)
                return new[] { text ?? "" };

            var words = text.Split(' ');
            var lines = new System.Collections.Generic.List<string>();
            var currentLine = "";

            foreach (var word in words)
            {
                var testLine = string.IsNullOrEmpty(currentLine) ? word : currentLine + " " + word;
                var testWidth = font.MeasureString(testLine).X * scale;

                if (testWidth <= maxWidth)
                {
                    currentLine = testLine;
                }
                else
                {
                    if (!string.IsNullOrEmpty(currentLine))
                    {
                        lines.Add(currentLine);
                        currentLine = word;
                    }
                    else
                    {
                        // Single word is too long, add it anyway
                        lines.Add(word);
                    }
                }
            }

            if (!string.IsNullOrEmpty(currentLine))
            {
                lines.Add(currentLine);
            }

            return lines.ToArray();
        }

        private void UpdateSelection()
        {
            var previousSong = SelectedSong;

            // Update logical selection with infinite looping support
            if (_currentList != null && _currentList.Count > 0)
            {
                // Use modulo arithmetic to find the actual song for infinite looping
                int actualIndex = ((_selectedIndex % _currentList.Count) + _currentList.Count) % _currentList.Count;
                SelectedSong = _currentList[actualIndex];
            }
            else
            {
                SelectedSong = null;
            }

            if (SelectedSong != previousSong)
            {
                // Force immediate redraw by invalidating visuals
                InvalidateVisuals();

                // Immediate texture generation for selected song
                GenerateSelectedSongTextureImmediately();

                // Pre-generate textures for adjacent songs (±5 from current selection - increased per senior engineer feedback)
                PreGenerateAdjacentSongTextures();

                SelectionChanged?.Invoke(this, new SongSelectionChangedEventArgs(SelectedSong, _currentDifficulty, IsScrollComplete));
            }
        }        /// <summary>
                 /// Immediately generate texture for the currently selected song without queuing
                 /// </summary>
        private void GenerateSelectedSongTextureImmediately()
        {
            if (_barRenderer == null || SelectedSong == null)
                return;

            // Performance metrics: Measure immediate texture generation timing
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            // Generate bar info immediately for the selected song
            var barInfo = _barRenderer.GenerateBarInfoWithPriority(SelectedSong, _currentDifficulty, true);

            // Cache the bar info if generation succeeded
            if (barInfo != null)
            {
                var cacheKey = $"{SelectedSong.GetHashCode()}_{_currentDifficulty}";
                _barInfoCache[cacheKey] = barInfo;
            }

            // Performance metrics logging
            stopwatch.Stop();
            if (stopwatch.Elapsed.TotalMilliseconds > 2.0) // Log if processing takes more than 2ms
            {
                System.Diagnostics.Debug.WriteLine($"SongListDisplay: Immediate selected song texture generation took {stopwatch.Elapsed.TotalMilliseconds:F2}ms");
            }
        }/// <summary>
         /// Pre-generate textures for adjacent songs (±5 from current selection)
         /// </summary>
        private void PreGenerateAdjacentSongTextures()
        {
            if (_currentList == null || _currentList.Count == 0 || _selectedIndex < 0)
                return;

            // Pre-generate for ±5 songs around the current selection (increased per senior engineer feedback)
            for (int offset = -5; offset <= 5; offset++)
            {
                if (offset == 0) // Skip the selected song itself (already generated immediately)
                    continue;

                int adjacentIndex = _selectedIndex + offset;

                // Implement infinite looping: wrap adjacent index using modulo arithmetic
                adjacentIndex = ((adjacentIndex % _currentList.Count) + _currentList.Count) % _currentList.Count;

                var adjacentSong = _currentList[adjacentIndex];

                // Check if we already have this texture cached
                var cacheKey = $"{adjacentSong.GetHashCode()}_{_currentDifficulty}";
                if (_barInfoCache.ContainsKey(cacheKey))
                    continue; // Already cached, skip                // Add to texture generation queue with appropriate priority using sorted insertion
                var request = new TextureGenerationRequest
                {
                    SongNode = adjacentSong,
                    SongIndex = adjacentIndex,
                    BarIndex = -1, // Not tied to a specific bar position
                    Difficulty = _currentDifficulty,
                    IsSelected = false,
                    Priority = 75 - Math.Abs(offset) * 5 // Closer songs get higher priority
                };

                InsertTextureRequestSorted(request);
            }
        }

        /// <summary>
        /// Queue texture generation for bars entering view during scroll
        /// Implements circular buffer pattern - only regenerate for ONE bar entering view
        /// </summary>
        private void QueueTextureGenerationForNewBars()
        {
            if (_currentList == null || _currentList.Count == 0)
                return;

            // Always pre-generate textures for ALL visible positions
            int centerSongIndex = _currentScrollCounter / SCROLL_UNIT;

            // Track which bar indices are currently visible
            var newVisibleIndices = new HashSet<int>();

            for (int barIndex = 0; barIndex < VISIBLE_ITEMS; barIndex++)
            {
                int songIndex = centerSongIndex + (barIndex - CENTER_INDEX);

                // Implement infinite looping: wrap song index using modulo arithmetic
                songIndex = ((songIndex % _currentList.Count) + _currentList.Count) % _currentList.Count;

                newVisibleIndices.Add(songIndex);                // Only queue if not already cached using sorted insertion
                var cacheKey = $"{_currentList[songIndex].GetHashCode()}_{_currentDifficulty}";
                if (!_barInfoCache.ContainsKey(cacheKey))
                {
                    var request = new TextureGenerationRequest
                    {
                        SongNode = _currentList[songIndex],
                        SongIndex = songIndex,
                        BarIndex = barIndex,
                        Difficulty = _currentDifficulty,
                        IsSelected = (barIndex == CENTER_INDEX),
                        Priority = 100 - Math.Abs(barIndex - CENTER_INDEX)
                    };

                    InsertTextureRequestSorted(request);
                }
            }            // Update visible bar indices
            _visibleBarIndices.Clear();
            foreach (var index in newVisibleIndices)
            {
                _visibleBarIndices.Add(index);
            }
        }        /// <summary>
                 /// Process pending texture generation requests during Update phase with priority queue
                 /// All texture generation moved to Update phase with no exceptions (per senior engineer feedback)
                 /// </summary>
        private void UpdatePendingTextures()
        {
            if (_barRenderer == null)
                return;            // Performance metrics: Measure texture generation timing
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();            // Process more requests per frame during scrolling for smoother experience
            int maxRequestsPerFrame = IsScrolling ? 10 : 6; // Increased during scrolling
            int processedCount = 0;

            // Queue is already sorted by priority (highest first) due to sorted insertion
            // No need to sort entire list each frame - significant performance improvement

            while (_textureGenerationQueue.Count > 0 && processedCount < maxRequestsPerFrame)
            {
                var request = _textureGenerationQueue[0];
                _textureGenerationQueue.RemoveAt(0);

                // Generate textures for this bar (always safe now - IsInDrawPhase checks removed)
                var barInfo = _barRenderer.GenerateBarInfo(request.SongNode, request.Difficulty, request.IsSelected);

                // Cache the bar info if generation succeeded
                if (barInfo != null)
                {
                    var cacheKey = $"{request.SongNode.GetHashCode()}_{request.Difficulty}";
                    _barInfoCache[cacheKey] = barInfo;
                }

                processedCount++;

                // Break early if we're taking too long (prevent frame drops)
                if (stopwatch.Elapsed.TotalMilliseconds > 8.0) // Max 8ms per frame
                    break;
            }

            // Performance metrics logging
            stopwatch.Stop();
            if (processedCount > 0 && stopwatch.Elapsed.TotalMilliseconds > 5.0) // Log if processing takes more than 5ms
            {
                System.Diagnostics.Debug.WriteLine($"SongListDisplay: Processed {processedCount} textures in {stopwatch.Elapsed.TotalMilliseconds:F2}ms");
            }
        }

        /// <summary>
        /// Insert texture generation request maintaining sorted order by priority (highest first)
        /// This avoids the need to sort the entire queue on every frame update
        /// </summary>
        private void InsertTextureRequestSorted(TextureGenerationRequest request)
        {
            // Binary search for insertion point to maintain sorted order (highest priority first)
            int left = 0;
            int right = _textureGenerationQueue.Count;

            while (left < right)
            {
                int mid = left + (right - left) / 2;
                if (_textureGenerationQueue[mid].Priority >= request.Priority)
                {
                    left = mid + 1;
                }
                else
                {
                    right = mid;
                }
            }

            // Insert at the correct position to maintain sorted order
            _textureGenerationQueue.Insert(left, request);
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

                // Phase 2: Dispose bar information cache
                foreach (var barInfo in _barInfoCache.Values)
                {
                    barInfo?.Dispose();
                }
                _barInfoCache.Clear();
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
        public bool IsScrollComplete { get; }

        public SongSelectionChangedEventArgs(SongListNode selectedSong, int currentDifficulty, bool isScrollComplete)
        {
            SelectedSong = selectedSong;
            CurrentDifficulty = currentDifficulty;
            IsScrollComplete = isScrollComplete;
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
    }    /// <summary>
         /// Request for texture generation during Update phase
         /// </summary>
    internal class TextureGenerationRequest
    {
        public SongListNode SongNode { get; set; }
        public int SongIndex { get; set; }
        public int BarIndex { get; set; }
        public int Difficulty { get; set; }
        public bool IsSelected { get; set; }
        public int Priority { get; set; } // Higher values = higher priority
    }

    #endregion
}
