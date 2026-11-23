#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Song.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SongEntity = DTXMania.Game.Lib.Song.Entities.Song;

namespace DTXMania.Game.Lib.Song
{
    /// <summary>
    /// Song database management and enumeration, owned by the game (HPA-801).
    /// <see cref="Game.BaseGame"/> constructs exactly one instance per process in
    /// <c>CreateLoadContentServices</c> and exposes it through
    /// <see cref="Stage.IStageGame.SongLibrary"/>; there is no singleton.
    /// Responsible for centralized song data management and enumeration.
    /// </summary>
    public sealed class SongLibrary : IDisposable
    {
        /// <summary>
        /// Creates the game-owned song library over one non-null database. The
        /// database may be uninitialized; Startup calls <see cref="InitializeAsync"/>
        /// before any database-backed operation runs, and <c>CreateContext</c>
        /// keeps rejecting use until then. Optional delegates are the final
        /// constructor seams (design Decision E) so tests inject collaborators
        /// once instead of via mutable per-test setters.
        /// </summary>
        internal SongLibrary(
            SongDatabase database,
            ILogger<SongLibrary>? logger = null,
            SongRootPolicy? rootPolicy = null,
            string? bundledSongsRoot = null,
            Func<string, Task<(SongEntity song, SongChart chart)>>? parseSongEntitiesAsync = null,
            Func<string, IEnumerable<string>>? enumerateFiles = null,
            Func<string, IEnumerable<string>>? enumerateDirectories = null,
            Func<string, Encoding, CancellationToken, Task<string[]>>? readAllLinesAsync = null,
            Func<
                SongCatalogStore,
                SongBulkImportRequest,
                IProgress<SongBulkImportProgress>?,
                CancellationToken,
                Task<SongBulkImportResult>>? importSongsAsync = null,
            Func<
                string[],
                IProgress<EnumerationProgress>?,
                CancellationToken,
                Task<SongEnumerationBatch>>? buildEnumerationBatchAsync = null,
            Action<SongEnumerationBatch, IReadOnlyDictionary<string, SongChart>>? finalizePendingNodes = null,
            Action<SongEnumerationBatch>? publishEnumeration = null)
        {
            _database = database ?? throw new ArgumentNullException(nameof(database));
            // Both stores share the same non-null database instance (HPA-801).
            _catalog = new SongCatalogStore(_database);
            _scoreStore = new SongScoreStore(_database);
            _logger = logger ?? NullLogger<SongLibrary>.Instance;
            RootPolicy = rootPolicy ?? SongRootPolicy.ForCurrentPlatform();
            BundledSongsRoot = bundledSongsRoot ?? BundledSongs.Root;
            // One definition reader for every SET.def/box.def consumer (HPA-801
            // Decision C): the same instance is injected into the cold scan below
            // and into the hierarchy builder, so one ReadAllLines seam and one
            // fallback rule govern both paths.
            _definitions = new SongDefinitionReader(_logger, readAllLinesAsync);
            // Warm-start hierarchy, pending-node finalization and the shared
            // BOX/root factory (HPA-801 Decision C/D).
            _hierarchy = new SongHierarchyBuilder(
                _catalog,
                _definitions,
                RootPolicy,
                BundledSongsRoot,
                _logger);
            // Stateless cold-path scanner: traversal, chart inventory and
            // freshness checks over the shared reader/builder/catalog and the
            // injected I/O delegates. It owns no locks, cancellation,
            // publication or score state (HPA-801 Decision B/D/E).
            _scanner = new SongLibraryScanner(
                _database,
                _catalog,
                _hierarchy,
                RootPolicy,
                _definitions,
                _logger,
                parseSongEntitiesAsync,
                enumerateFiles,
                enumerateDirectories);
            // Score orchestration (store save + this library's locked in-memory
            // refresh delegate) and live recent/bookmark queries with their SQL
            // inline (HPA-801 Decision D). Both share the one database; queries
            // map nodes through the same hierarchy builder as cold/warm paths.
            _scores = new SongScoreService(
                _database,
                _scoreStore,
                RefreshInMemoryScoreForChartAsync,
                _logger);
            _queries = new SongQueryService(
                _database,
                _hierarchy,
                GetCurrentSearchPathsSnapshot,
                () => ReadsAsNeverInitialized(_database));
            ImportSongsCoreAsync = importSongsAsync ?? DefaultImportSongsAsync;
            // Default batch-building delegate captures this instance so tests can
            // inject controlled batches (e.g. an incomplete batch) without subclassing.
            BuildEnumerationBatchCoreAsync = buildEnumerationBatchAsync
                ?? ((searchPaths, progress, token) =>
                    BuildEnumerationBatchAsync(searchPaths, progress, token));
            FinalizePendingNodesCore = finalizePendingNodes
                ?? ((batch, chartsByPath) => _hierarchy.FinalizePendingNodes(batch, chartsByPath));
            PublishEnumerationCore = publishEnumeration ?? PublishEnumeration;
        }

        #region Private Fields

        private readonly List<SongListNode> _rootSongs = new();
        private readonly object _lockObject = new();
        private CancellationTokenSource? _enumCancellation;
        private readonly SongDatabase _database;

        // The two persistence stores over the shared database. Owned here so
        // scan orchestration and score refresh reach their SQL through the
        // store that owns it (HPA-801).
        private readonly SongCatalogStore _catalog;

        private readonly SongScoreStore _scoreStore;

        // Score orchestration and live queries over the shared database
        // (HPA-801 Decision D); stages reach them via Scores/Queries.
        private readonly SongScoreService _scores;

        private readonly SongQueryService _queries;
        private string[] _currentSearchPaths = Array.Empty<string>();
        private long _publicationVersion;

        // Supplied by the constructor; game construction passes the app logger,
        // independent tests keep the null logger by default.
        private readonly ILogger<SongLibrary> _logger;

        // SET.def/box.def reading and parsing (HPA-801 Decision C): one shared
        // reader, no publication state, no DbContext.
        private readonly SongDefinitionReader _definitions;

        // Warm-start hierarchy, pending-node finalization and the shared BOX/root
        // factory used by the cold scan as well (HPA-801 Decision C/D).
        private readonly SongHierarchyBuilder _hierarchy;

        // Stateless traversal/inventory/freshness scanner (HPA-801 Decision
        // B/D); the library remains the sole scan-transaction owner.
        private readonly SongLibraryScanner _scanner;

        /// <summary>
        /// The hierarchy builder carrying the shared <see cref="SongDefinitionReader"/>
        /// instance and BOX/root factory; exposed for tests that drive warm-side
        /// steps directly (e.g. pending-node finalization).
        /// </summary>
        internal SongHierarchyBuilder Hierarchy => _hierarchy;

        /// <summary>
        /// The stateless filesystem scanner (traversal, chart inventory and
        /// cache-freshness checks), sharing this library's definition reader,
        /// hierarchy builder and catalog. Exposed for tests that exercise
        /// scanner behavior directly; publication and the scan slot stay with
        /// the library (HPA-801 Decision B).
        /// </summary>
        internal SongLibraryScanner Scanner => _scanner;

        internal SongRootPolicy RootPolicy { get; }

        /// <summary>
        /// The root whose folders merge into the library's top level rather than
        /// getting a root BOX. Callers decide whether to scan it at all.
        /// </summary>
        internal string BundledSongsRoot { get; }

        // Constructor seams (HPA-801 Decision E): immutable per instance so tests
        // inject collaborators exactly once, at construction. The parse and
        // enumerate delegates live on the scanner's constructor; these remain
        // because the scan transaction invokes them directly.
        internal Func<
            SongCatalogStore,
            SongBulkImportRequest,
            IProgress<SongBulkImportProgress>?,
            CancellationToken,
            Task<SongBulkImportResult>> ImportSongsCoreAsync
            { get; }

        internal Func<
            string[],
            IProgress<EnumerationProgress>?,
            CancellationToken,
            Task<SongEnumerationBatch>> BuildEnumerationBatchCoreAsync
            { get; }

        internal Action<
            SongEnumerationBatch,
            IReadOnlyDictionary<string, SongChart>> FinalizePendingNodesCore
            { get; }

        internal Action<SongEnumerationBatch> PublishEnumerationCore
            { get; }

        // Initialization state tracking
        private bool _isInitialized = false;

        // True once InitializeAsync has attempted to initialize (or purge) the
        // game-owned database. Mirrors the pre-rename semantics where the
        // database reference became non-null at the start of initialization:
        // before that, queries read as empty; afterwards, a not-initialized
        // database is an initialization/purge failure that CreateContext's
        // guard must surface instead of reading as an empty result.
        private bool _databaseInitializationAttempted = false;

        // Back off the persisted scan-start watermark to account for filesystems
        // that round modification times down to a coarse resolution.
        private static readonly TimeSpan EnumerationWatermarkBackoff =
            TimeSpan.FromSeconds(5);

        #endregion

        #region Public Properties

        /// <summary>
        /// Gets whether the library has been initialized with song data
        /// </summary>
        public bool IsInitialized
        {
            get
            {
                lock (_lockObject)
                {
                    return _isInitialized;
                }
            }
        }

        /// <summary>
        /// The one database this library owns. Initialized by Startup via
        /// <see cref="InitializeAsync"/>; <c>CreateContext</c> rejects use until then.
        /// </summary>
        internal SongDatabase Database => _database;

        /// <summary>
        /// Score save orchestration — store save (RunId idempotent) followed by
        /// this library's locked in-memory refresh — and NX score import over
        /// the shared database (HPA-801 Decision D).
        /// </summary>
        internal SongScoreService Scores => _scores;

        /// <summary>
        /// Live recent-play and bookmark queries with their SQL inline over the
        /// shared database (HPA-801 Decision D; no query store).
        /// </summary>
        internal SongQueryService Queries => _queries;

        /// <summary>
        /// True when the database has never been presented to initialization:
        /// queries then read as empty (the pre-HPA-801 null-service contract).
        /// Once initialization was attempted, a not-initialized database is an
        /// initialization/purge failure — the query falls through so
        /// CreateContext throws, exactly as the pre-rename non-null service did.
        /// </summary>
        private bool ReadsAsNeverInitialized(SongDatabase database)
        {
            if (database.IsInitialized)
            {
                return false;
            }

            lock (_lockObject)
            {
                return !_databaseInitializationAttempted;
            }
        }

        private static Task<SongBulkImportResult> DefaultImportSongsAsync(
            SongCatalogStore catalog,
            SongBulkImportRequest request,
            IProgress<SongBulkImportProgress>? progress,
            CancellationToken cancellationToken) =>
            catalog.ImportSongsAsync(request, progress, cancellationToken);

        /// <summary>
        /// Gets the root song list
        /// </summary>
        public IReadOnlyList<SongListNode> RootSongs
        {
            get => GetLibrarySnapshot().RootSongs;
        }

        /// <summary>
        /// Gets a stable, versioned view of the currently published library.
        /// Reading never changes the publication version.
        /// </summary>
        public SongLibrarySnapshot GetLibrarySnapshot()
        {
            lock (_lockObject)
                return CreateSnapshotLocked();
        }

        /// <summary>
        /// Number of discovered scores during enumeration
        /// </summary>
        public int DiscoveredScoreCount { get; private set; }

        /// <summary>
        /// Number of enumerated files
        /// </summary>
        public int EnumeratedFileCount { get; private set; }

        #endregion

        #region Events

        /// <summary>
        /// Fired after a complete library snapshot has been published. Subscribers run
        /// outside the manager lock and always receive one coherent hierarchy/root view.
        /// </summary>
        public event EventHandler<SongLibraryPublishedEventArgs>? SongLibraryPublished;

        #endregion

        #region Individual Phase Operations

        /// <summary>
        /// Initializes the game-owned database (the SongListDB startup phase).
        /// The database instance exists from library construction; this creates
        /// its directory and schema. The path belongs to the game (HPA-801), so
        /// there is no path parameter anymore.
        /// </summary>
        public async Task<bool> InitializeAsync(bool purgeDatabaseFirst = false)
        {
            // From here on the database has been presented to initialization:
            // the pre-rename contract is that queries no longer read as empty
            // but surface the failure through the database's own guard.
            lock (_lockObject)
            {
                _databaseInitializationAttempted = true;
            }

            try
            {
                if (purgeDatabaseFirst)
                {
                    await _database.PurgeDatabaseAsync().ConfigureAwait(false);
                }

                await _database.InitializeDatabaseAsync().ConfigureAwait(false);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error during database service initialization");
                return false;
            }
        }

        /// <summary>
        /// Marks the song manager as fully initialized
        /// </summary>
        public void SetInitialized()
        {
            lock (_lockObject)
            {
                _isInitialized = true;
            }
        }

        /// <summary>
        /// Refreshes the in-memory <see cref="RootSongs"/> list from the database using
        /// the current search paths. Call this after database mutations (e.g. NX score
        /// import, score updates) to keep the song list in sync without a full restart.
        /// </summary>
        public async Task RefreshSongListFromDatabaseAsync()
        {
            string[] searchPaths;
            long capturedVersion;
            lock (_lockObject)
            {
                // Roots and the publication version must come from one critical
                // section: a scan publishing between two separate reads would
                // leave the build below holding the newer version alongside
                // stale roots, so the commit-time version check would pass and
                // overwrite the scan's publication.
                searchPaths = _currentSearchPaths.ToArray();
                capturedVersion = _publicationVersion;
            }

            if (searchPaths.Length > 0)
            {
                await BuildHierarchyFromDatabaseOnceAsync(searchPaths, capturedVersion)
                    .ConfigureAwait(false);
            }
        }

        #endregion

        #region Song Enumeration

        /// <summary>
        /// Builds the cold-start enumeration batch through the stateless
        /// scanner (HPA-801 Decision B/D). Kept as the library's seam so tests
        /// and the scan transaction reach traversal in one place.
        /// </summary>
        internal Task<SongEnumerationBatch> BuildEnumerationBatchAsync(
            string[] searchPaths,
            IProgress<EnumerationProgress>? progress,
            CancellationToken cancellationToken) =>
            _scanner.BuildEnumerationBatchAsync(searchPaths, progress, cancellationToken);

        public Task<SongEnumerationResult> EnumerateAndImportSongsAsync(
            string[] searchPaths,
            IProgress<EnumerationProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            // Acquire the enumeration slot atomically. Direct callers of this
            // public/internal entry point keep the historical throwing contract:
            // a concurrent enumeration surfaces as InvalidOperationException.
            if (!TryBeginEnumeration(cancellationToken, out var linked))
            {
                throw new SongEnumerationBusyException();
            }
            return EnumerateAndImportSongsCoreAsync(
                searchPaths,
                progress,
                cancellationToken,
                linked);
        }

        private async Task<SongEnumerationResult> EnumerateAndImportSongsCoreAsync(
            string[] searchPaths,
            IProgress<EnumerationProgress>? progress,
            CancellationToken cancellationToken,
            CancellationTokenSource linked)
        {
            SongEnumerationResult? result = null;

            // Capture a conservatively backed-off enumeration start as the
            // freshness watermark. It is persisted only after successful
            // publication so that any file edit during the (possibly long) scan
            // remains newer than the watermark and triggers the next refresh.
            // Using the completion time instead would mask mid-scan edits: a
            // file parsed early and then modified while other roots are still
            // scanning would have an mtime older than the completion-time
            // watermark and be silently ignored.
            var enumerationStartedUtc = DateTime.UtcNow - EnumerationWatermarkBackoff;

            try
            {
                // The cancellation check and database snapshot must run inside
                // the try/finally so that a pre-try throw (already-cancelled
                // token, or uninitialized database on the direct entry point)
                // still reaches EndEnumeration in the finally. Without this,
                // _enumCancellation stays non-null permanently and every
                // subsequent enumeration is rejected as "already in progress".
                cancellationToken.ThrowIfCancellationRequested();
                // Readiness guard: the game-owned database exists from launch but
                // is unusable until Startup's InitializeAsync runs. The guard is
                // an in-memory initialization-state check (the pre-rename
                // null-reference throw) — no SQL, no I/O — so it fails before
                // root validation and traversal (releasing the slot via the
                // finally) without querying the database.
                if (!_database.IsInitialized)
                {
                    throw new InvalidOperationException(
                        "Song database is not initialized.");
                }
                var batch = await BuildEnumerationBatchCoreAsync(
                    searchPaths,
                    progress,
                    linked.Token).ConfigureAwait(false);
                if (!batch.IsComplete)
                {
                    throw new InvalidOperationException(
                        "An incomplete enumeration cannot be imported.");
                }

                if (batch.ActiveRoots.Count == 0)
                {
                    result = new SongEnumerationResult(
                        SongEnumerationOutcome.NoActiveRoots,
                        batch,
                        SongBulkImportResult.Empty);
                    return result;
                }

                var import = await ImportSongsCoreAsync(
                    _catalog,
                    new SongBulkImportRequest(
                        batch.ActiveRoots,
                        batch.DiscoveredChartPaths,
                        batch.Candidates),
                    CreatePersistenceProgressAdapter(progress),
                    linked.Token).ConfigureAwait(false);

                try
                {
                    FinalizePendingNodesCore(batch, import.ChartsByPath);
                }
                catch (Exception exception)
                {
                    throw new SongEnumerationPostCommitException(
                        SongEnumerationPostCommitPhase.Finalization,
                        batch,
                        import,
                        exception);
                }

                try
                {
                    PublishEnumerationCore(batch);
                }
                catch (Exception exception)
                {
                    throw new SongEnumerationPostCommitException(
                        SongEnumerationPostCommitPhase.Publication,
                        batch,
                        import,
                        exception);
                }

                result = new SongEnumerationResult(
                    SongEnumerationOutcome.ImportedAndPublished,
                    batch,
                    import);

                // Rejected tabs are written for every scanned root, before the
                // watermark, so the next freshness check counts them.
                await SetRejectedChartCountsAsync(batch).ConfigureAwait(false);

                // Persist the backed-off START watermark (not the completion time)
                // so that mid-scan edits remain newer than the watermark and are
                // detected by the next freshness check. NoActiveRoots and failure
                // paths do not update the watermark: nothing was enumerated.
                //
                // Roots with recoverable (non-root) errors are excluded: a
                // recoverable error (file parse failure, set.def read failure)
                // leaves the chart's old database row preserved but unupdated.
                // Advancing that root's watermark would make the next freshness
                // check see matching counts and a watermark newer than the file's
                // mtime, silently accepting stale content. Leaving the watermark
                // dirty forces a rescan that retries the failed parse.
                var rootsToAdvance = GetRootsWithoutRecoverableErrors(batch);
                if (rootsToAdvance.Count > 0)
                {
                    await SetEnumerationWatermarkAsync(
                        rootsToAdvance,
                        enumerationStartedUtc)
                        .ConfigureAwait(false);
                }

                return result;
            }
            finally
            {
                EndEnumeration(linked);
            }
        }

        /// <summary>
        /// Atomically checks whether enumeration is idle and, if so, assigns a new
        /// linked cancellation source for the upcoming enumeration. Returns false
        /// (without mutating state) when an enumeration is already in progress,
        /// eliminating the check-then-act race that a separate idle-check and
        /// acquire step would introduce under concurrent callers.
        /// </summary>
        private bool TryBeginEnumeration(
            CancellationToken token,
            out CancellationTokenSource linked)
        {
            lock (_lockObject)
            {
                // Any non-null source means the slot is occupied, including one
                // that has been cancelled but whose enumeration task has not yet
                // reached EndEnumeration. Clearing the slot on cancellation
                // alone would let a new enumeration start while the previous
                // task is still mid-traversal/transaction/rollback/publication,
                // racing on the shared _rootSongs list and the database. Only
                // EndEnumeration — run in the prior task's finally block — clears
                // the slot, proving the prior operation has terminated.
                if (_enumCancellation != null)
                {
                    linked = null!;
                    return false;
                }

                _enumCancellation =
                    CancellationTokenSource.CreateLinkedTokenSource(token);
                linked = _enumCancellation;
                return true;
            }
        }

        private void EndEnumeration(CancellationTokenSource source)
        {
            lock (_lockObject)
            {
                if (ReferenceEquals(_enumCancellation, source))
                    _enumCancellation = null;
            }

            source.Dispose();
        }

        private static IProgress<SongBulkImportProgress>?
            CreatePersistenceProgressAdapter(
                IProgress<EnumerationProgress>? progress)
        {
            if (progress == null)
                return null;

            return new DelegateProgress<SongBulkImportProgress>(update =>
                progress.Report(new EnumerationProgress
                {
                    CurrentOperation = update.Milestone switch
                    {
                        SongBulkImportMilestone.PreloadStarted =>
                            "Loading existing songs",
                        SongBulkImportMilestone.MatchingCompleted =>
                            "Matching charts",
                        SongBulkImportMilestone.MutationsStaged =>
                            "Preparing changes",
                        SongBulkImportMilestone.CleanupCompleted =>
                            "Removing stale records",
                        SongBulkImportMilestone.SaveStarted =>
                            "Saving songs",
                        SongBulkImportMilestone.Committed =>
                            "Song database committed",
                        _ => "Updating song database"
                    },
                    ProcessedCount = update.Processed,
                    DiscoveredSongs = update.Total
                }));
        }

        internal void PublishEnumeration(SongEnumerationBatch batch)
        {
            ArgumentNullException.ThrowIfNull(batch);
            SongLibrarySnapshot snapshot;
            lock (_lockObject)
            {
                _rootSongs.Clear();
                _rootSongs.AddRange(batch.RootNodes);
                var activeRoots = RootPolicy.Validate(batch.ActiveRoots)
                    .CanonicalRoots
                    .ToArray();
                _currentSearchPaths = activeRoots;
                EnumeratedFileCount = batch.DiscoveredChartPaths.Count;
                // Count discovered difficulty charts, not grouped logical songs:
                // a multi-difficulty set contributes one PendingSong but one
                // candidate per chart, and the contract is "number of discovered
                // scores" (mirroring the legacy per-chart increment).
                DiscoveredScoreCount = batch.Candidates.Count;
                _publicationVersion++;
                snapshot = CreateSnapshotLocked();
            }

            SafeInvoke(
                SongLibraryPublished,
                this,
                new SongLibraryPublishedEventArgs(snapshot),
                nameof(SongLibraryPublished));
        }

        /// <summary>
        /// Publishes an explicit empty library. Startup uses this to distinguish an
        /// intentionally empty accepted-root set from a failed or unfinished enumeration.
        /// </summary>
        public void PublishEmptyLibrary()
        {
            SongLibrarySnapshot snapshot;
            lock (_lockObject)
            {
                _rootSongs.Clear();
                _currentSearchPaths = Array.Empty<string>();
                EnumeratedFileCount = 0;
                DiscoveredScoreCount = 0;
                _publicationVersion++;
                snapshot = CreateSnapshotLocked();
            }

            SafeInvoke(
                SongLibraryPublished,
                this,
                new SongLibraryPublishedEventArgs(snapshot),
                nameof(SongLibraryPublished));
        }

        private SongLibrarySnapshot CreateSnapshotLocked() =>
            new(
                _publicationVersion,
                _rootSongs,
                _currentSearchPaths,
                EnumeratedFileCount,
                DiscoveredScoreCount);

        // Subscribers run independently so that one throwing handler cannot
        // suppress later handlers, and —
        // because the database has already committed and the hierarchy is
        // already published by the time these fire — cannot escape to turn a
        // successful import into a reported failure. Exceptions are logged.
        private void SafeInvoke<TArgs>(
            EventHandler<TArgs>? evt,
            object sender,
            TArgs args,
            string eventName) where TArgs : EventArgs
        {
            if (evt == null) return;
            foreach (var handler in evt.GetInvocationList())
            {
                try { ((EventHandler<TArgs>)handler)(sender, args); }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "{EventName} subscriber threw", eventName);
                }
            }
        }

        private sealed class DelegateProgress<T>(Action<T> report) :
            IProgress<T>
        {
            public void Report(T value) => report(value);
        }

        /// <summary>
        /// Builds the song list from existing database entries
        /// Used when the database is already populated but _rootSongs is empty
        /// Preserves the original folder hierarchy structure
        /// </summary>
        internal async Task BuildHierarchyFromDatabaseOnceAsync(
            string[] searchPaths,
            long? expectedPublicationVersion = null)
        {
            // Capture the published version before the async prepare: a scan
            // that commits while the hierarchy is being built must not be
            // overwritten by this (necessarily older) snapshot. Callers that
            // already hold an atomically captured version (the refresh path)
            // pass it in so the version always pairs with the roots snapshot it
            // was taken beside.
            long capturedVersion;
            lock (_lockObject)
            {
                capturedVersion = expectedPublicationVersion ?? _publicationVersion;
            }

            var hierarchy =
                await PrepareHierarchyFromDatabaseOnceAsync(searchPaths)
                    .ConfigureAwait(false);
            if (hierarchy == null)
            {
                return;
            }

            try
            {
                PublishDatabaseHierarchy(
                    hierarchy.RootNodes,
                    hierarchy.ActiveRoots,
                    hierarchy.ActiveChartCount,
                    capturedVersion);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error building song list from database");
            }
        }

        /// <summary>
        /// Builds the database hierarchy without publishing it. The hierarchy
        /// assembly itself lives in <see cref="SongHierarchyBuilder"/>; this thin
        /// library seam is what activation-scoped stages call before committing
        /// via <see cref="PublishDatabaseHierarchy(SongDatabaseHierarchy)"/>.
        /// </summary>
        internal Task<SongDatabaseHierarchy?> PrepareHierarchyFromDatabaseOnceAsync(
            string[] searchPaths) =>
            _hierarchy.PrepareHierarchyFromDatabaseOnceAsync(searchPaths);

        /// <summary>
        /// Commits a hierarchy prepared by
        /// <see cref="PrepareHierarchyFromDatabaseOnceAsync"/> to the shared
        /// library. Callers that defer publication (e.g. a stage posting to its
        /// activation scope) invoke this from the thread that owns the commit.
        /// </summary>
        internal void PublishDatabaseHierarchy(SongDatabaseHierarchy hierarchy)
        {
            ArgumentNullException.ThrowIfNull(hierarchy);
            PublishDatabaseHierarchy(
                hierarchy.RootNodes,
                hierarchy.ActiveRoots,
                hierarchy.ActiveChartCount);
        }

        private void PublishDatabaseHierarchy(
            IReadOnlyList<SongListNode> rootNodes,
            IReadOnlyList<string> activeRoots,
            int activeChartCount,
            long? expectedPublicationVersion = null)
        {
            SongLibrarySnapshot snapshot;
            lock (_lockObject)
            {
                // A scan or empty-library publication committed while the
                // hierarchy was being prepared; publishing now would replace
                // those newer roots with a snapshot built from stale state.
                if (expectedPublicationVersion.HasValue &&
                    _publicationVersion != expectedPublicationVersion.Value)
                {
                    _logger.LogInformation(
                        "Skipping database hierarchy publication: published version changed during prepare");
                    return;
                }

                _rootSongs.Clear();
                _rootSongs.AddRange(rootNodes);
                _currentSearchPaths = activeRoots.ToArray();
                EnumeratedFileCount = activeChartCount;
                DiscoveredScoreCount = activeChartCount;
                _publicationVersion++;
                snapshot = CreateSnapshotLocked();
            }

            SafeInvoke(
                SongLibraryPublished,
                this,
                new SongLibraryPublishedEventArgs(snapshot),
                nameof(SongLibraryPublished));
        }

        #endregion

        #region EF Core Helper Methods

        /// <summary>
        /// Refreshes the in-memory score cache for a single chart+instrument+speed after a
        /// database write. Re-queries the fresh <see cref="SongScore"/> (with
        /// <see cref="SongScore.PerformanceHistory"/> eagerly loaded), clones the current
        /// variant map, replaces only the matching speed, and atomically publishes the
        /// complete new snapshot. The fixed score slot is replaced only for 1.00x.
        /// Silently no-ops when the DB service is unavailable or the fresh score cannot
        /// be loaded.
        /// </summary>
        /// <returns>
        /// <see cref="ScoreRefreshOutcome.Published"/> if at least one song-list node
        /// published the fresh score variant; <see cref="ScoreRefreshOutcome.AlreadyCurrent"/>
        /// if every matching node already held a snapshot at least as new as the
        /// fresh one (a concurrent refresh won the publication race); <see
        /// cref="ScoreRefreshOutcome.NoMatch"/> if the DB service is unavailable, the
        /// fresh score is missing, or no matching node was found in the tree.
        /// </returns>
        private async Task<ScoreRefreshOutcome> RefreshInMemoryScoreForChartAsync(
            int chartId,
            EInstrumentPart instrument,
            int playSpeedPercent)
        {
            var fresh = await _scoreStore.GetScoreWithHistoryAsync(
                chartId,
                instrument,
                playSpeedPercent).ConfigureAwait(false);

            if (fresh == null) return ScoreRefreshOutcome.NoMatch;

            bool published = false;
            bool alreadyCurrent = false;
            lock (_lockObject)
            {
                foreach (var root in _rootSongs)
                {
                    switch (PublishNodeScoreVariant(
                        root, chartId, instrument, playSpeedPercent, fresh))
                    {
                        case ScoreRefreshOutcome.Published:
                            published = true;
                            break;
                        case ScoreRefreshOutcome.AlreadyCurrent:
                            alreadyCurrent = true;
                            break;
                    }
                }
            }
            if (published) return ScoreRefreshOutcome.Published;
            if (alreadyCurrent) return ScoreRefreshOutcome.AlreadyCurrent;
            return ScoreRefreshOutcome.NoMatch;
        }

        /// <summary>
        /// Recursively walks the complete song-list tree and publishes the fresh score
        /// into every canonical node containing the matching chart and instrument.
        /// When the in-memory score carries a zero ChartId (legacy set.def nodes),
        /// falls back to matching by Instrument + DifficultyLevel, mirroring the logic
        /// in <see cref="SongListNode.PopulatePlayHistoryFromCharts"/>. Unlike that
        /// node-scoped method, this walk spans the entire tree, so the legacy fallback
        /// is additionally scoped to the owning song (via <see cref="SongListNode.DatabaseSongId"/>
        /// vs. <paramref name="fresh"/>'s chart song) to prevent a difficulty collision
        /// with a different legacy node from hijacking the refresh and stamping the
        /// wrong song's cached score/history with this chart id.
        /// </summary>
        /// <returns>
        /// <see cref="ScoreRefreshOutcome.Published"/> if this subtree published the
        /// fresh snapshot; <see cref="ScoreRefreshOutcome.AlreadyCurrent"/> if the
        /// matching slot already held a snapshot at least as new (a concurrent refresh
        /// won the publication race, so the older snapshot is intentionally dropped);
        /// <see cref="ScoreRefreshOutcome.NoMatch"/> if no matching slot was found.
        /// </returns>
        internal static ScoreRefreshOutcome PublishNodeScoreVariant(
            SongListNode node,
            int chartId,
            EInstrumentPart instrument,
            int playSpeedPercent,
            SongScore fresh)
        {
            bool updated = false;
            bool sawAlreadyCurrent = false;

            if (node.Type == NodeType.Score)
            {
                // Owning song of the just-played chart, resolved from the eagerly-loaded
                // Chart navigation. Used to scope the legacy (ChartId == 0) fallback so
                // the tree-wide walk cannot match a different song that happens to share
                // the same instrument + numeric difficulty level.
                int? owningSongId = fresh.Chart?.SongId;

                for (int difficultyIndex = 0;
                    difficultyIndex < node.Scores.Length;
                    difficultyIndex++)
                {
                    var score = node.Scores[difficultyIndex];
                    if (score == null) continue;
                    if (score.Instrument != instrument) continue;

                    // Primary match: ChartId (set for individual-file nodes built via
                    // CreateSongNodeFromDatabaseEntities). Fallback: Instrument +
                    // DifficultyLevel, scoped to the owning song (for legacy set.def
                    // nodes where ChartId is 0). If the song identity is unavailable on
                    // either side, skip rather than risk updating the wrong node.
                    bool match;
                    if (score.ChartId != 0)
                    {
                        match = score.ChartId == chartId;
                    }
                    else
                    {
                        match = owningSongId.HasValue
                            && node.DatabaseSongId.HasValue
                            && node.DatabaseSongId == owningSongId
                            && score.DifficultyLevel == fresh.DifficultyLevel;
                    }

                    if (match)
                    {
                        var variantKey = new ScoreVariantKey(
                            difficultyIndex,
                            playSpeedPercent);

                        // Stale-publication guard. The detached DB read in
                        // RefreshInMemoryScoreForChartAsync runs outside _lockObject,
                        // so a caller that read an older snapshot can arrive at the
                        // publication lock after a concurrent caller has already
                        // published a newer snapshot for the same score key. Without
                        // this guard the older snapshot would overwrite the newer
                        // one. PlayCount is monotonic per (chart, instrument, speed),
                        // so a smaller PlayCount means a stale snapshot. When
                        // PlayCount is equal, LastPlayedAt breaks the tie (an equal
                        // or older timestamp means the snapshot carries no new
                        // information). Skipping a stale/equal publication is safe:
                        // the in-memory state is already consistent with the
                        // database, so the caller still reports success.
                        if (node.ScoreVariants.TryGetValue(variantKey, out var existing) &&
                            IsStaleOrEqualSnapshot(fresh, existing))
                        {
                            sawAlreadyCurrent = true;
                            continue;
                        }

                        var published = SongHierarchyBuilder.CreateScoreSnapshot(fresh);
                        published.ChartId = chartId;
                        published.PlaySpeedPercent = playSpeedPercent;

                        // Mirror the fresh snapshot into the chart entity's Scores
                        // collection so consumers that read directly from
                        // DatabaseSong.Charts[].Scores (e.g. SongStatusPanel's
                        // difficulty-grid ResolveChartScore) see the just-saved
                        // speed variant without waiting for a full song-database
                        // reload. PublishScoreVariants above only updates the
                        // node's ScoreVariants/Scores[]; the chart entity is a
                        // separate object graph that also needs the fresh entry.
                        UpdateChartEntityScore(node, chartId, published);

                        var variants = node.ScoreVariants.ToDictionary(
                            pair => pair.Key,
                            pair => pair.Value);

                        // Seed legacy Scores[] slots as default variants so
                        // PublishScoreVariants preserves them. Legacy SET.def nodes
                        // populate Scores[] directly without publishing ScoreVariants
                        // entries; without this seed, publication clears every non-null
                        // Scores slot lacking a 1.00x key, losing the other difficulties
                        // and their metadata when returning to song select.
                        for (int legacyIndex = 0;
                            legacyIndex < node.Scores.Length;
                            legacyIndex++)
                        {
                            if (node.Scores[legacyIndex] == null)
                                continue;
                            var legacyKey = new ScoreVariantKey(
                                legacyIndex,
                                PlaySpeedRange.Default);
                            if (!variants.ContainsKey(legacyKey))
                                variants[legacyKey] = node.Scores[legacyIndex];
                        }

                        variants[variantKey] = published;
                        node.PublishScoreVariants(variants);
                        updated = true;
                    }
                }
            }

            if (node.Children != null)
            {
                foreach (var child in node.Children)
                {
                    switch (PublishNodeScoreVariant(
                        child,
                        chartId,
                        instrument,
                        playSpeedPercent,
                        fresh))
                    {
                        case ScoreRefreshOutcome.Published:
                            updated = true;
                            break;
                        case ScoreRefreshOutcome.AlreadyCurrent:
                            sawAlreadyCurrent = true;
                            break;
                    }
                }
            }

            if (updated) return ScoreRefreshOutcome.Published;
            if (sawAlreadyCurrent) return ScoreRefreshOutcome.AlreadyCurrent;
            return ScoreRefreshOutcome.NoMatch;
        }

        /// <summary>
        /// Returns true when <paramref name="fresh"/> is stale relative to (or
        /// carries no new information beyond) <paramref name="current"/>, so the
        /// publication should be skipped. Compares <see cref="SongScore.PlayCount"/>
        /// (monotonic per chart+instrument+speed) first, then <see
        /// cref="SongScore.LastPlayedAt"/> as a tiebreaker when PlayCount is equal.
        /// </summary>
        private static bool IsStaleOrEqualSnapshot(SongScore fresh, SongScore current)
        {
            if (fresh.PlayCount < current.PlayCount)
                return true;
            if (fresh.PlayCount > current.PlayCount)
                return false;
            // Equal PlayCount — compare LastPlayedAt. A missing timestamp on either
            // side means we cannot prove freshness; treat as equal (skip) to avoid
            // a redundant overwrite that could regress a concurrently published
            // snapshot whose timestamp is identical.
            if (fresh.LastPlayedAt.HasValue && current.LastPlayedAt.HasValue)
            {
                return fresh.LastPlayedAt.Value <= current.LastPlayedAt.Value;
            }
            return true;
        }

        /// <summary>
        /// Mirrors a freshly published score snapshot into the matching
        /// <see cref="SongChart"/>'s <see cref="SongChart.Scores"/> collection
        /// on the node's <see cref="SongListNode.DatabaseSong"/>. Replaces an
        /// existing entry with the same instrument and play speed, or appends
        /// if none exists yet. Silently no-ops when the node has no database
        /// song or the chart is not found on this node (e.g. a legacy set.def
        /// node whose scores are matched by difficulty rather than chart id).
        /// </summary>
        private static void UpdateChartEntityScore(
            SongListNode node,
            int chartId,
            SongScore published)
        {
            var charts = node.DatabaseSong?.Charts;
            if (charts == null) return;

            SongChart? targetChart = null;
            foreach (var chart in charts)
            {
                if (chart.Id == chartId)
                {
                    targetChart = chart;
                    break;
                }
            }
            if (targetChart == null) return;

            // Copy-on-write: build a replacement list so concurrent readers
            // enumerating targetChart.Scores (e.g. SongStatusPanel's
            // ResolveChartScore FirstOrDefault) never observe an in-place
            // mutation that would throw "Collection was modified". The previous
            // collection is left untouched and the new one is published via an
            // atomic reference assignment, preserving the existing
            // replace-by-instrument-and-play-speed semantics.
            var updated = new List<SongScore>(targetChart.Scores);
            SongScore? existing = null;
            foreach (var score in updated)
            {
                if (score.Instrument == published.Instrument
                    && score.PlaySpeedPercent == published.PlaySpeedPercent)
                {
                    existing = score;
                    break;
                }
            }

            if (existing != null)
            {
                updated.Remove(existing);
            }
            updated.Add(published.Clone());
            targetChart.Scores = updated;
        }

        #endregion

        #region Helper Methods

        private string[] GetCurrentSearchPathsSnapshot()
        {
            lock (_lockObject)
                return _currentSearchPaths.ToArray();
        }

        /// <summary>
        /// Checks if enumeration is needed based on database accessibility and
        /// filesystem freshness. Delegates to the stateless scanner, which
        /// evaluates the same canonical root set the scan would use.
        /// </summary>
        public Task<bool> NeedsEnumerationAsync(string[] searchPaths, bool forceEnumeration = false) =>
            _scanner.NeedsEnumerationAsync(searchPaths, forceEnumeration);

        /// <summary>
        /// Returns the subset of <paramref name="batch"/>'s active roots that
        /// had NO recoverable (non-root) enumeration errors. Roots with
        /// recoverable errors (file parse failures, set.def read failures) are
        /// excluded so their watermarks are not advanced, forcing the next
        /// freshness check to rescan and retry the failed operation rather than
        /// silently accepting stale content.
        /// </summary>
        private static IReadOnlyList<string> GetRootsWithoutRecoverableErrors(
            SongEnumerationBatch batch)
        {
            if (batch.Errors.Count == 0)
                return batch.ActiveRoots;

            var dirtyRoots = new HashSet<string>(SongPathIdentity.CanonicalComparer);
            foreach (var error in batch.Errors)
            {
                if (error.IsRootFailure)
                    continue;
                var containingRoot = SongLibraryScanner.FindContainingRoot(
                    error.Path, batch.ActiveRoots);
                if (containingRoot != null)
                {
                    dirtyRoots.Add(containingRoot);
                }
            }

            if (dirtyRoots.Count == 0)
                return batch.ActiveRoots;

            return batch.ActiveRoots
                .Where(root => !dirtyRoots.Contains(root))
                .ToArray();
        }

        /// <summary>
        /// Persists, for every root of a published scan, how many tabs it
        /// rejected for having no playable drum part (HPA-693). Roots with none
        /// are written as 0 so a tab that gained drums stops counting.
        /// </summary>
        private async Task SetRejectedChartCountsAsync(SongEnumerationBatch batch)
        {
            var counts = new Dictionary<string, int>(SongPathIdentity.CanonicalComparer);
            foreach (var root in batch.ActiveRoots)
                counts.TryAdd(root, 0);
            foreach (var path in batch.RejectedChartPaths)
            {
                var root = SongLibraryScanner.FindContainingRoot(path, batch.ActiveRoots);
                if (root != null)
                    counts[root]++;
            }

            await _catalog.SetRejectedChartCountsAsync(counts).ConfigureAwait(false);
        }

        /// <summary>
        /// Persists the enumeration watermark after a successful
        /// <see cref="SongEnumerationOutcome.ImportedAndPublished"/> result.
        /// The watermark is the backed-off START of filesystem traversal
        /// (captured in <see cref="EnumerateAndImportSongsCoreAsync"/>), not the
        /// completion time, so that files edited during the scan remain newer
        /// than the watermark and trigger the next freshness check. Updated only
        /// for the roots in the successful batch so a partial multi-root scan
        /// cannot advance another root's watermark. Unrelated database writes
        /// (bookmark toggles, score saves) cannot advance either value.
        /// <para>
        /// The global and per-root watermarks are written as one atomic database
        /// transaction. A crash or failure mid-write cannot leave the global
        /// timestamp committed while per-root rows are absent, which would
        /// otherwise force a conservative full rescan on every startup until
        /// the per-root rows catch up.
        /// </para>
        /// </summary>
        private async Task SetEnumerationWatermarkAsync(
            IReadOnlyList<string> activeRoots,
            DateTime startUtc)
        {
            try
            {
                var success = await _catalog
                    .SetEnumerationWatermarkAtomicallyAsync(startUtc, activeRoots)
                    .ConfigureAwait(false);
                if (!success)
                {
                    // A failed write leaves the previous watermark in place (or
                    // none), so the next freshness check remains conservative.
                    // No partial state is possible because the write is atomic.
                    // The database service swallows the underlying exception and
                    // returns false, so this is the only place it is reported.
                    _logger.LogWarning(
                        "Enumeration watermark write failed; the next freshness check will stay conservative");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error updating enumeration watermark");
            }
        }

        /// <summary>
        /// The library is game-owned for the whole process lifetime (HPA-801):
        /// the singleton-era <c>Clear()</c>/<c>ResetInstanceForTesting()</c> were
        /// deleted, and disposal happens in <see cref="Dispose"/> when the game
        /// shuts down.
        /// </summary>
        public void Dispose()
        {
            // Cancel under the enumeration gate: EndEnumeration clears and
            // disposes the source under this same lock, so an enumeration
            // finishing during shutdown cannot be disposed between the read
            // and Cancel() (an ObjectDisposedException here would also skip
            // the remaining BaseGame cleanup). Source disposal stays with the
            // enumeration's finally.
            lock (_lockObject)
            {
                _enumCancellation?.Cancel();
            }

            _database.Dispose();
        }

        #endregion
    }

    #region Supporting Classes

    /// <summary>
    /// Event args for enumeration progress
    /// </summary>
    public class EnumerationProgressEventArgs : EventArgs
    {
        public EnumerationProgress Progress { get; }

        public EnumerationProgressEventArgs(EnumerationProgress progress)
        {
            Progress = progress;
        }
    }

    /// <summary>
    /// Enumeration progress information
    /// </summary>
    public class EnumerationProgress
    {
        public string CurrentOperation { get; set; } = "";
        public string CurrentFile { get; set; } = "";
        public int ProcessedCount { get; set; }
        public int DiscoveredSongs { get; set; }
        public string CurrentDirectory { get; set; } = "";
    }

    #endregion

    /// <summary>
    /// Outcome of an in-memory score refresh after a database save. Distinguishes
    /// a successful publication from a race-resolved no-op (a concurrent refresh
    /// already published a newer snapshot) and a true failure (no matching node).
    /// </summary>
    internal enum ScoreRefreshOutcome
    {
        /// <summary>
        /// No matching song-list node was found, or the database service/fresh score
        /// was unavailable. The caller should report a refresh failure so the UI can
        /// prompt a retry.
        /// </summary>
        NoMatch,

        /// <summary>
        /// At least one matching node published the fresh score snapshot.
        /// </summary>
        Published,

        /// <summary>
        /// Every matching node already held a snapshot at least as new as the fresh
        /// one (a concurrent refresh won the publication race). The in-memory state
        /// is already consistent with the database, so the caller reports success.
        /// </summary>
        AlreadyCurrent,
    }
}
