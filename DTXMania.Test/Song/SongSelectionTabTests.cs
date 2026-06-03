using DTXMania.Game.Lib.Song;
using Xunit;

namespace DTXMania.Test.Song
{
    [Trait("Category", "Unit")]
    public class SongSelectionTabTests
    {
        [Fact]
        public void Next_FromAllSongs_ReturnsRecentPlays()
        {
            Assert.Equal(SongSelectionTab.RecentPlays,
                SongSelectionTabExtensions.Next(SongSelectionTab.AllSongs));
        }

        [Fact]
        public void Next_FromRecentPlays_WrapsToAllSongs()
        {
            Assert.Equal(SongSelectionTab.AllSongs,
                SongSelectionTabExtensions.Next(SongSelectionTab.RecentPlays));
        }

        [Fact]
        public void DisplayLabel_ReturnsHumanReadableNames()
        {
            Assert.Equal("All Songs", SongSelectionTab.AllSongs.DisplayLabel());
            Assert.Equal("Recent", SongSelectionTab.RecentPlays.DisplayLabel());
        }
    }
}
