using Microsoft.Xna.Framework.Graphics;

namespace DTXMania.Game.Lib.Resources
{
    /// <summary>
    /// Factory interface for creating platform-specific font implementations
    /// </summary>
    public interface IFontFactory
    {
        /// <summary>
        /// Create a font from system font name or font file path
        /// </summary>
        /// <param name="graphicsDevice">Graphics device for font creation</param>
        /// <param name="fontPath">Path to font file or system font name</param>
        /// <param name="size">Font size in points</param>
        /// <param name="style">Font style</param>
        /// <returns>Platform-specific font implementation</returns>
        IFont CreateFont(GraphicsDevice graphicsDevice, string fontPath, int size, FontStyle style = FontStyle.Regular);

        /// <summary>
        /// Create a font from existing SpriteFont
        /// </summary>
        /// <param name="spriteFont">Existing SpriteFont instance</param>
        /// <param name="sourcePath">Source path for reference</param>
        /// <returns>Platform-specific font implementation</returns>
        IFont CreateFont(SpriteFont spriteFont, string sourcePath);
    }
}
