using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Stage.Performance;
using DTXMania.Game.Lib.UI.Layout;
using DTXMania.Test.TestData;
using Microsoft.Xna.Framework;
using Moq;
using Xunit;

namespace DTXMania.Test.Stage.Performance
{
    [Trait("Category", "Performance")]
    public class PooledEffectsManagerTests
    {
        [Fact]
        public void SpawnHitEffect_WhenPoolContainsReusableInstance_ShouldUsePoolAndTrackHit()
        {
            using var fixture = CreateManager(totalSprites: 4);
            var manager = fixture.Manager;
            var initialPoolSize = manager.GetPoolingStats().PoolSize;

            manager.SpawnHitEffect(3);

            var stats = manager.GetPoolingStats();
            Assert.Equal(initialPoolSize - 1, stats.PoolSize);
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
            using var fixture = CreateManager(totalSprites: 5);
            var manager = fixture.Manager;
            var initialPoolSize = manager.GetPoolingStats().PoolSize;

            for (int i = 0; i < initialPoolSize; i++)
            {
                manager.SpawnHitEffect(i % PerformanceUILayout.LaneCount);
            }

            manager.SpawnHitEffect(5);

            var stats = manager.GetPoolingStats();
            Assert.Equal(initialPoolSize + 1, stats.ActiveInstances);
            Assert.Equal(initialPoolSize + 1, stats.TotalRequests);
            Assert.Equal(initialPoolSize, stats.PoolHits);
            Assert.Equal(1, stats.PoolMisses);
            Assert.Equal(0, stats.PoolSize);
        }

        [Fact]
        public void SpawnHitEffect_WhenActiveEffectsAlreadyAtCapacity_ShouldSkipAddingNewEffect()
        {
            using var fixture = CreateManager(totalSprites: 3);
            var manager = fixture.Manager;
            var previousStats = manager.GetPoolingStats();
            EffectPoolingStats? skippedEffectStats = null;

            for (int i = 0; i < 1000; i++)
            {
                manager.SpawnHitEffect(i % PerformanceUILayout.LaneCount);
                var currentStats = manager.GetPoolingStats();
                if (currentStats.ActiveInstances == previousStats.ActiveInstances)
                {
                    skippedEffectStats = currentStats;
                    Assert.Equal(previousStats.TotalRequests + 1, currentStats.TotalRequests);
                    Assert.Equal(previousStats.PoolHits, currentStats.PoolHits);
                    Assert.Equal(previousStats.PoolMisses + 1, currentStats.PoolMisses);
                    break;
                }

                previousStats = currentStats;
            }

            Assert.NotNull(skippedEffectStats);

            manager.SpawnHitEffect(1);

            var stats = manager.GetPoolingStats();
            Assert.Equal(skippedEffectStats!.ActiveInstances, stats.ActiveInstances);
            Assert.Equal(skippedEffectStats.TotalRequests + 1, stats.TotalRequests);
            Assert.Equal(skippedEffectStats.PoolHits, stats.PoolHits);
            Assert.Equal(skippedEffectStats.PoolMisses + 1, stats.PoolMisses);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(PerformanceUILayout.LaneCount)]
        public void SpawnHitEffect_InvalidLane_ThrowsArgumentOutOfRange(int lane)
        {
            using var fixture = CreateManager(totalSprites: 4);

            Assert.Throws<ArgumentOutOfRangeException>(() => fixture.Manager.SpawnHitEffect(lane));
        }

        [Fact]
        public void Update_WhenActiveEffectExpires_ShouldReturnItToPool()
        {
            using var fixture = CreateManager(totalSprites: 2);
            var manager = fixture.Manager;
            var initialPoolSize = manager.GetPoolingStats().PoolSize;
            manager.SpawnHitEffect(0);

            manager.Update(1.0);

            var stats = manager.GetPoolingStats();
            Assert.Equal(0, stats.ActiveInstances);
            Assert.Equal(initialPoolSize, stats.PoolSize);
        }

        [Fact]
        public void ResetStats_ShouldClearCountersWithoutChangingActiveEffectCounts()
        {
            using var fixture = CreateManager(totalSprites: 4);
            var manager = fixture.Manager;
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
            using var fixture = CreateManager(totalSprites: 3);
            var manager = fixture.Manager;
            var initialPoolSize = manager.GetPoolingStats().PoolSize;
            manager.SpawnHitEffect(0);
            manager.SpawnHitEffect(1);

            manager.ClearAllEffects();

            var stats = manager.GetPoolingStats();
            Assert.Equal(0, stats.ActiveInstances);
            Assert.Equal(initialPoolSize, stats.PoolSize);
        }

        [Fact]
        public void Dispose_ShouldClearEffectsAndDisposeHitTexture()
        {
            using var fixture = CreateManager(totalSprites: 3);
            var manager = fixture.Manager;
            var initialPoolSize = manager.GetPoolingStats().PoolSize;
            manager.SpawnHitEffect(0);

            manager.Dispose();

            var stats = manager.GetPoolingStats();
            Assert.Equal(0, stats.ActiveInstances);
            Assert.Equal(initialPoolSize, stats.PoolSize);
            Assert.True(fixture.HitTexture.WasDisposed);
        }

        private static ManagerFixture CreateManager(int totalSprites)
        {
            return new ManagerFixture(totalSprites);
        }

        private static IReadOnlyList<object> GetActiveEffects(PooledEffectsManager manager)
        {
            return ((IEnumerable)ReflectionHelpers.GetPrivateField<object>(manager, "_activeEffects")!)
                .Cast<object>()
                .ToList();
        }

        private static Microsoft.Xna.Framework.Graphics.GraphicsDevice CreateGraphicsDeviceStub()
        {
            return (Microsoft.Xna.Framework.Graphics.GraphicsDevice)RuntimeHelpers.GetUninitializedObject(
                typeof(Microsoft.Xna.Framework.Graphics.GraphicsDevice));
        }

        private static TrackingTexture2D CreateTrackingTexture(int width, int height)
        {
            var texture = (TrackingTexture2D)RuntimeHelpers.GetUninitializedObject(typeof(TrackingTexture2D));
            ReflectionHelpers.SetPrivateField(texture, "width", width);
            ReflectionHelpers.SetPrivateField(texture, "height", height);
            ReflectionHelpers.SetPrivateField(texture, "<TexelWidth>k__BackingField", 1f / width);
            ReflectionHelpers.SetPrivateField(texture, "<TexelHeight>k__BackingField", 1f / height);
            return texture;
        }

        private sealed class ManagerFixture : IDisposable
        {
            public ManagerFixture(int totalSprites)
            {
                HitTexture = CreateTrackingTexture(width: 8 * totalSprites, height: 32);
                var graphicsDevice = CreateGraphicsDeviceStub();
                var loadedTexture = new ManagedTexture(graphicsDevice, HitTexture, "Graphics/hit_fx.png");
                var resourceManager = new Mock<IResourceManager>();
                resourceManager
                    .Setup(x => x.LoadTexture("Graphics/hit_fx.png"))
                    .Returns(loadedTexture);

                Manager = new PooledEffectsManager(graphicsDevice, resourceManager.Object);
            }

            public PooledEffectsManager Manager { get; }

            public TrackingTexture2D HitTexture { get; }

            public void Dispose()
            {
                Manager.Dispose();
            }
        }

        private sealed class TrackingTexture2D : Microsoft.Xna.Framework.Graphics.Texture2D
        {
            public TrackingTexture2D() : base(null!, 1, 1)
            {
            }

            public bool WasDisposed { get; private set; }

            protected override void Dispose(bool disposing)
            {
                WasDisposed = true;
            }
        }
    }
}
