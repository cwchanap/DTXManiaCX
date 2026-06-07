#nullable enable

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
        public static void Apply(IEnumerable<SongListNode?>? nodes, int songId, bool isBookmarked)
        {
            if (nodes == null)
                return;

            foreach (var node in nodes)
            {
                if (node == null)
                    continue;

                // Match by the persisted id (DatabaseSongId) when available, falling back to
                // the entity id. SET.def duplicates can carry a populated DatabaseSongId while
                // their DatabaseSong.Id is still 0 (AddSongAsync returns the existing id
                // without stamping it onto the parsed entity), so matching solely on
                // DatabaseSong.Id would miss them — or worse, match every zero-id node when
                // songId itself is 0.
                int? effectiveId = node.DatabaseSongId ?? node.DatabaseSong?.Id;
                if (node.DatabaseSong != null && effectiveId == songId)
                    node.DatabaseSong.IsBookmarked = isBookmarked;

                if (node.Children.Count > 0)
                    Apply(node.Children, songId, isBookmarked);
            }
        }
    }
}
