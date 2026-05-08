using DTXMania.Game.Lib.Song;

namespace DTXMania.Game.Lib.Song.Filtering
{
    public sealed record SongFilterCriteria(
        string SearchQuery,
        int? MinLevel,
        int? MaxLevel,
        PlayedStatus PlayedStatus,
        SongSortCriteria SortBy,
        bool SortDescending)
    {
        public static SongFilterCriteria Default { get; } = new(
            SearchQuery: "",
            MinLevel: null,
            MaxLevel: null,
            PlayedStatus: PlayedStatus.All,
            SortBy: SongSortCriteria.Title,
            SortDescending: false);

        public bool IsEmpty =>
            string.IsNullOrEmpty(SearchQuery)
            && MinLevel is null
            && MaxLevel is null
            && PlayedStatus == PlayedStatus.All
            && SortBy == SongSortCriteria.Title
            && !SortDescending;
    }
}
