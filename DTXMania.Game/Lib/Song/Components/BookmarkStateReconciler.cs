using System.Collections.Generic;

namespace DTXMania.Game.Lib.Song.Components
{
    /// <summary>
    /// Propagates a bookmark-state change to every in-memory <see cref="SongListNode"/>
    /// representing the same song. The browse tree, Recent list, and Bookmarks list each hold
    /// distinct node/entity instances per song, so a toggle must reconcile all of them by song
    /// Id to keep the star marker consistent across tabs without a full reload.
    /// </summary>
    public static class BookmarkStateReconciler
    {
        /// <summary>
        /// Recursively sets <c>DatabaseSong.IsBookmarked = isBookmarked</c> on every node
        /// (and descendant) whose <c>DatabaseSong.Id</c> equals <paramref name="songId"/>.
        /// Null-safe: a null collection is ignored.
        /// </summary>
        public static void Apply(IEnumerable<SongListNode> nodes, int songId, bool isBookmarked)
        {
            if (nodes == null)
                return;

            foreach (var node in nodes)
            {
                if (node == null)
                    continue;

                if (node.DatabaseSong != null && node.DatabaseSong.Id == songId)
                    node.DatabaseSong.IsBookmarked = isBookmarked;

                if (node.Children != null && node.Children.Count > 0)
                    Apply(node.Children, songId, isBookmarked);
            }
        }
    }
}
