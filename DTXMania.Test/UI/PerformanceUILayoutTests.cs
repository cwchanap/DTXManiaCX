using System;
using Microsoft.Xna.Framework;
using DTXMania.Game.Lib.UI.Layout;
using Xunit;

namespace DTXMania.Test.UI
{
    /// <summary>
    /// Tests for PerformanceUILayout constants and methods
    /// </summary>
    public class PerformanceUILayoutTests
    {
        #region Screen Configuration Tests

        [Fact]
        public void ScreenWidth_ShouldBe1280()
        {
            Assert.Equal(1280, PerformanceUILayout.ScreenWidth);
        }

        [Fact]
        public void ScreenHeight_ShouldBe720()
        {
            Assert.Equal(720, PerformanceUILayout.ScreenHeight);
        }

        [Fact]
        public void ScreenCenter_ShouldBeCenter()
        {
            var center = PerformanceUILayout.ScreenCenter;
            Assert.Equal(640, center.X);
            Assert.Equal(360, center.Y);
        }

        [Fact]
        public void ScreenSize_ShouldMatchWidthHeight()
        {
            var size = PerformanceUILayout.ScreenSize;
            Assert.Equal(PerformanceUILayout.ScreenWidth, size.X);
            Assert.Equal(PerformanceUILayout.ScreenHeight, size.Y);
        }

        [Fact]
        public void JudgelineY_ShouldBe600()
        {
            Assert.Equal(600, PerformanceUILayout.JudgelineY);
        }

        [Fact]
        public void LaneCount_ShouldBe10()
        {
            Assert.Equal(10, PerformanceUILayout.LaneCount);
        }

        #endregion

        #region Lane Position Method Tests

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        [InlineData(6)]
        [InlineData(7)]
        [InlineData(8)]
        [InlineData(9)]
        public void GetLaneX_ValidIndex_ShouldReturnPositiveValue(int laneIndex)
        {
            var x = PerformanceUILayout.GetLaneX(laneIndex);
            Assert.True(x > 0);
        }

        [Fact]
        public void GetLaneX_NegativeIndex_ShouldThrow()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => PerformanceUILayout.GetLaneX(-1));
        }

        [Fact]
        public void GetLaneX_TooLargeIndex_ShouldThrow()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => PerformanceUILayout.GetLaneX(10));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(5)]
        [InlineData(9)]
        public void GetLaneLeftX_ValidIndex_ShouldReturnPositiveValue(int laneIndex)
        {
            var x = PerformanceUILayout.GetLaneLeftX(laneIndex);
            Assert.True(x >= 0);
        }

        [Fact]
        public void GetLaneLeftX_NegativeIndex_ShouldThrow()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => PerformanceUILayout.GetLaneLeftX(-1));
        }

        [Fact]
        public void GetLaneLeftX_TooLargeIndex_ShouldThrow()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => PerformanceUILayout.GetLaneLeftX(10));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(5)]
        [InlineData(9)]
        public void GetLaneWidth_ValidIndex_ShouldReturnPositiveValue(int laneIndex)
        {
            var width = PerformanceUILayout.GetLaneWidth(laneIndex);
            Assert.True(width > 0);
        }

        [Fact]
        public void GetLaneWidth_NegativeIndex_ShouldThrow()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => PerformanceUILayout.GetLaneWidth(-1));
        }

        [Fact]
        public void GetLaneRightX_Lane0_ShouldBeLeftPlusWidth()
        {
            const int laneIndex = 0;
            var right = PerformanceUILayout.GetLaneRightX(laneIndex);
            var expected = PerformanceUILayout.GetLaneLeftX(laneIndex) + PerformanceUILayout.GetLaneWidth(laneIndex);
            Assert.Equal(expected, right);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(9)]
        public void GetLaneCenterX_ShouldEqualGetLaneX(int laneIndex)
        {
            Assert.Equal(PerformanceUILayout.GetLaneX(laneIndex), PerformanceUILayout.GetLaneCenterX(laneIndex));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(5)]
        [InlineData(9)]
        public void GetLaneRectangle_ShouldHaveCorrectDimensions(int laneIndex)
        {
            var rect = PerformanceUILayout.GetLaneRectangle(laneIndex);
            Assert.Equal(PerformanceUILayout.GetLaneLeftX(laneIndex), rect.X);
            Assert.Equal(0, rect.Y);
            Assert.Equal(PerformanceUILayout.GetLaneWidth(laneIndex), rect.Width);
            Assert.Equal(PerformanceUILayout.LaneHeight, rect.Height);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(3)]
        [InlineData(9)]
        public void GetLaneColor_ValidIndex_ShouldReturnColor(int laneIndex)
        {
            var color = PerformanceUILayout.GetLaneColor(laneIndex);
            // Color should be non-black (each lane has a distinct color)
            Assert.True(color.R > 0 || color.G > 0 || color.B > 0);
        }

        [Fact]
        public void GetLaneColor_NegativeIndex_ShouldThrow()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => PerformanceUILayout.GetLaneColor(-1));
        }

        [Fact]
        public void GetLaneColor_TooLargeIndex_ShouldThrow()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => PerformanceUILayout.GetLaneColor(10));
        }

        #endregion

        #region Lane Data Arrays Tests

        [Fact]
        public void LaneNames_ShouldHave10Entries()
        {
            Assert.Equal(10, PerformanceUILayout.LaneNames.Length);
        }

        [Fact]
        public void LaneCenterX_ShouldHave10Entries()
        {
            Assert.Equal(10, PerformanceUILayout.LaneCenterX.Length);
        }

        [Fact]
        public void LaneWidths_ShouldHave10Entries()
        {
            Assert.Equal(10, PerformanceUILayout.LaneWidths.Length);
        }

        [Fact]
        public void LaneLeftX_ShouldHave10Entries()
        {
            Assert.Equal(10, PerformanceUILayout.LaneLeftX.Length);
        }

        #endregion

        #region Score Display Tests

        [Fact]
        public void Score_MaxDigits_ShouldBe7()
        {
            Assert.Equal(7, PerformanceUILayout.Score.MaxDigits);
        }

        [Fact]
        public void Score_DigitSpacing_ShouldBePositive()
        {
            Assert.True(PerformanceUILayout.Score.DigitSpacing > 0);
        }

        #endregion

        #region Gauge Tests

        [Fact]
        public void Gauge_FillHeight_ShouldBePositive()
        {
            Assert.True(PerformanceUILayout.Gauge.FillHeight > 0);
        }

        [Fact]
        public void Gauge_HiSpeedBadge_GetPosition_ShouldReturnCalculatedPosition()
        {
            var pos = PerformanceUILayout.Gauge.HiSpeedBadge.GetPosition(200);
            // Position.X = BasePosition.X + gaugeFrameWidth + Offset.X
            Assert.True(pos.X > 0);
        }

        #endregion

        #region Legacy Compatibility Tests

        [Fact]
        public void ScorePosition_ShouldMatchScoreFirstDigitPosition()
        {
            Assert.Equal(PerformanceUILayout.Score.FirstDigitPosition, PerformanceUILayout.ScorePosition);
        }

        [Fact]
        public void ComboPosition_ShouldMatchComboBasePosition()
        {
            Assert.Equal(PerformanceUILayout.Combo.BasePosition, PerformanceUILayout.ComboPosition);
        }

        [Fact]
        public void GaugePosition_ShouldMatchGaugeFramePosition()
        {
            Assert.Equal(PerformanceUILayout.Gauge.FramePosition, PerformanceUILayout.GaugePosition);
        }

        #endregion

        #region LaneType Enum Tests

        [Fact]
        public void LaneType_Values_ShouldBeConsistent()
        {
            Assert.Equal(0, (int)PerformanceUILayout.LaneType.LC);
            Assert.Equal(1, (int)PerformanceUILayout.LaneType.HH);
            Assert.Equal(9, (int)PerformanceUILayout.LaneType.RD);
        }

        #endregion
    }

    /// <summary>
    /// Tests for ResultUILayout constants
    /// </summary>
    public class ResultUILayoutTests
    {
        [Fact]
        public void ResultDisplay_StartY_ShouldBePositive()
        {
            Assert.True(ResultUILayout.ResultDisplay.StartY > 0);
        }

        [Fact]
        public void ResultDisplay_LineHeight_ShouldBePositive()
        {
            Assert.True(ResultUILayout.ResultDisplay.LineHeight > 0);
        }

        [Fact]
        public void FallbackText_CharacterWidth_ShouldBePositive()
        {
            Assert.True(ResultUILayout.FallbackText.CharacterWidth > 0);
        }

        [Fact]
        public void FallbackText_RectHeight_ShouldBePositive()
        {
            Assert.True(ResultUILayout.FallbackText.RectHeight > 0);
        }

        [Fact]
        public void ResultDisplay_TitleColor_ShouldNotBeDefault()
        {
            var color = ResultUILayout.ResultDisplay.TitleColor;
            // Yellow color requires both red and green components
            Assert.True(color.R > 0 && color.G > 0);
        }

        [Fact]
        public void Background_BackgroundColor_ShouldBeAccessible()
        {
            var color = ResultUILayout.Background.BackgroundColor;
            // Should not throw
            Assert.NotEqual(Color.Transparent, color);
        }
    }

    /// <summary>
    /// Tests for SongTransitionUILayout constants and methods
    /// </summary>
    public class SongTransitionUILayoutTests
    {
        [Fact]
        public void MainPanel_BackgroundAlpha_ShouldBe0Point8()
        {
            Assert.Equal(0.8f, SongTransitionUILayout.MainPanel.BackgroundAlpha);
        }

        [Fact]
        public void MainPanel_BackgroundColor_ShouldBeAccessible()
        {
            var color = SongTransitionUILayout.MainPanel.BackgroundColor;
            Assert.NotEqual(Color.Transparent, color);
        }

        [Fact]
        public void SongTitle_Position_ShouldHaveCorrectCoordinates()
        {
            var pos = SongTransitionUILayout.SongTitle.Position;
            Assert.Equal(SongTransitionUILayout.SongTitle.X, pos.X);
            Assert.Equal(SongTransitionUILayout.SongTitle.Y, pos.Y);
        }

        [Fact]
        public void SongTitle_Size_ShouldHaveCorrectDimensions()
        {
            var size = SongTransitionUILayout.SongTitle.Size;
            Assert.Equal(SongTransitionUILayout.SongTitle.Width, size.X);
            Assert.Equal(SongTransitionUILayout.SongTitle.Height, size.Y);
        }

        [Fact]
        public void SongTitle_TextColor_ShouldBeAccessible()
        {
            var color = SongTransitionUILayout.SongTitle.TextColor;
            Assert.NotEqual(Color.Transparent, color);
        }

        [Fact]
        public void Artist_Position_ShouldHaveCorrectCoordinates()
        {
            var pos = SongTransitionUILayout.Artist.Position;
            Assert.Equal(SongTransitionUILayout.Artist.X, pos.X);
            Assert.Equal(SongTransitionUILayout.Artist.Y, pos.Y);
        }

        [Fact]
        public void Artist_Size_ShouldHaveCorrectDimensions()
        {
            var size = SongTransitionUILayout.Artist.Size;
            Assert.Equal(SongTransitionUILayout.Artist.Width, size.X);
            Assert.Equal(SongTransitionUILayout.Artist.Height, size.Y);
        }

        [Fact]
        public void Artist_TextColor_ShouldBeAccessible()
        {
            var color = SongTransitionUILayout.Artist.TextColor;
            Assert.NotEqual(Color.Transparent, color);
        }

        [Fact]
        public void SongTitle_X_ShouldBePositive()
        {
            Assert.True(SongTransitionUILayout.SongTitle.X >= 0);
        }

        [Fact]
        public void SongTitle_FontSize_ShouldBePositive()
        {
            Assert.True(SongTransitionUILayout.SongTitle.FontSize > 0);
        }
    }
}
