using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using DTX.Resources;
using System;

namespace DTX.Resources
{
    /// <summary>
    /// DTXMania-style bitmap font renderer using console font textures
    /// Based on CCharacterConsole from DTXManiaNX
    /// </summary>
    public class BitmapFont : IDisposable
    {
        #region Constants

        // DTXMania font constants
        private const int FontWidth = 8;
        private const int FontHeight = 16;
        private const string DisplayableCharacters = " !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~ ";

        #endregion

        #region Fields

        private readonly GraphicsDevice _graphicsDevice;
        private ITexture _fontTexture;
        private ITexture _fontTexture2;
        private Rectangle[,] _characterRectangles;
        private bool _disposed = false;

        #endregion

        #region Properties

        public int CharacterWidth => FontWidth;
        public int CharacterHeight => FontHeight;
        public bool IsLoaded => _fontTexture != null;

        #endregion

        #region Constructor

        public BitmapFont(GraphicsDevice graphicsDevice, IResourceManager resourceManager)
        {
            _graphicsDevice = graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
            
            LoadFontTextures(resourceManager);
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
        /// <param name="fontType">Font type (0 = normal, 1 = thin, 2 = white thin)</param>
        public void DrawText(SpriteBatch spriteBatch, string text, int x, int y, FontType fontType = FontType.Normal)
        {
            if (_disposed || string.IsNullOrEmpty(text) || !IsLoaded)
                return;

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
                    currentY += FontHeight;
                }
                else
                {
                    int charIndex = DisplayableCharacters.IndexOf(ch);
                    if (charIndex < 0)
                    {
                        // Character not found, just advance position
                        currentX += FontWidth;
                    }
                    else
                    {
                        // Draw the character
                        DrawCharacter(spriteBatch, charIndex, currentX, currentY, fontType);
                        currentX += FontWidth;
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
                    currentWidth += FontWidth;
                }
            }

            maxWidth = Math.Max(maxWidth, currentWidth);
            return new Vector2(maxWidth, lines * FontHeight);
        }

        #endregion

        #region Private Methods

        private void LoadFontTextures(IResourceManager resourceManager)
        {
            try
            {
                // Load console font textures (DTXMania pattern)
                _fontTexture = resourceManager.LoadTexture("Graphics/Console font 8x16.png");
                _fontTexture2 = resourceManager.LoadTexture("Graphics/Console font 2 8x16.png");
                
                System.Diagnostics.Debug.WriteLine("Loaded bitmap font textures successfully");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load bitmap font textures: {ex.Message}");
                // Font textures are optional - we'll just not render text if they fail
            }
        }

        private void InitializeCharacterRectangles()
        {
            // Initialize character rectangles following DTXMania pattern
            _characterRectangles = new Rectangle[3, DisplayableCharacters.Length];
            
            for (int fontType = 0; fontType < 3; fontType++)
            {
                for (int charIndex = 0; charIndex < DisplayableCharacters.Length; charIndex++)
                {
                    const int regionX = 128;
                    const int regionY = 16;
                    
                    // Calculate character position in texture (DTXMania formula)
                    _characterRectangles[fontType, charIndex].X = ((fontType / 2) * regionX) + ((charIndex % regionY) * FontWidth);
                    _characterRectangles[fontType, charIndex].Y = ((fontType % 2) * regionX) + ((charIndex / regionY) * FontHeight);
                    _characterRectangles[fontType, charIndex].Width = FontWidth;
                    _characterRectangles[fontType, charIndex].Height = FontHeight;
                }
            }
        }

        private void DrawCharacter(SpriteBatch spriteBatch, int charIndex, int x, int y, FontType fontType)
        {
            if (_characterRectangles == null)
                return;

            int fontIndex = (int)fontType;
            ITexture texture = (fontIndex / 2 == 0) ? _fontTexture : _fontTexture2;
            
            if (texture?.Texture == null)
                return;

            var sourceRect = _characterRectangles[fontIndex % 3, charIndex];
            var destRect = new Rectangle(x, y, FontWidth, FontHeight);
            
            spriteBatch.Draw(texture.Texture, destRect, sourceRect, Color.White);
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
    }
}
