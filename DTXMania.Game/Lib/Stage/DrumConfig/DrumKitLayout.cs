#nullable enable

using System.Collections.Generic;
using Microsoft.Xna.Framework;
using DTXMania.Game.Lib.UI.Layout;

namespace DTXMania.Game.Lib.Stage.DrumConfig
{
    /// <summary>Which skin art a kit piece draws (TexturePath.DrumPad*).</summary>
    public enum DrumPieceArt { Cymbal, HiHat, Drum, Kick, Pedal }

    /// <summary>
    /// One clickable kit piece, mapped 1:1 to a drum lane (0-9). <see cref="Bounds"/> is the
    /// screen-space box the art is fit into (1280x720 virtual space, inside the kit panel);
    /// the click target is the ellipse inscribed in it. The lane tag sits at
    /// <see cref="TagOffsetY"/> below the box center.
    /// </summary>
    public readonly struct DrumKitPiece
    {
        public int Lane { get; }
        public DrumPieceArt Art { get; }
        public Rectangle Bounds { get; }
        public int TagOffsetY { get; }

        public DrumKitPiece(int lane, DrumPieceArt art, int centerX, int centerY, int width, int height, int tagOffsetY)
        {
            Lane = lane;
            Art = art;
            Bounds = new Rectangle(centerX - (width / 2), centerY - (height / 2), width, height);
            TagOffsetY = tagOffsetY;
        }

        /// <summary>Lane-code tag rectangle (e.g. "SN"), centered under the piece's middle.</summary>
        public Rectangle TagRect => new(
            Bounds.Center.X - (DrumConfigUILayout.PieceTagWidth / 2),
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
    /// Static kit layout in the kit panel. Pieces are listed in lane order (LC..RD) and placed
    /// like a real kit seen from the drummer's seat; each box keeps the shipped art's aspect
    /// ratio so the art fills it.
    /// </summary>
    public static class DrumKitLayout
    {
        public static IReadOnlyList<DrumKitPiece> Pieces { get; } = new[]
        {
            new DrumKitPiece(0, DrumPieceArt.Cymbal, 176, 234, 170, 150, 30),  // LC: crash, upper left
            new DrumKitPiece(1, DrumPieceArt.HiHat,  112, 368, 130, 122, 30),  // HH: left stand
            new DrumKitPiece(2, DrumPieceArt.Pedal,  120, 570,  96, 123, 40),  // LP: hi-hat foot
            new DrumKitPiece(3, DrumPieceArt.Drum,   192, 474, 118, 107, 40),  // SN
            new DrumKitPiece(4, DrumPieceArt.Drum,   296, 316, 100,  91, 34),  // HT: left rack tom
            new DrumKitPiece(5, DrumPieceArt.Kick,   358, 488, 180, 176, 74),  // BD: the kick itself
            new DrumKitPiece(6, DrumPieceArt.Drum,   420, 316, 108,  98, 36),  // LT: right rack tom
            new DrumKitPiece(7, DrumPieceArt.Drum,   544, 484, 140, 127, 46),  // FT
            new DrumKitPiece(8, DrumPieceArt.Cymbal, 496, 204, 170, 150, 30),  // CY: crash, upper right
            new DrumKitPiece(9, DrumPieceArt.Cymbal, 612, 320, 140, 124, 28),  // RD: ride, far right
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
