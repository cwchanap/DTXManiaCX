using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using DTXMania.Game.Lib.Song.Entities;
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
        /// Gets the database service instance. May be null if not initialized.
        /// </summary>
        public SongDatabaseService DatabaseService
        {
            get
            {
                lock (_lockObject)
                {
                    return _databaseService;
                }
            }
        }

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
        /// Fired when enumeration progress changes
        /// </summary>
        public event EventHandler<EnumerationProgressEventArgs>? ProgressChanged;

        /// <summary>
        /// Fired when enumeration completes
        /// </summary>
        public event EventHandler? EnumerationCompleted;

        #endregion

        #region Individual Phase Operations

        /// <summary>
        /// Initializes the database service (SongListDB and SongsDB phases)
        /// </summary>
        public async Task<bool> InitializeDatabaseServiceAsync(string databasePath = "songs.db", bool purgeDatabaseFirst = false)
        {
            try
            {
                // Initialize the database service if not already done (with proper synchronization)
                lock (_lockObject)
                {
                    if (_databaseService == null)
                    {
                        _databaseService = new SongDatabaseService(databasePath);
                    }
                }

                // Check for database corruption first
                bool isDatabaseCorrupted = await IsDatabaseCorruptedAsync().ConfigureAwait(false);

                // Purge the database only if explicitly requested OR if corruption is detected
                if (purgeDatabaseFirst)
                {
                    Debug.WriteLine("SongManager: Purging existing database for fresh rebuild (explicitly requested)");
                    await _databaseService.PurgeDatabaseAsync().ConfigureAwait(false);
                }
                else if (isDatabaseCorrupted)
                {
                    Debug.WriteLine("SongManager: Database corruption detected, purging corrupted database");
                    await _databaseService.PurgeDatabaseAsync().ConfigureAwait(false);
                }
                else
                {
                    Debug.WriteLine("SongManager: Database appears healthy, proceeding with existing database");
                }

                await _databaseService.InitializeDatabaseAsync().ConfigureAwait(false);
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
                if (_databaseService == null)
                {
                    Debug.WriteLine("SongManager: Cannot load score cache - database service not initialized");
                    return false;
                }

                // Check if we need to enumerate or can build from database
                bool needsEnumeration = await GetDatabaseScoreCountAsync().ConfigureAwait(false) == 0 || await NeedsEnumerationAsync(searchPaths).ConfigureAwait(false);

                if (!needsEnumeration)
                {
                    Debug.WriteLine("SongManager: Building song list from database cache");
                    await BuildSongListFromDatabaseAsync(searchPaths).ConfigureAwait(false);
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
            Debug.WriteLine("SongManager: Starting EnumerateSongsOnlyAsync...");
            var result = await EnumerateSongsAsync(searchPaths, progress, cancellationToken).ConfigureAwait(false);
            
            Debug.WriteLine($"SongManager: EnumerateSongsAsync returned {result} songs");
            
            // Check database count after enumeration
            var dbCount = await GetDatabaseScoreCountAsync().ConfigureAwait(false);
            Debug.WriteLine($"SongManager: Database now contains {dbCount} songs");
            
            // Check _rootSongs count
            lock (_lockObject)
            {
                Debug.WriteLine($"SongManager: _rootSongs contains {_rootSongs.Count} nodes");
                
                // Log some sample songs for debugging
                for (int i = 0; i < Math.Min(3, _rootSongs.Count); i++)
                {
                    var node = _rootSongs[i];
                    Debug.WriteLine($"SongManager: Root node {i}: {node.Type} - {node.Title}");
                    
                    if (node.Type == NodeType.Score && node.Scores.Length > 0)
                    {
                        var score = node.Scores[0];
                        var previewImage = score.Chart?.PreviewImage ?? "null";
                        Debug.WriteLine($"SongManager: Song details - DifficultyLevel: {score.DifficultyLevel}, PreviewImage: {previewImage}");
                    }
                }
            }
            
            // Update enumeration timestamp after successful enumeration
            if (result >= 0) // Changed from > 0 to >= 0 to handle empty directories
            {
                await UpdateEnumerationTimestampAsync().ConfigureAwait(false);
            }
            
            return result;
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

                // Database is automatically saved during enumeration, so this is mostly a confirmation step
                var stats = await GetDatabaseStatsAsync().ConfigureAwait(false);
                if (stats != null)
                {
                    Debug.WriteLine($"SongManager: Songs DB save complete. Database contains {stats.ScoreCount} songs");
                    return true;
                }
                else
                {
                    Debug.WriteLine("SongManager: Unable to verify database save");
                    return false;
                }
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
        /// Public wrapper for BuildSongListFromDatabaseAsync (needed for StartupStage)
        /// </summary>
        public async Task BuildSongListFromDatabasePublicAsync(string[] searchPaths)
        {
            Debug.WriteLine($"SongManager: BuildSongListFromDatabasePublicAsync called with {searchPaths.Length} search paths");
            
            // Check database state before building
            var dbCount = await GetDatabaseScoreCountAsync();
            Debug.WriteLine($"SongManager: Database contains {dbCount} songs before building list");
            
            // Check current _rootSongs state
            lock (_lockObject)
            {
                Debug.WriteLine($"SongManager: _rootSongs currently has {_rootSongs.Count} nodes");
            }
            
            await BuildSongListFromDatabaseAsync(searchPaths).ConfigureAwait(false);
            
            // Check results after building
            lock (_lockObject)
            {
                Debug.WriteLine($"SongManager: _rootSongs now has {_rootSongs.Count} nodes after building");
                
                // Log some details about the root nodes
                for (int i = 0; i < Math.Min(5, _rootSongs.Count); i++)
                {
                    var node = _rootSongs[i];
                    Debug.WriteLine($"SongManager: Root node {i}: {node.Type} - {node.Title} (children: {node.Children.Count})");
                }
                
                if (_rootSongs.Count > 5)
                {
                    Debug.WriteLine($"SongManager: ... and {_rootSongs.Count - 5} more root nodes");
                }
            }
        }

        #endregion

        #region Initialization and Database Management


        /// <summary>
        /// Checks if the database exists and is accessible
        /// </summary>
        public async Task<bool> DatabaseExistsAsync()
        {
            if (_databaseService == null) return false;

            try
            {
                return await _databaseService.DatabaseExistsAsync();
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
            if (_databaseService == null) return false;

            try
            {
                // Check if we can connect to the database
                if (!await _databaseService.DatabaseExistsAsync())
                    return false; // Database doesn't exist, not corrupted

                // Try to get basic stats to verify database integrity
                var stats = await _databaseService.GetDatabaseStatsAsync();
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
            if (_databaseService == null) return null;

            try
            {
                return await _databaseService.GetDatabaseStatsAsync();
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
            CancellationToken token;
            lock (_lockObject)
            {
                if (_enumCancellation != null && !_enumCancellation.Token.IsCancellationRequested)
                {
                    Debug.WriteLine("SongManager: Enumeration already in progress");
                    return 0;
                }

                _enumCancellation?.Dispose();
                _enumCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                token = _enumCancellation.Token;
            }

            DiscoveredScoreCount = 0;
            EnumeratedFileCount = 0;

            try
            {
                Debug.WriteLine($"SongManager: Starting enumeration of {searchPaths.Length} paths");

                var newRootNodes = new List<SongListNode>();

                foreach (var searchPath in searchPaths)
                {
                    if (string.IsNullOrEmpty(searchPath) || !Directory.Exists(searchPath))
                    {
                        Debug.WriteLine($"SongManager: Skipping invalid path: {searchPath}");
                        continue;
                    }

                    var pathNodes = await EnumerateDirectoryAsync(searchPath, null, progress, token).ConfigureAwait(false);
                    newRootNodes.AddRange(pathNodes);
                }

                // Clean up stale database entries before finalizing
                if (_databaseService != null)
                {
                    Debug.WriteLine("SongManager: Cleaning up stale database entries...");
                    await _databaseService.CleanupStaleChartsAsync();
                }

                // Update root songs list
                lock (_lockObject)
                {
                    _rootSongs.Clear();
                    _rootSongs.AddRange(newRootNodes);
                }

                Debug.WriteLine($"SongManager: Enumeration complete. Found {DiscoveredScoreCount} songs in {newRootNodes.Count} root nodes");

                EnumerationCompleted?.Invoke(this, EventArgs.Empty);

                return DiscoveredScoreCount;
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("SongManager: Enumeration was cancelled");
                return DiscoveredScoreCount;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SongManager: Error during enumeration: {ex.Message}");
                return DiscoveredScoreCount;
            }
            finally
            {
                lock (_lockObject)
                {
                    _enumCancellation?.Dispose();
                    _enumCancellation = null;
                }
            }
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
        private async Task BuildSongListFromDatabaseAsync(string[] searchPaths)
        {
            if (_databaseService == null)
            {
                Debug.WriteLine("SongManager: Cannot build song list - database service not initialized");
                return;
            }

            try
            {
                // Clean up stale database entries first to avoid processing outdated file paths
                Debug.WriteLine("SongManager: Cleaning up stale database entries before building song list...");
                await _databaseService.CleanupStaleChartsAsync();

                var newRootNodes = new List<SongListNode>();

                foreach (var searchPath in searchPaths)
                {
                    if (string.IsNullOrEmpty(searchPath) || !Directory.Exists(searchPath))
                    {
                        Debug.WriteLine($"SongManager: Skipping invalid path during database rebuild: {searchPath}");
                        continue;
                    }

                    // Get all songs from the database
                    var allSongs = await _databaseService.GetSongsAsync();

                    // Get all charts that belong to this search path
                    var relevantCharts = new List<(SongEntity song, SongChart chart)>();

                    foreach (var song in allSongs)
                    {
                        foreach (var chart in song.Charts)
                        {
                            if (!string.IsNullOrEmpty(chart.FilePath) &&
                                Path.GetFullPath(chart.FilePath).StartsWith(Path.GetFullPath(searchPath), StringComparison.OrdinalIgnoreCase))
                            {
                                relevantCharts.Add((song, chart));
                            }
                        }
                    }

                    Debug.WriteLine($"SongManager: Found {relevantCharts.Count} charts in database for path: {searchPath}");

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

                Debug.WriteLine($"SongManager: Built song list from database. {newRootNodes.Count} root nodes created");
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

            // First group charts by song Title+Artist to deduplicate songs
            var songGroups = charts
                .GroupBy(item => new { item.song.Title, item.song.Artist })
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
            var directory = new DirectoryInfo(directoryPath);

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

                // Check for box.def (folder metadata)
                BoxDefinition? boxDef = null;
                var boxDefPath = Path.Combine(directoryPath, "box.def");
                if (File.Exists(boxDefPath))
                {
                    boxDef = await ParseBoxDefinitionAsync(boxDefPath, cancellationToken);
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

                // Group song nodes by song (title + artist) to handle multi-chart songs
                var groupedSongs = await GroupSongNodesBySong(tempSongNodes);
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
                if (_databaseService == null) return null;

                var (song, chart) = await DTXChartParser.ParseSongEntitiesAsync(filePath);

                if (song == null || chart == null)
                {
                    Debug.WriteLine($"SongManager: Metadata parsing returned null for {filePath}.");
                    return null;
                }

                // Add song to EF Core database
                var songId = await _databaseService.AddSongAsync(song, chart);

                // Reload the complete entity from database to ensure we have all metadata and relationships
                var completeEntities = await _databaseService.GetSongWithChartsAsync(songId);
                if (completeEntities == null)
                {
                    Debug.WriteLine($"SongManager: Failed to reload song from database after saving: {songId}");
                    // Fallback to original entities if reload fails
                    var songNode = SongListNode.CreateSongNode(song, chart);
                    songNode.Parent = parent;
                    songNode.DatabaseSongId = songId;
                    return songNode;
                }

                // Create song node using complete database entities (like the database load path)
                var completeNode = CreateSongNodeFromDatabaseEntities(completeEntities.Value.song, completeEntities.Value.charts);
                if (completeNode != null)
                {
                    completeNode.Parent = parent;
                    completeNode.DatabaseSongId = songId;
                    return completeNode;
                }

                // Final fallback if CreateSongNodeFromDatabaseEntities fails
                var fallbackNode = SongListNode.CreateSongNode(song, chart);
                fallbackNode.Parent = parent;
                fallbackNode.DatabaseSongId = songId;
                return fallbackNode;
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
                var sb = new StringBuilder(line.Length);
                
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
                        if (upperCommand.Length > 1 && char.IsDigit(upperCommand[1]))
                        {
                            if (upperCommand.EndsWith("LABEL") || upperCommand.EndsWith("FILE"))
                            {
                                return $"#{upperCommand} {value}";
                            }
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
            
            // Use StringBuilder for efficient string building
            var sb = new StringBuilder(line.Length);
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

        private async Task<List<SongListNode>> ParseSetDefinitionAsync(string setDefPath, SongListNode? parent, CancellationToken cancellationToken)
        {
            var results = new List<SongListNode>();
            var directory = Path.GetDirectoryName(setDefPath) ?? "";

            try
            {
                // Try different encodings for Japanese text support
                var encodings = new List<Encoding>
                {
                    Encoding.UTF8,
                    Encoding.Default
                };

                // Try to add Shift_JIS if available
                try
                {
                    encodings.Add(Encoding.GetEncoding("Shift_JIS"));
                }
                catch (ArgumentException)
                {
                    Debug.WriteLine("SongManager: Shift_JIS encoding not available for SET.def parsing");
                }

                string[]? lines = null;
                foreach (var encoding in encodings)
                {
                    try
                    {
                        lines = await File.ReadAllLinesAsync(setDefPath, encoding, cancellationToken);
                        break; // Success, use this encoding
                    }
                    catch (Exception ex)
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
                            else if (command.StartsWith("L") && command.EndsWith("LABEL"))
                            {
                                // Extract level number (L1LABEL -> 1)
                                if (int.TryParse(command.Substring(1, command.Length - 6), out int level))
                                {
                                    if (!difficulties.ContainsKey(level))
                                        difficulties[level] = ("", "");
                                    difficulties[level] = (value, difficulties[level].file);
                                }
                            }
                            else if (command.StartsWith("L") && command.EndsWith("FILE"))
                            {
                                // Extract level number (L1FILE -> 1)
                                if (int.TryParse(command.Substring(1, command.Length - 5), out int level))
                                {
                                    if (!difficulties.ContainsKey(level))
                                        difficulties[level] = ("", "");
                                    difficulties[level] = (difficulties[level].label, value);
                                }
                            }
                        }
                    }
                }

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
                        var (label, fileName) = kvp.Value;
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

                                    // Add each difficulty to EF Core database if we have the database service
                                    if (_databaseService != null)
                                    {
                                        try
                                        {
                                            var songId = await _databaseService.AddSongAsync(diffSong, diffChart);
                                            currentSong.DatabaseSongId = songId;
                                            
                                            // Update the current song's database entities to reflect the latest data
                                            if (scoreIndex == 0)
                                            {
                                                // For the first chart, update the node's entities to match what's in the database
                                                currentSong.DatabaseSong = diffSong;
                                                currentSong.DatabaseChart = diffChart;
                                            }

                                            // Populate the Score entry for this difficulty in the set.def node
                                            if (scoreIndex < currentSong.Scores.Length)
                                            {
                                                currentSong.DifficultyLabels[scoreIndex] = !string.IsNullOrEmpty(label) ? label : $"Level {level}";

                                                // Create a score entry for the primary instrument found in the chart
                                                var primaryInstrument = DTXMania.Game.Lib.Song.Entities.EInstrumentPart.DRUMS; // Default
                                                int difficultyLevel = 50; // Default

                                                if (diffChart.HasDrumChart && diffChart.DrumLevel > 0)
                                                {
                                                    primaryInstrument = DTXMania.Game.Lib.Song.Entities.EInstrumentPart.DRUMS;
                                                    difficultyLevel = diffChart.DrumLevel;
                                                }
                                                else if (diffChart.HasGuitarChart && diffChart.GuitarLevel > 0)
                                                {
                                                    primaryInstrument = DTXMania.Game.Lib.Song.Entities.EInstrumentPart.GUITAR;
                                                    difficultyLevel = diffChart.GuitarLevel;
                                                }
                                                else if (diffChart.HasBassChart && diffChart.BassLevel > 0)
                                                {
                                                    primaryInstrument = DTXMania.Game.Lib.Song.Entities.EInstrumentPart.BASS;
                                                    difficultyLevel = diffChart.BassLevel;
                                                }

                                                currentSong.Scores[scoreIndex] = new DTXMania.Game.Lib.Song.Entities.SongScore
                                                {
                                                    Instrument = primaryInstrument,
                                                    DifficultyLevel = difficultyLevel,
                                                    DifficultyLabel = !string.IsNullOrEmpty(label) ? label : $"Level {level}"
                                                };
                                                scoreIndex++;
                                            }

                                            DiscoveredScoreCount++;
                                        }
                                        catch (Exception ex)
                                        {
                                            Debug.WriteLine($"SongManager: Error adding song to database: {ex.Message}");
                                        }
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

                foreach (var line in lines)
                {
                    var trimmedLine = line.Trim();
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
            if (_databaseService == null) return new List<SongScoreEntity>();

            try
            {
                return await _databaseService.GetTopScoresAsync(instrument, limit);
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

        #endregion

        #region Helper Methods

        /// <summary>
        /// Finds songs by search term
        /// </summary>
        public async Task<List<SongEntity>> FindSongsBySearchAsync(string searchTerm)
        {
            if (_databaseService == null) return new List<SongEntity>();

            try
            {
                return await _databaseService.SearchSongsAsync(searchTerm);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SongManager: Error searching songs: {ex.Message}");
                return new List<SongEntity>();
            }
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

                foreach (var song in allSongs)
                {
                    if (song.Charts != null)
                    {
                        foreach (var chart in song.Charts)
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
                    // Add default search paths
                    searchPaths.Add("DTXFiles");
                    
                    // You could extend this to get search paths from config if needed
                }

                // Search for the file in all DTX directories within search paths
                foreach (var searchPath in searchPaths)
                {
                    if (!Directory.Exists(searchPath))
                        continue;

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
                foreach (var searchPath in searchPaths)
                {
                    if (string.IsNullOrEmpty(searchPath) || !Directory.Exists(searchPath))
                        continue;

                    // Use async enumeration to avoid blocking
                    await Task.Run(() =>
                    {
                        int pathCount = 0;
                        foreach (var dtxFile in Directory.EnumerateFiles(searchPath, "*.dtx", SearchOption.AllDirectories))
                        {
                            pathCount++;
                        }
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

                // Create the song node using the first chart
                var songNode = SongListNode.CreateSongNode(song, primaryChart);

                // If there are multiple charts, populate the difficulties
                if (charts.Length > 1)
                {
                    int scoreIndex = 0;
                    foreach (var chart in charts.Take(5)) // Limit to 5 difficulties
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

                        songNode.Scores[scoreIndex] = new DTXMania.Game.Lib.Song.Entities.SongScore
                        {
                            Instrument = primaryInstrument,
                            DifficultyLevel = difficultyLevel,
                            DifficultyLabel = $"Level {scoreIndex + 1}"
                        };

                        songNode.DifficultyLabels[scoreIndex] = $"Level {scoreIndex + 1}";
                        scoreIndex++;
                    }
                }

                return songNode;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SongManager: Error creating song node from database entities: {ex.Message}");
                return null;
            }
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

        /// <summary>
        /// Groups song nodes by song (title + artist) and creates unified nodes with all charts
        /// This ensures the fresh scan behaves like the database load path
        /// </summary>
        private async Task<List<SongListNode>> GroupSongNodesBySong(List<SongListNode> songNodes)
        {
            Debug.WriteLine($"=== GroupSongNodesBySong DEBUG ===");
            Debug.WriteLine($"Input songNodes.Count: {songNodes.Count}");
            
            if (_databaseService == null)
            {
                Debug.WriteLine($"Database service is null, returning original nodes");
                return songNodes;
            }

            var groupedResults = new List<SongListNode>();

            try
            {
                // Group by song (Title + Artist)
                var songGroups = songNodes
                    .Where(node => node.DatabaseSong != null)
                    .GroupBy(node => new { node.DatabaseSong.Title, node.DatabaseSong.Artist })
                    .ToList();

                Debug.WriteLine($"Created {songGroups.Count} song groups");

                foreach (var songGroup in songGroups)
                {
                    var songNodes_InGroup = songGroup.ToList();
                    Debug.WriteLine($"Processing group: '{songGroup.Key.Title}' by '{songGroup.Key.Artist}' with {songNodes_InGroup.Count} charts");
                    
                    if (songNodes_InGroup.Count == 1)
                    {
                        // Single chart song, use as-is
                        groupedResults.Add(songNodes_InGroup[0]);
                    }
                    else
                    {
                        // Multi-chart song, create unified node using database entities
                        var firstNode = songNodes_InGroup[0];
                        var songId = firstNode.DatabaseSongId;

                        if (songId.HasValue)
                        {
                            // Get complete song with all charts from database
                            var completeEntities = await _databaseService.GetSongWithChartsAsync(songId.Value);
                            if (completeEntities.HasValue)
                            {
                                // Create unified song node (like database load path)
                                var unifiedNode = CreateSongNodeFromDatabaseEntities(
                                    completeEntities.Value.song, 
                                    completeEntities.Value.charts);
                                
                                if (unifiedNode != null)
                                {
                                    unifiedNode.Parent = firstNode.Parent;
                                    unifiedNode.DatabaseSongId = songId;
                                    groupedResults.Add(unifiedNode);
                                    
                                    Debug.WriteLine($"SongManager: Grouped {songNodes_InGroup.Count} charts into unified song: {firstNode.DatabaseSong.Title}");
                                    continue;
                                }
                            }
                        }

                        // Fallback: if grouping fails, add the first node
                        Debug.WriteLine($"SongManager: Failed to group charts for song: {firstNode.DatabaseSong?.Title}, using first chart as fallback");
                        groupedResults.Add(firstNode);
                    }
                }

                Debug.WriteLine($"SongManager: Grouped {songNodes.Count} individual nodes into {groupedResults.Count} unified songs");
                return groupedResults;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SongManager: Error grouping song nodes: {ex.Message}");
                // Fallback to original nodes if grouping fails
                return songNodes;
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
}
