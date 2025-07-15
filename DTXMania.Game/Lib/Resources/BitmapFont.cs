using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using DTX.Resources;
using System;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("DTXMania.Test")]

namespace DTX.Resources
{
    /// <summary>
    /// Generic bitmap font renderer supporting different character sets and configurations
    /// Based on DTXMania patterns with extensible design
    /// </summary>
    public class BitmapFont : IDisposable
    {
        #region Configuration Classes

        /// <summary>
        /// Configuration for bitmap font character layout
        /// </summary>
        public class BitmapFontConfig
        {
            public string DisplayableCharacters { get; set; }
            public int[] CharacterWidths { get; set; }  // Width for each character (or single width for all)
            public int CharacterHeight { get; set; }
            public int[] SourceCharacterWidths { get; set; }  // Source texture widths (or single width)
            public int SourceCharacterHeight { get; set; }
            public string[] TexturePaths { get; set; }
            public bool UseVariableWidths { get; set; } = false;
        }

        #endregion

        #region Fields

        private readonly GraphicsDevice _graphicsDevice;
        private readonly BitmapFontConfig _config;
        private ITexture[] _fontTextures;
        private Rectangle[] _characterRectangles;
        private bool _disposed = false;
        
        // Legacy DTXMania console font support
        private ITexture _fontTexture;
        private ITexture _fontTexture2;

        #endregion

        #region Properties

        public int CharacterHeight => _config.CharacterHeight;
        public bool IsLoaded => _fontTextures != null && _fontTextures.Length > 0 && _fontTextures[0] != null;

        #endregion

        #region Constructor

        public BitmapFont(GraphicsDevice graphicsDevice, IResourceManager resourceManager, BitmapFontConfig config)
        {
            _graphicsDevice = graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
            _config = config ?? throw new ArgumentNullException(nameof(config));

            LoadFontTextures(resourceManager);
            InitializeCharacterRectangles();
        }

        /// <summary>
        /// Internal constructor for testing purposes
        /// Allows null GraphicsDevice since it's not actually used in the current implementation
        /// </summary>
        internal BitmapFont(IResourceManager resourceManager, BitmapFontConfig config, bool allowNullGraphicsDevice)
        {
            if (!allowNullGraphicsDevice)
                throw new ArgumentException("This constructor is for testing only", nameof(allowNullGraphicsDevice));

            _graphicsDevice = null!; // Allowed for testing since GraphicsDevice is not used
            _config = config ?? throw new ArgumentNullException(nameof(config));

            LoadFontTextures(resourceManager ?? throw new ArgumentNullException(nameof(resourceManager)));
            InitializeCharacterRectangles();
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Draw text using the bitmap font
        /// </summary>
        /// <param name="spriteBatch">SpriteBatch for drawing</param>
        /// <param name="text">Text to draw</param>
        /// <param name="x">X position</param>
        /// <param name="y">Y position</param>
        /// <param name="color">Tint color (default: White)</param>
        /// <param name="fontType">Font type for console fonts (ignored for other font types)</param>
        public void DrawText(SpriteBatch spriteBatch, string text, int x, int y, Color color = default, FontType fontType = FontType.Normal)
        {
            if (_disposed || string.IsNullOrEmpty(text) || !IsLoaded)
                return;

            if (color == default)
                color = Color.White;

            int currentX = x;
            int currentY = y;
            int startX = x; // Remember start position for newlines

            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                
                if (ch == '\n')
                {
                    // Handle newline
                    currentX = startX;
                    currentY += _config.CharacterHeight;
                }
                else
                {
                    int charIndex = _config.DisplayableCharacters.IndexOf(ch);
                    if (charIndex < 0)
                    {
                        // Character not found, just advance position with default width
                        currentX += GetCharacterWidth(0);
                    }
                    else
                    {
                        // Draw the character
                        DrawCharacter(spriteBatch, charIndex, currentX, currentY, color, fontType);
                        currentX += GetCharacterWidth(charIndex);
                    }
                }
            }
        }

        /// <summary>
        /// Measure the size of text when rendered
        /// </summary>
        /// <param name="text">Text to measure</param>
        /// <returns>Size of the text</returns>
        public Vector2 MeasureText(string text)
        {
            if (string.IsNullOrEmpty(text))
                return Vector2.Zero;

            int maxWidth = 0;
            int currentWidth = 0;
            int lines = 1;

            foreach (char ch in text)
            {
                if (ch == '\n')
                {
                    maxWidth = Math.Max(maxWidth, currentWidth);
                    currentWidth = 0;
                    lines++;
                }
                else
                {
                    int charIndex = _config.DisplayableCharacters.IndexOf(ch);
                    if (charIndex >= 0)
                    {
                        currentWidth += GetCharacterWidth(charIndex);
                    }
                }
            }

            maxWidth = Math.Max(maxWidth, currentWidth);
            return new Vector2(maxWidth, lines * _config.CharacterHeight);
        }

        #endregion

        #region Private Methods

        private void LoadFontTextures(IResourceManager resourceManager)
        {
            try
            {
                if (_config.TexturePaths == null || _config.TexturePaths.Length == 0)
                {
                    System.Diagnostics.Debug.WriteLine("BitmapFont: No texture paths configured");
                    return;
                }

                _fontTextures = new ITexture[_config.TexturePaths.Length];
                
                for (int i = 0; i < _config.TexturePaths.Length; i++)
                {
                    var texturePath = _config.TexturePaths[i];
                    System.Diagnostics.Debug.WriteLine($"BitmapFont: Loading texture {i}: {texturePath}");
                    
                    _fontTextures[i] = resourceManager.LoadTexture(texturePath);
                    
                    if (_fontTextures[i] != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"BitmapFont: Successfully loaded texture {i}. Size: {_fontTextures[i].Width}x{_fontTextures[i].Height}");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"BitmapFont: Failed to load texture {i}: {texturePath}");
                    }
                }
                
                // For legacy DTXMania console font support
                if (_config.TexturePaths.Length >= 1)
                    _fontTexture = _fontTextures[0];
                if (_config.TexturePaths.Length >= 2)
                    _fontTexture2 = _fontTextures[1];
                
                System.Diagnostics.Debug.WriteLine("BitmapFont: Texture loading completed");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"BitmapFont: Exception during texture loading: {ex.Message}");
                // Font textures are optional - we'll just not render text if they fail
            }
        }

        private void InitializeCharacterRectangles()
        {
            var charCount = _config.DisplayableCharacters.Length;
            
            if (_config.UseVariableWidths)
            {
                // Variable width characters (like level numbers)
                _characterRectangles = new Rectangle[charCount];
                
                int currentX = 0;
                for (int charIndex = 0; charIndex < charCount; charIndex++)
                {
                    int sourceCharWidth = GetSourceCharacterWidth(charIndex);
                    
                    _characterRectangles[charIndex] = new Rectangle(
                        currentX, 
                        0, 
                        sourceCharWidth, 
                        _config.SourceCharacterHeight
                    );
                    
                    currentX += sourceCharWidth;
                }
            }
            else
            {
                // Fixed width characters (DTXMania console font pattern)
                _characterRectangles = new Rectangle[charCount];
                
                for (int charIndex = 0; charIndex < charCount; charIndex++)
                {
                    const int regionY = 16;
                    
                    // Calculate character position in texture (DTXMania formula)
                    var sourceWidth = _config.SourceCharacterWidths?[0] ?? 8;
                    var sourceHeight = _config.SourceCharacterHeight;
                    
                    _characterRectangles[charIndex] = new Rectangle(
                        (charIndex % regionY) * sourceWidth,
                        (charIndex / regionY) * sourceHeight,
                        sourceWidth,
                        sourceHeight
                    );
                }
            }
        }

        private void DrawCharacter(SpriteBatch spriteBatch, int charIndex, int x, int y, Color color, FontType fontType)
        {
            if (_characterRectangles == null || charIndex < 0 || charIndex >= _characterRectangles.Length)
                return;

            ITexture texture;
            Rectangle sourceRect;
            
            if (_config.UseVariableWidths)
            {
                // Variable width font (like level numbers)
                texture = _fontTextures?[0];
                sourceRect = _characterRectangles[charIndex];
            }
            else
            {
                // Fixed width font (DTXMania console font)
                int fontIndex = (int)fontType;
                texture = (fontIndex / 2 == 0) ? _fontTexture : _fontTexture2;
                sourceRect = _characterRectangles[charIndex];
            }
            
            if (texture?.Texture == null)
                return;

            var destWidth = GetCharacterWidth(charIndex);
            var destHeight = _config.CharacterHeight;
            var destRect = new Rectangle(x, y, destWidth, destHeight);
            
            spriteBatch.Draw(texture.Texture, destRect, sourceRect, color);
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
            if (!_disposed)
            {
                if (disposing)
                {
                    // Note: Don't dispose textures here as they're managed by ResourceManager
                    _fontTexture = null;
                    _fontTexture2 = null;
                    _fontTextures = null;
                    _characterRectangles = null;
                }
                _disposed = true;
            }
        }

        #endregion

        #region Enums

        /// <summary>
        /// Font types matching DTXMania's EFontType
        /// </summary>
        public enum FontType
        {
            Normal = 0,      // Regular font
            Thin = 1,        // Thin font
            WhiteThin = 2    // White thin font
        }

        #endregion
        
        #region Helper Methods
        
        /// <summary>
        /// Get the rendered width of a specific character
        /// </summary>
        /// <param name="charIndex">Index of character in DisplayableCharacters</param>
        /// <returns>Width in pixels</returns>
        private int GetCharacterWidth(int charIndex)
        {
            if (_config.UseVariableWidths && _config.CharacterWidths != null)
            {
                return charIndex < _config.CharacterWidths.Length ? _config.CharacterWidths[charIndex] : _config.CharacterWidths[0];
            }
            else
            {
                return _config.CharacterWidths?[0] ?? 8; // Default console font width
            }
        }
        
        /// <summary>
        /// Get the source texture width of a specific character
        /// </summary>
        /// <param name="charIndex">Index of character in DisplayableCharacters</param>
        /// <returns>Source width in pixels</returns>
        private int GetSourceCharacterWidth(int charIndex)
        {
            if (_config.UseVariableWidths && _config.SourceCharacterWidths != null)
            {
                return charIndex < _config.SourceCharacterWidths.Length ? _config.SourceCharacterWidths[charIndex] : _config.SourceCharacterWidths[0];
            }
            else
            {
                return _config.SourceCharacterWidths?[0] ?? 8; // Default console font width
            }
        }
        
        /// <summary>
        /// Create configuration for DTXMania console font
        /// </summary>
        public static BitmapFontConfig CreateConsoleFontConfig()
        {
            const string displayableChars = " !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~";
            
            return new BitmapFontConfig
            {
                DisplayableCharacters = displayableChars,
                CharacterWidths = new[] { 8 }, // Fixed 8px width
                CharacterHeight = 16,
                SourceCharacterWidths = new[] { 8 }, // Fixed 8px source width
                SourceCharacterHeight = 16,
                TexturePaths = new[] { TexturePath.ConsoleFont, TexturePath.ConsoleFontSecondary },
                UseVariableWidths = false
            };
        }
        
        /// <summary>
        /// Create configuration for level number font
        /// </summary>
        public static BitmapFontConfig CreateLevelNumberFontConfig()
        {
            const string displayableChars = "0123456789.";
            
            return new BitmapFontConfig
            {
                DisplayableCharacters = displayableChars,
                CharacterWidths = new[] { 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 9 }, // Numbers: 30px, Dot: 9px
                CharacterHeight = 38,
                SourceCharacterWidths = new[] { 100, 100, 100, 100, 100, 100, 100, 100, 100, 100, 30 }, // Numbers: 100px, Dot: 30px
                SourceCharacterHeight = 130,
                TexturePaths = new[] { TexturePath.LevelNumberFont },
                UseVariableWidths = true
            };
        }
        
        #endregion
    }
}
