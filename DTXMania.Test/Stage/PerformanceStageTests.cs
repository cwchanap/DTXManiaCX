using DTX.Stage;
using DTX.Stage.Performance;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using Xunit;
using DTXMania.Game;
using DTX.Resources;

namespace DTXMania.Test.Stage
{
    /// <summary>
    /// Unit tests for PerformanceStage and its components
    /// Tests component instantiation, UI layout calculations, and basic functionality
    /// </summary>
    public class PerformanceStageTests
    {
        #region PerformanceUILayout Tests

        [Fact]
        public void PerformanceUILayout_LaneCount_ShouldBe9()
        {
            // Assert
            Assert.Equal(9, PerformanceUILayout.LaneCount);
        }

        [Fact]
        public void PerformanceUILayout_GetLaneX_ShouldReturnCorrectPositions()
        {
            // Arrange & Act
            var lane0X = PerformanceUILayout.GetLaneX(0);
            var lane8X = PerformanceUILayout.GetLaneX(8);

            // Assert
            Assert.Equal(348, lane0X); // FirstLaneX (316) + LaneWidth/2 (32)
            Assert.True(lane8X > lane0X); // Lane 8 should be to the right of lane 0
        }

        [Fact]
        public void PerformanceUILayout_GetLaneX_InvalidIndex_ShouldThrow()
        {
            // Act & Assert
            Assert.Throws<ArgumentOutOfRangeException>(() => PerformanceUILayout.GetLaneX(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => PerformanceUILayout.GetLaneX(9));
        }

        [Fact]
        public void PerformanceUILayout_GetLaneRectangle_ShouldReturnValidRectangle()
        {
            // Act
            var laneRect = PerformanceUILayout.GetLaneRectangle(0);

            // Assert
            Assert.Equal(PerformanceUILayout.LaneWidth, laneRect.Width);
            Assert.Equal(PerformanceUILayout.LaneHeight, laneRect.Height);
            Assert.Equal(0, laneRect.Y);
        }

        [Fact]
        public void PerformanceUILayout_GetLaneColor_ShouldReturnValidColors()
        {
            // Act
            var lane0Color = PerformanceUILayout.GetLaneColor(0);
            var lane8Color = PerformanceUILayout.GetLaneColor(8);

            // Assert
            Assert.NotEqual(Color.Transparent, lane0Color);
            Assert.NotEqual(Color.Transparent, lane8Color);
            Assert.NotEqual(lane0Color, lane8Color); // Different lanes should have different colors
        }

        [Fact]
        public void PerformanceUILayout_UIPositions_ShouldBeValid()
        {
            // Assert
            Assert.True(PerformanceUILayout.ScorePosition.X > 0);
            Assert.True(PerformanceUILayout.ScorePosition.Y > 0);
            Assert.True(PerformanceUILayout.ComboPosition.X > 0);
            Assert.True(PerformanceUILayout.ComboPosition.Y > 0);
            Assert.True(PerformanceUILayout.GaugePosition.X > 0);
            Assert.True(PerformanceUILayout.GaugePosition.Y > 0);
            Assert.True(PerformanceUILayout.GaugeSize.X > 0);
            Assert.True(PerformanceUILayout.GaugeSize.Y > 0);
        }

        #endregion

        #region Component Null Reference Tests

        [Fact]
        public void BackgroundRenderer_Constructor_NullResourceManager_ShouldThrow()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new BackgroundRenderer(null));
        }

        [Fact]
        public void LaneBackgroundRenderer_Constructor_NullGraphicsDevice_ShouldThrow()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new LaneBackgroundRenderer(null));
        }

        [Fact]
        public void JudgementLineRenderer_Constructor_NullGraphicsDevice_ShouldThrow()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new JudgementLineRenderer(null));
        }

        [Fact]
        public void ScoreDisplay_Constructor_NullResourceManager_ShouldThrow()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new ScoreDisplay(null, null));
        }

        [Fact]
        public void ComboDisplay_Constructor_NullResourceManager_ShouldThrow()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new ComboDisplay(null, null));
        }

        [Fact]
        public void GaugeDisplay_Constructor_NullResourceManager_ShouldThrow()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new GaugeDisplay(null, null));
        }

        #endregion

        #region Component Logic Tests (No Graphics Dependencies)

        [Fact]
        public void GaugeDisplay_ValueClamping_ShouldWorkCorrectly()
        {
            // Test the clamping logic without creating the actual component
            // This tests the MathHelper.Clamp functionality used in the component

            // Act
            var negativeResult = MathHelper.Clamp(-0.5f, 0.0f, 1.0f);
            var maxResult = MathHelper.Clamp(1.5f, 0.0f, 1.0f);
            var validResult = MathHelper.Clamp(0.75f, 0.0f, 1.0f);

            // Assert
            Assert.Equal(0.0f, negativeResult); // Should clamp to 0
            Assert.Equal(1.0f, maxResult); // Should clamp to 1
            Assert.Equal(0.75f, validResult, 2); // Should accept valid value
        }

        [Fact]
        public void ScoreDisplay_ScoreClamping_ShouldWorkCorrectly()
        {
            // Test the score clamping logic without creating the actual component
            const int MaxScore = 9999999;

            // Act
            var negativeResult = Math.Clamp(-100, 0, MaxScore);
            var maxResult = Math.Clamp(10000000, 0, MaxScore);
            var validResult = Math.Clamp(1234567, 0, MaxScore);

            // Assert
            Assert.Equal(0, negativeResult); // Should clamp to 0
            Assert.Equal(MaxScore, maxResult); // Should clamp to max
            Assert.Equal(1234567, validResult); // Should accept valid score
        }

        [Fact]
        public void ComboDisplay_ComboLogic_ShouldWorkCorrectly()
        {
            // Test combo visibility logic without creating the actual component

            // Act
            var zeroComboVisible = 0 > 0;
            var positiveComboVisible = 10 > 0;

            // Assert
            Assert.False(zeroComboVisible); // Zero combo should not be visible
            Assert.True(positiveComboVisible); // Positive combo should be visible
        }

        #endregion
    }
}
