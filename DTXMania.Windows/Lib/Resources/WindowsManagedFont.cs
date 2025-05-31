using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using DTX.Resources;
using XnaColor = Microsoft.Xna.Framework.Color;
using XnaRectangle = Microsoft.Xna.Framework.Rectangle;

namespace DTXMania.Windows.Resources
{
    /// <summary>
    /// Windows-specific managed font implementation using GDI+ and System.Drawing
    /// Based on DTXMania's CPrivateFont patterns with Japanese support
    /// </summary>
    public class WindowsManagedFont : ManagedFont
    {
        #region Windows-Specific Fields

        // GDI+ font resources (like DTXMania's CPrivateFont)
        private System.Drawing.Text.PrivateFontCollection _privateFontCollection;
        private System.Drawing.FontFamily _fontFamily;
        private System.Drawing.Font _gdiFont;

        #endregion

        #region Constructors

        /// <summary>
        /// Create Windows font from system font name or font file path
        /// </summary>
        public WindowsManagedFont(GraphicsDevice graphicsDevice, string fontPath, int size, FontStyle style = FontStyle.Regular)
            : base(graphicsDevice, fontPath, size, style)
        {
        }

        /// <summary>
        /// Create Windows font from existing SpriteFont
        /// </summary>
        public WindowsManagedFont(SpriteFont spriteFont, string sourcePath, int size, FontStyle style = FontStyle.Regular)
            : base(spriteFont, sourcePath, size, style)
        {
        }

        #endregion

        #region Platform-Specific Implementation

        protected override void LoadFont(GraphicsDevice graphicsDevice, string fontPath, int size, FontStyle style)
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
                Debug.WriteLine($"WindowsManagedFont: Creating minimal fallback font");
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
                        Debug.WriteLine("WindowsManagedFont: All fallback fonts failed, creating minimal placeholder");
                        _customLineSpacing = size;
                        _customFontCharacters = new HashSet<char> { '?' };
                        _customFontGlyphs = new Dictionary<char, XnaRectangle> { { '?', new XnaRectangle(0, 0, size, size) } };
                        BuildCharacterCache();
                    }
                }
                Debug.WriteLine($"WindowsManagedFont: Failed to load font '{fontPath}', using fallback. Error: {ex.Message}");
            }
        }

        #endregion

        #region Windows-Specific Font Loading Methods

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

        #endregion

        #region Windows-Specific Font Creation Methods

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

        #region IDisposable Implementation

        protected override void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                // Dispose Windows-specific resources
                _gdiFont?.Dispose();
                _privateFontCollection?.Dispose();
                _fontFamily?.Dispose();
            }

            base.Dispose(disposing);
        }

        #endregion
    }
}
