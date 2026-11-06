using System.Linq;
using DTXMania.Game.Lib.Stage.DrumConfig;
using DTXMania.Game.Lib.UI.Layout;
using Xunit;

namespace DTXMania.Test.Stage.DrumConfig
{
    [Trait("Category", "Unit")]
    public class DrumKitLayoutTests
    {
        [Fact]
        public void Pieces_AreListedInLaneOrder()
        {
            Assert.Equal(Enumerable.Range(0, 10), DrumKitLayout.Pieces.Select(p => p.Lane));
        }

        [Fact]
        public void Pieces_AndTheirTags_StayInsideTheKitPanel()
        {
            var panel = DrumConfigUILayout.KitPanel;
            foreach (var piece in DrumKitLayout.Pieces)
            {
                Assert.True(panel.Contains(piece.Bounds), $"lane {piece.Lane} piece leaves the kit panel");
                Assert.True(panel.Contains(piece.TagRect), $"lane {piece.Lane} tag leaves the kit panel");
            }
        }

        [Fact]
        public void Pieces_UseTheArtThatMatchesTheirInstrument()
        {
            DrumPieceArt ArtOf(int lane) => DrumKitLayout.Pieces[lane].Art;
            Assert.Equal(DrumPieceArt.Cymbal, ArtOf(0));
            Assert.Equal(DrumPieceArt.HiHat, ArtOf(1));
            Assert.Equal(DrumPieceArt.Pedal, ArtOf(2));
            Assert.Equal(DrumPieceArt.Kick, ArtOf(5));
            Assert.Equal(DrumPieceArt.Cymbal, ArtOf(9));
            foreach (var lane in new[] { 3, 4, 6, 7 })
                Assert.Equal(DrumPieceArt.Drum, ArtOf(lane));
        }

        [Fact]
        public void HitTest_AtEveryPieceCenter_ReturnsThatPiecesLane()
        {
            foreach (var piece in DrumKitLayout.Pieces)
                Assert.Equal(piece.Lane, DrumKitLayout.HitTest(piece.Bounds.Center.X, piece.Bounds.Center.Y));
        }

        [Fact]
        public void HitTest_OnEveryLaneTag_ReturnsThatTagsLane()
        {
            foreach (var piece in DrumKitLayout.Pieces)
                Assert.Equal(piece.Lane, DrumKitLayout.HitTest(piece.TagRect.Center.X, piece.TagRect.Center.Y));
        }

        [Fact]
        public void HitTest_OutsideEveryPiece_ReturnsMinusOne()
        {
            Assert.Equal(-1, DrumKitLayout.HitTest(5, 5));
            Assert.Equal(-1, DrumKitLayout.HitTest(900, 300)); // bindings panel
            // Corner of the kick's box, outside its inscribed ellipse.
            var kick = DrumKitLayout.Pieces[5].Bounds;
            Assert.Equal(-1, DrumKitLayout.HitTest(kick.Right - 2, kick.Bottom - 2));
        }

        [Fact]
        public void NormalizedDistance_IsZeroAtCenterAndOneOnTheEllipseEdge()
        {
            var piece = DrumKitLayout.Pieces[3];
            var b = piece.Bounds;
            Assert.Equal(0f, piece.NormalizedDistance(b.X + (b.Width / 2f), b.Y + (b.Height / 2f)), 3);
            Assert.Equal(1f, piece.NormalizedDistance(b.Right, b.Y + (b.Height / 2f)), 3);
        }
    }
}
