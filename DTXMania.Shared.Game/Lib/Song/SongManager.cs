using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Text.Json;

namespace DTX.Song
{
    /// <summary>
    /// Song database management and enumeration
    /// Based on DTXManiaNX CSongManager patterns
    /// </summary>
    public class SongManager
    {
        #region Private Fields

        private readonly List<SongScore> _songsDatabase = new();
        private readonly List<SongListNode> _rootSongs = new();
        private readonly DTXMetadataParser _metadataParser = new();
        private readonly object _lockObject = new();
        private CancellationTokenSource? _enumCancellation;

        #endregion

        #region Public Properties

        /// <summary>
        /// Gets the cached song database
        /// </summary>
        public IReadOnlyList<SongScore> SongsDatabase
        {
            get
            {
                lock (_lockObject)
                {
                    return _songsDatabase.AsReadOnly();
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
        public int DatabaseScoreCount
        {
            get
            {
                lock (_lockObject)
                {
                    return _songsDatabase.Count;
                }
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
        public bool IsEnumerating => _enumCancellation != null && !_enumCancellation.Token.IsCancellationRequested;

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

        #region Database Management

        /// <summary>
        /// Loads the songs database from file
        /// </summary>
        public async Task<bool> LoadSongsDatabaseAsync(string databasePath)
        {
            try
            {
                if (!File.Exists(databasePath))
                {
                    Debug.WriteLine($"SongManager: Database file not found: {databasePath}");
                    return false;
                }

                var json = await File.ReadAllTextAsync(databasePath);
                var data = JsonSerializer.Deserialize<SongDatabaseData>(json);

                if (data != null)
                {
                    lock (_lockObject)
                    {
                        _songsDatabase.Clear();
                        _songsDatabase.AddRange(data.Scores);
                        
                        _rootSongs.Clear();
                        _rootSongs.AddRange(data.RootNodes);
                    }

                    Debug.WriteLine($"SongManager: Loaded {_songsDatabase.Count} scores from database");
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SongManager: Error loading database: {ex.Message}");
            }

            return false;
        }

        /// <summary>
        /// Saves the songs database to file
        /// </summary>
        public async Task<bool> SaveSongsDatabaseAsync(string databasePath)
        {
            try
            {
                var data = new SongDatabaseData();
                
                lock (_lockObject)
                {
                    data.Scores.AddRange(_songsDatabase);
                    data.RootNodes.AddRange(_rootSongs);
                }

                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };

                var json = JsonSerializer.Serialize(data, options);
                await File.WriteAllTextAsync(databasePath, json);

                Debug.WriteLine($"SongManager: Saved {data.Scores.Count} scores to database");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SongManager: Error saving database: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region Song Enumeration

        /// <summary>
        /// Enumerates songs from specified search paths
        /// </summary>
        public async Task<int> EnumerateSongsAsync(string[] searchPaths, IProgress<EnumerationProgress>? progress = null)
        {
            if (IsEnumerating)
            {
                Debug.WriteLine("SongManager: Enumeration already in progress");
                return 0;
            }

            _enumCancellation = new CancellationTokenSource();
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

                    var pathNodes = await EnumerateDirectoryAsync(searchPath, null, progress, _enumCancellation.Token);
                    newRootNodes.AddRange(pathNodes);
                }

                // Update root songs list
                lock (_lockObject)
                {
                    _rootSongs.Clear();
                    _rootSongs.AddRange(newRootNodes);
                }

                Debug.WriteLine($"SongManager: Enumeration complete. Found {DiscoveredScoreCount} songs in {newRootNodes.Count} root nodes");

                // Log discovered songs for debugging
                foreach (var rootNode in newRootNodes)
                {
                    LogNodeHierarchy(rootNode, 0);
                }

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
                _enumCancellation?.Dispose();
                _enumCancellation = null;
            }
        }

        /// <summary>
        /// Cancels ongoing enumeration
        /// </summary>
        public void CancelEnumeration()
        {
            _enumCancellation?.Cancel();
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
                    // TODO: Implement set.def parsing in Phase 2
                    Debug.WriteLine($"SongManager: Found set.def in {directoryPath} (not implemented yet)");
                }

                // Check for box.def (folder metadata)
                var boxDefPath = Path.Combine(directoryPath, "box.def");
                if (File.Exists(boxDefPath))
                {
                    // TODO: Implement box.def parsing in Phase 2
                    Debug.WriteLine($"SongManager: Found box.def in {directoryPath} (not implemented yet)");
                }

                // Process subdirectories as BOX folders
                foreach (var subDir in directory.GetDirectories())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var boxNode = SongListNode.CreateBoxNode(subDir.Name, subDir.FullName, parent);
                    var children = await EnumerateDirectoryAsync(subDir.FullName, boxNode, progress, cancellationToken);
                    
                    foreach (var child in children)
                    {
                        boxNode.AddChild(child);
                    }

                    if (boxNode.Children.Count > 0)
                    {
                        results.Add(boxNode);
                    }
                }

                // Process individual song files
                foreach (var file in directory.GetFiles())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (DTXMetadataParser.IsSupportedFile(file.FullName))
                    {
                        var songNode = await CreateSongNodeAsync(file.FullName, parent);
                        if (songNode != null)
                        {
                            results.Add(songNode);
                            DiscoveredScoreCount++;

                            Debug.WriteLine($"SongManager: Discovered song '{songNode.DisplayTitle}' by {songNode.Metadata?.DisplayArtist} ({file.Name})");

                            SongDiscovered?.Invoke(this, new SongDiscoveredEventArgs(songNode));

                            progress?.Report(new EnumerationProgress
                            {
                                CurrentFile = file.Name,
                                ProcessedCount = ++EnumeratedFileCount,
                                DiscoveredSongs = DiscoveredScoreCount
                            });
                        }
                    }
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
                var metadata = await _metadataParser.ParseMetadataAsync(filePath);
                var songNode = SongListNode.CreateSongNode(metadata);
                songNode.Parent = parent;

                // Add scores to database
                lock (_lockObject)
                {
                    foreach (var score in songNode.Scores)
                    {
                        if (score != null)
                        {
                            _songsDatabase.Add(score);
                        }
                    }
                }

                return songNode;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SongManager: Error creating song node for {filePath}: {ex.Message}");
                return null;
            }
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Finds a song by file path
        /// </summary>
        public SongScore? FindSongByPath(string filePath)
        {
            lock (_lockObject)
            {
                return _songsDatabase.FirstOrDefault(s => 
                    s.Metadata.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase));
            }
        }

        /// <summary>
        /// Gets songs by genre
        /// </summary>
        public IEnumerable<SongScore> GetSongsByGenre(string genre)
        {
            lock (_lockObject)
            {
                return _songsDatabase.Where(s => 
                    s.Metadata.Genre.Equals(genre, StringComparison.OrdinalIgnoreCase)).ToList();
            }
        }

        /// <summary>
        /// Clears all data
        /// </summary>
        public void Clear()
        {
            lock (_lockObject)
            {
                _songsDatabase.Clear();
                _rootSongs.Clear();
            }
            DiscoveredScoreCount = 0;
            EnumeratedFileCount = 0;
        }

        /// <summary>
        /// Logs the node hierarchy for debugging
        /// </summary>
        private void LogNodeHierarchy(SongListNode node, int depth)
        {
            var indent = new string(' ', depth * 2);
            var nodeInfo = $"{indent}[{node.Type}] {node.DisplayTitle}";

            if (node.Type == NodeType.Score && node.Metadata != null)
            {
                nodeInfo += $" - {node.Metadata.DisplayArtist} ({node.Metadata.FileFormat})";
                if (node.AvailableDifficulties > 0)
                {
                    nodeInfo += $" [{node.AvailableDifficulties} difficulties]";
                }
            }
            else if (node.Type == NodeType.Box)
            {
                nodeInfo += $" ({node.Children.Count} items)";
            }

            Debug.WriteLine($"SongManager: {nodeInfo}");

            // Recursively log children
            foreach (var child in node.Children)
            {
                LogNodeHierarchy(child, depth + 1);
            }
        }

        #endregion
    }

    #region Supporting Classes

    /// <summary>
    /// Database serialization container
    /// </summary>
    public class SongDatabaseData
    {
        public List<SongScore> Scores { get; set; } = new();
        public List<SongListNode> RootNodes { get; set; } = new();
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
        public string Version { get; set; } = "1.0";
    }

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

    #endregion
}
