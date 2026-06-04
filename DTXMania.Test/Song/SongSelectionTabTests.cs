using DTXMania.Game.Lib.Song;
using Xunit;

namespace DTXMania.Test.Song
{
    [Trait("Category", "Unit")]
    public class SongSelectionTabTests
    {
        [Fact]
        public void FromAllSongs_ShouldReturnRecentPlays()
        {
            Assert.Equal(SongSelectionTab.RecentPlays,
                SongSelectionTabExtensions.Next(SongSelectionTab.AllSongs));
        }

        [Fact]
        public void FromRecentPlays_ShouldWrapToAllSongs()
        {
            Assert.Equal(SongSelectionTab.AllSongs,
                SongSelectionTabExtensions.Next(SongSelectionTab.RecentPlays));
        }

        [Fact]
        public void DisplayLabel_ShouldReturnHumanReadableNames()
        {
            Assert.Equal("All Songs", SongSelectionTab.AllSongs.DisplayLabel());
            Assert.Equal("Recent", SongSelectionTab.RecentPlays.DisplayLabel());
        }

        [Fact]
        public void ForInvalidEnumValue_ShouldFallBackToAllSongs()
        {
            Assert.Equal(SongSelectionTab.AllSongs,
                SongSelectionTabExtensions.Next((SongSelectionTab)999));
        }

        [Fact]
        public void ForInvalidEnumValue_ShouldFallBackToString()
        {
            Assert.Equal("999", ((SongSelectionTab)999).DisplayLabel());
        }
    }
}
