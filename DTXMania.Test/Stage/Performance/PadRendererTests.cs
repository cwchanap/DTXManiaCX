using System;
using Xunit;
using DTXMania.Game.Lib.Stage.Performance;

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
    }
}