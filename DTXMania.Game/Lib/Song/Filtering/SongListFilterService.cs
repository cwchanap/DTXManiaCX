using System;
using System.Collections.Generic;
using System.Linq;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Song.Entities;

namespace DTXMania.Game.Lib.Song.Filtering
{
    public sealed class SongListFilterService : ISongListFilterService
    {
        /// <summary>
        /// Exclusive upper bound for rank indices that count as "cleared".
        /// Ranks with ComputeRankIndex below this threshold count as cleared.
        /// </summary>
        private const int ClearedRankThreshold = 7;

        public IReadOnlyList<FilteredSongResult> Apply(
            IEnumerable<SongListNode> roots,
            SongFilterCriteria criteria)
        {
            return Apply(roots, criteria, PlaySpeedRange.Default);
        }

        /// <summary>
        /// Applies score-derived filters against one exact play-speed profile.
        /// Metadata filters such as title and difficulty remain speed-independent.
        /// </summary>
        public IReadOnlyList<FilteredSongResult> Apply(
            IEnumerable<SongListNode> roots,
            SongFilterCriteria criteria,
            int playSpeedPercent)
        {
            if (roots == null) throw new ArgumentNullException(nameof(roots));
            if (criteria == null) throw new ArgumentNullException(nameof(criteria));

            var flat = new List<FilteredSongResult>();
            foreach (var root in roots)
                Flatten(root, parentPath: "", flat);

            var afterSearch = ApplySearch(flat, criteria.SearchQuery);
            var afterLevel  = ApplyLevel(afterSearch, criteria.MinLevel, criteria.MaxLevel);
            var afterPlayed = ApplyPlayedStatus(
                afterLevel,
                criteria.PlayedStatus,
                playSpeedPercent);
            return SortResults(afterPlayed, criteria);
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

        private static List<FilteredSongResult> ApplySearch(
            List<FilteredSongResult> flat, string query)
        {
            if (string.IsNullOrEmpty(query)) return flat;

            return flat.Where(r =>
                Contains(r.Node.DisplayTitle, query) ||
                Contains(r.Node.DatabaseSong?.DisplayArtist, query))
                .ToList();
        }

        private static List<FilteredSongResult> ApplyLevel(
            List<FilteredSongResult> flat, int? min, int? max)
        {
            if (min is null && max is null) return flat;

            // MinLevel/MaxLevel are normalized by the caller before reaching here.
            int lo = min ?? int.MinValue;
            int hi = max ?? int.MaxValue;

            return flat.Where(r =>
            {
                int level = r.Node.MaxDifficultyLevel;
                return level >= lo && level <= hi;
            }).ToList();
        }

        private static List<FilteredSongResult> ApplyPlayedStatus(
            List<FilteredSongResult> flat,
            PlayedStatus status,
            int playSpeedPercent)
        {
            if (status == PlayedStatus.All) return flat;

            return flat.Where(r => Match(r.Node, status, playSpeedPercent)).ToList();
        }

        private static bool Match(
            SongListNode node,
            PlayedStatus status,
            int playSpeedPercent)
        {
            var scores = Enumerable.Range(0, node.Scores?.Length ?? 0)
                .Select(index => ResolveScore(node, index, playSpeedPercent))
                .Where(score => score != null)
                .Cast<SongScore>();

            bool anyPlayed = scores.Any(score => score.PlayCount > 0);
            bool anyCleared = scores.Any(score => score.PlayCount > 0
                && (score.ClearCount > 0
                    || SongScore.ComputeRankIndex(
                        SongScore.NormalizeStoredBestRank(score.BestRank))
                        < ClearedRankThreshold));

            return status switch
            {
                PlayedStatus.Unplayed => !anyPlayed,
                PlayedStatus.Played   => anyPlayed,
                PlayedStatus.Cleared  => anyCleared,
                _ => true
            };
        }

        internal static SongScore? ResolveScore(
            SongListNode node,
            int difficulty,
            int playSpeedPercent)
        {
            if (node == null)
                return null;

            return playSpeedPercent == PlaySpeedRange.Default
                ? node.GetScore(difficulty)
                : node.GetScore(difficulty, playSpeedPercent);
        }

        private static bool Contains(string? haystack, string needle)
        {
            if (string.IsNullOrEmpty(haystack)) return false;
            return haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
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
