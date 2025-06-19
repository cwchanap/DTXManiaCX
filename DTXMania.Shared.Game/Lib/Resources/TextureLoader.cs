using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using DTX.Utilities;

namespace DTX.Resources
{
    /// <summary>
    /// Asynchronous texture loader with placeholder support
    /// Provides non-blocking texture loading for smooth UI navigation
    /// </summary>
    public class TextureLoader : IDisposable
    {
        #region Constants

        private const int MAX_CONCURRENT_LOADS = 4;
        private const int PLACEHOLDER_SIZE = 64;

        #endregion

        #region Private Fields

        private readonly IResourceManager _resourceManager;
        private readonly GraphicsDevice _graphicsDevice;
        private readonly ConcurrentDictionary<string, Task<ITexture>> _loadingTasks;
        private readonly ConcurrentDictionary<string, ITexture> _textureCache;
        private readonly SemaphoreSlim _loadingSemaphore;
        private ITexture _placeholderTexture;
        private bool _disposed = false;

        #endregion

        #region Constructor

        /// <summary>
        /// Initialize texture loader with resource manager and graphics device
        /// </summary>
        public TextureLoader(IResourceManager resourceManager, GraphicsDevice graphicsDevice)
        {
            _resourceManager = resourceManager ?? throw new ArgumentNullException(nameof(resourceManager));
            _graphicsDevice = graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
            
            _loadingTasks = new ConcurrentDictionary<string, Task<ITexture>>();
            _textureCache = new ConcurrentDictionary<string, ITexture>();
            _loadingSemaphore = new SemaphoreSlim(MAX_CONCURRENT_LOADS, MAX_CONCURRENT_LOADS);
            
            CreatePlaceholderTexture();
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Load texture asynchronously with immediate placeholder return
        /// Returns placeholder immediately, then replaces with actual texture when loaded
        /// </summary>
        /// <param name="path">Path to texture file</param>
        /// <param name="enableTransparency">Whether to enable transparency</param>
        /// <returns>Placeholder texture immediately, actual texture loaded asynchronously</returns>
        public Task<ITexture> LoadTextureAsync(string path, bool enableTransparency = false)
        {
            if (string.IsNullOrEmpty(path))
            {
                _placeholderTexture?.AddReference();
                return Task.FromResult(_placeholderTexture);
            }

            var cacheKey = $"{path}|{enableTransparency}";

            // Return cached texture if available
            if (_textureCache.TryGetValue(cacheKey, out var cachedTexture))
            {
                cachedTexture?.AddReference();
                return Task.FromResult(cachedTexture);
            }

            // Get or create the loading task
            var loadingTask = _loadingTasks.GetOrAdd(cacheKey, key =>
            {
                var task = LoadTextureInternalAsync(path, enableTransparency, key);
                task.ContinueWith(t =>
                {
                    _loadingTasks.TryRemove(key, out _);
                    if (t.IsFaulted)
                    {
                        System.Diagnostics.Debug.WriteLine($"TextureLoader: Failed to load texture '{path}'. {t.Exception?.InnerException?.Message}");
                    }
                }, TaskScheduler.Default);
                return task;
            });

            // Return a continuation that provides the placeholder on failure.
            return loadingTask.ContinueWith(t =>
            {
                if (t.IsCompletedSuccessfully)
                {
                    return t.Result;
                }
                else
                {
                    _placeholderTexture?.AddReference();
                    return _placeholderTexture;
                }
            }, TaskScheduler.Default);
        }

        /// <summary>
        /// Get placeholder texture for immediate display
        /// </summary>
        public ITexture GetPlaceholder()
        {
            _placeholderTexture?.AddReference();
            return _placeholderTexture;
        }

        /// <summary>
        /// Pre-load textures for specified range of song indices
        /// Used for smooth scrolling experience
        /// </summary>
        /// <param name="songNodes">List of song nodes</param>
        /// <param name="centerIndex">Current center index</param>
        /// <param name="preloadRange">Range around center to preload (default: 3)</param>
        public void PreloadTextures(System.Collections.Generic.IList<DTX.Song.SongListNode> songNodes, 
                                   int centerIndex, int preloadRange = 3)
        {
            if (songNodes == null || songNodes.Count == 0)
                return;

            var startIndex = Math.Max(0, centerIndex - preloadRange);
            var endIndex = Math.Min(songNodes.Count - 1, centerIndex + preloadRange);

            for (int i = startIndex; i <= endIndex; i++)
            {
                var node = songNodes[i];
                if (node?.Metadata?.PreviewImage != null)
                {
                    // Start loading asynchronously without waiting
                    _ = LoadTextureAsync(GetPreviewImagePath(node), false);
                }
            }
        }

        /// <summary>
        /// Clear texture cache and cancel pending loads
        /// </summary>
        public void ClearCache()
        {
            // Dispose cached textures
            foreach (var texture in _textureCache.Values)
            {
                texture?.RemoveReference();
            }
            _textureCache.Clear();

            // Clear loading tasks (they will complete or be cancelled naturally)
            _loadingTasks.Clear();
        }

        #endregion

        #region Private Methods

        private async Task<ITexture> LoadTextureInternalAsync(string path, bool enableTransparency, string cacheKey)
        {
            await _loadingSemaphore.WaitAsync();
            
            try
            {
                // Double-check cache after acquiring semaphore
                if (_textureCache.TryGetValue(cacheKey, out var cachedTexture))
                {
                    cachedTexture?.AddReference();
                    return cachedTexture;
                }

                // Load texture on background thread
                var texture = await Task.Run(() => _resourceManager.LoadTexture(path, enableTransparency));
                
                if (texture != null)
                {
                    // Cache the loaded texture
                    _textureCache.TryAdd(cacheKey, texture);
                    texture.AddReference();
                    return texture;
                }
                else
                {
                    // Return placeholder if loading failed
                    _placeholderTexture?.AddReference();
                    return _placeholderTexture;
                }
            }
            finally
            {
                _loadingSemaphore.Release();
            }
        }

        private void CreatePlaceholderTexture()
        {
            try
            {
                // Create a simple colored placeholder texture
                var texture2D = new Texture2D(_graphicsDevice, PLACEHOLDER_SIZE, PLACEHOLDER_SIZE);
                var colorData = new Color[PLACEHOLDER_SIZE * PLACEHOLDER_SIZE];
                
                // Create a simple pattern (checkerboard or gradient)
                for (int i = 0; i < colorData.Length; i++)
                {
                    int x = i % PLACEHOLDER_SIZE;
                    int y = i / PLACEHOLDER_SIZE;
                    
                    // Simple checkerboard pattern
                    bool isLight = ((x / 8) + (y / 8)) % 2 == 0;
                    colorData[i] = isLight ? Color.LightGray : Color.DarkGray;
                }
                
                texture2D.SetData(colorData);
                _placeholderTexture = new ManagedTexture(_graphicsDevice, texture2D, "placeholder");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"TextureLoader: Failed to create placeholder texture: {ex.Message}");
            }
        }

        private string GetPreviewImagePath(DTX.Song.SongListNode node)
        {
            if (node?.Metadata?.PreviewImage == null || node.Metadata?.FilePath == null)
                return null;

            var songDirectory = System.IO.Path.GetDirectoryName(node.Metadata.FilePath);
            return System.IO.Path.Combine(songDirectory, node.Metadata.PreviewImage);
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_disposed)
                return;

            ClearCache();
            _placeholderTexture?.Dispose();
            _loadingSemaphore?.Dispose();
            
            _disposed = true;
        }

        #endregion
    }
}
