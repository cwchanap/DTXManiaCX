using System;
using System.Linq;
using System.Runtime.Serialization;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Stage.Performance;
using DTXMania.Game.Lib.UI.Layout;
using DTXMania.Test.TestData;
using Moq;
using Xunit;

namespace DTXMania.Test.Stage.Performance
{
    /// <summary>
    /// Unit tests for PadRenderer component
    /// Tests pad visual state management without requiring graphics rendering
    /// </summary>
    public class PadRendererTests
    {
        [Fact]
        public void Constructor_NullGraphicsDevice_ThrowsArgumentNullException()
        {
            // Arrange, Act & Assert
            Assert.Throws<ArgumentNullException>(() => new PadRenderer(null, null));
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

        [Fact]
        public void TriggerPadPress_WhenLaneIsInvalid_ShouldLeavePadVisualsUnchanged()
        {
            var renderer = CreateRenderer();
            var visuals = GetPadVisuals(renderer);
            visuals[2].State = PadState.Pressed;
            visuals[2].TimePressed = 12.0;

            renderer.TriggerPadPress(-1);
            renderer.TriggerPadPress(PerformanceUILayout.LaneCount);

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

        private static PadRenderer CreateRenderer()
        {
#pragma warning disable SYSLIB0050
            var renderer = (PadRenderer)FormatterServices.GetUninitializedObject(typeof(PadRenderer));
#pragma warning restore SYSLIB0050
            ReflectionHelpers.SetPrivateField(
                renderer,
                "_padVisuals",
                Enumerable.Range(0, PerformanceUILayout.LaneCount)
                    .Select(_ => new PadVisual())
                    .ToArray());
            return renderer;
        }

        private static PadVisual[] GetPadVisuals(PadRenderer renderer)
        {
            return ReflectionHelpers.GetPrivateField<PadVisual[]>(renderer, "_padVisuals")!;
        }
    }
}
