using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace DTXMania.Game.Lib.Resources
{
    /// <summary>
    /// Texture abstraction interface for DTXManiaCX
    /// Wraps MonoGame Texture2D with reference counting and disposal tracking
    /// Based on DTXMania's CTexture patterns
    /// </summary>
    public interface ITexture : IDisposable
    {
        #region Properties

        /// <summary>
        /// Underlying MonoGame Texture2D
        /// </summary>
        Texture2D Texture { get; }

        /// <summary>
        /// Source path used to load this texture
        /// </summary>
        string SourcePath { get; }

        /// <summary>
        /// Texture width in pixels
        /// </summary>
        int Width { get; }

        /// <summary>
        /// Texture height in pixels
        /// </summary>
        int Height { get; }

        /// <summary>
        /// Texture size as Vector2
        /// </summary>
        Vector2 Size { get; }

        /// <summary>
        /// Whether this texture is disposed
        /// </summary>
        bool IsDisposed { get; }

        /// <summary>
        /// Current reference count
        /// </summary>
        int ReferenceCount { get; }

        /// <summary>
        /// Memory usage in bytes (estimated)
        /// </summary>
        long MemoryUsage { get; }

        /// <summary>
        /// Transparency setting (0-255)
        /// Based on DTXMania's nTransparency pattern
        /// </summary>
        int Transparency { get; set; }

        /// <summary>
        /// Scale ratio for rendering
        /// Based on DTXMania's vcScaleRatio pattern
        /// </summary>
        Vector3 ScaleRatio { get; set; }

        /// <summary>
        /// Z-axis rotation in radians
        /// Based on DTXMania's fZAxisRotation pattern
        /// </summary>
        float ZAxisRotation { get; set; }

        /// <summary>
        /// Enable additive blending
        /// Based on DTXMania's bAdditiveBlending pattern
        /// </summary>
        bool AdditiveBlending { get; set; }

        #endregion

        #region Reference Counting

        /// <summary>
        /// Add a reference to this texture
        /// Prevents disposal while references exist
        /// </summary>
        void AddReference();

        /// <summary>
        /// Remove a reference from this texture
        /// Disposes when reference count reaches zero
        /// </summary>
        void RemoveReference();

        #endregion

        #region Drawing Methods

        /// <summary>
        /// Draw texture at specified position
        /// Based on DTXMania's tDraw2D patterns
        /// </summary>
        /// <param name="spriteBatch">SpriteBatch for rendering</param>
        /// <param name="position">Position to draw at</param>
        void Draw(SpriteBatch spriteBatch, Vector2 position);

        /// <summary>
        /// Draw texture with source rectangle
        /// </summary>
        /// <param name="spriteBatch">SpriteBatch for rendering</param>
        /// <param name="position">Position to draw at</param>
        /// <param name="sourceRectangle">Source rectangle within texture</param>
        void Draw(SpriteBatch spriteBatch, Vector2 position, Rectangle? sourceRectangle);

        /// <summary>
        /// Draw texture with full parameters
        /// </summary>
        /// <param name="spriteBatch">SpriteBatch for rendering</param>
        /// <param name="destinationRectangle">Destination rectangle</param>
        /// <param name="sourceRectangle">Source rectangle within texture</param>
        /// <param name="color">Tint color</param>
        /// <param name="rotation">Rotation in radians</param>
        /// <param name="origin">Origin point for rotation</param>
        /// <param name="effects">Sprite effects</param>
        /// <param name="layerDepth">Layer depth (0.0f to 1.0f)</param>
        void Draw(SpriteBatch spriteBatch, Rectangle destinationRectangle, Rectangle? sourceRectangle, 
                 Color color, float rotation, Vector2 origin, SpriteEffects effects, float layerDepth);

        /// <summary>
        /// Draw texture with position, scale, and rotation
        /// Based on DTXMania's transformation patterns
        /// </summary>
        /// <param name="spriteBatch">SpriteBatch for rendering</param>
        /// <param name="position">Position to draw at</param>
        /// <param name="scale">Scale factor</param>
        /// <param name="rotation">Rotation in radians</param>
        /// <param name="origin">Origin point for rotation</param>
        void Draw(SpriteBatch spriteBatch, Vector2 position, Vector2 scale, float rotation, Vector2 origin);

        #endregion

        #region Utility Methods

        /// <summary>
        /// Create a copy of this texture
        /// Useful for creating modified versions
        /// </summary>
        /// <returns>New texture instance</returns>
        ITexture Clone();

        /// <summary>
        /// Get color data from texture
        /// </summary>
        /// <returns>Array of color data</returns>
        Color[] GetColorData();

        /// <summary>
        /// Set color data for texture
        /// </summary>
        /// <param name="colorData">Color data to set</param>
        void SetColorData(Color[] colorData);

        /// <summary>
        /// Save texture to file
        /// </summary>
        /// <param name="filePath">Path to save to</param>
        void SaveToFile(string filePath);

        #endregion
    }

    /// <summary>
    /// Texture creation parameters
    /// Based on DTXMania's texture creation patterns
    /// </summary>
    public class TextureCreationParams
    {
        public bool EnableTransparency { get; set; } = false;
        public Color TransparencyColor { get; set; } = Color.Black;
        public bool GenerateMipmaps { get; set; } = false;
        public SurfaceFormat Format { get; set; } = SurfaceFormat.Color;
        public bool PremultiplyAlpha { get; set; } = true;
        public TextureFilter Filter { get; set; } = TextureFilter.Linear;
    }

    /// <summary>
    /// Texture loading exception
    /// Based on DTXMania's CTextureCreateFailedException
    /// </summary>
    public class TextureLoadException : Exception
    {
        public string TexturePath { get; }

        public TextureLoadException(string texturePath, string message) : base(message)
        {
            TexturePath = texturePath;
        }

        public TextureLoadException(string texturePath, string message, Exception innerException) 
            : base(message, innerException)
        {
            TexturePath = texturePath;
        }
    }
}
