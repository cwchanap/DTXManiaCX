using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using DTXMania.Game.Lib.Utilities;
using System.IO;
using System.Linq;
using System.Threading;

[assembly: InternalsVisibleTo("DTXMania.Test")]
[assembly: InternalsVisibleTo("DTXMania.Test.Mac")]

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
        private ISkinTheme _currentTheme;

        // Read-only bundled System skin root (macOS .app Contents/Resources/System or
        // portable System/ sibling to the executable). Used as the ultimate fallback
        // when the writable app-data System skin is missing assets. Null when no
        // bundled System directory exists (e.g. dev runs, Windows-installed builds
        // where the installer already seeds app-data).
        private string _bundledSystemSkinRoot;

        // Cached app data root to avoid repeated OS checks and path calculations
        private readonly string _cachedAppDataRoot;

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

            // Cache app data root once to avoid repeated OS checks and path calculations
            _cachedAppDataRoot = AppPaths.GetAppDataRoot();

            // Resolve the read-only bundled System skin root (null if none exists on disk).
            _bundledSystemSkinRoot = ResolveBundledSystemSkinRoot();

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
                try
                {
                    cachedTexture.AddReference();
                    Interlocked.Increment(ref _cacheHits);
                    return cachedTexture;
                }
                catch (ObjectDisposedException)
                {
                    _textureCache.TryRemove(cacheKey, out _);
                    // Continue to load a new texture
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
                    if (File.Exists(fallbackPath))
                    {
                        resolvedPath = fallbackPath;
                    }
                    else
                    {
                        // Try read-only bundled System skin (macOS .app / portable build)
                        var bundledPath = TryResolveFromBundledSkin(path);
                        if (bundledPath != null && File.Exists(bundledPath))
                        {
                            resolvedPath = bundledPath;
                        }
                        else
                        {
                            var errorMsg = $"Texture not found: {path} (resolved: {resolvedPath})";
                            OnResourceLoadFailed(new ResourceLoadFailedEventArgs(path,
                                new FileNotFoundException(errorMsg), errorMsg));
                            return CreateFallbackTexture(path);
                        }
                    }
                }

                // Create texture parameters
                var creationParams = new TextureCreationParams
                {
                    EnableTransparency = enableTransparency,
                    TransparencyColor = Color.Black
                };

                // Load and create texture
                var texture = CreateTextureCore(resolvedPath, path, creationParams);
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
                try
                {
                    cachedFont.AddReference();
                    Interlocked.Increment(ref _cacheHits);
                    return cachedFont;
                }
                catch (ObjectDisposedException)
                {
                    _fontCache.TryRemove(cacheKey, out _);
                    // Continue to load a new font
                }
            }

            Interlocked.Increment(ref _cacheMisses);
            _totalLoadTime.Start();

            try
            {
                // Create font using ManagedFont factory
                var font = CreateFontCore(normalizedPath, size, style);
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
                try
                {
                    cachedSound.AddReference();
                    Interlocked.Increment(ref _cacheHits);
                    return cachedSound;
                }
                catch (ObjectDisposedException)
                {
                    _soundCache.TryRemove(cacheKey, out _);
                    // Continue to load a new sound
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
                    if (File.Exists(fallbackPath))
                    {
                        resolvedPath = fallbackPath;
                    }
                    else
                    {
                        // Try read-only bundled System skin (macOS .app / portable build)
                        var bundledPath = TryResolveFromBundledSkin(path);
                        if (bundledPath != null && File.Exists(bundledPath))
                        {
                            resolvedPath = bundledPath;
                        }
                        else
                        {
                            var errorMsg = $"Sound not found: {path} (resolved: {resolvedPath})";
                            OnResourceLoadFailed(new ResourceLoadFailedEventArgs(path,
                                new FileNotFoundException(errorMsg), errorMsg));
                            return CreateFallbackSound(path);
                        }
                    }
                }

                // Load and create sound
                var sound = CreateSoundCore(resolvedPath, path);
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
                try
                {
                    cachedTexture.AddReference();
                    Interlocked.Increment(ref _cacheHits);
                    return cachedTexture;
                }
                catch (ObjectDisposedException)
                {
                    _textureCache.TryRemove(cacheKey, out _);
                    // Continue to create new texture
                }
            }

            // Create new texture (either no cache entry or disposed texture was removed)
            var managedTexture = CreateColorTextureCore(color, cacheKey);
            managedTexture.AddReference();

            _textureCache.TryAdd(cacheKey, managedTexture);

            return managedTexture;
        }

        public void SetSkinPath(string skinPath)
        {
            if (string.IsNullOrEmpty(skinPath))
                throw new ArgumentException("Skin path cannot be null or empty", nameof(skinPath));

            SkinChangedEventArgs eventArgs;
            lock (_lockObject)
            {
                var oldSkinPath = _currentSkinPath;
                _currentSkinPath = NormalizePath(skinPath);

                // Validate skin path
                if (!ValidateSkinPath(_currentSkinPath))
                {
                    Debug.WriteLine($"Warning: Skin path validation failed for {_currentSkinPath}");
                }

                // Invalidate the cached theme before notifying subscribers so
                // synchronous SkinChanged handlers querying CurrentTheme observe
                // the invalidated state and reload for the new skin.
                _currentTheme = null;

                // When the skin actually changes, evict all cached textures and sounds
                // so subsequent LoadTexture/LoadSound calls resolve against the new skin.
                // The cache is keyed by relative path (not resolved absolute path), so
                // without eviction the old skin's assets would be served until a full
                // restart. Fonts are not skin-specific (loaded from Fonts/, not Graphics/)
                // so the font cache is intentionally preserved.
                if (!string.Equals(oldSkinPath, _currentSkinPath, StringComparison.Ordinal))
                {
                    EvictSkinDependentCache();
                }

                eventArgs = new SkinChangedEventArgs(oldSkinPath, _currentSkinPath);
            }

            // Raise the event outside _lockObject so subscribers that call back
            // into ResourceManager (e.g. querying CurrentTheme, reloading
            // textures) cannot deadlock. Eviction and theme invalidation have
            // already completed under the lock, so handlers observe a
            // consistent post-switch state.
            OnSkinChanged(eventArgs);
        }

        /// <summary>
        /// Disposes and clears all cached textures and sounds. Called when the skin
        /// path changes so subsequent loads resolve from the new skin. Fonts are
        /// preserved because they are not skin-specific. Stages that hold texture
        /// references reload them on their next OnActivate (the documented design).
        /// </summary>
        /// <remarks>
        /// Safety invariant: this method calls Dispose() directly on every cached
        /// texture/sound, bypassing reference counting. ManagedTexture.Dispose()
        /// unconditionally releases the underlying GPU texture regardless of the
        /// current reference count, and RemoveReference() deliberately does NOT
        /// auto-dispose (see ManagedTexture.RemoveReference). Decrementing refs
        /// instead of disposing would therefore leave the old skin's textures
        /// alive until CollectUnusedResources runs, defeating live reload.
        ///
        /// This is safe only because of two design invariants that callers must
        /// preserve:
        ///  1. Eviction runs inside the caller's lock, BEFORE OnSkinChanged is
        ///     raised (the event is raised outside the lock in SetSkinPath).
        ///     Subscribers that react by reloading observe an empty cache.
        ///  2. No stage draws a skin-dependent texture between SkinChanged and its
        ///     own OnActivate (where it re-resolves and reloads). Stages holding
        ///     disposed references must not touch them after the skin switch until
        ///     they have reloaded. The game loop is single-threaded today, so this
        ///     holds by construction; if draw and update ever run concurrently this
        ///     contract must be revisited.
        /// </remarks>
        private void EvictSkinDependentCache()
        {
            var textureCount = _textureCache.Count;
            foreach (var texture in _textureCache.Values)
            {
                try { texture.Dispose(); }
                catch (Exception ex) { Debug.WriteLine($"ResourceManager: Error disposing texture during skin switch: {ex.Message}"); }
            }
            _textureCache.Clear();

            var soundCount = _soundCache.Count;
            foreach (var sound in _soundCache.Values)
            {
                try { sound.Dispose(); }
                catch (Exception ex) { Debug.WriteLine($"ResourceManager: Error disposing sound during skin switch: {ex.Message}"); }
            }
            _soundCache.Clear();

            // Only log when something was actually evicted. SwitchToSystemSkin
            // calls SetBoxDefSkinPath("") then SetSkinPath, which can trigger
            // eviction twice — the second call finds an empty cache and would
            // produce a misleading "evicted" log line.
            if (textureCount > 0 || soundCount > 0)
                Debug.WriteLine($"ResourceManager: Evicted skin-dependent cache ({textureCount} textures, {soundCount} sounds)");
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
            if (string.IsNullOrEmpty(relativePath))
                return false;

            var resolvedPath = ResolvePath(relativePath);
            if (File.Exists(resolvedPath))
                return true;

            // Match LoadTexture/LoadSound semantics: allow fallback skin hits.
            var fallbackPath = ResolvePathWithSkin(relativePath, _fallbackSkinPath);
            if (File.Exists(fallbackPath))
                return true;

            // Read-only bundled System skin (macOS .app / portable build)
            var bundledPath = TryResolveFromBundledSkin(relativePath);
            return bundledPath != null && File.Exists(bundledPath);
        }

        /// <summary>
        /// Set box.def skin path for temporary skin override
        /// Based on DTXMania's box.def skin system
        /// </summary>
        /// <param name="boxDefSkinPath">Path to box.def skin directory</param>
        public void SetBoxDefSkinPath(string boxDefSkinPath)
        {
            // NOTE: This evicts the skin-dependent cache (when the effective skin
            // actually changes) but does NOT raise SkinChanged. box.def skins are
            // per-song-set overrides, not a global skin selection — callers that
            // need global reload notification subscribe to SkinChanged via
            // SetSkinPath instead. The theme cache is always invalidated here
            // regardless of whether eviction runs, so the next CurrentTheme read
            // reloads for the (possibly) new effective skin.
            lock (_lockObject)
            {
                var oldPath = _boxDefSkinPath;

                // Capture the effective skin before mutation so we can detect
                // whether the switch actually changes which skin ResolvePath
                // uses. The cache is keyed by relative path, so without
                // eviction a box.def switch would serve the previous skin's
                // textures until a restart.
                var oldEffective = EffectiveSkinPathNoLock();

                // Don't use NormalizePath for box.def skins - preserve relative paths
                // so they resolve relative to the current working directory (song location)
                var path = boxDefSkinPath ?? "";
                if (string.IsNullOrEmpty(path))
                {
                    _boxDefSkinPath = "";
                }
                else if (Path.IsPathRooted(path))
                {
                    // Absolute paths: normalize normally
                    _boxDefSkinPath = NormalizePath(path);
                }
                else
                {
                    // Relative paths: preserve as-is, just ensure proper separators
                    // Path.GetFullPath will resolve them relative to current directory when used
                    _boxDefSkinPath = path.Replace('\\', Path.DirectorySeparatorChar)
                                          .Replace('/', Path.DirectorySeparatorChar);

                    // Ensure trailing separator for consistency
                    if (!_boxDefSkinPath.EndsWith(Path.DirectorySeparatorChar.ToString()))
                    {
                        _boxDefSkinPath += Path.DirectorySeparatorChar;
                    }
                }

                if (oldPath != _boxDefSkinPath)
                {
                    Debug.WriteLine($"Box.def skin path changed: {oldPath} -> {_boxDefSkinPath}");
                }
                _currentTheme = null;

                if (!string.Equals(oldEffective, EffectiveSkinPathNoLock(), StringComparison.Ordinal))
                {
                    EvictSkinDependentCache();
                }
            }
        }

        /// <summary>
        /// Enable or disable box.def skin usage
        /// </summary>
        /// <param name="useBoxDefSkin">True to use box.def skins when available</param>
        public void SetUseBoxDefSkin(bool useBoxDefSkin)
        {
            // NOTE: Same eviction-without-SkinChanged contract as SetBoxDefSkinPath
            // above — box.def toggles are per-song-set, not a global skin change.
            lock (_lockObject)
            {
                var oldEffective = EffectiveSkinPathNoLock();
                _useBoxDefSkin = useBoxDefSkin;
                Debug.WriteLine($"Box.def skin usage: {(_useBoxDefSkin ? "enabled" : "disabled")}");
                _currentTheme = null;

                if (!string.Equals(oldEffective, EffectiveSkinPathNoLock(), StringComparison.Ordinal))
                {
                    EvictSkinDependentCache();
                }
            }
        }

        /// <summary>
        /// Get current effective skin path (considering box.def override)
        /// </summary>
        /// <returns>Current skin path being used</returns>
        /// <remarks>
        /// This read is intentionally lock-free. The game loop (update + draw)
        /// is single-threaded today, and SetSkinPath / SetBoxDefSkinPath /
        /// SetUseBoxDefSkin are only called from the update thread. The fields
        /// read here (_boxDefSkinPath, _useBoxDefSkin, _currentSkinPath) are
        /// references/bools, so reads are atomic at the CLR level. If draw and
        /// update ever run concurrently, this method must be revisited.
        /// </remarks>
        public string GetCurrentEffectiveSkinPath()
        {
            return EffectiveSkinPathNoLock();
        }

        /// <summary>
        /// Computes the effective skin path from the current field state.
        /// Callers must hold _lockObject when the fields may be mutating
        /// (e.g. from SetBoxDefSkinPath / SetUseBoxDefSkin). The public
        /// GetCurrentEffectiveSkinPath calls this without the lock — see its
        /// remarks for the single-threaded rationale.
        /// </summary>
        private string EffectiveSkinPathNoLock()
        {
            if (!string.IsNullOrEmpty(_boxDefSkinPath) && _useBoxDefSkin)
            {
                return _boxDefSkinPath;
            }
            return _currentSkinPath;
        }

        /// <summary>
        /// Theme for the effective skin. Lazily loaded; invalidated whenever the
        /// skin path, box.def path, or box.def usage changes.
        /// </summary>
        public ISkinTheme CurrentTheme
        {
            get
            {
                // Fast path: return the cached theme under the lock.
                lock (_lockObject)
                {
                    if (_currentTheme != null)
                        return _currentTheme;
                }

                // Slow path: resolve the theme file path and parse it outside
                // the lock so concurrent resource operations (ResolvePath, texture
                // lookups, etc.) are not blocked on File.ReadAllLines disk I/O.
                // Capture the effective skin path we are loading for so the second
                // lock can detect a concurrent SetSkinPath/SetBoxDefSkinPath that
                // invalidated the theme between the fast-path check and the publish
                // (otherwise we would cache a stale theme for the old skin).
                var loadedForSkinPath = GetCurrentEffectiveSkinPath();
                var themePath = ResolveThemeFilePath();
                var loaded = SkinTheme.Load(themePath);

                lock (_lockObject)
                {
                    // Another caller may have loaded — or the skin may have been
                    // invalidated — between the fast-path check and now. Prefer
                    // any already-cached instance. If the effective skin path
                    // changed since we resolved the theme file, discard our load
                    // without publishing so the next caller reloads for the new
                    // skin.
                    if (_currentTheme != null)
                        return _currentTheme;
                    if (!string.Equals(GetCurrentEffectiveSkinPath(), loadedForSkinPath,
                                        StringComparison.Ordinal))
                        return loaded;
                    _currentTheme = loaded;
                    return _currentTheme;
                }
            }
        }

        /// <summary>
        /// Finds Theme.ini using the same candidate order as texture resolution:
        /// effective skin path, then fallback skin path, then the read-only bundled
        /// System skin root (macOS .app / portable build). This third tier is how a
        /// release build — where CX Neon *is* System/ — picks up its Theme.ini even
        /// when the writable app-data skin directory doesn't have one.
        /// </summary>
        private string ResolveThemeFilePath()
        {
            var effectivePath = Path.Combine(GetCurrentEffectiveSkinPath(), SkinTheme.ThemeFileName);
            if (File.Exists(effectivePath))
                return effectivePath;

            var fallbackPath = Path.Combine(_fallbackSkinPath, SkinTheme.ThemeFileName);
            if (File.Exists(fallbackPath))
                return fallbackPath;

            var bundledPath = TryResolveFromBundledSkin(SkinTheme.ThemeFileName);
            if (bundledPath != null && File.Exists(bundledPath))
                return bundledPath;

            return fallbackPath;
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

        #region Virtual Hooks

        protected virtual ITexture CreateTextureCore(string resolvedPath, string originalPath, TextureCreationParams creationParams)
        {
            return new ManagedTexture(_graphicsDevice, resolvedPath, originalPath, creationParams);
        }

        protected virtual IFont CreateFontCore(string normalizedPath, int size, FontStyle style)
        {
            return ManagedFont.CreateFont(_graphicsDevice, normalizedPath, size, style);
        }

        protected virtual ISound CreateSoundCore(string resolvedPath, string originalPath)
        {
            return new ManagedSound(resolvedPath, originalPath);
        }

        protected virtual ITexture CreateColorTextureCore(Color color, string cacheKey)
        {
            var texture = new Texture2D(_graphicsDevice, 1, 1);
            texture.SetData(new[] { color });
            return new ManagedTexture(_graphicsDevice, texture, cacheKey);
        }

        private void InitializeDefaultSkinPath()
        {
            // DTXMania pattern: Default skin uses System/Graphics/ directly, custom skins use System/{SkinName}/Graphics/
            // Use cached app data root for testability and consistency
            var defaultPath = NormalizePath(Path.Combine(_cachedAppDataRoot, "System"));

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
                // Use cached app data root for testability and consistency
                var systemPath = Path.Combine(_cachedAppDataRoot, "System");
                var graphicsPath = Path.Combine(systemPath, "Graphics");
                var fontsPath = Path.Combine(systemPath, "Fonts");

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

        /// <summary>
        /// Resolve the read-only bundled System skin root from the candidate paths
        /// (macOS .app Contents/Resources/System, portable System/ sibling to the
        /// executable). Returns the first existing candidate with a trailing
        /// separator, or null when no bundled System directory exists.
        /// </summary>
        private string ResolveBundledSystemSkinRoot()
        {
            return ResolveBundledSystemSkinRootFromCandidates(AppPaths.GetBundledSystemSkinRootCandidates());
        }

        /// <summary>
        /// Core logic of <see cref="ResolveBundledSystemSkinRoot"/> extracted as an
        /// internal static method so the candidate iteration, trailing-separator
        /// normalization, and exception handling are unit-testable without a real
        /// assembly directory or files on disk.
        /// </summary>
        /// <param name="candidates">Ordered candidate bundled System skin root paths.</param>
        /// <returns>The first existing candidate with a trailing separator, or null.</returns>
        internal static string ResolveBundledSystemSkinRootFromCandidates(IEnumerable<string> candidates)
        {
            foreach (var candidate in candidates)
            {
                try
                {
                    if (Directory.Exists(candidate))
                    {
                        var normalized = candidate.EndsWith(Path.DirectorySeparatorChar.ToString())
                            ? candidate
                            : candidate + Path.DirectorySeparatorChar;
                        Debug.WriteLine($"ResourceManager: Using bundled System skin root: {normalized}");
                        return normalized;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"ResourceManager: Bundled candidate '{candidate}' check failed: {ex.Message}");
                }
            }
            return null;
        }

        /// <summary>
        /// Resolve a relative resource path against the read-only bundled System skin.
        /// Returns null when no bundled root is available or the path is absolute, so
        /// callers can treat null as "no bundled hit" and fall through to the
        /// fallback texture/sound. Mirrors ResolvePathWithSkin's absolute-path guard.
        /// </summary>
        internal string TryResolveFromBundledSkin(string relativePath)
        {
            if (string.IsNullOrEmpty(_bundledSystemSkinRoot))
                return null;
            if (Path.IsPathRooted(relativePath))
                return null;

            try
            {
                return Path.GetFullPath(Path.Combine(_bundledSystemSkinRoot, relativePath));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ResourceManager: Bundled resolution of '{relativePath}' failed: {ex.Message}");
                return null;
            }
        }

        private string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;

            string resolvedPath;

            // Avoid redundant resolution for already-absolute paths
            // AppPaths.GetDefaultSystemSkinRoot() already returns Path.GetFullPath(...)
            if (Path.IsPathRooted(path))
            {
                resolvedPath = Path.GetFullPath(path);
            }
            else
            {
                resolvedPath = AppPaths.ResolvePath(path, _cachedAppDataRoot);
            }

            // Ensure directory path ends with directory separator
            return resolvedPath.EndsWith(Path.DirectorySeparatorChar.ToString())
                ? resolvedPath
                : resolvedPath + Path.DirectorySeparatorChar;
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

        protected virtual ITexture CreateFallbackTexture(string originalPath)
        {
            // Create a simple 1x1 white texture as fallback
            var texture = new Texture2D(_graphicsDevice, 1, 1);
            texture.SetData(new[] { Color.White });

            var fallback = new ManagedTexture(_graphicsDevice, texture, originalPath);
            fallback.AddReference();
            return fallback;
        }

        protected virtual IFont CreateFallbackFont(string originalPath, int size, FontStyle style)
        {
            // Create fallback font using system default
            try
            {
                var fallback = ManagedFont.CreateFont(_graphicsDevice, "Arial", size, style);
                fallback.AddReference();
                return fallback;
            }
            catch (Exception ex)
            {
                // If even Arial fails, return null - calling code should handle this
                Debug.WriteLine($"ResourceManager: {ex.GetType().Name} creating fallback font for '{originalPath}': {ex.Message}");
                return null;
            }
        }

        protected virtual ISound CreateFallbackSound(string originalPath)
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
            catch (Exception ex)
            {
                // If even silent sound fails, return null - calling code should handle this
                Debug.WriteLine($"ResourceManager: {ex.GetType().Name} creating fallback sound for '{originalPath}': {ex.Message}");
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
