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
    }
}
