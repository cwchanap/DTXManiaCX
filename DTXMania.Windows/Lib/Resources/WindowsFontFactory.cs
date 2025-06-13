using Microsoft.Xna.Framework.Graphics;
using DTX.Resources;

namespace DTXMania.Windows.Resources
{
    /// <summary>
    /// Simplified font factory implementation that only uses MonoGame SpriteFont
    /// No longer uses Windows-specific font loading - always defaults to SpriteFont
    /// </summary>
    public class WindowsFontFactory : IFontFactory
    {
        public IFont CreateFont(GraphicsDevice graphicsDevice, string fontPath, int size, FontStyle style = FontStyle.Regular)
        {
            // For now, we'll create a basic SpriteFont with minimal fallback
            // In a real implementation, this would load a default SpriteFont from Content Pipeline
            var defaultSpriteFont = CreateDefaultSpriteFont(graphicsDevice, size);
            if (defaultSpriteFont != null)
            {
                return new SpriteFontManagedFont(defaultSpriteFont, fontPath, size, style);
            }

            // If no SpriteFont is available, throw an exception indicating content pipeline setup needed
            throw new System.NotSupportedException(
                $"Cannot create font '{fontPath}' - SpriteFont-only implementation requires " +
                "fonts to be built through the Content Pipeline. Please add .spritefont files to your Content project.");
        }

        public IFont CreateFont(SpriteFont spriteFont, string sourcePath)
        {
            // Extract size from SpriteFont if possible, otherwise use default
            int size = (int)spriteFont.LineSpacing; // Approximate size from line spacing
            return new SpriteFontManagedFont(spriteFont, sourcePath, size, FontStyle.Regular);
        }

        /// <summary>
        /// Creates a default SpriteFont when no specific font is available
        /// This would normally load from Content Pipeline, but for now returns null
        /// </summary>
        private SpriteFont CreateDefaultSpriteFont(GraphicsDevice graphicsDevice, int size)
        {
            // In a real implementation, this would load a default SpriteFont from Content Pipeline
            // For now, we'll return null to force users to set up proper SpriteFont content
            return null;
        }
    }
}
