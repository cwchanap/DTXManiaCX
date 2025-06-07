using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using DTX.Resources;
using DTX.UI.Components;
using System;
using System.Collections.Generic;

namespace DTX.UI
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
        private SpriteBatch _spriteBatch;
        private Texture2D _whitePixel;
        private bool _disposed;

        #endregion

        #region Constructor

        public DefaultGraphicsGenerator(GraphicsDevice graphicsDevice)
        {
            _graphicsDevice = graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
            _generatedTextures = new Dictionary<string, ITexture>();
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
            var cacheKey = $"Panel_{width}x{height}_{withBorder}";
            
            if (_generatedTextures.TryGetValue(cacheKey, out var cached))
                return cached;

            var texture = CreatePanelTexture(width, height, withBorder);
            _generatedTextures[cacheKey] = texture;
            return texture;
        }

        /// <summary>
        /// Generate default button texture
        /// </summary>
        public ITexture GenerateButton(int width, int height, bool isPressed = false)
        {
            var cacheKey = $"Button_{width}x{height}_{isPressed}";
            
            if (_generatedTextures.TryGetValue(cacheKey, out var cached))
                return cached;

            var texture = CreateButtonTexture(width, height, isPressed);
            _generatedTextures[cacheKey] = texture;
            return texture;
        }

        #endregion

        #region Private Methods

        private ITexture CreateSongBarTexture(int width, int height, bool isSelected, bool isCenter)
        {
            var renderTarget = new RenderTarget2D(_graphicsDevice, width, height);
            
            _graphicsDevice.SetRenderTarget(renderTarget);
            _graphicsDevice.Clear(Color.Transparent);

            _spriteBatch.Begin();

            // Base color
            var baseColor = DTXManiaVisualTheme.SongSelection.SongBarBackground;
            if (isCenter)
                baseColor = DTXManiaVisualTheme.SongSelection.SongBarCenter;
            else if (isSelected)
                baseColor = DTXManiaVisualTheme.SongSelection.SongBarSelected;

            // Draw background with gradient effect
            DrawGradientRectangle(_spriteBatch, new Rectangle(0, 0, width, height), 
                                baseColor, Color.Lerp(baseColor, Color.Black, 0.3f));

            // Draw border for selected items
            if (isSelected || isCenter)
            {
                var borderColor = isCenter ? Color.Yellow : Color.White;
                var borderThickness = isCenter ? 2 : 1;
                
                // Top border
                _spriteBatch.Draw(_whitePixel, new Rectangle(0, 0, width, borderThickness), borderColor);
                // Bottom border
                _spriteBatch.Draw(_whitePixel, new Rectangle(0, height - borderThickness, width, borderThickness), borderColor);
            }

            _spriteBatch.End();
            _graphicsDevice.SetRenderTarget(null);

            return new ManagedTexture(_graphicsDevice, renderTarget, $"Generated_SongBar_{width}x{height}");
        }

        private ITexture CreateClearLampTexture(int difficulty, bool hasCleared)
        {
            var width = DTXManiaVisualTheme.Layout.ClearLampWidth;
            var height = DTXManiaVisualTheme.Layout.ClearLampHeight;
            var renderTarget = new RenderTarget2D(_graphicsDevice, width, height);
            
            _graphicsDevice.SetRenderTarget(renderTarget);
            _graphicsDevice.Clear(Color.Transparent);

            _spriteBatch.Begin();

            var lampColor = DTXManiaVisualTheme.GetDifficultyColor(difficulty);
            if (!hasCleared)
                lampColor = Color.Lerp(lampColor, Color.Gray, 0.7f);

            // Draw lamp with gradient effect
            DrawGradientRectangle(_spriteBatch, new Rectangle(0, 0, width, height),
                                lampColor, Color.Lerp(lampColor, Color.Black, 0.5f));

            // Draw border
            var borderColor = hasCleared ? Color.White : Color.Gray;
            DrawRectangleBorder(_spriteBatch, new Rectangle(0, 0, width, height), borderColor, 1);

            _spriteBatch.End();
            _graphicsDevice.SetRenderTarget(null);

            return new ManagedTexture(_graphicsDevice, renderTarget, $"Generated_ClearLamp_{difficulty}_{hasCleared}");
        }

        private ITexture CreatePanelTexture(int width, int height, bool withBorder)
        {
            var renderTarget = new RenderTarget2D(_graphicsDevice, width, height);
            
            _graphicsDevice.SetRenderTarget(renderTarget);
            _graphicsDevice.Clear(Color.Transparent);

            _spriteBatch.Begin();

            // Draw background
            var bgColor = DTXManiaVisualTheme.SongSelection.PanelBackground;
            _spriteBatch.Draw(_whitePixel, new Rectangle(0, 0, width, height), bgColor);

            // Draw border if requested
            if (withBorder)
            {
                var borderColor = DTXManiaVisualTheme.SongSelection.PanelBorder;
                DrawRectangleBorder(_spriteBatch, new Rectangle(0, 0, width, height), borderColor, 2);
            }

            _spriteBatch.End();
            _graphicsDevice.SetRenderTarget(null);

            return new ManagedTexture(_graphicsDevice, renderTarget, $"Generated_Panel_{width}x{height}");
        }

        private ITexture CreateButtonTexture(int width, int height, bool isPressed)
        {
            var renderTarget = new RenderTarget2D(_graphicsDevice, width, height);
            
            _graphicsDevice.SetRenderTarget(renderTarget);
            _graphicsDevice.Clear(Color.Transparent);

            _spriteBatch.Begin();

            var baseColor = new Color(60, 80, 120);
            if (isPressed)
                baseColor = Color.Lerp(baseColor, Color.White, 0.3f);

            // Draw button with gradient effect
            DrawGradientRectangle(_spriteBatch, new Rectangle(0, 0, width, height),
                                Color.Lerp(baseColor, Color.White, 0.2f), baseColor);

            // Draw border
            DrawRectangleBorder(_spriteBatch, new Rectangle(0, 0, width, height), Color.White, 1);

            _spriteBatch.End();
            _graphicsDevice.SetRenderTarget(null);

            return new ManagedTexture(_graphicsDevice, renderTarget, $"Generated_Button_{width}x{height}");
        }

        private void DrawGradientRectangle(SpriteBatch spriteBatch, Rectangle bounds, Color topColor, Color bottomColor)
        {
            // Simple vertical gradient using multiple horizontal lines
            for (int y = 0; y < bounds.Height; y++)
            {
                float ratio = (float)y / bounds.Height;
                var color = Color.Lerp(topColor, bottomColor, ratio);
                var lineRect = new Rectangle(bounds.X, bounds.Y + y, bounds.Width, 1);
                spriteBatch.Draw(_whitePixel, lineRect, color);
            }
        }

        private void DrawRectangleBorder(SpriteBatch spriteBatch, Rectangle bounds, Color color, int thickness)
        {
            // Top
            spriteBatch.Draw(_whitePixel, new Rectangle(bounds.X, bounds.Y, bounds.Width, thickness), color);
            // Bottom
            spriteBatch.Draw(_whitePixel, new Rectangle(bounds.X, bounds.Bottom - thickness, bounds.Width, thickness), color);
            // Left
            spriteBatch.Draw(_whitePixel, new Rectangle(bounds.X, bounds.Y, thickness, bounds.Height), color);
            // Right
            spriteBatch.Draw(_whitePixel, new Rectangle(bounds.Right - thickness, bounds.Y, thickness, bounds.Height), color);
        }

        private ITexture CreateEnhancedClearLampTexture(int difficulty, ClearStatus clearStatus)
        {
            var renderTarget = new RenderTarget2D(_graphicsDevice, 8, 24);

            _graphicsDevice.SetRenderTarget(renderTarget);
            _graphicsDevice.Clear(Color.Transparent);

            _spriteBatch.Begin();

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
            DrawGradientRectangle(_spriteBatch, new Rectangle(0, 0, 8, 24),
                                Color.Lerp(lampColor, Color.White, 0.3f), lampColor);

            // Add border for better definition
            if (clearStatus != ClearStatus.NotPlayed)
            {
                DrawRectangleBorder(_spriteBatch, new Rectangle(0, 0, 8, 24), Color.White * 0.8f, 1);
            }

            _spriteBatch.End();
            _graphicsDevice.SetRenderTarget(null);

            return new ManagedTexture(_graphicsDevice, renderTarget, $"Generated_EnhancedClearLamp_{difficulty}_{clearStatus}");
        }

        private ITexture CreateBarTypeTexture(int width, int height, BarType barType, bool isSelected, bool isCenter)
        {
            var renderTarget = new RenderTarget2D(_graphicsDevice, width, height);

            _graphicsDevice.SetRenderTarget(renderTarget);
            _graphicsDevice.Clear(Color.Transparent);

            _spriteBatch.Begin();

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
            DrawGradientRectangle(_spriteBatch, new Rectangle(0, 0, width, height),
                                baseColor, Color.Lerp(baseColor, Color.Black, 0.3f));

            // Draw border for selected items
            if (isSelected || isCenter)
            {
                var borderColor = isCenter ? Color.Yellow : Color.White;
                var borderThickness = isCenter ? 2 : 1;
                DrawRectangleBorder(_spriteBatch, new Rectangle(0, 0, width, height), borderColor, borderThickness);
            }

            // Add special effects for different bar types
            if (barType == BarType.Box)
            {
                // Add folder icon indicator (simple rectangle on left side)
                var iconRect = new Rectangle(2, height / 4, 4, height / 2);
                _spriteBatch.Draw(_whitePixel, iconRect, Color.Cyan * 0.7f);
            }
            else if (barType == BarType.Other)
            {
                // Add special indicator (diamond pattern)
                var centerY = height / 2;
                var indicatorRect = new Rectangle(width - 8, centerY - 2, 4, 4);
                _spriteBatch.Draw(_whitePixel, indicatorRect, Color.Magenta * 0.8f);
            }

            _spriteBatch.End();
            _graphicsDevice.SetRenderTarget(null);

            return new ManagedTexture(_graphicsDevice, renderTarget, $"Generated_BarType_{barType}_{width}x{height}");
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
