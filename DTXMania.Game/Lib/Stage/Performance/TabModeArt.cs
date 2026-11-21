#nullable enable

using System;
using System.Runtime.ExceptionServices;
using Microsoft.Xna.Framework;
using DTXMania.Game.Lib.Resources;

namespace DTXMania.Game.Lib.Stage.Performance
{
    /// <summary>
    /// Optional skin art for Tab mode, named by the active skin's Theme.ini:
    /// <c>Tab.StripTexture</c> (opaque strip backplate), <c>Tab.PanelTexture</c>
    /// (nine-slice HUD panel with quarter-size borders) and <c>Tab.HitLineTexture</c>
    /// (vertical hit-line beam). Each slot is independent: a missing key, a missing
    /// file or LoadTexture's 1x1 fallback leaves it null and the strip/HUD draw that
    /// part procedurally (in the theme palette) instead.
    /// The Tab playfield owns the instance and calls <see cref="Release"/> on teardown.
    /// </summary>
    public sealed class TabModeArt
    {
        public const string StripKey = "Tab.StripTexture";
        public const string PanelKey = "Tab.PanelTexture";
        public const string HitLineKey = "Tab.HitLineTexture";

        public static readonly TabModeArt None = new(null, null, null);

        private TabModeArt(ITexture? strip, ITexture? panel, ITexture? hitLine)
        {
            Strip = strip;
            Panel = panel;
            HitLine = hitLine;
        }

        public ITexture? Strip { get; }
        public ITexture? Panel { get; }
        public ITexture? HitLine { get; }

        public static TabModeArt Load(IResourceManager? resourceManager, ISkinTheme? theme)
        {
            if (resourceManager == null || theme == null)
                return None;

            ITexture? strip = null;
            ITexture? panel = null;
            try
            {
                strip = TryLoad(resourceManager, theme, StripKey);
                panel = TryLoad(resourceManager, theme, PanelKey);
                return new TabModeArt(strip, panel, TryLoad(resourceManager, theme, HitLineKey));
            }
            catch
            {
                try { strip?.RemoveReference(); }
                catch { }
                try { panel?.RemoveReference(); }
                catch { }
                throw;
            }
        }

        /// <summary>Drops the references taken by <see cref="Load"/>.</summary>
        public void Release()
        {
            Exception? firstFailure = null;
            void Release(ITexture? texture)
            {
                try { texture?.RemoveReference(); }
                catch (Exception ex) { firstFailure ??= ex; }
            }
            Release(Strip);
            Release(Panel);
            Release(HitLine);
            if (firstFailure != null) ExceptionDispatchInfo.Capture(firstFailure).Throw();
        }

        private static ITexture? TryLoad(IResourceManager resourceManager, ISkinTheme theme, string key)
        {
            var path = theme.GetString(key, string.Empty);
            if (string.IsNullOrWhiteSpace(path))
                return null;

            ITexture? texture;
            bool cached;
            try
            {
                cached = resourceManager.TryLoadTexture(path, out texture);
            }
            catch
            {
                return null;
            }

            if (texture == null)
                return null;
            try
            {
                var width = texture.Width;
                var height = texture.Height;
                if (width > 1 && height > 1)
                    return texture;

                // A 1x1 fallback is uncached; thin real textures are shared.
                var invalid = texture;
                texture = null;
                if (width == 1 && height == 1)
                    invalid.Dispose();
                else
                    invalid.RemoveReference();
            }
            catch
            {
                try
                {
                    if (cached) texture?.RemoveReference();
                    else texture?.Dispose();
                }
                catch { }
                throw;
            }
            return null;
        }

        /// <summary>
        /// Nine-slice patches for drawing a <paramref name="sourceWidth"/> x
        /// <paramref name="sourceHeight"/> panel texture with quarter-size borders
        /// (a quarter of the width on X, a quarter of the height on Y) into
        /// <paramref name="destination"/>: corners are drawn at
        /// <paramref name="destinationCorner"/> px (clamped to half the destination),
        /// edges stretch along one axis and the center along both. Writes nine
        /// row-major source/destination pairs.
        /// </summary>
        internal static void GetNineSlice(
            Rectangle destination,
            int sourceWidth,
            int sourceHeight,
            int destinationCorner,
            Span<Rectangle> sources,
            Span<Rectangle> destinations)
        {
            int sourceCornerX = sourceWidth / 4;
            int sourceCornerY = sourceHeight / 4;
            int corner = Math.Min(destinationCorner, Math.Min(destination.Width, destination.Height) / 2);

            Span<int> sx = stackalloc int[] { 0, sourceCornerX, sourceWidth - sourceCornerX, sourceWidth };
            Span<int> sy = stackalloc int[] { 0, sourceCornerY, sourceHeight - sourceCornerY, sourceHeight };
            Span<int> dx = stackalloc int[] { destination.Left, destination.Left + corner, destination.Right - corner, destination.Right };
            Span<int> dy = stackalloc int[] { destination.Top, destination.Top + corner, destination.Bottom - corner, destination.Bottom };

            for (int row = 0; row < 3; row++)
            {
                for (int col = 0; col < 3; col++)
                {
                    int i = row * 3 + col;
                    sources[i] = new Rectangle(sx[col], sy[row], sx[col + 1] - sx[col], sy[row + 1] - sy[row]);
                    destinations[i] = new Rectangle(dx[col], dy[row], dx[col + 1] - dx[col], dy[row + 1] - dy[row]);
                }
            }
        }
    }
}
