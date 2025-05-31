using Microsoft.Xna.Framework.Graphics;
using DTX.Resources;

namespace DTXMania.Windows.Resources
{
    /// <summary>
    /// Windows-specific font factory implementation
    /// </summary>
    public class WindowsFontFactory : IFontFactory
    {
        public IFont CreateFont(GraphicsDevice graphicsDevice, string fontPath, int size, FontStyle style = FontStyle.Regular)
        {
            return new WindowsManagedFont(graphicsDevice, fontPath, size, style);
        }

        public IFont CreateFont(SpriteFont spriteFont, string sourcePath)
        {
            // Extract size from SpriteFont if possible, otherwise use default
            int size = (int)spriteFont.LineSpacing; // Approximate size from line spacing
            return new WindowsManagedFont(spriteFont, sourcePath, size, FontStyle.Regular);
        }
    }
}
