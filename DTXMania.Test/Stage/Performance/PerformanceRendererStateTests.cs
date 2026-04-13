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
        public void BackgroundRenderer_Dispose_ShouldReleaseLoadedTextureAndResetFlags()
        {
            var resourceManager = new Mock<IResourceManager>();
            var renderer = new BackgroundRenderer(resourceManager.Object);
            var texture = new TrackingTextureResource();
            ReflectionHelpers.SetPrivateField(renderer, "_backgroundTexture", texture);
            ReflectionHelpers.SetPrivateField(renderer, "_isLoading", true);
            ReflectionHelpers.SetPrivateField(renderer, "_loadingFailed", true);

            renderer.Dispose();

            Assert.Equal(1, texture.RemoveReferenceCount);
            Assert.Null(ReflectionHelpers.GetPrivateField<ITexture>(renderer, "_backgroundTexture"));
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

            var exception = Record.Exception(() => renderer.DrawLane(null!, laneIndex));

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
        public void Dispose_ShouldDisposeHitTextureAndClearActiveEffects()
        {
            using var fixture = new EffectsManagerFixture(totalSprites: 4);
            fixture.Manager.SpawnHitEffect(0);

            fixture.Manager.Dispose();

            var activeEffects = ReflectionHelpers.GetPrivateField<System.Collections.IList>(fixture.Manager, "_activeEffects");
            Assert.Null(ReflectionHelpers.GetPrivateField<ManagedSpriteTexture>(fixture.Manager, "_hitEffectTexture"));
            Assert.Empty(activeEffects!.Cast<object>());
            Assert.True(fixture.HitTexture.WasDisposed);
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

        private sealed class EffectsManagerFixture : IDisposable
        {
            public EffectsManagerFixture(int totalSprites)
            {
                HitTexture = CreateTrackingTexture(width: 8 * totalSprites, height: 32);
                var graphicsDevice = CreateGraphicsDeviceStub();
                var loadedTexture = new ManagedTexture(graphicsDevice, HitTexture, "Graphics/hit_fx.png");
                var resourceManager = new Mock<IResourceManager>();
                resourceManager
                    .Setup(manager => manager.LoadTexture("Graphics/hit_fx.png"))
                    .Returns(loadedTexture);

                Manager = new EffectsManager(graphicsDevice, resourceManager.Object);
            }

            public EffectsManager Manager { get; }

            public TrackingTexture2D HitTexture { get; }

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
