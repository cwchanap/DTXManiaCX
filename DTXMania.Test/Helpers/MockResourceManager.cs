using Microsoft.Xna.Framework.Graphics;
using DTX.Resources;
using DTX.Utilities;
using System;
using System.Collections.Generic;

namespace DTXMania.Test.Helpers
{
    /// <summary>
    /// Mock resource manager for unit tests
    /// </summary>
    public class MockResourceManager : IResourceManager, IDisposable
    {
        private readonly GraphicsDevice _graphicsDevice;        private readonly CacheManager<string, ITexture> _textureCache;
        private readonly CacheManager<string, IFont> _fontCache;
        private readonly CacheManager<string, ISound> _soundCache;
        private string _skinPath = "System/";
        private string _boxDefSkinPath = "";
        private bool _useBoxDefSkin = false;        public event EventHandler<ResourceLoadFailedEventArgs>? ResourceLoadFailed;
        public event EventHandler<SkinChangedEventArgs>? SkinChanged;        public MockResourceManager(GraphicsDevice graphicsDevice)
        {
            _graphicsDevice = graphicsDevice;
            _textureCache = new CacheManager<string, ITexture>();
            _fontCache = new CacheManager<string, IFont>();
            _soundCache = new CacheManager<string, ISound>();
        }        public ITexture LoadTexture(string path, bool enableTransparency = true)
        {
            if (string.IsNullOrEmpty(path))
                return null!;

            var cacheKey = $"{path}|{enableTransparency}";
            
            if (_textureCache.TryGet(cacheKey, out var cachedTexture))
                return cachedTexture;

            // Create a mock texture for testing
            if (_graphicsDevice != null)
            {
                try
                {
                    var texture2D = new Texture2D(_graphicsDevice, 1, 1);
                    texture2D.SetData(new[] { Microsoft.Xna.Framework.Color.White });
                    var mockTexture = new ManagedTexture(_graphicsDevice, texture2D, path);
                    _textureCache.Add(cacheKey, mockTexture);
                    return mockTexture;
                }
                catch
                {
                    // If texture creation fails, return null
                    return null!;
                }
            }

            return null!;
        }        public IFont LoadFont(string path, int size, FontStyle style = FontStyle.Regular)
        {
            if (string.IsNullOrEmpty(path))
                return null!;

            var cacheKey = $"{path}|{size}|{style}";
            
            if (_fontCache.TryGet(cacheKey, out var cachedFont))
                return cachedFont;

            // For tests, we don't need real fonts
            return null!;
        }        public ISound LoadSound(string path)
        {
            if (string.IsNullOrEmpty(path))
                return null!;

            if (_soundCache.TryGet(path, out var cachedSound))
                return cachedSound;

            // For tests, we don't need real sounds
            return null!;
        }        public void UnloadTexture(string path)
        {
            _textureCache.RemoveByPattern(key => key.StartsWith(path));
        }

        public void UnloadFont(string path)
        {
            _fontCache.RemoveByPattern(key => key.StartsWith(path));
        }

        public void UnloadSound(string path)
        {
            _soundCache.Remove(path);
        }        public void ClearCache()
        {
            _textureCache.Clear();
            _fontCache.Clear();
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
        }        public void UnloadByPattern(string pathPattern)
        {
            // Simple pattern matching for tests
            _textureCache.RemoveByPattern(key => key.Contains(pathPattern));
        }        public ResourceUsageInfo GetUsageInfo()
        {
            var textureStats = _textureCache.GetStats();
            var fontStats = _fontCache.GetStats();
            var soundStats = _soundCache.GetStats();
            
            return new ResourceUsageInfo
            {
                LoadedTextures = textureStats.ItemCount,
                LoadedFonts = fontStats.ItemCount,
                LoadedSounds = soundStats.ItemCount,
                TotalMemoryUsage = textureStats.MemoryUsage + fontStats.MemoryUsage + soundStats.MemoryUsage,
                CacheHits = textureStats.HitCount + fontStats.HitCount + soundStats.HitCount,
                CacheMisses = textureStats.MissCount + fontStats.MissCount + soundStats.MissCount,
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
