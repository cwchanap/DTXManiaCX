using System;
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
        public void JudgementLineRenderer_GetJudgementLineRectangle_ShouldUseNxHitBarBoundsAndThickness()
        {
            var renderer = CreateUninitialized<JudgementLineRenderer>();
            renderer.LineThickness = 4;

            var rectangle = ReflectionHelpers.InvokePrivateMethod<Rectangle>(renderer, "GetJudgementLineRectangle");

            Assert.Equal(PerformanceUILayout.HitBar.Bounds.X, rectangle.X);
            Assert.Equal(PerformanceUILayout.HitBar.Bounds.Y, rectangle.Y);
            Assert.Equal(PerformanceUILayout.HitBar.Bounds.Width, rectangle.Width);
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
