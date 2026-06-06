using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Song.Entities;
using Xunit;

namespace DTXMania.Test.Song
{
    [Trait("Category", "Unit")]
    public class SongBookmarkIndicatorTests
    {
        [Fact]
        public void ShouldShow_WhenNull_ReturnsFalse()
        {
            Assert.False(SongBookmarkIndicator.ShouldShow(null));
        }

        [Fact]
        public void ShouldShow_WhenFolderNode_ReturnsFalse()
        {
            var node = new SongListNode { Type = NodeType.Box };
            Assert.False(SongBookmarkIndicator.ShouldShow(node));
        }

        [Fact]
        public void ShouldShow_WhenScoreNotBookmarked_ReturnsFalse()
        {
            var node = new SongListNode
            {
                Type = NodeType.Score,
                DatabaseSong = new DTXMania.Game.Lib.Song.Entities.Song { IsBookmarked = false }
            };
            Assert.False(SongBookmarkIndicator.ShouldShow(node));
        }

        [Fact]
        public void ShouldShow_WhenScoreBookmarked_ReturnsTrue()
        {
            var node = new SongListNode
            {
                Type = NodeType.Score,
                DatabaseSong = new DTXMania.Game.Lib.Song.Entities.Song { IsBookmarked = true }
            };
            Assert.True(SongBookmarkIndicator.ShouldShow(node));
        }

        [Fact]
        public void ShouldShow_WhenScoreNodeHasNoDatabaseSong_ReturnsFalse()
        {
            var node = new SongListNode { Type = NodeType.Score, DatabaseSong = null };
            Assert.False(SongBookmarkIndicator.ShouldShow(node));
        }
    }
}
