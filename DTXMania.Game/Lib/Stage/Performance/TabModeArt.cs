#nullable enable

using System;
using Microsoft.Xna.Framework;
using DTXMania.Game.Lib.Resources;

namespace DTXMania.Game.Lib.Stage.Performance
{
    /// <summary>
    /// Optional skin art for Tab mode, named by the active skin's Theme.ini:
    /// <c>Tab.StripTexture</c> (opaque strip backplate), <c>Tab.PanelTexture</c>
    /// (nine-slice HUD panel with quarter-size corners) and <c>Tab.HitLineTexture</c>
    /// (vertical hit-line beam). Each slot is independent: a missing key, a missing
    /// file or LoadTexture's 1x1 fallback leaves it null and the strip/HUD keep their
    /// procedural drawing, so skins without Tab art look exactly as before.
    /// The stage owns the instance and calls <see cref="Release"/> on teardown.
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

            return new TabModeArt(
                TryLoad(resourceManager, theme, StripKey),
                TryLoad(resourceManager, theme, PanelKey),
                TryLoad(resourceManager, theme, HitLineKey));
        }

        /// <summary>Drops the references taken by <see cref="Load"/>.</summary>
        public void Release()
        {
            Strip?.RemoveReference();
            Panel?.RemoveReference();
            HitLine?.RemoveReference();
        }

        private static ITexture? TryLoad(IResourceManager resourceManager, ISkinTheme theme, string key)
        {
            var path = theme.GetString(key, string.Empty);
            if (string.IsNullOrWhiteSpace(path))
                return null;

            ITexture? texture;
            try
            {
                texture = resourceManager.LoadTexture(path);
            }
            catch
            {
                return null;
            }

            if (texture != null && texture.Width > 1 && texture.Height > 1)
                return texture;

            // LoadTexture's 1x1 fallback is uncached, so disposing it is the only
            // way to release its GPU texture (same rule as the skill-panel loader).
            texture?.Dispose();
            return null;
        }

        /// <summary>
        /// Nine-slice patches for drawing a <paramref name="sourceWidth"/> x
        /// <paramref name="sourceHeight"/> panel texture with quarter-width corners
        /// into <paramref name="destination"/>: corners keep their aspect at
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
            int sourceCorner = sourceWidth / 4;
            int corner = Math.Min(destinationCorner, Math.Min(destination.Width, destination.Height) / 2);

            Span<int> sx = stackalloc int[] { 0, sourceCorner, sourceWidth - sourceCorner, sourceWidth };
            Span<int> sy = stackalloc int[] { 0, sourceCorner, sourceHeight - sourceCorner, sourceHeight };
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
