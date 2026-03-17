using System;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Utilities;
using Xunit;

namespace DTXMania.Test.Utilities
{
    /// <summary>
    /// Tests for CacheManager and CacheStats
    /// </summary>
    public class CacheManagerTests : IDisposable
    {
        private readonly CacheManager<string, DisposableValue> _cache;

        public CacheManagerTests()
        {
            _cache = new CacheManager<string, DisposableValue>();
        }

        public void Dispose()
        {
            _cache?.Dispose();
        }

        #region CacheStats Tests

        [Fact]
        public void CacheStats_DefaultValues_ShouldBeZero()
        {
            var stats = new CacheStats();
            Assert.Equal(0, stats.ItemCount);
            Assert.Equal(0, stats.HitCount);
            Assert.Equal(0, stats.MissCount);
            Assert.Equal(0, stats.MemoryUsage);
        }

        [Fact]
        public void CacheStats_SetProperties_ShouldRetainValues()
        {
            var stats = new CacheStats
            {
                ItemCount = 5,
                HitCount = 10,
                MissCount = 3,
                MemoryUsage = 4096
            };

            Assert.Equal(5, stats.ItemCount);
            Assert.Equal(10, stats.HitCount);
            Assert.Equal(3, stats.MissCount);
            Assert.Equal(4096, stats.MemoryUsage);
        }

        #endregion

        #region Add Tests

        [Fact]
        public void Add_NewItem_ShouldStoreIt()
        {
            var value = new DisposableValue("test");
            _cache.Add("key1", value);

            Assert.True(_cache.TryGet("key1", out var retrieved));
            Assert.Equal(value, retrieved);
        }

        [Fact]
        public void Add_NullKey_ShouldNotStore()
        {
            var value = new DisposableValue("test");
            _cache.Add(null, value); // Should not throw

            Assert.Equal(0, _cache.GetStats().ItemCount);
        }

        [Fact]
        public void Add_NullValue_ShouldNotStore()
        {
            _cache.Add("key1", null); // Should not throw
            Assert.Equal(0, _cache.GetStats().ItemCount);
        }

        [Fact]
        public void Add_ExistingKey_ShouldReplaceAndDisposeOld()
        {
            var original = new DisposableValue("original");
            var replacement = new DisposableValue("replacement");

            _cache.Add("key1", original);
            _cache.Add("key1", replacement);

            Assert.True(original.IsDisposed);
            Assert.True(_cache.TryGet("key1", out var retrieved));
            Assert.Equal(replacement, retrieved);
        }

        #endregion

        #region TryGet Tests

        [Fact]
        public void TryGet_ExistingKey_ShouldReturnTrueAndValue()
        {
            var value = new DisposableValue("hello");
            _cache.Add("key", value);

            var found = _cache.TryGet("key", out var result);

            Assert.True(found);
            Assert.Equal(value, result);
        }

        [Fact]
        public void TryGet_MissingKey_ShouldReturnFalseAndDefault()
        {
            var found = _cache.TryGet("nonexistent", out var result);

            Assert.False(found);
            Assert.Null(result);
        }

        [Fact]
        public void TryGet_NullKey_ShouldReturnFalse()
        {
            var found = _cache.TryGet(null, out var result);

            Assert.False(found);
            Assert.Null(result);
        }

        [Fact]
        public void TryGet_ShouldIncrementHitCount()
        {
            var value = new DisposableValue("x");
            _cache.Add("k", value);

            _cache.TryGet("k", out _);
            _cache.TryGet("k", out _);

            var stats = _cache.GetStats();
            Assert.Equal(2, stats.HitCount);
        }

        [Fact]
        public void TryGet_MissShouldIncrementMissCount()
        {
            _cache.TryGet("missing", out _);
            _cache.TryGet("also_missing", out _);

            var stats = _cache.GetStats();
            Assert.Equal(2, stats.MissCount);
        }

        #endregion

        #region Remove Tests

        [Fact]
        public void Remove_ExistingKey_ShouldReturnTrueAndDisposeValue()
        {
            var value = new DisposableValue("to_remove");
            _cache.Add("key", value);

            var removed = _cache.Remove("key");

            Assert.True(removed);
            Assert.True(value.IsDisposed);
            Assert.False(_cache.TryGet("key", out _));
        }

        [Fact]
        public void Remove_MissingKey_ShouldReturnFalse()
        {
            var removed = _cache.Remove("nonexistent");
            Assert.False(removed);
        }

        [Fact]
        public void Remove_NullKey_ShouldReturnFalse()
        {
            var removed = _cache.Remove(null);
            Assert.False(removed);
        }

        #endregion

        #region Clear Tests

        [Fact]
        public void Clear_WithItems_ShouldRemoveAllAndDisposeValues()
        {
            var v1 = new DisposableValue("a");
            var v2 = new DisposableValue("b");
            var v3 = new DisposableValue("c");

            _cache.Add("k1", v1);
            _cache.Add("k2", v2);
            _cache.Add("k3", v3);

            _cache.Clear();

            Assert.Equal(0, _cache.GetStats().ItemCount);
            Assert.True(v1.IsDisposed);
            Assert.True(v2.IsDisposed);
            Assert.True(v3.IsDisposed);
        }

        [Fact]
        public void Clear_EmptyCache_ShouldNotThrow()
        {
            _cache.Clear(); // No exception
            Assert.Equal(0, _cache.GetStats().ItemCount);
        }

        #endregion

        #region RemoveByPattern Tests

        [Fact]
        public void RemoveByPattern_MatchingKeys_ShouldRemoveAndDisposeMatches()
        {
            var v1 = new DisposableValue("v1");
            var v2 = new DisposableValue("v2");
            var v3 = new DisposableValue("v3");

            _cache.Add("prefix_one", v1);
            _cache.Add("prefix_two", v2);
            _cache.Add("other_key", v3);

            _cache.RemoveByPattern(k => k.StartsWith("prefix_"));

            Assert.True(v1.IsDisposed);
            Assert.True(v2.IsDisposed);
            Assert.False(v3.IsDisposed);
            Assert.Equal(1, _cache.GetStats().ItemCount);
        }

        [Fact]
        public void RemoveByPattern_NullPredicate_ShouldNotThrow()
        {
            _cache.Add("key", new DisposableValue("val"));
            _cache.RemoveByPattern(null); // Should not throw
            Assert.Equal(1, _cache.GetStats().ItemCount);
        }

        [Fact]
        public void RemoveByPattern_NoMatches_ShouldLeaveCacheUnchanged()
        {
            _cache.Add("key1", new DisposableValue("v1"));
            _cache.Add("key2", new DisposableValue("v2"));

            _cache.RemoveByPattern(k => k.StartsWith("zzz"));

            Assert.Equal(2, _cache.GetStats().ItemCount);
        }

        #endregion

        #region GetStats Tests

        [Fact]
        public void GetStats_ReflectsCurrentState()
        {
            _cache.Add("a", new DisposableValue("1"));
            _cache.Add("b", new DisposableValue("2"));
            _cache.TryGet("a", out _);  // hit
            _cache.TryGet("x", out _);  // miss

            var stats = _cache.GetStats();

            Assert.Equal(2, stats.ItemCount);
            Assert.Equal(1, stats.HitCount);
            Assert.Equal(1, stats.MissCount);
            Assert.True(stats.MemoryUsage > 0);
        }

        #endregion

        #region Dispose Tests

        [Fact]
        public void Dispose_ShouldDisposeAllValues()
        {
            var cache = new CacheManager<string, DisposableValue>();
            var v1 = new DisposableValue("v1");
            var v2 = new DisposableValue("v2");
            cache.Add("k1", v1);
            cache.Add("k2", v2);

            cache.Dispose();

            Assert.True(v1.IsDisposed);
            Assert.True(v2.IsDisposed);
        }

        [Fact]
        public void Dispose_CalledTwice_ShouldNotThrow()
        {
            var cache = new CacheManager<string, DisposableValue>();
            cache.Add("k", new DisposableValue("v"));
            cache.Dispose();
            cache.Dispose(); // Second dispose should not throw
        }

        #endregion

        #region Thread Safety Tests

        [Fact]
        public void Add_ConcurrentAccess_ShouldNotThrow()
        {
            var tasks = new Task[10];
            for (int i = 0; i < 10; i++)
            {
                int idx = i;
                tasks[idx] = Task.Run(() =>
                {
                    _cache.Add($"key_{idx}", new DisposableValue($"val_{idx}"));
                    _cache.TryGet($"key_{idx}", out _);
                });
            }

            Task.WaitAll(tasks);
            // If we get here without exception, thread safety is working
        }

        #endregion

        /// <summary>
        /// Helper class that tracks disposal for testing
        /// </summary>
        private class DisposableValue : IDisposable
        {
            public string Value { get; }
            public bool IsDisposed { get; private set; }

            public DisposableValue(string value)
            {
                Value = value;
            }

            public void Dispose()
            {
                IsDisposed = true;
            }
        }
    }
}
