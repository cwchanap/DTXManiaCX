using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Content;
using DTX.Resources;

namespace DTXMania.Game.Resources
{
    /// <summary>
    /// Shared font factory implementation that uses MonoGame SpriteFont
    /// Works across all platforms (Windows, Mac, etc.)
    /// </summary>
    public class SharedFontFactory : IFontFactory
    {
        private readonly ContentManager _contentManager;
        private SpriteFont _defaultFont;

        public SharedFontFactory(ContentManager contentManager)
        {
            _contentManager = contentManager ?? throw new System.ArgumentNullException(nameof(contentManager));
        }

        public IFont CreateFont(GraphicsDevice graphicsDevice, string fontPath, int size, FontStyle style = FontStyle.Regular)
        {
            // Try to load the default SpriteFont if we don't have one yet
            if (_defaultFont == null)
            {
                try
                {
                    _defaultFont = _contentManager.Load<SpriteFont>("NotoSerifJP");
                }
                catch (System.Exception ex)
                {
                    throw new System.NotSupportedException(
                        $"Cannot create font '{fontPath}' - failed to load default SpriteFont 'NotoSerifJP'. " +
                        "Please ensure NotoSerifJP.spritefont is built in your Content project. Error: " + ex.Message);
                }
            }

            return new SpriteFontManagedFont(_defaultFont, fontPath, size, style);
        }

        public IFont CreateFont(SpriteFont spriteFont, string sourcePath)
        {
            // Extract size from SpriteFont if possible, otherwise use default
            int size = (int)spriteFont.LineSpacing; // Approximate size from line spacing
            return new SpriteFontManagedFont(spriteFont, sourcePath, size, FontStyle.Regular);
        }
    }
}