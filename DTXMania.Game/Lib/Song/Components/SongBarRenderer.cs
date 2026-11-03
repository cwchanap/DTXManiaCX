using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.UI;
using DTXMania.Game.Lib.UI.Layout;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using SongScore = DTXMania.Game.Lib.Song.Entities.SongScore;

namespace DTXMania.Game.Lib.Song.Components
{
    /// <summary>
    /// Song bar texture generation and caching system
    /// Equivalent to DTXManiaNX song bar generation methods
    ///
    /// Texture generation is performed in the Update phase (not Draw) to prevent
    /// screen blackouts from RenderTarget switching during draw operations.
    /// </summary>
    public class SongBarRenderer : IDisposable
    {
        #region Constants

        // Using centralized layout constants from SongSelectionUILayout
        private const int TITLE_TEXTURE_WIDTH = SongSelectionUILayout.SongBars.TitleTextureWidth;
        private const int TITLE_TEXTURE_HEIGHT = SongSelectionUILayout.SongBars.TitleTextureHeight;
        // Guards against pathological files (corrupt headers claiming huge
        // buffers), not jacket quality — real jackets well past 500KB decode
        // once and then live in the texture cache, so refusing to load them
        // just hides legitimate art behind the skin's "no preview" tile.
        private const int MAX_PREVIEW_IMAGE_SIZE_BYTES = 8 * 1024 * 1024; // 8 MiB
        // The encoded-size cap does not bound decoded size: a highly
        // compressible file can stay under it while declaring dimensions that
        // explode on decode (width × height × 4 bytes). Header-declared
        // dimensions are checked against this decoded budget before the file
        // reaches Texture2D.FromStream. 256 MiB admits an 8192×8192 jacket —
        // already at the GPU texture ceiling on common hardware.
        private const long MAX_PREVIEW_IMAGE_DECODED_BYTES = 256L * 1024 * 1024;

        // FIFO bounds so a long scroll session cannot grow the texture caches
        // without limit. Clear lamps key per (node, difficulty, play-speed), so
        // their bound spans more difficulty cycling than the per-node bounds.
        private const int MAX_TITLE_TEXTURE_CACHE_ENTRIES = SongSelectionUILayout.SongBars.VisibleItems * 4;
        private const int MAX_PREVIEW_IMAGE_CACHE_ENTRIES = SongSelectionUILayout.SongBars.VisibleItems * 4;
        private const int MAX_CLEAR_LAMP_CACHE_ENTRIES = SongSelectionUILayout.SongBars.VisibleItems * 8;

        #endregion

        #region Fields

        private readonly GraphicsDevice _graphicsDevice;
        private readonly IResourceManager _resourceManager;
        private SpriteFont _font;
        private Texture2D _whitePixel;        // Texture caches using consolidated cache manager
        private readonly CacheManager<string, ITexture> _titleTextureCache;
        private readonly CacheManager<string, ITexture> _previewImageCache;
        // CacheManager cannot hold nulls, so failed loads get their own
        // negative cache — otherwise every bar-info regeneration re-reads
        // and re-decodes a corrupt jacket file. Only attempted loads are
        // cached; a missing or guard-rejected file stays retryable.
        private readonly HashSet<string> _failedPreviewPaths;
        private readonly CacheManager<string, ITexture> _clearLampCache;
        // Insertion order for the bounded caches above; eviction is
        // refcount-aware — see EvictGeneratedTextureOverflow /
        // EvictPreviewImageOverflow for why CacheManager.Remove is unsafe here.
        private readonly Queue<string> _titleTextureCacheOrder = new();
        private readonly Queue<string> _previewImageCacheOrder = new();
        private readonly Queue<string> _clearLampCacheOrder = new();

        // GPU disposal deferred to the update phase. Cache evictions run
        // inside the deferred SpriteBatch during draw, and a texture queued
        // for drawing must not be freed before the batch flushes.
        // FlushDeferredReclamation drains these once per frame.
        private readonly Queue<ITexture> _pendingDispose = new();
        private bool _reclamationPending;

        // Render targets for texture generation
        private RenderTarget2D _titleRenderTarget;
        private RenderTarget2D _clearLampRenderTarget;
        private readonly bool _ownsSharedRenderTarget;
        private SpriteBatch _spriteBatch;

        // Default graphics generator for clear lamp generation - must stay alive as long as cached textures exist
        private DefaultGraphicsGenerator _graphicsGenerator;

        // Fast scroll mode flag to skip preview image loading during active scrolling
        private bool _isFastScrollMode = false;
        private bool _disposed = false;
        private int _playSpeedPercent = PlaySpeedRange.Default;

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
            _failedPreviewPaths = new HashSet<string>(AppPaths.SkinPathComparer);
            _clearLampCache = new CacheManager<string, ITexture>();

            // Create graphics generator that will live for the lifetime of SongBarRenderer
            // This ensures generated textures remain valid while cached
            _graphicsGenerator = new DefaultGraphicsGenerator(_graphicsDevice, _clearLampRenderTarget);

            Initialize();
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Exact speed profile used by compatibility overloads.
        /// Changing it invalidates cached clear-lamp textures.
        /// </summary>
        public int PlaySpeedPercent
        {
            get => _playSpeedPercent;
            set
            {
                if (_playSpeedPercent == value)
                    return;

                _playSpeedPercent = value;
                ReleaseClearLampCache();
            }
        }

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
                _titleTextureCache.Add(cacheKey, texture);
                texture.AddReference(); // Cache holds one reference, caller gets another
                _titleTextureCacheOrder.Enqueue(cacheKey);
                EvictGeneratedTextureOverflow(_titleTextureCache, _titleTextureCacheOrder, MAX_TITLE_TEXTURE_CACHE_ENTRIES, generatorOwned: false);
            }

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
            if (_failedPreviewPaths.Contains(cacheKey))
                return null;

            // Existence, size, and decode-failure guards live in the shared load helper.
            var texture = LoadPreviewImage(resolvedPreviewImagePath, out var cacheableFailure);

            if (texture != null)
            {
                _previewImageCache.Add(cacheKey, texture);
                texture.AddReference(); // Cache holds one reference, caller gets another
                _previewImageCacheOrder.Enqueue(cacheKey);
                EvictPreviewImageOverflow();
            }
            else if (cacheableFailure)
            {
                _failedPreviewPaths.Add(cacheKey);
            }

            return texture;
        }

        /// <summary>
        /// Generate complete bar information for a song node
        /// Equivalent to DTXManiaNX t現在選択中の曲を元に曲バーを再構成する() method
        /// </summary>
        public SongBarInfo GenerateBarInfo(SongListNode songNode, int difficulty, bool isSelected = false)
        {
            return GenerateBarInfo(
                songNode,
                difficulty,
                PlaySpeedPercent,
                isSelected);
        }

        /// <summary>
        /// Generates bar information for one exact score-speed profile.
        /// </summary>
        public SongBarInfo GenerateBarInfo(
            SongListNode songNode,
            int difficulty,
            int playSpeedPercent,
            bool isSelected = false)
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
                PlaySpeedPercent = playSpeedPercent,
                IsSelected = isSelected
            };

            // Generate textures
            barInfo.TitleTexture = GenerateTitleTexture(songNode);
            barInfo.PreviewImage = GeneratePreviewImageTexture(songNode);
            barInfo.ClearLamp = GenerateClearLampTexture(
                songNode,
                difficulty,
                playSpeedPercent);

            return barInfo;
        }

        /// <summary>
        /// Update bar information when state changes (selection, difficulty)
        /// </summary>
        public void UpdateBarInfo(SongBarInfo barInfo, int newDifficulty, bool isSelected)
        {
            UpdateBarInfo(
                barInfo,
                newDifficulty,
                PlaySpeedPercent,
                isSelected);
        }

        /// <summary>
        /// Updates cached bar state for one exact score-speed profile.
        /// </summary>
        public void UpdateBarInfo(
            SongBarInfo barInfo,
            int newDifficulty,
            int playSpeedPercent,
            bool isSelected)
        {
            if (barInfo == null)
                return;

            var stateChanged =
                barInfo.DifficultyLevel != newDifficulty
                || barInfo.PlaySpeedPercent != playSpeedPercent
                || barInfo.IsSelected != isSelected;

            barInfo.DifficultyLevel = newDifficulty;
            barInfo.PlaySpeedPercent = playSpeedPercent;
            barInfo.IsSelected = isSelected;
            barInfo.TextColor = isSelected ? Color.Yellow : GetNodeTypeColor(barInfo.SongNode);

            // Regenerate clear lamp if difficulty changed
            if (stateChanged && barInfo.SongNode?.Type == NodeType.Score)
            {
                barInfo.ClearLamp?.RemoveReference();
                barInfo.ClearLamp = null;
                barInfo.ClearLamp = GenerateClearLampTexture(
                    barInfo.SongNode,
                    newDifficulty,
                    playSpeedPercent);
            }
        }

        /// <summary>
        /// Generate or retrieve cached clear lamp texture
        /// Uses Phase 2 enhanced clear lamp generation
        /// </summary>
        public ITexture GenerateClearLampTexture(SongListNode songNode, int difficulty)
        {
            return GenerateClearLampTexture(
                songNode,
                difficulty,
                PlaySpeedPercent);
        }

        /// <summary>
        /// Generates a clear lamp from one exact speed variant.
        /// </summary>
        public ITexture GenerateClearLampTexture(
            SongListNode songNode,
            int difficulty,
            int playSpeedPercent)
        {
            if (songNode?.Type != NodeType.Score)
                return null;

            var cacheKey = GetClearLampCacheKey(
                songNode,
                difficulty,
                playSpeedPercent);
            if (_clearLampCache.TryGet(cacheKey, out var cachedTexture))
            {
                cachedTexture.AddReference();
                return cachedTexture;
            }

            // Use Phase 2 enhanced clear lamp generation with DefaultGraphicsGenerator
            // Note: _graphicsGenerator must stay alive for the lifetime of cached textures
            if (_graphicsGenerator == null)
                return null;

            var clearStatus = ResolveClearStatus(
                songNode,
                difficulty,
                playSpeedPercent);
            var texture = _graphicsGenerator.GenerateEnhancedClearLamp(difficulty, clearStatus);

            if (texture != null)
            {
                _clearLampCache.Add(cacheKey, texture);
                texture.AddReference(); // Cache holds one reference, caller gets another
                _clearLampCacheOrder.Enqueue(cacheKey);
                EvictGeneratedTextureOverflow(_clearLampCache, _clearLampCacheOrder, MAX_CLEAR_LAMP_CACHE_ENTRIES, generatorOwned: true);
            }

            return texture;
        }

        /// <summary>
        /// Clear all texture caches
        /// </summary>
        public void ClearCache()
        {
            _titleTextureCache.Clear();
            _titleTextureCacheOrder.Clear();
            _previewImageCache.Clear();
            _previewImageCacheOrder.Clear();
            _failedPreviewPaths.Clear();
            ReleaseClearLampCache();
        }

        /// <summary>
        /// Release all clear-lamp cache entries without disposing the
        /// textures. Lamp textures are generated and owned by
        /// _graphicsGenerator — shared per (difficulty, clear status) rather
        /// than per key — so CacheManager.Clear, which disposes values,
        /// would leave the generator holding and re-handing disposed
        /// entries. Each key holds one counted reference; releasing it here
        /// keeps the accounting symmetric with the per-key AddReference.
        /// </summary>
        private void ReleaseClearLampCache()
        {
            while (_clearLampCacheOrder.Count > 0)
            {
                var key = _clearLampCacheOrder.Dequeue();
                if (_clearLampCache.TryRemove(key, out var texture))
                    texture?.RemoveReference();
            }
        }        /// <summary>
        /// Priority texture generation method for immediate processing of selected items
        /// Generates texture immediately with performance metrics
        /// </summary>
        public SongBarInfo GenerateBarInfoWithPriority(SongListNode songNode, int difficulty, bool isSelected = false)
        {
            return GenerateBarInfoWithPriority(
                songNode,
                difficulty,
                PlaySpeedPercent,
                isSelected);
        }

        /// <summary>
        /// Generates priority bar information for one exact speed profile.
        /// </summary>
        public SongBarInfo GenerateBarInfoWithPriority(
            SongListNode songNode,
            int difficulty,
            int playSpeedPercent,
            bool isSelected = false)
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
                PlaySpeedPercent = playSpeedPercent,
                IsSelected = isSelected
            };

            // Generate textures immediately (safety checks removed)
            barInfo.TitleTexture = GenerateTitleTexture(songNode);
            
            // Only generate preview image for selected items or if not in fast scroll mode
            if (isSelected || !_isFastScrollMode)
            {
                barInfo.PreviewImage = GeneratePreviewImageTexture(songNode);
            }
            
            barInfo.ClearLamp = GenerateClearLampTexture(
                songNode,
                difficulty,
                playSpeedPercent);

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
            _titleTextureCacheOrder.Clear();
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
                return LoadPreviewImage(resolvedPreviewImagePath, out _);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load preview image: {ex.Message}");
            }

            return null;
        }

        private ITexture LoadPreviewImage(string resolvedPreviewImagePath, out bool cacheableFailure)
        {
            return LoadResolvedPreviewImage(_resourceManager, resolvedPreviewImagePath, out cacheableFailure);
        }

        /// <summary>
        /// Resolve a node's preview image to an absolute path, or null when the
        /// chart carries no preview reference. Shared with fallback draw paths
        /// that have no SongBarRenderer instance.
        /// </summary>
        internal static string GetResolvedPreviewImagePath(SongListNode songNode)
        {
            var chart = songNode?.DatabaseChart;
            var filePath = chart?.FilePath;
            var previewImage = chart?.PreviewImage;

            if (string.IsNullOrEmpty(filePath) || string.IsNullOrEmpty(previewImage))
                return null;

            var songDirectory = Path.GetDirectoryName(filePath);
            if (string.IsNullOrEmpty(songDirectory))
                return null;

            try
            {
                // A #PREIMAGE value with platform-invalid characters throws here;
                // callers fall back to the skin default instead of failing the draw.
                return Path.GetFullPath(Path.Combine(songDirectory, previewImage));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Invalid preview image path '{previewImage}': {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Load a resolved preview image through a resource manager without a
        /// renderer instance, applying the same existence and size guards as the
        /// enhanced path. Returns null when the manager reports the load as
        /// failed (LoadTexture returns a 1x1 white fallback rather than null),
        /// so callers fall back to the skin default instead of caching or
        /// drawing the placeholder. The caller owns the returned reference.
        /// <paramref name="cacheableFailure"/> is set only once the load is
        /// genuinely attempted — a failure the caller may negative-cache.
        /// Guard rejections (missing file, size cap, a race between Exists and
        /// stat) leave it false so the path stays retryable.
        /// </summary>
        internal static ITexture LoadResolvedPreviewImage(IResourceManager resourceManager, string resolvedPreviewImagePath, out bool cacheableFailure)
        {
            cacheableFailure = false;
            if (resourceManager == null || string.IsNullOrEmpty(resolvedPreviewImagePath) || !File.Exists(resolvedPreviewImagePath))
                return null;

            try
            {
                if (new FileInfo(resolvedPreviewImagePath).Length > MAX_PREVIEW_IMAGE_SIZE_BYTES)
                {
                    System.Diagnostics.Debug.WriteLine($"Skipping large preview image: {resolvedPreviewImagePath}");
                    return null;
                }

                // Decoded-size guard: reject headers declaring a pixel buffer
                // over the decoded budget before the decode runs. Like the
                // other guard rejections this stays retryable; files whose
                // format or dimensions cannot be read pass through to the
                // decoder, which rejects them as before.
                if (TryReadImageDimensions(resolvedPreviewImagePath, out var imageWidth, out var imageHeight)
                    && (long)imageWidth * imageHeight > MAX_PREVIEW_IMAGE_DECODED_BYTES / 4)
                {
                    System.Diagnostics.Debug.WriteLine($"Skipping oversized decoded preview image ({imageWidth}x{imageHeight}): {resolvedPreviewImagePath}");
                    return null;
                }

                // From here the load is genuinely attempted, so any failure is
                // safe to negative-cache.
                cacheableFailure = true;

                // TryLoadTexture reports the failure LoadTexture hides behind
                // its 1x1 fallback. The fallback is uncached and caller-owned,
                // so dispose it here rather than handing the placeholder out
                // as if it were a real jacket.
                if (!resourceManager.TryLoadTexture(resolvedPreviewImagePath, out var texture))
                {
                    texture?.Dispose();
                    return null;
                }

                return texture;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load preview image: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Read a file's declared pixel dimensions from its header without
        /// decoding it, covering the formats Texture2D.FromStream accepts for
        /// jacket art (PNG, GIF, BMP, JPEG). Returns false for unrecognized or
        /// unparseable files so the decoder can decide.
        /// </summary>
        private static bool TryReadImageDimensions(string path, out int width, out int height)
        {
            width = 0;
            height = 0;
            try
            {
                using var stream = File.OpenRead(path);
                var header = new byte[32];
                var read = stream.Read(header, 0, header.Length);
                if (read < 10)
                    return false;

                // PNG: 8-byte signature, then the first chunk must be IHDR —
                // length (4B BE), type (4B), width/height (4B BE each).
                if (read >= 24
                    && header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47
                    && header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A
                    && header[12] == 'I' && header[13] == 'H' && header[14] == 'D' && header[15] == 'R')
                {
                    width = ReadBigEndianInt32(header, 16);
                    height = ReadBigEndianInt32(header, 20);
                    return width > 0 && height > 0;
                }

                // GIF: "GIF87a"/"GIF89a", logical screen width/height as
                // little-endian uint16 at offsets 6 and 8.
                if (read >= 10
                    && header[0] == 'G' && header[1] == 'I' && header[2] == 'F'
                    && header[3] == '8' && (header[4] == '7' || header[4] == '9') && header[5] == 'a')
                {
                    width = header[6] | (header[7] << 8);
                    height = header[8] | (header[9] << 8);
                    return width > 0 && height > 0;
                }

                // BMP: "BM", width/height as little-endian int32 at 18/22
                // inside the BITMAPINFOHEADER; a negative height marks a
                // top-down row order.
                if (read >= 26 && header[0] == 'B' && header[1] == 'M')
                {
                    width = ReadLittleEndianInt32(header, 18);
                    var rawHeight = ReadLittleEndianInt32(header, 22);
                    height = rawHeight == int.MinValue ? 0 : Math.Abs(rawHeight);
                    return width > 0 && height > 0;
                }

                // JPEG: SOI then length-prefixed marker segments; the frame
                // dimensions live in the first SOF segment.
                if (header[0] == 0xFF && header[1] == 0xD8)
                    return TryReadJpegDimensions(stream, out width, out height);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Could not read preview image header '{path}': {ex.Message}");
            }
            return false;
        }

        /// <summary>
        /// Walk JPEG marker segments until the first SOF segment and return
        /// its frame dimensions. Each segment is skipped by its declared
        /// length, so payload bytes are never mistaken for markers; SOF
        /// always precedes the entropy-coded scan data.
        /// </summary>
        private static bool TryReadJpegDimensions(Stream stream, out int width, out int height)
        {
            width = 0;
            height = 0;
            stream.Seek(2, SeekOrigin.Begin); // skip the SOI marker
            while (stream.Position < stream.Length)
            {
                // A segment boundary must start with a marker prefix; anything
                // else means the stream is not well-formed JPEG.
                if (stream.ReadByte() != 0xFF)
                    return false;
                int marker;
                do
                {
                    marker = stream.ReadByte(); // skip marker fill bytes
                } while (marker == 0xFF);
                if (marker <= 0)
                    return false;
                // Standalone markers (SOI, EOI, TEM, RST0-7) carry no payload.
                if (marker == 0xD8 || marker == 0xD9 || marker == 0x01
                    || (marker >= 0xD0 && marker <= 0xD7))
                    continue;

                var lengthHi = stream.ReadByte();
                var lengthLo = stream.ReadByte();
                if (lengthHi < 0 || lengthLo < 0)
                    return false;
                var segmentLength = (lengthHi << 8) | lengthLo; // includes its own 2 bytes
                if (segmentLength < 2)
                    return false;

                // SOF markers are C0-CF except DHT (C4), reserved JPG (C8),
                // and DAC (CC).
                if (marker >= 0xC0 && marker <= 0xCF
                    && marker != 0xC4 && marker != 0xC8 && marker != 0xCC)
                {
                    if (segmentLength < 7)
                        return false;
                    stream.ReadByte(); // sample precision
                    var hHi = stream.ReadByte();
                    var hLo = stream.ReadByte();
                    var wHi = stream.ReadByte();
                    var wLo = stream.ReadByte();
                    if ((hHi | hLo | wHi | wLo) < 0)
                        return false;
                    height = (hHi << 8) | hLo;
                    width = (wHi << 8) | wLo;
                    return width > 0 && height > 0;
                }

                stream.Seek(segmentLength - 2, SeekOrigin.Current);
            }
            return false;
        }

        private static int ReadBigEndianInt32(byte[] bytes, int offset) =>
            (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];

        private static int ReadLittleEndianInt32(byte[] bytes, int offset) =>
            bytes[offset] | (bytes[offset + 1] << 8) | (bytes[offset + 2] << 16) | (bytes[offset + 3] << 24);

        private string GetTitleCacheKey(SongListNode songNode)
        {
            return $"{songNode.Type}_{songNode.DisplayTitle}_{songNode.GetHashCode()}";
        }

        private string GetClearLampCacheKey(
            SongListNode songNode,
            int difficulty,
            int playSpeedPercent)
        {
            var clearStatus = ResolveClearStatus(
                songNode,
                difficulty,
                playSpeedPercent);
            return $"{songNode.GetHashCode()}_{difficulty}_{playSpeedPercent}_{clearStatus}";
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

        internal static ClearStatus ResolveClearStatus(
            SongListNode songNode,
            int difficulty,
            int playSpeedPercent)
        {
            if (songNode == null)
                return ClearStatus.NotPlayed;

            var score = playSpeedPercent == PlaySpeedRange.Default
                ? songNode.GetScore(difficulty)
                : songNode.GetScore(difficulty, playSpeedPercent);
            if (score == null || score.PlayCount <= 0)
                return ClearStatus.NotPlayed;

            if (score.FullCombo)
                return ClearStatus.FullCombo;

            return SongScore.ComputeRankIndex(
                    SongScore.NormalizeStoredBestRank(score.BestRank)) <= 2
                ? ClearStatus.Clear
                : ClearStatus.Failed;
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

        /// <summary>
        /// Evict the oldest generated textures (titles, clear lamps) while the
        /// cache exceeds its bound. Renderer-owned entries still referenced
        /// by a cached SongBarInfo are re-queued — the held set is itself
        /// bounded by SongListDisplay's bar-info cache, so the cache cannot
        /// grow without limit.
        /// <paramref name="generatorOwned"/> distinguishes ownership:
        /// clear-lamp textures come from _graphicsGenerator, which keeps its
        /// own reference in _generatedTextures and re-hands them across cache
        /// keys — disposing one here would leave the generator returning a
        /// disposed texture to the next caller, so eviction only drops this
        /// key's counted reference. Lamp textures are shared per
        /// (difficulty, status), so the texture's global ReferenceCount is
        /// no signal for whether this key's entry is still needed: one live
        /// bar would otherwise pin every key mapping to the same lamp. Those
        /// keys are therefore evicted unconditionally — the generator
        /// re-hands the texture on the next lookup at no GPU cost.
        /// Renderer-owned textures (titles) are queued for Dispose on the
        /// next update rather than freed inline: eviction runs inside the
        /// deferred SpriteBatch during draw, and a texture at refcount zero
        /// may still be queued for drawing.
        /// </summary>
        private void EvictGeneratedTextureOverflow(
            CacheManager<string, ITexture> cache,
            Queue<string> order,
            int maxEntries,
            bool generatorOwned)
        {
            // Bound the scan so held entries cannot keep the loop re-queueing forever.
            var attempts = order.Count;
            while (cache.Count > maxEntries && attempts-- > 0 && order.Count > 0)
            {
                var oldestKey = order.Dequeue();
                if (!cache.TryRemove(oldestKey, out var texture))
                    continue; // stale queue slot — entry already removed

                if (!generatorOwned && texture.ReferenceCount > 0)
                {
                    cache.Add(oldestKey, texture);
                    order.Enqueue(oldestKey);
                    continue;
                }

                if (generatorOwned)
                {
                    texture.RemoveReference();
                }
                else
                {
                    _pendingDispose.Enqueue(texture);
                }
            }
        }

        /// <summary>
        /// Evict the oldest preview images while the cache exceeds its bound.
        /// Preview textures come from the ResourceManager (TryLoadTexture) and
        /// the cache holds a counted reference, so eviction drops only that
        /// reference — a SongBarInfo still holding the jacket keeps it alive.
        /// CacheManager.Remove is not used here: it disposes the value
        /// outright and would break bars still drawing the texture.
        /// Reclamation is deferred to FlushDeferredReclamation: during draw
        /// the evicted texture may already be queued in the deferred
        /// SpriteBatch, so CollectUnusedResources must run after the batch
        /// has flushed, not inside the draw pass.
        /// </summary>
        private void EvictPreviewImageOverflow()
        {
            while (_previewImageCache.Count > MAX_PREVIEW_IMAGE_CACHE_ENTRIES
                && _previewImageCacheOrder.Count > 0)
            {
                var oldestPath = _previewImageCacheOrder.Dequeue();
                if (_previewImageCache.TryRemove(oldestPath, out var evictedTexture))
                {
                    evictedTexture?.RemoveReference();
                    _reclamationPending = true;
                }
            }
        }

        /// <summary>
        /// Perform GPU reclamation deferred by draw-pass evictions. Called
        /// once per frame from SongListDisplay.OnUpdate — after the previous
        /// deferred SpriteBatch has flushed — and from Dispose. Disposes
        /// evicted renderer-owned textures and collects ResourceManager
        /// entries whose references were dropped since the last call.
        /// </summary>
        internal void FlushDeferredReclamation()
        {
            while (_pendingDispose.Count > 0)
            {
                _pendingDispose.Dequeue()?.Dispose();
            }

            if (_reclamationPending)
            {
                _reclamationPending = false;
                _resourceManager.CollectUnusedResources();
            }
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (!_disposed)
            {
                FlushDeferredReclamation();
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
            public int PlaySpeedPercent { get; set; } = PlaySpeedRange.Default;
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
