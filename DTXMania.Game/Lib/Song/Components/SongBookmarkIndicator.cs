using DTXMania.Game.Lib.Song;

namespace DTXMania.Game.Lib.Song.Components
{
    /// <summary>
    /// Render-agnostic decision logic for the bookmark star marker on song bars.
    /// Kept separate from the renderer so it is unit-testable without a GraphicsDevice.
    /// </summary>
    public static class SongBookmarkIndicator
    {
        /// <summary>The glyph drawn on a bookmarked song bar.</summary>
        public const string Glyph = "★";

        /// <summary>
        /// True when the node is a real song whose database entity is bookmarked.
        /// </summary>
        public static bool ShouldShow(SongListNode node)
        {
            return node != null
                && node.Type == NodeType.Score
                && node.DatabaseSong?.IsBookmarked == true;
        }
    }
}
