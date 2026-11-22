#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.Song.Import;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SongEntity = DTXMania.Game.Lib.Song.Entities.Song;
using SongScoreEntity = DTXMania.Game.Lib.Song.Entities.SongScore;

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
            ParseSongEntitiesCoreAsync = parseSongEntitiesAsync
                ?? DTXChartParser.ParseSongEntitiesAsync;
            EnumerateFilesCore = enumerateFiles ?? Directory.EnumerateFiles;
            EnumerateDirectoriesCore = enumerateDirectories ?? Directory.EnumerateDirectories;
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

        /// <summary>
        /// The hierarchy builder carrying the shared <see cref="SongDefinitionReader"/>
        /// instance and BOX/root factory; exposed for tests that drive warm-side
        /// steps directly (e.g. pending-node finalization).
        /// </summary>
        internal SongHierarchyBuilder Hierarchy => _hierarchy;

        internal SongRootPolicy RootPolicy { get; }

        /// <summary>
        /// The root whose folders merge into the library's top level rather than
        /// getting a root BOX. Callers decide whether to scan it at all.
        /// </summary>
        internal string BundledSongsRoot { get; }

        // Constructor seams (HPA-801 Decision E): immutable per instance so tests
        // inject collaborators exactly once, at construction.
        internal Func<string, Task<(SongEntity song, SongChart chart)>>
            ParseSongEntitiesCoreAsync { get; }

        internal Func<string, IEnumerable<string>> EnumerateFilesCore { get; }

        internal Func<string, IEnumerable<string>> EnumerateDirectoriesCore { get; }

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
        /// Bulk import, chart inventory, watermark and rejected-count queries
        /// over the shared database (HPA-801).
        /// </summary>
        internal SongCatalogStore Catalog => _catalog;

        /// <summary>
        /// Score save transactions and score/history reads over the shared
        /// database (HPA-801).
        /// </summary>
        internal SongScoreStore ScoreStore => _scoreStore;

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
        /// Number of chart rows in the database whose files reside under one of
        /// <paramref name="activeRoots"/>. This is the active-root-scoped
        /// counterpart to the global chart count: cache freshness checks compare
        /// it against the filesystem chart-file count, which is also scoped to the
        /// active roots. Using the global count instead would count retained rows
        /// belonging to a removed root and force a full rescan on every subsequent
        /// startup. Counts charts (not score rows) so the database side matches
        /// the one-file-per-chart filesystem contract.
        /// </summary>
        internal async Task<int> GetActiveDatabaseChartCountAsync(
            IReadOnlyList<string> activeRoots)
        {
            try
            {
                return await _catalog.GetActiveChartCountAsync(activeRoots);
            }
            catch
            {
                return 0;
            }
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
            lock (_lockObject)
            {
                searchPaths = _currentSearchPaths.ToArray();
            }

            if (searchPaths.Length > 0)
            {
                await BuildHierarchyFromDatabaseOnceAsync(searchPaths).ConfigureAwait(false);
            }
            else
            {
            }
        }

        #endregion

        #region Initialization and Database Management


        #endregion

        #region Song Enumeration

        internal async Task<SongEnumerationBatch> BuildEnumerationBatchAsync(
            string[] searchPaths,
            IProgress<EnumerationProgress>? progress,
            CancellationToken cancellationToken)
        {
            var builder = CreateBatchBuilder(searchPaths);
            var rootHeaders = await _hierarchy.ResolveSongRootHeadersOrNullAsync(
                builder.ActiveRoots,
                cancellationToken).ConfigureAwait(false);
            foreach (var root in builder.ActiveRoots)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var rootBox = rootHeaders != null &&
                    rootHeaders.TryGetValue(root, out var header)
                    ? _hierarchy.CreateSongRootBoxNode(root, header)
                    : null;
                await EnumerateDirectoryIntoBatchAsync(
                    root,
                    rootBox,
                    builder,
                    progress,
                    cancellationToken).ConfigureAwait(false);
                if (rootBox != null && rootBox.Children.Count > 0)
                    AddNodeToBatch(builder, parent: null, rootBox);
            }

            return builder.Complete();
        }

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

        private sealed class SongEnumerationBatchBuilder
        {
            public SongEnumerationBatchBuilder(
                IReadOnlyList<string> activeRoots,
                List<SongEnumerationError>? errors = null)
            {
                ActiveRoots = activeRoots;
                if (errors != null)
                    Errors.AddRange(errors);
            }

            public IReadOnlyList<string> ActiveRoots { get; }
            public HashSet<string> DiscoveredChartPaths { get; } =
                new(SongPathIdentity.CanonicalComparer);
            public List<SongImportCandidate> Candidates { get; } = new();
            public List<SongListNode> RootNodes { get; } = new();
            public List<PendingSongNode> PendingSongs { get; } = new();
            public List<SongEnumerationError> Errors { get; } = new();
            public HashSet<string> RejectedChartPaths { get; } =
                new(SongPathIdentity.CanonicalComparer);

            public SongEnumerationBatch Complete()
            {
                return new SongEnumerationBatch
                {
                    ActiveRoots = ActiveRoots,
                    DiscoveredChartPaths = DiscoveredChartPaths,
                    Candidates = Candidates,
                    RootNodes = RootNodes,
                    PendingSongs = PendingSongs,
                    Errors = Errors,
                    RejectedChartPaths = RejectedChartPaths,
                    IsComplete = true
                };
            }
        }

        private sealed class TemporaryPendingGroup
        {
            public TemporaryPendingGroup(
                string groupKey,
                SongListNode placeholder)
            {
                GroupKey = groupKey;
                Placeholder = placeholder;
            }

            public string GroupKey { get; }
            public SongListNode Placeholder { get; }
            public List<string> OrderedChartPaths { get; } = new();
        }

        private SongEnumerationBatchBuilder CreateBatchBuilder(
            IEnumerable<string> searchPaths)
        {
            ArgumentNullException.ThrowIfNull(searchPaths);
            var configuredRoots = searchPaths.ToArray();
            var validation = RootPolicy.Validate(configuredRoots);
            var errors = new List<SongEnumerationError>();
            foreach (var diagnostic in validation.Diagnostics)
            {
                if (diagnostic.IsWarning)
                    continue;

                var error = new SongEnumerationError(
                    diagnostic.Path,
                    diagnostic.Message,
                    IsRootFailure: true);
                errors.Add(error);
            }

            var activeRoots = new List<string>();
            foreach (var normalizedRoot in validation.CanonicalRoots)
            {
                var probe = RootPolicy.Probe(normalizedRoot);
                if (probe.Availability == SongRootAvailability.Available)
                {
                    activeRoots.Add(normalizedRoot);
                    continue;
                }

                var message = probe.Availability == SongRootAvailability.Missing
                    ? $"Configured song root does not exist: {normalizedRoot}"
                    : $"Cannot read configured song root ({probe.Reason}): {normalizedRoot}";
                var error = new SongEnumerationError(
                    normalizedRoot,
                    message,
                    IsRootFailure: true);
                errors.Add(error);
            }

            return new SongEnumerationBatchBuilder(activeRoots, errors);
        }

        private async Task EnumerateDirectoryIntoBatchAsync(
            string directoryPath,
            SongListNode? parent,
            SongEnumerationBatchBuilder builder,
            IProgress<EnumerationProgress>? progress,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var setDefPath = Path.Combine(directoryPath, "set.def");
            if (File.Exists(setDefPath))
            {
                await ParseSetDefinitionIntoBatchAsync(
                    setDefPath,
                    parent,
                    builder,
                    progress,
                    cancellationToken).ConfigureAwait(false);
                return;
            }

            var subdirectories = EnumerateDirectoriesCore(directoryPath)
                .Select(SongPathIdentity.Normalize)
                .OrderBy(path => path, SongPathIdentity.CanonicalComparer)
                .ToArray();
            foreach (var subdirectoryPath in subdirectories)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var directory = new DirectoryInfo(subdirectoryPath);
                var boxDefPath = Path.Combine(subdirectoryPath, "box.def");
                var isBox = directory.Name.StartsWith(
                    "DTXFiles.",
                    StringComparison.OrdinalIgnoreCase) ||
                    File.Exists(boxDefPath);
                if (!isBox)
                {
                    await EnumerateDirectoryIntoBatchAsync(
                        subdirectoryPath,
                        parent,
                        builder,
                        progress,
                        cancellationToken).ConfigureAwait(false);
                    continue;
                }

                BoxDefinition? boxDefinition = null;
                if (File.Exists(boxDefPath))
                {
                    try
                    {
                        boxDefinition = await _definitions.ParseBoxDefinitionAsync(
                            boxDefPath,
                            cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        builder.Errors.Add(new SongEnumerationError(
                            SongPathIdentity.Normalize(boxDefPath),
                            exception.Message,
                            IsRootFailure: false));
                    }
                }

                var boxNode = _hierarchy.CreateBoxNodeFromDirectory(
                    directory,
                    parent,
                    boxDefinition);
                await EnumerateDirectoryIntoBatchAsync(
                    subdirectoryPath,
                    boxNode,
                    builder,
                    progress,
                    cancellationToken).ConfigureAwait(false);
                if (boxNode.Children.Count > 0)
                    AddNodeToBatch(builder, parent, boxNode);
            }

            var groups = new Dictionary<string, TemporaryPendingGroup>(
                StringComparer.Ordinal);
            var files = EnumerateFilesCore(directoryPath)
                .Where(DTXChartParser.IsSupportedFile)
                .Select(SongPathIdentity.Normalize)
                .OrderBy(path => path, SongPathIdentity.CanonicalComparer)
                .ToArray();
            foreach (var normalizedPath in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                builder.DiscoveredChartPaths.Add(normalizedPath);
                var fileName = Path.GetFileName(normalizedPath);
                try
                {
                    var (song, chart) = await ParseSongEntitiesCoreAsync(
                        normalizedPath).ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    chart.FilePath = normalizedPath;
                    var groupKey = SongPathIdentity.ForOrdinaryChart(
                        normalizedPath,
                        song.Title,
                        song.Artist);
                    if (!groups.TryGetValue(groupKey, out var group))
                    {
                        var placeholder = CreateTemporarySongNode(
                            song,
                            chart,
                            parent);
                        group = new TemporaryPendingGroup(
                            groupKey,
                            placeholder);
                        groups.Add(groupKey, group);
                        AddNodeToBatch(builder, parent, placeholder);
                    }

                    var groupOrder = group.OrderedChartPaths.Count;
                    group.OrderedChartPaths.Add(normalizedPath);
                    AddTemporaryScore(
                        group.Placeholder,
                        chart,
                        groupOrder,
                        authoredLabel: chart.DifficultyLabel);
                    builder.Candidates.Add(new SongImportCandidate(
                        song,
                        chart,
                        normalizedPath,
                        groupKey,
                        groupOrder));
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (UnplayableTabException exception)
                {
                    RejectUnplayableTab(builder, normalizedPath, exception);
                }
                catch (Exception exception)
                {
                    builder.Errors.Add(new SongEnumerationError(
                        normalizedPath,
                        exception.Message,
                        IsRootFailure: false));
                }

                progress?.Report(new EnumerationProgress
                {
                    CurrentFile = fileName,
                    CurrentDirectory = directoryPath,
                    ProcessedCount = builder.DiscoveredChartPaths.Count,
                    DiscoveredSongs = builder.Candidates.Count
                });
            }

            foreach (var group in groups.Values)
            {
                builder.PendingSongs.Add(new PendingSongNode(
                    group.GroupKey,
                    group.Placeholder,
                    group.OrderedChartPaths));
            }
        }

        private async Task ParseSetDefinitionIntoBatchAsync(
            string setDefPath,
            SongListNode? parent,
            SongEnumerationBatchBuilder builder,
            IProgress<EnumerationProgress>? progress,
            CancellationToken cancellationToken)
        {
            string[] lines;
            try
            {
                lines = await _definitions.ReadAllLines(
                    setDefPath,
                    Encoding.UTF8,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                ProtectSupportedChartPathsInSubtree(
                    Path.GetDirectoryName(setDefPath) ?? "",
                    builder,
                    cancellationToken);
                builder.Errors.Add(new SongEnumerationError(
                    SongPathIdentity.Normalize(setDefPath),
                    exception.Message,
                    IsRootFailure: false));
                return;
            }

            string setTitle;
            Dictionary<int, (string label, string file)> difficulties;
            try
            {
                (setTitle, difficulties) = _definitions.ParseSetDefContent(
                    lines,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                ProtectSupportedChartPathsInSubtree(
                    Path.GetDirectoryName(setDefPath) ?? "",
                    builder,
                    cancellationToken);
                builder.Errors.Add(new SongEnumerationError(
                    SongPathIdentity.Normalize(setDefPath),
                    exception.Message,
                    IsRootFailure: false));
                return;
            }

            var directory = Path.GetDirectoryName(setDefPath) ?? "";
            var normalizedSetDefDirectory = SongPathIdentity.Normalize(directory);
            var groupKey = SongPathIdentity.ForSetDefinition(setDefPath);
            SongListNode? placeholder = null;
            var orderedPaths = new List<string>();
            var orderedDifficulties = difficulties
                .OrderBy(difficulty => difficulty.Key)
                .ToArray();
            foreach (var difficulty in orderedDifficulties)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var level = difficulty.Key;
                var (label, fileName) = difficulty.Value;
                if (string.IsNullOrWhiteSpace(fileName))
                    continue;

                // Reject absolute set.def chart references outright.
                // Path.Combine silently overrides the set.def directory when
                // the second argument is rooted, which would let an absolute
                // path point anywhere on disk.
                if (Path.IsPathRooted(fileName))
                {
                    continue;
                }

                var path = SongPathIdentity.Normalize(
                    Path.Combine(directory, fileName));
                if (!DTXChartParser.IsSupportedFile(path) ||
                    !File.Exists(path))
                {
                    continue;
                }

                // Reject set.def chart references that escape the directory
                // containing the set.def. A reference like
                // ../Sibling/chart.dtx (still inside the active root) creates a
                // duplicate placeholder: one under the set.def's directory, one
                // under the chart's own directory. Both resolve to the single
                // persisted chart in the freshly published hierarchy, but on a
                // cached restart the chart is placed only by its physical
                // path, so the duplicate/misplaced node disappears — an
                // inconsistency between fresh publish and cached
                // reconstruction. Valid child paths (e.g. charts/basic.dtx)
                // are permitted.
                if (!SongPathIdentity.IsUnderNormalizedRoot(
                        path,
                        normalizedSetDefDirectory))
                {
                    continue;
                }

                builder.DiscoveredChartPaths.Add(path);
                var groupOrder = orderedPaths.Count;
                orderedPaths.Add(path);
                try
                {
                    var (song, chart) = await ParseSongEntitiesCoreAsync(path)
                        .ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    chart.FilePath = path;
                    chart.DifficultyLevel = level;
                    if (!string.IsNullOrWhiteSpace(label))
                    {
                        chart.DifficultyLabel = label.Length > 50
                            ? label[..50]
                            : label;
                    }

                    if (!string.IsNullOrWhiteSpace(setTitle))
                        song.Title = setTitle;
                    else if (string.IsNullOrWhiteSpace(song.Title))
                        song.Title = new DirectoryInfo(directory).Name;

                    placeholder ??= CreateTemporarySongNode(
                        song,
                        chart,
                        parent);
                    AddTemporaryScore(
                        placeholder,
                        chart,
                        groupOrder,
                        authoredLabel: string.IsNullOrWhiteSpace(label)
                            ? $"Level {level}"
                            : label);
                    builder.Candidates.Add(new SongImportCandidate(
                        song,
                        chart,
                        path,
                        groupKey,
                        groupOrder));
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (UnplayableTabException exception)
                {
                    RejectUnplayableTab(builder, path, exception);
                }
                catch (Exception exception)
                {
                    builder.Errors.Add(new SongEnumerationError(
                        path,
                        exception.Message,
                        IsRootFailure: false));
                }

                progress?.Report(new EnumerationProgress
                {
                    CurrentFile = Path.GetFileName(path),
                    CurrentDirectory = directory,
                    ProcessedCount = builder.DiscoveredChartPaths.Count,
                    DiscoveredSongs = builder.Candidates.Count
                });
            }

            if (placeholder == null)
                return;

            AddNodeToBatch(builder, parent, placeholder);
            builder.PendingSongs.Add(new PendingSongNode(
                groupKey,
                placeholder,
                orderedPaths));
        }

        private void ProtectSupportedChartPathsInSubtree(
            string directoryPath,
            SongEnumerationBatchBuilder builder,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var path in EnumerateFilesCore(directoryPath)
                .Where(DTXChartParser.IsSupportedFile)
                .Select(SongPathIdentity.Normalize)
                .OrderBy(path => path, SongPathIdentity.CanonicalComparer))
            {
                cancellationToken.ThrowIfCancellationRequested();
                builder.DiscoveredChartPaths.Add(path);
            }

            foreach (var subdirectory in EnumerateDirectoriesCore(directoryPath)
                .Select(SongPathIdentity.Normalize)
                .OrderBy(path => path, SongPathIdentity.CanonicalComparer))
            {
                ProtectSupportedChartPathsInSubtree(
                    subdirectory,
                    builder,
                    cancellationToken);
            }
        }

        private static SongListNode CreateTemporarySongNode(
            SongEntity song,
            SongChart chart,
            SongListNode? parent)
        {
            var node = SongListNode.CreateSongNode(
                song,
                chart,
                hydratePersistedScores: false);
            node.Scores = new SongScoreEntity[5];
            node.PublishScoreVariants(
                Array.Empty<KeyValuePair<ScoreVariantKey, SongScoreEntity>>());
            node.Parent = parent;
            node.DatabaseSongId = null;
            return node;
        }

        private static void AddTemporaryScore(
            SongListNode node,
            SongChart chart,
            int scoreIndex,
            string? authoredLabel)
        {
            if (scoreIndex < 0 || scoreIndex >= node.Scores.Length)
                return;

            var (instrument, difficultyLevel) =
                SongHierarchyBuilder.GetPrimaryInstrumentAndDifficulty(chart);
            var label = string.IsNullOrWhiteSpace(authoredLabel)
                ? $"Level {scoreIndex + 1}"
                : authoredLabel;
            node.DifficultyLabels[scoreIndex] = label;
            node.SetScore(scoreIndex, new SongScoreEntity
            {
                Instrument = instrument,
                DifficultyLevel = difficultyLevel,
                DifficultyLabel = label
            });
        }

        /// <summary>
        /// A tab with no playable drum part is a stable result, not a
        /// recoverable error (HPA-693): it does not hold back its root's
        /// watermark, and dropping it from the discovered paths removes any
        /// row left from when the tab had drums. The freshness check adds the
        /// persisted per-root rejected count to the database chart count,
        /// because the inventory still counts the file.
        /// </summary>
        private static void RejectUnplayableTab(
            SongEnumerationBatchBuilder builder,
            string path,
            UnplayableTabException exception)
        {
            builder.DiscoveredChartPaths.Remove(path);
            builder.RejectedChartPaths.Add(path);
        }

        private static void AddNodeToBatch(
            SongEnumerationBatchBuilder builder,
            SongListNode? parent,
            SongListNode node)
        {
            if (parent != null)
            {
                if (!parent.Children.Contains(node))
                    parent.AddChild(node);
                return;
            }

            if (!builder.RootNodes.Contains(node))
            {
                node.Parent = null;
                node.BreadcrumbPath = node.Title;
                builder.RootNodes.Add(node);
            }
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
        /// Cancels ongoing enumeration
        /// </summary>
        public void CancelEnumeration()
        {
            lock (_lockObject)
            {
                _enumCancellation?.Cancel();
            }
        }

        /// <summary>
        /// Builds the song list from existing database entries
        /// Used when the database is already populated but _rootSongs is empty
        /// Preserves the original folder hierarchy structure
        /// </summary>
        internal async Task BuildHierarchyFromDatabaseOnceAsync(string[] searchPaths)
        {
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
                    hierarchy.ActiveChartCount);
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
            int activeChartCount)
        {
            SongLibrarySnapshot snapshot;
            lock (_lockObject)
            {
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
        /// Persists a complete PerformanceSummary (score + skill + judgement counts) for a
        /// specific chart and instrument. Forwards to SongScoreStore, then refreshes
        /// the in-memory score cache for that chart so the play-history badge and score
        /// fields on the song-list node reflect the just-saved play without a full
        /// song-database reload.
        /// </summary>
        /// <remarks>
        /// Per the accepted implementation plan (invariant 8): "The Result stage cannot
        /// report a save as successful until the database write and matching in-memory
        /// refresh have completed." A save that commits to the database but cannot
        /// refresh at least one matching in-memory node is therefore reported as
        /// <see cref="ScoreSaveStatus.Failed"/> so the UI does not display SAVED over a
        /// stale song-selection cache. The player can retry; the database write is
        /// idempotent (RunId receipt), so a retry that succeeds at refresh will surface
        /// <see cref="ScoreSaveStatus.AlreadySaved"/> as a successful save.
        /// </remarks>
        public async Task<ScoreSaveResult> UpdateScoreAsync(
            int chartId,
            EInstrumentPart instrument,
            DTXMania.Game.Lib.Stage.Performance.PerformanceSummary summary)
        {
            var result = await _scoreStore
                .UpdateScoreAsync(chartId, instrument, summary)
                .ConfigureAwait(false);

            if (!result.IsSuccess)
                return result;

            // The save committed. The plan's contract requires the matching in-memory
            // refresh to complete before the UI may report success. A refresh failure
            // (transient DB read error) or a no-op refresh (no matching node found,
            // e.g. the song list was rebuilt/cleared between save and refresh) must
            // not be masked by returning the original successful result. A refresh
            // that finds a newer snapshot already published for the same score key
            // (chart+instrument+speed) by a concurrent save is treated as success:
            // the in-memory state is already consistent with (or ahead of) this
            // caller's snapshot, so the publication is intentionally skipped.
            ScoreRefreshOutcome outcome;
            try
            {
                outcome = await RefreshInMemoryScoreForChartAsync(
                    chartId,
                    instrument,
                    summary.PlaySpeedPercent).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "In-memory score refresh failed after successful save for chart {ChartId}", chartId);
                outcome = ScoreRefreshOutcome.NoMatch;
            }

            if (outcome == ScoreRefreshOutcome.NoMatch)
            {
                return ScoreSaveResult.Failed(
                    "The score was saved to the database, but the song list could not be "
                    + "refreshed. Please retry to update the displayed score.");
            }

            return result;
        }

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

        /// <summary>
        /// <summary>
        /// Imports DTXManiaNX drum scores from sibling &lt;chart&gt;.score.ini files for every
        /// drum chart already in the database. Best-of merge with snapshot-delta counters;
        /// safe to run repeatedly. Reports progress per chart. See the design spec.
        /// </summary>
        public async Task<NxImportResult> ImportNxScoresAsync(
            IProgress<NxImportProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            var result = new NxImportResult();
            var db = _database;
            try
            {
                using var probe = db.CreateContext();
            }
            catch (InvalidOperationException exception)
            {
                // The game-owned database exists from launch but Startup has not
                // initialized it yet (or initialization failed): report the explicit
                // unavailable result the Config stage maps to its status text.
                _logger.LogWarning(
                    exception,
                    "NX import unavailable: song database is not initialized");
                result.DbUnavailable = true;
                return result;
            }

            var importer = new NxScoreImporter();
            using var context = db.CreateContext();

            var sw = System.Diagnostics.Stopwatch.StartNew();

            var charts = await context.SongCharts
                .Include(c => c.Song)
                .Where(c => c.HasDrumChart)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (var chart in charts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                result.Scanned++;
                try
                {
                    if (string.IsNullOrEmpty(chart.FilePath) || !File.Exists(chart.FilePath))
                    {
                        result.Skipped++;
                    }
                    else
                    {
                        var iniPath = chart.FilePath + ".score.ini";
                        var data = NxScoreIniParser.Parse(iniPath);
                        if (data == null)
                        {
                            result.Skipped++;
                        }
                        else
                        {
                            await importer.MergeAsync(context, chart, data, cancellationToken).ConfigureAwait(false);
                            result.Imported++;
                            // Clear tracked entities after each successful merge to prevent
                            // memory accumulation when importing large libraries.
                            context.ChangeTracker.Clear();
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    result.Errors++;
                    // Clear tracked entities so a failed merge does not poison the next chart.
                    context.ChangeTracker.Clear();
                    _logger.LogWarning(ex, "NX import error for {ChartPath}", chart.FilePath);
                }

                progress?.Report(new NxImportProgress
                {
                    Scanned = result.Scanned,
                    Imported = result.Imported,
                    Skipped = result.Skipped,
                    Errors = result.Errors,
                    CurrentFile = Path.GetFileName(chart.FilePath)
                });
            }

            return result;
        }

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
            if (limit <= 0 || ReadsAsNeverInitialized(_database)) return new List<SongListNode>();

            // Push the active-root criterion into the DB query as a set of active
            // chart IDs so that grouping, ordering, AND pagination all use
            // active-chart recency. This is what makes the ordering globally
            // correct: a song promoted up the all-chart ordering by a play on a
            // now-inactive chart can no longer push a more-recently-played active
            // song past the page boundary. With active filtering in SQL, every
            // returned song already has at least one active chart, so a single
            // bounded query is sufficient (no client-side re-pagination).
            IReadOnlyCollection<int>? activeChartIds = null;
            var roots = GetCurrentSearchPathsSnapshot();
            if (roots.Length > 0)
            {
                activeChartIds = await _catalog.GetActiveChartIdsAsync(roots)
                    .ConfigureAwait(false);
            }

            var songs = await _catalog.GetRecentlyPlayedSongsAsync(limit, 0, activeChartIds)
                .ConfigureAwait(false);
            var nodes = new List<SongListNode>(songs.Count);
            AppendActiveNodes(songs, limit, ref nodes);
            return nodes;
        }

        private void AppendActiveNodes(
            List<SongEntity> songs,
            int limit,
            ref List<SongListNode> nodes)
        {
            // The DB query already orders by active-chart recency, so callers
            // receive songs in the correct global order; we only filter to each
            // song's active charts and build nodes, preserving DB order.
            var items = songs
                .Select(song =>
                {
                    var charts = GetActiveCharts(song);
                    var lastPlayedAt = charts
                        .SelectMany(chart =>
                            chart.Scores ?? Enumerable.Empty<SongScore>())
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
                            chart.Scores ?? Enumerable.Empty<SongScore>())
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
        /// (BeginBookmarksLoad) can distinguish a genuine failure from an empty result.
        /// </summary>
        public async Task<List<SongListNode>> GetBookmarkedNodesAsync()
        {
            // Same contract as GetRecentlyPlayedNodesAsync: only a
            // never-initialized database reads as empty; a failed or purged
            // initialization surfaces the database failure.
            if (ReadsAsNeverInitialized(_database)) return new List<SongListNode>();

            var songs = await _catalog.GetBookmarkedSongsAsync().ConfigureAwait(false);
            var nodes = new List<SongListNode>(songs.Count);
            foreach (var song in songs)
            {
                var charts = GetActiveCharts(song);
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
        /// Sets or clears the bookmark flag on a song. Safe no-op when the database is
        /// unavailable.
        /// </summary>
        public async Task SetBookmarkAsync(int songId, bool bookmarked)
        {
            // Writes fail clearly before Startup's initialization (HPA-801):
            // a purged/never-initialized database must surface the fault to the
            // caller's rollback path instead of silently no-oping.
            await _catalog.SetBookmarkAsync(songId, bookmarked).ConfigureAwait(false);
        }

        #endregion

        #region Helper Methods

        private string[] GetCurrentSearchPathsSnapshot()
        {
            lock (_lockObject)
                return _currentSearchPaths.ToArray();
        }

        private SongChart[] GetActiveCharts(SongEntity song)
        {
            var roots = GetCurrentSearchPathsSnapshot();
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

        /// <summary>
        /// Checks if enumeration is needed based on database existence and directory modification times
        /// </summary>
        public async Task<bool> NeedsEnumerationAsync(string[] searchPaths, bool forceEnumeration = false)
        {
            try
            {
                if (forceEnumeration)
                {
                    return true;
                }

                // Check if database service exists and is accessible
                if (!await _database.CanConnectAsync())
                {
                    return true;
                }

                // If database has no songs, we need enumeration (narrow EXISTS
                // read; the old five-COUNT aggregate statistics query is gone)
                if (!await _catalog.HasAnySongsAsync())
                {
                    return true;
                }


                // Check for filesystem changes by comparing directory modification times
                // Cache freshness must evaluate the same canonical root set as database
                // hierarchy rebuilds and filesystem enumeration. Otherwise duplicate or
                // overlapping configured roots can double-count charts here while the
                // published library only contains one root identity.
                var canonicalRoots = RootPolicy.Validate(searchPaths)
                    .CanonicalRoots
                    .ToArray();
                var changeDetected = await DetectFilesystemChangesAsync(canonicalRoots);
                if (changeDetected)
                {
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error checking enumeration need");
                return true; // Default to enumeration if we can't determine
            }
        }

        /// <summary>
        /// Checks if files in the database still exist at their recorded paths
        /// This detects when files have been moved or deleted.
        /// <para>
        /// Only charts whose <see cref="SongChart.FilePath"/> resides under one of
        /// <paramref name="activeRoots"/> are checked. Charts belonging to removed
        /// (retained) roots are intentionally skipped: their files are expected to be
        /// absent from the active root set, and flagging them as missing would force a
        /// full rescan on every startup. This mirrors the active-root scoping already
        /// applied to the filesystem and database chart counts in
        /// <see cref="DetectFilesystemChangesAsync"/>.
        /// </summary>
        private async Task<bool> CheckDatabaseFilesStillExist(IReadOnlyList<string> activeRoots)
        {
            try
            {
                var charts = await _catalog.GetActiveChartsAsync(activeRoots);
                int missingFiles = 0;
                int updatedFiles = 0;
                int totalFiles = 0;

                foreach (var chart in charts)
                {
                    totalFiles++;
                    if (!string.IsNullOrEmpty(chart.FilePath) && !File.Exists(chart.FilePath))
                    {
                        // File is missing from recorded path - try to find it in new location
                        string? newPath = await FindMovedFileAsync(chart.FilePath);

                        if (!string.IsNullOrEmpty(newPath))
                        {
                            // Found the file in a new location - update database
                            await UpdateChartFilePathAsync(chart.Id, newPath);
                            updatedFiles++;
                        }
                        else
                        {
                            missingFiles++;
                        }
                    }
                }


                // Return true if we had missing files OR updated file paths (both require re-enumeration)
                return missingFiles > 0 || updatedFiles > 0;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error checking database file existence");
                // If we can't check, assume no changes to avoid unnecessary re-enumeration
                return false;
            }
        }

        /// <summary>
        /// Attempts to find a moved file by searching for it in the current search paths
        /// Returns the new path if found, null if not found
        /// </summary>
        private async Task<string?> FindMovedFileAsync(string originalPath)
        {
            try
            {
                var fileName = Path.GetFileName(originalPath);
                if (string.IsNullOrEmpty(fileName))
                    return null;

                // Get current search paths
                var searchPaths = new List<string>();
                // Add current search paths (fallback to defaults if none)
                if (_currentSearchPaths.Length > 0)
                {
                    searchPaths.AddRange(_currentSearchPaths);
                }
                else
                {
                    searchPaths.AddRange(Resources.Constants.SongPaths.Default);
                }

                // You could extend this to get search paths from config if needed

                // Search for the file in all DTX directories within search paths
                foreach (var searchPath in searchPaths.Where(Directory.Exists))
                {
                    // Use async enumeration to avoid blocking
                    var foundFiles = await Task.Run(() =>
                        Directory.EnumerateFiles(searchPath, fileName, SearchOption.AllDirectories).ToList());

                    foreach (var foundFile in foundFiles)
                    {
                        // Verify it's actually a DTX file and has similar content/size
                        if (await IsLikelyMatchAsync(originalPath, foundFile))
                        {
                            return foundFile;
                        }
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error finding moved file {OriginalPath}", originalPath);
                return null;
            }
        }

        /// <summary>
        /// Checks if a found file is likely the same as the original file
        /// Uses filename and file size as basic heuristics
        /// </summary>
        private async Task<bool> IsLikelyMatchAsync(string originalPath, string candidatePath)
        {
            try
            {
                // Basic filename match (already checked by caller)
                if (Path.GetFileName(originalPath) != Path.GetFileName(candidatePath))
                    return false;

                // Check file sizes match (good indicator it's the same file)
                if (File.Exists(originalPath))
                {
                    var originalSize = new FileInfo(originalPath).Length;
                    var candidateSize = new FileInfo(candidatePath).Length;
                    return originalSize == candidateSize;
                }

                // If original doesn't exist, assume candidate is a match based on filename
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error comparing files {OriginalPath} vs {CandidatePath}", originalPath, candidatePath);
                return false;
            }
        }

        /// <summary>
        /// Updates the file path for a chart in the database
        /// </summary>
        private async Task UpdateChartFilePathAsync(int chartId, string newPath)
        {
            try
            {
                using var context = _database.CreateContext();
                var chart = await context.SongCharts.FindAsync(chartId);
                if (chart != null)
                {
                    chart.FilePath = newPath;
                    await context.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error updating chart file path for ID {ChartId}", chartId);
            }
        }

        /// <summary>
        /// Detects filesystem changes in song directories by walking the
        /// available roots once via the shared chart-inventory scanner.
        /// </summary>
        private async Task<bool> DetectFilesystemChangesAsync(string[] searchPaths)
        {
            try
            {
                // Get the last enumeration timestamp from database
                var lastEnumerationTime = await GetLastEnumerationTimestampAsync();
                if (lastEnumerationTime == null)
                {
                    return true; // First time enumeration
                }


                // Probe canonical roots once and carry only currently available
                // roots through every downstream check. A missing or inaccessible
                // configured root is a diagnostic condition, NOT a filesystem
                // change: a full scan cannot make the root available, and
                // treating its absence as a change whenever another root has
                // charts forces a perpetual rescan on every startup. When the
                // root returns (external drive reattached, network share
                // mounted), its retained watermark is used to detect edits even
                // when the chart count still matches the retained database rows.
                var availableRoots = new List<string>(searchPaths.Length);
                foreach (var searchPath in searchPaths)
                {
                    if (string.IsNullOrEmpty(searchPath))
                        continue;
                    if (RootPolicy.Probe(searchPath).Availability == SongRootAvailability.Available)
                        availableRoots.Add(searchPath);
                }

                // Walk the available roots once, mirroring full enumeration's
                // directory and set.def discovery rules. The inventory yields
                // exactly the chart files enumeration would import (set.def-
                // referenced charts only in a set.def directory; case-insensitive
                // extension matching) plus the set.def and directory mtimes used
                // by the change check below.
                var inventory = await ScanChartInventoryAsync(
                    availableRoots.ToArray(), CancellationToken.None).ConfigureAwait(false);

                // File count mismatch is a reliable indicator of added/removed
                // charts. Both counts are scoped to the same available root set:
                // the inventory is restricted to these roots, so the database
                // count must be too. The global chart count would include
                // retained rows belonging to a removed or unavailable root,
                // which can never match the scoped filesystem count and would
                // force a full rescan on every startup. The database side counts
                // charts (one row per chart file), not score rows, so it matches
                // the one-file-per-chart filesystem contract even when a chart
                // has many score variants.
                // Tabs the last scan rejected (HPA-693) are in the inventory
                // but have no database row, so their persisted count is added.
                var currentFileCount = inventory.Charts.Count;
                var databaseChartCount = await GetActiveDatabaseChartCountAsync(availableRoots)
                    + await GetRejectedChartCountAsync(availableRoots).ConfigureAwait(false);


                if (currentFileCount != databaseChartCount)
                {
                    return true;
                }

                // Check if files in database still exist at their recorded paths
                // (detects moves). Scoped to the same available roots as the
                // file/DB counts above so retained charts belonging to a removed
                // or unavailable root are not flagged as missing.
                var filesMovedOrDeleted = await CheckDatabaseFilesStillExist(availableRoots);
                if (filesMovedOrDeleted)
                {
                    return true;
                }

                // Check each available root against its own last successful
                // watermark. A root omitted from a partial scan has no new
                // watermark, so its return is conservatively treated as a change.
                var rootWatermarks = await GetRootEnumerationWatermarksAsync(
                    availableRoots).ConfigureAwait(false);
                if (InventoryHasChangesSince(
                        inventory,
                        availableRoots,
                        rootWatermarks))
                {
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error detecting filesystem changes");
                return true; // Default to enumeration on error
            }
        }

        private async Task<IReadOnlyDictionary<string, DateTime>>
            GetRootEnumerationWatermarksAsync(
                IReadOnlyList<string> availableRoots)
        {
            var watermarks = new Dictionary<string, DateTime>(
                SongPathIdentity.CanonicalComparer);

            foreach (var root in availableRoots)
            {
                var watermark = await _catalog
                    .GetLastSuccessfulEnumerationUtcAsync(root)
                    .ConfigureAwait(false);
                if (watermark.HasValue)
                    watermarks[root] = watermark.Value;
            }

            // A missing per-root watermark must remain missing. Assigning the
            // legacy global timestamp to a root that was never successfully
            // enumerated (or that returned after a partial scan where another
            // root advanced the global value) would hide stale content: the
            // root's retained rows and file mtimes would compare clean against
            // a watermark it never earned, so NeedsEnumerationAsync would
            // return false and never retry the failed parse. The conservative
            // behavior — treat a missing watermark as "needs enumeration" —
            // costs one full scan to initialize per-root metadata on a legacy
            // database, then never triggers again.
            return watermarks;
        }

        /// <summary>
        /// Walks the active song roots once, mirroring full enumeration's
        /// directory and set.def discovery rules, and returns the chart inventory
        /// that would be imported plus the set.def and directory mtimes used by
        /// the change check. Replaces the previous per-extension recursive globs
        /// (six walks for counting plus seven for timestamp checking per root)
        /// with a single walk per root.
        /// <para>
        /// Traversal matches <see cref="EnumerateDirectoryIntoBatchAsync"/>: a
        /// directory containing <c>set.def</c> contributes only the set.def's
        /// referenced charts (and is not recursed); a directory without one
        /// contributes its loose supported charts and recurses into all
        /// subdirectories. Chart paths are deduplicated by normalized path so
        /// overlapping roots or a set.def referencing one file from two
        /// difficulties cannot double-count.
        /// </para>
        /// </summary>
        private Task<ChartInventory> ScanChartInventoryAsync(
            string[] searchPaths, CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                var inventory = new ChartInventory();
                var seenCharts = new HashSet<string>(SongPathIdentity.CanonicalComparer);
                foreach (var root in searchPaths
                    .Where(path => !string.IsNullOrEmpty(path) && Directory.Exists(path)))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ScanChartInventoryDirectory(
                        root, inventory, seenCharts, cancellationToken);
                }
                return inventory;
            }, cancellationToken);
        }

        private void ScanChartInventoryDirectory(
            string directoryPath,
            ChartInventory inventory,
            HashSet<string> seenCharts,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Wrap this directory's access (DirectoryInfo construction and
            // file/directory enumeration) so an inaccessible or unreadable
            // directory skips itself while its siblings continue scanning.
            // OperationCanceledException is NOT caught here, so cancellation
            // from ThrowIfCancellationRequested (above and in the loops below)
            // propagates unchanged.
            try
            {
                var dirInfo = new DirectoryInfo(directoryPath);
                inventory.Directories.Add(new ChartInventoryEntry(
                    SongPathIdentity.Normalize(directoryPath),
                    dirInfo.LastWriteTimeUtc,
                    dirInfo.CreationTimeUtc));

                var setDefPath = Path.Combine(directoryPath, "set.def");
                if (File.Exists(setDefPath))
                {
                    // set.def directory: only referenced charts are imported; loose
                    // charts and subdirectories are NOT scanned (enumeration returns
                    // early after ParseSetDefinitionIntoBatchAsync).
                    var setDefInfo = new FileInfo(setDefPath);
                    inventory.SetDefinitions.Add(new ChartInventoryEntry(
                        SongPathIdentity.Normalize(setDefPath),
                        setDefInfo.LastWriteTimeUtc,
                        setDefInfo.CreationTimeUtc));
                    foreach (var chartPath in EnumerateSetDefReferencedCharts(
                        setDefPath, cancellationToken))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (!seenCharts.Add(chartPath))
                            continue;
                        var chartInfo = new FileInfo(chartPath);
                        inventory.Charts.Add(new ChartInventoryEntry(
                            chartPath,
                            chartInfo.LastWriteTimeUtc,
                            chartInfo.CreationTimeUtc));
                    }
                    return;
                }

                // No set.def: collect loose supported charts (case-insensitive via
                // IsSupportedFile, matching full enumeration) and recurse into all
                // subdirectories. Boxes are organizational only and do not filter
                // charts, so they are recursed like any other directory.
                foreach (var file in EnumerateFilesCore(directoryPath)
                    .Where(DTXChartParser.IsSupportedFile)
                    .Select(SongPathIdentity.Normalize)
                    .OrderBy(path => path, SongPathIdentity.CanonicalComparer))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!File.Exists(file))
                        continue;
                    if (!seenCharts.Add(file))
                        continue;
                    var fileInfo = new FileInfo(file);
                    inventory.Charts.Add(new ChartInventoryEntry(
                        file,
                        fileInfo.LastWriteTimeUtc,
                        fileInfo.CreationTimeUtc));
                }

                foreach (var subdirectory in EnumerateDirectoriesCore(directoryPath)
                    .Select(SongPathIdentity.Normalize)
                    .OrderBy(path => path, SongPathIdentity.CanonicalComparer))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ScanChartInventoryDirectory(
                        subdirectory, inventory, seenCharts, cancellationToken);
                }
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (IOException)
            {
            }
        }

        /// <summary>
        /// Resolves the chart file paths a <c>set.def</c> references (and that
        /// exist on disk, are supported, and remain within the directory
        /// containing the set.def), mirroring
        /// <see cref="ParseSetDefinitionIntoBatchAsync"/>. Reuses the shared
        /// <see cref="SongDefinitionReader.ReadSetDefLines"/> encoding chain and
        /// <see cref="SongDefinitionReader.ParseSetDefContent"/> so the inventory never drifts from
        /// what full enumeration imports. References that escape the set.def's
        /// directory via <c>../</c> traversal or absolute paths are rejected so
        /// the inventory count matches the database freshness count and no
        /// duplicate placeholders are created.
        /// </summary>
        private IEnumerable<string> EnumerateSetDefReferencedCharts(
            string setDefPath,
            CancellationToken cancellationToken)
        {
            var directory = Path.GetDirectoryName(setDefPath) ?? "";
            var normalizedSetDefDirectory = SongPathIdentity.Normalize(directory);
            var lines = _definitions.ReadSetDefLines(setDefPath);
            if (lines == null)
                yield break;

            var (_, difficulties) = _definitions.ParseSetDefContent(lines, cancellationToken);
            var emitted = new HashSet<string>(SongPathIdentity.CanonicalComparer);
            foreach (var (_, fileName) in difficulties.Values)
            {
                if (string.IsNullOrWhiteSpace(fileName))
                    continue;
                if (Path.IsPathRooted(fileName))
                    continue;
                var path = SongPathIdentity.Normalize(Path.Combine(directory, fileName));
                if (DTXChartParser.IsSupportedFile(path) &&
                    File.Exists(path) &&
                    SongPathIdentity.IsUnderNormalizedRoot(
                        path, normalizedSetDefDirectory) &&
                    emitted.Add(path))
                {
                    yield return path;
                }
            }
        }

        /// <summary>
        /// Returns true if any directory, set.def, or chart in
        /// <paramref name="inventory"/> was created or modified after the
        /// watermark for its containing root. Mirrors the per-entry checks
        /// previously spread across multiple recursive checks, but evaluated over
        /// a single inventory walk instead of one recursive walk per extension.
        /// </summary>
        private static bool InventoryHasChangesSince(
            ChartInventory inventory,
            IReadOnlyList<string> activeRoots,
            IReadOnlyDictionary<string, DateTime> rootWatermarks)
        {
            foreach (var root in activeRoots)
            {
                if (!rootWatermarks.ContainsKey(root))
                {
                    return true;
                }
            }

            foreach (var dir in inventory.Directories)
            {
                if (TryGetRootWatermark(
                        dir.Path,
                        activeRoots,
                        rootWatermarks,
                        out var watermark) &&
                    dir.LastWriteTimeUtc > watermark)
                {
                    return true;
                }
            }

            foreach (var setDef in inventory.SetDefinitions)
            {
                if (TryGetRootWatermark(
                        setDef.Path,
                        activeRoots,
                        rootWatermarks,
                        out var watermark) &&
                    (setDef.CreationTimeUtc > watermark ||
                     setDef.LastWriteTimeUtc > watermark))
                {
                    return true;
                }
            }

            foreach (var chart in inventory.Charts)
            {
                if (TryGetRootWatermark(
                        chart.Path,
                        activeRoots,
                        rootWatermarks,
                        out var watermark) &&
                    (chart.CreationTimeUtc > watermark ||
                     chart.LastWriteTimeUtc > watermark))
                {
                    var reason = chart.CreationTimeUtc > watermark ? "new" : "modified";
                    return true;
                }
            }

            return false;
        }

        private static bool TryGetRootWatermark(
            string path,
            IReadOnlyList<string> activeRoots,
            IReadOnlyDictionary<string, DateTime> rootWatermarks,
            out DateTime watermark)
        {
            watermark = default;
            var containingRoot = FindContainingRoot(path, activeRoots);
            return containingRoot != null &&
                rootWatermarks.TryGetValue(containingRoot, out watermark);
        }

        /// <summary>
        /// Finds the most specific (longest) normalized active root that
        /// contains the supplied normalized path, or <c>null</c> if the path is
        /// not beneath any root. Used to map a recoverable enumeration error
        /// (file parse failure, set.def read failure) to the root whose
        /// watermark must NOT advance so the next freshness check retries it.
        /// </summary>
        private static string? FindContainingRoot(
            string normalizedPath,
            IReadOnlyList<string> normalizedRoots)
        {
            string? containingRoot = null;
            foreach (var root in normalizedRoots)
            {
                if (!SongPathIdentity.IsUnderNormalizedRoot(normalizedPath, root) ||
                    (containingRoot != null && root.Length <= containingRoot.Length))
                {
                    continue;
                }
                containingRoot = root;
            }
            return containingRoot;
        }

        /// <summary>
        /// Gets the timestamp of the last successful enumeration from the database.
        /// <para>
        /// The timestamp is an explicitly persisted
        /// <c>LastSuccessfulEnumerationUtc</c> metadata value, NOT the database
        /// file's last-write time. The file mtime advances on every
        /// <c>SaveChangesAsync</c> (bookmark toggle, score save), so deriving the
        /// enumeration time from it would let an unrelated write mask later chart
        /// edits when the file count is unchanged. A null result means no
        /// enumeration has been recorded yet and forces a first-time scan.
        /// </para>
        /// <para>
        /// Returns UTC directly. The inventory walker uses
        /// <c>LastWriteTimeUtc</c>/<c>CreationTimeUtc</c>, so the comparison is
        /// UTC-vs-UTC — no local-time conversion that would break under DST or
        /// timezone shifts.
        /// </para>
        /// </summary>
        private async Task<DateTime?> GetLastEnumerationTimestampAsync()
        {
            try
            {
                var timestamp = await _catalog.GetLastSuccessfulEnumerationUtcAsync();
                if (timestamp == null)
                {
                    return null;
                }

                return timestamp;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error getting last enumeration timestamp");
                return null;
            }
        }

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
                var containingRoot = FindContainingRoot(
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
                var root = FindContainingRoot(path, batch.ActiveRoots);
                if (root != null)
                    counts[root]++;
            }

            await _catalog.SetRejectedChartCountsAsync(counts).ConfigureAwait(false);
        }

        private async Task<int> GetRejectedChartCountAsync(IReadOnlyList<string> roots)
        {
            return await _catalog.GetRejectedChartCountAsync(roots).ConfigureAwait(false);
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
