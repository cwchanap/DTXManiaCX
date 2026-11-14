using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DTXMania.Game.Lib.Resources;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace DTXMania.Test.Helpers
{
    /// <summary>
    /// Builds a real SpriteFont over a 1x1 atlas. Device tests need a usable font but the
    /// Content Pipeline is unavailable in unit tests, so every printable ASCII character maps
    /// to the same one-pixel glyph with a two-pixel advance (width 1, right bearing 1). Callers
    /// own both the returned font and the texture it samples.
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

        /// <summary>
        /// Runs <paramref name="action"/> with ManagedFont's static font factory forced
        /// unavailable (ContentManager null, font cache emptied), so code under test takes its
        /// font-unavailable path. The lock and finally-based restore keep ManagedFont's private
        /// static state intact for other tests.
        /// </summary>
        internal static T WithManagedFontUnavailable<T>(Func<T> action)
        {
            var managedFontType = typeof(ManagedFont);
            var factoryLock = managedFontType
                .GetField("_fontFactoryLock", BindingFlags.Static | BindingFlags.NonPublic)!
                .GetValue(null)!;

            lock (factoryLock)
            {
                var contentManagerField = managedFontType.GetField("_contentManager", BindingFlags.Static | BindingFlags.NonPublic)!;
                var originalContentManager = contentManagerField.GetValue(null);

                var loadedFontsField = managedFontType.GetField("_loadedFonts", BindingFlags.Static | BindingFlags.NonPublic)!;
                var loadedFonts = (IDictionary)loadedFontsField.GetValue(null)!;
                var originalEntries = new DictionaryEntry[loadedFonts.Count];
                loadedFonts.CopyTo(originalEntries, 0);

                try
                {
                    contentManagerField.SetValue(null, null);
                    loadedFonts.Clear();
                    return action();
                }
                finally
                {
                    loadedFonts.Clear();
                    foreach (DictionaryEntry entry in originalEntries)
                    {
                        loadedFonts[entry.Key] = entry.Value;
                    }
                    contentManagerField.SetValue(null, originalContentManager);
                }
            }
        }
    }
}
