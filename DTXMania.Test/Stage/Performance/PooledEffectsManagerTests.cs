using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Stage.Performance;
using DTXMania.Game.Lib.UI.Layout;
using DTXMania.Test.TestData;
using Microsoft.Xna.Framework;
using Xunit;

namespace DTXMania.Test.Stage.Performance
{
    [Trait("Category", "Performance")]
    public class PooledEffectsManagerTests
    {
        private static readonly int InitialPoolSize =
            (int)typeof(PooledEffectsManager)
                .GetField("InitialPoolSize", BindingFlags.Static | BindingFlags.NonPublic)!
                .GetRawConstantValue()!;

        private static readonly int MaxPoolSize =
            (int)typeof(PooledEffectsManager)
                .GetField("MaxPoolSize", BindingFlags.Static | BindingFlags.NonPublic)!
                .GetRawConstantValue()!;

        [Fact]
        public void SpawnHitEffect_WhenPoolContainsReusableInstance_ShouldUsePoolAndTrackHit()
        {
            var manager = CreateManager(totalSprites: 4);

            manager.SpawnHitEffect(3);

            var stats = manager.GetPoolingStats();
            Assert.Equal(InitialPoolSize - 1, stats.PoolSize);
            Assert.Equal(1, stats.ActiveInstances);
            Assert.Equal(1, stats.TotalRequests);
            Assert.Equal(1, stats.PoolHits);
            Assert.Equal(0, stats.PoolMisses);

            var activeEffect = GetActiveEffects(manager).Single();
            var position = (Vector2)activeEffect.GetType().GetProperty("Position")!.GetValue(activeEffect)!;
            Assert.Equal(new Vector2(PerformanceUILayout.GetLaneX(3), PerformanceUILayout.JudgementLineY), position);
        }

        [Fact]
        public void SpawnHitEffect_WhenInitialPoolIsExhausted_ShouldCreateNewInstanceAndTrackMiss()
        {
            var manager = CreateManager(totalSprites: 5);
            for (int i = 0; i < InitialPoolSize; i++)
            {
                manager.SpawnHitEffect(i % PerformanceUILayout.LaneCount);
            }

            manager.SpawnHitEffect(5);

            var stats = manager.GetPoolingStats();
            Assert.Equal(InitialPoolSize + 1, stats.ActiveInstances);
            Assert.Equal(InitialPoolSize + 1, stats.TotalRequests);
            Assert.Equal(InitialPoolSize, stats.PoolHits);
            Assert.Equal(1, stats.PoolMisses);
            Assert.Equal(0, stats.PoolSize);
        }

        [Fact]
        public void SpawnHitEffect_WhenActiveEffectsAlreadyAtMaxPoolSize_ShouldSkipAddingNewEffect()
        {
            var manager = CreateManager(totalSprites: 3);
            for (int i = 0; i < MaxPoolSize; i++)
            {
                manager.SpawnHitEffect(i % PerformanceUILayout.LaneCount);
            }

            manager.SpawnHitEffect(1);

            var stats = manager.GetPoolingStats();
            Assert.Equal(MaxPoolSize, stats.ActiveInstances);
            Assert.Equal(MaxPoolSize + 1, stats.TotalRequests);
            Assert.Equal(InitialPoolSize, stats.PoolHits);
            Assert.Equal((MaxPoolSize + 1) - InitialPoolSize, stats.PoolMisses);
        }

        [Fact]
        public void Update_WhenActiveEffectExpires_ShouldReturnItToPool()
        {
            var manager = CreateManager(totalSprites: 2);
            manager.SpawnHitEffect(0);

            manager.Update(1.0);

            var stats = manager.GetPoolingStats();
            Assert.Equal(0, stats.ActiveInstances);
            Assert.Equal(InitialPoolSize, stats.PoolSize);
        }

        [Fact]
        public void ResetStats_ShouldClearCountersWithoutChangingActiveEffectCounts()
        {
            var manager = CreateManager(totalSprites: 4);
            manager.SpawnHitEffect(0);
            manager.SpawnHitEffect(1);

            manager.ResetStats();

            var stats = manager.GetPoolingStats();
            Assert.Equal(2, stats.ActiveInstances);
            Assert.Equal(0, stats.TotalRequests);
            Assert.Equal(0, stats.PoolHits);
            Assert.Equal(0, stats.PoolMisses);
        }

        [Fact]
        public void ClearAllEffects_ShouldMoveActiveEffectsBackToPool()
        {
            var manager = CreateManager(totalSprites: 3);
            manager.SpawnHitEffect(0);
            manager.SpawnHitEffect(1);

            manager.ClearAllEffects();

            var stats = manager.GetPoolingStats();
            Assert.Equal(0, stats.ActiveInstances);
            Assert.Equal(InitialPoolSize, stats.PoolSize);
        }

        [Fact]
        public void Dispose_ShouldClearEffectsAndDisposeHitTexture()
        {
            var manager = CreateManager(totalSprites: 3);
            var hitTexture = ReflectionHelpers.GetPrivateField<ManagedSpriteTexture>(manager, "_hitEffectTexture")!;
            manager.SpawnHitEffect(0);

            manager.Dispose();

            var stats = manager.GetPoolingStats();
            Assert.Equal(0, stats.ActiveInstances);
            Assert.Equal(InitialPoolSize, stats.PoolSize);
            Assert.True(hitTexture.IsDisposed);
        }

        private static PooledEffectsManager CreateManager(int totalSprites)
        {
#pragma warning disable SYSLIB0050
            var manager = (PooledEffectsManager)FormatterServices.GetUninitializedObject(typeof(PooledEffectsManager));
            var hitTexture = (ManagedSpriteTexture)FormatterServices.GetUninitializedObject(typeof(ManagedSpriteTexture));
#pragma warning restore SYSLIB0050

            ReflectionHelpers.SetPrivateField(hitTexture, "_spriteWidth", 8);
            ReflectionHelpers.SetPrivateField(hitTexture, "_spriteHeight", 32);
            ReflectionHelpers.SetPrivateField(hitTexture, "_spritesPerRow", totalSprites);
            ReflectionHelpers.SetPrivateField(hitTexture, "_totalSprites", totalSprites);
            ReflectionHelpers.SetPrivateField(hitTexture, "_lockObject", new object());

            ReflectionHelpers.SetPrivateField(manager, "_effectPool", CreatePrivateCollection("_effectPool"));
            ReflectionHelpers.SetPrivateField(manager, "_activeEffects", CreatePrivateCollection("_activeEffects"));
            ReflectionHelpers.SetPrivateField(manager, "_hitEffectTexture", hitTexture);
            ReflectionHelpers.SetPrivateField(manager, "_activeLock", new object());
            ReflectionHelpers.SetPrivateField(manager, "_totalRequests", 0L);
            ReflectionHelpers.SetPrivateField(manager, "_poolHits", 0L);
            ReflectionHelpers.SetPrivateField(manager, "_poolMisses", 0L);
            ReflectionHelpers.InvokePrivateMethod(manager, "InitializePool");

            return manager;
        }

        private static object CreatePrivateCollection(string fieldName)
        {
            var fieldType = typeof(PooledEffectsManager)
                .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)!
                .FieldType;
            return Activator.CreateInstance(fieldType)!;
        }

        private static IReadOnlyList<object> GetActiveEffects(PooledEffectsManager manager)
        {
            return ((IEnumerable)ReflectionHelpers.GetPrivateField<object>(manager, "_activeEffects")!)
                .Cast<object>()
                .ToList();
        }
    }
}
