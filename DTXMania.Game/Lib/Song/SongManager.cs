#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Song.Entities;
using Microsoft.EntityFrameworkCore;
using SongEntity = DTXMania.Game.Lib.Song.Entities.Song;
using SongScoreEntity = DTXMania.Game.Lib.Song.Entities.SongScore;

namespace DTXMania.Game.Lib.Song
{
    /// <summary>
    /// Song database management and enumeration (Singleton)
    /// Based on DTXManiaNX CSongManager patterns
    /// Responsible for centralized song data management and enumeration
    /// </summary>
    public sealed class SongManager
    {
        #region Singleton Implementation

        private static SongManager _instance;
        private static readonly object _instanceLock = new();

        /// <summary>
        /// Gets the singleton instance of SongManager
        /// </summary>
        public static SongManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_instanceLock)
                    {
                        if (_instance == null)
                        {
                            _instance = new SongManager();
                        }
                    }
                }
                return _instance;
            }
        }

        /// <summary>
        /// Private constructor for singleton pattern
        /// </summary>
        private SongManager()
        {
        }

        #endregion

        #region Private Fields

        private readonly List<SongListNode> _rootSongs = new();
        private readonly object _lockObject = new();
        private CancellationTokenSource? _enumCancellation;
        private SongDatabaseService? _databaseService;
        private string[] _currentSearchPaths = Array.Empty<string>();

        internal Func<string, Task<(SongEntity song, SongChart chart)>>
            ParseSongEntitiesCoreAsync { get; set; } =
                DTXChartParser.ParseSongEntitiesAsync;

        internal Func<string, IEnumerable<string>> EnumerateFilesCore
            { get; set; } = Directory.EnumerateFiles;

        internal Func<string, IEnumerable<string>> EnumerateDirectoriesCore
            { get; set; } = Directory.EnumerateDirectories;

        internal Func<string, Encoding, CancellationToken, Task<string[]>>
            ReadAllLinesCoreAsync { get; set; } =
                static (path, encoding, token) =>
                    File.ReadAllLinesAsync(path, encoding, token);

        internal Func<
            SongDatabaseService,
            SongBulkImportRequest,
            IProgress<SongBulkImportProgress>?,
            CancellationToken,
            Task<SongBulkImportResult>> ImportSongsCoreAsync
            { get; set; } = DefaultImportSongsAsync;

        internal Func<SongDatabaseService, Task<DatabaseStats?>>
            GetDatabaseStatsCoreAsync { get; set; } =
                static database => database.GetDatabaseStatsAsync();

        // Initialization state tracking
        private bool _isInitialized = false;

        // Compiled regex patterns for SET.def normalization (performance optimization)
        private static readonly Regex NullBytePattern = new Regex(@"\u0000", RegexOptions.Compiled);
        private static readonly Regex BomPattern = new Regex(@"[\uFEFF\u200B]+", RegexOptions.Compiled);
        private static readonly Regex HashSpacePattern = new Regex(@"#\s+([A-Z]+)\s+(.*)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex SpacedCommandPattern = new Regex(@"#\s*([A-Z\s]+?)\s+(.*)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex ExcessiveSpacesPattern = new Regex(@"\s+", RegexOptions.Compiled);

        #endregion

        #region Public Properties

        /// <summary>
        /// Gets whether the SongManager has been initialized with song data
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
        /// Gets the database service instance. Returns null if not initialized.
        /// </summary>
        public SongDatabaseService? DatabaseService
        {
            get
            {
                lock (_lockObject)
                {
                    return _databaseService;
                }
            }
        }

        private SongDatabaseService? GetDatabaseServiceSnapshot()
        {
            lock (_lockObject)
            {
                return _databaseService;
            }
        }

        private void SetCurrentSearchPaths(string[] searchPaths)
        {
            if (searchPaths == null || searchPaths.Length == 0)
                return;

            _currentSearchPaths = searchPaths
                .Where(path => SongPathIdentity.TryNormalize(path, out _))
                .Select(SongPathIdentity.Normalize)
                .Distinct(SongPathIdentity.CanonicalComparer)
                .ToArray();
        }

        private static Task<SongBulkImportResult> DefaultImportSongsAsync(
            SongDatabaseService database,
            SongBulkImportRequest request,
            IProgress<SongBulkImportProgress>? progress,
            CancellationToken cancellationToken) =>
            database.ImportSongsAsync(request, progress, cancellationToken);

        /// <summary>
        /// Gets the root song list
        /// </summary>
        public IReadOnlyList<SongListNode> RootSongs
        {
            get
            {
                lock (_lockObject)
                {
                    return _rootSongs.AsReadOnly();
                }
            }
        }

        /// <summary>
        /// Number of scores in the database
        /// </summary>
        public async Task<int> GetDatabaseScoreCountAsync()
        {
            // Copy reference under lock to avoid race with Clear()
            SongDatabaseService? dbService;
            lock (_lockObject)
            {
                dbService = _databaseService;
            }
            
            if (dbService == null) return 0;
            try
            {
                var stats = await dbService.GetDatabaseStatsAsync();
                return stats.ScoreCount;
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

        /// <summary>
        /// Whether enumeration is currently in progress
        /// </summary>
        public bool IsEnumerating
        {
            get
            {
                lock (_lockObject)
                {
                    var c = _enumCancellation;
                    return c != null && !c.Token.IsCancellationRequested;
                }
            }
        }

        #endregion

        #region Events

        /// <summary>
        /// Fired when a song is discovered during enumeration
        /// </summary>
        public event EventHandler<SongDiscoveredEventArgs>? SongDiscovered;

        /// <summary>
        /// Fired when enumeration completes
        /// </summary>
        public event EventHandler? EnumerationCompleted;

        #endregion

        #region Individual Phase Operations

        /// <summary>
        /// Initializes the database service (SongListDB and SongsDB phases)
        /// </summary>
        public async Task<bool> InitializeDatabaseServiceAsync(string? databasePath = null, bool purgeDatabaseFirst = false)
        {
            return await InitializeDatabaseServiceAsync(
                databasePath,
                purgeDatabaseFirst,
                observer: null).ConfigureAwait(false);
        }

        internal async Task<bool> InitializeDatabaseServiceAsync(
            string? databasePath,
            bool purgeDatabaseFirst,
            IStartupSongLoadTimingObserver? observer)
        {
            try
            {
                // Initialize and capture a stable reference under lock to avoid races with Clear()
                SongDatabaseService? db;
                lock (_lockObject)
                {
                        var resolvedDatabasePath = string.IsNullOrWhiteSpace(databasePath)
                            ? Utilities.AppPaths.GetSongsDatabasePath()
                            : databasePath;

                    if (_databaseService == null)
                    {
                        observer.TryBeginDatabaseSpan(
                            StartupDatabaseTimingSpan.ServiceSetup);
                        try
                        {
                            _databaseService =
                                new SongDatabaseService(resolvedDatabasePath);
                        }
                        finally
                        {
                            observer.TryEndDatabaseSpan(
                                StartupDatabaseTimingSpan.ServiceSetup);
                        }
                    }
                    db = _databaseService;
                }

                if (db == null)
                {
                    Debug.WriteLine("SongManager: Cannot initialize database service - service instance is null");
                    return false;
                }

                // Check for database corruption first
                bool isDatabaseCorrupted;
                observer.TryBeginDatabaseSpan(
                    StartupDatabaseTimingSpan.CorruptionProbe);
                try
                {
                    isDatabaseCorrupted = await IsDatabaseCorruptedAsync(db)
                        .ConfigureAwait(false);
                }
                finally
                {
                    observer.TryEndDatabaseSpan(
                        StartupDatabaseTimingSpan.CorruptionProbe);
                }

                // Purge the database only if explicitly requested OR if corruption is detected
                bool shouldPurge;
                if (purgeDatabaseFirst)
                {
                    Debug.WriteLine("SongManager: Purging existing database for fresh rebuild (explicitly requested)");
                    shouldPurge = true;
                }
                else if (isDatabaseCorrupted)
                {
                    Debug.WriteLine("SongManager: Database corruption detected, purging corrupted database");
                    shouldPurge = true;
                }
                else
                {
                    Debug.WriteLine("SongManager: Database appears healthy, proceeding with existing database");
                    shouldPurge = false;
                }

                if (shouldPurge)
                {
                    observer.TryBeginDatabaseSpan(
                        StartupDatabaseTimingSpan.InvalidRecovery);
                    try
                    {
                        await db.PurgeDatabaseAsync().ConfigureAwait(false);
                    }
                    finally
                    {
                        observer.TryEndDatabaseSpan(
                            StartupDatabaseTimingSpan.InvalidRecovery);
                    }
                }

                await db.InitializeDatabaseAsync(observer).ConfigureAwait(false);
                Debug.WriteLine("SongManager: Database service initialized successfully");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SongManager: Error during database service initialization: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Loads existing songs from database (LoadScoreCache phase)
        /// </summary>
        public async Task<bool> LoadScoreCacheAsync(string[] searchPaths)
        {
            try
            {
                SetCurrentSearchPaths(searchPaths);
                var db = GetDatabaseServiceSnapshot();
                if (db == null)
                {
                    Debug.WriteLine("SongManager: Cannot load score cache - database service not initialized");
                    return false;
                }

                // Check if we need to enumerate or can build from database
                bool needsEnumeration = await GetDatabaseScoreCountAsync().ConfigureAwait(false) == 0 || await NeedsEnumerationAsync(searchPaths).ConfigureAwait(false);

                if (!needsEnumeration)
                {
                    Debug.WriteLine("SongManager: Building song list from database cache");
                    await BuildHierarchyFromDatabaseOnceAsync(searchPaths).ConfigureAwait(false);
                    return true;
                }
                else
                {
                    Debug.WriteLine("SongManager: Score cache is empty or outdated, enumeration needed");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SongManager: Error during score cache loading: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Enumerates songs from file system (EnumerateSongs phase)
        /// </summary>
        public async Task<int> EnumerateSongsOnlyAsync(string[] searchPaths, IProgress<EnumerationProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            var result = await EnumerateSongsOnlyWithPublicationAsync(
                searchPaths,
                progress,
                cancellationToken).ConfigureAwait(false);
            return result.SongCount;
        }

        internal async Task<(int SongCount, bool Published)>
            EnumerateSongsOnlyWithPublicationAsync(
                string[] searchPaths,
                IProgress<EnumerationProgress>? progress = null,
                CancellationToken cancellationToken = default)
        {
            if (GetDatabaseServiceSnapshot() == null)
                return (0, false);

            lock (_lockObject)
            {
                if (_enumCancellation is { IsCancellationRequested: false })
                    return (0, false);
            }

            var result = await EnumerateAndImportSongsAsync(
                searchPaths,
                progress,
                cancellationToken).ConfigureAwait(false);
            await UpdateEnumerationTimestampAsync().ConfigureAwait(false);

            return (result.Batch.Candidates.Count, true);
        }

        /// <summary>
        /// Builds final song lists from enumerated data (BuildSongLists phase)
        /// </summary>
        public async Task<bool> BuildSongListsAsync()
        {
            try
            {
                lock (_lockObject)
                {
                    if (_rootSongs.Count == 0)
                    {
                        Debug.WriteLine("SongManager: No songs to build lists from");
                        return false;
                    }
                }

                Debug.WriteLine($"SongManager: Building song lists complete. {_rootSongs.Count} root nodes organized");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SongManager: Error during song list building: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Saves song data to database (SaveSongsDB phase)
        /// </summary>
        public async Task<bool> SaveSongsDBAsync()
        {
            try
            {
                if (_databaseService == null)
                {
                    Debug.WriteLine("SongManager: Cannot save songs DB - database service not initialized");
                    return false;
                }

                // Enumeration persists transactionally. Keep the legacy save phase as
                // a lightweight availability confirmation without aggregate statistics.
                return await _databaseService.DatabaseExistsAsync()
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SongManager: Error during songs DB save: {ex.Message}");
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
            Debug.WriteLine("SongManager: Marked as initialized");
        }

        /// <summary>
        /// Public compatibility wrapper for the cleanup-free database hierarchy builder.
        /// </summary>
        public async Task BuildSongListFromDatabasePublicAsync(string[] searchPaths)
        {
            SetCurrentSearchPaths(searchPaths);

            await BuildHierarchyFromDatabaseOnceAsync(searchPaths).ConfigureAwait(false);
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
                Debug.WriteLine("SongManager: RefreshSongListFromDatabaseAsync skipped — no current search paths.");
            }
        }

        #endregion

        #region Initialization and Database Management


        /// <summary>
        /// Checks if the database exists and is accessible
        /// </summary>
        public async Task<bool> DatabaseExistsAsync()
        {
            var db = GetDatabaseServiceSnapshot();
            if (db == null) return false;

            try
            {
                return await db.DatabaseExistsAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SongManager: Error checking database existence: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Checks if the database is corrupted and needs to be rebuilt
        /// Returns true if the database is corrupted or inaccessible
        /// </summary>
        public async Task<bool> IsDatabaseCorruptedAsync()
        {
            var db = GetDatabaseServiceSnapshot();
            if (db == null) return false;

            return await IsDatabaseCorruptedAsync(db).ConfigureAwait(false);
        }

        private async Task<bool> IsDatabaseCorruptedAsync(SongDatabaseService db)
        {

            try
            {
                // Check if we can connect to the database
                if (!await db.DatabaseExistsAsync().ConfigureAwait(false))
                    return false; // Database doesn't exist, not corrupted

                // Try to get basic stats to verify database integrity
                var stats = await db.GetDatabaseStatsAsync().ConfigureAwait(false);
                if (stats == null)
                    return true; // Can't get stats, likely corrupted

                return false; // Database is accessible and functional
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SongManager: Database corruption check failed: {ex.Message}");
                // If we can't determine corruption state, assume it's corrupted to be safe
                return true;
            }
        }

        /// <summary>
        /// Gets database statistics
        /// </summary>
        public async Task<DatabaseStats?> GetDatabaseStatsAsync()
        {
            var db = GetDatabaseServiceSnapshot();
            if (db == null) return null;

            try
            {
                return await GetDatabaseStatsCoreAsync(db).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SongManager: Error getting database stats: {ex.Message}");
                return null;
            }
        }

        #endregion

        #region Song Enumeration

        /// <summary>
        /// Enumerates songs from specified search paths
        /// Should primarily be called during initialization
        /// </summary>
        public async Task<int> EnumerateSongsAsync(string[] searchPaths, IProgress<EnumerationProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            if (GetDatabaseServiceSnapshot() == null)
                return 0;

            lock (_lockObject)
            {
                if (_enumCancellation is { IsCancellationRequested: false })
                    return 0;
            }

            var result = await EnumerateAndImportSongsAsync(
                searchPaths,
                progress,
                cancellationToken).ConfigureAwait(false);
            return result.Batch.Candidates.Count;
        }

        internal async Task<SongEnumerationBatch> BuildEnumerationBatchAsync(
            string[] searchPaths,
            IProgress<EnumerationProgress>? progress,
            CancellationToken cancellationToken)
        {
            var builder = CreateBatchBuilder(searchPaths);
            foreach (var root in builder.ActiveRoots)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await EnumerateDirectoryIntoBatchAsync(
                    root,
                    parent: null,
                    builder,
                    progress,
                    cancellationToken).ConfigureAwait(false);
            }

            return builder.Complete();
        }

        public Task<SongEnumerationResult> EnumerateAndImportSongsAsync(
            string[] searchPaths,
            IProgress<EnumerationProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            return EnumerateAndImportSongsAsync(
                searchPaths,
                progress,
                cancellationToken,
                observer: null);
        }

        internal Task<SongEnumerationResult> EnumerateAndImportSongsAsync(
            string[] searchPaths,
            IProgress<EnumerationProgress>? progress,
            CancellationToken cancellationToken,
            IStartupSongLoadTimingObserver? observer)
        {
            return EnumerateAndImportSongsCoreAsync(
                searchPaths,
                progress,
                cancellationToken,
                observer);
        }

        private async Task<SongEnumerationResult> EnumerateAndImportSongsCoreAsync(
            string[] searchPaths,
            IProgress<EnumerationProgress>? progress,
            CancellationToken cancellationToken,
            IStartupSongLoadTimingObserver? observer)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var database = GetDatabaseServiceSnapshot()
                ?? throw new InvalidOperationException(
                    "Song database is not initialized.");
            var linked = BeginEnumeration(cancellationToken);
            SongEnumerationResult? result = null;
            var outcome = StartupOperationOutcome.Failure;
            try
            {
                var batch = await BuildEnumerationBatchAsync(
                    searchPaths,
                    progress,
                    linked.Token).ConfigureAwait(false);
                if (!batch.IsComplete)
                {
                    throw new InvalidOperationException(
                        "An incomplete enumeration cannot be imported.");
                }

                var import = await ImportSongsCoreAsync(
                    database,
                    new SongBulkImportRequest(
                        batch.ActiveRoots,
                        batch.DiscoveredChartPaths,
                        batch.Candidates),
                    CreatePersistenceProgressAdapter(progress),
                    linked.Token).ConfigureAwait(false);

                var hierarchy = Stopwatch.StartNew();
                FinalizePendingNodes(batch, import.ChartsByPath);
                hierarchy.Stop();
                PublishEnumeration(batch);

                result = new SongEnumerationResult(
                    batch,
                    import,
                    hierarchy.Elapsed);
                outcome = StartupOperationOutcome.Success;
                return result;
            }
            catch (OperationCanceledException)
            {
                outcome = StartupOperationOutcome.Cancellation;
                throw;
            }
            finally
            {
                EndEnumeration(linked);
                observer.TryRecordEnumerationTerminal(result, outcome);
            }
        }

        private CancellationTokenSource BeginEnumeration(CancellationToken token)
        {
            lock (_lockObject)
            {
                if (_enumCancellation is { IsCancellationRequested: false })
                {
                    throw new InvalidOperationException(
                        "Song enumeration is already in progress.");
                }

                _enumCancellation?.Dispose();
                _enumCancellation =
                    CancellationTokenSource.CreateLinkedTokenSource(token);
                return _enumCancellation;
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
            private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

            public SongEnumerationBatchBuilder(
                IReadOnlyList<string> activeRoots)
            {
                ActiveRoots = activeRoots;
            }

            public IReadOnlyList<string> ActiveRoots { get; }
            public HashSet<string> DiscoveredChartPaths { get; } =
                new(SongPathIdentity.CanonicalComparer);
            public List<SongImportCandidate> Candidates { get; } = new();
            public List<SongListNode> RootNodes { get; } = new();
            public List<PendingSongNode> PendingSongs { get; } = new();
            public List<SongEnumerationError> Errors { get; } = new();

            public SongEnumerationBatch Complete()
            {
                _stopwatch.Stop();
                return new SongEnumerationBatch
                {
                    ActiveRoots = ActiveRoots,
                    DiscoveredChartPaths = DiscoveredChartPaths,
                    Candidates = Candidates,
                    RootNodes = RootNodes,
                    PendingSongs = PendingSongs,
                    Errors = Errors,
                    DiscoveryAndParsingDuration = _stopwatch.Elapsed,
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

        private static SongEnumerationBatchBuilder CreateBatchBuilder(
            IEnumerable<string> searchPaths)
        {
            ArgumentNullException.ThrowIfNull(searchPaths);
            var roots = searchPaths.Select(path =>
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    throw new DirectoryNotFoundException(
                        "A configured song root is blank.");
                }

                var normalized = SongPathIdentity.Normalize(path);
                if (!Directory.Exists(normalized))
                {
                    throw new DirectoryNotFoundException(
                        $"Configured song root does not exist: {normalized}");
                }

                return normalized;
            })
            .Distinct(SongPathIdentity.CanonicalComparer)
            .ToArray();
            return new SongEnumerationBatchBuilder(roots);
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
                        boxDefinition = await ParseBoxDefinitionAsync(
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

                var boxNode = CreateBoxNodeFromDirectory(
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
                lines = await ReadAllLinesCoreAsync(
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
                (setTitle, difficulties) = ParseSetDefContent(
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

                var path = SongPathIdentity.Normalize(
                    Path.Combine(directory, fileName));
                if (!DTXChartParser.IsSupportedFile(path) ||
                    !File.Exists(path))
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
                GetPrimaryInstrumentAndDifficulty(chart);
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

        private static (
            EInstrumentPart instrument,
            int difficultyLevel) GetPrimaryInstrumentAndDifficulty(
                SongChart chart)
        {
            if (chart.HasDrumChart && chart.DrumLevel > 0)
                return (EInstrumentPart.DRUMS, chart.DrumLevel);
            if (chart.HasGuitarChart && chart.GuitarLevel > 0)
                return (EInstrumentPart.GUITAR, chart.GuitarLevel);
            if (chart.HasBassChart && chart.BassLevel > 0)
                return (EInstrumentPart.BASS, chart.BassLevel);
            return (EInstrumentPart.DRUMS, 50);
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

        internal void FinalizePendingNodes(
            SongEnumerationBatch batch,
            IReadOnlyDictionary<string, SongChart> chartsByPath)
        {
            foreach (var pending in batch.PendingSongs)
            {
                var placeholder = pending.Placeholder;
                var resolvedCharts = new List<SongChart>();
                for (var index = 0;
                    index < pending.OrderedChartPaths.Count &&
                    index < placeholder.Scores.Length;
                    index++)
                {
                    var path = pending.OrderedChartPaths[index];
                    if (!chartsByPath.TryGetValue(path, out var chart))
                    {
                        placeholder.Scores[index] = null!;
                        continue;
                    }

                    var (instrument, difficultyLevel) =
                        GetPrimaryInstrumentAndDifficulty(chart);
                    var label = placeholder.DifficultyLabels[index];
                    if (string.IsNullOrWhiteSpace(label))
                    {
                        label = ResolveDifficultyLabel(
                            chart,
                            new Dictionary<string, string>(),
                            index);
                        placeholder.DifficultyLabels[index] = label;
                    }

                    placeholder.SetScore(index, new SongScoreEntity
                    {
                        ChartId = chart.Id,
                        Instrument = instrument,
                        DifficultyLevel = difficultyLevel,
                        DifficultyLabel = label
                    });
                    resolvedCharts.Add(chart);
                    if (placeholder.DatabaseSong == null ||
                        placeholder.DatabaseSongId == null)
                    {
                        placeholder.DatabaseSong = chart.Song;
                        placeholder.DatabaseChart = chart;
                        placeholder.DatabaseSongId = chart.SongId;
                    }
                }

                if (resolvedCharts.Count == 0)
                {
                    RemovePlaceholder(batch, placeholder);
                    continue;
                }

                placeholder.DatabaseSong = resolvedCharts[0].Song;
                placeholder.DatabaseChart = resolvedCharts[0];
                placeholder.DatabaseSongId = resolvedCharts[0].SongId;
                HydrateScoreVariants(placeholder, resolvedCharts);
            }
        }

        private static void RemovePlaceholder(
            SongEnumerationBatch batch,
            SongListNode placeholder)
        {
            if (placeholder.Parent != null)
            {
                placeholder.Parent.RemoveChild(placeholder);
                return;
            }

            batch.RootNodes.Remove(placeholder);
        }

        internal void PublishEnumeration(SongEnumerationBatch batch)
        {
            lock (_lockObject)
            {
                _rootSongs.Clear();
                _rootSongs.AddRange(batch.RootNodes);
                _currentSearchPaths = batch.ActiveRoots.ToArray();
                EnumeratedFileCount = batch.DiscoveredChartPaths.Count;
                DiscoveredScoreCount = batch.PendingSongs.Count;
            }

            foreach (var song in FlattenScoreNodes(batch.RootNodes))
                SongDiscovered?.Invoke(this, new SongDiscoveredEventArgs(song));
            EnumerationCompleted?.Invoke(this, EventArgs.Empty);
        }

        private static IEnumerable<SongListNode> FlattenScoreNodes(
            IEnumerable<SongListNode> nodes)
        {
            foreach (var node in nodes)
            {
                if (node.Type == NodeType.Score)
                    yield return node;
                foreach (var child in FlattenScoreNodes(node.Children))
                    yield return child;
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
            var db = GetDatabaseServiceSnapshot();
            if (db == null)
            {
                Debug.WriteLine("SongManager: Cannot build song list - database service not initialized");
                return;
            }

            try
            {
                var newRootNodes = new List<SongListNode>();
                var allSongs = await db.GetSongsAsync().ConfigureAwait(false);

                foreach (var searchPath in searchPaths)
                {
                    if (string.IsNullOrEmpty(searchPath) || !Directory.Exists(searchPath))
                    {
                        Debug.WriteLine($"SongManager: Skipping invalid path during database rebuild: {searchPath}");
                        continue;
                    }

                    // Get all charts that belong to this search path
                    var relevantCharts = allSongs
                        .SelectMany(song => song.Charts
                            .Where(chart =>
                                SongPathIdentity.TryNormalize(
                                    chart.FilePath,
                                    out var normalized) &&
                                SongPathIdentity.IsUnderRoot(
                                    normalized,
                                    searchPath))
                            .Select(chart => (song, chart)))
                        .ToList();

                    // Build the folder hierarchy structure from file paths
                    var pathNodes = await BuildHierarchyFromCharts(searchPath, relevantCharts);
                    newRootNodes.AddRange(pathNodes);
                }

                // Update root songs list
                lock (_lockObject)
                {
                    _rootSongs.Clear();
                    _rootSongs.AddRange(newRootNodes);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SongManager: Error building song list from database: {ex.Message}");
            }
        }

        /// <summary>
        /// Builds hierarchical folder structure from chart file paths
        /// Recreates the original folder structure based on file paths
        /// </summary>
        private async Task<List<SongListNode>> BuildHierarchyFromCharts(string searchPath, List<(SongEntity song, SongChart chart)> charts)
        {
            var rootNodes = new List<SongListNode>();
            var folderNodeCache = new Dictionary<string, SongListNode>(StringComparer.OrdinalIgnoreCase);

            // Persisted Song.Id is the grouping authority. Title/artist are display
            // metadata and can legitimately collide across different directories.
            var songGroups = charts
                .GroupBy(item => item.song.Id)
                .ToList();

            foreach (var songGroup in songGroups)
            {
                var firstChart = songGroup.First().chart;
                var firstSong = songGroup.First().song;
                var allCharts = songGroup.Select(item => item.chart).ToArray();

                if (string.IsNullOrEmpty(firstChart.FilePath))
                    continue;

                // Check if the file still exists at the recorded path
                if (!File.Exists(firstChart.FilePath))
                {
                    Debug.WriteLine($"SongManager: Skipping song '{firstSong.Title}' - file no longer exists at {firstChart.FilePath}");
                    continue;
                }

                // Get the directory path relative to the search path
                var fullDirectoryPath = Path.GetDirectoryName(firstChart.FilePath);
                if (string.IsNullOrEmpty(fullDirectoryPath))
                    continue;

                // Create folder hierarchy for this song
                var parentNode = await EnsureFolderHierarchy(searchPath, fullDirectoryPath, folderNodeCache, rootNodes);

                // Create the song node
                var songNode = CreateSongNodeFromDatabaseEntities(firstSong, allCharts);
                if (songNode != null)
                {
                    if (parentNode != null)
                    {
                        parentNode.AddChild(songNode);
                        songNode.Parent = parentNode;
                    }
                    else
                    {
                        // Song is directly in the search path root
                        rootNodes.Add(songNode);
                    }
                }
            }

            return rootNodes;
        }

        /// <summary>
        /// Ensures folder hierarchy exists for a given path
        /// Returns the deepest folder node for the path
        /// </summary>
        private async Task<SongListNode?> EnsureFolderHierarchy(string searchPath, string fullPath, Dictionary<string, SongListNode> folderCache, List<SongListNode> rootNodes)
        {
            if (string.IsNullOrEmpty(fullPath) || fullPath.Equals(searchPath, StringComparison.OrdinalIgnoreCase))
                return null;

            // Check cache first
            if (folderCache.TryGetValue(fullPath, out var cachedNode))
                return cachedNode;

            var parentPath = Path.GetDirectoryName(fullPath);
            var folderName = Path.GetFileName(fullPath);

            if (string.IsNullOrEmpty(folderName))
                return null;

            // Recursively ensure parent folder exists (only if parentPath is not null/empty)
            SongListNode? parentNode = null;
            if (!string.IsNullOrEmpty(parentPath))
            {
                parentNode = await EnsureFolderHierarchy(searchPath, parentPath, folderCache, rootNodes);
            }

            // Check if this is a DTXFiles.* prefixed folder or has box.def
            var isDTXFilesFolder = folderName.StartsWith("DTXFiles.", StringComparison.OrdinalIgnoreCase);
            var boxDefPath = Path.Combine(fullPath, "box.def");
            var hasBoxDef = File.Exists(boxDefPath);

            // Create folder node if it's a BOX folder
            if (isDTXFilesFolder || hasBoxDef)
            {
                BoxDefinition? boxDef = null;
                if (hasBoxDef)
                {
                    try
                    {
                        boxDef = await ParseBoxDefinitionAsync(boxDefPath, CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"SongManager: Error parsing box.def at {boxDefPath}: {ex.Message}");
                    }
                }

                var folderNode = SongListNode.CreateBoxNode(
                    boxDef?.Title ?? folderName,
                    fullPath,
                    parentNode
                );

                if (boxDef != null)
                {
                    folderNode.Genre = boxDef.Genre ?? "";
                    folderNode.SkinPath = boxDef.SkinPath;

                    // Convert System.Drawing.Color to Microsoft.Xna.Framework.Color
                    if (boxDef.TextColor != System.Drawing.Color.Empty)
                    {
                        folderNode.TextColor = new Microsoft.Xna.Framework.Color(
                            boxDef.TextColor.R,
                            boxDef.TextColor.G,
                            boxDef.TextColor.B,
                            boxDef.TextColor.A
                        );
                    }
                }

                // Add to parent or root
                if (parentNode != null)
                {
                    parentNode.AddChild(folderNode);
                }
                else
                {
                    // This is a top-level folder, add to root nodes
                    rootNodes.Add(folderNode);
                }

                folderCache[fullPath] = folderNode;
                return folderNode;
            }

            // If not a BOX folder, treat as part of the path but don't create a folder node
            return parentNode;
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Enumerates a directory recursively
        /// </summary>
        private async Task<List<SongListNode>> EnumerateDirectoryAsync(
            string directoryPath,
            SongListNode? parent,
            IProgress<EnumerationProgress>? progress,
            CancellationToken cancellationToken)
        {
            var results = new List<SongListNode>();

            try
            {
                // Check for set.def (multi-difficulty songs)
                var setDefPath = Path.Combine(directoryPath, "set.def");
                if (File.Exists(setDefPath))
                {
                    var setDefSongs = await ParseSetDefinitionAsync(setDefPath, parent, cancellationToken);
                    results.AddRange(setDefSongs);

                    // If set.def exists, don't process individual files in this directory
                    return results;
                }

                // Process subdirectories - distinguish between BOX folders and song folders
                // Use async enumeration to avoid blocking the thread
                var subdirectoryPaths = await Task.Run(() => 
                    Directory.EnumerateDirectories(directoryPath).ToList(), cancellationToken);

                foreach (var subdirPath in subdirectoryPaths)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var subDirInfo = new DirectoryInfo(subdirPath);
                    
                    // Check if this is a DTXFiles.* prefixed folder (should be NodeType.Box)
                    var isDTXFilesFolder = subDirInfo.Name.StartsWith("DTXFiles.", StringComparison.OrdinalIgnoreCase);

                    // Check for box.def in the subdirectory itself
                    BoxDefinition? subDirBoxDef = null;
                    var subDirBoxDefPath = Path.Combine(subdirPath, "box.def");
                    var hasBoxDef = File.Exists(subDirBoxDefPath);
                    if (hasBoxDef)
                    {
                        subDirBoxDef = await ParseBoxDefinitionAsync(subDirBoxDefPath, cancellationToken);
                    }

                    // Determine if this should be a BOX (folder container) or treated as individual songs
                    if (isDTXFilesFolder || hasBoxDef)
                    {
                        // This is a BOX folder (DTXFiles.* prefix or has box.def)
                        Debug.WriteLine($"SongManager: Creating BOX node for {subDirInfo.Name}");
                        var boxNode = CreateBoxNodeFromDirectory(subDirInfo, parent, subDirBoxDef);
                        var children = await EnumerateDirectoryAsync(subdirPath, boxNode, progress, cancellationToken);

                        foreach (var child in children)
                        {
                            boxNode.AddChild(child);
                        }

                        if (boxNode.Children.Count > 0)
                        {
                            results.Add(boxNode);
                            Debug.WriteLine($"SongManager: Added BOX {subDirInfo.Name} with {boxNode.Children.Count} children");
                        }
                        else
                        {
                            Debug.WriteLine($"SongManager: Skipping empty BOX {subDirInfo.Name}");
                        }
                    }
                    else
                    {
                        // This is a regular song folder - treat contents as individual songs
                        var children = await EnumerateDirectoryAsync(subdirPath, parent, progress, cancellationToken);
                        results.AddRange(children);
                    }
                }

                // Process individual song files
                var tempSongNodes = new List<SongListNode>();
                Debug.WriteLine($"SongManager: Processing files in directory {directoryPath}");
                
                // Use async enumeration for files to avoid blocking
                var filePaths = await Task.Run(() => 
                    Directory.EnumerateFiles(directoryPath).ToList(), cancellationToken);

                foreach (var filePath in filePaths)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (DTXChartParser.IsSupportedFile(filePath))
                    {
                        var fileName = Path.GetFileName(filePath);
                        Debug.WriteLine($"SongManager: Creating song node for {fileName}");
                        var songNode = await CreateSongNodeAsync(filePath, parent);
                        if (songNode != null)
                        {
                            tempSongNodes.Add(songNode);
                            DiscoveredScoreCount++;
                            Debug.WriteLine($"SongManager: Successfully created song node for {songNode.Title}");

                            progress?.Report(new EnumerationProgress
                            {
                                CurrentFile = fileName,
                                ProcessedCount = ++EnumeratedFileCount,
                                DiscoveredSongs = DiscoveredScoreCount
                            });
                        }
                        else
                        {
                            Debug.WriteLine($"SongManager: Failed to create song node for {fileName}");
                        }
                    }
                    else
                    {
                        Debug.WriteLine($"SongManager: Skipping unsupported file {Path.GetFileName(filePath)}");
                    }
                }

                // This compatibility helper is no longer used by startup enumeration.
                // Keep its parse-only behavior for legacy reflection tests.
                var groupedSongs = tempSongNodes;
                results.AddRange(groupedSongs);

                // Fire SongDiscovered events for the final grouped songs
                foreach (var finalSongNode in groupedSongs)
                {
                    SongDiscovered?.Invoke(this, new SongDiscoveredEventArgs(finalSongNode));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SongManager: Error enumerating directory {directoryPath}: {ex.Message}");
            }

            return results;
        }

        /// <summary>
        /// Creates a song node from a file
        /// </summary>
        private async Task<SongListNode?> CreateSongNodeAsync(string filePath, SongListNode? parent)
        {
            try
            {
                var normalizedPath = SongPathIdentity.Normalize(filePath);
                if (!File.Exists(normalizedPath) ||
                    !DTXChartParser.IsSupportedFile(normalizedPath))
                {
                    return null;
                }

                var (song, chart) = await ParseSongEntitiesCoreAsync(
                    normalizedPath).ConfigureAwait(false);
                chart.FilePath = normalizedPath;
                return CreateTemporarySongNode(song, chart, parent);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SongManager: Error creating song node for {filePath}: {ex.Message}");
                return null;
            }
        }

        // Include all the other parsing methods from the original file
        /// <summary>
        /// Normalizes a SET.def line to handle corrupted/spaced formatting and UTF-16 encoding issues
        /// Optimized version using compiled regex patterns and StringBuilder for better performance
        /// </summary>
        private string NormalizeSetDefLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return "";
            
            try
            {
                // Step 1: Remove BOMs and null bytes using compiled regex (faster than multiple Replace calls)
                string processedLine = BomPattern.Replace(line, "");
                processedLine = NullBytePattern.Replace(processedLine, "");
                processedLine = processedLine.Trim();
                
                // Quick check: if it's already a proper command line, return it
                if (processedLine.StartsWith("#") && !processedLine.Contains("  ") && processedLine.Length > 1)
                {
                    return processedLine;
                }
                
                // Step 2: Handle spaced-out commands using compiled regex
                if (processedLine.Contains("#"))
                {
                    // Try simple spaced pattern first: "# TITLE My Song" -> "#TITLE My Song"
                    var simpleMatch = HashSpacePattern.Match(processedLine);
                    if (simpleMatch.Success)
                    {
                        var command = simpleMatch.Groups[1].Value.Replace(" ", "");
                        var value = simpleMatch.Groups[2].Value;
                        return $"#{command} {value}";
                    }
                    
                    // Handle complex spaced patterns: "# T I T L E My Song" 
                    var complexMatch = SpacedCommandPattern.Match(processedLine);
                    if (complexMatch.Success)
                    {
                        var spacedCommand = complexMatch.Groups[1].Value;
                        var value = complexMatch.Groups[2].Value;
                        
                        // Remove spaces from command part using compiled regex
                        var command = ExcessiveSpacesPattern.Replace(spacedCommand, "");
                        
                        // Handle special patterns efficiently
                        var upperCommand = command.ToUpperInvariant();
                        if (IsKnownCommand(upperCommand))
                        {
                            return $"#{upperCommand} {value}";
                        }
                        
                        // Handle L#LABEL and L#FILE patterns
                        if (upperCommand.Length > 1 && char.IsDigit(upperCommand[1]) &&
                            (upperCommand.EndsWith("LABEL") || upperCommand.EndsWith("FILE")))
                        {
                            return $"#{upperCommand} {value}";
                        }
                        
                        return $"#{command} {value}";
                    }
                    
                    // Fallback: try to reconstruct manually for very corrupted lines
                    return ReconstructCorruptedLine(processedLine);
                }
                
                return processedLine;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SongManager: Error normalizing SET.def line '{line}': {ex.Message}");
                return "";
            }
        }
        
        /// <summary>
        /// Fast lookup for known SET.def commands to avoid string processing
        /// </summary>
        private static bool IsKnownCommand(string command)
        {
            // Use switch expression for optimal performance (faster than HashSet for small sets)
            return command switch
            {
                "TITLE" or "L1LABEL" or "L2LABEL" or "L3LABEL" or "L4LABEL" or "L5LABEL" or
                "L1FILE" or "L2FILE" or "L3FILE" or "L4FILE" or "L5FILE" => true,
                _ => false
            };
        }
        
        /// <summary>
        /// Fallback method for heavily corrupted lines using StringBuilder
        /// </summary>
        private string ReconstructCorruptedLine(string line)
        {
            var hashIndex = line.IndexOf('#');
            if (hashIndex < 0) return line;
            
            var afterHash = line.Substring(hashIndex + 1).Trim();
            if (string.IsNullOrEmpty(afterHash)) return line;
            
            var parts = afterHash.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            
            if (parts.Length < 2) return line;
            
            // Build command efficiently
            var commandBuilder = new StringBuilder(parts.Length * 2);
            var valueBuilder = new StringBuilder(afterHash.Length);
            var commandComplete = false;
            
            foreach (var part in parts)
            {
                if (!commandComplete)
                {
                    commandBuilder.Append(part);
                    var accumulated = commandBuilder.ToString().ToUpperInvariant();
                    
                    // Check if we've completed a known command
                    if (IsKnownCommand(accumulated) || 
                        (accumulated.Length > 2 && accumulated.EndsWith("LABEL")) ||
                        (accumulated.Length > 2 && accumulated.EndsWith("FILE")))
                    {
                        commandComplete = true;
                        continue;
                    }
                }
                else
                {
                    if (valueBuilder.Length > 0) valueBuilder.Append(' ');
                    valueBuilder.Append(part);
                }
            }
            
            if (commandComplete && valueBuilder.Length > 0)
            {
                return $"#{commandBuilder} {valueBuilder}";
            }
            
            // Final fallback
            return $"#{parts[0]} {string.Join(" ", parts.Skip(1))}";
        }

        /// <summary>
        /// Builds the list of encodings used to read SET.def files. Shared by
        /// <see cref="ReadSetDefLines"/> (synchronous database-load path) and
        /// <see cref="ParseSetDefinitionAsync"/> (async enumeration path) so the two never drift.
        /// </summary>
        /// <remarks>
        /// NOTE: this is <b>not</b> a true encoding fallback chain as written today.
        /// <see cref="Encoding.UTF8"/> uses <see cref="DecoderReplacementFallback"/>, so
        /// <c>File.ReadAllLines(UTF-8)</c> never throws on bad bytes — it emits U+FFFD and
        /// "succeeds" on the first entry, and the subsequent system-default / Shift_JIS entries
        /// are never exercised for decoding reasons. (The <c>catch</c> below only catches I/O
        /// errors, which fail identically across the whole list.) Shift_JIS is retained so that a
        /// future switch to <c>new UTF8Encoding(false, throwOnInvalidBytes: true)</c> could enable a
        /// real fallback for legacy Japanese charts; it is added defensively because it needs a
        /// registered code-page provider on some runtimes. The actual repair of the
        /// BOM/null-byte/spaced artifacts produced by reading a Shift_JIS or UTF-16 SET.def as
        /// UTF-8 happens after the read, in <see cref="NormalizeSetDefLine"/>.
        /// </remarks>
        private static List<Encoding> BuildSetDefEncodings()
        {
            var encodings = new List<Encoding> { Encoding.UTF8, Encoding.Default };
            try
            {
                encodings.Add(Encoding.GetEncoding("Shift_JIS"));
            }
            catch (ArgumentException)
            {
                Debug.WriteLine("SongManager: Shift_JIS encoding not available for SET.def parsing");
            }
            return encodings;
        }

        /// <summary>
        /// Reads a SET.def file using <see cref="BuildSetDefEncodings"/>. Returns null only when the
        /// file cannot be read at all (it does not exist or an I/O error occurs). Because UTF-8 uses
        /// <see cref="DecoderReplacementFallback"/>, a read with invalid bytes never throws and the
        /// later encodings in the list are not retried — see the remarks on
        /// <see cref="BuildSetDefEncodings"/>. <see cref="NormalizeSetDefLine"/> repairs the
        /// BOM/null-byte/spaced artifacts that result from reading a Shift_JIS or UTF-16 SET.def as UTF-8.
        /// </summary>
        /// <remarks>
        /// Synchronous by design: the only caller is <see cref="GetSetDefLabelsByFile"/>, which
        /// runs on the synchronous database-load path. The async enumeration path reads via
        /// <see cref="File.ReadAllLinesAsync"/> in <see cref="ParseSetDefinitionAsync"/>.
        /// </remarks>
        private static string[]? ReadSetDefLines(string setDefPath)
        {
            foreach (var encoding in BuildSetDefEncodings())
            {
                try
                {
                    return File.ReadAllLines(setDefPath, encoding);
                }
                // Only realistic failures here are I/O (the path is pre-validated and UTF-8 uses
                // DecoderReplacementFallback, so encoding mismatches never throw). Let genuinely
                // unexpected exceptions (OOM, contract changes) propagate instead of being masked.
                catch (IOException ex)
                {
                    Debug.WriteLine($"SongManager: Failed to read SET.def with {encoding.EncodingName}: {ex.Message}");
                }
                catch (UnauthorizedAccessException ex)
                {
                    Debug.WriteLine($"SongManager: Failed to read SET.def with {encoding.EncodingName}: {ex.Message}");
                }
            }
            return null;
        }

        /// <summary>
        /// Parses the #TITLE and #Ln(LABEL|FILE) commands out of SET.def lines into the
        /// title plus a per-difficulty (label, file) map keyed by the L-slot number. Shared by
        /// enumeration (ParseSetDefinitionAsync) and the database-load path so the difficulty
        /// badge can recover the authentic difficulty label from the SET.def.
        /// </summary>
        private (string title, Dictionary<int, (string label, string file)> difficulties) ParseSetDefContent(
            string[] lines, CancellationToken cancellationToken)
        {
            string songTitle = "";
            var difficulties = new Dictionary<int, (string label, string file)>();

            foreach (var line in lines)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var trimmedLine = line.Trim();
                if (string.IsNullOrEmpty(trimmedLine) || trimmedLine.StartsWith("//"))
                    continue;

                // Parse SET.def commands with robust parsing for corrupted/spaced text
                if (trimmedLine.StartsWith("#") || trimmedLine.Contains("#"))
                {
                    // Handle both normal and spaced-out command formats
                    string normalizedLine = NormalizeSetDefLine(trimmedLine);

                    if (string.IsNullOrEmpty(normalizedLine)) continue;

                    var parts = normalizedLine.Split(new char[] { ' ', '\t' }, 2, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2)
                    {
                        var command = parts[0].Substring(1).ToUpperInvariant(); // Remove # and convert to uppercase
                        var value = parts[1].Trim();

                        if (command == "TITLE")
                        {
                            songTitle = value;
                        }
                        else if (command.StartsWith("L") &&
                                 (command.EndsWith("LABEL") || command.EndsWith("FILE")))
                        {
                            // Extract level number (L1LABEL -> 1, L1FILE -> 1).
                            // Guard the slice length: a malformed command such as "#LABEL"
                            // (length 5, LABEL suffix 6) would otherwise pass a negative length to
                            // Substring and crash enumeration with ArgumentOutOfRangeException.
                            int suffixLength = command.EndsWith("LABEL") ? 6 : 5;
                            int levelTokenLength = command.Length - suffixLength;
                            if (levelTokenLength > 0 &&
                                int.TryParse(command.Substring(1, levelTokenLength), out int level))
                            {
                                if (!difficulties.ContainsKey(level))
                                    difficulties[level] = ("", "");

                                if (command.EndsWith("LABEL"))
                                    difficulties[level] = (value, difficulties[level].file);
                                else
                                    difficulties[level] = (difficulties[level].label, value);
                            }
                        }
                    }
                }
            }

            return (songTitle, difficulties);
        }

        /// <summary>
        /// Reads the SET.def in <paramref name="directory"/> (if any) and returns a map of
        /// chart file name (e.g. "bas.dtx", case-insensitive) to its authentic #LnLABEL
        /// (e.g. "BASIC"). The database-load path uses this to recover difficulty-tier labels
        /// for the performance-stage difficulty badge when the persisted SongChart.DifficultyLabel
        /// is empty (legacy databases never stored it). Returns an empty map when there is no
        /// SET.def or it declares no labelled difficulties.
        /// </summary>
        internal Dictionary<string, string> GetSetDefLabelsByFile(string directory)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(directory))
                return result;

            // The chart referenced by #LnFILE may live in a subdirectory (e.g. "#L1FILE
            // charts/bas.dtx"), so the caller passes the chart's directory while set.def sits in
            // the song-folder root one level up. Walk up from the chart's directory until set.def
            // is found so subdirectory chart references resolve to the correct labels instead of
            // returning an empty map (which would make the badge fall back to "Level N"/DTX).
            var setDefPath = FindSetDefPath(directory);
            if (setDefPath == null)
                return result;

            var lines = ReadSetDefLines(setDefPath);
            if (lines == null)
                return result;

            var (_, difficulties) = ParseSetDefContent(lines, CancellationToken.None);
            foreach (var (label, file) in difficulties.Values)
            {
                if (!string.IsNullOrEmpty(file) && !string.IsNullOrEmpty(label))
                {
                    // Normalize to the bare file name. A legacy SET.def may reference a chart via a
                    // relative path (e.g. "#L1FILE charts/bas.dtx"), but ResolveDifficultyLabel looks
                    // up by Path.GetFileName(chart.FilePath) ("bas.dtx"). Storing the file name only
                    // keeps the producer and consumer keys consistent so subdirectory references also
                    // resolve; plain file names ("bas.dtx") are unaffected by GetFileName.
                    result[Path.GetFileName(file)] = label;
                }
            }
            return result;
        }

        /// <summary>
        /// Searches <paramref name="directory"/> and its parent directories (bounded) for a
        /// <c>set.def</c> file, returning the first match. This lets the database-load path
        /// recover difficulty labels for charts that live in a subdirectory of the song folder.
        /// Returns null when no set.def is found within the search depth.
        /// </summary>
        private static string? FindSetDefPath(string directory)
        {
            var current = directory;
            for (int depth = 0; depth < 4 && !string.IsNullOrEmpty(current); depth++)
            {
                var candidate = Path.Combine(current, "set.def");
                if (File.Exists(candidate))
                    return candidate;

                var parent = Path.GetDirectoryName(current);
                if (parent == null || parent == current)
                    break;
                current = parent;
            }
            return null;
        }

        private async Task<List<SongListNode>> ParseSetDefinitionAsync(string setDefPath, SongListNode? parent, CancellationToken cancellationToken)
        {
            var results = new List<SongListNode>();
            var directory = Path.GetDirectoryName(setDefPath) ?? "";

            try
            {
                // Read with the shared encoding list (see BuildSetDefEncodings for why this is not
                // a true fallback chain — UTF-8 always "wins" and repair happens in NormalizeSetDefLine).
                var encodings = BuildSetDefEncodings();

                string[]? lines = null;
                foreach (var encoding in encodings)
                {
                    try
                    {
                        lines = await File.ReadAllLinesAsync(setDefPath, encoding, cancellationToken);
                        break; // Success, use this encoding
                    }
                    // Only realistic failures here are I/O (UTF-8 uses DecoderReplacementFallback,
                    // so encoding mismatches never throw). Let genuinely unexpected exceptions
                    // propagate instead of being masked.
                    catch (IOException ex)
                    {
                        Debug.WriteLine($"SongManager: Failed to read SET.def with {encoding.EncodingName}: {ex.Message}");
                        continue;
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        Debug.WriteLine($"SongManager: Failed to read SET.def with {encoding.EncodingName}: {ex.Message}");
                        continue;
                    }
                }

                if (lines == null)
                {
                    Debug.WriteLine($"SongManager: Failed to read SET.def with any encoding: {setDefPath}");
                    return results;
                }

                SongListNode? currentSong = null;
                var (songTitle, difficulties) = ParseSetDefContent(lines, cancellationToken);

                // Store the SET.def title (may be empty if parsing failed)
                string setDefTitle = songTitle;

                // Create song node if we have difficulties (title will be determined later)
                if (difficulties.Count > 0)
                {
                    // Parse the first valid DTX file to get real metadata
                    DTXMania.Game.Lib.Song.Entities.Song? primarySong = null;
                    DTXMania.Game.Lib.Song.Entities.SongChart? primaryChart = null;
                    
                    // Find the first valid DTX file to use as the primary chart
                    foreach (var kvp in difficulties.OrderBy(d => d.Key))
                    {
                        var (_, fileName) = kvp.Value;
                        if (!string.IsNullOrEmpty(fileName))
                        {
                            var filePath = Path.Combine(directory, fileName);
                            if (File.Exists(filePath) && DTXChartParser.IsSupportedFile(filePath))
                            {
                                var (song, chart) = await DTXChartParser.ParseSongEntitiesAsync(filePath);
                                
                                // Priority: SET.def title > DTX title > directory name
                                if (!string.IsNullOrEmpty(setDefTitle))
                                {
                                    // Use SET.def title if available
                                    song.Title = setDefTitle;
                                }
                                else if (string.IsNullOrEmpty(song.Title))
                                {
                                    // If DTX also has no title, use directory name as fallback
                                    var dirInfo = new DirectoryInfo(directory);
                                    song.Title = dirInfo.Name;
                                    Debug.WriteLine($"SongManager: Both SET.def and DTX title parsing failed, using directory name: {song.Title}");
                                }
                                // Otherwise, keep the DTX title as-is
                                
                                primarySong = song;
                                primaryChart = chart;
                                break; // Use the first valid chart as primary
                            }
                        }
                    }
                    
                    // If we found a valid primary chart, create the song node
                    if (primarySong != null && primaryChart != null)
                    {
                        currentSong = SongListNode.CreateSongNode(primarySong, primaryChart);
                        currentSong.Parent = parent;

                        // Process each difficulty and store in database
                        int scoreIndex = 0;
                        foreach (var kvp in difficulties.OrderBy(d => d.Key))
                        {
                            var level = kvp.Key;
                            var (label, fileName) = kvp.Value;

                            if (!string.IsNullOrEmpty(fileName))
                            {
                                var filePath = Path.Combine(directory, fileName);
                                if (File.Exists(filePath) && DTXChartParser.IsSupportedFile(filePath))
                                {
                                    var (diffSong, diffChart) = await DTXChartParser.ParseSongEntitiesAsync(filePath);

                                    // Use the set.def title if available, otherwise keep the DTX title
                                    if (!string.IsNullOrEmpty(songTitle))
                                    {
                                        diffSong.Title = songTitle;
                                    }

                                    // Set the chart difficulty level from SET.def (L1, L2, L3, L5 etc.)
                                    diffChart.DifficultyLevel = level;

                                    // Persist the SET.def difficulty label (BASIC/ADVANCED/EXTREME/...) on the
                                    // chart so the database-load path can drive the performance-stage difficulty
                                    // badge without re-reading the SET.def. Legacy databases stored an empty
                                    // label, so GetSetDefLabelsByFile recovers it from disk at load time.
                                    if (!string.IsNullOrEmpty(label))
                                    {
                                        // SongChart.DifficultyLabel is [MaxLength(50)]; clamp the
                                        // raw SET.def value so an unusually long label cannot violate
                                        // the persisted column contract.
                                        diffChart.DifficultyLabel = label.Length > 50
                                            ? label.Substring(0, 50)
                                            : label;
                                    }

                                    // Compatibility-only parser: build the temporary score metadata
                                    // without performing any persistence.
                                    if (scoreIndex < currentSong.Scores.Length)
                                    {
                                        currentSong.DifficultyLabels[scoreIndex] =
                                            !string.IsNullOrEmpty(label)
                                                ? label
                                                : $"Level {level}";
                                        var (
                                            primaryInstrument,
                                            difficultyLevel) =
                                            GetPrimaryInstrumentAndDifficulty(
                                                diffChart);
                                        currentSong.Scores[scoreIndex] =
                                            new SongScoreEntity
                                        {
                                            Instrument = primaryInstrument,
                                            DifficultyLevel = difficultyLevel,
                                            DifficultyLabel =
                                                !string.IsNullOrEmpty(label)
                                                    ? label
                                                    : $"Level {level}"
                                        };
                                        scoreIndex++;
                                    }
                                }
                            }
                        }
                    }

                    if (currentSong != null && currentSong.AvailableDifficulties > 0)
                    {
                        results.Add(currentSong);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SongManager: Error parsing set.def {setDefPath}: {ex.Message}");
            }

            return results;
        }

        private async Task<BoxDefinition?> ParseBoxDefinitionAsync(string boxDefPath, CancellationToken cancellationToken)
        {
            try
            {
                var lines = await File.ReadAllLinesAsync(boxDefPath, cancellationToken);
                var boxDef = new BoxDefinition();

                foreach (var trimmedLine in lines.Select(line => line.Trim()))
                {
                    if (string.IsNullOrEmpty(trimmedLine) || trimmedLine.StartsWith("//"))
                        continue;

                    var parts = trimmedLine.Split(':', 2);
                    if (parts.Length == 2)
                    {
                        var command = parts[0].Trim().ToUpperInvariant();
                        var value = parts[1].Trim();

                        switch (command)
                        {
                            case "#TITLE":
                                boxDef.Title = value;
                                break;
                            case "#GENRE":
                                boxDef.Genre = value;
                                break;
                            case "#SKINPATH":
                                boxDef.SkinPath = value;
                                break;
                            case "#BGCOLOR":
                                if (TryParseColor(value, out var bgColor))
                                    boxDef.BackgroundColor = bgColor;
                                break;
                            case "#TEXTCOLOR":
                                if (TryParseColor(value, out var textColor))
                                    boxDef.TextColor = textColor;
                                break;
                        }
                    }
                }

                return boxDef;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SongManager: Error parsing box.def {boxDefPath}: {ex.Message}");
                return null;
            }
        }

        private SongListNode CreateBoxNodeFromDirectory(DirectoryInfo directory, SongListNode? parent, BoxDefinition? boxDef)
        {
            var title = boxDef?.Title ?? directory.Name;
            var boxNode = SongListNode.CreateBoxNode(title, directory.FullName, parent);

            if (boxDef != null)
            {
                boxNode.Genre = boxDef.Genre ?? "";
                boxNode.SkinPath = boxDef.SkinPath ?? "";
                boxNode.TextColor = new Microsoft.Xna.Framework.Color(
                    boxDef.TextColor.R,
                    boxDef.TextColor.G,
                    boxDef.TextColor.B,
                    boxDef.TextColor.A);
            }

            return boxNode;
        }

        private bool TryParseColor(string colorValue, out System.Drawing.Color color)
        {
            color = System.Drawing.Color.White;

            try
            {
                if (colorValue.StartsWith("#"))
                {
                    // Hex color format
                    var hex = colorValue.Substring(1);
                    if (hex.Length == 6)
                    {
                        var r = Convert.ToByte(hex.Substring(0, 2), 16);
                        var g = Convert.ToByte(hex.Substring(2, 2), 16);
                        var b = Convert.ToByte(hex.Substring(4, 2), 16);
                        color = System.Drawing.Color.FromArgb(r, g, b);
                        return true;
                    }
                }
                else
                {
                    // Named color
                    color = System.Drawing.Color.FromName(colorValue);
                    return color.IsKnownColor;
                }
            }
            catch
            {
                // Ignore parsing errors
            }

            return false;
        }

        #endregion

        #region EF Core Helper Methods

        /// <summary>
        /// Gets top scores for a specific instrument
        /// </summary>
        public async Task<List<SongScoreEntity>> GetTopScoresAsync(EInstrumentPart instrument, int limit = 10)
        {
            return await GetTopScoresForSpeedAsync(
                instrument,
                PlaySpeedRange.Default,
                limit).ConfigureAwait(false);
        }

        /// <summary>
        /// Gets top scores for one explicit gameplay-speed bucket.
        /// </summary>
        public async Task<List<SongScoreEntity>> GetTopScoresForSpeedAsync(
            EInstrumentPart instrument,
            int playSpeedPercent,
            int limit = 10)
        {
            var db = GetDatabaseServiceSnapshot();
            if (db == null) return new List<SongScoreEntity>();

            try
            {
                return await db.GetTopScoresForSpeedAsync(
                    instrument,
                    playSpeedPercent,
                    limit).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SongManager: Error getting top scores: {ex.Message}");
                return new List<SongScoreEntity>();
            }
        }

        /// <summary>
        /// Updates a score for a specific chart and instrument
        /// </summary>
        public async Task<bool> UpdateScoreAsync(int chartId, EInstrumentPart instrument, int newScore, double achievementRate, bool fullCombo)
        {
            if (_databaseService == null) return false;

            try
            {
                await _databaseService.UpdateScoreAsync(chartId, instrument, newScore, achievementRate, fullCombo);
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SongManager: Error updating score: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Persists a complete PerformanceSummary (score + skill + judgement counts) for a
        /// specific chart and instrument. Forwards to SongDatabaseService, then refreshes
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
            if (_databaseService == null)
            {
                return ScoreSaveResult.Failed(
                    "The song database is unavailable, so the result could not be saved.");
            }

            var result = await _databaseService
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
                Debug.WriteLine(
                    $"SongManager: In-memory score refresh failed after successful save for chart {chartId}: {ex.Message}");
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
            var db = GetDatabaseServiceSnapshot();
            if (db == null) return ScoreRefreshOutcome.NoMatch;

            var fresh = await db.GetScoreWithHistoryAsync(
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

                        var published = CreateScoreSnapshot(fresh);
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
        /// Creates a detached score snapshot with only its own pitch-bearing history
        /// rows and rebuilds the legacy display lines from that scoped collection.
        /// </summary>
        private static SongScore CreateScoreSnapshot(SongScore source)
        {
            var snapshot = source.Clone();
            snapshot.PerformanceHistory = snapshot.PerformanceHistory
                .Where(h => h.SongScoreId == source.Id)
                .OrderBy(h => h.DisplayOrder)
                .ToList();
            snapshot.PlayHistoryLines = snapshot.PerformanceHistory
                .Select(history => history.HistoryLine)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Take(GameConstants.PlayHistory.MaxRecentPlays)
                .ToList();
            return snapshot;
        }

        /// <summary>
        /// Imports DTXManiaNX drum scores from sibling &lt;chart&gt;.score.ini files for every
        /// drum chart already in the database. Best-of merge with snapshot-delta counters;
        /// safe to run repeatedly. Reports progress per chart. See the design spec.
        /// </summary>
        public async Task<NxImportResult> ImportNxScoresAsync(
            IProgress<NxImportProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            var result = new NxImportResult();
            var db = GetDatabaseServiceSnapshot();
            if (db == null)
            {
                Debug.WriteLine("SongManager: ImportNxScoresAsync called with no database service.");
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
                    Debug.WriteLine($"SongManager: NX import error for {chart.FilePath}: {ex.Message}");
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

            Debug.WriteLine($"SongManager: NX import complete — scanned {result.Scanned}, imported {result.Imported}, skipped {result.Skipped}, errors {result.Errors} in {sw.ElapsedMilliseconds}ms.");
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
            var db = GetDatabaseServiceSnapshot();
            if (db == null) return new List<SongListNode>();

            var hasActiveRoots = GetCurrentSearchPathsSnapshot().Length > 0;
            var songs = await db.GetRecentlyPlayedSongsAsync(
                hasActiveRoots ? int.MaxValue : limit).ConfigureAwait(false);
            var nodes = new List<SongListNode>(Math.Min(songs.Count, limit));
            var activeSongs = songs
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
            if (hasActiveRoots)
            {
                activeSongs = activeSongs
                    .OrderByDescending(item => item.LastPlayedAt)
                    .ThenBy(item => item.Song.Id);
            }

            foreach (var item in activeSongs)
            {
                var song = item.Song;
                var charts = item.Charts;
                song.Charts = charts;
                var node = CreateSongNodeFromDatabaseEntities(song, charts);
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
                    if (nodes.Count >= limit)
                        break;
                }
            }
            return nodes;
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
            var db = GetDatabaseServiceSnapshot();
            if (db == null) return new List<SongListNode>();

            var songs = await db.GetBookmarkedSongsAsync().ConfigureAwait(false);
            var nodes = new List<SongListNode>(songs.Count);
            foreach (var song in songs)
            {
                var charts = GetActiveCharts(song);
                if (charts.Length == 0)
                    continue;
                song.Charts = charts;
                var node = CreateSongNodeFromDatabaseEntities(song, charts);
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
            var db = GetDatabaseServiceSnapshot();
            if (db == null) return;
            await db.SetBookmarkAsync(songId, bookmarked).ConfigureAwait(false);
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Finds songs by search term
        /// </summary>
        public async Task<List<SongEntity>> FindSongsBySearchAsync(string searchTerm)
        {
            var database = GetDatabaseServiceSnapshot();
            if (database == null) return new List<SongEntity>();

            try
            {
                var songs = await database.SearchSongsAsync(searchTerm)
                    .ConfigureAwait(false);
                var result = new List<SongEntity>(songs.Count);
                foreach (var song in songs)
                {
                    var charts = GetActiveCharts(song);
                    if (charts.Length == 0)
                        continue;
                    song.Charts = charts;
                    result.Add(song);
                }

                return result;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SongManager: Error searching songs: {ex.Message}");
                return new List<SongEntity>();
            }
        }

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
        /// Gets songs by genre
        /// </summary>
        public async Task<List<SongEntity>> GetSongsByGenreAsync(string genre)
        {
            if (_databaseService == null) return new List<SongEntity>();

            try
            {
                return await _databaseService.GetSongsByGenreAsync(genre);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SongManager: Error getting songs by genre: {ex.Message}");
                return new List<SongEntity>();
            }
        }

        /// <summary>
        /// Checks if enumeration is needed based on database existence and directory modification times
        /// </summary>
        public async Task<bool> NeedsEnumerationAsync(string[] searchPaths, bool forceEnumeration = false)
        {
            try
            {
                SetCurrentSearchPaths(searchPaths);
                if (forceEnumeration)
                {
                    Debug.WriteLine("SongManager: Force enumeration requested");
                    return true;
                }

                // Check if database service exists and is accessible
                if (_databaseService == null || !await _databaseService.DatabaseExistsAsync())
                {
                    Debug.WriteLine("SongManager: Database doesn't exist, enumeration needed");
                    return true;
                }

                // If database has no songs, we need enumeration
                var stats = await _databaseService.GetDatabaseStatsAsync();
                if (stats.SongCount == 0)
                {
                    Debug.WriteLine("SongManager: Database is empty, enumeration needed");
                    return true;
                }

                Debug.WriteLine($"SongManager: Database contains {stats.SongCount} songs, checking for filesystem changes...");

                // Check for filesystem changes by comparing directory modification times
                var changeDetected = await DetectFilesystemChangesAsync(searchPaths);
                if (changeDetected)
                {
                    Debug.WriteLine("SongManager: Filesystem changes detected, enumeration needed");
                    return true;
                }

                Debug.WriteLine("SongManager: No changes detected, enumeration not needed");
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SongManager: Error checking enumeration need: {ex.Message}");
                Debug.WriteLine($"SongManager: Stack trace: {ex.StackTrace}");
                return true; // Default to enumeration if we can't determine
            }
        }

        /// <summary>
        /// Checks if files in the database still exist at their recorded paths
        /// This detects when files have been moved or deleted
        /// </summary>
        private async Task<bool> CheckDatabaseFilesStillExist()
        {
            try
            {
                if (_databaseService == null)
                    return false;

                var allSongs = await _databaseService.GetSongsAsync();
                int missingFiles = 0;
                int updatedFiles = 0;
                int totalFiles = 0;

                foreach (var song in allSongs.Where(s => s.Charts != null))
                {
                    foreach (var chart in song.Charts!)
                    {
                        totalFiles++;
                        if (!string.IsNullOrEmpty(chart.FilePath) && !File.Exists(chart.FilePath))
                        {
                            // File is missing from recorded path - try to find it in new location
                            string? newPath = await FindMovedFileAsync(chart.FilePath);
                            
                            if (!string.IsNullOrEmpty(newPath))
                            {
                                // Found the file in a new location - update database
                                Debug.WriteLine($"SongManager: File moved detected: '{chart.FilePath}' -> '{newPath}'");
                                await UpdateChartFilePathAsync(chart.Id, newPath);
                                updatedFiles++;
                            }
                            else
                            {
                                missingFiles++;
                                Debug.WriteLine($"SongManager: Missing file detected: {chart.FilePath}");
                            }
                        }
                    }
                }

                Debug.WriteLine($"SongManager: File existence check - {missingFiles} missing, {updatedFiles} updated, {totalFiles} total files");

                // Return true if we had missing files OR updated file paths (both require re-enumeration)
                return missingFiles > 0 || updatedFiles > 0;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SongManager: Error checking database file existence: {ex.Message}");
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
                if (_databaseService != null)
                {
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
                }

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
                Debug.WriteLine($"SongManager: Error finding moved file {originalPath}: {ex.Message}");
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
                Debug.WriteLine($"SongManager: Error comparing files {originalPath} vs {candidatePath}: {ex.Message}");
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
                if (_databaseService == null)
                    return;

                using var context = _databaseService.CreateContext();
                var chart = await context.SongCharts.FindAsync(chartId);
                if (chart != null)
                {
                    chart.FilePath = newPath;
                    await context.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SongManager: Error updating chart file path for ID {chartId}: {ex.Message}");
            }
        }

        /// <summary>
        /// Detects filesystem changes in song directories
        /// </summary>
        private async Task<bool> DetectFilesystemChangesAsync(string[] searchPaths)
        {
            try
            {
                // Get the last enumeration timestamp from database
                var lastEnumerationTime = await GetLastEnumerationTimestampAsync();
                if (lastEnumerationTime == null)
                {
                    Debug.WriteLine("SongManager: No last enumeration timestamp found - first time enumeration");
                    return true; // First time enumeration
                }

                Debug.WriteLine($"SongManager: Last enumeration was at {lastEnumerationTime:yyyy-MM-dd HH:mm:ss}");

                // First, check if file counts have changed - this is a reliable indicator
                var currentFileCount = await CountDTXFilesAsync(searchPaths);
                var databaseSongCount = await GetDatabaseScoreCountAsync();
                
                Debug.WriteLine($"SongManager: Current DTX files: {currentFileCount}, Database songs: {databaseSongCount}");
                
                if (currentFileCount != databaseSongCount)
                {
                    Debug.WriteLine($"SongManager: File count mismatch detected - files: {currentFileCount}, database: {databaseSongCount}");
                    return true;
                }

                // Check if files in database still exist at their recorded paths (detects moves)
                var filesMovedOrDeleted = await CheckDatabaseFilesStillExist();
                if (filesMovedOrDeleted)
                {
                    Debug.WriteLine($"SongManager: Some files have been moved or deleted since last enumeration");
                    return true;
                }

                // Check each search path for changes
                foreach (var searchPath in searchPaths)
                {
                    if (string.IsNullOrEmpty(searchPath))
                    {
                        Debug.WriteLine($"SongManager: Search path is null or empty, skipping");
                        continue;
                    }

                    var fullPath = Path.GetFullPath(searchPath);
                    Debug.WriteLine($"SongManager: Checking search path: {fullPath}");

                    if (!Directory.Exists(fullPath))
                    {
                        Debug.WriteLine($"SongManager: Search path doesn't exist: {fullPath}");
                        // If the directory doesn't exist but we have songs in DB, this is a change
                        var songCount = await GetDatabaseScoreCountAsync();
                        if (songCount > 0)
                        {
                            Debug.WriteLine($"SongManager: Directory missing but database has {songCount} songs - change detected");
                            return true;
                        }
                        continue;
                    }

                    // Check if directory or its contents have been modified since last enumeration
                    var hasChanges = await CheckDirectoryForChangesAsync(fullPath, lastEnumerationTime.Value);
                    if (hasChanges)
                    {
                        Debug.WriteLine($"SongManager: Changes detected in {fullPath}");
                        return true;
                    }
                    else
                    {
                        Debug.WriteLine($"SongManager: No changes detected in {fullPath}");
                    }
                }

                Debug.WriteLine("SongManager: No changes detected in any search path");
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SongManager: Error detecting filesystem changes: {ex.Message}");
                Debug.WriteLine($"SongManager: Stack trace: {ex.StackTrace}");
                return true; // Default to enumeration on error
            }
        }

        /// <summary>
        /// Counts DTX files in all search paths
        /// </summary>
        private async Task<int> CountDTXFilesAsync(string[] searchPaths)
        {
            int totalCount = 0;
            try
            {
                foreach (var searchPath in searchPaths.Where(path => !string.IsNullOrEmpty(path) && Directory.Exists(path)))
                {
                    // Use async enumeration to avoid blocking
                    await Task.Run(() =>
                    {
                        int pathCount = Directory.EnumerateFiles(searchPath, "*.dtx", SearchOption.AllDirectories).Count();
                        totalCount += pathCount;
                        Debug.WriteLine($"SongManager: Found {pathCount} DTX files in {searchPath}");
                    });
                }
                Debug.WriteLine($"SongManager: Total DTX files found: {totalCount}");
                return totalCount;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SongManager: Error counting DTX files: {ex.Message}");
                return -1; // Return invalid count to trigger enumeration
            }
        }

        /// <summary>
        /// Recursively checks a directory for changes since the last enumeration
        /// </summary>
        private async Task<bool> CheckDirectoryForChangesAsync(string directoryPath, DateTime lastEnumerationTime)
        {
            try
            {
                var dirInfo = new DirectoryInfo(directoryPath);
                Debug.WriteLine($"SongManager: Checking directory: {directoryPath}");
                Debug.WriteLine($"SongManager: Directory last write time: {dirInfo.LastWriteTime:yyyy-MM-dd HH:mm:ss}");
                Debug.WriteLine($"SongManager: Comparing against: {lastEnumerationTime:yyyy-MM-dd HH:mm:ss}");
                
                // Check if the directory itself was modified
                if (dirInfo.LastWriteTime > lastEnumerationTime)
                {
                    Debug.WriteLine($"SongManager: Directory modified: {directoryPath} at {dirInfo.LastWriteTime:yyyy-MM-dd HH:mm:ss} (after {lastEnumerationTime:yyyy-MM-dd HH:mm:ss})");
                    return true;
                }

                // Check for new or modified DTX/SET files using async enumeration
                var dtxExtensions = new[] { "*.dtx", "*.set" };
                int totalFilesChecked = 0;
                int modifiedFilesFound = 0;

                foreach (var extension in dtxExtensions)
                {
                    Debug.WriteLine($"SongManager: Scanning for {extension} files in {directoryPath}");
                    
                    // Use async enumeration to avoid blocking
                    var hasChanges = await Task.Run(() =>
                    {
                        int extensionFileCount = 0;
                        foreach (var filePath in Directory.EnumerateFiles(directoryPath, extension, SearchOption.AllDirectories))
                        {
                            extensionFileCount++;
                            totalFilesChecked++;
                            
                            var fileInfo = new FileInfo(filePath);
                            var fileIsNew = fileInfo.CreationTime > lastEnumerationTime;
                            var fileIsModified = fileInfo.LastWriteTime > lastEnumerationTime;
                            
                            if (fileIsNew || fileIsModified)
                            {
                                modifiedFilesFound++;
                                var reason = fileIsNew ? "new" : "modified";
                                var timestamp = fileIsNew ? fileInfo.CreationTime : fileInfo.LastWriteTime;
                                Debug.WriteLine($"SongManager: {reason.ToUpper()} file detected: {filePath}");
                                Debug.WriteLine($"SongManager: File timestamp: {timestamp:yyyy-MM-dd HH:mm:ss} vs enumeration: {lastEnumerationTime:yyyy-MM-dd HH:mm:ss}");
                                return true;
                            }
                        }
                        Debug.WriteLine($"SongManager: Found {extensionFileCount} {extension} files");
                        return false;
                    });
                    
                    if (hasChanges) return true;
                }

                Debug.WriteLine($"SongManager: Checked {totalFilesChecked} files, found {modifiedFilesFound} modified files");

                // Check subdirectories recursively (but limit depth to avoid infinite loops)
                var subdirChanges = await CheckSubdirectoriesForChangesAsync(directoryPath, lastEnumerationTime, 0, 10);
                if (subdirChanges) return true;

                Debug.WriteLine($"SongManager: No changes detected in {directoryPath}");
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SongManager: Error checking directory {directoryPath}: {ex.Message}");
                Debug.WriteLine($"SongManager: Stack trace: {ex.StackTrace}");
                return true; // Assume changes on error
            }
        }

        /// <summary>
        /// Recursively checks subdirectories with depth limit
        /// </summary>
        private async Task<bool> CheckSubdirectoriesForChangesAsync(string directoryPath, DateTime lastEnumerationTime, int currentDepth, int maxDepth)
        {
            if (currentDepth >= maxDepth)
            {
                Debug.WriteLine($"SongManager: Maximum depth {maxDepth} reached for {directoryPath}");
                return false;
            }

            try
            {
                // Use async enumeration for directories
                var hasChanges = await Task.Run(() =>
                {
                    var subdirectoryPaths = Directory.EnumerateDirectories(directoryPath).ToList();
                    Debug.WriteLine($"SongManager: Checking {subdirectoryPaths.Count} subdirectories in {directoryPath} (depth {currentDepth})");

                    foreach (var subdirPath in subdirectoryPaths)
                    {
                        var subdirInfo = new DirectoryInfo(subdirPath);
                        
                        // Skip hidden directories and common non-song directories
                        if (subdirInfo.Name.StartsWith(".") || 
                            subdirInfo.Name.Equals("System", StringComparison.OrdinalIgnoreCase) ||
                            subdirInfo.Name.Equals("Cache", StringComparison.OrdinalIgnoreCase))
                        {
                            Debug.WriteLine($"SongManager: Skipping directory: {subdirInfo.Name}");
                            continue;
                        }

                        Debug.WriteLine($"SongManager: Checking subdirectory: {subdirPath}");
                        Debug.WriteLine($"SongManager: Subdirectory last write time: {subdirInfo.LastWriteTime:yyyy-MM-dd HH:mm:ss}");

                        if (subdirInfo.LastWriteTime > lastEnumerationTime)
                        {
                            Debug.WriteLine($"SongManager: Subdirectory modified: {subdirPath} at {subdirInfo.LastWriteTime:yyyy-MM-dd HH:mm:ss} (after {lastEnumerationTime:yyyy-MM-dd HH:mm:ss})");
                            return true;
                        }

                        // Check for DTX files directly in this subdirectory using enumeration
                        var dtxFileCount = 0;
                        foreach (var dtxFilePath in Directory.EnumerateFiles(subdirPath, "*.dtx", SearchOption.TopDirectoryOnly))
                        {
                            dtxFileCount++;
                            var dtxFileInfo = new FileInfo(dtxFilePath);
                            
                            if (dtxFileInfo.CreationTime > lastEnumerationTime || dtxFileInfo.LastWriteTime > lastEnumerationTime)
                            {
                                var reason = dtxFileInfo.CreationTime > lastEnumerationTime ? "new" : "modified";
                                var timestamp = dtxFileInfo.CreationTime > lastEnumerationTime ? dtxFileInfo.CreationTime : dtxFileInfo.LastWriteTime;
                                Debug.WriteLine($"SongManager: {reason.ToUpper()} DTX file in subdirectory: {dtxFilePath}");
                                Debug.WriteLine($"SongManager: File timestamp: {timestamp:yyyy-MM-dd HH:mm:ss} vs enumeration: {lastEnumerationTime:yyyy-MM-dd HH:mm:ss}");
                                return true;
                            }
                        }
                        
                        if (dtxFileCount > 0)
                        {
                            Debug.WriteLine($"SongManager: Found {dtxFileCount} DTX files in {subdirPath}");
                        }
                    }
                    return false;
                });
                
                if (hasChanges) return true;

                // Now recursively check each subdirectory
                foreach (var subdirPath in Directory.EnumerateDirectories(directoryPath))
                {
                    var subdirInfo = new DirectoryInfo(subdirPath);
                    
                    // Skip hidden directories and common non-song directories
                    if (subdirInfo.Name.StartsWith(".") || 
                        subdirInfo.Name.Equals("System", StringComparison.OrdinalIgnoreCase) ||
                        subdirInfo.Name.Equals("Cache", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    // Recursively check subdirectory
                    var subdirHasChanges = await CheckSubdirectoriesForChangesAsync(subdirPath, lastEnumerationTime, currentDepth + 1, maxDepth);
                    if (subdirHasChanges) return true;
                }

                Debug.WriteLine($"SongManager: No changes found in subdirectories of {directoryPath}");
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SongManager: Error checking subdirectories of {directoryPath}: {ex.Message}");
                Debug.WriteLine($"SongManager: Stack trace: {ex.StackTrace}");
                return true; // Assume changes on error
            }
        }

        /// <summary>
        /// Gets the timestamp of the last enumeration from the database
        /// </summary>
        private async Task<DateTime?> GetLastEnumerationTimestampAsync()
        {
            try
            {
                if (_databaseService == null)
                {
                    Debug.WriteLine("SongManager: Database service is null");
                    return null;
                }

                var dbPath = _databaseService.DatabasePath;
                Debug.WriteLine($"SongManager: Checking database path: {dbPath}");

                if (!File.Exists(dbPath))
                {
                    Debug.WriteLine($"SongManager: Database file doesn't exist: {dbPath}");
                    return null;
                }

                var dbInfo = new FileInfo(dbPath);
                var lastWriteTime = dbInfo.LastWriteTime;
                Debug.WriteLine($"SongManager: Database last write time: {lastWriteTime:yyyy-MM-dd HH:mm:ss}");

                // Check if the database actually has songs
                var songCount = await GetDatabaseScoreCountAsync();
                Debug.WriteLine($"SongManager: Database contains {songCount} songs");

                if (songCount == 0)
                {
                    Debug.WriteLine("SongManager: Database is empty, treating as no enumeration done");
                    return null;
                }

                // Use database modification time, but subtract a small buffer to ensure we catch recent changes
                // This is important because filesystem timestamps might have slight differences
                var timestampWithBuffer = lastWriteTime.AddMinutes(-1);
                Debug.WriteLine($"SongManager: Using enumeration timestamp with 1-minute buffer: {timestampWithBuffer:yyyy-MM-dd HH:mm:ss}");
                
                return timestampWithBuffer;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SongManager: Error getting last enumeration timestamp: {ex.Message}");
                Debug.WriteLine($"SongManager: Stack trace: {ex.StackTrace}");
                return null;
            }
        }

        /// <summary>
        /// Updates the enumeration timestamp in the database
        /// </summary>
        private async Task UpdateEnumerationTimestampAsync()
        {
            try
            {
                if (_databaseService == null) return;

                // This could be enhanced to store enumeration metadata in a dedicated table
                // For now, the database modification time serves as the enumeration timestamp
                Debug.WriteLine($"SongManager: Enumeration timestamp updated to {DateTime.Now}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SongManager: Error updating enumeration timestamp: {ex.Message}");
            }
        }

        /// <summary>
        /// Creates a SongListNode from database entities (song + charts)
        /// </summary>
        private SongListNode? CreateSongNodeFromDatabaseEntities(SongEntity song, SongChart[] charts)
        {
            try
            {
                if (charts.Length == 0) return null;

                // Use the first chart for the primary file path
                var primaryChart = charts[0];
                var orderedCharts = charts
                    .OrderBy(c => (c.HasDrumChart && c.DrumLevel > 0) ? 0 : 1)
                    .ThenBy(c => c.DrumLevel)
                    .ToArray();

                // Create the song node using the first chart
                var songNode = SongListNode.CreateSongNode(
                    song,
                    primaryChart,
                    hydratePersistedScores: false);

                // If there are multiple charts, populate the difficulties
                if (charts.Length > 1)
                {
                    // The factory seeded slots from only the primary chart. Rebuild the
                    // complete multi-chart metadata layout from scratch so unused
                    // instrument slots from that chart cannot survive the ordered pass.
                    songNode.Scores = new SongScore[5];
                    songNode.PublishScoreVariants(
                        Array.Empty<KeyValuePair<ScoreVariantKey, SongScore>>());

                    // Recover the authentic difficulty-tier labels (BASIC/ADVANCED/EXTREME/...) from the
                    // SET.def when the persisted chart label is empty, so the performance-stage difficulty
                    // badge selects the matching cell instead of always falling back to the DTX cell.
                    // Legacy databases stored an empty SongChart.DifficultyLabel.
                    // Only hit disk when at least one chart is missing its label — avoids re-reading
                    // SET.def for every multi-chart song on startup when labels are already persisted.
                    var setDefLabels = charts.Any(c => string.IsNullOrWhiteSpace(c.DifficultyLabel))
                        ? GetSetDefLabelsByFile(Path.GetDirectoryName(primaryChart.FilePath) ?? "")
                        : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                    int scoreIndex = 0;
                    // Order the charts by the same criteria SongChartHelper.GetCurrentDifficultyChart
                    // uses to map a difficulty index to a chart (drum charts with a positive level,
                    // ascending by DrumLevel), so DifficultyLabels[scoreIndex] corresponds to the chart
                    // selected for that difficulty. Without this, an arbitrary database ordering could
                    // place an EXTREME chart's label in slot 0 while difficulty 0 loads the BASIC chart,
                    // making the performance-stage badge show the wrong difficulty cell. Non-drum charts
                    // (DrumLevel == 0) sort after drum charts, matching the drum-first selection fallback.
                    foreach (var chart in orderedCharts.Take(5)) // Limit to 5 difficulties
                    {
                        if (scoreIndex >= songNode.Scores.Length) break;

                        // Determine the primary instrument and difficulty for this chart
                        var primaryInstrument = DTXMania.Game.Lib.Song.Entities.EInstrumentPart.DRUMS;
                        int difficultyLevel = 50;

                        if (chart.HasDrumChart && chart.DrumLevel > 0)
                        {
                            primaryInstrument = DTXMania.Game.Lib.Song.Entities.EInstrumentPart.DRUMS;
                            difficultyLevel = chart.DrumLevel;
                        }
                        else if (chart.HasGuitarChart && chart.GuitarLevel > 0)
                        {
                            primaryInstrument = DTXMania.Game.Lib.Song.Entities.EInstrumentPart.GUITAR;
                            difficultyLevel = chart.GuitarLevel;
                        }
                        else if (chart.HasBassChart && chart.BassLevel > 0)
                        {
                            primaryInstrument = DTXMania.Game.Lib.Song.Entities.EInstrumentPart.BASS;
                            difficultyLevel = chart.BassLevel;
                        }

                        string difficultyLabelText = ResolveDifficultyLabel(chart, setDefLabels, scoreIndex);

                        songNode.SetScore(scoreIndex, new DTXMania.Game.Lib.Song.Entities.SongScore
                        {
                            ChartId = chart.Id,
                            Instrument = primaryInstrument,
                            DifficultyLevel = difficultyLevel,
                            DifficultyLabel = difficultyLabelText
                        });

                        songNode.DifficultyLabels[scoreIndex] = difficultyLabelText;
                        scoreIndex++;
                    }
                }

                // Metadata slots are now final. Publish every eagerly loaded persisted
                // speed as one complete immutable snapshot; only 1.00x replaces Scores.
                HydrateScoreVariants(songNode, charts);

                return songNode;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SongManager: Error creating song node from database entities: {ex.Message}");
                return null;
            }
        }

        private static void HydrateScoreVariants(
            SongListNode songNode,
            IEnumerable<SongChart> charts)
        {
            var chartArray = charts.ToArray();
            var variants = new Dictionary<ScoreVariantKey, SongScore>();

            for (int difficultyIndex = 0;
                difficultyIndex < songNode.Scores.Length;
                difficultyIndex++)
            {
                var metadata = songNode.Scores[difficultyIndex];
                if (metadata == null)
                    continue;

                metadata.PlaySpeedPercent = PlaySpeedRange.Default;
                variants[new ScoreVariantKey(
                    difficultyIndex,
                    PlaySpeedRange.Default)] = metadata.Clone();

                var persistedScores = chartArray
                    .Where(chart => metadata.ChartId != 0
                        ? chart.Id == metadata.ChartId
                        : chart.GetDifficultyLevel(
                            metadata.Instrument.ToString()) == metadata.DifficultyLevel)
                    .SelectMany(chart =>
                        chart.Scores ?? Enumerable.Empty<SongScore>())
                    .Where(score => score.Instrument == metadata.Instrument);

                foreach (var persisted in persistedScores)
                {
                    var snapshot = CreateScoreSnapshot(persisted);
                    snapshot.ChartId = metadata.ChartId;
                    snapshot.Instrument = metadata.Instrument;
                    snapshot.DifficultyLevel = metadata.DifficultyLevel;
                    snapshot.DifficultyLabel = metadata.DifficultyLabel;
                    variants[new ScoreVariantKey(
                        difficultyIndex,
                        persisted.PlaySpeedPercent)] = snapshot;
                }
            }

            songNode.PublishScoreVariants(variants);
        }

        /// <summary>
        /// Resolves the difficulty-tier label shown by the performance-stage difficulty badge for a
        /// chart. Prefers the persisted <see cref="SongChart.DifficultyLabel"/>, then the SET.def
        /// #LnLABEL recovered by matching the chart's file name (e.g. "bas.dtx" -> "BASIC"), and
        /// finally a synthetic "Level N" placeholder when no authentic label is available.
        /// </summary>
        internal static string ResolveDifficultyLabel(SongChart chart, IReadOnlyDictionary<string, string> setDefLabels, int scoreIndex)
        {
            if (!string.IsNullOrWhiteSpace(chart.DifficultyLabel))
                return chart.DifficultyLabel;

            if (setDefLabels != null && !string.IsNullOrEmpty(chart.FilePath))
            {
                var fileName = Path.GetFileName(chart.FilePath);
                if (setDefLabels.TryGetValue(fileName, out var label) && !string.IsNullOrWhiteSpace(label))
                    return label;
            }

            return $"Level {scoreIndex + 1}";
        }

        /// <summary>
        /// Clears all data
        /// </summary>
        public void Clear()
        {
            lock (_lockObject)
            {
                // Cancel any ongoing enumeration first
                _enumCancellation?.Cancel();
                _enumCancellation?.Dispose();
                _enumCancellation = null;

                _rootSongs.Clear();
                _isInitialized = false;
                _databaseService?.Dispose();
                _databaseService = null;
                _currentSearchPaths = Array.Empty<string>();
                ParseSongEntitiesCoreAsync =
                    DTXChartParser.ParseSongEntitiesAsync;
                EnumerateFilesCore = Directory.EnumerateFiles;
                EnumerateDirectoriesCore = Directory.EnumerateDirectories;
                ReadAllLinesCoreAsync =
                    static (path, encoding, token) =>
                        File.ReadAllLinesAsync(path, encoding, token);
                ImportSongsCoreAsync = DefaultImportSongsAsync;
                GetDatabaseStatsCoreAsync =
                    static database => database.GetDatabaseStatsAsync();
            }
            DiscoveredScoreCount = 0;
            EnumeratedFileCount = 0;
        }

        /// <summary>
        /// Resets the singleton instance completely for testing purposes
        /// </summary>
        public static void ResetInstanceForTesting()
        {
            lock (_instanceLock)
            {
                _instance?.Clear();
                _instance = null;
            }
        }

        #endregion
    }

    #region Supporting Classes

    /// <summary>
    /// Event args for song discovered event
    /// </summary>
    public class SongDiscoveredEventArgs : EventArgs
    {
        public SongListNode Song { get; }

        public SongDiscoveredEventArgs(SongListNode song)
        {
            Song = song;
        }
    }

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

    /// <summary>
    /// Box definition metadata from box.def files
    /// </summary>
    public class BoxDefinition
    {
        public string Title { get; set; } = "";
        public string Genre { get; set; } = "";
        public string SkinPath { get; set; } = "";
        public System.Drawing.Color BackgroundColor { get; set; } = System.Drawing.Color.Black;
        public System.Drawing.Color TextColor { get; set; } = System.Drawing.Color.White;
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
