using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Diagnostics;
using DTX.Song;

namespace DTX.Services
{
    /// <summary>
    /// Service for enumerating and discovering song files
    /// Based on DTXManiaNX CEnumSongs patterns
    ///
    /// NOTE: This service is being superseded by the new SongManager class
    /// which provides more comprehensive song management with metadata parsing
    /// and hierarchical organization. This class remains for compatibility.
    /// </summary>
    public class SongEnumerationService
    {
        #region Private Fields

        private readonly string[] _supportedExtensions = { ".dtx", ".gda", ".g2d", ".bms", ".bme", ".bml" };
        private readonly List<SongInfo> _discoveredSongs = new();
        private bool _isEnumerating = false;

        #endregion

        #region Public Properties

        /// <summary>
        /// Gets whether song enumeration is currently in progress
        /// </summary>
        public bool IsEnumerating => _isEnumerating;

        /// <summary>
        /// Gets the list of discovered songs
        /// </summary>
        public IReadOnlyList<SongInfo> DiscoveredSongs => _discoveredSongs.AsReadOnly();

        /// <summary>
        /// Event fired when a song is discovered during enumeration
        /// </summary>
        public event EventHandler<SongDiscoveredEventArgs>? SongDiscovered;

        /// <summary>
        /// Event fired when enumeration is complete
        /// </summary>
        public event EventHandler<EnumerationCompleteEventArgs>? EnumerationComplete;

        #endregion

        #region Public Methods

        /// <summary>
        /// Start enumerating songs in the specified directories
        /// </summary>
        /// <param name="searchPaths">Paths to search for songs</param>
        /// <returns>Task representing the enumeration operation</returns>
        public async Task<int> EnumerateSongsAsync(params string[] searchPaths)
        {
            if (_isEnumerating)
            {
                Debug.WriteLine("SongEnumerationService: Enumeration already in progress");
                return _discoveredSongs.Count;
            }

            _isEnumerating = true;
            _discoveredSongs.Clear();

            try
            {
                Debug.WriteLine($"SongEnumerationService: Starting enumeration of {searchPaths.Length} paths");

                foreach (var searchPath in searchPaths)
                {
                    if (string.IsNullOrEmpty(searchPath))
                        continue;

                    await EnumeratePathAsync(searchPath);
                }

                Debug.WriteLine($"SongEnumerationService: Enumeration complete. Found {_discoveredSongs.Count} songs");
                EnumerationComplete?.Invoke(this, new EnumerationCompleteEventArgs(_discoveredSongs.Count));

                return _discoveredSongs.Count;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SongEnumerationService: Error during enumeration: {ex.Message}");
                return _discoveredSongs.Count;
            }
            finally
            {
                _isEnumerating = false;
            }
        }

        /// <summary>
        /// Get songs by directory
        /// </summary>
        /// <param name="directoryPath">Directory to filter by</param>
        /// <returns>Songs in the specified directory</returns>
        public IEnumerable<SongInfo> GetSongsByDirectory(string directoryPath)
        {
            return _discoveredSongs.Where(s => 
                s.DirectoryPath.Equals(directoryPath, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Clear all discovered songs
        /// </summary>
        public void Clear()
        {
            _discoveredSongs.Clear();
        }

        #endregion

        #region Private Methods

        private async Task EnumeratePathAsync(string searchPath)
        {
            try
            {
                var fullPath = Path.GetFullPath(searchPath);
                if (!Directory.Exists(fullPath))
                {
                    Debug.WriteLine($"SongEnumerationService: Directory not found: {fullPath}");
                    return;
                }

                Debug.WriteLine($"SongEnumerationService: Searching in {fullPath}");

                // Search recursively for song files
                await Task.Run(() => SearchDirectory(fullPath));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SongEnumerationService: Error searching {searchPath}: {ex.Message}");
            }
        }

        private void SearchDirectory(string directoryPath)
        {
            try
            {
                // Get all files in current directory
                var files = Directory.GetFiles(directoryPath, "*.*", SearchOption.TopDirectoryOnly)
                    .Where(f => _supportedExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()));

                foreach (var file in files)
                {
                    var songInfo = CreateSongInfo(file);
                    if (songInfo != null)
                    {
                        _discoveredSongs.Add(songInfo);
                        SongDiscovered?.Invoke(this, new SongDiscoveredEventArgs(songInfo));
                    }
                }

                // Search subdirectories
                var subdirectories = Directory.GetDirectories(directoryPath);
                foreach (var subdirectory in subdirectories)
                {
                    SearchDirectory(subdirectory);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SongEnumerationService: Error searching directory {directoryPath}: {ex.Message}");
            }
        }

        private SongInfo? CreateSongInfo(string filePath)
        {
            try
            {
                var fileInfo = new FileInfo(filePath);
                return new SongInfo
                {
                    FilePath = filePath,
                    FileName = fileInfo.Name,
                    DirectoryPath = fileInfo.DirectoryName ?? "",
                    Extension = fileInfo.Extension.ToLowerInvariant(),
                    SizeBytes = fileInfo.Length,
                    LastModified = fileInfo.LastWriteTime,
                    Title = Path.GetFileNameWithoutExtension(filePath), // Basic title extraction
                    Artist = "Unknown", // Would need to parse file for actual metadata
                    Genre = "Unknown"
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SongEnumerationService: Error creating song info for {filePath}: {ex.Message}");
                return null;
            }
        }

        #endregion
    }

    #region Supporting Classes

    /// <summary>
    /// Information about a discovered song file
    /// </summary>
    public class SongInfo
    {
        public string FilePath { get; set; } = "";
        public string FileName { get; set; } = "";
        public string DirectoryPath { get; set; } = "";
        public string Extension { get; set; } = "";
        public long SizeBytes { get; set; }
        public DateTime LastModified { get; set; }
        public string Title { get; set; } = "";
        public string Artist { get; set; } = "";
        public string Genre { get; set; } = "";
    }

    /// <summary>
    /// Event args for song discovered event
    /// </summary>
    public class SongDiscoveredEventArgs : EventArgs
    {
        public SongInfo Song { get; }

        public SongDiscoveredEventArgs(SongInfo song)
        {
            Song = song;
        }
    }

    /// <summary>
    /// Event args for enumeration complete event
    /// </summary>
    public class EnumerationCompleteEventArgs : EventArgs
    {
        public int TotalSongsFound { get; }

        public EnumerationCompleteEventArgs(int totalSongsFound)
        {
            TotalSongsFound = totalSongsFound;
        }
    }

    #endregion
}
