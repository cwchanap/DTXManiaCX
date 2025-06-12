using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace DTX.Utilities
{
    /// <summary>
    /// Generic cache management interface for consolidating redundant cache operations
    /// </summary>
    public interface ICacheManager<TKey, TValue> : IDisposable
        where TValue : IDisposable
    {
        /// <summary>
        /// Add or update item in cache
        /// </summary>
        void Add(TKey key, TValue value);

        /// <summary>
        /// Get item from cache
        /// </summary>
        bool TryGet(TKey key, out TValue value);

        /// <summary>
        /// Remove item from cache
        /// </summary>
        bool Remove(TKey key);

        /// <summary>
        /// Clear all cached items
        /// </summary>
        void Clear();

        /// <summary>
        /// Remove items matching pattern
        /// </summary>
        void RemoveByPattern(Func<TKey, bool> predicate);

        /// <summary>
        /// Get cache statistics
        /// </summary>
        CacheStats GetStats();
    }

    /// <summary>
    /// Cache statistics information
    /// </summary>
    public class CacheStats
    {
        public int ItemCount { get; set; }
        public int HitCount { get; set; }
        public int MissCount { get; set; }
        public long MemoryUsage { get; set; }
    }

    /// <summary>
    /// Generic cache manager implementation
    /// Consolidates redundant cache management logic from ResourceManager, SongBarRenderer, etc.
    /// </summary>
    public class CacheManager<TKey, TValue> : ICacheManager<TKey, TValue>
        where TValue : IDisposable
    {
        private readonly Dictionary<TKey, TValue> _cache;
        private readonly object _lockObject = new object();
        private int _hitCount = 0;
        private int _missCount = 0;
        private bool _disposed = false;

        public CacheManager()
        {
            _cache = new Dictionary<TKey, TValue>();
        }

        public void Add(TKey key, TValue value)
        {
            if (key == null || value == null)
                return;

            lock (_lockObject)
            {
                if (_cache.TryGetValue(key, out var existing))
                {
                    existing.Dispose();
                }
                _cache[key] = value;
            }
        }

        public bool TryGet(TKey key, out TValue value)
        {
            if (key == null)
            {
                value = default(TValue);
                return false;
            }

            lock (_lockObject)
            {
                if (_cache.TryGetValue(key, out value))
                {
                    _hitCount++;
                    return true;
                }
                else
                {
                    _missCount++;
                    return false;
                }
            }
        }

        public bool Remove(TKey key)
        {
            if (key == null)
                return false;

            lock (_lockObject)
            {
                if (_cache.TryGetValue(key, out var value))
                {
                    value.Dispose();
                    return _cache.Remove(key);
                }
                return false;
            }
        }

        public void Clear()
        {
            lock (_lockObject)
            {
                foreach (var value in _cache.Values)
                {
                    value?.Dispose();
                }
                _cache.Clear();
                Debug.WriteLine($"CacheManager: Cleared {_cache.Count} items");
            }
        }

        public void RemoveByPattern(Func<TKey, bool> predicate)
        {
            if (predicate == null)
                return;

            lock (_lockObject)
            {
                var keysToRemove = new List<TKey>();
                foreach (var kvp in _cache)
                {
                    if (predicate(kvp.Key))
                    {
                        keysToRemove.Add(kvp.Key);
                    }
                }

                foreach (var key in keysToRemove)
                {
                    if (_cache.TryGetValue(key, out var value))
                    {
                        value.Dispose();
                        _cache.Remove(key);
                    }
                }

                Debug.WriteLine($"CacheManager: Removed {keysToRemove.Count} items by pattern");
            }
        }

        public CacheStats GetStats()
        {
            lock (_lockObject)
            {
                return new CacheStats
                {
                    ItemCount = _cache.Count,
                    HitCount = _hitCount,
                    MissCount = _missCount,
                    MemoryUsage = CalculateMemoryUsage()
                };
            }
        }

        private long CalculateMemoryUsage()
        {
            // Basic memory estimation - can be overridden for specific cache types
            return _cache.Count * 1024; // Rough estimate
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                Clear();
                _disposed = true;
            }
        }
    }
}
