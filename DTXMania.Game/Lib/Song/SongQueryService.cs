#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Song.Entities;
using Microsoft.EntityFrameworkCore;
using SongEntity = DTXMania.Game.Lib.Song.Entities.Song;
using SongScoreEntity = DTXMania.Game.Lib.Song.Entities.SongScore;

namespace DTXMania.Game.Lib.Song
{
    /// <summary>
    /// Live song-list queries over the game-owned database: recent plays and
    /// bookmark read/toggle (HPA-801 Decision D). The SQL lives here directly —
    /// there is deliberately no <c>SongQueryStore</c>. Node mapping reuses the
    /// library's <see cref="SongHierarchyBuilder"/> so Recent/Bookmarks nodes
    /// behave exactly like browse nodes, and active-root filtering mirrors the
    /// library's published search-paths snapshot. Constructed by
    /// <see cref="SongLibrary"/>.
    /// </summary>
    internal sealed class SongQueryService
    {
        public SongQueryService(
            SongDatabase database,
            SongHierarchyBuilder hierarchy,
            Func<string[]>? currentSearchPathsSnapshot = null,
            Func<bool>? readsAsNeverInitialized = null)
        {
            _database = database ?? throw new ArgumentNullException(nameof(database));
            _hierarchy = hierarchy ?? throw new ArgumentNullException(nameof(hierarchy));
            _currentSearchPathsSnapshot = currentSearchPathsSnapshot
                ?? (() => Array.Empty<string>());
            _readsAsNeverInitialized = readsAsNeverInitialized
                ?? (() => !database.IsInitialized);
        }

        private readonly SongDatabase _database;
        private readonly SongHierarchyBuilder _hierarchy;
        private readonly Func<string[]> _currentSearchPathsSnapshot;
        private readonly Func<bool> _readsAsNeverInitialized;

        /// <summary>
        /// Builds a flat list of Score nodes for the most-recently-played songs, newest first.
        /// One node per song (multi-chart songs collapse to a single node carrying all charts),
        /// limited to <paramref name="limit"/>. Returns an empty list when the database service
        /// is unavailable or nothing has been played. Reuses the same node builder as the browse
        /// list so difficulty cycling, status panel, preview, and activation behave identically.
        /// Exceptions from the database layer are allowed to propagate so the caller
        /// (BeginRecentPlaysLoad) can distinguish a genuine failure from an empty result
        /// and set the appropriate UI state.
        /// </summary>
        public async Task<List<SongListNode>> GetRecentlyPlayedNodesAsync(int limit = 20)
        {
            // Pre-initialization queries read as empty (pre-HPA-801 contract);
            // once initialization was attempted, a failed or purged database
            // falls through to the query, whose CreateContext guard throws the
            // pre-rename load failure instead of reading as an empty result.
            if (limit <= 0 || _readsAsNeverInitialized()) return new List<SongListNode>();

            // Push the active-root criterion into the DB query as a set of active
            // chart IDs so that grouping, ordering, AND pagination all use
            // active-chart recency. This is what makes the ordering globally
            // correct: a song promoted up the all-chart ordering by a play on a
            // now-inactive chart can no longer push a more-recently-played active
            // song past the page boundary. With active filtering in SQL, every
            // returned song already has at least one active chart, so a single
            // bounded query is sufficient (no client-side re-pagination).
            IReadOnlyCollection<int>? activeChartIds = null;
            var roots = _currentSearchPathsSnapshot();
            if (roots.Length > 0)
            {
                activeChartIds = await GetActiveChartIdsAsync(roots)
                    .ConfigureAwait(false);
            }

            var songs = await GetRecentlyPlayedSongsAsync(limit, 0, activeChartIds)
                .ConfigureAwait(false);
            var nodes = new List<SongListNode>(songs.Count);
            AppendActiveNodes(songs, roots, limit, ref nodes);
            return nodes;
        }

        private void AppendActiveNodes(
            List<SongEntity> songs,
            string[] roots,
            int limit,
            ref List<SongListNode> nodes)
        {
            // The DB query already orders by active-chart recency, so callers
            // receive songs in the correct global order; we only filter to each
            // song's active charts and build nodes, preserving DB order. The
            // same roots snapshot that fed the query filters the node charts —
            // rereading the live snapshot after the awaits could drop every
            // chart of a selected song when roots change mid-load.
            var items = songs
                .Select(song =>
                {
                    var charts = GetActiveCharts(song, roots);
                    var lastPlayedAt = charts
                        .SelectMany(chart =>
                            chart.Scores ?? Enumerable.Empty<SongScoreEntity>())
                        .Where(score => score.LastPlayedAt.HasValue)
                        .Select(score => score.LastPlayedAt)
                        .Max();
                    return (Song: song, Charts: charts, LastPlayedAt: lastPlayedAt);
                })
                .Where(item =>
                    item.Charts.Length > 0 && item.LastPlayedAt.HasValue);

            foreach (var item in items)
            {
                if (nodes.Count >= limit)
                    break;
                var song = item.Song;
                var charts = item.Charts;
                song.Charts = charts;
                var node = _hierarchy.CreateSongNodeFromDatabaseEntities(song, charts);
                if (node != null)
                {
                    node.RecentPlaySpeedPercent = charts
                        .SelectMany(chart =>
                            chart.Scores ?? Enumerable.Empty<SongScoreEntity>())
                        .Where(score => score.LastPlayedAt.HasValue)
                        .OrderByDescending(score => score.LastPlayedAt)
                        .ThenByDescending(score => score.Id)
                        .Select(score => (int?)score.PlaySpeedPercent)
                        .FirstOrDefault();
                    nodes.Add(node);
                }
            }
        }

        /// <summary>
        /// Returns bookmarked songs as flat Score nodes, alphabetical by title. Returns an
        /// empty list if the database is unavailable. Reuses the same node builder as the
        /// browse list so difficulty cycling, status panel, preview, and activation behave
        /// identically. Exceptions from the database layer propagate so the caller
        /// (RequestBookmarksRefresh) can distinguish a genuine failure from an empty result.
        /// </summary>
        public async Task<List<SongListNode>> GetBookmarkedNodesAsync()
        {
            // Same contract as GetRecentlyPlayedNodesAsync: only a
            // never-initialized database reads as empty; a failed or purged
            // initialization surfaces the database failure.
            if (_readsAsNeverInitialized()) return new List<SongListNode>();

            var songs = await GetBookmarkedSongsAsync().ConfigureAwait(false);
            var nodes = new List<SongListNode>(songs.Count);
            var bookmarkRoots = _currentSearchPathsSnapshot();
            foreach (var song in songs)
            {
                var charts = GetActiveCharts(song, bookmarkRoots);
                if (charts.Length == 0)
                    continue;
                song.Charts = charts;
                var node = _hierarchy.CreateSongNodeFromDatabaseEntities(song, charts);
                if (node != null)
                    nodes.Add(node);
            }
            return nodes;
        }

        /// <summary>
        /// Sets or clears the bookmark flag on a song. No-op if the song id is not
        /// found. Throws when the database is unavailable so the caller's rollback
        /// path can surface the fault (HPA-801).
        /// </summary>
        public async Task SetBookmarkAsync(int songId, bool bookmarked)
        {
            // Writes fail clearly before Startup's initialization (HPA-801):
            // a purged/never-initialized database must surface the fault to the
            // caller's rollback path instead of silently no-oping.
            using var context = _database.CreateContext();
            var song = await context.Songs.FirstOrDefaultAsync(s => s.Id == songId);
            if (song == null) return;
            song.IsBookmarked = bookmarked;
            song.UpdatedAt = DateTime.UtcNow;
            await context.SaveChangesAsync();
        }

        /// <summary>
        /// Returns the most-recently-played songs, one row per song. A song with multiple
        /// difficulty charts is collapsed to a single entry using the maximum LastPlayedAt
        /// across its charts. Songs that have never been played (no score with a LastPlayedAt)
        /// are excluded. Results are ordered newest-first and limited to <paramref name="limit"/>.
        /// Recency intentionally aggregates across speeds at the song level. Each returned
        /// Song retains every eagerly loaded score variant and each variant's pitch-bearing
        /// history, so materialization does not collapse chart/instrument/speed identity.
        /// </summary>
        /// <param name="activeChartIds">
        /// When non-null, recency (grouping, ordering, and pagination) considers only the
        /// scores of these charts. This pushes an active-root criterion into SQL so that
        /// ordering is globally correct across pages; null means every chart is considered.
        /// </param>
        internal async Task<List<SongEntity>> GetRecentlyPlayedSongsAsync(
            int limit = 20,
            int skip = 0,
            IReadOnlyCollection<int>? activeChartIds = null)
        {
            if (limit <= 0) return new List<SongEntity>();

            using var context = _database.CreateContext();

            // Push grouping + ordering + limit into SQL so we transfer at most `limit`
            // integer IDs back to the client instead of every played SongScore row.
            // The IDs are then used in a second query to load full Song+Chart+Score
            // graphs, and manually reordered client-side (IN does not preserve order).
            IQueryable<SongScoreEntity> scores = context.SongScores
                .Where(s => s.LastPlayedAt != null);
            if (activeChartIds != null)
                // EF Core 10 translates a parameterized collection as one SQL
                // parameter per element, which exceeds SQLite's variable limit
                // for large libraries. EF.Parameter forces the single JSON
                // parameter (json_each) translation for this unbounded set.
                scores = scores.Where(s => EF.Parameter(activeChartIds).Contains(s.ChartId));
            var orderedIds = await scores
                .Select(s => new { s.Chart.SongId, s.LastPlayedAt })
                .GroupBy(s => s.SongId)
                .Select(g => new { SongId = g.Key, LastPlayed = g.Max(s => s.LastPlayedAt) })
                .OrderByDescending(x => x.LastPlayed)
                .ThenBy(x => x.SongId)
                .Skip(skip)
                .Take(limit)
                .Select(x => x.SongId)
                .ToListAsync();

            if (orderedIds.Count == 0) return new List<SongEntity>();

            var songs = await context.Songs
                .Where(s => orderedIds.Contains(s.Id))
                .Include(s => s.Charts)
                    .ThenInclude(c => c.Scores)
                        .ThenInclude(sc => sc.PerformanceHistory)
                .ToListAsync();

            // Re-order the loaded songs to match the recency ordering (the IN query does
            // not preserve order).
            var byId = songs.ToDictionary(s => s.Id);
            var result = new List<SongEntity>(orderedIds.Count);
            foreach (var id in orderedIds)
            {
                if (byId.TryGetValue(id, out var song))
                    result.Add(song);
            }
            return result;
        }

        /// <summary>
        /// Returns the database IDs of charts whose <see cref="SongChart.FilePath"/>
        /// resides under one of <paramref name="roots"/> (root-containment uses
        /// <see cref="SongPathIdentity"/>, mirroring the library's active-chart
        /// rule). Used to push an active-root criterion into recency queries so
        /// that ordering and pagination are globally correct. Returns an empty
        /// list for an empty root set.
        /// </summary>
        internal async Task<IReadOnlyList<int>> GetActiveChartIdsAsync(
            IReadOnlyList<string> roots)
        {
            if (roots == null || roots.Count == 0)
                return Array.Empty<int>();

            // Normalize defensively so blank or malformed roots do not abort the
            // query. Mirrors the active-root pre-normalization in
            // SongCatalogStore.GetActiveChartCountAsync.
            var normalizedRoots = roots
                .Select(root =>
                {
                    SongPathIdentity.TryNormalize(root, out var normalized);
                    return normalized;
                })
                .Where(r => !string.IsNullOrEmpty(r))
                .ToArray();
            if (normalizedRoots.Length == 0)
                return Array.Empty<int>();
            using var context = _database.CreateContext();
            var charts = await context.SongCharts
                .AsNoTracking()
                .Select(chart => new { chart.Id, chart.FilePath })
                .ToListAsync();
            var activeIds = new List<int>(charts.Count);
            foreach (var chart in charts)
            {
                if (SongPathIdentity.TryNormalize(chart.FilePath, out var normalized) &&
                    normalizedRoots.Any(root =>
                        SongPathIdentity.IsUnderRoot(normalized, root)))
                {
                    activeIds.Add(chart.Id);
                }
            }
            return activeIds;
        }

        /// <summary>
        /// Returns all bookmarked songs ordered alphabetically by Title (Id as a stable
        /// tiebreak). Each Song has its Charts and the charts' Scores eagerly loaded so
        /// callers can build fully-populated SongListNodes, mirroring
        /// <see cref="GetRecentlyPlayedSongsAsync"/>.
        /// </summary>
        internal async Task<List<SongEntity>> GetBookmarkedSongsAsync()
        {
            using var context = _database.CreateContext();
            return await context.Songs
                .Where(s => s.IsBookmarked)
                .Include(s => s.Charts)
                    .ThenInclude(c => c.Scores)
                        .ThenInclude(sc => sc.PerformanceHistory)
                .OrderBy(s => s.Title)
                .ThenBy(s => s.Id)
                .ToListAsync();
        }

        private SongChart[] GetActiveCharts(SongEntity song) =>
            GetActiveCharts(song, _currentSearchPathsSnapshot());

        private static SongChart[] GetActiveCharts(SongEntity song, string[] roots)
        {
            var charts = song.Charts?.ToArray() ?? Array.Empty<SongChart>();
            if (roots.Length == 0)
                return charts;

            return charts.Where(chart =>
                SongPathIdentity.TryNormalize(
                    chart.FilePath,
                    out var normalized) &&
                roots.Any(root =>
                    SongPathIdentity.IsUnderRoot(normalized, root)))
                .ToArray();
        }
    }
}
