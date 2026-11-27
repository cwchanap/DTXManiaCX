#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.Utilities;

namespace DTXMania.Game.Lib.Stage.SongSelection
{
    /// <summary>
    /// Why the song-select list shows a recovery message instead of songs (HPA-802).
    /// </summary>
    internal enum SongSelectionLibraryEmptyState
    {
        HasSongs,
        NoActiveRoots,
        NoSupportedCharts
    }

    /// <summary>
    /// Passive song library intake for the song-select stage (HPA-802). Owns the
    /// publication subscription, the applied snapshot/score-identity index/version
    /// marker, the active roots, the flat-row eligibility filter and the
    /// library-empty classification.
    ///
    /// The intake is passive: a publisher thread only reports a version into the
    /// activation scope captured by <see cref="Subscribe"/>; the update thread
    /// fetches the coherent snapshot (<see cref="TryGetNewer"/>), installs it
    /// (<see cref="Install"/>), lets the stage reconcile its UI, and only then
    /// acknowledges the version (<see cref="Commit"/>). The library is resolved
    /// lazily through the constructor accessor — construction does no database or
    /// filesystem work.
    /// </summary>
    internal sealed class SongSelectionLibraryIntake
    {
        private readonly Func<SongLibrary> _getLibrary;
        private SongLibrarySnapshot? _appliedSnapshot;
        private HashSet<string>? _appliedScoreIdentities;
        private Dictionary<int, bool>? _appliedBookmarkStates;
        private long _committedVersion;
        private SongSelectionLibraryEmptyState _emptyState = SongSelectionLibraryEmptyState.HasSongs;
        private EventHandler<SongLibraryPublishedEventArgs>? _publicationHandler;
        private SongLibrary? _subscribedLibrary;

        public SongSelectionLibraryIntake(Func<SongLibrary> getLibrary)
        {
            _getLibrary = getLibrary ?? throw new ArgumentNullException(nameof(getLibrary));
        }

        /// <summary>The coherent snapshot whose UI projection is currently shown.</summary>
        public SongLibrarySnapshot? AppliedSnapshot => _appliedSnapshot;

        /// <summary>
        /// Active roots of the applied snapshot, or null when nothing was applied yet
        /// ("no root contract yet", not an intentionally empty root set).
        /// </summary>
        public IReadOnlyList<string>? AppliedActiveRoots => _appliedSnapshot?.ActiveRoots;

        public SongSelectionLibraryEmptyState EmptyState => _emptyState;

        /// <summary>Newest acknowledged publication version (signal or applied snapshot).</summary>
        public long CommittedVersion => Volatile.Read(ref _committedVersion);

        /// <summary>
        /// Binds the stage's publication handler to THIS activation's scope reporter:
        /// the publisher thread only records the newest version signal, and the update
        /// thread fetches the snapshot when the drained apply runs (HPA-799). No scope
        /// (headless stage without the seam) means nothing could apply publications, so
        /// no handler is registered. Re-subscribing first unbinds any previous binding.
        /// </summary>
        public void Subscribe(StageBackgroundWorkScope? scope, Action<long> onSignal)
        {
            ArgumentNullException.ThrowIfNull(onSignal);
            Unsubscribe();

            if (scope == null)
                return;

            var reporter = scope.Latest<long>(onSignal);

            // The registered delegate closes over THIS activation's reporter. A publisher
            // that retains the handler past deactivation can then only reach the retired
            // scope, which drops the signal — it can never enqueue into a later
            // activation's scope.
            void PublishToActivationScope(object? sender, SongLibraryPublishedEventArgs e) =>
                reporter(e.Snapshot.Version);

            var library = _getLibrary();
            _publicationHandler = PublishToActivationScope;
            _subscribedLibrary = library;
            library.SongLibraryPublished += _publicationHandler;
        }

        /// <summary>
        /// Detaches the registered handler from the actually subscribed library
        /// instance (never a fresh lookup) and clears the binding. Idempotent.
        /// </summary>
        public void Unsubscribe()
        {
            var handler = _publicationHandler;
            var library = _subscribedLibrary;
            _publicationHandler = null;
            _subscribedLibrary = null;

            if (handler != null)
                library!.SongLibraryPublished -= handler;
        }

        /// <summary>
        /// Drops the applied snapshot/index/version and empty classification back to
        /// activation defaults and unsubscribes. Called on activation (after the prior
        /// activation's scope was retired) and on teardown.
        /// </summary>
        public void Reset()
        {
            Unsubscribe();
            _appliedSnapshot = null;
            _appliedScoreIdentities = null;
            _appliedBookmarkStates = null;
            _emptyState = SongSelectionLibraryEmptyState.HasSongs;
            Interlocked.Exchange(ref _committedVersion, 0);
        }

        /// <summary>
        /// Fetches the current coherent snapshot (update thread only — never a copy
        /// taken on a worker) and returns it only if it should replace the applied one.
        /// The marker is NOT advanced here; acknowledgment happens via
        /// <see cref="Commit"/> after the stage reconciled successfully.
        ///
        /// Acceptance: a signal of 0 (the initializer poll) is accepted while no
        /// snapshot has been installed — even at version zero — and otherwise only
        /// when the newer of the signal and the fetched snapshot version exceeds the
        /// committed version, so an older fetch can never reapply over a newer
        /// publication.
        /// </summary>
        public SongLibrarySnapshot? TryGetNewer(long signal)
        {
            var snapshot = _getLibrary().GetLibrarySnapshot();

            if ((_appliedSnapshot == null && signal == 0)
                || Math.Max(signal, snapshot.Version) > Volatile.Read(ref _committedVersion))
            {
                return snapshot;
            }

            return null;
        }

        /// <summary>
        /// Assigns the applied snapshot and computes its score-identity index and
        /// empty classification. UI projection stays with the stage.
        /// </summary>
        public void Install(SongLibrarySnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);

            _appliedSnapshot = snapshot;
            var identities = new HashSet<string>(StringComparer.Ordinal);
            var bookmarkStates = new Dictionary<int, bool>();
            AddPublishedScoreIdentities(snapshot.RootSongs, identities, bookmarkStates);
            _appliedScoreIdentities = identities;
            _appliedBookmarkStates = bookmarkStates;
            _emptyState = ResolveLibraryEmptyState(snapshot);
        }

        /// <summary>
        /// Looks up the bookmark state indexed from the applied snapshot. Missing ids
        /// simply have no canonical tree state; callers keep the row's own state.
        /// </summary>
        internal bool TryGetAppliedBookmarkState(int songId, out bool isBookmarked)
        {
            if (_appliedBookmarkStates != null
                && _appliedBookmarkStates.TryGetValue(songId, out isBookmarked))
            {
                return true;
            }

            isBookmarked = false;
            return false;
        }

        /// <summary>
        /// Acknowledges <paramref name="version"/> after the stage reconciled its UI
        /// successfully. Advancing is monotonic so a late signal can never lower the
        /// marker below an already-applied snapshot version (the publication handler
        /// commits the newer of the signal and the fetched snapshot).
        /// </summary>
        public void Commit(long version)
        {
            if (version > Volatile.Read(ref _committedVersion))
                Interlocked.Exchange(ref _committedVersion, version);
        }

        /// <summary>
        /// Restores intake state after a failed stage reconciliation (HPA-802):
        /// reinstalls the prior snapshot's derived state, or — when no snapshot was
        /// applied before the failed attempt — clears everything back to the
        /// pre-initialization placeholder state. The committed version is never
        /// touched: a failed reconciliation acknowledged nothing, so the next drain
        /// retries against intact state.
        /// </summary>
        public void Rollback(SongLibrarySnapshot? snapshot)
        {
            if (snapshot == null)
            {
                _appliedSnapshot = null;
                _appliedScoreIdentities = null;
                _appliedBookmarkStates = null;
                _emptyState = SongSelectionLibraryEmptyState.HasSongs;
                return;
            }

            Install(snapshot);
        }

        /// <summary>
        /// Projects a cached flat tab against the coherent library currently applied by
        /// the stage. The cache remains intact so a chart that is re-published under an
        /// active root becomes eligible again without a destructive cache rebuild.
        /// </summary>
        public List<SongListNode> Filter(IEnumerable<SongListNode>? rows)
        {
            var rootFilteredNodes = FilterNodesForActiveRoots(rows, _appliedSnapshot?.ActiveRoots);
            if (_appliedSnapshot == null || rootFilteredNodes.Count == 0)
                return rootFilteredNodes;

            var publishedScoreIdentities = _appliedScoreIdentities;
            if (publishedScoreIdentities == null || publishedScoreIdentities.Count == 0)
                return new List<SongListNode>();

            return rootFilteredNodes
                .Where(node => HasPublishedScoreIdentity(node, publishedScoreIdentities))
                .ToList();
        }

        #region Pure eligibility and classification helpers (plain snapshots and nodes)

        internal static List<SongListNode> FilterNodesForActiveRoots(
            IEnumerable<SongListNode>? nodes,
            IReadOnlyList<string>? activeRoots)
        {
            if (nodes == null)
                return new List<SongListNode>();

            // Unit-level callers that have not applied a snapshot still exercise the legacy
            // tab rendering path. A live stage always has an applied snapshot before it draws,
            // so null means "no root contract yet", not an intentionally empty root set.
            if (activeRoots == null)
            {
                try
                {
                    return nodes.ToList();
                }
                catch (InvalidOperationException)
                {
                    return new List<SongListNode>();
                }
            }

            if (activeRoots.Count == 0)
                return new List<SongListNode>();

            var normalizedRoots = new List<string>(activeRoots.Count);
            foreach (var root in activeRoots)
            {
                if (SongPathIdentity.TryNormalize(root, out var normalized))
                    normalizedRoots.Add(normalized);
            }

            if (normalizedRoots.Count == 0)
                return new List<SongListNode>();

            SongListNode[] copiedNodes;
            try
            {
                copiedNodes = nodes.ToArray();
            }
            catch (InvalidOperationException)
            {
                // A caller supplied a mutable list being replaced concurrently. Do not leak
                // a collection-modified exception into the update thread; the next refresh
                // will project the replacement list.
                return new List<SongListNode>();
            }

            var filtered = new List<SongListNode>(copiedNodes.Length);
            foreach (var node in copiedNodes)
            {
                if (NodeIsUnderAnyActiveRoot(node, normalizedRoots))
                    filtered.Add(node);
            }

            return filtered;
        }

        internal static HashSet<string> GetPublishedScoreIdentities(
            IEnumerable<SongListNode> nodes)
        {
            var identities = new HashSet<string>(StringComparer.Ordinal);
            AddPublishedScoreIdentities(nodes, identities);
            return identities;
        }

        private static void AddPublishedScoreIdentities(
            IEnumerable<SongListNode> nodes,
            ISet<string> identities,
            Dictionary<int, bool>? bookmarkStates = null)
        {
            foreach (var node in nodes)
            {
                foreach (var identity in GetScoreIdentityCandidates(node))
                    identities.Add(identity);

                int songId = node.DatabaseSongId ?? node.DatabaseSong?.Id ?? 0;
                if (bookmarkStates != null
                    && node.DatabaseSong != null
                    && songId > 0)
                {
                    bookmarkStates[songId] = node.DatabaseSong.IsBookmarked;
                }

                if (node.Children.Count > 0)
                    AddPublishedScoreIdentities(node.Children, identities, bookmarkStates);
            }
        }

        internal static bool HasPublishedScoreIdentity(
            SongListNode node,
            ISet<string> publishedScoreIdentities)
        {
            return GetScoreIdentityCandidates(node)
                .Any(publishedScoreIdentities.Contains);
        }

        internal static IEnumerable<string> GetScoreIdentityCandidates(SongListNode node)
        {
            if (node.Type != NodeType.Score)
                yield break;

            int songId = node.DatabaseSongId ?? node.DatabaseSong?.Id ?? 0;
            if (songId > 0)
                yield return $"song:{songId}";

            foreach (var path in GetNodeChartPaths(node))
            {
                if (SongPathIdentity.TryNormalize(path, out var normalized))
                    yield return $"chart:{normalized}";
            }
        }

        internal static bool NodeIsUnderAnyActiveRoot(
            SongListNode node,
            IReadOnlyList<string> normalizedRoots)
        {
            foreach (var path in GetNodeChartPaths(node))
            {
                if (!SongPathIdentity.TryNormalize(path, out var normalizedPath))
                    continue;

                foreach (var root in normalizedRoots)
                {
                    if (SongPathIdentity.IsUnderNormalizedRoot(normalizedPath, root))
                        return true;
                }
            }

            return false;
        }

        internal static IEnumerable<string> GetNodeChartPaths(SongListNode node)
        {
            if (!string.IsNullOrWhiteSpace(node.DatabaseChart?.FilePath))
                yield return node.DatabaseChart.FilePath;

            // Same filtered set as EnumerateNodeCharts: paths belonging to
            // dropped (missing-file) chart rows must not count toward node
            // identity or active-root membership.
            var charts = node.ExistingCharts
                ?? (IEnumerable<SongChart>?)node.DatabaseSong?.Charts?.ToArray();
            if (charts != null)
            {
                foreach (var chart in charts)
                {
                    if (!string.IsNullOrWhiteSpace(chart?.FilePath))
                        yield return chart.FilePath;
                }
            }

            if (!string.IsNullOrWhiteSpace(node.DirectoryPath))
                yield return node.DirectoryPath;
        }

        internal static SongSelectionLibraryEmptyState ResolveLibraryEmptyState(
            SongLibrarySnapshot snapshot)
        {
            if (snapshot.ActiveRoots.Count == 0)
                return SongSelectionLibraryEmptyState.NoActiveRoots;

            return ContainsPublishedScore(snapshot.RootSongs)
                ? SongSelectionLibraryEmptyState.HasSongs
                : SongSelectionLibraryEmptyState.NoSupportedCharts;
        }

        internal static string ResolveLibraryEmptyMessage(SongSelectionLibraryEmptyState state) => state switch
        {
            SongSelectionLibraryEmptyState.NoActiveRoots =>
                "No usable song folder - see CONFIG > Song Folders",
            SongSelectionLibraryEmptyState.NoSupportedCharts =>
                "No supported charts - see CONFIG > Song Folders",
            _ => string.Empty,
        };

        internal static bool ContainsPublishedScore(IEnumerable<SongListNode> nodes)
        {
            foreach (var node in nodes)
            {
                if (node.Type == NodeType.Score)
                    return true;
                if (node.Children.Count > 0 && ContainsPublishedScore(node.Children))
                    return true;
            }

            return false;
        }

        #endregion
    }
}
