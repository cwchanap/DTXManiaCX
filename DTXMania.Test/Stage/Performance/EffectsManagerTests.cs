using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Stage.Performance;
using DTXMania.Game.Lib.UI.Layout;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Moq;
using Xunit;

namespace DTXMania.Test.Stage.Performance
{
    [Trait("Category", "Performance")]
    public class EffectsManagerTests
    {
        [Fact]
        public void Constructor_WhenHitEffectTextureLoads_ShouldEnableEffectsAndRetainCacheReference()
        {
            using var fixture = CreateManager(totalSprites: 4);

            Assert.True(fixture.EffectsEnabled);
            Assert.NotNull(fixture.HitEffectTexture);
            var cachedSource = GetCachedTextureSource(fixture.Manager);
            Assert.Same(fixture.HitTextureSource, cachedSource);
            // The cache reference added by LoadTexture is retained (not balanced back to 0).
            Assert.Equal(1, cachedSource!.ReferenceCount);
        }

        [Fact]
        public void Dispose_WhenTextureCameFromCache_ShouldReleaseReferenceWithoutDisposingUnderlyingTexture()
        {
            using var fixture = CreateManager(totalSprites: 3);
            var cachedSource = GetCachedTextureSource(fixture.Manager);
            var initialRefCount = cachedSource!.ReferenceCount;

            fixture.Manager.Dispose();

            Assert.Equal(initialRefCount - 1, cachedSource.ReferenceCount);
            // The underlying Texture2D is owned by the ResourceManager cache and must NOT
            // be disposed by EffectsManager — disposing it would poison the cache for the
            // next PerformanceStage activation that reloads HitFx.
            Assert.False(fixture.HitTexture.WasDisposed);
            Assert.Null(GetCachedTextureSource(fixture.Manager));
        }

        [Fact]
        public void Dispose_WhenConstructedFromTwoSequentialManagers_ShouldNotPoisonCacheForSecondInstance()
        {
            // Regression test mirroring PooledEffectsManager: two sequential EffectsManagers
            // backed by the same cached ITexture. The second must still observe a usable
            // (non-disposed) texture after the first is disposed.
            var hitTexture = CreateTrackingTexture(width: 8 * 4, height: 32);
            var graphicsDevice = CreateGraphicsDeviceStub();
            var sharedCachedTexture = new ManagedTexture(graphicsDevice, hitTexture, TexturePath.HitFx);
            var resourceManager = new Mock<IResourceManager>();
            resourceManager
                .Setup(x => x.LoadTexture(It.IsAny<string>()))
                .Returns(() =>
                {
                    sharedCachedTexture.AddReference();
                    return sharedCachedTexture;
                });

            var first = new EffectsManager(graphicsDevice, resourceManager.Object);
            first.Dispose();

            Assert.False(hitTexture.WasDisposed,
                "EffectsManager.Dispose() must not dispose the shared cached Texture2D");

            var second = new EffectsManager(graphicsDevice, resourceManager.Object);
            try
            {
                Assert.True(IsEffectsEnabled(second));
            }
            finally
            {
                second.Dispose();
            }
        }

        [Fact]
        public void Constructor_WhenLoadTextureReturnsNullTexture_ShouldRunFallbackAndEnableEffects()
        {
            // The standard ResourceManager never returns null, but EffectsManager defends
            // against it: the ArgumentException is caught by the outer catch, the fallback
            // path synthesizes its own texture, and effects end up enabled via the fallback.
            var graphicsDevice = CreateGraphicsDeviceStub();
            var resourceManager = new Mock<IResourceManager>();
            resourceManager
                .Setup(x => x.LoadTexture(It.IsAny<string>()))
                .Returns((ManagedTexture?)null!);

            var manager = new TrackingFallbackEffectsManager(graphicsDevice, resourceManager.Object);

            Assert.True(manager.EffectsEnabled);
            // No cached reference is retained when the load failed before sprite construction.
            Assert.Null(GetCachedTextureSource(manager));
            Assert.NotNull(manager.LastFallbackTexture);
        }

        [Fact]
        public void Constructor_WhenSpriteConstructionThrows_ShouldReleaseCacheReferenceAndRunFallback()
        {
            // ManagedSpriteTexture divides Width by spriteWidth; a zero-width texture yields
            // spriteWidth == 0, so the sprite-sheet computation throws inside the
            // ManagedSpriteTexture constructor. The inner catch must release the cache
            // reference added by LoadTexture before the outer catch runs the fallback.
            var graphicsDevice = CreateGraphicsDeviceStub();
            var zeroWidthTexture = CreateTrackingTexture(width: 0, height: 32);
            var loadedTexture = new ManagedTexture(graphicsDevice, zeroWidthTexture, TexturePath.HitFx);
            var resourceManager = new Mock<IResourceManager>();
            resourceManager
                .Setup(x => x.LoadTexture(It.IsAny<string>()))
                .Returns(() =>
                {
                    loadedTexture.AddReference();
                    return loadedTexture;
                });

            var manager = new TrackingFallbackEffectsManager(graphicsDevice, resourceManager.Object);

            // Sprite construction threw → inner catch released the cache reference → outer
            // catch ran the fallback → fallback enabled effects. The cache reference must
            // not leak.
            Assert.True(manager.EffectsEnabled);
            Assert.Equal(0, loadedTexture.ReferenceCount);
            Assert.Null(GetCachedTextureSource(manager));
        }

        [Fact]
        public void Constructor_WhenFallbackTextureCreationThrows_ShouldDisableEffects()
        {
            // If even the fallback synthesis fails, EffectsManager must disable effects
            // rather than throwing out of the constructor.
            var graphicsDevice = CreateGraphicsDeviceStub();
            var zeroWidthTexture = CreateTrackingTexture(width: 0, height: 32);
            var loadedTexture = new ManagedTexture(graphicsDevice, zeroWidthTexture, TexturePath.HitFx);
            var resourceManager = new Mock<IResourceManager>();
            resourceManager
                .Setup(x => x.LoadTexture(It.IsAny<string>()))
                .Returns(() =>
                {
                    loadedTexture.AddReference();
                    return loadedTexture;
                });

            var manager = new ThrowingFallbackEffectsManager(graphicsDevice, resourceManager.Object);

            Assert.False(manager.EffectsEnabled);
            Assert.Equal(0, loadedTexture.ReferenceCount);
        }

        [Fact]
        public void Dispose_WhenTextureCameFromFallback_ShouldDisposeOwnedFallbackTexture()
        {
            // Fallback path: EffectsManager synthesized its own Texture2D, so Dispose must
            // own and dispose it (the else branch of Dispose).
            var graphicsDevice = CreateGraphicsDeviceStub();
            var resourceManager = new Mock<IResourceManager>();
            resourceManager
                .Setup(x => x.LoadTexture(It.IsAny<string>()))
                .Returns((ManagedTexture?)null!);

            var manager = new TrackingFallbackEffectsManager(graphicsDevice, resourceManager.Object);
            var fallbackTexture = manager.LastFallbackTexture!;
            Assert.NotNull(fallbackTexture);

            manager.Dispose();

            Assert.True(fallbackTexture.WasDisposed);
            Assert.Null(GetCachedTextureSource(manager));
        }

        [Fact]
        public void SpawnHitEffect_WhenEffectsDisabled_ShouldBeNoOp()
        {
            using var fixture = CreateManager(totalSprites: 4);
            fixture.DisableEffects();

            fixture.Manager.SpawnHitEffect(0);

            Assert.Empty(GetActiveEffects(fixture.Manager));
        }

        [Fact]
        public void Update_WhenEffectsDisabled_ShouldBeNoOp()
        {
            using var fixture = CreateManager(totalSprites: 2);
            fixture.DisableEffects();

            var exception = Record.Exception(() => fixture.Manager.Update(0.1));

            Assert.Null(exception);
        }

        [Fact]
        public void Draw_WhenEffectsDisabled_ShouldBeNoOp()
        {
            using var fixture = CreateManager(totalSprites: 2);
            fixture.DisableEffects();

            var exception = Record.Exception(() =>
                fixture.Manager.Draw((SpriteBatch)RuntimeHelpers.GetUninitializedObject(typeof(SpriteBatch))));

            Assert.Null(exception);
        }

        [Fact]
        public void ClearAllEffects_WhenCalled_ShouldNotThrow()
        {
            using var fixture = CreateManager(totalSprites: 2);

            var exception = Record.Exception(() => fixture.Manager.ClearAllEffects());

            Assert.Null(exception);
        }

        private static ManagerFixture CreateManager(int totalSprites) => new(totalSprites);

        private static IReadOnlyList<object> GetActiveEffects(EffectsManager manager)
        {
            return ((IEnumerable)GetPrivateField<object>(manager, "_activeEffects")!)
                .Cast<object>()
                .ToList();
        }

        private static ITexture? GetCachedTextureSource(EffectsManager manager)
            => GetPrivateField<ITexture>(manager, "_cachedTextureSource");

        private static bool IsEffectsEnabled(EffectsManager manager)
            => GetPrivateField<bool>(manager, "_effectsEnabled");

        private static T? GetPrivateField<T>(object target, string fieldName)
        {
            var field = FindField(target.GetType(), fieldName);
            Assert.NotNull(field);
            return (T?)field!.GetValue(target);
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            var field = FindField(target.GetType(), fieldName);
            Assert.NotNull(field);
            field!.SetValue(target, value);
        }

        private static FieldInfo? FindField(Type type, string fieldName)
        {
            for (var t = type; t != null; t = t.BaseType)
            {
                var field = t.GetField(fieldName,
                    BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
                if (field != null)
                    return field;
            }
            return null;
        }

        private static GraphicsDevice CreateGraphicsDeviceStub()
        {
            return (GraphicsDevice)RuntimeHelpers.GetUninitializedObject(typeof(GraphicsDevice));
        }

        private static TrackingTexture2D CreateTrackingTexture(int width, int height)
        {
            var texture = (TrackingTexture2D)RuntimeHelpers.GetUninitializedObject(typeof(TrackingTexture2D));
            SetPrivateField(texture, "width", width);
            SetPrivateField(texture, "height", height);
            SetPrivateField(texture, "<TexelWidth>k__BackingField", 1f / Math.Max(1, width));
            SetPrivateField(texture, "<TexelHeight>k__BackingField", 1f / Math.Max(1, height));
            return texture;
        }

        /// <summary>
        /// EffectsManager subclass whose fallback path returns a tracking Texture2D instead
        /// of allocating a real one (which requires a live graphics device).
        /// </summary>
        private sealed class TrackingFallbackEffectsManager : EffectsManager
        {
            public TrackingFallbackEffectsManager(GraphicsDevice graphicsDevice, IResourceManager resourceManager)
                : base(graphicsDevice, resourceManager)
            {
            }

            public TrackingTexture2D? LastFallbackTexture { get; private set; }

            public bool EffectsEnabled => IsEffectsEnabled(this);

            protected override Texture2D CreateFallbackTexture(GraphicsDevice graphicsDevice, int width, int height)
            {
                var texture = CreateTrackingTexture(width, height);
                LastFallbackTexture = texture;
                return texture;
            }
        }

        /// <summary>
        /// EffectsManager subclass whose fallback path always throws, exercising the
        /// fallback-failure catch that disables effects.
        /// </summary>
        private sealed class ThrowingFallbackEffectsManager : EffectsManager
        {
            public ThrowingFallbackEffectsManager(GraphicsDevice graphicsDevice, IResourceManager resourceManager)
                : base(graphicsDevice, resourceManager)
            {
            }

            public bool EffectsEnabled => IsEffectsEnabled(this);

            protected override Texture2D CreateFallbackTexture(GraphicsDevice graphicsDevice, int width, int height)
                => throw new InvalidOperationException("fallback unavailable");
        }

        private sealed class ManagerFixture : IDisposable
        {
            public ManagerFixture(int totalSprites)
            {
                HitTexture = CreateTrackingTexture(width: 8 * totalSprites, height: 32);
                var graphicsDevice = CreateGraphicsDeviceStub();
                HitTextureSource = new ManagedTexture(graphicsDevice, HitTexture, TexturePath.HitFx);
                var resourceManager = new Mock<IResourceManager>();
                resourceManager
                    .Setup(x => x.LoadTexture(TexturePath.HitFx))
                    .Returns(() =>
                    {
                        // Mirror the real ResourceManager, which calls AddReference()
                        // before returning a cached texture.
                        HitTextureSource.AddReference();
                        return HitTextureSource;
                    });

                Manager = new EffectsManager(graphicsDevice, resourceManager.Object);
            }

            public EffectsManager Manager { get; }
            public TrackingTexture2D HitTexture { get; }
            public ManagedTexture HitTextureSource { get; }

            public bool EffectsEnabled => IsEffectsEnabled(Manager);
            public ManagedSpriteTexture? HitEffectTexture => GetPrivateField<ManagedSpriteTexture>(Manager, "_hitEffectTexture");

            public void DisableEffects() => SetPrivateField(Manager, "_effectsEnabled", false);

            public void Dispose() => Manager.Dispose();
        }

        private sealed class TrackingTexture2D : Texture2D
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
