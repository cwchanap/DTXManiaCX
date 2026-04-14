using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song.Components;
using System;
using System.Collections.Generic;

namespace DTXMania.Game.Lib.UI
{
    /// <summary>
    /// Generates default graphics when skin textures are missing
    /// Creates DTXManiaNX-style fallback graphics programmatically
    /// </summary>
    public class DefaultGraphicsGenerator : IDisposable
    {
        #region Private Fields

        private readonly GraphicsDevice _graphicsDevice;
        private readonly Dictionary<string, ITexture> _generatedTextures;
        private readonly RenderTarget2D _renderTarget;
        private SpriteBatch _spriteBatch;
        private Texture2D _whitePixel;
        private bool _disposed;

        #endregion

        #region Constructor

        public DefaultGraphicsGenerator(GraphicsDevice graphicsDevice, RenderTarget2D renderTarget)
        {
            _graphicsDevice = graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
            _generatedTextures = new Dictionary<string, ITexture>();
            _renderTarget = renderTarget ?? throw new ArgumentNullException(nameof(renderTarget));
            _spriteBatch = new SpriteBatch(_graphicsDevice);

            // Create white pixel texture for drawing primitives
            _whitePixel = new Texture2D(_graphicsDevice, 1, 1);
            _whitePixel.SetData(new[] { Color.White });
        }

        #endregion

        #region Public Properties

        /// <summary>
        /// Graphics device used for rendering
        /// </summary>
        public GraphicsDevice GraphicsDevice => _graphicsDevice;

        #endregion

        #region Public Methods

        /// <summary>
        /// Generate default song bar background texture
        /// </summary>
        public ITexture GenerateSongBarBackground(int width, int height, bool isSelected = false, bool isCenter = false)
        {
            ValidateDimensions(width, height);

            var cacheKey = $"SongBar_{width}x{height}_{isSelected}_{isCenter}";

            if (_generatedTextures.TryGetValue(cacheKey, out var cached))
                return cached;

            var texture = CreateSongBarTexture(width, height, isSelected, isCenter);
            _generatedTextures[cacheKey] = texture;
            return texture;
        }

        /// <summary>
        /// Generate default clear lamp texture
        /// </summary>
        public ITexture GenerateClearLamp(int difficulty, bool hasCleared = false)
        {
            var cacheKey = $"ClearLamp_{difficulty}_{hasCleared}";

            if (_generatedTextures.TryGetValue(cacheKey, out var cached))
                return cached;

            var texture = CreateClearLampTexture(difficulty, hasCleared);
            _generatedTextures[cacheKey] = texture;
            return texture;
        }

        /// <summary>
        /// Generate enhanced clear lamp texture with clear status (Phase 2)
        /// </summary>
        public ITexture GenerateEnhancedClearLamp(int difficulty, ClearStatus clearStatus)
        {
            var cacheKey = $"EnhancedClearLamp_{difficulty}_{clearStatus}";

            if (_generatedTextures.TryGetValue(cacheKey, out var cached))
                return cached;

            var texture = CreateEnhancedClearLampTexture(difficulty, clearStatus);
            _generatedTextures[cacheKey] = texture;
            return texture;
        }

        /// <summary>
        /// Generate bar type specific background (Phase 2)
        /// </summary>
        public ITexture GenerateBarTypeBackground(int width, int height, BarType barType, bool isSelected = false, bool isCenter = false)
        {
            ValidateDimensions(width, height);

            var cacheKey = $"BarType_{width}x{height}_{barType}_{isSelected}_{isCenter}";

            if (_generatedTextures.TryGetValue(cacheKey, out var cached))
                return cached;

            var texture = CreateBarTypeTexture(width, height, barType, isSelected, isCenter);
            _generatedTextures[cacheKey] = texture;
            return texture;
        }

        /// <summary>
        /// Generate default panel background texture
        /// </summary>
        public ITexture GeneratePanelBackground(int width, int height, bool withBorder = true)
        {
            ValidateDimensions(width, height);

            var cacheKey = $"Panel_{width}x{height}_{withBorder}";

            if (_generatedTextures.TryGetValue(cacheKey, out var cached))
                return cached;

            var texture = CreatePanelTexture(width, height, withBorder);
            _generatedTextures[cacheKey] = texture;
            return texture;
        }

        /// <summary>
        /// Generate BPM background panel with text labels
        /// </summary>
        public ITexture GenerateBPMBackground(int width, int height, bool withLabels = true)
        {
            ValidateDimensions(width, height);

            var cacheKey = $"BPMBackground_{width}x{height}_{withLabels}";

            if (_generatedTextures.TryGetValue(cacheKey, out var cached))
                return cached;

            var texture = CreateBPMBackgroundTexture(width, height, withLabels);
            _generatedTextures[cacheKey] = texture;
            return texture;
        }

        /// <summary>
        /// Generate default button texture
        /// </summary>
        public ITexture GenerateButton(int width, int height, bool isPressed = false)
        {
            ValidateDimensions(width, height);

            var cacheKey = $"Button_{width}x{height}_{isPressed}";

            if (_generatedTextures.TryGetValue(cacheKey, out var cached))
                return cached;

            var texture = CreateButtonTexture(width, height, isPressed);
            _generatedTextures[cacheKey] = texture;
            return texture;
        }

        #endregion

        #region Private Methods

        protected virtual void ClearGraphics(Color color)
        {
            _graphicsDevice.Clear(color);
        }

        /// <summary>
        /// Sets the shared render target as the active render target and returns
        /// the previous render targets for restoration. Call before drawing.
        /// </summary>
        protected virtual RenderTargetBinding[] SetRenderTarget()
        {
            var previousTargets = _graphicsDevice.GetRenderTargets();
            _graphicsDevice.SetRenderTarget(_renderTarget);
            return previousTargets;
        }

        /// <summary>
        /// Restores the previous render targets. Call after drawing and before
        /// CreateGeneratedTexture (which reads from the render target).
        /// </summary>
        protected virtual void RestoreRenderTargets(RenderTargetBinding[] previousTargets)
        {
            _graphicsDevice.SetRenderTargets(previousTargets);
        }

        protected virtual void BeginSpriteBatch()
        {
            _spriteBatch.Begin();
        }

        protected virtual void EndSpriteBatch()
        {
            _spriteBatch.End();
        }

        protected virtual void DrawSolidRectangle(Rectangle destination, Color color)
        {
            _spriteBatch.Draw(_whitePixel, destination, color);
        }

        /// <summary>
        /// Captures a region of the render target into a new immutable Texture2D,
        /// then wraps it in a ManagedTexture for caching. This ensures each cached
        /// texture has its own independent backing texture rather than sharing
        /// the single reusable _renderTarget, and crops to only the generated region
        /// instead of copying the full render target.
        /// </summary>
        protected virtual ITexture CreateGeneratedTexture(string sourcePath, int width, int height)
        {
            // Copy only the generated region from the render target into a cropped Texture2D
            var region = new Rectangle(0, 0, width, height);
            var texture = new Texture2D(_graphicsDevice, width, height);
            var data = new Color[width * height];
            _renderTarget.GetData(0, region, data, 0, data.Length);
            texture.SetData(data);
            return new ManagedTexture(_graphicsDevice, texture, sourcePath);
        }

        private static void ValidateDimensions(int width, int height)
        {
            if (width <= 0)
                throw new ArgumentOutOfRangeException(nameof(width));

            if (height <= 0)
                throw new ArgumentOutOfRangeException(nameof(height));
        }

        private ITexture CreateSongBarTexture(int width, int height, bool isSelected, bool isCenter)
        {
            var previousTargets = SetRenderTarget();
            try
            {
                ClearGraphics(Color.Transparent);
                BeginSpriteBatch();

                // Base color
                var baseColor = DTXManiaVisualTheme.SongSelection.SongBarBackground;
                if (isCenter)
                    baseColor = DTXManiaVisualTheme.SongSelection.SongBarCenter;
                else if (isSelected)
                    baseColor = DTXManiaVisualTheme.SongSelection.SongBarSelected;

                // Draw background with gradient effect
                DrawGradientRectangle(new Rectangle(0, 0, width, height),
                                    baseColor, Color.Lerp(baseColor, Color.Black, 0.3f));

                // Draw border for selected items
                if (isSelected || isCenter)
                {
                    var borderColor = isCenter ? Color.Yellow : Color.White;
                    var borderThickness = isCenter ? 2 : 1;

                    // Top border
                    DrawSolidRectangle(new Rectangle(0, 0, width, borderThickness), borderColor);
                    // Bottom border
                    DrawSolidRectangle(new Rectangle(0, height - borderThickness, width, borderThickness), borderColor);
                }

                EndSpriteBatch();
            }
            finally
            {
                RestoreRenderTargets(previousTargets);
            }

            return CreateGeneratedTexture($"Generated_SongBar_{width}x{height}", width, height);
        }

        private ITexture CreateClearLampTexture(int difficulty, bool hasCleared)
        {
            var width = DTXManiaVisualTheme.Layout.ClearLampWidth;
            var height = DTXManiaVisualTheme.Layout.ClearLampHeight;

            var previousTargets = SetRenderTarget();
            try
            {
                ClearGraphics(Color.Transparent);
                BeginSpriteBatch();

                var lampColor = DTXManiaVisualTheme.GetDifficultyColor(difficulty);
                if (!hasCleared)
                    lampColor = Color.Lerp(lampColor, Color.Gray, 0.7f);

                // Draw lamp with gradient effect
                DrawGradientRectangle(new Rectangle(0, 0, width, height),
                                    lampColor, Color.Lerp(lampColor, Color.Black, 0.5f));

                // Draw border
                var borderColor = hasCleared ? Color.White : Color.Gray;
                DrawRectangleBorder(new Rectangle(0, 0, width, height), borderColor, 1);

                EndSpriteBatch();
            }
            finally
            {
                RestoreRenderTargets(previousTargets);
            }

            return CreateGeneratedTexture($"Generated_ClearLamp_{difficulty}_{hasCleared}", width, height);
        }

        private ITexture CreatePanelTexture(int width, int height, bool withBorder)
        {
            var previousTargets = SetRenderTarget();
            try
            {
                ClearGraphics(Color.Transparent);
                BeginSpriteBatch();

                // Draw background
                var bgColor = DTXManiaVisualTheme.SongSelection.PanelBackground;
                DrawSolidRectangle(new Rectangle(0, 0, width, height), bgColor);

                // Draw border if requested
                if (withBorder)
                {
                    var borderColor = DTXManiaVisualTheme.SongSelection.PanelBorder;
                    DrawRectangleBorder(new Rectangle(0, 0, width, height), borderColor, 2);
                }

                EndSpriteBatch();
            }
            finally
            {
                RestoreRenderTargets(previousTargets);
            }

            return CreateGeneratedTexture($"Generated_Panel_{width}x{height}", width, height);
        }

        private ITexture CreateButtonTexture(int width, int height, bool isPressed)
        {
            var previousTargets = SetRenderTarget();
            try
            {
                ClearGraphics(Color.Transparent);
                BeginSpriteBatch();

                var baseColor = new Color(60, 80, 120);
                if (isPressed)
                    baseColor = Color.Lerp(baseColor, Color.White, 0.3f);

                // Draw button with gradient effect
                DrawGradientRectangle(new Rectangle(0, 0, width, height),
                                    Color.Lerp(baseColor, Color.White, 0.2f), baseColor);

                // Draw border
                DrawRectangleBorder(new Rectangle(0, 0, width, height), Color.White, 1);

                EndSpriteBatch();
            }
            finally
            {
                RestoreRenderTargets(previousTargets);
            }

            return CreateGeneratedTexture($"Generated_Button_{width}x{height}", width, height);
        }

        private void DrawGradientRectangle(Rectangle bounds, Color topColor, Color bottomColor)
        {
            // Simple vertical gradient using multiple horizontal lines
            for (int y = 0; y < bounds.Height; y++)
            {
                float ratio = (float)y / bounds.Height;
                var color = Color.Lerp(topColor, bottomColor, ratio);
                var lineRect = new Rectangle(bounds.X, bounds.Y + y, bounds.Width, 1);
                DrawSolidRectangle(lineRect, color);
            }
        }

        private void DrawRectangleBorder(Rectangle bounds, Color color, int thickness)
        {
            // Top
            DrawSolidRectangle(new Rectangle(bounds.X, bounds.Y, bounds.Width, thickness), color);
            // Bottom
            DrawSolidRectangle(new Rectangle(bounds.X, bounds.Bottom - thickness, bounds.Width, thickness), color);
            // Left
            DrawSolidRectangle(new Rectangle(bounds.X, bounds.Y, thickness, bounds.Height), color);
            // Right
            DrawSolidRectangle(new Rectangle(bounds.Right - thickness, bounds.Y, thickness, bounds.Height), color);
        }

        private ITexture CreateEnhancedClearLampTexture(int difficulty, ClearStatus clearStatus)
        {
            var previousTargets = SetRenderTarget();
            try
            {
                ClearGraphics(Color.Transparent);
                BeginSpriteBatch();

                // Get base color for difficulty
                var difficultyColors = DTXManiaVisualTheme.SongSelection.DifficultyColors;
                var baseColor = difficulty < difficultyColors.Length ? difficultyColors[difficulty] : Color.Gray;

                // Modify color based on clear status
                Color lampColor = clearStatus switch
                {
                    ClearStatus.FullCombo => Color.Gold,
                    ClearStatus.Clear => baseColor,
                    ClearStatus.Failed => baseColor * 0.5f,
                    ClearStatus.NotPlayed => Color.Gray * 0.3f,
                    _ => Color.Gray * 0.3f
                };

                // Draw lamp with gradient effect
                DrawGradientRectangle(new Rectangle(0, 0, 8, 24),
                                    Color.Lerp(lampColor, Color.White, 0.3f), lampColor);

                // Add border for better definition
                if (clearStatus != ClearStatus.NotPlayed)
                {
                    DrawRectangleBorder(new Rectangle(0, 0, 8, 24), Color.White * 0.8f, 1);
                }

                EndSpriteBatch();
            }
            finally
            {
                RestoreRenderTargets(previousTargets);
            }

            return CreateGeneratedTexture($"Generated_EnhancedClearLamp_{difficulty}_{clearStatus}", 8, 24);
        }

        private ITexture CreateBarTypeTexture(int width, int height, BarType barType, bool isSelected, bool isCenter)
        {
            var previousTargets = SetRenderTarget();
            try
            {
                ClearGraphics(Color.Transparent);
                BeginSpriteBatch();

                // Get base color based on bar type
                Color baseColor = barType switch
                {
                    BarType.Score => DTXManiaVisualTheme.SongSelection.SongBarBackground,
                    BarType.Box => DTXManiaVisualTheme.SongSelection.FolderBackground,
                    BarType.Other => DTXManiaVisualTheme.SongSelection.SpecialBackground,
                    _ => DTXManiaVisualTheme.SongSelection.SongBarBackground
                };

                // Apply selection highlighting
                if (isCenter)
                    baseColor = DTXManiaVisualTheme.SongSelection.SongBarCenter;
                else if (isSelected)
                    baseColor = DTXManiaVisualTheme.SongSelection.SongBarSelected;

                // Draw background with gradient effect
                DrawGradientRectangle(new Rectangle(0, 0, width, height),
                                    baseColor, Color.Lerp(baseColor, Color.Black, 0.3f));

                // Draw border for selected items
                if (isSelected || isCenter)
                {
                    var borderColor = isCenter ? Color.Yellow : Color.White;
                    var borderThickness = isCenter ? 2 : 1;
                    DrawRectangleBorder(new Rectangle(0, 0, width, height), borderColor, borderThickness);
                }

                // Add special effects for different bar types
                if (barType == BarType.Box)
                {
                    // Add folder icon indicator (simple rectangle on left side)
                    var iconRect = new Rectangle(2, height / 4, 4, height / 2);
                    DrawSolidRectangle(iconRect, Color.Cyan * 0.7f);
                }
                else if (barType == BarType.Other)
                {
                    // Add special indicator (diamond pattern)
                    var centerY = height / 2;
                    var indicatorRect = new Rectangle(width - 8, centerY - 2, 4, 4);
                    DrawSolidRectangle(indicatorRect, Color.Magenta * 0.8f);
                }

                EndSpriteBatch();
            }
            finally
            {
                RestoreRenderTargets(previousTargets);
            }

            return CreateGeneratedTexture($"Generated_BarType_{barType}_{width}x{height}", width, height);
        }

        private ITexture CreateBPMBackgroundTexture(int width, int height, bool withLabels)
        {
            var previousTargets = SetRenderTarget();
            try
            {
                ClearGraphics(Color.Transparent);
                BeginSpriteBatch();

                // Draw background with DTXManiaNX-style panel appearance
                var bgColor = DTXManiaVisualTheme.SongSelection.PanelBackground;
                DrawGradientRectangle(new Rectangle(0, 0, width, height),
                                    Color.Lerp(bgColor, Color.White, 0.1f), bgColor);

                // Draw border
                var borderColor = DTXManiaVisualTheme.SongSelection.PanelBorder;
                DrawRectangleBorder(new Rectangle(0, 0, width, height), borderColor, 2);

                if (withLabels)
                {
                    // Note: We would need a font system to draw text labels
                    // For now, we'll create placeholder areas where text would go
                    
                    // Length label area (top portion)
                    var labelWidth = Math.Max(0, width - 10);
                    var labelHeight = Math.Max(0, height / 2 - 5);
                    var lengthLabelRect = new Rectangle(5, 5, labelWidth, labelHeight);
                    DrawSolidRectangle(lengthLabelRect, Color.Black * 0.2f);
                    
                    // BPM label area (bottom portion)
                    var bpmLabelRect = new Rectangle(5, height / 2, labelWidth, labelHeight);
                    DrawSolidRectangle(bpmLabelRect, Color.Black * 0.2f);
                }

                EndSpriteBatch();
            }
            finally
            {
                RestoreRenderTargets(previousTargets);
            }

            return CreateGeneratedTexture($"Generated_BPMBackground_{width}x{height}", width, height);
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_disposed) return;

            foreach (var texture in _generatedTextures.Values)
            {
                texture?.Dispose();
            }
            _generatedTextures.Clear();

            _spriteBatch?.Dispose();
            _whitePixel?.Dispose();

            _disposed = true;
        }

        #endregion
    }
}
