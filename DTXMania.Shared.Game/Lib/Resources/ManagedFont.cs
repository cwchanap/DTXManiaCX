using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace DTX.Resources
{
    /// <summary>
    /// Managed font implementation with reference counting
    /// Based on DTXMania's CPrivateFont patterns with Japanese support
    /// </summary>
    public class ManagedFont : IFont
    {
        #region Private Fields

        private SpriteFont _spriteFont;
        private readonly string _sourcePath;
        private readonly int _size;
        private readonly FontStyle _style;
        private int _referenceCount;
        private bool _disposed;
        private readonly object _lockObject = new object();
        private char _defaultCharacter = '?';

        // Character support cache for performance
        private readonly HashSet<char> _supportedCharacters = new HashSet<char>();
        private bool _characterCacheBuilt = false;

        #endregion

        #region Constructors

        /// <summary>
        /// Create font from system font name or font file path
        /// </summary>
        public ManagedFont(GraphicsDevice graphicsDevice, string fontPath, int size, FontStyle style = FontStyle.Regular)
        {
            if (graphicsDevice == null)
                throw new ArgumentNullException(nameof(graphicsDevice));
            if (string.IsNullOrEmpty(fontPath))
                throw new ArgumentException("Font path cannot be null or empty", nameof(fontPath));

            _sourcePath = fontPath;
            _size = size;
            _style = style;

            try
            {
                LoadFont(graphicsDevice, fontPath, size, style);
            }
            catch (Exception ex)
            {
                throw new FontLoadException(_sourcePath, $"Failed to load font from {fontPath}", ex);
            }
        }

        /// <summary>
        /// Create font from existing SpriteFont
        /// </summary>
        public ManagedFont(SpriteFont spriteFont, string sourcePath, int size, FontStyle style = FontStyle.Regular)
        {
            _spriteFont = spriteFont ?? throw new ArgumentNullException(nameof(spriteFont));
            _sourcePath = sourcePath ?? "Unknown";
            _size = size;
            _style = style;

            BuildCharacterCache();
        }

        #endregion

        #region IFont Properties

        public SpriteFont SpriteFont => _spriteFont;
        public string SourcePath => _sourcePath;
        public int Size => _size;
        public FontStyle Style => _style;
        public bool IsDisposed => _disposed;
        public int ReferenceCount => _referenceCount;
        public float LineSpacing => _spriteFont?.LineSpacing ?? 0f;

        public char DefaultCharacter
        {
            get => _defaultCharacter;
            set
            {
                _defaultCharacter = value;
                if (_spriteFont != null)
                {
                    _spriteFont.DefaultCharacter = value;
                }
            }
        }

        #endregion

        #region Reference Counting

        public void AddReference()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ManagedFont));

            Interlocked.Increment(ref _referenceCount);
        }

        public void RemoveReference()
        {
            var newCount = Interlocked.Decrement(ref _referenceCount);
            if (newCount <= 0)
            {
                Dispose();
            }
        }

        #endregion

        #region Character Support

        public bool SupportsCharacter(char character)
        {
            if (_disposed || _spriteFont == null)
                return false;

            if (!_characterCacheBuilt)
            {
                BuildCharacterCache();
            }

            return _supportedCharacters.Contains(character);
        }

        #endregion

        #region Text Measurement

        public Vector2 MeasureString(string text)
        {
            if (_disposed || _spriteFont == null || string.IsNullOrEmpty(text))
                return Vector2.Zero;

            try
            {
                return _spriteFont.MeasureString(text);
            }
            catch (ArgumentException)
            {
                // Handle unsupported characters by replacing them
                var sanitizedText = SanitizeText(text);
                return _spriteFont.MeasureString(sanitizedText);
            }
        }

        public Vector2 MeasureStringWrapped(string text, float maxWidth)
        {
            if (_disposed || _spriteFont == null || string.IsNullOrEmpty(text))
                return Vector2.Zero;

            var lines = WrapText(text, maxWidth);
            var totalHeight = lines.Count * LineSpacing;
            var maxLineWidth = lines.Max(line => MeasureString(line).X);

            return new Vector2(maxLineWidth, totalHeight);
        }

        public Rectangle[] GetCharacterBounds(string text)
        {
            if (_disposed || _spriteFont == null || string.IsNullOrEmpty(text))
                return new Rectangle[0];

            var bounds = new List<Rectangle>();
            var position = Vector2.Zero;

            foreach (char c in text)
            {
                if (c == '\n')
                {
                    position.X = 0;
                    position.Y += LineSpacing;
                    bounds.Add(Rectangle.Empty);
                    continue;
                }

                var charSize = MeasureString(c.ToString());
                bounds.Add(new Rectangle((int)position.X, (int)position.Y, (int)charSize.X, (int)charSize.Y));
                position.X += charSize.X;
            }

            return bounds.ToArray();
        }

        #endregion

        #region Text Rendering

        public void DrawString(SpriteBatch spriteBatch, string text, Vector2 position, Color color)
        {
            if (_disposed || _spriteFont == null || string.IsNullOrEmpty(text))
                return;

            try
            {
                spriteBatch.DrawString(_spriteFont, text, position, color);
            }
            catch (ArgumentException)
            {
                // Handle unsupported characters
                var sanitizedText = SanitizeText(text);
                spriteBatch.DrawString(_spriteFont, sanitizedText, position, color);
            }
        }

        public void DrawString(SpriteBatch spriteBatch, string text, Vector2 position, Color color,
                              float rotation, Vector2 origin, Vector2 scale, SpriteEffects effects, float layerDepth)
        {
            if (_disposed || _spriteFont == null || string.IsNullOrEmpty(text))
                return;

            try
            {
                spriteBatch.DrawString(_spriteFont, text, position, color, rotation, origin, scale, effects, layerDepth);
            }
            catch (ArgumentException)
            {
                var sanitizedText = SanitizeText(text);
                spriteBatch.DrawString(_spriteFont, sanitizedText, position, color, rotation, origin, scale, effects, layerDepth);
            }
        }

        public void DrawStringWithOutline(SpriteBatch spriteBatch, string text, Vector2 position,
                                         Color textColor, Color outlineColor, int outlineThickness = 1)
        {
            if (_disposed || _spriteFont == null || string.IsNullOrEmpty(text))
                return;

            // Draw outline by drawing text in 8 directions
            for (int x = -outlineThickness; x <= outlineThickness; x++)
            {
                for (int y = -outlineThickness; y <= outlineThickness; y++)
                {
                    if (x == 0 && y == 0) continue;

                    var outlinePos = position + new Vector2(x, y);
                    DrawString(spriteBatch, text, outlinePos, outlineColor);
                }
            }

            // Draw main text on top
            DrawString(spriteBatch, text, position, textColor);
        }

        public void DrawStringWithGradient(SpriteBatch spriteBatch, string text, Vector2 position,
                                          Color topColor, Color bottomColor)
        {
            if (_disposed || _spriteFont == null || string.IsNullOrEmpty(text))
                return;

            // For now, use a simple implementation - could be enhanced with custom shaders
            var textSize = MeasureString(text);
            var lines = text.Split('\n');
            var currentY = position.Y;

            foreach (var line in lines)
            {
                var lineHeight = LineSpacing;
                var progress = (currentY - position.Y) / textSize.Y;
                var color = Color.Lerp(topColor, bottomColor, progress);

                DrawString(spriteBatch, line, new Vector2(position.X, currentY), color);
                currentY += lineHeight;
            }
        }

        public void DrawStringWithShadow(SpriteBatch spriteBatch, string text, Vector2 position,
                                        Color textColor, Color shadowColor, Vector2 shadowOffset)
        {
            if (_disposed || _spriteFont == null || string.IsNullOrEmpty(text))
                return;

            // Draw shadow first
            DrawString(spriteBatch, text, position + shadowOffset, shadowColor);

            // Draw main text on top
            DrawString(spriteBatch, text, position, textColor);
        }

        public void DrawStringWrapped(SpriteBatch spriteBatch, string text, Rectangle bounds,
                                     Color color, TextAlignment alignment = TextAlignment.Left)
        {
            if (_disposed || _spriteFont == null || string.IsNullOrEmpty(text))
                return;

            var lines = WrapText(text, bounds.Width);
            var currentY = bounds.Y;

            foreach (var line in lines)
            {
                if (currentY + LineSpacing > bounds.Bottom)
                    break;

                var lineSize = MeasureString(line);
                var x = bounds.X;

                switch (alignment)
                {
                    case TextAlignment.Center:
                        x = bounds.X + (int)((bounds.Width - lineSize.X) / 2);
                        break;
                    case TextAlignment.Right:
                        x = bounds.Right - (int)lineSize.X;
                        break;
                }

                DrawString(spriteBatch, line, new Vector2(x, currentY), color);
                currentY += (int)LineSpacing;
            }
        }

        #endregion

        #region Advanced Features

        public ITexture CreateTextTexture(GraphicsDevice graphicsDevice, string text, Color color)
        {
            var options = new TextRenderOptions { TextColor = color };
            return CreateTextTexture(graphicsDevice, text, options);
        }

        public ITexture CreateTextTexture(GraphicsDevice graphicsDevice, string text, TextRenderOptions options)
        {
            if (_disposed || _spriteFont == null || string.IsNullOrEmpty(text))
                return null;

            var textSize = MeasureString(text);
            var width = (int)Math.Ceiling(textSize.X);
            var height = (int)Math.Ceiling(textSize.Y);

            if (width <= 0 || height <= 0)
                return null;

            // Create render target
            var renderTarget = new RenderTarget2D(graphicsDevice, width, height);
            var spriteBatch = new SpriteBatch(graphicsDevice);

            graphicsDevice.SetRenderTarget(renderTarget);
            graphicsDevice.Clear(Color.Transparent);

            spriteBatch.Begin();

            // Apply rendering options
            if (options.EnableShadow)
            {
                DrawStringWithShadow(spriteBatch, text, Vector2.Zero, options.TextColor,
                                   options.ShadowColor, options.ShadowOffset);
            }
            else if (options.EnableOutline)
            {
                DrawStringWithOutline(spriteBatch, text, Vector2.Zero, options.TextColor,
                                    options.OutlineColor, options.OutlineThickness);
            }
            else if (options.EnableGradient)
            {
                DrawStringWithGradient(spriteBatch, text, Vector2.Zero,
                                     options.GradientTopColor, options.GradientBottomColor);
            }
            else
            {
                DrawString(spriteBatch, text, Vector2.Zero, options.TextColor);
            }

            spriteBatch.End();
            graphicsDevice.SetRenderTarget(null);

            spriteBatch.Dispose();

            return new ManagedTexture(graphicsDevice, renderTarget, $"TextTexture_{text.GetHashCode()}");
        }

        public Vector2 GetKerning(char first, char second)
        {
            // MonoGame SpriteFont doesn't expose kerning information directly
            // This is a simplified implementation
            return Vector2.Zero;
        }

        #endregion

        #region Private Methods

        private void LoadFont(GraphicsDevice graphicsDevice, string fontPath, int size, FontStyle style)
        {
            // For now, we'll assume the font is a content pipeline font
            // In a real implementation, you'd need to handle TTF/OTF loading
            // This would require additional libraries like SharpFont or similar

            try
            {
                // Try to load as content pipeline font first
                // This is a placeholder - actual implementation would depend on your content pipeline setup
                throw new NotImplementedException("Font loading from files not yet implemented. Use content pipeline fonts for now.");
            }
            catch
            {
                // Fallback to creating a basic font
                // This is a simplified approach - real implementation would be more complex
                throw new FontLoadException(fontPath, "Font file loading not implemented. Use SpriteFont content pipeline.");
            }
        }

        private void BuildCharacterCache()
        {
            if (_spriteFont == null) return;

            _supportedCharacters.Clear();

            // Build cache of supported characters
            // This is a simplified approach - real implementation would query the font directly
            for (char c = ' '; c <= '~'; c++) // Basic ASCII
            {
                try
                {
                    _spriteFont.MeasureString(c.ToString());
                    _supportedCharacters.Add(c);
                }
                catch
                {
                    // Character not supported
                }
            }

            _characterCacheBuilt = true;
        }

        private string SanitizeText(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            var result = new System.Text.StringBuilder(text.Length);

            foreach (char c in text)
            {
                if (SupportsCharacter(c))
                {
                    result.Append(c);
                }
                else
                {
                    result.Append(_defaultCharacter);
                }
            }

            return result.ToString();
        }

        private List<string> WrapText(string text, float maxWidth)
        {
            var lines = new List<string>();
            var words = text.Split(' ');
            var currentLine = "";

            foreach (var word in words)
            {
                var testLine = string.IsNullOrEmpty(currentLine) ? word : currentLine + " " + word;
                var testSize = MeasureString(testLine);

                if (testSize.X > maxWidth && !string.IsNullOrEmpty(currentLine))
                {
                    lines.Add(currentLine);
                    currentLine = word;
                }
                else
                {
                    currentLine = testLine;
                }
            }

            if (!string.IsNullOrEmpty(currentLine))
            {
                lines.Add(currentLine);
            }

            return lines;
        }

        #endregion

        #region IDisposable Implementation

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                lock (_lockObject)
                {
                    if (!_disposed)
                    {
                        // Note: SpriteFont disposal is handled by content manager
                        // We don't dispose it directly here
                        _disposed = true;
                    }
                }
            }
        }

        ~ManagedFont()
        {
            if (!_disposed)
            {
                Debug.WriteLine($"ManagedFont: Dispose leak detected for font: {_sourcePath}");
            }
            Dispose(false);
        }

        #endregion
    }
}
