using Microsoft.Xna.Framework.Graphics;
using DTX.Resources;
using System;
using System.Collections.Generic;

namespace DTXMania.Test.Helpers
{
    /// <summary>
    /// Mock resource manager for unit tests
    /// </summary>
    public class MockResourceManager : IResourceManager, IDisposable
    {
        private readonly GraphicsDevice _graphicsDevice;
        private readonly Dictionary<string, ITexture> _textureCache;
        private readonly Dictionary<string, IFont> _fontCache;
        private readonly Dictionary<string, ISound> _soundCache;
        private string _skinPath = "System/";
        private string _boxDefSkinPath = "";
        private bool _useBoxDefSkin = false;

        public event EventHandler<ResourceLoadFailedEventArgs> ResourceLoadFailed;
        public event EventHandler<SkinChangedEventArgs> SkinChanged;

        public MockResourceManager(GraphicsDevice graphicsDevice)
        {
            _graphicsDevice = graphicsDevice;
            _textureCache = new Dictionary<string, ITexture>();
            _fontCache = new Dictionary<string, IFont>();
            _soundCache = new Dictionary<string, ISound>();
        }

        public ITexture LoadTexture(string path, bool enableTransparency = true)
        {
            if (string.IsNullOrEmpty(path))
                return null;

            var cacheKey = $"{path}|{enableTransparency}";
            
            if (_textureCache.TryGetValue(cacheKey, out var cachedTexture))
                return cachedTexture;

            // Create a mock texture for testing
            if (_graphicsDevice != null)
            {
                try
                {
                    var texture2D = new Texture2D(_graphicsDevice, 1, 1);
                    texture2D.SetData(new[] { Microsoft.Xna.Framework.Color.White });
                    var mockTexture = new ManagedTexture(_graphicsDevice, texture2D, path);
                    _textureCache[cacheKey] = mockTexture;
                    return mockTexture;
                }
                catch
                {
                    // If texture creation fails, return null
                    return null;
                }
            }

            return null;
        }

        public IFont LoadFont(string path, int size, FontStyle style = FontStyle.Regular)
        {
            if (string.IsNullOrEmpty(path))
                return null;

            var cacheKey = $"{path}|{size}|{style}";
            
            if (_fontCache.TryGetValue(cacheKey, out var cachedFont))
                return cachedFont;

            // For tests, we don't need real fonts
            return null;
        }

        public ISound LoadSound(string path)
        {
            if (string.IsNullOrEmpty(path))
                return null;

            if (_soundCache.TryGetValue(path, out var cachedSound))
                return cachedSound;

            // For tests, we don't need real sounds
            return null;
        }

        public void UnloadTexture(string path)
        {
            var keysToRemove = new List<string>();
            foreach (var key in _textureCache.Keys)
            {
                if (key.StartsWith(path))
                {
                    keysToRemove.Add(key);
                }
            }

            foreach (var key in keysToRemove)
            {
                if (_textureCache.TryGetValue(key, out var texture))
                {
                    texture?.Dispose();
                    _textureCache.Remove(key);
                }
            }
        }

        public void UnloadFont(string path)
        {
            var keysToRemove = new List<string>();
            foreach (var key in _fontCache.Keys)
            {
                if (key.StartsWith(path))
                {
                    keysToRemove.Add(key);
                }
            }

            foreach (var key in keysToRemove)
            {
                if (_fontCache.TryGetValue(key, out var font))
                {
                    font?.Dispose();
                    _fontCache.Remove(key);
                }
            }
        }

        public void UnloadSound(string path)
        {
            if (_soundCache.TryGetValue(path, out var sound))
            {
                sound?.Dispose();
                _soundCache.Remove(path);
            }
        }

        public void ClearCache()
        {
            foreach (var texture in _textureCache.Values)
                texture?.Dispose();
            _textureCache.Clear();

            foreach (var font in _fontCache.Values)
                font?.Dispose();
            _fontCache.Clear();

            foreach (var sound in _soundCache.Values)
                sound?.Dispose();
            _soundCache.Clear();
        }

        // Additional IResourceManager methods for testing
        public ITexture LoadTexture(string path)
        {
            return LoadTexture(path, true);
        }

        public IFont LoadFont(string path, int size)
        {
            return LoadFont(path, size, FontStyle.Regular);
        }

        public void SetSkinPath(string skinPath)
        {
            var oldPath = _skinPath;
            _skinPath = skinPath ?? "System/";
            SkinChanged?.Invoke(this, new SkinChangedEventArgs(oldPath, _skinPath));
        }

        public string ResolvePath(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath))
                return "";

            var effectiveSkinPath = GetCurrentEffectiveSkinPath();
            return System.IO.Path.Combine(effectiveSkinPath, relativePath);
        }

        public bool ResourceExists(string relativePath)
        {
            var fullPath = ResolvePath(relativePath);
            return System.IO.File.Exists(fullPath);
        }

        public void SetBoxDefSkinPath(string boxDefSkinPath)
        {
            _boxDefSkinPath = boxDefSkinPath ?? "";
        }

        public void SetUseBoxDefSkin(bool useBoxDefSkin)
        {
            _useBoxDefSkin = useBoxDefSkin;
        }

        public string GetCurrentEffectiveSkinPath()
        {
            if (_useBoxDefSkin && !string.IsNullOrEmpty(_boxDefSkinPath))
                return _boxDefSkinPath;
            return _skinPath;
        }

        public void UnloadAll()
        {
            ClearCache();
        }

        public void UnloadByPattern(string pathPattern)
        {
            // Simple pattern matching for tests
            var keysToRemove = new List<string>();

            foreach (var key in _textureCache.Keys)
            {
                if (key.Contains(pathPattern))
                    keysToRemove.Add(key);
            }

            foreach (var key in keysToRemove)
            {
                if (_textureCache.TryGetValue(key, out var texture))
                {
                    texture?.Dispose();
                    _textureCache.Remove(key);
                }
            }
        }

        public ResourceUsageInfo GetUsageInfo()
        {
            return new ResourceUsageInfo
            {
                LoadedTextures = _textureCache.Count,
                LoadedFonts = _fontCache.Count,
                LoadedSounds = _soundCache.Count,
                TotalMemoryUsage = 0,
                CacheHits = 0,
                CacheMisses = 0,
                TotalLoadTime = TimeSpan.Zero
            };
        }

        public void CollectUnusedResources()
        {
            // For tests, just clear everything
            ClearCache();
        }

        public void Dispose()
        {
            ClearCache();
        }
    }
}
