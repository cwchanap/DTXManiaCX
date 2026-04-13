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
    /// <summary>
    /// Unit tests for PadRenderer component
    /// Tests pad visual state management without requiring graphics rendering
    /// </summary>
    [Trait("Category", "Performance")]
    public class PadRendererTests
    {
        private sealed record SpriteSheetDrawCall(Rectangle Destination, Rectangle Source, Color Tint);
        private sealed record FallbackDrawCall(Rectangle Destination, Color Color);

        private sealed class TestablePadRenderer : PadRenderer
        {
            public List<SpriteSheetDrawCall> SpriteSheetDrawCalls { get; } = new();
            public List<FallbackDrawCall> FallbackDrawCalls { get; } = new();
            public int CreateFallbackTextureCalls { get; private set; }
            public Texture2D? NextFallbackTexture { get; set; }

            public TestablePadRenderer(GraphicsDevice graphicsDevice, IResourceManager resourceManager)
                : base(graphicsDevice, resourceManager)
            {
            }

            protected override void DrawPadSpriteCore(ITexture texture, SpriteBatch spriteBatch, Rectangle destination, Rectangle source, Color tint)
            {
                SpriteSheetDrawCalls.Add(new SpriteSheetDrawCall(destination, source, tint));
            }

            protected override Texture2D CreateFallbackTextureCore()
            {
                CreateFallbackTextureCalls++;
                return NextFallbackTexture ?? (Texture2D)RuntimeHelpers.GetUninitializedObject(typeof(Texture2D));
            }

            protected override void DrawFallbackTextureCore(SpriteBatch spriteBatch, Texture2D texture, Rectangle destination, Color color)
            {
                FallbackDrawCalls.Add(new FallbackDrawCall(destination, color));
            }
        }

        [Fact]
        public void Constructor_NullGraphicsDevice_ThrowsArgumentNullException()
        {
            // Arrange, Act & Assert
            Assert.Throws<ArgumentNullException>(() => new PadRenderer(null, null));
        }

        [Fact]
        public void Constructor_NullResourceManager_ThrowsArgumentNullException()
        {
            var graphicsDevice = CreateGraphicsDeviceStub();

            Assert.Throws<ArgumentNullException>(() => new PadRenderer(graphicsDevice, null!));
        }

        [Fact]
        public void PadVisual_StateEnum_HasCorrectValues()
        {
            // Arrange & Act
            var idleState = PadState.Idle;
            var pressedState = PadState.Pressed;

            // Assert
            Assert.Equal(0, (int)idleState);
            Assert.Equal(1, (int)pressedState);
        }

        [Fact]
        public void PadVisual_Constructor_InitializesWithIdleState()
        {
            // Arrange & Act
            var padVisual = new PadVisual();

            // Assert
            Assert.Equal(PadState.Idle, padVisual.State);
            Assert.Equal(0.0, padVisual.TimePressed);
        }

        [Fact]
        public void PadVisual_SetState_UpdatesCorrectly()
        {
            // Arrange
            var padVisual = new PadVisual();

            // Act
            padVisual.State = PadState.Pressed;
            padVisual.TimePressed = 50.0;

            // Assert
            Assert.Equal(PadState.Pressed, padVisual.State);
            Assert.Equal(50.0, padVisual.TimePressed);
        }

        [Fact]
        public void TriggerPadPress_WhenLaneIsValid_ShouldSetPadPressedAndResetTimer()
        {
            var renderer = CreateRenderer();
            var visuals = GetPadVisuals(renderer);
            visuals[4].TimePressed = 32.5;

            renderer.TriggerPadPress(4, isJudgedHit: false);

            Assert.Equal(PadState.Pressed, visuals[4].State);
            Assert.Equal(0.0, visuals[4].TimePressed);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(PerformanceUILayout.LaneCount)]
        public void TriggerPadPress_WhenLaneIsInvalid_ShouldLeavePadVisualsUnchanged(int laneIndex)
        {
            var renderer = CreateRenderer();
            var visuals = GetPadVisuals(renderer);
            visuals[2].State = PadState.Pressed;
            visuals[2].TimePressed = 12.0;

            renderer.TriggerPadPress(laneIndex);

            Assert.Equal(PadState.Pressed, visuals[2].State);
            Assert.Equal(12.0, visuals[2].TimePressed);
        }

        [Fact]
        public void Update_WhenPressedPadExceedsJudgedDuration_ShouldResetToIdle()
        {
            var renderer = CreateRenderer();
            var visuals = GetPadVisuals(renderer);
            visuals[1].State = PadState.Pressed;
            visuals[1].TimePressed = 0.0;

            renderer.Update(0.1);

            Assert.Equal(PadState.Idle, visuals[1].State);
            Assert.Equal(0.0, visuals[1].TimePressed);
        }

        [Fact]
        public void Update_WhenPressedPadDurationIsBelowThreshold_ShouldRemainPressed()
        {
            var renderer = CreateRenderer();
            var visuals = GetPadVisuals(renderer);
            visuals[1].State = PadState.Pressed;

            renderer.Update(0.05);

            Assert.Equal(PadState.Pressed, visuals[1].State);
            Assert.Equal(50.0, visuals[1].TimePressed, 3);
        }

        [Fact]
        public void Draw_WhenSpriteBatchIsNull_ShouldReturnWithoutThrowing()
        {
            var renderer = CreateRenderer();

            var exception = Record.Exception(() => renderer.Draw(null!));

            Assert.Null(exception);
        }

        [Fact]
        public void Constructor_WhenAlternateTexturePathSucceeds_ShouldLoadSpriteSheetAndCalculateCells()
        {
            var resourceManager = new Mock<IResourceManager>(MockBehavior.Strict);
            var spriteSheet = new Mock<ITexture>();
            spriteSheet.SetupGet(x => x.Width).Returns(400);
            spriteSheet.SetupGet(x => x.Height).Returns(150);
            resourceManager.SetupSequence(x => x.LoadTexture(It.IsAny<string>()))
                .Returns((ITexture)null!)
                .Returns(spriteSheet.Object);

            var renderer = new PadRenderer(CreateGraphicsDeviceStub(), resourceManager.Object);

            Assert.Same(spriteSheet.Object, ReflectionHelpers.GetPrivateField<ITexture>(renderer, "_padSpriteSheet"));
            Assert.Equal(4, ReflectionHelpers.GetPrivateField<int>(renderer, "_spriteColumns"));
            Assert.Equal(100, ReflectionHelpers.GetPrivateField<int>(renderer, "_cellWidth"));
            Assert.Equal(50, ReflectionHelpers.GetPrivateField<int>(renderer, "_cellHeight"));
            resourceManager.Verify(x => x.LoadTexture("Graphics/7_pads.png"), Times.Exactly(2));
        }

        [Fact]
        public void Constructor_WhenCalculateCellDimensionsThrows_ShouldClearSpriteSheet()
        {
            var resourceManager = new Mock<IResourceManager>();
            var spriteSheet = new Mock<ITexture>();
            spriteSheet.SetupGet(x => x.Width).Throws(new InvalidOperationException("width failed"));
            resourceManager.Setup(x => x.LoadTexture(It.IsAny<string>())).Returns(spriteSheet.Object);

            var renderer = new PadRenderer(CreateGraphicsDeviceStub(), resourceManager.Object);

            Assert.Null(ReflectionHelpers.GetPrivateField<ITexture>(renderer, "_padSpriteSheet"));
        }

        [Fact]
        public void CalculateCellDimensions_WhenSpriteSheetLoaded_ShouldUseExpectedGrid()
        {
            var renderer = CreateRenderer();
            var spriteSheet = new Mock<ITexture>();
            spriteSheet.SetupGet(x => x.Width).Returns(400);
            spriteSheet.SetupGet(x => x.Height).Returns(150);
            ReflectionHelpers.SetPrivateField(renderer, "_padSpriteSheet", spriteSheet.Object);

            ReflectionHelpers.InvokePrivateMethod(renderer, "CalculateCellDimensions");

            Assert.Equal(4, ReflectionHelpers.GetPrivateField<int>(renderer, "_spriteColumns"));
            Assert.Equal(100, ReflectionHelpers.GetPrivateField<int>(renderer, "_cellWidth"));
            Assert.Equal(50, ReflectionHelpers.GetPrivateField<int>(renderer, "_cellHeight"));
        }

        [Theory]
        [InlineData(0, 0, 0)]
        [InlineData(1, 1, 0)]
        [InlineData(2, 0, 2)]
        [InlineData(3, 0, 1)]
        [InlineData(4, 1, 1)]
        [InlineData(5, 1, 2)]
        [InlineData(6, 2, 1)]
        [InlineData(7, 3, 1)]
        [InlineData(8, 2, 0)]
        [InlineData(9, 3, 0)]
        [InlineData(99, -1, -1)]
        public void GetSpritePositionForLane_ShouldReturnExpectedMapping(int laneIndex, int expectedColumn, int expectedRow)
        {
            var renderer = CreateRenderer();

            var result = ReflectionHelpers.InvokePrivateMethod<(int columnIndex, int rowIndex)>(
                renderer,
                "GetSpritePositionForLane",
                laneIndex);

            Assert.Equal((expectedColumn, expectedRow), result);
        }

        [Fact]
        public void Dispose_ShouldReleaseSpriteSheetAndMarkRendererDisposed()
        {
            var renderer = CreateRenderer();
            var spriteSheet = new Mock<ITexture>();
            ReflectionHelpers.SetPrivateField(renderer, "_padSpriteSheet", spriteSheet.Object);

            renderer.Dispose();

            spriteSheet.Verify(x => x.RemoveReference(), Times.Once);
            Assert.Null(ReflectionHelpers.GetPrivateField<ITexture?>(renderer, "_padSpriteSheet"));
            Assert.True(ReflectionHelpers.GetPrivateField<bool>(renderer, "_disposed"));
        }

        [Fact]
        public void Draw_WhenSpriteSheetAvailable_ShouldDrawSpriteSheetPadsForAllLanes()
        {
            var resourceManager = new Mock<IResourceManager>();
            resourceManager.Setup(x => x.LoadTexture(It.IsAny<string>())).Returns((ITexture)null!);
            var renderer = new TestablePadRenderer(CreateGraphicsDeviceStub(), resourceManager.Object);
            var spriteSheet = new Mock<ITexture>();
            var spriteBatch = (SpriteBatch)RuntimeHelpers.GetUninitializedObject(typeof(SpriteBatch));

            ReflectionHelpers.SetPrivateField(renderer, "_padSpriteSheet", spriteSheet.Object);
            ReflectionHelpers.SetPrivateField(renderer, "_cellWidth", 100);
            ReflectionHelpers.SetPrivateField(renderer, "_cellHeight", 50);
            ReflectionHelpers.SetPrivateField(renderer, "_spriteColumns", 4);
            GetPadVisuals(renderer)[0].State = PadState.Pressed;

            renderer.Draw(spriteBatch);

            Assert.Equal(PerformanceUILayout.LaneCount, renderer.SpriteSheetDrawCalls.Count);
            Assert.Empty(renderer.FallbackDrawCalls);
            Assert.Equal(new Rectangle(0, 0, 100, 50), renderer.SpriteSheetDrawCalls[0].Source);
            Assert.Equal(Color.White * 1.5f, renderer.SpriteSheetDrawCalls[0].Tint);
            Assert.Equal(new Rectangle(PerformanceUILayout.GetLaneLeftX(1), 670, PerformanceUILayout.GetLaneWidth(1), 60), renderer.SpriteSheetDrawCalls[1].Destination);
        }

        [Fact]
        public void Draw_WhenCellDimensionsInvalid_ShouldUseFallbackPads()
        {
            var resourceManager = new Mock<IResourceManager>();
            resourceManager.Setup(x => x.LoadTexture(It.IsAny<string>())).Returns((ITexture)null!);
            var renderer = new TestablePadRenderer(CreateGraphicsDeviceStub(), resourceManager.Object)
            {
                NextFallbackTexture = (Texture2D)RuntimeHelpers.GetUninitializedObject(typeof(Texture2D))
            };
            var spriteSheet = new Mock<ITexture>();
            var spriteBatch = (SpriteBatch)RuntimeHelpers.GetUninitializedObject(typeof(SpriteBatch));

            ReflectionHelpers.SetPrivateField(renderer, "_padSpriteSheet", spriteSheet.Object);
            ReflectionHelpers.SetPrivateField(renderer, "_cellWidth", 0);
            ReflectionHelpers.SetPrivateField(renderer, "_cellHeight", 0);

            renderer.Draw(spriteBatch);

            Assert.Empty(renderer.SpriteSheetDrawCalls);
            Assert.Equal(PerformanceUILayout.LaneCount, renderer.FallbackDrawCalls.Count);
            Assert.Equal(1, renderer.CreateFallbackTextureCalls);
        }

        [Fact]
        public void Draw_WhenSpriteSheetMissing_ShouldDrawFallbackPadsForAllLanes()
        {
            var resourceManager = new Mock<IResourceManager>();
            resourceManager.Setup(x => x.LoadTexture(It.IsAny<string>())).Returns((ITexture)null!);
            var renderer = new TestablePadRenderer(CreateGraphicsDeviceStub(), resourceManager.Object)
            {
                NextFallbackTexture = (Texture2D)RuntimeHelpers.GetUninitializedObject(typeof(Texture2D))
            };
            var spriteBatch = (SpriteBatch)RuntimeHelpers.GetUninitializedObject(typeof(SpriteBatch));
            GetPadVisuals(renderer)[2].State = PadState.Pressed;

            renderer.Draw(spriteBatch);

            Assert.Empty(renderer.SpriteSheetDrawCalls);
            Assert.Equal(PerformanceUILayout.LaneCount, renderer.FallbackDrawCalls.Count);
            Assert.Equal(1, renderer.CreateFallbackTextureCalls);
            Assert.Equal(Color.Red, renderer.FallbackDrawCalls[2].Color);
            Assert.Equal(Color.Yellow, renderer.FallbackDrawCalls[0].Color);
        }

        private static PadRenderer CreateRenderer()
        {
            var resourceManager = new Mock<IResourceManager>();
            resourceManager.Setup(x => x.LoadTexture(It.IsAny<string>())).Returns((ITexture)null!);
            return new PadRenderer(CreateGraphicsDeviceStub(), resourceManager.Object);
        }

        private static Microsoft.Xna.Framework.Graphics.GraphicsDevice CreateGraphicsDeviceStub()
        {
            return (Microsoft.Xna.Framework.Graphics.GraphicsDevice)RuntimeHelpers.GetUninitializedObject(
                typeof(Microsoft.Xna.Framework.Graphics.GraphicsDevice));
        }

        private static PadVisual[] GetPadVisuals(PadRenderer renderer)
        {
            return ReflectionHelpers.GetPrivateField<PadVisual[]>(renderer, "_padVisuals")!;
        }
    }
}
