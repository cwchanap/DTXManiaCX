#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.Config;
using Microsoft.Extensions.Logging;

namespace DTXMania.Game.Lib.Stage.SongSelection
{
    /// <summary>
    /// Owns the song-select tabs and the durable bookmark write path (HPA-802):
    /// the active tab, the Recent/Bookmarks caches with their distinct empty/error
    /// states, the async-tab-refresh request/consumption protocol, flat-tab loads on
    /// the activation scope, and bookmark toggles (per-song serialized writes,
    /// toggle generations, revert gate and the bounded disposal drain).
    ///
    /// The stage owns input/overlay policy, navigation and the visible-list
    /// projection; this owner resolves the library lazily through the constructor
    /// accessor (no database or filesystem work at construction) and reads the
    /// intake's applied snapshot and eligibility filter directly. Pending bookmark
    /// writes, per-song toggle generations and the revert gate are stage-instance
    /// lifetime: they survive activation resets so durable writes chain and persist
    /// across navigation, and only the origin-scope UI rollback drops.
    /// </summary>
    internal sealed class SongSelectionTabs
    {
        private const int RecentPlaysLimit = 20;        // Max rows shown on the Recent tab.

        private readonly Func<SongLibrary> _getLibrary;
        private readonly SongSelectionLibraryIntake _intake;
        private readonly Func<StageBackgroundWorkScope?> _getScope;
        private readonly ILogger _logger;
        private readonly Func<Task<List<SongListNode>>> _loadRecentPlaysAsync;
        private readonly Func<Task<List<SongListNode>>> _loadBookmarkedNodesAsync;
        // Receives the library CAPTURED AT TOGGLE TIME so a queued (chained) write always
        // executes against the instance the toggle saw — the accessor may have been
        // cleared (shutdown) or re-pointed at a newer activation's library by the time a
        // serialized write actually runs.
        private readonly Func<SongLibrary, int, bool, Task> _setBookmarkAsync;

        /// <summary>
        /// I/O-EXECUTION SEAM (HPA-802 fix round 2 follow-up, test-only): wraps the
        /// real <see cref="StageBackgroundWorkScope.Run"/> query submission of the
        /// shared tab-load path. Production passes null — the default below calls the
        /// scope's actual Run with the same worker and terminals (same worker, same
        /// behavior). A test wrapper may synchronously record the submission (before
        /// any worker can run) and delegate to the real scope, optionally gating the
        /// actual worker, so the deferred-wait and supersession interleavings can
        /// assert zero/exactly-one ISSUE at a boundary. Not production telemetry: no
        /// counters, notifications, queues or events in production.
        /// </summary>
        internal delegate Task<IReadOnlyList<SongListNode>?> SubmitTabLoadQuery(
            StageBackgroundWorkScope scope,
            Func<CancellationToken, IReadOnlyList<SongListNode>> work,
            Action<IReadOnlyList<SongListNode>> onSuccess,
            Action<Exception> onError);

        // The I/O-execution seam (see SubmitTabLoadQuery); resolves to the real
        // scope.Run query submission in production.
        private readonly SubmitTabLoadQuery _submitTabLoadQuery;

        // Active tab. The stage instance is cached per game; ResetForActivation returns it
        // to AllSongs so every activation starts there.
        private SongSelectionTab _activeTab = SongSelectionTab.AllSongs;

        // Cached recent-plays nodes (flat Score nodes), refreshed on activation and on
        // switching into the Recent tab. Null until first load. Worker completions only
        // enqueue immutable records; this and all related flags are update-thread owned.
        private List<SongListNode>? _recentPlayNodes;
        private bool _showEmptyRecentMessage;
        // True when the most recent recent-plays load continuation failed (DB error,
        // IO error, etc.). Distinct from _showEmptyRecentMessage so a corrupted/locked
        // score DB doesn't look identical to "no plays yet."
        private bool _recentPlaysLoadFailed;
        // Cached bookmark nodes (flat Score nodes), refreshed on activation and on switching
        // into the Bookmarks tab. Worker completions only enqueue immutable records; this and
        // all related flags are update-thread owned.
        private List<SongListNode>? _bookmarkNodes;
        private bool _showEmptyBookmarksMessage;
        // True when the most recent bookmarks load continuation failed (DB/IO error).
        private bool _bookmarksLoadFailed;
        // Per-load sequence guard for bookmarks loads. Incremented on the update thread so
        // a queued completion from an older same-activation load cannot overwrite the result
        // of a newer load with a pre-toggle snapshot that omits a newly bookmarked song.
        // Bumped by a toggle, by a refresh REQUEST (request-time invalidation, so a query
        // still running can never apply rows after a newer refresh has been requested) and
        // by the actual query launch (fresh generation).
        private int _bookmarksLoadVersion;
        // The query generation captured by the most recent ACTUAL bookmarks launch, also
        // updated only on the update thread. A stale completion must retry its refresh
        // intent only when the invalidation came from a toggle (current generation above
        // this value, nothing launched for it yet); when a sibling launch owns the current
        // generation the stale completion drops instead — without this check, a retried
        // launch would invalidate its still-in-flight sibling, whose own stale completion
        // would retry again, cascading without end.
        private int _bookmarksLaunchedLoadVersion;
        // STALE RECENT CORRECTION (HPA-802): the Recent tab owns the same per-load
        // generation guard as Bookmarks. The old code applied Recent results in completion
        // order, so a slower OLDER load could overwrite a newer load's rows — or flag the
        // load failed with a stale error. Incremented on the update thread at each
        // BeginRecentPlaysLoad; older success AND error completions are rejected.
        private int _recentPlaysLoadVersion;
        // DEFERRED BOOKMARKS CORRECTION (HPA-802): monotonic refresh INTENT id, separate
        // from the _bookmarksLoadVersion query generation. Every Bookmark refresh request
        // (tab switch, activation warm load, failed-write rollback reload) enters the same
        // wait path and increments this id, so a newer request supersedes an older
        // outstanding wait. A bookmark toggle bumps ONLY the query generation — never this
        // id — so it invalidates an already-running query without canceling the owed
        // reload. Stage-instance lifetime: it is deliberately not reset on activation.
        private int _deferredBookmarksRequestId;
        // Set when the active tab's list must be repopulated on the next OnUpdate. It is owned
        // by the update thread; background completions queue into the activation scope instead.
        private bool _tabListNeedsRefresh;
        // Completion application increments this before requesting a refresh. The update thread
        // consumes the value around a projection so a refresh requested during reconciliation is
        // re-queued instead of being lost by a stale false write.
        private int _asyncTabListRefreshVersion;

        // Serializes bookmark persistence writes per song. Each toggle chains after the
        // in-flight write for the same song so rapid double-toggles apply in order and the
        // last user intent wins in the database, instead of a fire-and-forget race that can
        // leave the DB divergent from the in-memory flag. ToggleBookmark runs on the update
        // thread, so dictionary access here is single-threaded.
        private readonly ConcurrentDictionary<int, Task> _pendingBookmarkWrites = new();
        private readonly ConcurrentDictionary<int, bool> _bookmarkPersistedStates = new();

        // Monotonic per-song toggle generation. Bumped on every toggle so a persist fault from
        // an older toggle can be detected and its rollback discarded when a newer toggle has
        // since changed the in-memory flag (otherwise a stale fault would override a successful
        // later toggle). Bumped and read on the update thread; the value is captured immutably
        // by the background write continuation, which also reads it (thread-safe dictionary
        // access) to version-guard the worker-side shared-entity revert.
        private readonly ConcurrentDictionary<int, int> _bookmarkToggleVersion = new();

        // Serializes the bookmark revert decision across threads: the update-thread toggle
        // bump and the worker-side fault continuation's version check + shared-snapshot
        // revert hold this same gate, so a newer toggle's bump can never slip between the
        // stale continuation's version check and its revert (check-then-write atomicity).
        // Held only for in-memory flag work — never across DB I/O.
        private readonly object _bookmarkRevertGate = new();

        public SongSelectionTabs(
            Func<SongLibrary> getLibrary,
            SongSelectionLibraryIntake intake,
            Func<StageBackgroundWorkScope?> getScope,
            ILogger logger,
            Func<Task<List<SongListNode>>>? loadRecentPlaysAsync = null,
            Func<Task<List<SongListNode>>>? loadBookmarkedNodesAsync = null,
            Func<SongLibrary, int, bool, Task>? setBookmarkAsync = null,
            SubmitTabLoadQuery? submitTabLoadQuery = null)
        {
            _getLibrary = getLibrary ?? throw new ArgumentNullException(nameof(getLibrary));
            _intake = intake ?? throw new ArgumentNullException(nameof(intake));
            _getScope = getScope ?? throw new ArgumentNullException(nameof(getScope));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            // Lazy library resolution (HPA-801): the queries resolve when a load or write
            // executes, never at construction.
            _loadRecentPlaysAsync = loadRecentPlaysAsync ??
                (() => _getLibrary().Queries.GetRecentlyPlayedNodesAsync(RecentPlaysLimit));
            _loadBookmarkedNodesAsync = loadBookmarkedNodesAsync ??
                (() => _getLibrary().Queries.GetBookmarkedNodesAsync());
            _setBookmarkAsync = setBookmarkAsync ??
                ((library, songId, newState) => library.Queries.SetBookmarkAsync(songId, newState));
            // Production default: the scope's ACTUAL Run query submission.
            _submitTabLoadQuery = submitTabLoadQuery ??
                ((scope, work, onSuccess, onError) => scope.Run(work, onSuccess, onError));
        }

        /// <summary>The tab whose rows the stage projects.</summary>
        public SongSelectionTab ActiveTab => _activeTab;

        /// <summary>True when the active tab's list must be repopulated on the next update.</summary>
        public bool TabListNeedsRefresh => _tabListNeedsRefresh;

        /// <summary>True when the most recent Recent-plays load failed (distinct from empty).</summary>
        public bool RecentPlaysLoadFailed => _recentPlaysLoadFailed;

        /// <summary>True when a successful Recent-plays load returned no rows.</summary>
        public bool ShowEmptyRecentMessage => _showEmptyRecentMessage;

        /// <summary>True when the most recent Bookmarks load failed (distinct from empty).</summary>
        public bool BookmarksLoadFailed => _bookmarksLoadFailed;

        /// <summary>True when a successful Bookmarks load returned no rows.</summary>
        public bool ShowEmptyBookmarksMessage => _showEmptyBookmarksMessage;

        /// <summary>The cached Recent-plays rows; null until the first load applied.</summary>
        public List<SongListNode>? RecentPlayNodes => _recentPlayNodes;

        /// <summary>The cached Bookmarks rows; null until the first load applied.</summary>
        public List<SongListNode>? BookmarkNodes => _bookmarkNodes;

        /// <summary>
        /// Drops the per-activation tab state back to activation defaults. The durable
        /// bookmark write state (pending writes, toggle generations, revert gate, load
        /// generations) is stage-instance lifetime and is intentionally kept.
        /// </summary>
        public void ResetForActivation()
        {
            _activeTab = SongSelectionTab.AllSongs;
            _recentPlayNodes = null;
            _showEmptyRecentMessage = false;
            _recentPlaysLoadFailed = false;
            _bookmarkNodes = null;
            _showEmptyBookmarksMessage = false;
            _bookmarksLoadFailed = false;
            // Clear any stale refresh flag from the previous activation (e.g., a tab-switch
            // that set the flag just before Deactivate). Without this, the first OnUpdate
            // would repopulate the All Songs list and reset selection/scroll to index 0.
            _tabListNeedsRefresh = false;
            Interlocked.Exchange(ref _asyncTabListRefreshVersion, 0);
        }

        /// <summary>
        /// Switches the active tab, kicks a recent-plays reload when entering the Recent
        /// tab, defers the Bookmarks load behind any in-flight bookmark writes, and
        /// requests a list repopulate on the next update. Input policy, sounds and
        /// status-panel exit stay with the stage.
        /// Returns the Bookmarks refresh's FIRST worker step (see
        /// <see cref="RequestBookmarksRefresh"/>): the task completes only after THAT
        /// step's own terminal is enqueued into the activation scope — or without
        /// enqueueing anything when the scope was retired. It is NOT a barrier for the
        /// whole refresh: a wait step's re-read and the eventual query enqueue their
        /// own terminals afterwards. Null means no Bookmarks refresh step was issued
        /// (no activation scope). Switching to the Recent tab ALSO returns null even
        /// though it launches a Recent load — Recent loads carry no write-ordering
        /// constraint, so no step task is surfaced. The stage ignores it; callers may
        /// observe it.
        /// </summary>
        public Task? SwitchToTab(SongSelectionTab tab)
        {
            _activeTab = tab;
            Task? deferredRefresh = null;

            if (_activeTab == SongSelectionTab.RecentPlays)
                BeginRecentPlaysLoad();

            if (_activeTab == SongSelectionTab.Bookmarks)
            {
                // DEFERRED BOOKMARKS CORRECTION (HPA-802): switching into Bookmarks no
                // longer posts a bare load. The refresh is an intent (request id + origin
                // scope) that waits for in-flight writes and re-reads pending writes at
                // each settle, so the eventual query reflects the latest toggles — a
                // bookmark-on followed by a quick tab switch can no longer query the DB
                // before SetBookmarkAsync commits and omit the just-bookmarked song.
                deferredRefresh = RequestBookmarksRefresh();
            }

            _tabListNeedsRefresh = true;
            return deferredRefresh;
        }

        /// <summary>
        /// Requests a Bookmarks refresh through the deferred wait path: the intent is
        /// registered as a new request id (superseding any older outstanding wait), any
        /// already-running Bookmarks query is invalidated AT REQUEST TIME, and the
        /// current activation scope is captured. The actual query launches only after
        /// every pending bookmark write has settled — through the captured scope, request
        /// id and (at launch) a fresh query generation. Used by the tab switch, the
        /// activation warm load and the failed-write rollback reload — every Bookmark
        /// refresh request enters this one path, and it is the ONLY externally callable
        /// Bookmarks refresh entry (the raw query launch is private). Update thread only.
        /// Returns the refresh's FIRST worker step — the deferred wait, or the
        /// immediately launched query when no writes are pending. The task completes
        /// only after THAT step's terminal is enqueued into the activation scope, or
        /// without enqueueing anything when the scope was retired; it is NOT a barrier
        /// for the whole refresh (a wait step's re-read and the eventual query enqueue
        /// their own terminals afterwards). Null means no step was issued (no
        /// activation scope). The stage ignores it; callers may observe it.
        /// </summary>
        public Task? RequestBookmarksRefresh()
        {
            int requestId = Interlocked.Increment(ref _deferredBookmarksRequestId);
            // REQUEST-TIME INVALIDATION (HPA-802 review): invalidate any already-running
            // query here, not only at the eventual launch — a warm query still in flight
            // must never apply rows or an error after a newer refresh has been requested
            // but before that refresh's own query launches.
            Interlocked.Increment(ref _bookmarksLoadVersion);
            var scope = _getScope();
            if (scope == null)
                return null;

            return AwaitPendingWritesThenLoadBookmarks(scope, requestId);
        }

        /// <summary>
        /// The deferred Bookmarks wait, following the required update-thread sequence:
        /// the incomplete pending-write snapshot is taken HERE on the update thread, the
        /// worker awaits exactly that batch once (completion, fault and a write's own
        /// cancellation all settle it; a scope retirement cancels the wait silently and
        /// never cancels the writes), and the scope drain applies the continuation on the
        /// update thread. There the request id is checked FIRST — superseded requests
        /// drop — and only then are pending writes re-read: newer incomplete writes
        /// re-enter this same method with the SAME request id and scope, and a fully
        /// settled pass launches the actual query via <see cref="BeginLoad"/> with a
        /// fresh query generation. No poll loop on any thread: each settle is one await.
        /// Update thread only (it snapshots and launches; only the await runs on a
        /// worker).
        /// </summary>
        private Task? AwaitPendingWritesThenLoadBookmarks(
            StageBackgroundWorkScope scope,
            int requestId)
        {
            // Update thread: snapshot the currently incomplete writes once. The worker
            // below awaits exactly this batch; re-reading happens on the update thread at
            // the drain, so an obsolete wait can never keep following newer writes.
            var incomplete = _pendingBookmarkWrites.Values.Where(write => !write.IsCompleted).ToArray();
            if (incomplete.Length == 0)
            {
                // Nothing to wait for: launch the actual query right away on the update
                // thread, with a fresh query generation and the captured scope.
                return BeginLoad(scope, SongSelectionTab.Bookmarks);
            }

            return scope.RunAsync<int>(
                async token =>
                {
                    try
                    {
                        await Task.WhenAll(incomplete).WaitAsync(token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        // Retirement: the scope drops the owed query silently; the
                        // durable writes themselves are never canceled.
                        throw;
                    }
                    catch (Exception)
                    {
                        // A write settled with a fault OR its own cancellation — the OCE
                        // of a canceled write is the write's own, not the token's — and
                        // that still releases the wait for this snapshot batch.
                    }

                    return 0;
                },
                onSuccess: _ =>
                {
                    // Update thread (scope drain). A newer refresh request supersedes
                    // this intent; check BEFORE re-reading pending writes.
                    if (requestId != Volatile.Read(ref _deferredBookmarksRequestId))
                        return;

                    // Re-read on the update thread: writes chained while we waited
                    // re-enter with the SAME request id and scope so the owed reload
                    // cannot be lost.
                    AwaitPendingWritesThenLoadBookmarks(scope, requestId);
                });
        }

        /// <summary>
        /// Sets the active tab without load or refresh side effects. Used only by the
        /// prepared-chart projection and its rollback, which must not kick loads or
        /// repopulate the list.
        /// </summary>
        public void SetActiveTabQuiet(SongSelectionTab tab)
        {
            _activeTab = tab;
        }

        /// <summary>
        /// Loads recent-plays nodes on the activation scope. Thin wrapper over the shared
        /// private BeginLoad(tab) execution path. Recent loads carry no write-ordering
        /// constraint, so this stays a public entry (unlike the Bookmarks raw launcher).
        /// STALE RECENT CORRECTION (HPA-802): each Recent load captures its own query
        /// generation, so a slower older load can no longer apply a stale success or a
        /// stale failure over a newer load's result.
        /// Returns the load's worker task; it completes only after the load's terminal
        /// (success or error) is enqueued into the activation scope — or without
        /// enqueueing anything when the scope was retired. Null when no scope is
        /// active. The stage ignores it; callers may observe it.
        /// </summary>
        public Task? BeginRecentPlaysLoad() => BeginLoad(SongSelectionTab.RecentPlays);

        /// <summary>
        /// The one BeginLoad(tab) query execution path for Recent and Bookmarks (HPA-802):
        /// resolves the activation scope, then runs the shared worker fetch/copy +
        /// scope-drain apply machinery with the per-tab loader, per-tab query generation
        /// and per-tab success/error application selected here. Update thread only.
        /// PRIVATE (HPA-802 review follow-up): there is deliberately no public raw
        /// Bookmarks query launch — every externally callable Bookmarks refresh entry is
        /// <see cref="RequestBookmarksRefresh"/>, so pending writes can never be bypassed.
        /// Recent loads stay public via <see cref="BeginRecentPlaysLoad"/>; they carry no
        /// write-ordering constraint.
        /// Returns the load's worker task (see
        /// <see cref="BeginRecentPlaysLoad"/> for the single-step completion contract).
        /// </summary>
        private Task? BeginLoad(SongSelectionTab tab)
        {
            var scope = _getScope();
            if (scope == null)
                return null;

            return BeginLoad(scope, tab);
        }

        /// <summary>
        /// Captured-scope overload of the shared execution path: the deferred wait path
        /// and a stale completion's retry launch through the SAME scope their request was
        /// captured on — re-resolving the scope here could land a stale request's work in
        /// a newer activation. Update thread only. Returns the load's worker task (see
        /// <see cref="BeginRecentPlaysLoad"/> for the single-step completion contract).
        /// </summary>
        private Task BeginLoad(StageBackgroundWorkScope scope, SongSelectionTab tab)
        {
            switch (tab)
            {
                case SongSelectionTab.RecentPlays:
                {
                    // Monotonic per-load id, assigned on the update thread: an older load
                    // whose completion lands after a newer request must reject its stale
                    // result AND its stale error.
                    int capturedLoadVersion = Interlocked.Increment(ref _recentPlaysLoadVersion);
                    var loadRecentPlaysAsync = _loadRecentPlaysAsync;
                    return RunTabLoad(
                        scope,
                        loadRecentPlaysAsync,
                        capturedLoadVersion,
                        () => Volatile.Read(ref _recentPlaysLoadVersion),
                        applySuccess: nodes =>
                        {
                            _recentPlaysLoadFailed = false;
                            var recentNodes = nodes.ToList();
                            ApplyCurrentBookmarkStates(recentNodes);
                            _recentPlayNodes = recentNodes;
                            if (_activeTab == SongSelectionTab.RecentPlays)
                                RequestAsyncTabListRefresh();
                        },
                        applyError: () =>
                        {
                            _recentPlaysLoadFailed = true;
                            if (_activeTab == SongSelectionTab.RecentPlays)
                                RequestAsyncTabListRefresh();
                        },
                        // Recent owns no deferred refresh intent, so a stale completion
                        // has nothing to retry.
                        onStaleCompletion: null);
                }
                case SongSelectionTab.Bookmarks:
                {
                    // The launch does NOT create a new refresh intent: it captures the
                    // current request id so a stale completion can tell a toggle
                    // invalidation (id still current → retry) from a newer request (id
                    // superseded → drop). The query generation below is what makes the
                    // launch "fresh".
                    int capturedRequestId = Volatile.Read(ref _deferredBookmarksRequestId);
                    // Monotonic per-load id, assigned on the update thread so an older
                    // same-activation load can discard its stale result.
                    int capturedLoadVersion = Interlocked.Increment(ref _bookmarksLoadVersion);
                    Volatile.Write(ref _bookmarksLaunchedLoadVersion, capturedLoadVersion);
                    var loadBookmarkedNodesAsync = _loadBookmarkedNodesAsync;
                    return RunTabLoad(
                        scope,
                        loadBookmarkedNodesAsync,
                        capturedLoadVersion,
                        () => Volatile.Read(ref _bookmarksLoadVersion),
                        applySuccess: nodes =>
                        {
                            _bookmarksLoadFailed = false;
                            _bookmarkNodes = nodes.ToList();
                            if (_activeTab == SongSelectionTab.Bookmarks)
                                RequestAsyncTabListRefresh();
                        },
                        applyError: () =>
                        {
                            _bookmarksLoadFailed = true;
                            if (_activeTab == SongSelectionTab.Bookmarks)
                                RequestAsyncTabListRefresh();
                        },
                        // Retry only for Bookmarks: only a toggle invalidation leaves the
                        // request id current, and the owed refresh then re-enters the wait
                        // path without applying these rows.
                        onStaleCompletion: () => RetryBookmarksRefreshIfCurrent(scope, capturedRequestId));
                }
            }

            throw new ArgumentOutOfRangeException(nameof(tab), tab, "flat tab has no query path");
        }

        /// <summary>
        /// The shared worker fetch/copy + scope-drain apply machinery behind
        /// <see cref="BeginLoad(StageBackgroundWorkScope, SongSelectionTab)"/>: the DB
        /// query starts inside the scope worker (it may execute synchronously and must
        /// never stall the update thread); the worker only queues a completion, and the
        /// scope drain applies success/error on the update thread — unless the load's
        /// generation has been superseded, in which case the per-tab stale callback runs
        /// instead (Bookmarks retry; Recent drops silently).
        /// The query submission goes through the I/O-execution seam
        /// (<see cref="SubmitTabLoadQuery"/>): production resolves it to the scope's
        /// actual Run; a test wrapper records the submission synchronously and delegates.
        /// Returns the scope worker task: it completes only AFTER the success/error
        /// terminal has been enqueued into the scope — so awaiting it is a deterministic
        /// enqueue barrier for this single step and a drain that follows it provably
        /// processes that terminal. On a RETIRED scope nothing is enqueued and the task
        /// completes immediately (RunAsync short-circuits; the loader never runs).
        /// </summary>
        private Task RunTabLoad(
            StageBackgroundWorkScope scope,
            Func<Task<List<SongListNode>>> loadAsync,
            int capturedLoadVersion,
            Func<int> currentLoadVersion,
            Action<IReadOnlyList<SongListNode>> applySuccess,
            Action applyError,
            Action? onStaleCompletion)
        {
            Func<CancellationToken, IReadOnlyList<SongListNode>> work = token =>
            {
                token.ThrowIfCancellationRequested();
                // Query here, on the worker: reaching the DB (which may run
                // synchronously) must never happen on the update thread.
                return CopyTabLoadNodes(loadAsync().GetAwaiter().GetResult());
            };

            return _submitTabLoadQuery(
                scope,
                work,
                onSuccess: nodes =>
                {
                    if (capturedLoadVersion != currentLoadVersion())
                    {
                        onStaleCompletion?.Invoke();
                        return;
                    }

                    applySuccess(nodes);
                },
                onError: _ =>
                {
                    if (capturedLoadVersion != currentLoadVersion())
                    {
                        // A stale fault must not flag the load failed; see onSuccess.
                        onStaleCompletion?.Invoke();
                        return;
                    }

                    applyError();
                });
        }

        /// <summary>
        /// Retries a refresh intent whose query was invalidated without applying the stale
        /// rows/error. Runs on the update thread (scope-drained completion) and re-enters
        /// the same wait path with the SAME request id and the LAUNCH-CAPTURED scope, so
        /// the toggle's own write settles before the re-query and a retired scope drops
        /// the owed re-query with the request.
        /// </summary>
        private void RetryBookmarksRefreshIfCurrent(
            StageBackgroundWorkScope scope,
            int capturedRequestId)
        {
            if (capturedRequestId != Volatile.Read(ref _deferredBookmarksRequestId))
                return;

            // A sibling launch owning the current generation must not be invalidated by a
            // retry (it would cascade): retry only when the invalidation came from a
            // toggle — the current generation was bumped without an actual launch.
            if (Volatile.Read(ref _bookmarksLaunchedLoadVersion) == Volatile.Read(ref _bookmarksLoadVersion))
                return;

            AwaitPendingWritesThenLoadBookmarks(scope, capturedRequestId);
        }

        private void ApplyCurrentBookmarkStates(List<SongListNode> nodes)
        {
            var states = new Dictionary<int, bool>();
            CollectBookmarkStates(
                _intake.AppliedSnapshot?.RootSongs ?? _getLibrary().GetLibrarySnapshot().RootSongs,
                states);
            CollectBookmarkStates(_bookmarkNodes, states);
            CollectBookmarkStates(_recentPlayNodes, states);

            foreach (var node in nodes)
            {
                int? effectiveId = node.DatabaseSongId ?? node.DatabaseSong?.Id;
                if (node.DatabaseSong != null
                    && effectiveId is int songId
                    && states.TryGetValue(songId, out var isBookmarked))
                {
                    node.DatabaseSong.IsBookmarked = isBookmarked;
                }
            }
        }

        private static void CollectBookmarkStates(
            IEnumerable<SongListNode>? nodes,
            Dictionary<int, bool> states)
        {
            if (nodes == null)
                return;

            foreach (var node in nodes)
            {
                int? effectiveId = node.DatabaseSongId ?? node.DatabaseSong?.Id;
                if (node.DatabaseSong != null && effectiveId is int songId && songId > 0)
                    states[songId] = node.DatabaseSong.IsBookmarked;

                if (node.Children.Count > 0)
                    CollectBookmarkStates(node.Children, states);
            }
        }

        private static IReadOnlyList<SongListNode> CopyTabLoadNodes(
            IEnumerable<SongListNode>? nodes)
        {
            return Array.AsReadOnly(nodes?.ToArray() ?? Array.Empty<SongListNode>());
        }

        /// <summary>
        /// Toggles the bookmark flag on the given song node (the stage resolves it from
        /// its visible selection). No-op for null/without-entity nodes. Returns true when
        /// the full persistence path ran (the stage plays its cursor sound); returns false
        /// for the zero-id shortcut so unrelated nodes stay untouched.
        ///
        /// Updates the in-memory flag immediately so the star marker refreshes this frame,
        /// persists asynchronously (serialized per song, last intent wins), and—on the
        /// Bookmarks tab when un-bookmarking—removes the row from the cached list and
        /// requests a repopulate.
        /// </summary>
        public bool ToggleBookmark(SongListNode node)
        {
            var song = node.DatabaseSong;
            if (song == null)
                return false;

            bool newState = !song.IsBookmarked;
            // Prefer the persisted id over the in-memory entity id. When re-enumerating
            // SET.def songs that already exist in the DB, AddSongAsync returns the existing
            // id without assigning it to the parsed SongEntity.Id (left at 0), while
            // DatabaseSongId holds the real persisted id. Using song.Id would pass 0 to
            // SetBookmarkAsync (a no-op) and reconcile against every zero-id node.
            int songId = node.DatabaseSongId ?? song.Id;
            // When neither the persisted id nor the entity id is set (e.g. an unpersisted
            // fallback node whose DatabaseSong.Id is still 0), keying the persistence write
            // and the cross-list reconciliation on the sentinel 0 would (a) be a silent
            // no-op in the DB and (b) flip IsBookmarked on EVERY unrelated zero-id node via
            // BookmarkStateReconciler.Apply. The optimistic in-memory flip on the selected
            // node above already refreshed its star marker, so bail out before touching the
            // shared persistence/reconciliation state to leave unrelated nodes untouched.
            if (songId <= 0)
            {
                song.IsBookmarked = newState; // immediate, in-memory; star refreshes next draw.
                return false;
            }
            var library = _getLibrary();
            var setBookmarkAsync = _setBookmarkAsync;
            // Bump the per-song toggle generation so a fault from THIS toggle can be told apart
            // from a fault of an older, already-superseded toggle at rollback time. The bump
            // shares _bookmarkRevertGate with the worker-side fault continuation so a stale
            // check-then-revert can never interleave with this bump. The optimistic
            // shared-library flip must join the same critical section, before the write is
            // even scheduled: a persist that faults immediately could otherwise run its
            // worker-side revert first and then be overwritten by the rejected value here.
            Interlocked.Increment(ref _bookmarksLoadVersion);
            int toggleVersion;
            lock (_bookmarkRevertGate)
            {
                _bookmarkPersistedStates.TryAdd(songId, !newState);
                toggleVersion = _bookmarkToggleVersion.AddOrUpdate(songId, 1, (_, v) => v + 1);
                // Immediate, in-memory flip (the star refreshes next draw). It joins the
                // gate with the version bump so a worker-side shared revert can never
                // interleave between the two writes.
                song.IsBookmarked = newState;
                BookmarkStateReconciler.Apply(
                    _intake.AppliedSnapshot?.RootSongs
                        ?? library.GetLibrarySnapshot().RootSongs,
                    songId,
                    newState);
            }
            // Reconcile the flag across every in-memory representation of this song (Recent
            // list, Bookmarks list) so the star marker stays consistent across tabs without
            // a reload — each surface holds a distinct node/entity instance. These lists are
            // update-thread owned, so they stay outside the gate.
            BookmarkStateReconciler.Apply(_recentPlayNodes, songId, newState);
            BookmarkStateReconciler.Apply(_bookmarkNodes, songId, newState);

            // Persist asynchronously, serialized per song so rapid toggles apply in order and
            // the last user intent (the captured newState above) is what lands in the database.
            // Each write chains after the previous in-flight write for the same song; without
            // this, two overlapping fire-and-forget calls could complete out of order and leave
            // the DB divergent from the in-memory flag.
            // Capture THIS activation's scope now, when the toggle happens: the fault
            // continuation below must post its rollback here, into the scope that owned the
            // toggle. Reading the scope at fault time could land the rollback in a LATER
            // activation's scope, where the generation check still matches (no intervening
            // toggle) and the stale revert would mutate the new activation's lists and start
            // another bookmark load. A retired scope drops the rollback.
            var originScope = _getScope();
            var prior = _pendingBookmarkWrites.GetOrAdd(songId, _ => Task.CompletedTask);
            // CAPTURED-LIBRARY WRITE (HPA-802 review): the write executes against `library`
            // — the instance captured from the accessor at TOGGLE time — never re-resolving
            // the accessor, which may have been cleared (shutdown) or re-pointed at a newer
            // activation's library by the time this serialized write actually runs.
            var write = prior.ContinueWith(
                _ => PersistBookmarkWriteAsync(setBookmarkAsync, library, songId, newState),
                TaskScheduler.Default).Unwrap();
            _pendingBookmarkWrites[songId] = write;

            // If the persist fails, the optimistic in-memory flip would otherwise diverge from
            // the database (the star would vanish on next load with no feedback). Post a
            // rollback to the pre-toggle state into the activation scope; the scope drain
            // applies it on the update thread so it can't race the draw path. The captured
            // toggleVersion lets the rollback be skipped if a newer toggle has since superseded
            // this one. The write itself is never canceled: a committed persistence task may
            // outlive navigation, and only the stale UI rollback is dropped on retirement.
            write.ContinueWith(task =>
            {
                if (task.IsFaulted || task.IsCanceled)
                {
                    // A failed write leaves no committed intent. While the origin scope
                    // is live the whole revert — shared library entities, Recent and
                    // Bookmarks lists — is applied on the update thread, so no worker
                    // ever writes IsBookmarked while the player may be toggling.
                    // If navigation retired the origin scope first, the posted rollback
                    // is dropped and the drop callback still reverts the SHARED library
                    // entities (the snapshot backs the reentry browse tree), so reentry
                    // starts DB-consistent; that path takes the same gate as the
                    // update-thread toggle bump, so it cannot interleave with a toggle.
                    // Hand the revert off first, then log: the warning says
                    // "reverting", so it must describe a hand-off that already happened
                    // (do-then-log); this order also makes the warning the post-handoff
                    // signal the race tests await before they drain or assert.
                    // ASYNCHRONOUS ROLLBACK (HPA-802 fix round 2): the continuation is
                    // SCHEDULED, never inlined on the thread that completes the write
                    // (a DB worker in production), so the update thread is never blocked
                    // by this logging. Completing the write task is NOT a barrier: an
                    // observer cannot assume this handoff ran just because the write
                    // settled — .NET publishes the antecedent's terminal state BEFORE
                    // running continuations, and ExecuteSynchronously only ever requests
                    // inlining. Wait for the warning below (or observe the revert
                    // itself); never rely on awaiting the antecedent alone.
                    if (originScope != null)
                    {
                        var revertTo = GetBookmarkPersistedState(songId, !newState);
                        originScope.Post(
                            () => ApplyBookmarkRevertCore(library, songId, revertTo, toggleVersion),
                            onDropped: () => RevertSharedBookmark(library, songId, revertTo, toggleVersion));
                    }
                    else
                    {
                        RevertSharedBookmark(
                            library,
                            songId,
                            GetBookmarkPersistedState(songId, !newState),
                            toggleVersion);
                    }

                    _logger.LogWarning(
                        task.Exception?.GetBaseException(),
                        "Bookmark save failed for song {SongId} (target state {Bookmarked}); reverting",
                        songId,
                        newState);
                }
            }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);

            // On the Bookmarks tab, an un-bookmark should drop the row from view.
            if (_activeTab == SongSelectionTab.Bookmarks && !newState)
            {
                // Bookmark caches are update-thread owned. A pending background query only
                // becomes visible when its queued completion is applied on a later update.
                _bookmarkNodes?.RemoveAll(n =>
                    ReferenceEquals(n, node) || (n.DatabaseSongId ?? n.DatabaseSong?.Id) == songId);
                _tabListNeedsRefresh = true;
            }

            return true;
        }

        private async Task PersistBookmarkWriteAsync(
            Func<SongLibrary, int, bool, Task> setBookmarkAsync,
            SongLibrary library,
            int songId,
            bool newState)
        {
            await setBookmarkAsync(library, songId, newState).ConfigureAwait(false);
            _bookmarkPersistedStates[songId] = newState;
        }

        private bool GetBookmarkPersistedState(int songId, bool fallback) =>
            _bookmarkPersistedStates.TryGetValue(songId, out var persistedState)
                ? persistedState
                : fallback;

        /// <summary>
        /// Reverts the SHARED library entities (the manager's current snapshot, which
        /// survives navigation and backs the reentry browse tree) for a failed persist,
        /// unless a newer toggle has superseded it. The version check and the reconcile
        /// hold the same gate as the update-thread toggle bump, so a newer toggle can
        /// never slip between the check and the revert. Reconciling by song id also
        /// covers duplicate SET.def nodes.
        /// </summary>
        private void RevertSharedBookmark(
            SongLibrary library,
            int songId,
            bool revertTo,
            int toggleVersion)
        {
            lock (_bookmarkRevertGate)
            {
                if (!_bookmarkToggleVersion.TryGetValue(songId, out var current)
                    || current == toggleVersion)
                {
                    BookmarkStateReconciler.Apply(
                        library.GetLibrarySnapshot().RootSongs,
                        songId,
                        revertTo);
                }
            }
        }

        /// <summary>
        /// Applies one posted bookmark rollback on the update thread. If the rollback's toggle
        /// generation is still current, the pre-toggle bookmark state is restored across every
        /// in-memory representation (browse tree, Recent list, Bookmarks list) so the star
        /// marker matches the database after a failed persist. A superseded revert — one
        /// superseded by a newer toggle — is skipped so a stale fault cannot override a
        /// later, possibly successful toggle.
        /// </summary>
        private void ApplyBookmarkRevertCore(
            SongLibrary library,
            int songId,
            bool revertTo,
            int toggleVersion)
        {
            if (_bookmarkToggleVersion.TryGetValue(songId, out var current)
                && current != toggleVersion)
            {
                // A newer toggle has since changed the flag; this fault is stale.
                return;
            }

            BookmarkStateReconciler.Apply(
                _intake.AppliedSnapshot?.RootSongs
                    ?? library.GetLibrarySnapshot().RootSongs,
                songId,
                revertTo);
            // The manager's current snapshot may be a newer publication than the one
            // this activation applied; revert it too (idempotent when they match).
            RevertSharedBookmark(library, songId, revertTo, toggleVersion);
            BookmarkStateReconciler.Apply(_recentPlayNodes, songId, revertTo);
            BookmarkStateReconciler.Apply(_bookmarkNodes, songId, revertTo);

            // On the Bookmarks tab, an un-bookmark that failed to persist wrongly removed
            // the row from view (the song is still bookmarked in the DB). Re-sync from the
            // authoritative store so the row reappears. The rollback reload enters the
            // same deferred wait path as every other Bookmark refresh request, so other
            // still-pending writes settle before the re-query runs.
            if (_activeTab == SongSelectionTab.Bookmarks)
                RequestBookmarksRefresh();
        }

        /// <summary>
        /// Builds the projected Recent-plays rows: the intake's eligibility filter over
        /// the cached rows, labeled with each row's exact play speed. Also settles the
        /// distinct empty flag (only a successful, empty load shows "No recent plays
        /// yet"; a failed load keeps its own error message in the draw path).
        /// </summary>
        public List<SongListNode> BuildRecentPlaysRows()
        {
            var nodes = _intake.Filter(_recentPlayNodes);
            LabelRecentPlayNodes(nodes);
            // Show empty message only when the load succeeded but returned nothing.
            // A failed load gets its own distinct message in the draw path.
            _showEmptyRecentMessage = !_recentPlaysLoadFailed && nodes.Count == 0;
            return nodes;
        }

        /// <summary>
        /// Builds the projected Bookmarks rows: the intake's eligibility filter over the
        /// cached rows. Also settles the distinct empty flag (only a successful, empty
        /// load shows "No bookmarks yet"; a failed load keeps its own error message in
        /// the draw path).
        /// </summary>
        public List<SongListNode> BuildBookmarksRows()
        {
            var nodes = _intake.Filter(_bookmarkNodes);
            // Show empty message only when the load succeeded but returned nothing.
            // A failed load gets its own distinct message in the draw path.
            _showEmptyBookmarksMessage = !_bookmarksLoadFailed && _bookmarkNodes != null && nodes.Count == 0;
            return nodes;
        }

        private static void LabelRecentPlayNodes(IEnumerable<SongListNode> nodes)
        {
            foreach (var node in nodes)
            {
                if (node?.RecentPlaySpeedPercent.HasValue == true)
                    node.Title = FormatRecentPlayDisplayTitle(node);
            }
        }

        /// <summary>
        /// Formats a detached Recent Plays node with the exact speed of the persisted
        /// play that caused it to appear in the cross-speed recent query.
        /// </summary>
        internal static string FormatRecentPlayDisplayTitle(SongListNode node)
        {
            if (node == null)
                return "";

            string title = node.DisplayTitle ?? "Unknown Song";
            if (!node.RecentPlaySpeedPercent.HasValue)
                return title;

            string suffix = $" [{PlaySpeedRange.Format(node.RecentPlaySpeedPercent.Value)}]";
            return title.EndsWith(suffix, StringComparison.Ordinal)
                ? title
                : title + suffix;
        }

        /// <summary>
        /// Requests a repopulation of the active tab's list on the next update.
        /// The generation increments BEFORE the flag write: a reconciliation that sees
        /// the new generation but races this request will restore the flag itself; one
        /// that completes first is followed by this write and is likewise re-queued.
        /// </summary>
        public void RequestAsyncTabListRefresh()
        {
            Interlocked.Increment(ref _asyncTabListRefreshVersion);
            _tabListNeedsRefresh = true;
        }

        /// <summary>
        /// Begins the update thread's consumption of pending refresh requests around a
        /// projection; pair with <see cref="CompleteTabListRefreshConsumption"/>.
        /// </summary>
        public int BeginTabListRefreshConsumption()
        {
            int version = Volatile.Read(ref _asyncTabListRefreshVersion);
            _tabListNeedsRefresh = false;
            return version;
        }

        /// <summary>
        /// Completes a refresh consumption: a refresh requested during the projection
        /// re-queues itself instead of being lost by the stale false write.
        /// </summary>
        public void CompleteTabListRefreshConsumption(int consumedVersion)
        {
            if (Volatile.Read(ref _asyncTabListRefreshVersion) != consumedVersion)
                _tabListNeedsRefresh = true;
        }

        /// <summary>
        /// The bounded pre-disposal drain (stage disposal): waits up to five seconds for
        /// pending bookmark writes so the game never disposes the library/database while
        /// a durable write is still running. Never cancels the writes; faults are
        /// contained and logged.
        /// </summary>
        public void WaitForPendingWritesAtDisposal()
        {
            var pending = _pendingBookmarkWrites.Values.ToArray();
            if (pending.Length == 0)
                return;

            try
            {
                if (!Task.WhenAll(pending).Wait(TimeSpan.FromSeconds(5)))
                {
                    _logger.LogWarning(
                        "Timed out waiting for {PendingCount} pending bookmark write(s) during stage disposal",
                        pending.Length);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Pending bookmark write failed during stage disposal");
            }
        }
    }
}
