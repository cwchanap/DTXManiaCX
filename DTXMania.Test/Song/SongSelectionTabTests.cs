using System;
using System.Runtime.CompilerServices;
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
                SongSelectionTab.AllSongs.Next());
        }

        [Fact]
        public void FromRecentPlays_ShouldWrapToAllSongs()
        {
            Assert.Equal(SongSelectionTab.AllSongs,
                SongSelectionTab.RecentPlays.Next());
        }

        [Fact]
        public void DisplayLabel_ShouldReturnHumanReadableNames()
        {
            Assert.Equal("All Songs", SongSelectionTab.AllSongs.DisplayLabel());
            Assert.Equal("Recent", SongSelectionTab.RecentPlays.DisplayLabel());
        }

        // Without a default arm, Next() throws for invalid enum values instead of
        // silently returning AllSongs. This makes future enum additions visible at
        // runtime rather than masked by a fallback.
        [Fact]
        public void Next_ForInvalidEnumValue_Throws()
        {
            Assert.Throws<SwitchExpressionException>(
                () => ((SongSelectionTab)999).Next());
        }

        [Fact]
        public void DisplayLabel_ForInvalidEnumValue_FallsBackToString()
        {
            Assert.Equal("999", ((SongSelectionTab)999).DisplayLabel());
        }
    }
}
