#nullable enable

using System.Collections.Generic;
using Microsoft.Xna.Framework;
using DTXMania.Game.Lib.UI.Layout;

namespace DTXMania.Game.Lib.Stage.DrumConfig
{
    /// <summary>
    /// One clickable kit piece, mapped 1:1 to a drum lane (0-9). <see cref="Bounds"/> is the
    /// screen-space box (1280x720 virtual space) around the piece as it appears in the kit
    /// illustration drawn into <see cref="DrumConfigUILayout.KitArt"/>. Cymbals and pedals click
    /// on the ellipse inscribed in the box; drums (<see cref="CapHeight"/> &gt; 0) click on a
    /// cylinder silhouette: straight sides with elliptical caps of that half-height at the top
    /// and bottom, so the shell's corners count too. The lane tag sits at
    /// <see cref="TagOffsetX"/>/<see cref="TagOffsetY"/> from the box center.
    /// </summary>
    public readonly struct DrumKitPiece
    {
        public int Lane { get; }
        public Rectangle Bounds { get; }
        public int TagOffsetX { get; }
        public int TagOffsetY { get; }
        /// <summary>Half-height of a drum's elliptical top/bottom caps; 0 for an ellipse.</summary>
        public int CapHeight { get; }

        public DrumKitPiece(int lane, int centerX, int centerY, int width, int height,
            int tagOffsetX, int tagOffsetY, int capHeight = 0)
        {
            Lane = lane;
            Bounds = new Rectangle(centerX - (width / 2), centerY - (height / 2), width, height);
            TagOffsetX = tagOffsetX;
            TagOffsetY = tagOffsetY;
            CapHeight = capHeight;
        }

        /// <summary>Lane-code tag rectangle (e.g. "SN"); its top-center sits at the tag offset.</summary>
        public Rectangle TagRect => new(
            Bounds.Center.X + TagOffsetX - (DrumConfigUILayout.PieceTagWidth / 2),
            Bounds.Center.Y + TagOffsetY,
            DrumConfigUILayout.PieceTagWidth,
            DrumConfigUILayout.PieceTagHeight);

        /// <summary>Whether the screen-space point is on the piece's click shape.</summary>
        public bool Contains(float x, float y)
        {
            if (CapHeight <= 0)
                return NormalizedDistance(x, y) <= 1f;

            float rx = Bounds.Width / 2f;
            float dx = (x - (Bounds.X + rx)) / rx;
            if (dx * dx > 1f)
                return false;
            float capTop = Bounds.Y + CapHeight;
            float capBottom = Bounds.Bottom - CapHeight;
            if (y >= capTop && y <= capBottom)
                return true;
            float dy = (y - (y < capTop ? capTop : capBottom)) / CapHeight;
            return (dx * dx) + (dy * dy) <= 1f;
        }

        /// <summary>
        /// Normalized ellipse distance of the point from the piece center (&lt;= 1 is inside the
        /// inscribed ellipse). Used to pick the nearest piece where two click shapes overlap.
        /// </summary>
        public float NormalizedDistance(float x, float y)
        {
            float rx = Bounds.Width / 2f;
            float ry = Bounds.Height / 2f;
            float dx = (x - (Bounds.X + rx)) / rx;
            float dy = (y - (Bounds.Y + ry)) / ry;
            return (dx * dx) + (dy * dy);
        }
    }

    /// <summary>
    /// Hotspots over the kit illustration (<see cref="Resources.TexturePath.DrumKitSet"/>), in
    /// lane order (LC..RD). The illustration shows a whole kit from the drummer's seat, so the
    /// boxes follow where each piece sits in that picture.
    /// </summary>
    public static class DrumKitLayout
    {
        public static IReadOnlyList<DrumKitPiece> Pieces { get; } = new[]
        {
            // Centers and boxes trace drumkit_set.png drawn at KitArt; tags sit above the
            // cymbals and on the drum heads. Drums pass their head's half-height as the cap.
            new DrumKitPiece(0, 169, 230, 164,  78,   0, -58),      // LC: left crash
            new DrumKitPiece(1, 104, 343, 128,  60, -30, -52),      // HH: hi-hat cymbals
            new DrumKitPiece(2, 115, 555,  56,  70, -45,  -7),      // LP: hi-hat foot pedal
            new DrumKitPiece(3, 214, 427, 128, 100,   0, -38, 22),  // SN
            new DrumKitPiece(4, 288, 322,  92,  86,   0, -35, 18),  // HT: left rack tom
            new DrumKitPiece(5, 346, 471, 172, 244,   0, -71, 48),  // BD: kick drum, pedal at the bottom cap
            new DrumKitPiece(6, 412, 318, 108,  98,   0, -37, 22),  // LT: right rack tom
            new DrumKitPiece(7, 502, 455, 138, 160,   0, -63, 26),  // FT
            new DrumKitPiece(8, 498, 221, 160,  80,   0, -62),      // CY: right crash
            new DrumKitPiece(9, 590, 330, 180,  96,   0, -71),      // RD: ride
        };

        /// <summary>
        /// Lane of the piece under the screen-space point, or -1. A point on a piece's lane tag
        /// counts as that piece; where two click shapes overlap the nearer center wins.
        /// </summary>
        public static int HitTest(float x, float y)
        {
            int best = -1;
            float bestDistance = float.MaxValue;
            foreach (var piece in Pieces)
            {
                if (piece.TagRect.Contains((int)x, (int)y))
                    return piece.Lane;

                if (!piece.Contains(x, y))
                    continue;
                float d = piece.NormalizedDistance(x, y);
                if (d < bestDistance)
                {
                    best = piece.Lane;
                    bestDistance = d;
                }
            }
            return best;
        }
    }
}
