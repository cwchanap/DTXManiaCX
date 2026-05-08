using System.Collections.Generic;

namespace DTXMania.Game.Lib.Song.Filtering
{
    public interface ISongListFilterService
    {
        IReadOnlyList<FilteredSongResult> Apply(
            IEnumerable<SongListNode> roots,
            SongFilterCriteria criteria);
    }
}
