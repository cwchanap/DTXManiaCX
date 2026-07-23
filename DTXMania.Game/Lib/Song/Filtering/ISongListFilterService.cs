using System.Collections.Generic;

namespace DTXMania.Game.Lib.Song.Filtering
{
    public interface ISongListFilterService
    {
        IReadOnlyList<FilteredSongResult> Apply(
            IEnumerable<SongListNode> roots,
            SongFilterCriteria criteria);

        /// <summary>
        /// Applies score-derived filters against one exact gameplay-speed profile.
        /// Implementations that only provide metadata filtering retain the legacy
        /// two-argument behavior through this compatibility default.
        /// </summary>
        IReadOnlyList<FilteredSongResult> Apply(
            IEnumerable<SongListNode> roots,
            SongFilterCriteria criteria,
            int playSpeedPercent)
        {
            _ = playSpeedPercent;
            return Apply(roots, criteria);
        }
    }
}
