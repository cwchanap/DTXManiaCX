using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace DTXMania.Game.Lib.Resources
{
    /// <summary>
    /// Core resource manager implementation for DTXManiaCX
    /// Based on DTXMania's CSkin and resource management patterns
    /// </summary>
    public class ResourceManager : IResourceManager
    {
        #region Private Fields

        private readonly GraphicsDevice _graphicsDevice;
        private readonly ConcurrentDictionary<string, ITexture> _textureCache;
        private readonly ConcurrentDictionary<string, IFont> _fontCache;
        private readonly ConcurrentDictionary<string, ISound> _soundCache;
        private readonly object _lockObject = new object();

        private string _currentSkinPath = "System/";
        private string _fallbackSkinPath = "System/";
        private string _boxDefSkinPath = "";
        private bool _useBoxDefSkin = true;
        private bool _disposed = false;

        // Statistics tracking
        private int _cacheHits = 0;
        private int _cacheMisses = 0;
        private readonly Stopwatch _totalLoadTime = new Stopwatch();

        #endregion

        #region Constructor

        public ResourceManager(GraphicsDevice graphicsDevice)
        {
            _graphicsDevice = graphicsDevice ?? throw new ArgumentNullException(nameof(graphicsDevice));
            _textureCache = new ConcurrentDictionary<string, ITexture>();
            _fontCache = new ConcurrentDictionary<string, IFont>();
            _soundCache = new ConcurrentDictionary<string, ISound>();

            // Initialize default skin path
            InitializeDefaultSkinPath();
        }

        #endregion

        #region IResourceManager Implementation

        public ITexture LoadTexture(string path)
        {
            return LoadTexture(path, false);
        }

        public ITexture LoadTexture(string path, bool enableTransparency)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentException("Path cannot be null or empty", nameof(path));

            // Normalize path for case-insensitive comparison (Windows compatibility)
            // Don't add directory separator to file paths
            var normalizedPath = NormalizeFilePath(path);
            var cacheKey = $"{normalizedPath}|{enableTransparency}";

            // Check cache first
            if (_textureCache.TryGetValue(cacheKey, out var cachedTexture))
            {
                // Check if the cached texture is disposed
                if (cachedTexture.IsDisposed)
                {
                    _textureCache.TryRemove(cacheKey, out _);
                    // Continue to load a new texture
                }
                else
                {
                    try
                    {
                        cachedTexture.AddReference();
                        Interlocked.Increment(ref _cacheHits);
                        return cachedTexture;
                    }
                    catch (ObjectDisposedException)
                    {
                        _textureCache.TryRemove(cacheKey, out _);
                    }
                }
            }

            Interlocked.Increment(ref _cacheMisses);
            _totalLoadTime.Start();

            try
            {
                // Resolve path using skin system (use original path for file system operations)
                var resolvedPath = ResolvePath(path);

                // Validate file exists
                if (!File.Exists(resolvedPath))
                {
                    // Try fallback skin
                    var fallbackPath = ResolvePathWithSkin(path, _fallbackSkinPath);
                    if (!File.Exists(fallbackPath))
                    {
                        var errorMsg = $"Texture not found: {path} (resolved: {resolvedPath})";
                        OnResourceLoadFailed(new ResourceLoadFailedEventArgs(path,
                            new FileNotFoundException(errorMsg), errorMsg));
                        return CreateFallbackTexture(path);
                    }
                    resolvedPath = fallbackPath;
                }

                // Create texture parameters
                var creationParams = new TextureCreationParams
                {
                    EnableTransparency = enableTransparency,
                    TransparencyColor = Color.Black
                };

                // Load and create texture
                var texture = new ManagedTexture(_graphicsDevice, resolvedPath, path, creationParams);
                texture.AddReference();

                // Cache the texture
                _textureCache.TryAdd(cacheKey, texture);

                return texture;
            }
            catch (Exception ex)
            {
                var errorMsg = $"Failed to load texture: {path}";
                OnResourceLoadFailed(new ResourceLoadFailedEventArgs(path, ex, errorMsg));
                return CreateFallbackTexture(path);
            }
            finally
            {
                _totalLoadTime.Stop();
            }
        }

        public IFont LoadFont(string path, int size)
        {
            return LoadFont(path, size, FontStyle.Regular);
        }

        public IFont LoadFont(string path, int size, FontStyle style)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentException("Path cannot be null or empty", nameof(path));

            // Normalize path for case-insensitive comparison (Windows compatibility)
            // Don't add directory separator to file paths
            var normalizedPath = NormalizeFilePath(path);
            var cacheKey = $"{normalizedPath}|{size}|{style}";

            // Check cache first
            if (_fontCache.TryGetValue(cacheKey, out var cachedFont))
            {
                // Check if the cached font is disposed
                if (cachedFont.IsDisposed)
                {
                    _fontCache.TryRemove(cacheKey, out _);
                    // Continue to load a new font
                }
                else
                {
                    try
                    {
                        cachedFont.AddReference();
                        Interlocked.Increment(ref _cacheHits);
                        return cachedFont;
                    }
                    catch (ObjectDisposedException)
                    {
                        _fontCache.TryRemove(cacheKey, out _);
                    }
                }
            }

            Interlocked.Increment(ref _cacheMisses);
            _totalLoadTime.Start();

            try
            {
                // Create font using ManagedFont factory
                var font = ManagedFont.CreateFont(_graphicsDevice, normalizedPath, size, style);
                font.AddReference();

                // Cache the font
                _fontCache.TryAdd(cacheKey, font);

                return font;
            }
            catch (Exception ex)
            {
                var errorMsg = $"Failed to load font: {path}";
                OnResourceLoadFailed(new ResourceLoadFailedEventArgs(path, ex, errorMsg));
                return CreateFallbackFont(path, size, style);
            }
            finally
            {
                _totalLoadTime.Stop();
            }
        }

        public ISound LoadSound(string path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentException("Path cannot be null or empty", nameof(path));

            // Normalize path for case-insensitive comparison (Windows compatibility)
            var normalizedPath = NormalizeFilePath(path);
            var cacheKey = normalizedPath;

            // Check cache first
            if (_soundCache.TryGetValue(cacheKey, out var cachedSound))
            {
                // Check if the cached sound is disposed
                if (cachedSound.IsDisposed)
                {
                    _soundCache.TryRemove(cacheKey, out _);
                    // Continue to load a new sound
                }
                else
                {
                    try
                    {
                        cachedSound.AddReference();
                        Interlocked.Increment(ref _cacheHits);
                        return cachedSound;
                    }
                    catch (ObjectDisposedException)
                    {
                        _soundCache.TryRemove(cacheKey, out _);
                    }
                }
            }

            Interlocked.Increment(ref _cacheMisses);
            _totalLoadTime.Start();

            try
            {
                // Resolve path using skin system
                var resolvedPath = ResolvePath(path);

                // Validate file exists
                if (!File.Exists(resolvedPath))
                {
                    // Try fallback skin
                    var fallbackPath = ResolvePathWithSkin(path, _fallbackSkinPath);
                    if (!File.Exists(fallbackPath))
                    {
                        var errorMsg = $"Sound not found: {path} (resolved: {resolvedPath})";
                        OnResourceLoadFailed(new ResourceLoadFailedEventArgs(path,
                            new FileNotFoundException(errorMsg), errorMsg));
                        return CreateFallbackSound(path);
                    }
                    resolvedPath = fallbackPath;
                }

                // Load and create sound
                var sound = new ManagedSound(resolvedPath, path);
                sound.AddReference();

                // Cache the sound
                _soundCache.TryAdd(cacheKey, sound);

                return sound;
            }
            catch (Exception ex)
            {
                var errorMsg = $"Failed to load sound: {path}";
                OnResourceLoadFailed(new ResourceLoadFailedEventArgs(path, ex, errorMsg));
                return CreateFallbackSound(path);
            }
            finally
            {
                _totalLoadTime.Stop();
            }
        }

        public ITexture CreateTextureFromColor(Color color)
        {
            var cacheKey = $"__Color|{color.PackedValue}";

            if (_textureCache.TryGetValue(cacheKey, out var cachedTexture))
            {
                // Check if the cached texture is disposed
                if (cachedTexture.IsDisposed)
                {
                    // Remove the disposed texture from cache
                    _textureCache.TryRemove(cacheKey, out _);
                }
                else
                {
                    try
                    {
                        cachedTexture.AddReference();
                        Interlocked.Increment(ref _cacheHits);
                        return cachedTexture;
                    }
                    catch (ObjectDisposedException)
                    {
                        _textureCache.TryRemove(cacheKey, out _);
                    }
                }
            }

            // Create new texture (either no cache entry or disposed texture was removed)
            var texture = new Texture2D(_graphicsDevice, 1, 1);
            texture.SetData(new[] { color });

            var managedTexture = new ManagedTexture(_graphicsDevice, texture, cacheKey);
            managedTexture.AddReference();

            _textureCache.TryAdd(cacheKey, managedTexture);

            return managedTexture;
        }

        public void SetSkinPath(string skinPath)
        {
            if (string.IsNullOrEmpty(skinPath))
                throw new ArgumentException("Skin path cannot be null or empty", nameof(skinPath));

            lock (_lockObject)
            {
                var oldSkinPath = _currentSkinPath;
                _currentSkinPath = NormalizePath(skinPath);

                // Validate skin path
                if (!ValidateSkinPath(_currentSkinPath))
                {
                    Debug.WriteLine($"Warning: Skin path validation failed for {_currentSkinPath}");
                }

                OnSkinChanged(new SkinChangedEventArgs(oldSkinPath, _currentSkinPath));
            }
        }

        public string ResolvePath(string relativePath)
        {
            // DTXMania-style path resolution: box.def skin takes priority over system skin
            if (!string.IsNullOrEmpty(_boxDefSkinPath) && _useBoxDefSkin)
            {
                return ResolvePathWithSkin(relativePath, _boxDefSkinPath);
            }
            else
            {
                return ResolvePathWithSkin(relativePath, _currentSkinPath);
            }
        }

        public bool ResourceExists(string relativePath)
        {
            var resolvedPath = ResolvePath(relativePath);
            return File.Exists(resolvedPath);
        }

        /// <summary>
        /// Set box.def skin path for temporary skin override
        /// Based on DTXMania's box.def skin system
        /// </summary>
        /// <param name="boxDefSkinPath">Path to box.def skin directory</param>
        public void SetBoxDefSkinPath(string boxDefSkinPath)
        {
            lock (_lockObject)
            {
                var oldPath = _boxDefSkinPath;
                _boxDefSkinPath = NormalizePath(boxDefSkinPath ?? "");

                if (oldPath != _boxDefSkinPath)
                {
                    Debug.WriteLine($"Box.def skin path changed: {oldPath} -> {_boxDefSkinPath}");
                }
            }
        }

        /// <summary>
        /// Enable or disable box.def skin usage
        /// </summary>
        /// <param name="useBoxDefSkin">True to use box.def skins when available</param>
        public void SetUseBoxDefSkin(bool useBoxDefSkin)
        {
            lock (_lockObject)
            {
                _useBoxDefSkin = useBoxDefSkin;
                Debug.WriteLine($"Box.def skin usage: {(_useBoxDefSkin ? "enabled" : "disabled")}");
            }
        }

        /// <summary>
        /// Get current effective skin path (considering box.def override)
        /// </summary>
        /// <returns>Current skin path being used</returns>
        public string GetCurrentEffectiveSkinPath()
        {
            if (!string.IsNullOrEmpty(_boxDefSkinPath) && _useBoxDefSkin)
            {
                return _boxDefSkinPath;
            }
            return _currentSkinPath;
        }

        public void UnloadAll()
        {
            lock (_lockObject)
            {
                // Dispose all cached textures
                foreach (var texture in _textureCache.Values)
                {
                    texture.Dispose();
                }
                _textureCache.Clear();

                // Dispose all cached fonts
                foreach (var font in _fontCache.Values)
                {
                    font.Dispose();
                }
                _fontCache.Clear();

                // Dispose all cached sounds
                foreach (var sound in _soundCache.Values)
                {
                    sound.Dispose();
                }
                _soundCache.Clear();

                Debug.WriteLine("ResourceManager: All resources unloaded");
            }
        }

        public void UnloadByPattern(string pathPattern)
        {
            if (string.IsNullOrEmpty(pathPattern))
                return;

            lock (_lockObject)
            {
                // Unload matching textures
                var texturesToRemove = _textureCache.Keys
                    .Where(key => key.Contains(pathPattern))
                    .ToList();

                foreach (var key in texturesToRemove)
                {
                    if (_textureCache.TryRemove(key, out var texture))
                    {
                        texture.Dispose();
                    }
                }

                // Unload matching fonts
                var fontsToRemove = _fontCache.Keys
                    .Where(key => key.Contains(pathPattern))
                    .ToList();

                foreach (var key in fontsToRemove)
                {
                    if (_fontCache.TryRemove(key, out var font))
                    {
                        font.Dispose();
                    }
                }

                // Unload matching sounds
                var soundsToRemove = _soundCache.Keys
                    .Where(key => key.Contains(pathPattern))
                    .ToList();

                foreach (var key in soundsToRemove)
                {
                    if (_soundCache.TryRemove(key, out var sound))
                    {
                        sound.Dispose();
                    }
                }

                Debug.WriteLine($"ResourceManager: Unloaded resources matching pattern: {pathPattern}");
            }
        }

        public ResourceUsageInfo GetUsageInfo()
        {
            return new ResourceUsageInfo
            {
                LoadedTextures = _textureCache.Count,
                LoadedFonts = _fontCache.Count,
                LoadedSounds = _soundCache.Count,
                TotalMemoryUsage = CalculateMemoryUsage(),
                CacheHits = _cacheHits,
                CacheMisses = _cacheMisses,
                TotalLoadTime = _totalLoadTime.Elapsed
            };
        }

        public void CollectUnusedResources()
        {
            lock (_lockObject)
            {
                // Remove textures with zero references
                var unusedTextures = _textureCache
                    .Where(kvp => kvp.Value.ReferenceCount <= 0)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var key in unusedTextures)
                {
                    if (_textureCache.TryRemove(key, out var texture))
                    {
                        texture.Dispose();
                    }
                }

                // Remove fonts with zero references
                var unusedFonts = _fontCache
                    .Where(kvp => kvp.Value.ReferenceCount <= 0)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var key in unusedFonts)
                {
                    if (_fontCache.TryRemove(key, out var font))
                    {
                        font.Dispose();
                    }
                }

                // Remove sounds with zero references
                var unusedSounds = _soundCache
                    .Where(kvp => kvp.Value.ReferenceCount <= 0)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var key in unusedSounds)
                {
                    if (_soundCache.TryRemove(key, out var sound))
                    {
                        sound.Dispose();
                    }
                }

                Debug.WriteLine($"ResourceManager: Collected {unusedTextures.Count} textures, {unusedFonts.Count} fonts, and {unusedSounds.Count} sounds");
            }
        }

        #endregion

        #region Events

        public event EventHandler<ResourceLoadFailedEventArgs> ResourceLoadFailed;
        public event EventHandler<SkinChangedEventArgs> SkinChanged;

        protected virtual void OnResourceLoadFailed(ResourceLoadFailedEventArgs e)
        {
            ResourceLoadFailed?.Invoke(this, e);
            Debug.WriteLine($"Resource load failed: {e.Path} - {e.ErrorMessage}");
        }

        protected virtual void OnSkinChanged(SkinChangedEventArgs e)
        {
            SkinChanged?.Invoke(this, e);
            Debug.WriteLine($"Skin changed: {e.OldSkinPath} -> {e.NewSkinPath}");
        }

        #endregion

        #region Private Helper Methods

        private void InitializeDefaultSkinPath()
        {
            // DTXMania pattern: Default skin uses System/Graphics/ directly, custom skins use System/{SkinName}/Graphics/
            var defaultPath = "System/";

            if (ValidateSkinPath(defaultPath))
            {
                _currentSkinPath = _fallbackSkinPath = defaultPath;
                Debug.WriteLine($"ResourceManager: Using default skin path: {defaultPath}");
            }
            else
            {
                // Fallback to System/Graphics/ even if validation fails (DTXMania compatibility)
                _currentSkinPath = _fallbackSkinPath = defaultPath;
                Debug.WriteLine($"ResourceManager: Fallback to default skin path: {defaultPath} (validation failed)");

                // Create default skin directory structure if it doesn't exist
                CreateDefaultSkinStructure();
            }
        }

        private void CreateDefaultSkinStructure()
        {
            try
            {
                var systemPath = "System";
                var graphicsPath = "System/Graphics";
                var fontsPath = "System/Fonts";

                // Create directories if they don't exist
                if (!Directory.Exists(systemPath))
                    Directory.CreateDirectory(systemPath);

                if (!Directory.Exists(graphicsPath))
                    Directory.CreateDirectory(graphicsPath);

                if (!Directory.Exists(fontsPath))
                    Directory.CreateDirectory(fontsPath);

                Debug.WriteLine("ResourceManager: Created default skin directory structure");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ResourceManager: Failed to create default skin structure: {ex.Message}");
            }
        }

        private string ResolvePathWithSkin(string relativePath, string skinPath)
        {
            if (Path.IsPathRooted(relativePath))
                return relativePath;

            // Combine skin path with relative path, ensuring it's relative to current working directory
            var combinedPath = Path.Combine(skinPath, relativePath);

            // Convert to full path relative to current working directory
            var fullPath = Path.GetFullPath(combinedPath);

            Debug.WriteLine($"ResourceManager: Resolved '{relativePath}' with skin '{skinPath}' to '{fullPath}'");
            return fullPath;
        }

        private string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;

            // Ensure directory path ends with directory separator
            return path.EndsWith(Path.DirectorySeparatorChar.ToString())
                ? path
                : path + Path.DirectorySeparatorChar;
        }

        private string NormalizeFilePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;

            // Normalize file path for case-insensitive comparison without adding directory separator
            return path.Replace('\\', '/').ToLowerInvariant();
        }

        private bool ValidateSkinPath(string skinPath)
        {
            // Based on DTXMania's bIsValid pattern - check for key files
            // For default skin (System/), files are directly in Graphics/
            // For custom skins (System/{SkinName}/), files are in Graphics/ subdirectory
            var validationFiles = new[]
            {
                Path.GetFullPath(Path.Combine(skinPath, "Graphics", "1_background.jpg")),
                Path.GetFullPath(Path.Combine(skinPath, "Graphics", "2_background.jpg"))
            };

            var isValid = validationFiles.Any(File.Exists);
            Debug.WriteLine($"ResourceManager: Validating skin path '{skinPath}' - {(isValid ? "VALID" : "INVALID")}");

            if (!isValid)
            {
                foreach (var file in validationFiles)
                {
                    Debug.WriteLine($"  Missing: {file}");
                }
            }

            return isValid;
        }

        private ITexture CreateFallbackTexture(string originalPath)
        {
            // Create a simple 1x1 white texture as fallback
            var texture = new Texture2D(_graphicsDevice, 1, 1);
            texture.SetData(new[] { Color.White });

            var fallback = new ManagedTexture(_graphicsDevice, texture, originalPath);
            fallback.AddReference();
            return fallback;
        }

        private IFont CreateFallbackFont(string originalPath, int size, FontStyle style)
        {
            // Create fallback font using system default
            try
            {
                var fallback = ManagedFont.CreateFont(_graphicsDevice, "Arial", size, style);
                fallback.AddReference();
                return fallback;
            }
            catch
            {
                // If even Arial fails, return null - calling code should handle this
                return null;
            }
        }

        private ISound CreateFallbackSound(string originalPath)
        {
            // Create a silent fallback sound (stub implementation)
            try
            {
                // Create a minimal silent sound effect (1 sample at 44.1kHz)
                // Use mono for minimal memory usage in fallback scenario
                var sampleData = new byte[2]; // 1 sample, 16-bit mono
                var soundEffect = new Microsoft.Xna.Framework.Audio.SoundEffect(sampleData, 44100, Microsoft.Xna.Framework.Audio.AudioChannels.Mono);

                var fallback = new ManagedSound(soundEffect, originalPath);
                fallback.AddReference();
                return fallback;
            }
            catch
            {
                // If even silent sound fails, return null - calling code should handle this
                return null;
            }
        }

        private long CalculateMemoryUsage()
        {
            long total = 0;

            foreach (var texture in _textureCache.Values)
            {
                total += texture.MemoryUsage;
            }

            // Font memory usage is harder to calculate, use approximation
            total += _fontCache.Count * 1024; // Rough estimate

            // Sound memory usage approximation (varies by format and length)
            total += _soundCache.Count * 512; // Rough estimate

            return total;
        }

        #endregion

        #region IDisposable Implementation

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                UnloadAll();
                _disposed = true;
            }
        }

        ~ResourceManager()
        {
            if (!_disposed)
            {
                Debug.WriteLine("ResourceManager: Dispose leak detected - ResourceManager was not properly disposed");
            }
            Dispose(false);
        }

        #endregion
    }
}
