using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using DTX.UI;
using DTX.Song;
using DTX.Resources;
using DTX.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace DTX.UI.Components
{
    /// <summary>
    /// Song bar texture generation and caching system
    /// Equivalent to DTXManiaNX song bar generation methods
    ///
    /// CRITICAL FIX: Added draw phase safety checks to prevent screen blackouts
    /// - IsInDrawPhase property prevents texture generation during draw operations
    /// - RenderTarget switching during draw phase causes screen blackouts
    /// - All texture generation methods now return cached/null during draw phase
    /// </summary>
    public class SongBarRenderer : IDisposable    {
        #region Constants

        private const int TITLE_TEXTURE_WIDTH = 400;
        private const int TITLE_TEXTURE_HEIGHT = 24;
        private const int PREVIEW_IMAGE_SIZE = 24;
        private const int CLEAR_LAMP_WIDTH = 8;
        private const int CLEAR_LAMP_HEIGHT = 24;
        private const int MAX_PREVIEW_IMAGE_SIZE_BYTES = 500 * 1024; // 500KB

        #endregion

        #region Fields

        private readonly GraphicsDevice _graphicsDevice;
        private readonly IResourceManager _resourceManager;
        private SpriteFont _font;
        private Texture2D _whitePixel;        // Texture caches using consolidated cache manager
        private readonly CacheManager<string, ITexture> _titleTextureCache;
        private readonly CacheManager<string, ITexture> _previewImageCache;
        private readonly CacheManager<string, ITexture> _clearLampCache;

        // Async texture loading components
        private readonly TextureLoader _textureLoader;
        private readonly PlaceholderTextureManager _placeholderManager;

        // Render targets for texture generation
        private RenderTarget2D _titleRenderTarget;
        private RenderTarget2D _clearLampRenderTarget;
        private SpriteBatch _spriteBatch;        // Clear lamp colors for different difficulties
        private static readonly Color[] DifficultyColors = new[]
        {
            Color.Green,    // Difficulty 0 - Easy
            Color.Yellow,   // Difficulty 1 - Normal  
            Color.Orange,   // Difficulty 2 - Hard
            Color.Red,      // Difficulty 3 - Expert
            Color.Purple    // Difficulty 4 - Master
        };

        // Fast scroll mode flag to skip preview image loading during active scrolling
        private bool _isFastScrollMode = false;
        private bool _disposed = false;

        // Draw phase safety check to prevent texture generation during draw
        public bool IsInDrawPhase { get; set; }

        #endregion

        #region Constructor

        public SongBarRenderer(GraphicsDevice graphicsDevice, IResourceManager resourceManager)
        {
            _graphicsDevice = graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
            _resourceManager = resourceManager ?? throw new ArgumentNullException(nameof(resourceManager));

            _titleTextureCache = new CacheManager<string, ITexture>();
            _previewImageCache = new CacheManager<string, ITexture>();
            _clearLampCache = new CacheManager<string, ITexture>();

            // Initialize async texture loading components
            _textureLoader = new TextureLoader(resourceManager, graphicsDevice);
            _placeholderManager = new PlaceholderTextureManager(graphicsDevice);

            Initialize();
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Generate or retrieve cached title texture for a song node
        /// </summary>
        public ITexture GenerateTitleTexture(SongListNode songNode)
        {
            if (songNode == null)
                return null;

            var cacheKey = GetTitleCacheKey(songNode);

            if (_titleTextureCache.TryGet(cacheKey, out var cachedTexture))
                return cachedTexture;

            // Safety check: NEVER generate during draw phase to prevent screen blackouts
            if (IsInDrawPhase)
            {
                // Return cached or null - NEVER generate during draw
                return _titleTextureCache.TryGet(cacheKey, out var cached) ? cached : null;
            }

            var texture = CreateTitleTexture(songNode);            if (texture != null)
            {
                _titleTextureCache.Add(cacheKey, texture);
            }

            return texture;
        }        /// <summary>
        /// Generate or retrieve cached preview image texture
        /// </summary>
        public ITexture GeneratePreviewImageTexture(SongListNode songNode)
        {
            if (songNode?.Metadata?.PreviewImage == null)
                return null;

            // Skip preview image loading during fast scroll to prevent UI freezes
            if (_isFastScrollMode)
                return null;

            var cacheKey = songNode.Metadata.PreviewImage;

            if (_previewImageCache.TryGet(cacheKey, out var cachedTexture))
                return cachedTexture;

            // Safety check: NEVER generate during draw phase to prevent screen blackouts
            if (IsInDrawPhase)
            {
                // Return cached or null - NEVER generate during draw
                return _previewImageCache.TryGet(cacheKey, out var cached) ? cached : null;
            }

            // Check file size before attempting to load - skip large files (>500KB)
            if (songNode.Metadata?.FilePath != null)
            {
                var songDirectory = Path.GetDirectoryName(songNode.Metadata.FilePath);
                var previewImagePath = Path.Combine(songDirectory, songNode.Metadata.PreviewImage);

                // Quick file existence and size check
                if (!File.Exists(previewImagePath))
                    return null;                var fileInfo = new FileInfo(previewImagePath);
                if (fileInfo.Length > MAX_PREVIEW_IMAGE_SIZE_BYTES)
                {
                    System.Diagnostics.Debug.WriteLine($"Skipping large preview image: {previewImagePath} ({fileInfo.Length} bytes)");
                    return null;
                }
            }

            var texture = LoadPreviewImage(songNode);            if (texture != null)
            {
                _previewImageCache.Add(cacheKey, texture);
            }

            return texture;
        }

        /// <summary>
        /// Generate complete bar information for a song node
        /// Equivalent to DTXManiaNX t現在選択中の曲を元に曲バーを再構成する() method
        /// </summary>
        public SongBarInfo GenerateBarInfo(SongListNode songNode, int difficulty, bool isSelected = false)
        {
            if (songNode == null)
                return null;

            var barInfo = new SongBarInfo
            {
                SongNode = songNode,
                BarType = GetBarType(songNode),
                TitleString = GetDisplayText(songNode),
                TextColor = GetNodeTypeColor(songNode),
                DifficultyLevel = difficulty,
                IsSelected = isSelected
            };

            // Generate textures
            barInfo.TitleTexture = GenerateTitleTexture(songNode);
            barInfo.PreviewImage = GeneratePreviewImageTexture(songNode);
            barInfo.ClearLamp = GenerateClearLampTexture(songNode, difficulty);

            return barInfo;
        }

        /// <summary>
        /// Update bar information when state changes (selection, difficulty)
        /// </summary>
        public void UpdateBarInfo(SongBarInfo barInfo, int newDifficulty, bool isSelected)
        {
            if (barInfo == null)
                return;

            var stateChanged = barInfo.DifficultyLevel != newDifficulty || barInfo.IsSelected != isSelected;

            barInfo.DifficultyLevel = newDifficulty;
            barInfo.IsSelected = isSelected;
            barInfo.TextColor = isSelected ? Color.Yellow : GetNodeTypeColor(barInfo.SongNode);

            // Regenerate clear lamp if difficulty changed
            if (stateChanged && barInfo.SongNode?.Type == NodeType.Score)
            {
                barInfo.ClearLamp?.RemoveReference();
                barInfo.ClearLamp = GenerateClearLampTexture(barInfo.SongNode, newDifficulty);
            }
        }

        /// <summary>
        /// Generate or retrieve cached clear lamp texture
        /// Uses Phase 2 enhanced clear lamp generation
        /// </summary>
        public ITexture GenerateClearLampTexture(SongListNode songNode, int difficulty)
        {
            if (songNode?.Type != NodeType.Score)
                return null;

            var cacheKey = GetClearLampCacheKey(songNode, difficulty);

            if (_clearLampCache.TryGet(cacheKey, out var cachedTexture))
                return cachedTexture;

            // Safety check: NEVER generate during draw phase to prevent screen blackouts
            if (IsInDrawPhase)
            {
                // Return cached or null - NEVER generate during draw
                return _clearLampCache.TryGet(cacheKey, out var cached) ? cached : null;
            }

            // Use Phase 2 enhanced clear lamp generation with DefaultGraphicsGenerator
            var clearStatus = GetClearStatus(songNode, difficulty);
            var graphicsGenerator = new DefaultGraphicsGenerator(_graphicsDevice);

            // CRITICAL: Sync draw phase state to prevent render target issues
            graphicsGenerator.IsInDrawPhase = this.IsInDrawPhase;

            var texture = graphicsGenerator.GenerateEnhancedClearLamp(difficulty, clearStatus);            if (texture != null)
            {
                _clearLampCache.Add(cacheKey, texture);
            }

            return texture;
        }        /// <summary>
        /// Clear all texture caches
        /// </summary>
        public void ClearCache()
        {
            _titleTextureCache.Clear();
            _previewImageCache.Clear();
            _clearLampCache.Clear();
        }        /// <summary>
        /// Set the font for title texture generation
        /// </summary>
        public void SetFont(SpriteFont font)
        {
            _font = font ?? CreateFallbackFont();

            // Debug logging for font issues
            if (_font == null)
            {
                System.Diagnostics.Debug.WriteLine("SongBarRenderer: Warning - No font available for text rendering");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"SongBarRenderer: Font set successfully - LineSpacing: {_font.LineSpacing}");
            }

            // Clear title cache since font changed
            _titleTextureCache.Clear();
        }

        /// <summary>
        /// Enable or disable fast scroll mode to skip preview image loading during active scrolling
        /// </summary>
        public void SetFastScrollMode(bool enabled)
        {
            _isFastScrollMode = enabled;
        }

        /// <summary>
        /// Check if fast scroll mode is currently enabled
        /// </summary>
        public bool IsFastScrollMode => _isFastScrollMode;

        /// <summary>
        /// Generate or retrieve title texture asynchronously with placeholder
        /// Returns placeholder immediately, loads actual texture in background
        /// </summary>
        public async Task<ITexture> GenerateTitleTextureAsync(SongListNode songNode)
        {
            if (songNode == null)
                return _placeholderManager.GetTitlePlaceholder();

            var cacheKey = GetTitleCacheKey(songNode);

            // Check cache first
            if (_titleTextureCache.TryGet(cacheKey, out var cachedTexture))
                return cachedTexture;

            // Create texture synchronously since GraphicsDevice requires main thread
            var texture = CreateTitleTexture(songNode);
            if (texture != null)
            {
                _titleTextureCache.Add(cacheKey, texture);
                return texture;
            }

            // Return placeholder if creation failed
            return _placeholderManager.GetTitlePlaceholder();
        }

        /// <summary>
        /// Generate or retrieve preview image texture asynchronously with placeholder
        /// Returns placeholder immediately, loads actual texture in background
        /// </summary>
        public async Task<ITexture> GeneratePreviewImageTextureAsync(SongListNode songNode)
        {
            if (songNode?.Metadata?.PreviewImage == null)
                return _placeholderManager.GetPreviewImagePlaceholder();

            // Skip preview image loading during fast scroll to prevent UI freezes
            if (_isFastScrollMode)
                return _placeholderManager.GetPreviewImagePlaceholder();

            var cacheKey = songNode.Metadata.PreviewImage;

            // Check cache first
            if (_previewImageCache.TryGet(cacheKey, out var cachedTexture))
                return cachedTexture;

            // Get preview image path
            var previewPath = GetPreviewImagePath(songNode);
            if (string.IsNullOrEmpty(previewPath))
                return _placeholderManager.GetPreviewImagePlaceholder();

            // Use TextureLoader for async loading with placeholder
            return await _textureLoader.LoadTextureAsync(previewPath, false);
        }

        /// <summary>
        /// Pre-load textures for song range to improve scrolling performance
        /// </summary>
        public void PreloadTexturesForRange(System.Collections.Generic.IList<SongListNode> songNodes,
                                           int centerIndex, int preloadRange = 3)
        {
            _textureLoader.PreloadTextures(songNodes, centerIndex, preloadRange);
        }

        /// <summary>
        /// Get title placeholder texture for immediate display
        /// </summary>
        public ITexture GetTitlePlaceholder()
        {
            return _placeholderManager?.GetTitlePlaceholder();
        }

        /// <summary>
        /// Get preview image placeholder texture for immediate display
        /// </summary>
        public ITexture GetPreviewImagePlaceholder()
        {
            return _placeholderManager?.GetPreviewImagePlaceholder();
        }

        /// <summary>
        /// Get clear lamp placeholder texture for immediate display
        /// </summary>
        public ITexture GetClearLampPlaceholder()
        {
            return _placeholderManager?.GetClearLampPlaceholder();
        }

        #endregion

        #region Private Methods

        private void Initialize()
        {
            // Create white pixel texture
            _whitePixel = new Texture2D(_graphicsDevice, 1, 1);
            _whitePixel.SetData(new[] { Color.White });

            // Create render targets
            _titleRenderTarget = new RenderTarget2D(_graphicsDevice, TITLE_TEXTURE_WIDTH, TITLE_TEXTURE_HEIGHT);
            _clearLampRenderTarget = new RenderTarget2D(_graphicsDevice, CLEAR_LAMP_WIDTH, CLEAR_LAMP_HEIGHT);

            // Create sprite batch for rendering
            _spriteBatch = new SpriteBatch(_graphicsDevice);

            // Ensure we have a font for rendering
            if (_font == null)
            {
                _font = CreateFallbackFont();
            }
        }

        /// <summary>
        /// Create a simple fallback font when no font is available
        /// </summary>
        private SpriteFont CreateFallbackFont()
        {
            try
            {
                // Try to load a system font through the resource manager
                var fallbackFont = _resourceManager?.LoadFont("Arial", 16, FontStyle.Regular);
                return fallbackFont?.SpriteFont;
            }
            catch
            {
                // If that fails, create a minimal texture-based font
                return CreateMinimalFont();
            }
        }

        /// <summary>
        /// Create a minimal texture-based font as last resort
        /// </summary>
        private SpriteFont CreateMinimalFont()
        {
            try
            {
                // Create a simple 8x8 character texture for basic text rendering
                var fontTexture = new Texture2D(_graphicsDevice, 128, 128);
                var fontData = new Color[128 * 128];

                // Fill with a simple pattern to represent characters
                for (int i = 0; i < fontData.Length; i++)
                {
                    int x = i % 128;
                    int y = i / 128;

                    // Create a simple grid pattern for character representation
                    bool isCharacterPixel = (x % 8 < 6) && (y % 8 < 6) &&
                                          ((x % 8 == 0) || (y % 8 == 0) ||
                                           (x % 8 == 5) || (y % 8 == 5));

                    fontData[i] = isCharacterPixel ? Color.White : Color.Transparent;
                }

                fontTexture.SetData(fontData);

                // Create character rectangles for ASCII characters
                var glyphBounds = new List<Rectangle>();
                var cropping = new List<Rectangle>();
                var chars = new List<char>();
                var kerning = new List<Vector3>();

                // Create basic ASCII character set (32-126)
                for (int c = 32; c <= 126; c++)
                {
                    chars.Add((char)c);

                    // Each character is 8x8 pixels
                    int charIndex = c - 32;
                    int charsPerRow = 16;
                    int charX = (charIndex % charsPerRow) * 8;
                    int charY = (charIndex / charsPerRow) * 8;

                    glyphBounds.Add(new Rectangle(charX, charY, 8, 8));
                    cropping.Add(new Rectangle(0, 0, 8, 8));
                    kerning.Add(new Vector3(0, 8, 1)); // left, width, right
                }

                return new SpriteFont(fontTexture, glyphBounds, cropping, chars, 8, 0, kerning, ' ');
            }
            catch
            {
                // If even this fails, return null and let the calling code handle it
                return null;
            }
        }

        private ITexture CreateTitleTexture(SongListNode songNode)
        {
            if (_titleRenderTarget == null)
            {
                return null;
            }

            // Ensure we have a font
            if (_font == null)
            {
                _font = CreateFallbackFont();
                if (_font == null)
                {
                    // If we still can't get a font, create a simple colored rectangle as fallback
                    return CreateTextFallbackTexture(songNode);
                }
            }

            try
            {
                var displayText = GetDisplayText(songNode);
                if (string.IsNullOrEmpty(displayText))
                {
                    System.Diagnostics.Debug.WriteLine("CreateTitleTexture: Display text is null or empty");
                    return null;
                }

                System.Diagnostics.Debug.WriteLine($"CreateTitleTexture: Creating texture for '{displayText}'");
                System.Diagnostics.Debug.WriteLine($"CreateTitleTexture: _spriteBatch null? {_spriteBatch == null}");
                System.Diagnostics.Debug.WriteLine($"CreateTitleTexture: _font null? {_font == null}");
                System.Diagnostics.Debug.WriteLine($"CreateTitleTexture: _titleRenderTarget null? {_titleRenderTarget == null}");

                // Set render target
                _graphicsDevice.SetRenderTarget(_titleRenderTarget);
                _graphicsDevice.Clear(Color.Transparent);

                // Render text
                _spriteBatch.Begin();

                var textColor = GetNodeTypeColor(songNode);
                var position = new Vector2(5, (TITLE_TEXTURE_HEIGHT - _font.LineSpacing) / 2);

                System.Diagnostics.Debug.WriteLine($"CreateTitleTexture: About to draw string - position: {position}, color: {textColor}");
                _spriteBatch.DrawString(_font, displayText, position, textColor);

                _spriteBatch.End();

                // Reset render target
                _graphicsDevice.SetRenderTarget(null);

                // Create texture wrapper
                var texture2D = new Texture2D(_graphicsDevice, TITLE_TEXTURE_WIDTH, TITLE_TEXTURE_HEIGHT);
                var data = new Color[TITLE_TEXTURE_WIDTH * TITLE_TEXTURE_HEIGHT];
                _titleRenderTarget.GetData(data);
                texture2D.SetData(data);

                var cacheKey = GetTitleCacheKey(songNode);
                System.Diagnostics.Debug.WriteLine($"CreateTitleTexture: Successfully created texture for '{displayText}'");
                return new ManagedTexture(_graphicsDevice, texture2D, $"title_{cacheKey}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to create title texture: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                return null;
            }
        }        private ITexture LoadPreviewImage(SongListNode songNode)
        {
            // Skip preview image loading during fast scroll to prevent UI freezes
            if (_isFastScrollMode)
                return null;

            try
            {
                if (songNode.Metadata?.FilePath != null)
                {
                    var songDirectory = Path.GetDirectoryName(songNode.Metadata.FilePath);
                    var previewImagePath = Path.Combine(songDirectory, songNode.Metadata.PreviewImage);

                    // Quick file existence check - return immediately if file doesn't exist
                    if (!File.Exists(previewImagePath))
                        return null;

                    return _resourceManager.LoadTexture(previewImagePath);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load preview image: {ex.Message}");
            }

            return null;
        }

        private ITexture CreateClearLampTexture(SongListNode songNode, int difficulty)
        {
            if (_clearLampRenderTarget == null)
                return null;

            try
            {
                // Get clear status for this difficulty
                var clearStatus = GetClearStatus(songNode, difficulty);
                var lampColor = GetClearLampColor(clearStatus, difficulty);

                // Set render target
                _graphicsDevice.SetRenderTarget(_clearLampRenderTarget);
                _graphicsDevice.Clear(Color.Transparent);

                // Render clear lamp
                _spriteBatch.Begin();
                
                var lampBounds = new Rectangle(0, 0, CLEAR_LAMP_WIDTH, CLEAR_LAMP_HEIGHT);
                _spriteBatch.Draw(_whitePixel, lampBounds, lampColor);
                
                _spriteBatch.End();

                // Reset render target
                _graphicsDevice.SetRenderTarget(null);

                // Create texture wrapper
                var texture2D = new Texture2D(_graphicsDevice, CLEAR_LAMP_WIDTH, CLEAR_LAMP_HEIGHT);
                var data = new Color[CLEAR_LAMP_WIDTH * CLEAR_LAMP_HEIGHT];
                _clearLampRenderTarget.GetData(data);
                texture2D.SetData(data);

                var cacheKey = GetClearLampCacheKey(songNode, difficulty);
                return new ManagedTexture(_graphicsDevice, texture2D, $"clearlamp_{cacheKey}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to create clear lamp texture: {ex.Message}");
                return null;
            }
        }

        private string GetTitleCacheKey(SongListNode songNode)
        {
            return $"{songNode.Type}_{songNode.DisplayTitle}_{songNode.GetHashCode()}";
        }

        private string GetClearLampCacheKey(SongListNode songNode, int difficulty)
        {
            var clearStatus = GetClearStatus(songNode, difficulty);
            return $"{songNode.GetHashCode()}_{difficulty}_{clearStatus}";
        }

        private string GetPreviewImagePath(SongListNode songNode)
        {
            if (songNode?.Metadata?.PreviewImage == null || songNode.Metadata?.FilePath == null)
                return null;

            var songDirectory = System.IO.Path.GetDirectoryName(songNode.Metadata.FilePath);
            if (songDirectory == null)
                return null;

            return System.IO.Path.Combine(songDirectory, songNode.Metadata.PreviewImage);
        }

        /// <summary>
        /// Create a simple colored rectangle as text fallback when no font is available
        /// </summary>
        private ITexture CreateTextFallbackTexture(SongListNode songNode)
        {
            try
            {
                // Create a simple colored rectangle based on node type
                var texture2D = new Texture2D(_graphicsDevice, TITLE_TEXTURE_WIDTH, TITLE_TEXTURE_HEIGHT);
                var colorData = new Color[TITLE_TEXTURE_WIDTH * TITLE_TEXTURE_HEIGHT];

                // Get color based on node type
                var nodeColor = GetNodeTypeColor(songNode);

                // Create a simple pattern to represent text
                for (int i = 0; i < colorData.Length; i++)
                {
                    int x = i % TITLE_TEXTURE_WIDTH;
                    int y = i / TITLE_TEXTURE_WIDTH;

                    // Create a simple bar pattern
                    bool isBar = y >= 8 && y <= 16 && x >= 10 && x <= TITLE_TEXTURE_WIDTH - 10;
                    colorData[i] = isBar ? nodeColor : Color.Transparent;
                }

                texture2D.SetData(colorData);
                return new ManagedTexture(_graphicsDevice, texture2D, $"fallback_{songNode?.GetHashCode()}");
            }
            catch
            {
                return null;
            }
        }

        private string GetDisplayText(SongListNode songNode)
        {
            return songNode.Type switch
            {
                NodeType.BackBox => ".. (Back)",
                NodeType.Box => $"[{songNode.DisplayTitle}]",
                NodeType.Random => "*** RANDOM SELECT ***",
                NodeType.Score => songNode.DisplayTitle ?? "Unknown Song",
                _ => songNode.DisplayTitle ?? "Unknown"
            };
        }

        private Color GetNodeTypeColor(SongListNode songNode)
        {
            if (songNode == null)
                return Color.White;

            return songNode.Type switch
            {
                NodeType.Box => Color.Cyan,
                NodeType.BackBox => Color.Orange,
                NodeType.Random => Color.Magenta,
                NodeType.Score => Color.White,
                _ => Color.White
            };
        }

        private ClearStatus GetClearStatus(SongListNode songNode, int difficulty)
        {
            // TODO: Get actual clear status from song scores
            // For now, return a placeholder based on difficulty
            if (songNode.Scores?.Length > difficulty && songNode.Scores[difficulty] != null)
            {
                var score = songNode.Scores[difficulty];
                if (score.FullCombo)
                    return ClearStatus.FullCombo;
                else if (score.BestRank >= 80)
                    return ClearStatus.Clear;
                else if (score.PlayCount > 0)
                    return ClearStatus.Failed;
            }
            
            return ClearStatus.NotPlayed;
        }

        private Color GetClearLampColor(ClearStatus clearStatus, int difficulty)
        {
            var baseColor = difficulty < DifficultyColors.Length ? DifficultyColors[difficulty] : Color.Gray;

            return clearStatus switch
            {
                ClearStatus.FullCombo => Color.Gold,
                ClearStatus.Clear => baseColor,
                ClearStatus.Failed => baseColor * 0.5f,
                ClearStatus.NotPlayed => Color.Gray * 0.3f,
                _ => Color.Gray * 0.3f
            };
        }

        private BarType GetBarType(SongListNode songNode)
        {
            return songNode.Type switch
            {
                NodeType.Score => BarType.Score,
                NodeType.Box => BarType.Box,
                NodeType.BackBox => BarType.Other,
                NodeType.Random => BarType.Other,
                _ => BarType.Other
            };
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (!_disposed)
            {
                ClearCache();

                _titleRenderTarget?.Dispose();
                _clearLampRenderTarget?.Dispose();
                _spriteBatch?.Dispose();
                _whitePixel?.Dispose();

                // Dispose async texture loading components
                _textureLoader?.Dispose();
                _placeholderManager?.Dispose();

                _disposed = true;
            }
        }

        #endregion
    }

    /// <summary>
    /// Clear status for songs
    /// </summary>
    public enum ClearStatus
    {
        NotPlayed,
        Failed,
        Clear,
        FullCombo
    }

    /// <summary>
    /// Bar type for different content types
    /// Equivalent to DTXManiaNX EBarType
    /// </summary>
    public enum BarType
    {
        Score,      // Regular songs
        Box,        // Folders/directories
        Other       // Random, back navigation, etc.
    }

    /// <summary>
    /// Complete bar information structure
    /// Equivalent to DTXManiaNX STBarInformation
    /// </summary>
    public class SongBarInfo : IDisposable
    {
        public SongListNode SongNode { get; set; }
        public BarType BarType { get; set; }
        public string TitleString { get; set; }
        public ITexture TitleTexture { get; set; }
        public Color TextColor { get; set; }
        public ITexture PreviewImage { get; set; }
        public ITexture ClearLamp { get; set; }
        public int DifficultyLevel { get; set; }
        public bool IsSelected { get; set; }

        public void Dispose()
        {
            TitleTexture?.Dispose();
            PreviewImage?.Dispose();
            ClearLamp?.Dispose();
        }
    }
}
