using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace DTXMania.Test.Helpers
{
    /// <summary>
    /// Builds a real SpriteFont over a 1x1 atlas. Device tests need a usable font but the
    /// Content Pipeline is unavailable in unit tests, so every printable ASCII character maps
    /// to the same one-pixel glyph with a one-pixel advance. Callers own both the returned
    /// font and the texture it samples.
    /// </summary>
    public static class TestFontFactory
    {
        public static SpriteFont Create(GraphicsDevice graphicsDevice, out Texture2D texture, int lineSpacing = 38)
        {
            texture = new Texture2D(graphicsDevice, 1, 1);
            texture.SetData(new[] { Color.White });

            var characters = new List<char>();
            var glyphBounds = new List<Rectangle>();
            var cropping = new List<Rectangle>();
            var kerning = new List<Vector3>();

            // SpriteFont requires an ascending character map.
            for (var c = ' '; c <= '~'; c++)
            {
                characters.Add(c);
                glyphBounds.Add(new Rectangle(0, 0, 1, 1));
                cropping.Add(new Rectangle(0, 0, 1, 1));
                kerning.Add(new Vector3(0f, 1f, 1f));
            }

            return new SpriteFont(texture, glyphBounds, cropping, characters, lineSpacing, 0f, kerning, '?');
        }
    }
}
