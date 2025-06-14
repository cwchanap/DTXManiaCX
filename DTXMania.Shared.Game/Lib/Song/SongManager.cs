using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Text.Json;

#nullable enable

namespace DTX.Song
{
    /// <summary>
    /// Song database management and enumeration (Singleton)
    /// Based on DTXManiaNX CSongManager patterns
    /// Responsible for centralized song data management and enumeration
    /// </summary>
    public sealed class SongManager
    {
        #region Singleton Implementation

        private static SongManager? _instance;
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
                        _instance ??= new SongManager();
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

        private readonly List<SongScore> _songsDatabase = new();
        private readonly List<SongListNode> _rootSongs = new();
        private readonly DTXMetadataParser _metadataParser = new();
        private readonly object _lockObject = new();
        private CancellationTokenSource? _enumCancellation;

        // Initialization state tracking
        private bool _isInitialized = false;

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

        #region Initialization and Database Management

        /// <summary>
        /// Initializes the SongManager with song data and marks it as initialized
        /// Should only be called once during application startup
        /// </summary>
        public async Task<bool> InitializeAsync(string[] searchPaths, string databasePath = "songs.db", IProgress<EnumerationProgress>? progress = null)
        {
            lock (_lockObject)
            {
                if (_isInitialized)
                {
                    Debug.WriteLine("SongManager: Already initialized");
                    return true;
                }
            }

            try
            {
                // First try to load from cached database
                var loaded = await LoadSongsDatabaseAsync(databasePath);
                
                // Check if enumeration is needed
                bool needsEnumeration = !loaded || DatabaseScoreCount == 0 || await NeedsEnumerationAsync(searchPaths);
                
                if (needsEnumeration)
                {
                    Debug.WriteLine("SongManager: Starting song enumeration during initialization");
                    await EnumerateSongsAsync(searchPaths, progress);
                    
                    // Save the database after enumeration
                    await SaveSongsDatabaseAsync(databasePath);
                }

                lock (_lockObject)
                {
                    _isInitialized = true;
                }

                Debug.WriteLine($"SongManager: Initialization complete. {DatabaseScoreCount} songs loaded.");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SongManager: Error during initialization: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Loads the songs database from file
        /// </summary>
        public async Task<bool> LoadSongsDatabaseAsync(string databasePath)
        {
            try
            {
                if (!File.Exists(databasePath))
                {
                    Debug.WriteLine($"SongManager: Database file {databasePath} does not exist");
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
        /// Should primarily be called during initialization
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
                foreach (var subDir in directory.GetDirectories())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // Check if this is a DTXFiles.* prefixed folder (should be NodeType.Box)
                    var isDTXFilesFolder = subDir.Name.StartsWith("DTXFiles.", StringComparison.OrdinalIgnoreCase);

                    // Check for box.def in the subdirectory itself
                    BoxDefinition? subDirBoxDef = null;
                    var subDirBoxDefPath = Path.Combine(subDir.FullName, "box.def");
                    var hasBoxDef = File.Exists(subDirBoxDefPath);
                    if (hasBoxDef)
                    {
                        subDirBoxDef = await ParseBoxDefinitionAsync(subDirBoxDefPath, cancellationToken);
                    }

                    // Determine if this should be a BOX (folder container) or treated as individual songs
                    if (isDTXFilesFolder || hasBoxDef)
                    {
                        // This is a BOX folder (DTXFiles.* prefix or has box.def)
                        var boxNode = CreateBoxNodeFromDirectory(subDir, parent, subDirBoxDef);
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
                    else
                    {
                        // This is a regular song folder - treat contents as individual songs
                        var children = await EnumerateDirectoryAsync(subDir.FullName, parent, progress, cancellationToken);
                        results.AddRange(children);
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

        // Include all the other parsing methods from the original file
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

                    // Parse SET.def commands
                    if (trimmedLine.StartsWith("#"))
                    {
                        var parts = trimmedLine.Split(new char[] { ' ', '\t' }, 2, StringSplitOptions.RemoveEmptyEntries);
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

                // Create song node if we have a title and difficulties
                if (!string.IsNullOrEmpty(songTitle) && difficulties.Count > 0)
                {
                    var metadata = new SongMetadata
                    {
                        Title = songTitle,
                        FilePath = setDefPath
                    };
                    currentSong = SongListNode.CreateSongNode(metadata);
                    currentSong.Parent = parent;

                    // Process each difficulty
                    foreach (var kvp in difficulties.OrderBy(d => d.Key))
                    {
                        var level = kvp.Key;
                        var (label, fileName) = kvp.Value;

                        if (!string.IsNullOrEmpty(fileName))
                        {
                            var filePath = Path.Combine(directory, fileName);
                            if (File.Exists(filePath) && DTXMetadataParser.IsSupportedFile(filePath))
                            {
                                var difficultyMetadata = await _metadataParser.ParseMetadataAsync(filePath);

                                // Use the set.def title if the file doesn't have one
                                if (string.IsNullOrEmpty(difficultyMetadata.Title))
                                {
                                    difficultyMetadata.Title = songTitle;
                                }

                                var score = new SongScore
                                {
                                    Metadata = difficultyMetadata,
                                    Instrument = "DRUMS", // Default to drums for DTX files
                                    DifficultyLevel = difficultyMetadata.DrumLevel ?? level,
                                    DifficultyLabel = !string.IsNullOrEmpty(label) ? label : $"Level {level}"
                                };
                                currentSong.SetScore(level - 1, score); // Convert to 0-based index

                                // Add to database
                                lock (_lockObject)
                                {
                                    _songsDatabase.Add(score);
                                }

                                DiscoveredScoreCount++;
                            }
                        }
                    }

                    if (currentSong.AvailableDifficulties > 0)
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
        /// Checks if enumeration is needed based on directory modification times
        /// </summary>
        public async Task<bool> NeedsEnumerationAsync(string[] searchPaths)
        {
            try
            {
                // Check if database exists
                var databasePath = "songs.db";
                if (!File.Exists(databasePath))
                    return true;

                // Get database last modified time
                var dbLastModified = File.GetLastWriteTime(databasePath);

                // Check if any search path has been modified since database was created
                foreach (var searchPath in searchPaths)
                {
                    if (Directory.Exists(searchPath))
                    {
                        var dirLastModified = Directory.GetLastWriteTime(searchPath);
                        if (dirLastModified > dbLastModified)
                        {
                            return true;
                        }

                        // Check subdirectories recursively (but limit depth for performance)
                        if (await HasRecentChangesAsync(searchPath, dbLastModified, 3))
                        {
                            return true;
                        }
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SongManager: Error checking enumeration need: {ex.Message}");
                return true; // Default to enumeration if we can't determine
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
                _isInitialized = false;
            }
            DiscoveredScoreCount = 0;
            EnumeratedFileCount = 0;
        }

        private async Task<bool> HasRecentChangesAsync(string directoryPath, DateTime lastCheck, int maxDepth)
        {
            if (maxDepth <= 0)
                return false;

            try
            {
                var directory = new DirectoryInfo(directoryPath);

                // Check if directory itself was modified
                if (directory.LastWriteTime > lastCheck)
                    return true;

                // Check files in directory
                foreach (var file in directory.GetFiles())
                {
                    if (file.LastWriteTime > lastCheck)
                    {
                        return true;
                    }
                }

                // Check subdirectories
                foreach (var subDir in directory.GetDirectories())
                {
                    if (await HasRecentChangesAsync(subDir.FullName, lastCheck, maxDepth - 1))
                        return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SongManager: Error checking directory changes {directoryPath}: {ex.Message}");
                return true; // Assume changes if we can't check
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
