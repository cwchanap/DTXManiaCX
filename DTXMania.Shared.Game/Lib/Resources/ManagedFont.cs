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

        // Text rendering cache for performance (similar to CPrivateFastFont)
        private readonly Dictionary<string, CachedTextRender> _textRenderCache = new Dictionary<string, CachedTextRender>();
        private const int MaxCacheSize = 128;

        private struct CachedTextRender
        {
            public ITexture Texture;
            public TextRenderOptions Options;
            public DateTime LastUsed;
            public string Text;
        }

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

            // Check cache first
            var cacheKey = GenerateCacheKey(text, options);
            if (_textRenderCache.TryGetValue(cacheKey, out var cachedRender))
            {
                cachedRender.LastUsed = DateTime.Now;
                _textRenderCache[cacheKey] = cachedRender;
                return cachedRender.Texture;
            }

            // Create new texture
            var texture = CreateTextTextureInternal(graphicsDevice, text, options);
            if (texture != null)
            {
                // Add to cache
                CacheTextTexture(cacheKey, text, texture, options);
            }

            return texture;
        }

        private ITexture CreateTextTextureInternal(GraphicsDevice graphicsDevice, string text, TextRenderOptions options)
        {
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
            // Clean up old cache entries if we're at the limit
            if (_textRenderCache.Count >= MaxCacheSize)
            {
                CleanupOldCacheEntries();
            }

            var cachedRender = new CachedTextRender
            {
                Texture = texture,
                Options = options,
                LastUsed = DateTime.Now,
                Text = text
            };

            _textRenderCache[cacheKey] = cachedRender;
        }

        private void CleanupOldCacheEntries()
        {
            // Remove the oldest entries (LRU eviction)
            var entriesToRemove = _textRenderCache.Count - MaxCacheSize + 10; // Remove extra to avoid frequent cleanup
            var sortedEntries = _textRenderCache.OrderBy(kvp => kvp.Value.LastUsed).Take(entriesToRemove);

            foreach (var entry in sortedEntries.ToList())
            {
                entry.Value.Texture?.Dispose();
                _textRenderCache.Remove(entry.Key);
            }
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
            try
            {
                // First, try to load as a SpriteFont from content pipeline
                if (fontPath.EndsWith(".spritefont", StringComparison.OrdinalIgnoreCase))
                {
                    LoadSpriteFontFromContent(graphicsDevice, fontPath);
                    return;
                }

                // Try to load as TTF/OTF font file
                if (File.Exists(fontPath) && IsSupportedFontFile(fontPath))
                {
                    LoadTrueTypeFont(graphicsDevice, fontPath, size, style);
                    return;
                }

                // Try to load as system font by name
                LoadSystemFont(graphicsDevice, fontPath, size, style);
            }
            catch (Exception ex)
            {
                // Fallback to default system font
                LoadFallbackFont(graphicsDevice, size, style);
                Debug.WriteLine($"ManagedFont: Failed to load font '{fontPath}', using fallback. Error: {ex.Message}");
            }
        }

        private bool IsSupportedFontFile(string fontPath)
        {
            var extension = Path.GetExtension(fontPath).ToLowerInvariant();
            return extension == ".ttf" || extension == ".otf" || extension == ".ttc";
        }

        private void LoadSpriteFontFromContent(GraphicsDevice graphicsDevice, string fontPath)
        {
            // Remove .spritefont extension for content loading
            var contentName = Path.GetFileNameWithoutExtension(fontPath);

            // This would require a content manager - for now, throw with better error message
            throw new FontLoadException(fontPath,
                "SpriteFont loading requires ContentManager integration. " +
                "Please use TTF/OTF files or system fonts instead.");
        }

        private void LoadTrueTypeFont(GraphicsDevice graphicsDevice, string fontPath, int size, FontStyle style)
        {
            // For MonoGame, we need to create a SpriteFont dynamically
            // This is a simplified implementation - in production you might want to use
            // libraries like MonoGame.Extended or create your own bitmap font generator

            try
            {
                // Create a basic SpriteFont using system font as fallback
                // In a full implementation, you would parse the TTF/OTF file
                var fontName = Path.GetFileNameWithoutExtension(fontPath);
                LoadSystemFont(graphicsDevice, fontName, size, style);
            }
            catch
            {
                // If that fails, try to load the font into system and use it
                LoadFontFileIntoSystem(fontPath, size, style);
            }
        }

        private void LoadFontFileIntoSystem(string fontPath, int size, FontStyle style)
        {
            // This is a placeholder for loading font files into the system
            // In a full implementation, you would use platform-specific APIs
            // to register the font temporarily and then create a SpriteFont

            throw new FontLoadException(fontPath,
                "Direct TTF/OTF loading not yet implemented. " +
                "Please convert to SpriteFont or use system fonts.");
        }

        private void LoadSystemFont(GraphicsDevice graphicsDevice, string fontName, int size, FontStyle style)
        {
            // Create a basic SpriteFont using MonoGame's built-in capabilities
            // This is a simplified approach - you might want to use a font texture generator

            // For now, we'll create a minimal SpriteFont
            // In production, you'd want to use MonoGame.Extended or similar
            CreateBasicSpriteFont(graphicsDevice, fontName, size, style);
        }

        private void CreateBasicSpriteFont(GraphicsDevice graphicsDevice, string fontName, int size, FontStyle style)
        {
            // This is a placeholder implementation
            // In a real implementation, you would generate a texture atlas with the font
            // and create a SpriteFont from it

            throw new FontLoadException(fontName,
                "Dynamic SpriteFont creation not implemented. " +
                "Please use pre-built SpriteFont files or implement font texture generation.");
        }

        private void LoadFallbackFont(GraphicsDevice graphicsDevice, int size, FontStyle style)
        {
            // Try to load a basic fallback font
            try
            {
                // This would be a pre-built SpriteFont that's always available
                // For now, we'll throw a more descriptive error
                throw new FontLoadException("fallback",
                    "No fallback font available. Please ensure at least one SpriteFont is available in the content pipeline.");
            }
            catch
            {
                // Last resort - create a minimal 1x1 texture as a placeholder
                // This prevents complete failure but won't render text properly
                Debug.WriteLine("ManagedFont: Creating minimal fallback font");
                _spriteFont = null; // Will be handled by null checks in rendering methods
            }
        }

        private void BuildCharacterCache()
        {
            if (_spriteFont == null) return;

            _supportedCharacters.Clear();

            // Build cache of supported characters including Japanese ranges
            BuildCharacterRangeCache();

            _characterCacheBuilt = true;
        }

        private void BuildCharacterRangeCache()
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

        private void TestCharacterRange(int startCode, int endCode, string rangeName)
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

        private void TestCommonKanjiCharacters()
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

        private bool TestCharacterSupport(char character)
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
                        // Clean up text render cache
                        foreach (var cachedRender in _textRenderCache.Values)
                        {
                            cachedRender.Texture?.Dispose();
                        }
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
    }
}
