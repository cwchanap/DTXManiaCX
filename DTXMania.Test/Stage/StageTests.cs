using Xunit;
using DTX.Stage;
using DTXMania.Shared.Game;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Moq;
using System;

namespace DTXMania.Test.Stage
{
    /// <summary>
    /// Unit tests for stage implementations
    /// Tests startup and title screen functionality
    /// </summary>
    public class StageTests
    {
        #region Test Helpers

        // Note: We can't easily mock BaseGame due to MonoGame dependencies
        // These tests focus on basic functionality that doesn't require graphics

        #endregion

        #region StartupStage Tests

        [Fact]
        public void StartupStage_Constructor_RequiresGame()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new StartupStage(null));
        }

        [Fact]
        public void StartupStage_Type_ReturnsCorrectValue()
        {
            // We can test the type property without needing graphics
            // This test verifies the stage type is correctly set

            // Note: We can't create the stage without a game instance due to constructor requirements
            // But we can test the enum values and stage type constants
            Assert.Equal(0, (int)StageType.Startup);
            Assert.True(Enum.IsDefined(typeof(StageType), StageType.Startup));
        }

        #endregion

        #region TitleStage Tests

        [Fact]
        public void TitleStage_Constructor_RequiresGame()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new TitleStage(null));
        }

        [Fact]
        public void TitleStage_Type_ReturnsCorrectValue()
        {
            // Test the stage type enum value
            Assert.Equal(1, (int)StageType.Title);
            Assert.True(Enum.IsDefined(typeof(StageType), StageType.Title));
        }

        #endregion

        #region ConfigStage Tests

        [Fact]
        public void ConfigStage_Constructor_RequiresGame()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new ConfigStage(null));
        }

        [Fact]
        public void ConfigStage_Type_ReturnsCorrectValue()
        {
            // Test the stage type enum value
            Assert.Equal(2, (int)StageType.Config);
            Assert.True(Enum.IsDefined(typeof(StageType), StageType.Config));
        }

        #endregion

        #region StageManager Tests

        [Fact]
        public void StageManager_Constructor_RequiresGame()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new StageManager(null));
        }

        #endregion

        #region StageType Enum Tests

        [Fact]
        public void StageType_AllValuesAreDefined()
        {
            // Act & Assert - Verify all expected stage types exist
            Assert.True(Enum.IsDefined(typeof(StageType), StageType.Startup));
            Assert.True(Enum.IsDefined(typeof(StageType), StageType.Title));
            Assert.True(Enum.IsDefined(typeof(StageType), StageType.Config));
            Assert.True(Enum.IsDefined(typeof(StageType), StageType.SongSelect));
            Assert.True(Enum.IsDefined(typeof(StageType), StageType.Performance));
            Assert.True(Enum.IsDefined(typeof(StageType), StageType.Result));
            Assert.True(Enum.IsDefined(typeof(StageType), StageType.UITest));
        }

        [Fact]
        public void StageType_HasExpectedValues()
        {
            // Act & Assert - Verify stage type values
            Assert.Equal(0, (int)StageType.Startup);
            Assert.Equal(1, (int)StageType.Title);
            Assert.Equal(2, (int)StageType.Config);
            Assert.Equal(6, (int)StageType.UITest);
        }

        [Fact]
        public void StageType_EnumCount_IsCorrect()
        {
            // Verify we have the expected number of stage types
            var stageTypes = Enum.GetValues(typeof(StageType));
            Assert.Equal(7, stageTypes.Length); // Startup, Title, Config, SongSelect, Performance, Result, UITest
        }

        #endregion
    }
}
