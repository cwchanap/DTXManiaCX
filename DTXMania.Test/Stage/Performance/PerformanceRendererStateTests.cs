using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Stage.Performance;
using DTXMania.Game.Lib.UI.Layout;
using DTXMania.Test.TestData;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Moq;
using Xunit;

namespace DTXMania.Test.Stage.Performance
{
    [Trait("Category", "Performance")]
    public class PerformanceRendererStateTests
    {
        #region BackgroundRenderer

        [Fact]
        public void BackgroundRenderer_Constructor_WithNullResourceManager_ShouldThrowArgumentNullException()
        {
            var exception = Assert.Throws<ArgumentNullException>(() => new BackgroundRenderer(null!));

            Assert.Equal("resourceManager", exception.ParamName);
        }

        [Fact]
        public async System.Threading.Tasks.Task LoadBackgroundAsync_WhenLoadSucceeds_ShouldMarkBackgroundReady()
        {
            var resourceManager = new Mock<IResourceManager>();
            var texture = CreateManagedTexture(width: 320, height: 180, sourcePath: "Graphics/7_background.jpg");
            resourceManager
                .Setup(manager => manager.LoadTexture(TexturePath.PerformanceBackground))
                .Returns(texture);

            var renderer = new BackgroundRenderer(resourceManager.Object);

            await renderer.LoadBackgroundAsync();

            Assert.False(renderer.IsLoading);
            Assert.False(renderer.LoadingFailed);
            Assert.True(renderer.IsReady);
            Assert.Same(texture, ReflectionHelpers.GetPrivateField<ITexture>(renderer, "_backgroundTexture"));
        }

        [Fact]
        public async System.Threading.Tasks.Task LoadBackgroundAsync_WhenLoadFails_ShouldRecordFailureState()
        {
            var resourceManager = new Mock<IResourceManager>();
            resourceManager
                .Setup(manager => manager.LoadTexture(TexturePath.PerformanceBackground))
                .Throws(new InvalidOperationException("load failed"));

            var renderer = new BackgroundRenderer(resourceManager.Object);

            await renderer.LoadBackgroundAsync();

            Assert.False(renderer.IsLoading);
            Assert.True(renderer.LoadingFailed);
            Assert.False(renderer.IsReady);
            Assert.Null(ReflectionHelpers.GetPrivateField<ITexture>(renderer, "_backgroundTexture"));
        }

        [Fact]
        public async System.Threading.Tasks.Task LoadBackgroundAsync_WhenRendererIsDisposed_ShouldReturnWithoutLoading()
        {
            var resourceManager = new Mock<IResourceManager>(MockBehavior.Strict);
            var renderer = new BackgroundRenderer(resourceManager.Object);
            ReflectionHelpers.SetPrivateField(renderer, "_disposed", true);

            await renderer.LoadBackgroundAsync();

            Assert.False(renderer.IsLoading);
            Assert.False(renderer.IsReady);
        }

        [Fact]
        public async System.Threading.Tasks.Task LoadBackgroundAsync_WhenAlreadyLoading_ShouldKeepCurrentState()
        {
            var resourceManager = new Mock<IResourceManager>(MockBehavior.Strict);
            var renderer = new BackgroundRenderer(resourceManager.Object);
            ReflectionHelpers.SetPrivateField(renderer, "_isLoading", true);

            await renderer.LoadBackgroundAsync();

            Assert.True(renderer.IsLoading);
            Assert.False(renderer.LoadingFailed);
        }

        [Fact]
        public async System.Threading.Tasks.Task LoadBackgroundAsync_WhenTextureAlreadyLoaded_ShouldSkipReload()
        {
            var resourceManager = new Mock<IResourceManager>(MockBehavior.Strict);
            var existingTexture = CreateManagedTexture(width: 64, height: 64, sourcePath: "existing.png");
            var renderer = new BackgroundRenderer(resourceManager.Object);
            ReflectionHelpers.SetPrivateField(renderer, "_backgroundTexture", existingTexture);

            await renderer.LoadBackgroundAsync();

            Assert.True(renderer.IsReady);
            Assert.Same(existingTexture, ReflectionHelpers.GetPrivateField<ITexture>(renderer, "_backgroundTexture"));
        }

        [Fact]
        public void BackgroundRenderer_Draw_WhenSpriteBatchIsNull_ShouldReturnWithoutThrowing()
        {
            var renderer = new BackgroundRenderer(new Mock<IResourceManager>().Object);

            var exception = Record.Exception(() => renderer.Draw(null!, new Rectangle(0, 0, 100, 100)));

            Assert.Null(exception);
        }

        [Fact]
        public void BackgroundRenderer_Draw_WhenBackgroundIsReady_ShouldDrawLoadedTextureAtRequestedDepth()
        {
            var renderer = new BackgroundRenderer(new Mock<IResourceManager>().Object);
            var spriteBatch = CreateUninitialized<SpriteBatch>();
            var texture = new Mock<ITexture>();
            var destinationRectangle = new Rectangle(12, 34, 320, 180);

            ReflectionHelpers.SetPrivateField(renderer, "_backgroundTexture", texture.Object);

            renderer.Draw(spriteBatch, destinationRectangle, 0.42f);

            texture.Verify(
                background => background.Draw(
                    spriteBatch,
                    destinationRectangle,
                    null,
                    Color.White,
                    0f,
                    Vector2.Zero,
                    SpriteEffects.None,
                    0.42f),
                Times.Once);
        }

        [Fact]
        public void BackgroundRenderer_Dispose_ShouldReleaseLoadedTextureAndResetFlags()
        {
            var resourceManager = new Mock<IResourceManager>();
            var renderer = new BackgroundRenderer(resourceManager.Object);
            var texture = new TrackingTextureResource();
            var fallbackTexture = CreateTrackingTexture(1, 1);
            ReflectionHelpers.SetPrivateField(renderer, "_backgroundTexture", texture);
            ReflectionHelpers.SetPrivateField(renderer, "_whiteTexture", fallbackTexture);
            ReflectionHelpers.SetPrivateField(renderer, "_isLoading", true);
            ReflectionHelpers.SetPrivateField(renderer, "_loadingFailed", true);

            renderer.Dispose();

            Assert.Equal(1, texture.RemoveReferenceCount);
            Assert.True(fallbackTexture.WasDisposed);
            Assert.Null(ReflectionHelpers.GetPrivateField<ITexture>(renderer, "_backgroundTexture"));
            Assert.Null(ReflectionHelpers.GetPrivateField<Texture2D>(renderer, "_whiteTexture"));
            Assert.False(renderer.IsLoading);
            Assert.False(renderer.LoadingFailed);
            Assert.True(ReflectionHelpers.GetPrivateField<bool>(renderer, "_disposed"));
        }

        #endregion

        #region LaneBackgroundRenderer

        [Fact]
        public void LaneBackgroundRenderer_Constructor_WithNullResourceManager_ShouldThrowArgumentNullException()
        {
            var exception = Assert.Throws<ArgumentNullException>(() => new LaneBackgroundRenderer(null!));

            Assert.Equal("resourceManager", exception.ParamName);
        }

        [Fact]
        public void LaneBackgroundRenderer_Constructor_WhenWhiteTextureCreationFails_ShouldWrapException()
        {
            var resourceManager = new Mock<IResourceManager>();
            resourceManager
                .Setup(manager => manager.CreateTextureFromColor(It.IsAny<Color>()))
                .Throws(new InvalidOperationException("failed"));

            var exception = Assert.Throws<InvalidOperationException>(() => new LaneBackgroundRenderer(resourceManager.Object));

            Assert.Contains("white texture could not be created", exception.Message);
            Assert.IsType<InvalidOperationException>(exception.InnerException);
        }

        [Fact]
        public void LaneBackgroundRenderer_Constructor_WhenWhiteTextureCreationSucceeds_ShouldStoreCreatedTexture()
        {
            var expectedTexture = new TrackingTextureResource();
            var resourceManager = new Mock<IResourceManager>();
            resourceManager
                .Setup(manager => manager.CreateTextureFromColor(Color.White))
                .Returns(expectedTexture);

            var renderer = new LaneBackgroundRenderer(resourceManager.Object);

            Assert.Same(expectedTexture, ReflectionHelpers.GetPrivateField<ITexture>(renderer, "_whiteTexture"));
        }

        [Fact]
        public void LaneBackgroundRenderer_Update_ShouldNotThrow()
        {
            var renderer = CreateUninitialized<LaneBackgroundRenderer>();

            var exception = Record.Exception(() => renderer.Update(0.016));

            Assert.Null(exception);
        }

        [Fact]
        public void LaneBackgroundRenderer_Draw_WithNullSpriteBatch_ShouldReturnWithoutThrowing()
        {
            var renderer = CreateUninitialized<LaneBackgroundRenderer>();

            var exception = Record.Exception(() => renderer.Draw(null!));

            Assert.Null(exception);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(PerformanceUILayout.LaneCount)]
        public void LaneBackgroundRenderer_DrawLane_WithInvalidLaneIndex_ShouldReturnWithoutThrowing(int laneIndex)
        {
            var renderer = CreateUninitialized<LaneBackgroundRenderer>();
            ReflectionHelpers.SetPrivateField(renderer, "_whiteTexture", new TrackingTextureResource());

            var exception = Record.Exception(() => renderer.DrawLane(CreateUninitialized<SpriteBatch>(), laneIndex));

            Assert.Null(exception);
        }

        [Theory]
        [InlineData(-1, -0.5f)]
        [InlineData(PerformanceUILayout.LaneCount, 1.5f)]
        public void LaneBackgroundRenderer_DrawLaneWithAlpha_WithInvalidLaneIndex_ShouldReturnWithoutThrowing(int laneIndex, float alpha)
        {
            var renderer = CreateUninitialized<LaneBackgroundRenderer>();
            ReflectionHelpers.SetPrivateField(renderer, "_whiteTexture", new TrackingTextureResource());

            var exception = Record.Exception(() => renderer.DrawLane(CreateUninitialized<SpriteBatch>(), laneIndex, alpha));

            Assert.Null(exception);
        }

        [Fact]
        public void LaneBackgroundRenderer_DrawWithAlpha_WhenWhiteTextureMissing_ShouldReturnWithoutThrowing()
        {
            var renderer = CreateUninitialized<LaneBackgroundRenderer>();

            var exception = Record.Exception(() => renderer.Draw(CreateUninitialized<SpriteBatch>(), 0.5f));

            Assert.Null(exception);
        }

        [Fact]
        public void LaneBackgroundRenderer_Dispose_ShouldReleaseWhiteTextureAndMarkDisposed()
        {
            var texture = new TrackingTextureResource();
            var renderer = CreateUninitialized<LaneBackgroundRenderer>();
            ReflectionHelpers.SetPrivateField(renderer, "_whiteTexture", texture);

            renderer.Dispose();

            Assert.Equal(1, texture.RemoveReferenceCount);
            Assert.True(ReflectionHelpers.GetPrivateField<bool>(renderer, "_disposed"));
        }

        #endregion

        #region JudgementLineRenderer

        [Fact]
        public void JudgementLineRenderer_Constructor_WithNullGraphicsDevice_ShouldThrowArgumentNullException()
        {
            var exception = Assert.Throws<ArgumentNullException>(() => new JudgementLineRenderer(null!));

            Assert.Equal("graphicsDevice", exception.ParamName);
        }

        [Fact]
        public void JudgementLineRenderer_PropertySetters_ShouldClampAndStoreValues()
        {
            var renderer = CreateUninitialized<JudgementLineRenderer>();

            renderer.LineColor = Color.CornflowerBlue;
            renderer.LineThickness = -5;
            renderer.Alpha = 2.5f;

            Assert.Equal(Color.CornflowerBlue, renderer.LineColor);
            Assert.Equal(1, renderer.LineThickness);
            Assert.Equal(1.0f, renderer.Alpha);

            renderer.Alpha = -1.0f;

            Assert.Equal(0.0f, renderer.Alpha);
        }

        [Fact]
        public void JudgementLineRenderer_GetJudgementLineRectangle_ShouldUseLaneBoundsAndThickness()
        {
            var renderer = CreateUninitialized<JudgementLineRenderer>();
            renderer.LineThickness = 4;

            var rectangle = ReflectionHelpers.InvokePrivateMethod<Rectangle>(renderer, "GetJudgementLineRectangle");

            var leftX = PerformanceUILayout.GetLaneLeftX(0);
            var rightX = PerformanceUILayout.GetLaneRightX(PerformanceUILayout.LaneCount - 1);
            Assert.Equal(leftX, rectangle.X);
            Assert.Equal(PerformanceUILayout.JudgementLineY, rectangle.Y);
            Assert.Equal(rightX - leftX, rectangle.Width);
            Assert.Equal(4, rectangle.Height);
        }

        [Fact]
        public void JudgementLineRenderer_DrawOverloads_WhenSpriteBatchIsNull_ShouldReturnWithoutThrowing()
        {
            var renderer = CreateUninitialized<JudgementLineRenderer>();
            ReflectionHelpers.SetPrivateField(renderer, "_whiteTexture", CreateTrackingTexture(1, 1));

            var exception = Record.Exception(() =>
            {
                renderer.Draw(null!);
                renderer.Draw(null!, Color.Red);
                renderer.Draw(null!, Color.Red, 0.5f);
            });

            Assert.Null(exception);
        }

        [Fact]
        public void JudgementLineRenderer_DrawOverloads_WhenWhiteTextureIsMissing_ShouldReturnWithoutThrowing()
        {
            var renderer = CreateUninitialized<JudgementLineRenderer>();
            var spriteBatch = CreateUninitialized<SpriteBatch>();

            var exception = Record.Exception(() =>
            {
                renderer.Draw(spriteBatch);
                renderer.Draw(spriteBatch, Color.Red);
                renderer.Draw(spriteBatch, Color.Red, 0.5f);
            });

            Assert.Null(exception);
        }

        [Fact]
        public void JudgementLineRenderer_CreateWhiteTexture_WhenGraphicsDeviceIsMissing_ShouldClearTextureAndThrow()
        {
            var renderer = CreateUninitialized<JudgementLineRenderer>();
            ReflectionHelpers.SetPrivateField(renderer, "_whiteTexture", CreateTrackingTexture(1, 1));
            ReflectionHelpers.SetPrivateField(renderer, "_graphicsDevice", null);

            var exception = Assert.ThrowsAny<Exception>(() => ReflectionHelpers.InvokePrivateMethod(renderer, "CreateWhiteTexture"));

            Assert.NotNull(exception);
            Assert.Null(ReflectionHelpers.GetPrivateField<Texture2D>(renderer, "_whiteTexture"));
        }

        [Fact]
        public void JudgementLineRenderer_Dispose_ShouldReleaseTextureAndClearGraphicsDevice()
        {
            var renderer = CreateUninitialized<JudgementLineRenderer>();
            var texture = CreateTrackingTexture(1, 1);
            ReflectionHelpers.SetPrivateField(renderer, "_whiteTexture", texture);
            ReflectionHelpers.SetPrivateField(renderer, "_graphicsDevice", CreateGraphicsDeviceStub());

            renderer.Dispose();

            Assert.True(texture.WasDisposed);
            Assert.Null(ReflectionHelpers.GetPrivateField<Texture2D>(renderer, "_whiteTexture"));
            Assert.Null(ReflectionHelpers.GetPrivateField<GraphicsDevice>(renderer, "_graphicsDevice"));
            Assert.True(ReflectionHelpers.GetPrivateField<bool>(renderer, "_disposed"));
        }

        #endregion

        #region EffectsManager

        [Fact]
        public void EffectsManager_Constructor_WithNullGraphicsDevice_ShouldThrowArgumentNullException()
        {
            var resourceManager = new Mock<IResourceManager>();

            var exception = Assert.Throws<ArgumentNullException>(() => new EffectsManager(null!, resourceManager.Object));

            Assert.Equal("graphicsDevice", exception.ParamName);
        }

        [Fact]
        public void EffectsManager_Constructor_WithNullResourceManager_ShouldThrowArgumentNullException()
        {
            var graphicsDevice = CreateGraphicsDeviceStub();

            var exception = Assert.Throws<ArgumentNullException>(() => new EffectsManager(graphicsDevice, null!));

            Assert.Equal("resourceManager", exception.ParamName);
        }

        [Fact]
        public void EffectsManager_Constructor_WithValidTexture_ShouldEnableEffects()
        {
            using var fixture = new EffectsManagerFixture(totalSprites: 4);

            Assert.True(ReflectionHelpers.GetPrivateField<bool>(fixture.Manager, "_effectsEnabled"));
            Assert.NotNull(ReflectionHelpers.GetPrivateField<ManagedSpriteTexture>(fixture.Manager, "_hitEffectTexture"));
            var activeEffects = ReflectionHelpers.GetPrivateField<System.Collections.IList>(fixture.Manager, "_activeEffects");
            Assert.Empty(activeEffects!.Cast<object>());
        }

        // Note: we do not unit-test the constructor's graceful-degradation fallback path
        // (missing/zero-sprite texture). That path synthesizes a real Texture2D against
        // the GraphicsDevice, which requires a real GraphicsDevice — the stub used here
        // (GetUninitialized<GraphicsDevice>) causes the synthesized Texture2D's finalizer
        // to crash the test host. The disabled-effects *behavior* that the fallback
        // produces is covered by the SpawnHitEffect_WhenEffectsAreDisabled_* and
        // SpawnHitEffect_WhenTextureIsMissing_* tests below, which bypass the constructor
        // via CreateUninitialized<EffectsManager>.

        [Fact]
        public void SpawnHitEffect_WhenEffectsAreEnabled_ShouldAddEffectAtLanePosition()
        {
            using var fixture = new EffectsManagerFixture(totalSprites: 4);

            fixture.Manager.SpawnHitEffect(2);

            var activeEffects = ReflectionHelpers.GetPrivateField<System.Collections.IList>(fixture.Manager, "_activeEffects");
            var effect = Assert.Single(activeEffects!.Cast<object>());
            var position = (Vector2)effect.GetType().GetProperty("Position")!.GetValue(effect)!;
            Assert.Equal(new Vector2(PerformanceUILayout.GetLaneX(2), PerformanceUILayout.JudgementLineY), position);
        }

        [Fact]
        public void SpawnHitEffect_WhenEffectsAreDisabled_ShouldNotAddEffects()
        {
            using var fixture = new EffectsManagerFixture(totalSprites: 4);
            ReflectionHelpers.SetPrivateField(fixture.Manager, "_effectsEnabled", false);

            fixture.Manager.SpawnHitEffect(1);

            var activeEffects = ReflectionHelpers.GetPrivateField<System.Collections.IList>(fixture.Manager, "_activeEffects");
            Assert.Empty(activeEffects!.Cast<object>());
        }

        [Fact]
        public void SpawnHitEffect_WhenTextureIsMissing_ShouldNotAddEffects()
        {
            var manager = CreateUninitialized<EffectsManager>();
            ReflectionHelpers.SetPrivateField(manager, "_effectsEnabled", true);
            ReflectionHelpers.SetPrivateField(manager, "_activeEffects", CreateActiveEffectsList());
            ReflectionHelpers.SetPrivateField(manager, "_hitEffectTexture", null);

            manager.SpawnHitEffect(1);

            var activeEffects = ReflectionHelpers.GetPrivateField<System.Collections.IList>(manager, "_activeEffects");
            Assert.Empty(activeEffects!.Cast<object>());
        }

        [Fact]
        public void Update_WhenEffectsAreDisabled_ShouldLeaveActiveEffectsUntouched()
        {
            using var fixture = new EffectsManagerFixture(totalSprites: 4);
            fixture.Manager.SpawnHitEffect(0);
            ReflectionHelpers.SetPrivateField(fixture.Manager, "_effectsEnabled", false);

            fixture.Manager.Update(1.0);

            var activeEffects = ReflectionHelpers.GetPrivateField<System.Collections.IList>(fixture.Manager, "_activeEffects");
            Assert.Single(activeEffects!.Cast<object>());
        }

        [Fact]
        public void Update_WhenEffectsExpire_ShouldRemoveThemFromActiveList()
        {
            using var fixture = new EffectsManagerFixture(totalSprites: 2);
            fixture.Manager.SpawnHitEffect(0);

            fixture.Manager.Update(1.0);

            var activeEffects = ReflectionHelpers.GetPrivateField<System.Collections.IList>(fixture.Manager, "_activeEffects");
            Assert.Empty(activeEffects!.Cast<object>());
        }

        [Fact]
        public void ClearAllEffects_ShouldRemoveEveryActiveEffect()
        {
            using var fixture = new EffectsManagerFixture(totalSprites: 4);
            fixture.Manager.SpawnHitEffect(0);
            fixture.Manager.SpawnHitEffect(1);

            fixture.Manager.ClearAllEffects();

            var activeEffects = ReflectionHelpers.GetPrivateField<System.Collections.IList>(fixture.Manager, "_activeEffects");
            Assert.Empty(activeEffects!.Cast<object>());
        }

        [Fact]
        public void Draw_WhenEffectsAreDisabled_ShouldReturnWithoutThrowing()
        {
            using var fixture = new EffectsManagerFixture(totalSprites: 4);
            ReflectionHelpers.SetPrivateField(fixture.Manager, "_effectsEnabled", false);

            var exception = Record.Exception(() => fixture.Manager.Draw(null!));

            Assert.Null(exception);
        }

        [Fact]
        public void Draw_WhenEffectFrameIsValidButTextureIsMissing_ShouldReturnWithoutThrowing()
        {
            var manager = CreateUninitialized<EffectsManager>();
            ReflectionHelpers.SetPrivateField(manager, "_effectsEnabled", true);
            ReflectionHelpers.SetPrivateField(manager, "_hitEffectTexture", CreateSpriteTexture(totalSprites: 3));
            ReflectionHelpers.SetPrivateField(manager, "_activeEffects", CreateActiveEffectsList(CreateEffectInstance(frameIndex: 1, totalFrames: 3)));

            var exception = Record.Exception(() => manager.Draw(CreateUninitialized<SpriteBatch>()));

            Assert.Null(exception);
        }

        [Fact]
        public void Draw_WhenEffectFrameIsOutsideSpriteRange_ShouldSkipItWithoutThrowing()
        {
            var manager = CreateUninitialized<EffectsManager>();
            ReflectionHelpers.SetPrivateField(manager, "_effectsEnabled", true);
            ReflectionHelpers.SetPrivateField(manager, "_hitEffectTexture", CreateSpriteTexture(totalSprites: 1));
            ReflectionHelpers.SetPrivateField(manager, "_activeEffects", CreateActiveEffectsList(CreateEffectInstance(frameIndex: 4, totalFrames: 1)));

            var exception = Record.Exception(() => manager.Draw(CreateUninitialized<SpriteBatch>()));

            Assert.Null(exception);
        }

        [Theory]
        [InlineData(-1, 4, false)]
        [InlineData(0, 4, true)]
        [InlineData(3, 4, true)]
        [InlineData(4, 4, false)]
        [InlineData(100, 4, false)]
        [InlineData(0, 0, false)]
        [InlineData(0, 1, true)]
        public void IsValidFrameIndex_ShouldValidateBounds(int frameIndex, int totalSprites, bool expected)
        {
            var result = EffectsManager.IsValidFrameIndex(frameIndex, totalSprites);

            Assert.Equal(expected, result);
        }

        [Fact]
        public void Dispose_ShouldReleaseCachedTextureReferenceAndClearActiveEffects()
        {
            using var fixture = new EffectsManagerFixture(totalSprites: 4);
            fixture.Manager.SpawnHitEffect(0);

            fixture.Manager.Dispose();

            var activeEffects = ReflectionHelpers.GetPrivateField<System.Collections.IList>(fixture.Manager, "_activeEffects");
            Assert.False(ReflectionHelpers.GetPrivateField<bool>(fixture.Manager, "_effectsEnabled"));
            Assert.Null(ReflectionHelpers.GetPrivateField<ManagedSpriteTexture>(fixture.Manager, "_hitEffectTexture"));
            Assert.Null(ReflectionHelpers.GetPrivateField<ITexture>(fixture.Manager, "_cachedTextureSource"));
            Assert.Empty(activeEffects!.Cast<object>());
            // The underlying Texture2D is owned by the ResourceManager cache and must
            // NOT be disposed by EffectsManager — disposing it would poison the cache
            // for the next PerformanceStage activation.
            Assert.False(fixture.HitTexture.WasDisposed);
        }

        [Fact]
        public void Dispose_ShouldCallRemoveReferenceOnCachedTextureSource()
        {
            // Simulate the real ResourceManager which calls AddReference() inside
            // LoadTexture. The fixture's LoadTexture mock already adds a reference;
            // Dispose() must balance it with RemoveReference().
            using var fixture = new EffectsManagerFixture(totalSprites: 4);
            var cachedTexture = ReflectionHelpers.GetPrivateField<ITexture>(fixture.Manager, "_cachedTextureSource");
            Assert.NotNull(cachedTexture);
            // Fixture sets up one AddReference; Dispose must remove exactly one.
            var initialRefCount = cachedTexture!.ReferenceCount;

            fixture.Manager.Dispose();

            var finalRefCount = cachedTexture.ReferenceCount;
            Assert.Equal(initialRefCount - 1, finalRefCount);
        }

        [Fact]
        public void Constructor_DisposingFirstInstance_ShouldNotPoisonCacheForSecondInstance()
        {
            // Regression test for the P1 "disposed cached texture" bug: two sequential
            // PerformanceStage activations construct two EffectsManagers backed by the
            // same cached ITexture. After the first is disposed, the second must still
            // see a usable (non-disposed) texture.
            var hitTexture = CreateTrackingTexture(width: 8 * 4, height: 32);
            var graphicsDevice = CreateGraphicsDeviceStub();
            var sharedCachedTexture = new ManagedTexture(graphicsDevice, hitTexture, "Graphics/hit_fx.png");
            var resourceManager = new Mock<IResourceManager>();
            resourceManager
                .Setup(manager => manager.LoadTexture("Graphics/hit_fx.png"))
                .Returns(() =>
                {
                    // Mirror real ResourceManager: bump refcount on each handout.
                    sharedCachedTexture.AddReference();
                    return sharedCachedTexture;
                });

            var first = new EffectsManager(graphicsDevice, resourceManager.Object);
            first.Dispose();

            // The shared Texture2D must still be alive for the second consumer.
            Assert.False(hitTexture.WasDisposed,
                "EffectsManager.Dispose() must not dispose the shared cached Texture2D");

            // Second construction must succeed and observe a valid sprite sheet.
            var second = new EffectsManager(graphicsDevice, resourceManager.Object);
            try
            {
                Assert.True(ReflectionHelpers.GetPrivateField<bool>(second, "_effectsEnabled"));
                var sprite = ReflectionHelpers.GetPrivateField<ManagedSpriteTexture>(second, "_hitEffectTexture");
                Assert.NotNull(sprite);
                Assert.True(sprite!.TotalSprites > 0);
            }
            finally
            {
                second.Dispose();
            }
        }

        #endregion

        #region Helpers

        private static T CreateUninitialized<T>() where T : class
        {
            return (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
        }

        private static GraphicsDevice CreateGraphicsDeviceStub()
        {
            return CreateUninitialized<GraphicsDevice>();
        }

        private static ManagedTexture CreateManagedTexture(int width, int height, string sourcePath)
        {
            return CreateManagedTexture(width, height, sourcePath, out _);
        }

        private static ManagedTexture CreateManagedTexture(int width, int height, string sourcePath, out TrackingTexture2D trackingTexture)
        {
            trackingTexture = CreateTrackingTexture(width, height);
            return new ManagedTexture(CreateGraphicsDeviceStub(), trackingTexture, sourcePath);
        }

        private static TrackingTexture2D CreateTrackingTexture(int width, int height)
        {
            var texture = CreateUninitialized<TrackingTexture2D>();
            ReflectionHelpers.SetPrivateField(texture, "width", width);
            ReflectionHelpers.SetPrivateField(texture, "height", height);
            ReflectionHelpers.SetPrivateField(texture, "<TexelWidth>k__BackingField", 1f / width);
            ReflectionHelpers.SetPrivateField(texture, "<TexelHeight>k__BackingField", 1f / height);
            return texture;
        }

        private static ManagedSpriteTexture CreateSpriteTexture(int totalSprites)
        {
            var texture = CreateUninitialized<ManagedSpriteTexture>();
            ReflectionHelpers.SetPrivateField(texture, "_spriteWidth", 8);
            ReflectionHelpers.SetPrivateField(texture, "_spriteHeight", 32);
            ReflectionHelpers.SetPrivateField(texture, "_spritesPerRow", Math.Max(1, totalSprites));
            ReflectionHelpers.SetPrivateField(texture, "_totalSprites", totalSprites);
            ReflectionHelpers.SetPrivateField(texture, "_texture", null);
            return texture;
        }

        private static object CreateEffectInstance(int frameIndex, int totalFrames)
        {
            var effectType = typeof(EffectsManager).GetNestedType("EffectInstance", System.Reflection.BindingFlags.NonPublic);
            Assert.NotNull(effectType);
            var effect = Activator.CreateInstance(effectType!, new object[] { Vector2.Zero, totalFrames });
            Assert.NotNull(effect);
            ReflectionHelpers.SetPrivateField(effect!, "_frameIndex", frameIndex);
            return effect!;
        }

        private static object CreateActiveEffectsList(params object[] effects)
        {
            var effectType = typeof(EffectsManager).GetNestedType("EffectInstance", System.Reflection.BindingFlags.NonPublic);
            Assert.NotNull(effectType);
            var listType = typeof(List<>).MakeGenericType(effectType!);
            var list = Activator.CreateInstance(listType);
            Assert.NotNull(list);
            var addMethod = listType.GetMethod("Add")!;
            foreach (var effect in effects)
            {
                addMethod.Invoke(list, new[] { effect });
            }
            return list!;
        }

        private sealed class EffectsManagerFixture : IDisposable
        {
            public EffectsManagerFixture(int totalSprites)
            {
                HitTexture = CreateTrackingTexture(width: 8 * totalSprites, height: 32);
                var graphicsDevice = CreateGraphicsDeviceStub();
                HitTextureSource = new ManagedTexture(graphicsDevice, HitTexture, "Graphics/hit_fx.png");
                var resourceManager = new Mock<IResourceManager>();
                resourceManager
                    .Setup(manager => manager.LoadTexture("Graphics/hit_fx.png"))
                    .Returns(() =>
                    {
                        // Mirror the real ResourceManager, which calls AddReference()
                        // before returning a cached texture. This lets refcount-based
                        // assertions in Dispose tests observe a RemoveReference() call.
                        HitTextureSource.AddReference();
                        return HitTextureSource;
                    });

                Manager = new EffectsManager(graphicsDevice, resourceManager.Object);
            }

            public EffectsManager Manager { get; }

            public TrackingTexture2D HitTexture { get; }

            public ManagedTexture HitTextureSource { get; }

            public void Dispose()
            {
                Manager.Dispose();
            }
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

        private sealed class TrackingTextureResource : ITexture
        {
            public Texture2D Texture => null!;

            public string SourcePath => "tracking";

            public int Width => 1;

            public int Height => 1;

            public Vector2 Size => Vector2.One;

            public bool IsDisposed { get; private set; }

            public int ReferenceCount => 0;

            public long MemoryUsage => 0;

            public int Transparency { get; set; } = 255;

            public Vector3 ScaleRatio { get; set; } = Vector3.One;

            public float ZAxisRotation { get; set; }

            public bool AdditiveBlending { get; set; }

            public int RemoveReferenceCount { get; private set; }

            public void AddReference()
            {
            }

            public ITexture Clone()
            {
                return this;
            }

            public Color[] GetColorData()
            {
                return Array.Empty<Color>();
            }

            public void RemoveReference()
            {
                RemoveReferenceCount++;
            }

            public void SetColorData(Color[] colorData)
            {
            }

            public void SaveToFile(string filePath)
            {
            }

            public void Draw(SpriteBatch spriteBatch, Vector2 position)
            {
            }

            public void Draw(SpriteBatch spriteBatch, Vector2 position, Rectangle? sourceRectangle)
            {
            }

            public void Draw(SpriteBatch spriteBatch, Rectangle destinationRectangle, Rectangle? sourceRectangle, Color color, float rotation, Vector2 origin, SpriteEffects effects, float layerDepth)
            {
            }

            public void Draw(SpriteBatch spriteBatch, Vector2 position, Vector2 scale, float rotation, Vector2 origin)
            {
            }

            public void Dispose()
            {
                IsDisposed = true;
            }
        }

        #endregion
    }
}
