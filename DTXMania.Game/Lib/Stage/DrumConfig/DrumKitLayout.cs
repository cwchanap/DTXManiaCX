#nullable enable

using System.Collections.Generic;
using Microsoft.Xna.Framework;
using DTXMania.Game.Lib.UI.Layout;

namespace DTXMania.Game.Lib.Stage.DrumConfig
{
    /// <summary>
    /// One clickable kit piece, mapped 1:1 to a drum lane (0-9). <see cref="Bounds"/> is the
    /// screen-space box (1280x720 virtual space) around the piece as it appears in the kit
    /// illustration drawn into <see cref="DrumConfigUILayout.KitArt"/>; the click target and
    /// the highlight ring are the ellipse inscribed in it. The lane tag sits at
    /// <see cref="TagOffsetX"/>/<see cref="TagOffsetY"/> from the box center.
    /// </summary>
    public readonly struct DrumKitPiece
    {
        public int Lane { get; }
        public Rectangle Bounds { get; }
        public int TagOffsetX { get; }
        public int TagOffsetY { get; }

        public DrumKitPiece(int lane, int centerX, int centerY, int width, int height, int tagOffsetX, int tagOffsetY)
        {
            Lane = lane;
            Bounds = new Rectangle(centerX - (width / 2), centerY - (height / 2), width, height);
            TagOffsetX = tagOffsetX;
            TagOffsetY = tagOffsetY;
        }

        /// <summary>Lane-code tag rectangle (e.g. "SN"); its top-center sits at the tag offset.</summary>
        public Rectangle TagRect => new(
            Bounds.Center.X + TagOffsetX - (DrumConfigUILayout.PieceTagWidth / 2),
            Bounds.Center.Y + TagOffsetY,
            DrumConfigUILayout.PieceTagWidth,
            DrumConfigUILayout.PieceTagHeight);

        /// <summary>
        /// Normalized ellipse distance of the point from the piece center (&lt;= 1 is inside the
        /// inscribed ellipse). Used to pick the nearest piece where two boxes overlap.
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
            // cymbals and on the drum heads.
            new DrumKitPiece(0, 169, 230, 164,  72,   0, -58),  // LC: left crash
            new DrumKitPiece(1, 104, 343, 128,  60, -30, -52),  // HH: hi-hat cymbals
            new DrumKitPiece(2, 115, 555,  56,  70, -45,  -7),  // LP: hi-hat foot pedal
            new DrumKitPiece(3, 214, 426, 128, 100,   0, -38),  // SN
            new DrumKitPiece(4, 288, 322,  92,  86,   0, -35),  // HT: left rack tom
            new DrumKitPiece(5, 345, 470, 172, 240,   0, -70),  // BD: kick drum and its pedal
            new DrumKitPiece(6, 412, 318, 108,  98,   0, -37),  // LT: right rack tom
            new DrumKitPiece(7, 501, 455, 136, 160,   0, -63),  // FT
            new DrumKitPiece(8, 498, 220, 160,  74,   0, -61),  // CY: right crash
            new DrumKitPiece(9, 590, 330, 180,  96,   0, -71),  // RD: ride
        };

        /// <summary>
        /// Lane of the piece under the screen-space point, or -1. A point on a piece's lane tag
        /// counts as that piece; where two ellipses overlap the nearer center wins.
        /// </summary>
        public static int HitTest(float x, float y)
        {
            int best = -1;
            float bestDistance = float.MaxValue;
            foreach (var piece in Pieces)
            {
                if (piece.TagRect.Contains((int)x, (int)y))
                    return piece.Lane;

                float d = piece.NormalizedDistance(x, y);
                if (d <= 1f && d < bestDistance)
                {
                    best = piece.Lane;
                    bestDistance = d;
                }
            }
            return best;
        }
    }
}
