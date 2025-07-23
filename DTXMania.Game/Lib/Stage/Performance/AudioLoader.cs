using System;
using System.IO;
using System.Threading.Tasks;
using DTX.Resources;
using Microsoft.Xna.Framework.Audio;

namespace DTX.Stage.Performance
{
    /// <summary>
    /// Handles loading and managing background music for gameplay
    /// Provides async loading and creates SongTimer instances for precise timing
    /// </summary>
    public class AudioLoader : IDisposable
    {
        #region Private Fields

        private readonly IResourceManager _resourceManager;
        private ISound _loadedSound;
        private bool _disposed = false;

        #endregion

        #region Properties

        /// <summary>
        /// Whether audio is currently loaded and ready
        /// </summary>
        public bool IsLoaded => _loadedSound != null && !_disposed;

        /// <summary>
        /// Path to the currently loaded audio file
        /// </summary>
        public string LoadedAudioPath { get; private set; } = "";

        #endregion

        #region Constructors

        /// <summary>
        /// Creates a new AudioLoader with the specified resource manager
        /// </summary>
        /// <param name="resourceManager">Resource manager for loading audio files</param>
        public AudioLoader(IResourceManager resourceManager)
        {
            _resourceManager = resourceManager ?? throw new ArgumentNullException(nameof(resourceManager));
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Loads background music asynchronously
        /// </summary>
        /// <param name="audioPath">Path to the audio file (absolute or relative to DTX file)</param>
        /// <returns>Task that completes when loading is finished</returns>
        public async Task LoadBackgroundMusicAsync(string audioPath)
        {
            if (string.IsNullOrEmpty(audioPath))
                throw new ArgumentException("Audio path cannot be null or empty", nameof(audioPath));

            if (_disposed)
                throw new ObjectDisposedException(nameof(AudioLoader));

            // Dispose previous sound if any
            UnloadCurrentSound();

            try
            {
                // Validate file exists
                if (!File.Exists(audioPath))
                {
                    throw new FileNotFoundException($"Audio file not found: {audioPath}");
                }

                // Load the sound using ResourceManager
                // Note: ResourceManager.LoadSound is typically synchronous, but we wrap it in Task.Run
                // for consistency with async pattern and to avoid blocking the UI thread
                _loadedSound = await Task.Run(() => _resourceManager.LoadSound(audioPath));
                LoadedAudioPath = audioPath;

                System.Diagnostics.Debug.WriteLine($"AudioLoader: Successfully loaded audio: {Path.GetFileName(audioPath)}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AudioLoader: Failed to load audio {audioPath}: {ex.Message}");
                UnloadCurrentSound();
                throw new InvalidOperationException($"Failed to load background music: {audioPath}", ex);
            }
        }

        /// <summary>
        /// Creates a SongTimer instance for the loaded audio
        /// </summary>
        /// <returns>SongTimer instance, or null if no audio is loaded</returns>
        public SongTimer CreateSongTimer()
        {
            if (!IsLoaded)
            {
                System.Diagnostics.Debug.WriteLine("AudioLoader: Cannot create SongTimer - no audio loaded");
                return null;
            }

            try
            {
                var soundInstance = _loadedSound.CreateInstance();
                if (soundInstance == null)
                {
                    System.Diagnostics.Debug.WriteLine("AudioLoader: Failed to create sound instance");
                    return null;
                }

                var songTimer = new SongTimer(soundInstance);
                System.Diagnostics.Debug.WriteLine($"AudioLoader: Created SongTimer for {Path.GetFileName(LoadedAudioPath)}");
                return songTimer;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AudioLoader: Failed to create SongTimer: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Preloads audio for a specific chart
        /// </summary>
        /// <param name="chartPath">Path to the DTX chart file</param>
        /// <param name="backgroundAudioPath">Path to the background audio (from ParsedChart)</param>
        /// <returns>Task that completes when preloading is finished</returns>
        public async Task PreloadForChartAsync(string chartPath, string backgroundAudioPath)
        {
            if (string.IsNullOrEmpty(backgroundAudioPath))
            {
                System.Diagnostics.Debug.WriteLine("AudioLoader: No background audio specified for chart");
                return;
            }

            string resolvedPath;

            // If the background audio path is already absolute, use it directly
            if (Path.IsPathRooted(backgroundAudioPath))
            {
                resolvedPath = backgroundAudioPath;
            }
            else
            {
                // Make path relative to the chart file location
                var chartDirectory = Path.GetDirectoryName(chartPath) ?? "";
                resolvedPath = Path.Combine(chartDirectory, backgroundAudioPath);
            }

            await LoadBackgroundMusicAsync(resolvedPath);
        }

        /// <summary>
        /// Unloads the currently loaded audio
        /// </summary>
        public void UnloadCurrentSound()
        {
            if (_loadedSound != null)
            {
                _loadedSound.RemoveReference();
                _loadedSound = null;
                LoadedAudioPath = "";
                System.Diagnostics.Debug.WriteLine("AudioLoader: Unloaded current sound");
            }
        }

        /// <summary>
        /// Gets information about the loaded audio
        /// </summary>
        /// <returns>Audio information, or null if no audio is loaded</returns>
        public AudioInfo GetAudioInfo()
        {
            if (!IsLoaded)
                return null;

            return new AudioInfo
            {
                FilePath = LoadedAudioPath,
                FileName = Path.GetFileName(LoadedAudioPath),
                FileSize = File.Exists(LoadedAudioPath) ? new FileInfo(LoadedAudioPath).Length : 0,
                IsLoaded = true
            };
        }

        #endregion

        #region IDisposable Implementation

        /// <summary>
        /// Disposes the AudioLoader and releases resources
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Protected dispose method
        /// </summary>
        /// <param name="disposing">Whether disposing from Dispose() call</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                UnloadCurrentSound();
                _disposed = true;
            }
        }

        #endregion
    }

    /// <summary>
    /// Information about loaded audio
    /// </summary>
    public class AudioInfo
    {
        public string FilePath { get; set; } = "";
        public string FileName { get; set; } = "";
        public long FileSize { get; set; }
        public bool IsLoaded { get; set; }

        public override string ToString()
        {
            return $"Audio: {FileName} ({FileSize / 1024}KB) - {(IsLoaded ? "Loaded" : "Not Loaded")}";
        }
    }
}
