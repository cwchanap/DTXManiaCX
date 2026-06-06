using System.Collections.Generic;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Song.Entities;
using Xunit;

namespace DTXMania.Test.Song
{
    [Trait("Category", "Unit")]
    public class BookmarkStateReconcilerTests
    {
        private static SongListNode Score(int id, bool bookmarked) => new SongListNode
        {
            Type = NodeType.Score,
            DatabaseSong = new DTXMania.Game.Lib.Song.Entities.Song { Id = id, IsBookmarked = bookmarked }
        };

        [Fact]
        public void Apply_SetsFlagOnAllNodesWithMatchingId_AcrossNestedChildren()
        {
            var match1 = Score(42, false);
            var nonMatch = Score(7, false);
            var folder = new SongListNode
            {
                Type = NodeType.Box,
                Children = new List<SongListNode> { Score(42, false), nonMatch }
            };
            var roots = new List<SongListNode> { match1, folder };

            BookmarkStateReconciler.Apply(roots, songId: 42, isBookmarked: true);

            Assert.True(match1.DatabaseSong!.IsBookmarked);
            Assert.True(folder.Children[0].DatabaseSong!.IsBookmarked);
            Assert.False(nonMatch.DatabaseSong!.IsBookmarked); // id 7 untouched
        }

        [Fact]
        public void Apply_CanClearFlag()
        {
            var n = Score(42, true);
            BookmarkStateReconciler.Apply(new List<SongListNode> { n }, 42, false);
            Assert.False(n.DatabaseSong!.IsBookmarked);
        }

        [Fact]
        public void Apply_WithNullCollection_DoesNotThrow()
        {
            var ex = Record.Exception(() => BookmarkStateReconciler.Apply(null, 1, true));
            Assert.Null(ex);
        }

        [Fact]
        public void Apply_IgnoresNodesWithoutDatabaseSongOrNonMatchingId()
        {
            var folderNoSong = new SongListNode { Type = NodeType.Box };
            var other = Score(99, false);
            BookmarkStateReconciler.Apply(new List<SongListNode> { folderNoSong, other }, 42, true);
            Assert.False(other.DatabaseSong!.IsBookmarked);
        }
    }
}
