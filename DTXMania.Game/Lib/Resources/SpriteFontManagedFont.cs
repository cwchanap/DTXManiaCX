using Microsoft.Xna.Framework.Graphics;
using System;

namespace DTX.Resources
{
    /// <summary>
    /// Simple SpriteFont-only implementation of ManagedFont
    /// Used as a replacement for WindowsManagedFont to always use MonoGame SpriteFont
    /// No custom font loading - purely SpriteFont based
    /// </summary>
    public class SpriteFontManagedFont : ManagedFont
    {
        /// <summary>
        /// Create font from SpriteFont instance
        /// </summary>
        public SpriteFontManagedFont(SpriteFont spriteFont, string sourcePath, int size, FontStyle style = FontStyle.Regular)
            : base(spriteFont, sourcePath, size, style)
        {
        }

        /// <summary>
        /// This implementation doesn't support loading from paths - only from existing SpriteFonts
        /// </summary>
        protected override void LoadFont(GraphicsDevice graphicsDevice, string fontPath, int size, FontStyle style)
        {
            // This implementation doesn't support dynamic font loading
            // It only works with pre-existing SpriteFont instances
            throw new NotSupportedException(
                "SpriteFontManagedFont only supports existing SpriteFont instances. " +
                "Use the constructor that takes a SpriteFont parameter instead.");
        }
    }
}
