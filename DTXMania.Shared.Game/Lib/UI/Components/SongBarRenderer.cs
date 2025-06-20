using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using DTX.UI;
using DTX.Song;
using DTX.Resources;
using DTX.Utilities;
using System;
using System.Collections.Generic;
using System.IO;

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

        public SongBarRenderer(GraphicsDevice graphicsDevice, IResourceManager resourceManager, 
                              RenderTarget2D sharedRenderTarget)
        {
            _graphicsDevice = graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
            _resourceManager = resourceManager ?? throw new ArgumentNullException(nameof(resourceManager));
            _titleRenderTarget = sharedRenderTarget ?? throw new ArgumentNullException(nameof(sharedRenderTarget));
            _clearLampRenderTarget = sharedRenderTarget; // Use the same shared RenderTarget

            _titleTextureCache = new CacheManager<string, ITexture>();
            _previewImageCache = new CacheManager<string, ITexture>();
            _clearLampCache = new CacheManager<string, ITexture>();

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
                barInfo.ClearLamp?.Dispose();
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
            }            // Use Phase 2 enhanced clear lamp generation with DefaultGraphicsGenerator
            var clearStatus = GetClearStatus(songNode, difficulty);
            var graphicsGenerator = new DefaultGraphicsGenerator(_graphicsDevice, _clearLampRenderTarget);
            var texture = graphicsGenerator.GenerateEnhancedClearLamp(difficulty, clearStatus);if (texture != null)
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

                // Set render target
                _graphicsDevice.SetRenderTarget(_titleRenderTarget);
                _graphicsDevice.Clear(Color.Transparent);

                // Render text
                _spriteBatch.Begin();
                
                var textColor = GetNodeTypeColor(songNode);
                var position = new Vector2(5, (TITLE_TEXTURE_HEIGHT - _font.LineSpacing) / 2);
                
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
                return new ManagedTexture(_graphicsDevice, texture2D, $"title_{cacheKey}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to create title texture: {ex.Message}");
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
