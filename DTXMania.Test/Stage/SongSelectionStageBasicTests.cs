using DTXMania.Game.Lib.Stage;
using DTXMania.Game;
using System;
using System.Collections.Generic;
using Xunit;
using Moq;

namespace DTXMania.Test.Stage
{
    /// <summary>
    /// Basic unit tests for SongSelectionStage that don't require graphics initialization
    /// These tests focus on constructor validation and basic property testing
    /// </summary>
    public class SongSelectionStageBasicTests
    {
        #region Constructor Tests

        [Fact]
        public void Constructor_WithValidGame_ShouldCreateInstance()
        {
            // Arrange
            var mockGame = new Mock<BaseGame>();

            // Act
            var stage = new SongSelectionStage(mockGame.Object);

            // Assert
            Assert.NotNull(stage);
            Assert.Equal(StageType.SongSelect, stage.Type);
            Assert.Equal(StagePhase.Inactive, stage.CurrentPhase);
        }

        [Fact]
        public void Constructor_WithNullGame_ShouldThrowArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new SongSelectionStage(null));
        }

        #endregion

        #region Type Property Tests

        [Fact]
        public void Type_Property_ShouldReturnSongSelect()
        {
            // Arrange
            var mockGame = new Mock<BaseGame>();
            var stage = new SongSelectionStage(mockGame.Object);

            // Act
            var type = stage.Type;

            // Assert
            Assert.Equal(StageType.SongSelect, type);
        }

        #endregion

        #region Phase Management Tests

        [Fact]
        public void InitialPhase_ShouldBeInactive()
        {
            // Arrange
            var mockGame = new Mock<BaseGame>();
            var stage = new SongSelectionStage(mockGame.Object);

            // Act
            var phase = stage.CurrentPhase;

            // Assert
            Assert.Equal(StagePhase.Inactive, phase);
        }

        [Fact]
        public void Deactivate_WithoutActivation_ShouldHandleGracefully()
        {
            // Arrange
            var mockGame = new Mock<BaseGame>();
            var stage = new SongSelectionStage(mockGame.Object);

            // Act & Assert - Should not throw exception
            stage.Deactivate();
            Assert.Equal(StagePhase.Inactive, stage.CurrentPhase);
        }

        [Fact]
        public void Deactivate_MultipleCallsShouldBeIdempotent()
        {
            // Arrange
            var mockGame = new Mock<BaseGame>();
            var stage = new SongSelectionStage(mockGame.Object);

            // Act
            stage.Deactivate();
            stage.Deactivate(); // Second call

            // Assert - Should not throw exception and remain inactive
            Assert.Equal(StagePhase.Inactive, stage.CurrentPhase);
        }

        #endregion

        #region Background Music Tests

        [Fact]
        public void SetBackgroundMusic_WithNullParameters_ShouldHandleGracefully()
        {
            // Arrange
            var mockGame = new Mock<BaseGame>();
            var stage = new SongSelectionStage(mockGame.Object);

            // Act & Assert - Should not throw exception
            stage.SetBackgroundMusic(null, null);
            
            // Verify stage still in proper state
            Assert.Equal(StagePhase.Inactive, stage.CurrentPhase);
            Assert.Equal(StageType.SongSelect, stage.Type);
        }

        #endregion
    }
}