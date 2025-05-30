using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using XnaColor = Microsoft.Xna.Framework.Color;
using XnaRectangle = Microsoft.Xna.Framework.Rectangle;
using GdiColor = System.Drawing.Color;
using GdiRectangle = System.Drawing.Rectangle;

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

        // Custom font rendering data (when SpriteFont creation fails)
        private Texture2D _customFontTexture;
        private Dictionary<char, XnaRectangle> _customFontGlyphs;
        private HashSet<char> _customFontCharacters;
        private int _customLineSpacing;

        // Text rendering cache for performance (similar to CPrivateFastFont)
        private readonly Dictionary<string, CachedTextRender> _textRenderCache = new Dictionary<string, CachedTextRender>();
        private const int MaxCacheSize = 128;

        // GDI+ font resources (like DTXMania's CPrivateFont)
        private System.Drawing.Text.PrivateFontCollection _privateFontCollection;
        private System.Drawing.FontFamily _fontFamily;
        private System.Drawing.Font _gdiFont;

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
                Debug.WriteLine($"ManagedFont: Creating minimal fallback font");
                try
                {
                    // Try Arial as fallback
                    LoadSystemFont(graphicsDevice, "Arial", size, style);
                }
                catch
                {
                    // If Arial fails, try Segoe UI
                    try
                    {
                        LoadSystemFont(graphicsDevice, "Segoe UI", size, style);
                    }
                    catch
                    {
                        // Last resort - create a minimal placeholder
                        Debug.WriteLine("ManagedFont: All fallback fonts failed, creating minimal placeholder");
                        _customLineSpacing = size;
                        _customFontCharacters = new HashSet<char> { '?' };
                        _customFontGlyphs = new Dictionary<char, XnaRectangle> { { '?', new XnaRectangle(0, 0, size, size) } };
                        BuildCharacterCache();
                    }
                }
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
            try
            {
                // Use DTXMania's approach: load TTF/OTF using PrivateFontCollection
                LoadPrivateFont(fontPath, size, style);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to load TTF/OTF font {fontPath}: {ex.Message}");
                // Try system font as fallback
                var fontName = Path.GetFileNameWithoutExtension(fontPath);
                LoadSystemFont(graphicsDevice, fontName, size, style);
            }
        }

        private void LoadPrivateFont(string fontPath, int size, FontStyle style)
        {
            try
            {
                Debug.WriteLine($"Attempting to load private font: {fontPath}");

                // Create PrivateFontCollection like DTXMania does
                _privateFontCollection = new System.Drawing.Text.PrivateFontCollection();
                _privateFontCollection.AddFontFile(fontPath);

                // Get the font family
                if (_privateFontCollection.Families.Length == 0)
                {
                    throw new FontLoadException(fontPath, "No font families found in font file");
                }

                _fontFamily = _privateFontCollection.Families[0];
                Debug.WriteLine($"Loaded font family: {_fontFamily.Name}");

                // Convert our FontStyle to System.Drawing.FontStyle
                var gdiStyle = ConvertToGdiFontStyle(style);
                var originalStyle = style;

                // Check if the requested style is available
                if (!_fontFamily.IsStyleAvailable(gdiStyle))
                {
                    Debug.WriteLine($"Style {style} not available, trying alternatives...");
                    // Try different styles like DTXMania does
                    var availableStyles = new[] { System.Drawing.FontStyle.Regular, System.Drawing.FontStyle.Bold, System.Drawing.FontStyle.Italic };
                    foreach (var availableStyle in availableStyles)
                    {
                        if (_fontFamily.IsStyleAvailable(availableStyle))
                        {
                            gdiStyle = availableStyle;
                            Debug.WriteLine($"Font style changed from {originalStyle} to {availableStyle} for {fontPath}");
                            break;
                        }
                    }
                }

                // Create the font with pixel-based sizing like DTXMania
                float emSize = size * 96.0f / 72.0f; // Convert points to pixels
                _gdiFont = new System.Drawing.Font(_fontFamily, emSize, gdiStyle, System.Drawing.GraphicsUnit.Pixel);

                Debug.WriteLine($"Successfully loaded private font: {fontPath} ({_fontFamily.Name}, {gdiStyle}, {emSize}px)");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to load private font {fontPath}: {ex.Message}");
                throw new FontLoadException(fontPath, $"Failed to load private font: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Convert our FontStyle enum to System.Drawing.FontStyle
        /// </summary>
        private static System.Drawing.FontStyle ConvertToGdiFontStyle(FontStyle style)
        {
            return style switch
            {
                FontStyle.Regular => System.Drawing.FontStyle.Regular,
                FontStyle.Bold => System.Drawing.FontStyle.Bold,
                FontStyle.Italic => System.Drawing.FontStyle.Italic,
                _ => System.Drawing.FontStyle.Regular
            };
        }

        private void LoadSystemFont(GraphicsDevice graphicsDevice, string fontName, int size, FontStyle style)
        {
            try
            {
                Debug.WriteLine($"Attempting to load system font: {fontName} {size}pt {style}");

                // Use custom font renderer directly (no SpriteFont fallbacks)
                if (TryCreateSimpleSystemFont(graphicsDevice, fontName, size, style))
                {
                    Debug.WriteLine($"Successfully loaded system font using custom renderer: {fontName}");
                    return;
                }

                // If custom font creation fails, throw exception
                throw new FontLoadException(fontName, "Failed to create custom font renderer");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to load system font {fontName}: {ex.Message}");
                throw new FontLoadException(fontName, $"Failed to load system font: {ex.Message}", ex);
            }
        }

        private bool TryCreateSimpleSystemFont(GraphicsDevice graphicsDevice, string fontName, int size, FontStyle style)
        {
            try
            {
                Debug.WriteLine($"Creating custom font renderer for {fontName}");

                // Create a minimal character set for testing
                var basicChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 !@#$%^&*()_+-=[]{}|;:,.<>?";

                // Convert our FontStyle to System.Drawing.FontStyle
                var gdiStyle = ConvertToGdiFontStyle(style);

                // Create GDI+ font
                float emSize = size * 96.0f / 72.0f; // Convert points to pixels
                using (var gdiFont = new System.Drawing.Font(fontName, emSize, gdiStyle, System.Drawing.GraphicsUnit.Pixel))
                {
                    Debug.WriteLine($"Created GDI font: {gdiFont.Name} {gdiFont.Size}px");

                    // Create a simple texture atlas with just basic characters
                    var atlas = CreateSimpleFontAtlas(graphicsDevice, gdiFont, basicChars);

                    // Store custom font data directly (skip SpriteFont creation)
                    _customFontTexture = atlas.Texture;
                    _customFontGlyphs = atlas.Characters.ToDictionary(kvp => kvp.Key, kvp =>
                        new XnaRectangle(kvp.Value.AtlasX, kvp.Value.AtlasY, kvp.Value.AtlasWidth, kvp.Value.AtlasHeight));
                    _customFontCharacters = new HashSet<char>(atlas.Characters.Keys);
                    _customLineSpacing = atlas.LineSpacing;

                    // Build character cache
                    BuildCharacterCache();

                    Debug.WriteLine($"Custom font renderer created successfully with {basicChars.Length} characters");
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Custom font creation failed: {ex.Message}");
                return false;
            }
        }

        // Removed CreateSystemFontSpriteFont and CreateSpriteFontFromGdiFont - using custom renderer only

        // Removed LoadFallbackFont, CreateFontAtlas, and CreateSpriteFontFromAtlas - using custom renderer only

        private void BuildCharacterCache()
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

        #region Simple Font Creation Methods

        private FontAtlas CreateSimpleFontAtlas(GraphicsDevice graphicsDevice, System.Drawing.Font gdiFont, string characters)
        {
            try
            {
                Debug.WriteLine($"Creating simple font atlas with {characters.Length} characters");

                // Measure all characters to determine atlas size
                var charData = new Dictionary<char, CharacterData>();
                int maxCharWidth = 0;
                int maxCharHeight = 0;

                using (var bitmap = new System.Drawing.Bitmap(1, 1))
                using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
                {
                    graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

                    foreach (char c in characters)
                    {
                        var charString = c.ToString();
                        var size = graphics.MeasureString(charString, gdiFont, System.Drawing.PointF.Empty, System.Drawing.StringFormat.GenericTypographic);

                        var charWidth = (int)Math.Ceiling(size.Width) + 2; // Add padding
                        var charHeight = (int)Math.Ceiling(size.Height) + 2;

                        maxCharWidth = Math.Max(maxCharWidth, charWidth);
                        maxCharHeight = Math.Max(maxCharHeight, charHeight);

                        charData[c] = new CharacterData
                        {
                            Character = c,
                            Width = charWidth,
                            Height = charHeight
                        };
                    }
                }

                // Calculate atlas dimensions (smaller for simple version)
                int charsPerRow = 16; // Fixed grid for simplicity
                int atlasWidth = NextPowerOfTwo(charsPerRow * maxCharWidth);
                int atlasHeight = NextPowerOfTwo((int)Math.Ceiling((double)characters.Length / charsPerRow) * maxCharHeight);

                Debug.WriteLine($"Atlas dimensions: {atlasWidth}x{atlasHeight}, char size: {maxCharWidth}x{maxCharHeight}");

                // Create atlas bitmap
                using (var atlasBitmap = new System.Drawing.Bitmap(atlasWidth, atlasHeight, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                using (var graphics = System.Drawing.Graphics.FromImage(atlasBitmap))
                {
                    graphics.Clear(System.Drawing.Color.Transparent);
                    graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

                    // Render characters to atlas
                    int x = 0, y = 0;
                    int charIndex = 0;
                    foreach (char c in characters)
                    {
                        var data = charData[c];

                        // Draw character
                        graphics.DrawString(c.ToString(), gdiFont, System.Drawing.Brushes.White, x + 1, y + 1, System.Drawing.StringFormat.GenericTypographic);

                        // Update character data with atlas position
                        data.AtlasX = x;
                        data.AtlasY = y;
                        data.AtlasWidth = maxCharWidth;
                        data.AtlasHeight = maxCharHeight;

                        // Move to next position
                        x += maxCharWidth;
                        charIndex++;
                        if (charIndex % charsPerRow == 0)
                        {
                            x = 0;
                            y += maxCharHeight;
                        }
                    }

                    // Convert to MonoGame texture
                    var texture = CreateTextureFromBitmap(graphicsDevice, atlasBitmap);

                    return new FontAtlas
                    {
                        Texture = texture,
                        Characters = charData,
                        LineSpacing = maxCharHeight
                    };
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"CreateSimpleFontAtlas error: {ex.Message}");
                throw;
            }
        }

        private SpriteFont CreateSimpleSpriteFontFromAtlas(GraphicsDevice graphicsDevice, FontAtlas atlas, System.Drawing.Font gdiFont)
        {
            try
            {
                Debug.WriteLine("Creating simple SpriteFont from atlas");

                // Create character data for SpriteFont
                var glyphs = new List<XnaRectangle>();
                var cropping = new List<XnaRectangle>();
                var characters = new List<char>();
                var kerning = new List<Vector3>();

                foreach (var kvp in atlas.Characters.OrderBy(x => x.Key))
                {
                    var c = kvp.Key;
                    var data = kvp.Value;

                    characters.Add(c);

                    // Glyph rectangle in atlas texture
                    glyphs.Add(new XnaRectangle(data.AtlasX, data.AtlasY, data.Width, data.Height));

                    // Cropping rectangle (no cropping for simplicity)
                    cropping.Add(new XnaRectangle(0, 0, data.Width, data.Height));

                    // Kerning (left bearing, width, right bearing)
                    kerning.Add(new Vector3(0, data.Width, 0));
                }

                // Try to create SpriteFont using a simpler approach
                // Instead of reflection, we'll try to use MonoGame's built-in methods if available
                return TryCreateSpriteFontDirect(atlas.Texture, glyphs, cropping, characters, atlas.LineSpacing, kerning);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"CreateSimpleSpriteFontFromAtlas error: {ex.Message}");
                return null;
            }
        }

        private SpriteFont TryCreateSpriteFontDirect(Texture2D texture, List<XnaRectangle> glyphs, List<XnaRectangle> cropping, List<char> characters, int lineSpacing, List<Vector3> kerning)
        {
            try
            {
                Debug.WriteLine("Attempting to create SpriteFont via reflection...");

                // Try multiple constructor signatures that MonoGame might use
                var spriteFontType = typeof(SpriteFont);

                // Try the most common constructor signature first
                var constructors = spriteFontType.GetConstructors(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                Debug.WriteLine($"Found {constructors.Length} constructors for SpriteFont");

                foreach (var ctor in constructors)
                {
                    var parameters = ctor.GetParameters();
                    Debug.WriteLine($"Constructor with {parameters.Length} parameters: {string.Join(", ", parameters.Select(p => p.ParameterType.Name))}");
                }

                // Since reflection is failing, let's create a custom font renderer instead
                Debug.WriteLine("SpriteFont reflection failed, creating custom font renderer");
                return CreateCustomSpriteFont(texture, glyphs, cropping, characters, lineSpacing, kerning);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"TryCreateSpriteFontDirect error: {ex.Message}");
                return null;
            }
        }

        private SpriteFont CreateCustomSpriteFont(Texture2D texture, List<XnaRectangle> glyphs, List<XnaRectangle> cropping, List<char> characters, int lineSpacing, List<Vector3> kerning)
        {
            try
            {
                Debug.WriteLine("Creating custom SpriteFont implementation");

                // Instead of trying to create a real SpriteFont, we'll store the data and create a custom renderer
                // Store the font atlas data for custom rendering
                _customFontTexture = texture;
                _customFontGlyphs = glyphs.ToDictionary(g => characters[glyphs.IndexOf(g)], g => g);
                _customFontCharacters = new HashSet<char>(characters);
                _customLineSpacing = lineSpacing;

                Debug.WriteLine($"Custom font data stored: {characters.Count} characters, line spacing: {lineSpacing}");

                // Return null since we can't create a real SpriteFont, but we have the data for custom rendering
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"CreateCustomSpriteFont error: {ex.Message}");
                return null;
            }
        }

        #endregion

        #region Helper Methods

        private static int NextPowerOfTwo(int value)
        {
            if (value <= 0) return 1;

            value--;
            value |= value >> 1;
            value |= value >> 2;
            value |= value >> 4;
            value |= value >> 8;
            value |= value >> 16;
            value++;

            return value;
        }

        private static Texture2D CreateTextureFromBitmap(GraphicsDevice graphicsDevice, System.Drawing.Bitmap bitmap)
        {
            try
            {
                // Lock bitmap data for reading
                var bitmapData = bitmap.LockBits(
                    new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height),
                    System.Drawing.Imaging.ImageLockMode.ReadOnly,
                    System.Drawing.Imaging.PixelFormat.Format32bppArgb);

                // Create texture
                var texture = new Texture2D(graphicsDevice, bitmap.Width, bitmap.Height);

                // Copy pixel data
                var pixelData = new byte[bitmapData.Stride * bitmap.Height];
                System.Runtime.InteropServices.Marshal.Copy(bitmapData.Scan0, pixelData, 0, pixelData.Length);

                // Convert BGRA to RGBA
                for (int i = 0; i < pixelData.Length; i += 4)
                {
                    var temp = pixelData[i]; // Blue
                    pixelData[i] = pixelData[i + 2]; // Red
                    pixelData[i + 2] = temp; // Blue
                }

                texture.SetData(pixelData);

                bitmap.UnlockBits(bitmapData);

                return texture;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"CreateTextureFromBitmap error: {ex.Message}");
                throw;
            }
        }

        #endregion
    }

    #region Helper Classes

    internal class FontAtlas
    {
        public Texture2D Texture { get; set; }
        public Dictionary<char, CharacterData> Characters { get; set; }
        public int LineSpacing { get; set; }
    }

    internal class CharacterData
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
