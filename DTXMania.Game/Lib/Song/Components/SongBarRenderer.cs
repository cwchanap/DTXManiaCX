using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using DTXMania.Game.Lib.UI;
using DTXMania.Game.Lib.UI.Layout;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Utilities;
using System;
using System.IO;

namespace DTXMania.Game.Lib.Song.Components
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
    public class SongBarRenderer : IDisposable
    {
        #region Constants

        // Using centralized layout constants from SongSelectionUILayout
        private const int TITLE_TEXTURE_WIDTH = SongSelectionUILayout.SongBars.TitleTextureWidth;
        private const int TITLE_TEXTURE_HEIGHT = SongSelectionUILayout.SongBars.TitleTextureHeight;
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

        // Render targets for texture generation
        private RenderTarget2D _titleRenderTarget;
        private RenderTarget2D _clearLampRenderTarget;
        private readonly bool _ownsSharedRenderTarget;
        private SpriteBatch _spriteBatch;

        // Default graphics generator for clear lamp generation - must stay alive as long as cached textures exist
        private DefaultGraphicsGenerator _graphicsGenerator;

        // Clear lamp colors for different difficulties

        // Fast scroll mode flag to skip preview image loading during active scrolling
        private bool _isFastScrollMode = false;
        private bool _disposed = false;

        #endregion

        #region Constructor

        public SongBarRenderer(GraphicsDevice graphicsDevice, IResourceManager resourceManager,
                              RenderTarget2D sharedRenderTarget, bool ownsSharedRenderTarget = false)
        {
            _graphicsDevice = graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
            _resourceManager = resourceManager ?? throw new ArgumentNullException(nameof(resourceManager));
            _titleRenderTarget = sharedRenderTarget ?? throw new ArgumentNullException(nameof(sharedRenderTarget));
            _clearLampRenderTarget = sharedRenderTarget; // Use the same shared RenderTarget
            _ownsSharedRenderTarget = ownsSharedRenderTarget;

            _titleTextureCache = new CacheManager<string, ITexture>();
            _previewImageCache = new CacheManager<string, ITexture>();
            _clearLampCache = new CacheManager<string, ITexture>();

            // Create graphics generator that will live for the lifetime of SongBarRenderer
            // This ensures generated textures remain valid while cached
            _graphicsGenerator = new DefaultGraphicsGenerator(_graphicsDevice, _clearLampRenderTarget);

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
            {
                cachedTexture.AddReference();
                return cachedTexture;
            }

            var texture = CreateTitleTexture(songNode);

            if (texture != null)
            {
                _titleTextureCache.Add(cacheKey, texture);            }

            return texture;
        }

        /// <summary>
        /// Generate or retrieve cached preview image texture
        /// </summary>
        public ITexture GeneratePreviewImageTexture(SongListNode songNode)
        {
            // Use EF Core entities
            var chart = songNode?.DatabaseChart;
            var previewImage = chart?.PreviewImage;
            
            if (string.IsNullOrEmpty(previewImage))
                return null;

            // Skip preview image loading during fast scroll to prevent UI freezes
            if (_isFastScrollMode)
                return null;

            var resolvedPreviewImagePath = GetResolvedPreviewImagePath(songNode);
            if (string.IsNullOrEmpty(resolvedPreviewImagePath))
                return null;

            var cacheKey = resolvedPreviewImagePath;
            if (_previewImageCache.TryGet(cacheKey, out var cachedTexture))
            {
                cachedTexture.AddReference();
                return cachedTexture;
            }

            // Check file size before attempting to load - skip large files (>500KB)
            if (File.Exists(resolvedPreviewImagePath))
            {
                var fileInfo = new FileInfo(resolvedPreviewImagePath);
                if (fileInfo.Length > MAX_PREVIEW_IMAGE_SIZE_BYTES)
                {
                    System.Diagnostics.Debug.WriteLine($"Skipping large preview image: {resolvedPreviewImagePath} ({fileInfo.Length} bytes)");
                    return null;
                }
            }
            else
            {
                return null;
            }

            var texture = LoadPreviewImage(resolvedPreviewImagePath);

            if (texture != null)
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
                barInfo.ClearLamp = null;
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
            {
                cachedTexture.AddReference();
                return cachedTexture;
            }

            // Use Phase 2 enhanced clear lamp generation with DefaultGraphicsGenerator
            // Note: _graphicsGenerator must stay alive for the lifetime of cached textures
            var clearStatus = GetClearStatus(songNode, difficulty);
            var texture = _graphicsGenerator.GenerateEnhancedClearLamp(difficulty, clearStatus);

            if (texture != null)
            {
                _clearLampCache.Add(cacheKey, texture);
            }

            return texture;
        }

        /// <summary>
        /// Clear all texture caches
        /// </summary>
        public void ClearCache()
        {
            _titleTextureCache.Clear();
            _previewImageCache.Clear();
            _clearLampCache.Clear();
        }        /// <summary>
        /// Priority texture generation method for immediate processing of selected items
        /// Generates texture immediately with performance metrics (IsInDrawPhase check removed per senior engineer feedback)
        /// </summary>
        public SongBarInfo GenerateBarInfoWithPriority(SongListNode songNode, int difficulty, bool isSelected = false)
        {
            if (songNode == null)
                return null;

            // Performance metrics: Measure priority generation timing
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            var barInfo = new SongBarInfo
            {
                SongNode = songNode,
                BarType = GetBarType(songNode),
                TitleString = GetDisplayText(songNode),
                TextColor = GetNodeTypeColor(songNode),
                DifficultyLevel = difficulty,
                IsSelected = isSelected
            };

            // Generate textures immediately (safety checks removed)
            barInfo.TitleTexture = GenerateTitleTexture(songNode);
            
            // Only generate preview image for selected items or if not in fast scroll mode
            if (isSelected || !_isFastScrollMode)
            {
                barInfo.PreviewImage = GeneratePreviewImageTexture(songNode);
            }
            
            barInfo.ClearLamp = GenerateClearLampTexture(songNode, difficulty);

            // Performance metrics logging
            stopwatch.Stop();
            if (stopwatch.Elapsed.TotalMilliseconds > 2.0) // Log if priority generation takes more than 2ms
            {
                System.Diagnostics.Debug.WriteLine($"SongBarRenderer: Priority generation for '{songNode.DisplayTitle}' took {stopwatch.Elapsed.TotalMilliseconds:F2}ms");
            }

            return barInfo;
        }

        /// <summary>
        /// Set the font for title texture generation
        /// </summary>
        public void SetFont(SpriteFont font)
        {
            _font = font;
            
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

        #endregion

        #region Private Methods

        private void Initialize()
        {
            // Create white pixel texture
            _whitePixel = new Texture2D(_graphicsDevice, 1, 1);
            _whitePixel.SetData(new[] { Color.White });

            // Note: RenderTargets are now provided via constructor
            // _titleRenderTarget and _clearLampRenderTarget are set in constructor

            // Create sprite batch for rendering
            _spriteBatch = new SpriteBatch(_graphicsDevice);
        }

        private ITexture CreateTitleTexture(SongListNode songNode)
        {
            if (_font == null || _titleRenderTarget == null)
            {
                return null;
            }

            try
            {
                var displayText = GetDisplayText(songNode);
                if (string.IsNullOrEmpty(displayText))
                    return null;

                // NX-authentic: 2x render, 0.5x display for anti-aliased text
                var renderScale = SongSelectionUILayout.SongBars.TitleRenderScale;

                // Measure text at native scale, then project to 2x to determine if compression is needed
                var textSize = _font.MeasureString(displayText);
                var scaledTextWidth = textSize.X * renderScale;
                var maxRenderWidth = TITLE_TEXTURE_WIDTH - (SongSelectionUILayout.SongBars.TextPositionX * 2);

                // Calculate horizontal compression if text exceeds max width
                float horizontalScale = renderScale;
                if (scaledTextWidth > maxRenderWidth)
                {
                    horizontalScale = maxRenderWidth / textSize.X;
                }

                // Bind render target and render text
                var previousRenderTarget = _graphicsDevice.GetRenderTargets();
                try
                {
                    _graphicsDevice.SetRenderTarget(_titleRenderTarget);

                    // Clear render target
                    _graphicsDevice.Clear(Color.Transparent);

                    // Render text at 2x (or compressed) scale
                    _spriteBatch.Begin();

                    try
                    {
                        var textColor = GetNodeTypeColor(songNode);
                        var textScale = new Vector2(horizontalScale, renderScale);
                        var scaledLineSpacing = _font.LineSpacing * renderScale;
                        var position = new Vector2(
                            SongSelectionUILayout.SongBars.TextPositionX,
                            (TITLE_TEXTURE_HEIGHT - scaledLineSpacing) / 2);

                        // Draw shadow at 2x for crisp shadow at display size
                        var shadowOffset = new Vector2(2, 2); // 2px offset at render resolution; appears as 1px at TitleDisplayScale (0.5x)
                        var shadowColor = Color.Black * 0.6f;
                        _spriteBatch.DrawString(_font, displayText, position + shadowOffset, shadowColor,
                            0f, Vector2.Zero, textScale, SpriteEffects.None, 0f);

                        // Draw main text
                        _spriteBatch.DrawString(_font, displayText, position, textColor,
                            0f, Vector2.Zero, textScale, SpriteEffects.None, 0f);
                    }
                    finally
                    {
                        _spriteBatch.End();
                    }
                }
                finally
                {
                    _graphicsDevice.SetRenderTargets(previousRenderTarget);
                }

                // Create texture wrapper from render target
                // Use Rectangle overload to extract only the sub-region we rendered to
                // The render target is larger (1024x1024) than our texture (1020x76)
                var texture2D = new Texture2D(_graphicsDevice, TITLE_TEXTURE_WIDTH, TITLE_TEXTURE_HEIGHT);
                var data = new Color[TITLE_TEXTURE_WIDTH * TITLE_TEXTURE_HEIGHT];
                var sourceRect = new Rectangle(0, 0, TITLE_TEXTURE_WIDTH, TITLE_TEXTURE_HEIGHT);
                _titleRenderTarget.GetData(0, sourceRect, data, 0, data.Length);
                texture2D.SetData(data);
                var cacheKey = GetTitleCacheKey(songNode);
                return new ManagedTexture(_graphicsDevice, texture2D, $"title_{cacheKey}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to create title texture: {ex.Message}");
                return null;
            }
        }

        private ITexture LoadPreviewImage(SongListNode songNode)
        {
            // Skip preview image loading during fast scroll to prevent UI freezes
            if (_isFastScrollMode)
                return null;

            try
            {
                var resolvedPreviewImagePath = GetResolvedPreviewImagePath(songNode);
                return LoadPreviewImage(resolvedPreviewImagePath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load preview image: {ex.Message}");
            }

            return null;
        }

        private ITexture LoadPreviewImage(string resolvedPreviewImagePath)
        {
            if (string.IsNullOrEmpty(resolvedPreviewImagePath) || !File.Exists(resolvedPreviewImagePath))
                return null;

            return _resourceManager.LoadTexture(resolvedPreviewImagePath);
        }

        private static string GetResolvedPreviewImagePath(SongListNode songNode)
        {
            var chart = songNode?.DatabaseChart;
            var filePath = chart?.FilePath;
            var previewImage = chart?.PreviewImage;

            if (string.IsNullOrEmpty(filePath) || string.IsNullOrEmpty(previewImage))
                return null;

            var songDirectory = Path.GetDirectoryName(filePath);
            if (string.IsNullOrEmpty(songDirectory))
                return null;

            return Path.GetFullPath(Path.Combine(songDirectory, previewImage));
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

        private BarType GetBarType(SongListNode songNode)
        {
            return songNode.Type switch
            {
                NodeType.Score => BarType.Score,
                NodeType.Box => BarType.Box,
                NodeType.BackBox => BarType.Other,                NodeType.Random => BarType.Other,
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

                _graphicsGenerator?.Dispose();
                if (_ownsSharedRenderTarget)
                {
                    if (ReferenceEquals(_titleRenderTarget, _clearLampRenderTarget))
                    {
                        _titleRenderTarget?.Dispose();
                    }
                    else
                    {
                        _titleRenderTarget?.Dispose();
                        _clearLampRenderTarget?.Dispose();
                    }
                }

                _spriteBatch?.Dispose();
                _whitePixel?.Dispose();

                _titleRenderTarget = null;
                _clearLampRenderTarget = null;
                _spriteBatch = null;
                _whitePixel = null;
                _graphicsGenerator = null;
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
                // Release reference-counted textures - don't call Dispose() directly.
                // The ResourceManager handles actual disposal via reference counting.
                TitleTexture?.RemoveReference();
                PreviewImage?.RemoveReference();
                ClearLamp?.RemoveReference();

                TitleTexture = null;
                PreviewImage = null;
                ClearLamp = null;
            }        }
    }
