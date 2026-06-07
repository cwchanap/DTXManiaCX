using System.Runtime.CompilerServices;
using DTXMania.Game.Lib.Song;
using Xunit;

namespace DTXMania.Test.Song
{
    [Trait("Category", "Unit")]
    public class SongSelectionTabTests
    {
        [Fact]
        public void Next_CyclesAllSongs_Recent_Bookmarks_AndWraps()
        {
            Assert.Equal(SongSelectionTab.RecentPlays, SongSelectionTab.AllSongs.Next());
            Assert.Equal(SongSelectionTab.Bookmarks, SongSelectionTab.RecentPlays.Next());
            Assert.Equal(SongSelectionTab.AllSongs, SongSelectionTab.Bookmarks.Next());
        }

        [Fact]
        public void DisplayLabel_ReturnsExpectedLabels()
        {
            Assert.Equal("All Songs", SongSelectionTab.AllSongs.DisplayLabel());
            Assert.Equal("Recent", SongSelectionTab.RecentPlays.DisplayLabel());
            Assert.Equal("Bookmarks", SongSelectionTab.Bookmarks.DisplayLabel());
        }

        // The Next() switch has no default arm by design: a future enum member that lacks an
        // explicit arm must fail loudly (SwitchExpressionException) instead of silently
        // falling through to AllSongs. This guards the exhaustive-switch contract.
        [Fact]
        public void Next_ForUnhandledEnumValue_ShouldThrow()
        {
            Assert.Throws<SwitchExpressionException>(() => ((SongSelectionTab)999).Next());
        }

        [Fact]
        public void DisplayLabel_ForInvalidEnumValue_FallsBackToString()
        {
            Assert.Equal("999", ((SongSelectionTab)999).DisplayLabel());
        }
    }
}
