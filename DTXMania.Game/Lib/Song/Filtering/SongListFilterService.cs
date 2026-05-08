using System;
using System.Collections.Generic;
using System.Linq;

namespace DTXMania.Game.Lib.Song.Filtering
{
    public sealed class SongListFilterService : ISongListFilterService
    {
        public IReadOnlyList<FilteredSongResult> Apply(
            IEnumerable<SongListNode> roots,
            SongFilterCriteria criteria)
        {
            if (roots == null) throw new ArgumentNullException(nameof(roots));
            if (criteria == null) throw new ArgumentNullException(nameof(criteria));

            var flat = new List<FilteredSongResult>();
            foreach (var root in roots)
                Flatten(root, parentPath: "", flat);

            return SortResults(flat, criteria);
        }

        private static void Flatten(SongListNode node, string parentPath, List<FilteredSongResult> sink)
        {
            if (node == null) return;

            if (node.Type == NodeType.Score)
            {
                sink.Add(new FilteredSongResult(node, parentPath));
                return;
            }

            if (node.Type == NodeType.Box)
            {
                var childPath = string.IsNullOrEmpty(parentPath)
                    ? node.DisplayTitle
                    : parentPath + " / " + node.DisplayTitle;
                foreach (var child in node.Children)
                    Flatten(child, childPath, sink);
            }
            // BackBox / Random: ignore
        }

        private static IReadOnlyList<FilteredSongResult> SortResults(
            List<FilteredSongResult> flat, SongFilterCriteria criteria)
        {
            int Compare(FilteredSongResult a, FilteredSongResult b)
            {
                int cmp = criteria.SortBy switch
                {
                    SongSortCriteria.Title =>
                        string.Compare(a.Node.DisplayTitle, b.Node.DisplayTitle,
                            StringComparison.OrdinalIgnoreCase),
                    SongSortCriteria.Artist =>
                        string.Compare(
                            a.Node.DatabaseSong?.DisplayArtist ?? "",
                            b.Node.DatabaseSong?.DisplayArtist ?? "",
                            StringComparison.OrdinalIgnoreCase),
                    SongSortCriteria.Genre =>
                        string.Compare(a.Node.Genre ?? "", b.Node.Genre ?? "",
                            StringComparison.OrdinalIgnoreCase),
                    SongSortCriteria.Level =>
                        a.Node.MaxDifficultyLevel.CompareTo(b.Node.MaxDifficultyLevel),
                    _ => string.Compare(a.Node.DisplayTitle, b.Node.DisplayTitle,
                            StringComparison.OrdinalIgnoreCase)
                };
                return criteria.SortDescending ? -cmp : cmp;
            }

            flat.Sort(Compare);
            return flat;
        }
    }
}
