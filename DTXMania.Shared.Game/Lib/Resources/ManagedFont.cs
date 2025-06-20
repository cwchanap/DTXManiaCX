using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using DTX.Utilities;
using XnaColor = Microsoft.Xna.Framework.Color;
using XnaRectangle = Microsoft.Xna.Framework.Rectangle;

namespace DTX.Resources
{
    /// <summary>
    /// Abstract managed font implementation with reference counting
    /// Based on DTXMania's CPrivateFont patterns with Japanese support
    /// Platform-specific implementations handle font loading
    /// </summary>
    public abstract class ManagedFont : IFont
    {
        #region Private Fields

        protected SpriteFont _spriteFont;
        protected readonly string _sourcePath;
        protected readonly int _size;
        protected readonly FontStyle _style;
        protected int _referenceCount;
        protected bool _disposed;
        protected readonly object _lockObject = new object();
        protected char _defaultCharacter = '?';

        // Character support cache for performance
        protected readonly HashSet<char> _supportedCharacters = new HashSet<char>();
        protected bool _characterCacheBuilt = false;

        // Custom font rendering data (when SpriteFont creation fails)
        protected Texture2D _customFontTexture;
        protected Dictionary<char, XnaRectangle> _customFontGlyphs;
        protected HashSet<char> _customFontCharacters;
        protected int _customLineSpacing;        // Text rendering cache for performance (similar to CPrivateFastFont)
        protected readonly CacheManager<string, CachedTextRender> _textRenderCache = new CacheManager<string, CachedTextRender>();
        protected const int MaxCacheSize = 128;

        // Platform-specific font resources (implemented by derived classes)

        protected class CachedTextRender : IDisposable
        {
            public ITexture Texture { get; set; }
            public TextRenderOptions Options { get; set; }
            public DateTime LastUsed { get; set; }
            public string Text { get; set; }

            public void Dispose()
            {
                Texture?.Dispose();
                Texture = null;
            }
        }

        #endregion

        #region Constructors

        /// <summary>
        /// Create font from system font name or font file path
        /// </summary>
        protected ManagedFont(GraphicsDevice graphicsDevice, string fontPath, int size, FontStyle style = FontStyle.Regular)
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
        public float LineSpacing => _spriteFont?.LineSpacing ?? _customLineSpacing;

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
            if (_disposed)
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
            if (_disposed || string.IsNullOrEmpty(text))
                return Vector2.Zero;

            // Use custom font measurement if SpriteFont is not available
            if (_spriteFont == null && _customFontTexture != null)
            {
                return MeasureStringCustom(text);
            }

            if (_spriteFont != null)
            {
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

            return Vector2.Zero;
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

        public XnaRectangle[] GetCharacterBounds(string text)
        {
            if (_disposed || _spriteFont == null || string.IsNullOrEmpty(text))
                return new XnaRectangle[0];

            var bounds = new List<XnaRectangle>();
            var position = Vector2.Zero;

            foreach (char c in text)
            {
                if (c == '\n')
                {
                    position.X = 0;
                    position.Y += LineSpacing;
                    bounds.Add(XnaRectangle.Empty);
                    continue;
                }

                var charSize = MeasureString(c.ToString());
                bounds.Add(new XnaRectangle((int)position.X, (int)position.Y, (int)charSize.X, (int)charSize.Y));
                position.X += charSize.X;
            }

            return bounds.ToArray();
        }

        #endregion

        #region Text Rendering

        public void DrawString(SpriteBatch spriteBatch, string text, Vector2 position, XnaColor color)
        {
            if (_disposed || string.IsNullOrEmpty(text))
                return;

            // Use custom font rendering if SpriteFont is not available
            if (_spriteFont == null && _customFontTexture != null)
            {
                DrawStringCustom(spriteBatch, text, position, color);
                return;
            }

            if (_spriteFont != null)
            {
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
        }

        public void DrawString(SpriteBatch spriteBatch, string text, Vector2 position, XnaColor color,
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
                                         XnaColor textColor, XnaColor outlineColor, int outlineThickness = 1)
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
                                          XnaColor topColor, XnaColor bottomColor)
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
                var color = XnaColor.Lerp(topColor, bottomColor, progress);

                DrawString(spriteBatch, line, new Vector2(position.X, currentY), color);
                currentY += lineHeight;
            }
        }

        public void DrawStringWithShadow(SpriteBatch spriteBatch, string text, Vector2 position,
                                        XnaColor textColor, XnaColor shadowColor, Vector2 shadowOffset)
        {
            if (_disposed || _spriteFont == null || string.IsNullOrEmpty(text))
                return;

            // Draw shadow first
            DrawString(spriteBatch, text, position + shadowOffset, shadowColor);

            // Draw main text on top
            DrawString(spriteBatch, text, position, textColor);
        }

        public void DrawStringWrapped(SpriteBatch spriteBatch, string text, XnaRectangle bounds,
                                     XnaColor color, TextAlignment alignment = TextAlignment.Left)
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

        public ITexture CreateTextTexture(GraphicsDevice graphicsDevice, string text, XnaColor color)
        {
            var options = new TextRenderOptions { TextColor = color };
            return CreateTextTexture(graphicsDevice, text, options);
        }        public ITexture CreateTextTexture(GraphicsDevice graphicsDevice, string text, TextRenderOptions options)
        {
            // This method should not be used anymore - require a shared RenderTarget
            throw new InvalidOperationException("CreateTextTexture must be called with a shared RenderTarget. Use the overload that accepts RenderTarget2D.");
        }        public ITexture CreateTextTexture(GraphicsDevice graphicsDevice, string text, TextRenderOptions options, RenderTarget2D sharedRenderTarget)
        {
            if (_disposed || _spriteFont == null || string.IsNullOrEmpty(text))
                return null;

            if (sharedRenderTarget == null)
                throw new ArgumentNullException(nameof(sharedRenderTarget));

            // Check cache first
            var cacheKey = GenerateCacheKey(text, options);
            if (_textRenderCache.TryGet(cacheKey, out var cachedRender))
            {
                cachedRender.LastUsed = DateTime.Now;
                return cachedRender.Texture;
            }

            // Create new texture
            var texture = CreateTextTextureInternal(graphicsDevice, text, options, sharedRenderTarget);
            if (texture != null)
            {
                // Add to cache
                CacheTextTexture(cacheKey, text, texture, options);
            }

            return texture;
        }        private ITexture CreateTextTextureInternal(GraphicsDevice graphicsDevice, string text, TextRenderOptions options, RenderTarget2D sharedRenderTarget)
        {
            var textSize = MeasureString(text);
            var width = (int)Math.Ceiling(textSize.X);
            var height = (int)Math.Ceiling(textSize.Y);

            if (width <= 0 || height <= 0)
                return null;

            var spriteBatch = new SpriteBatch(graphicsDevice);

            // Use the shared RenderTarget for rendering
            graphicsDevice.SetRenderTarget(sharedRenderTarget);
            graphicsDevice.Clear(XnaColor.Transparent);

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
            graphicsDevice.SetRenderTarget(null);            spriteBatch.Dispose();

            // Create a texture from the rendered content
            return new ManagedTexture(graphicsDevice, sharedRenderTarget, $"TextTexture_{text.GetHashCode()}");
        }

        private string GenerateCacheKey(string text, TextRenderOptions options)
        {
            // Create a unique key based on text and rendering options
            var keyBuilder = new System.Text.StringBuilder();
            keyBuilder.Append(text);
            keyBuilder.Append('|');
            keyBuilder.Append(options.TextColor.PackedValue);
            keyBuilder.Append('|');
            keyBuilder.Append(options.EnableOutline);
            keyBuilder.Append('|');
            keyBuilder.Append(options.OutlineColor.PackedValue);
            keyBuilder.Append('|');
            keyBuilder.Append(options.OutlineThickness);
            keyBuilder.Append('|');
            keyBuilder.Append(options.EnableGradient);
            keyBuilder.Append('|');
            keyBuilder.Append(options.GradientTopColor.PackedValue);
            keyBuilder.Append('|');
            keyBuilder.Append(options.GradientBottomColor.PackedValue);
            keyBuilder.Append('|');
            keyBuilder.Append(options.EnableShadow);
            keyBuilder.Append('|');
            keyBuilder.Append(options.ShadowColor.PackedValue);
            keyBuilder.Append('|');
            keyBuilder.Append(options.ShadowOffset.X);
            keyBuilder.Append(',');
            keyBuilder.Append(options.ShadowOffset.Y);

            return keyBuilder.ToString();
        }
        private void CacheTextTexture(string cacheKey, string text, ITexture texture, TextRenderOptions options)
        {
            var cachedRender = new CachedTextRender
            {
                Texture = texture,
                Options = options,
                LastUsed = DateTime.Now,
                Text = text
            };

            _textRenderCache.Add(cacheKey, cachedRender);
        }

        public Vector2 GetKerning(char first, char second)
        {
            // MonoGame SpriteFont doesn't expose kerning information directly
            // This is a simplified implementation
            return Vector2.Zero;
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Platform-specific font loading implementation
        /// </summary>
        protected abstract void LoadFont(GraphicsDevice graphicsDevice, string fontPath, int size, FontStyle style);

        /// <summary>
        /// Check if a font file is supported (implemented by derived classes)
        /// </summary>
        protected virtual bool IsSupportedFontFile(string fontPath)
        {
            var extension = Path.GetExtension(fontPath).ToLowerInvariant();
            return extension == ".ttf" || extension == ".otf" || extension == ".ttc" || extension == ".spritefont";
        }

        // Windows-specific font loading methods moved to platform-specific implementations

        protected void BuildCharacterCache()
        {
            _supportedCharacters.Clear();

            // For custom fonts, use the character set we have
            if (_customFontCharacters != null)
            {
                foreach (var c in _customFontCharacters)
                {
                    _supportedCharacters.Add(c);
                }
                Debug.WriteLine($"ManagedFont: Character cache built with {_supportedCharacters.Count} custom font characters");
            }
            else if (_spriteFont != null)
            {
                // Build cache of supported characters including Japanese ranges
                BuildCharacterRangeCache();
            }

            _characterCacheBuilt = true;
        }

        protected void BuildCharacterRangeCache()
        {
            // Basic ASCII (0x20-0x7E)
            TestCharacterRange(0x0020, 0x007E, "Basic ASCII");

            // Latin-1 Supplement (0x80-0xFF)
            TestCharacterRange(0x0080, 0x00FF, "Latin-1 Supplement");

            // Japanese Hiragana (0x3040-0x309F)
            TestCharacterRange(0x3040, 0x309F, "Hiragana");

            // Japanese Katakana (0x30A0-0x30FF)
            TestCharacterRange(0x30A0, 0x30FF, "Katakana");

            // CJK Unified Ideographs (Common Kanji) - Test a subset for performance
            TestCommonKanjiCharacters();

            // Halfwidth and Fullwidth Forms (0xFF00-0xFFEF)
            TestCharacterRange(0xFF00, 0xFFEF, "Halfwidth and Fullwidth Forms");

            // Common punctuation and symbols
            TestCharacterRange(0x2000, 0x206F, "General Punctuation");
            TestCharacterRange(0x3000, 0x303F, "CJK Symbols and Punctuation");

            Debug.WriteLine($"ManagedFont: Character cache built with {_supportedCharacters.Count} supported characters");
        }

        protected void TestCharacterRange(int startCode, int endCode, string rangeName)
        {
            int supportedCount = 0;
            for (int code = startCode; code <= endCode; code++)
            {
                char c = (char)code;
                if (TestCharacterSupport(c))
                {
                    _supportedCharacters.Add(c);
                    supportedCount++;
                }
            }
            Debug.WriteLine($"ManagedFont: {rangeName} range - {supportedCount}/{endCode - startCode + 1} characters supported");
        }

        protected void TestCommonKanjiCharacters()
        {
            // Test a subset of common Kanji characters for performance
            // These are some of the most frequently used Kanji
            var commonKanji = new[]
            {
                // Numbers
                '一', '二', '三', '四', '五', '六', '七', '八', '九', '十',
                // Common words
                '人', '日', '本', '国', '年', '大', '学', '生', '会', '社',
                '時', '間', '分', '月', '火', '水', '木', '金', '土', '曜',
                '今', '明', '昨', '来', '行', '見', '聞', '言', '話', '読',
                '書', '食', '飲', '買', '売', '作', '使', '思', '知', '好'
            };

            int supportedCount = 0;
            foreach (char kanji in commonKanji)
            {
                if (TestCharacterSupport(kanji))
                {
                    _supportedCharacters.Add(kanji);
                    supportedCount++;
                }
            }
            Debug.WriteLine($"ManagedFont: Common Kanji - {supportedCount}/{commonKanji.Length} characters supported");
        }

        protected bool TestCharacterSupport(char character)
        {
            if (_spriteFont == null) return false;

            try
            {
                // Try to measure the character - if it throws, it's not supported
                var size = _spriteFont.MeasureString(character.ToString());
                return size.X > 0 && size.Y > 0;
            }
            catch
            {
                // Character not supported
                return false;
            }
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
                    // Try to find a suitable replacement for Japanese characters
                    char replacement = GetCharacterReplacement(c);
                    result.Append(replacement);
                }
            }

            return result.ToString();
        }

        private char GetCharacterReplacement(char unsupportedChar)
        {
            // Try to provide meaningful replacements for Japanese characters

            // For Hiragana, try to find Katakana equivalent
            if (unsupportedChar >= 0x3040 && unsupportedChar <= 0x309F)
            {
                char katakanaEquivalent = (char)(unsupportedChar + 0x0060); // Hiragana to Katakana offset
                if (SupportsCharacter(katakanaEquivalent))
                    return katakanaEquivalent;
            }

            // For Katakana, try to find Hiragana equivalent
            if (unsupportedChar >= 0x30A0 && unsupportedChar <= 0x30FF)
            {
                char hiraganaEquivalent = (char)(unsupportedChar - 0x0060); // Katakana to Hiragana offset
                if (SupportsCharacter(hiraganaEquivalent))
                    return hiraganaEquivalent;
            }

            // For fullwidth characters, try halfwidth equivalents
            if (unsupportedChar >= 0xFF00 && unsupportedChar <= 0xFFEF)
            {
                char halfwidthEquivalent = (char)(unsupportedChar - 0xFEE0);
                if (halfwidthEquivalent >= 0x20 && halfwidthEquivalent <= 0x7E && SupportsCharacter(halfwidthEquivalent))
                    return halfwidthEquivalent;
            }

            // For other characters, try common replacements
            switch (unsupportedChar)
            {
                case '…': return SupportsCharacter('.') ? '.' : _defaultCharacter;
                case '—': return SupportsCharacter('-') ? '-' : _defaultCharacter;
                case '\u2018': case '\u2019': return SupportsCharacter('\'') ? '\'' : _defaultCharacter; // Left/Right single quotation marks
                case '\u201C': case '\u201D': return SupportsCharacter('"') ? '"' : _defaultCharacter; // Left/Right double quotation marks
                default: return _defaultCharacter;
            }
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
                        // Clean up text render cache - CacheManager handles disposal automatically
                        _textRenderCache.Clear();

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

        #region Custom Font Rendering Methods

        private void DrawStringCustom(SpriteBatch spriteBatch, string text, Vector2 position, XnaColor color)
        {
            if (_customFontTexture == null || _customFontGlyphs == null || string.IsNullOrEmpty(text))
                return;

            try
            {
                var currentPosition = position;

                foreach (char c in text)
                {
                    if (c == '\n')
                    {
                        currentPosition.X = position.X;
                        currentPosition.Y += _customLineSpacing;
                        continue;
                    }

                    if (_customFontGlyphs.TryGetValue(c, out var glyph))
                    {
                        spriteBatch.Draw(_customFontTexture, currentPosition, glyph, color);
                        currentPosition.X += glyph.Width;
                    }
                    else if (_customFontGlyphs.TryGetValue(_defaultCharacter, out var defaultGlyph))
                    {
                        spriteBatch.Draw(_customFontTexture, currentPosition, defaultGlyph, color);
                        currentPosition.X += defaultGlyph.Width;
                    }
                    else
                    {
                        // Skip unknown characters
                        currentPosition.X += 8; // Default character width
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"DrawStringCustom error: {ex.Message}");
            }
        }

        private Vector2 MeasureStringCustom(string text)
        {
            if (_customFontGlyphs == null || string.IsNullOrEmpty(text))
                return Vector2.Zero;

            try
            {
                float width = 0;
                float height = _customLineSpacing;
                float currentLineWidth = 0;
                int lineCount = 1;

                foreach (char c in text)
                {
                    if (c == '\n')
                    {
                        width = Math.Max(width, currentLineWidth);
                        currentLineWidth = 0;
                        lineCount++;
                        continue;
                    }

                    if (_customFontGlyphs.TryGetValue(c, out var glyph))
                    {
                        currentLineWidth += glyph.Width;
                    }
                    else
                    {
                        currentLineWidth += 8; // Default character width
                    }
                }

                width = Math.Max(width, currentLineWidth);
                height = lineCount * _customLineSpacing;

                return new Vector2(width, height);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MeasureStringCustom error: {ex.Message}");
                return Vector2.Zero;
            }
        }

        #endregion

        // Platform-specific font creation methods moved to derived classes
    }

    #region Helper Classes

    /// <summary>
    /// Font atlas data structure for platform-specific font implementations
    /// </summary>
    public class FontAtlas
    {
        public Texture2D Texture { get; set; }
        public Dictionary<char, CharacterData> Characters { get; set; }
        public int LineSpacing { get; set; }
    }

    /// <summary>
    /// Character data for font atlas
    /// </summary>
    public class CharacterData
    {
        public char Character { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int AtlasX { get; set; }
        public int AtlasY { get; set; }
        public int AtlasWidth { get; set; }
        public int AtlasHeight { get; set; }
    }

    #endregion
}
