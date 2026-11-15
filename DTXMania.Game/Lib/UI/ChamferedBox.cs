#nullable enable

using System.Diagnostics.CodeAnalysis;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace DTXMania.Game.Lib.UI
{
    /// <summary>
    /// The CX Neon panel shape: a box with 45° corner cuts, drawn from 1px white-pixel
    /// primitives so it needs no art. The fill steps in one pixel per row inside each cut,
    /// and the 1px border runs the four edges plus the four diagonals. Shared by the tab
    /// HUD and the Drum Key Mapping page.
    /// </summary>
    public static class ChamferedBox
    {
        /// <summary>
        /// Draws the box. <paramref name="chamfer"/> is the corner cut in pixels; a transparent
        /// <paramref name="fill"/> or <paramref name="border"/> skips that pass. Depths only
        /// matter for depth-sorted batches (the tab HUD); deferred batches ignore them.
        /// </summary>
        [ExcludeFromCodeCoverage]
        public static void Draw(
            SpriteBatch spriteBatch,
            Texture2D whitePixel,
            Rectangle r,
            int chamfer,
            Color fill,
            Color border,
            float fillDepth = 0f,
            float borderDepth = 0f)
        {
            int c = System.Math.Clamp(chamfer, 0, System.Math.Min(r.Width, r.Height) / 2);

            if (fill.A > 0)
            {
                Fill(spriteBatch, whitePixel, new Rectangle(r.X, r.Y + c, r.Width, r.Height - 2 * c), fill, fillDepth);
                for (int i = 0; i < c; i++)
                {
                    int inset = c - i;
                    Fill(spriteBatch, whitePixel, new Rectangle(r.X + inset, r.Y + i, r.Width - 2 * inset, 1), fill, fillDepth);
                    Fill(spriteBatch, whitePixel, new Rectangle(r.X + inset, r.Bottom - 1 - i, r.Width - 2 * inset, 1), fill, fillDepth);
                }
            }

            if (border.A == 0)
                return;

            // HPA-796: a zero chamfer is an ordinary rectangle border (overlapping corners);
            // only the chamfered diagonals below stay Draw-specific.
            if (c == 0)
            {
                Border(spriteBatch, whitePixel, r, 1, border, borderDepth);
                return;
            }

            Fill(spriteBatch, whitePixel, new Rectangle(r.X + c, r.Y, r.Width - 2 * c, 1), border, borderDepth);
            Fill(spriteBatch, whitePixel, new Rectangle(r.X + c, r.Bottom - 1, r.Width - 2 * c, 1), border, borderDepth);
            Fill(spriteBatch, whitePixel, new Rectangle(r.X, r.Y + c, 1, r.Height - 2 * c), border, borderDepth);
            Fill(spriteBatch, whitePixel, new Rectangle(r.Right - 1, r.Y + c, 1, r.Height - 2 * c), border, borderDepth);
            for (int i = 0; i < c; i++)
            {
                Fill(spriteBatch, whitePixel, new Rectangle(r.X + i, r.Y + c - 1 - i, 1, 1), border, borderDepth);
                Fill(spriteBatch, whitePixel, new Rectangle(r.Right - 1 - i, r.Y + c - 1 - i, 1, 1), border, borderDepth);
                Fill(spriteBatch, whitePixel, new Rectangle(r.X + i, r.Bottom - c + i, 1, 1), border, borderDepth);
                Fill(spriteBatch, whitePixel, new Rectangle(r.Right - 1 - i, r.Bottom - c + i, 1, 1), border, borderDepth);
            }
        }

        /// <summary>
        /// Draws a plain filled rectangle from the white pixel. Public so callers with
        /// matching fill geometry reuse it instead of inlining their own primitive.
        /// </summary>
        [ExcludeFromCodeCoverage]
        public static void Fill(SpriteBatch spriteBatch, Texture2D whitePixel, Rectangle rect, Color color, float depth)
        {
            spriteBatch.Draw(whitePixel, rect, null, color, 0f, Vector2.Zero, SpriteEffects.None, depth);
        }

        /// <summary>
        /// Draws a rectangular border of <paramref name="thickness"/> pixels with overlapping
        /// corners (the verticals span the full height, the horizontals the full width), the
        /// shared geometry of the former per-renderer border helpers. A transparent
        /// <paramref name="color"/> skips the draw.
        /// </summary>
        [ExcludeFromCodeCoverage]
        public static void Border(
            SpriteBatch spriteBatch,
            Texture2D whitePixel,
            Rectangle r,
            int thickness,
            Color color,
            float depth = 0f)
        {
            if (color.A == 0)
                return;

            foreach (var edge in BorderRects(r, thickness))
                Fill(spriteBatch, whitePixel, edge, color, depth);
        }

        /// <summary>Exact edge rectangles of <see cref="Border"/>: top, bottom, left, right.</summary>
        internal static Rectangle[] BorderRects(Rectangle r, int thickness)
        {
            int t = System.Math.Max(1, thickness);
            return new[]
            {
                new Rectangle(r.X, r.Y, r.Width, t),
                new Rectangle(r.X, r.Bottom - t, r.Width, t),
                new Rectangle(r.X, r.Y, t, r.Height),
                new Rectangle(r.Right - t, r.Y, t, r.Height),
            };
        }
    }
}
