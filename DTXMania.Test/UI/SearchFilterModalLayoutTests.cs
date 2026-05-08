using DTXMania.Game.Lib.UI.Layout;
using Microsoft.Xna.Framework;
using Xunit;

namespace DTXMania.Test.UI
{
    public class SearchFilterModalLayoutTests
    {
        [Fact]
        public void Modal_IsCentered1280x720()
        {
            // Modal: 600 wide, 360 tall, centered in 1280x720
            Assert.Equal(340, SongSelectionUILayout.SearchFilterModal.X);
            Assert.Equal(180, SongSelectionUILayout.SearchFilterModal.Y);
            Assert.Equal(600, SongSelectionUILayout.SearchFilterModal.Width);
            Assert.Equal(360, SongSelectionUILayout.SearchFilterModal.Height);
        }

        [Fact]
        public void Modal_BoundsMatchPositionAndSize()
        {
            var b = SongSelectionUILayout.SearchFilterModal.Bounds;
            Assert.Equal(new Rectangle(340, 180, 600, 360), b);
        }

        [Fact]
        public void SearchBox_HasWidth()
        {
            Assert.True(SongSelectionUILayout.SearchFilterModal.SearchBoxWidth > 0);
        }
    }
}
