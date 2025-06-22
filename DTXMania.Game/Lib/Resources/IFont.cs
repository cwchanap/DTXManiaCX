using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace DTX.Resources
{
    /// <summary>
    /// Font abstraction interface for DTXManiaCX
    /// Wraps MonoGame SpriteFont with advanced rendering capabilities
    /// Based on DTXMania's CPrivateFont patterns with Japanese support
    /// </summary>
    public interface IFont : IDisposable
    {
        #region Properties

        /// <summary>
        /// Underlying MonoGame SpriteFont (may be null for bitmap fonts)
        /// </summary>
        SpriteFont SpriteFont { get; }

        /// <summary>
        /// Source path used to load this font
        /// </summary>
        string SourcePath { get; }

        /// <summary>
        /// Font size in pixels
        /// </summary>
        int Size { get; }

        /// <summary>
        /// Font style
        /// </summary>
        FontStyle Style { get; }

        /// <summary>
        /// Whether this font is disposed
        /// </summary>
        bool IsDisposed { get; }

        /// <summary>
        /// Current reference count
        /// </summary>
        int ReferenceCount { get; }

        /// <summary>
        /// Line spacing for this font
        /// </summary>
        float LineSpacing { get; }

        /// <summary>
        /// Default character for missing glyphs
        /// </summary>
        char DefaultCharacter { get; set; }

        /// <summary>
        /// Whether this font supports the specified character
        /// Important for Japanese character support
        /// </summary>
        /// <param name="character">Character to check</param>
        /// <returns>True if character is supported</returns>
        bool SupportsCharacter(char character);

        #endregion

        #region Reference Counting

        /// <summary>
        /// Add a reference to this font
        /// </summary>
        void AddReference();

        /// <summary>
        /// Remove a reference from this font
        /// </summary>
        void RemoveReference();

        #endregion

        #region Text Measurement

        /// <summary>
        /// Measure the size of text when rendered with this font
        /// </summary>
        /// <param name="text">Text to measure</param>
        /// <returns>Size of rendered text</returns>
        Vector2 MeasureString(string text);

        /// <summary>
        /// Measure text with word wrapping
        /// </summary>
        /// <param name="text">Text to measure</param>
        /// <param name="maxWidth">Maximum width before wrapping</param>
        /// <returns>Size of wrapped text</returns>
        Vector2 MeasureStringWrapped(string text, float maxWidth);

        /// <summary>
        /// Get the bounds of individual characters in text
        /// Useful for advanced text layout
        /// </summary>
        /// <param name="text">Text to analyze</param>
        /// <returns>Array of character bounds</returns>
        Rectangle[] GetCharacterBounds(string text);

        #endregion

        #region Text Rendering

        /// <summary>
        /// Draw text at specified position
        /// </summary>
        /// <param name="spriteBatch">SpriteBatch for rendering</param>
        /// <param name="text">Text to draw</param>
        /// <param name="position">Position to draw at</param>
        /// <param name="color">Text color</param>
        void DrawString(SpriteBatch spriteBatch, string text, Vector2 position, Color color);

        /// <summary>
        /// Draw text with full parameters
        /// </summary>
        /// <param name="spriteBatch">SpriteBatch for rendering</param>
        /// <param name="text">Text to draw</param>
        /// <param name="position">Position to draw at</param>
        /// <param name="color">Text color</param>
        /// <param name="rotation">Rotation in radians</param>
        /// <param name="origin">Origin point for rotation</param>
        /// <param name="scale">Scale factor</param>
        /// <param name="effects">Sprite effects</param>
        /// <param name="layerDepth">Layer depth</param>
        void DrawString(SpriteBatch spriteBatch, string text, Vector2 position, Color color,
                       float rotation, Vector2 origin, Vector2 scale, SpriteEffects effects, float layerDepth);

        /// <summary>
        /// Draw text with outline/edge effect
        /// Based on DTXMania's edge rendering patterns
        /// </summary>
        /// <param name="spriteBatch">SpriteBatch for rendering</param>
        /// <param name="text">Text to draw</param>
        /// <param name="position">Position to draw at</param>
        /// <param name="textColor">Main text color</param>
        /// <param name="outlineColor">Outline color</param>
        /// <param name="outlineThickness">Outline thickness in pixels</param>
        void DrawStringWithOutline(SpriteBatch spriteBatch, string text, Vector2 position,
                                  Color textColor, Color outlineColor, int outlineThickness = 1);

        /// <summary>
        /// Draw text with gradient effect
        /// Based on DTXMania's gradation patterns
        /// </summary>
        /// <param name="spriteBatch">SpriteBatch for rendering</param>
        /// <param name="text">Text to draw</param>
        /// <param name="position">Position to draw at</param>
        /// <param name="topColor">Top gradient color</param>
        /// <param name="bottomColor">Bottom gradient color</param>
        void DrawStringWithGradient(SpriteBatch spriteBatch, string text, Vector2 position,
                                   Color topColor, Color bottomColor);

        /// <summary>
        /// Draw text with shadow effect
        /// </summary>
        /// <param name="spriteBatch">SpriteBatch for rendering</param>
        /// <param name="text">Text to draw</param>
        /// <param name="position">Position to draw at</param>
        /// <param name="textColor">Main text color</param>
        /// <param name="shadowColor">Shadow color</param>
        /// <param name="shadowOffset">Shadow offset</param>
        void DrawStringWithShadow(SpriteBatch spriteBatch, string text, Vector2 position,
                                 Color textColor, Color shadowColor, Vector2 shadowOffset);

        /// <summary>
        /// Draw wrapped text within specified bounds
        /// </summary>
        /// <param name="spriteBatch">SpriteBatch for rendering</param>
        /// <param name="text">Text to draw</param>
        /// <param name="bounds">Bounds to draw within</param>
        /// <param name="color">Text color</param>
        /// <param name="alignment">Text alignment</param>
        void DrawStringWrapped(SpriteBatch spriteBatch, string text, Rectangle bounds,
                              Color color, TextAlignment alignment = TextAlignment.Left);

        #endregion

        #region Advanced Features

        /// <summary>
        /// Create a texture containing rendered text
        /// Useful for caching complex text rendering
        /// </summary>
        /// <param name="graphicsDevice">Graphics device</param>
        /// <param name="text">Text to render</param>
        /// <param name="color">Text color</param>
        /// <returns>Texture containing rendered text</returns>
        ITexture CreateTextTexture(GraphicsDevice graphicsDevice, string text, Color color);

        /// <summary>
        /// Create a texture with advanced text effects
        /// Based on DTXMania's DrawPrivateFont patterns
        /// </summary>
        /// <param name="graphicsDevice">Graphics device</param>
        /// <param name="text">Text to render</param>
        /// <param name="options">Text rendering options</param>
        /// <returns>Texture containing rendered text</returns>
        ITexture CreateTextTexture(GraphicsDevice graphicsDevice, string text, TextRenderOptions options);

        /// <summary>
        /// Get kerning information between two characters
        /// </summary>
        /// <param name="first">First character</param>
        /// <param name="second">Second character</param>
        /// <returns>Kerning offset</returns>
        Vector2 GetKerning(char first, char second);

        #endregion
    }

    /// <summary>
    /// Text alignment options
    /// </summary>
    public enum TextAlignment
    {
        Left,
        Center,
        Right,
        Justify
    }

    /// <summary>
    /// Text rendering options
    /// Based on DTXMania's DrawMode patterns
    /// </summary>
    public class TextRenderOptions
    {
        public Color TextColor { get; set; } = Color.White;
        public bool EnableOutline { get; set; } = false;
        public Color OutlineColor { get; set; } = Color.Black;
        public int OutlineThickness { get; set; } = 1;
        public bool EnableGradient { get; set; } = false;
        public Color GradientTopColor { get; set; } = Color.White;
        public Color GradientBottomColor { get; set; } = Color.Gray;
        public bool EnableShadow { get; set; } = false;
        public Color ShadowColor { get; set; } = Color.Black;
        public Vector2 ShadowOffset { get; set; } = new Vector2(2, 2);
        public float Scale { get; set; } = 1.0f;
        public float Rotation { get; set; } = 0.0f;
        public TextAlignment Alignment { get; set; } = TextAlignment.Left;
        public float MaxWidth { get; set; } = float.MaxValue;
        public bool WordWrap { get; set; } = false;
    }

    /// <summary>
    /// Font loading exception
    /// </summary>
    public class FontLoadException : Exception
    {
        public string FontPath { get; }

        public FontLoadException(string fontPath, string message) : base(message)
        {
            FontPath = fontPath;
        }

        public FontLoadException(string fontPath, string message, Exception innerException)
            : base(message, innerException)
        {
            FontPath = fontPath;
        }
    }
}
