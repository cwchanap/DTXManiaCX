using System;
using Microsoft.Xna.Framework;
using DTXMania.Game.Lib.UI.Layout;
using Xunit;

namespace DTXMania.Test.UI
{
    /// <summary>
    /// Tests for PerformanceUILayout constants and methods
    /// </summary>
    [Trait("Category", "Unit")]
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
        public void JudgelineY_ShouldMatchNxDefaultDrumJudgeLine()
        {
            Assert.Equal(561, PerformanceUILayout.JudgelineY);
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

        [Fact]
        public void NXViewport_ShouldMatchResultAssetResolution()
        {
            Assert.Equal(1280, ResultUILayout.NXViewport.Width);
            Assert.Equal(720, ResultUILayout.NXViewport.Height);
        }

        [Fact]
        public void NXLayout_KeyPositions_ShouldMatchDrumsResultLayout()
        {
            Assert.Equal(new Vector2(480, 0), ResultUILayout.Rank.BadgePosition);
            Assert.Equal(new Vector2(315, 100), ResultUILayout.ResultPlate.Position);
            Assert.Equal(new Vector2(420, 156), ResultUILayout.ResultPlate.FailedTextPosition);
            Assert.Equal(new Vector2(467, 287), ResultUILayout.Jacket.PanelPosition);
            Assert.Equal(new Rectangle(519, 338, 245, 245), ResultUILayout.Jacket.PreviewDestination);
            Assert.Equal(new Vector2(180, 260), ResultUILayout.SkillPanel.PanelPosition);
            Assert.Equal(new Vector2(198, 550), ResultUILayout.SkillPanel.LevelPosition);
            Assert.Equal(new Vector2(238, 537), ResultUILayout.SkillPanel.PlayingSkillPosition);
            Assert.Equal(new Vector2(268, 623), ResultUILayout.SkillPanel.GameSkillPosition);
            Assert.Equal(new Vector2(260, 332), ResultUILayout.SkillPanel.PerfectCountPosition);
            Assert.Equal(new Vector2(260, 362), ResultUILayout.SkillPanel.GreatCountPosition);
            Assert.Equal(new Vector2(260, 392), ResultUILayout.SkillPanel.GoodCountPosition);
            Assert.Equal(new Vector2(260, 422), ResultUILayout.SkillPanel.PoorCountPosition);
            Assert.Equal(new Vector2(260, 452), ResultUILayout.SkillPanel.MissCountPosition);
            Assert.Equal(new Vector2(260, 482), ResultUILayout.SkillPanel.MaxComboCountPosition);
            Assert.Equal(new Vector2(347, 332), ResultUILayout.SkillPanel.PerfectPercentPosition);
            Assert.Equal(new Vector2(347, 362), ResultUILayout.SkillPanel.GreatPercentPosition);
            Assert.Equal(new Vector2(347, 392), ResultUILayout.SkillPanel.GoodPercentPosition);
            Assert.Equal(new Vector2(347, 422), ResultUILayout.SkillPanel.PoorPercentPosition);
            Assert.Equal(new Vector2(347, 452), ResultUILayout.SkillPanel.MissPercentPosition);
            Assert.Equal(new Vector2(347, 482), ResultUILayout.SkillPanel.MaxComboPercentPosition);
            Assert.Equal(new Vector2(30, 58), ResultUILayout.Score.Position);
            Assert.Equal(new Vector2(500, 630), ResultUILayout.SongInfo.TitlePosition);
            Assert.Equal(new Vector2(500, 665), ResultUILayout.SongInfo.ArtistPosition);
            Assert.Equal(320, ResultUILayout.SongInfo.MaxWidth);
            Assert.Equal(new Vector2(298, 582), ResultUILayout.NewRecord.BadgePosition);
        }

        [Fact]
        public void PlaybackPresentation_RowsAndGuidance_ShouldNotOverlap()
        {
            Assert.True(ResultUILayout.PlaybackPresentation.ProfilePosition.Y
                < ResultUILayout.PlaybackPresentation.ScoreBucketPosition.Y);
            Assert.True(ResultUILayout.PlaybackPresentation.ScoreBucketPosition.Y
                < ResultUILayout.PlaybackPresentation.SaveStatusPosition.Y);
            Assert.True(ResultUILayout.PlaybackPresentation.SaveStatusPosition.Y
                < ResultUILayout.PlaybackPresentation.SaveGuidanceBounds.Top);
            Assert.True(ResultUILayout.PlaybackPresentation.SaveGuidanceBounds.Left
                >= ResultUILayout.Jacket.PreviewDestination.Right);
            Assert.True(ResultUILayout.PlaybackPresentation.SaveGuidanceBounds.Right
                <= ResultUILayout.NXViewport.Width);
            Assert.True(ResultUILayout.PlaybackPresentation.SaveGuidanceBounds.Bottom
                <= ResultUILayout.NXViewport.Height);
        }

        [Fact]
        public void NXLayout_FontSizes_ShouldMatchDrumsResultLayout()
        {
            Assert.Equal(16, ResultUILayout.Fonts.Small);
            Assert.Equal(20, ResultUILayout.Fonts.Normal);
            Assert.Equal(32, ResultUILayout.Fonts.Large);
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

        #region Tab-mode HUD (HPA-509) Tests

        // HPA-509 layout contract: Tab HUD regions must sit in their spec bands and
        // never collide with the unchanged notation strip (y=290..580) or its key mask.

        [Fact]
        public void TabHud_Header_ShouldMatchSpecBounds()
        {
            Assert.Equal(16, PerformanceUILayout.TabHud.HeaderLeft);
            Assert.Equal(1264, PerformanceUILayout.TabHud.HeaderRight);
            Assert.Equal(12, PerformanceUILayout.TabHud.HeaderTop);
            Assert.Equal(82, PerformanceUILayout.TabHud.HeaderBottom);
        }

        [Fact]
        public void TabHud_HeaderPanels_ShouldNotOverlapAndKeepRowsInside()
        {
            Assert.True(PerformanceUILayout.TabHud.TitlePanelRight < PerformanceUILayout.TabHud.ScorePanelLeft);
            Assert.InRange(PerformanceUILayout.TabHud.HeaderTitleCenterY, PerformanceUILayout.TabHud.HeaderTop, PerformanceUILayout.TabHud.HeaderBottom);
            Assert.InRange(PerformanceUILayout.TabHud.HeaderArtistCenterY, PerformanceUILayout.TabHud.HeaderTitleCenterY, PerformanceUILayout.TabHud.HeaderBottom);
        }

        [Fact]
        public void TabHud_HitFeedback_ShouldCenterOnStripHitMarker()
        {
            Assert.Equal(PerformanceUILayout.NotationStrip.HitMarkerX, PerformanceUILayout.TabHud.HitFeedbackCenterX);
            Assert.Equal(90, PerformanceUILayout.TabHud.HitFeedbackTop);
            Assert.Equal(250, PerformanceUILayout.TabHud.HitFeedbackBottom);
        }

        [Fact]
        public void TabHud_HitFeedback_ShouldStayAboveHeaderAndStrip()
        {
            Assert.True(PerformanceUILayout.TabHud.HitFeedbackTop >= PerformanceUILayout.TabHud.HeaderBottom);
            Assert.True(PerformanceUILayout.TabHud.HitFeedbackBottom <= PerformanceUILayout.NotationStrip.StripTop);
        }

        [Fact]
        public void TabHud_Life_ShouldStartAtKeyColumnAndSitAboveStrip()
        {
            Assert.Equal(PerformanceUILayout.NotationStrip.KeyColumnWidth, PerformanceUILayout.TabHud.LifeLeft);
            Assert.Equal(1264, PerformanceUILayout.TabHud.LifeRight);
            Assert.Equal(258, PerformanceUILayout.TabHud.LifeTop);
            Assert.Equal(282, PerformanceUILayout.TabHud.LifeBottom);
            Assert.True(PerformanceUILayout.TabHud.LifeLabelRight < PerformanceUILayout.TabHud.LifeLeft);
            Assert.True(PerformanceUILayout.TabHud.LifeTop >= PerformanceUILayout.TabHud.HitFeedbackBottom);
            Assert.True(PerformanceUILayout.TabHud.LifeBottom <= PerformanceUILayout.NotationStrip.StripTop);
        }

        [Fact]
        public void TabHud_Progress_ShouldStartAtKeyColumnAndSitBelowStrip()
        {
            Assert.Equal(PerformanceUILayout.NotationStrip.KeyColumnWidth, PerformanceUILayout.TabHud.ProgressLeft);
            Assert.Equal(1264, PerformanceUILayout.TabHud.ProgressRight);
            Assert.Equal(588, PerformanceUILayout.TabHud.ProgressTop);
            Assert.Equal(614, PerformanceUILayout.TabHud.ProgressBottom);
            Assert.True(PerformanceUILayout.TabHud.ProgressTop >= PerformanceUILayout.NotationStrip.StripBottom);
        }

        [Fact]
        public void TabHud_StatsRow_ShouldSpanBottomBelowProgress()
        {
            Assert.Equal(16, PerformanceUILayout.TabHud.StatsLeft);
            Assert.Equal(1264, PerformanceUILayout.TabHud.StatsRight);
            Assert.Equal(624, PerformanceUILayout.TabHud.StatsTop);
            Assert.Equal(708, PerformanceUILayout.TabHud.StatsBottom);
            Assert.True(PerformanceUILayout.TabHud.StatsTop >= PerformanceUILayout.TabHud.ProgressBottom);
        }

        [Fact]
        public void TabHud_AllRegions_ShouldStayWithinVirtualScreen()
        {
            Assert.InRange(PerformanceUILayout.TabHud.HeaderLeft, 0, PerformanceUILayout.ScreenWidth);
            Assert.InRange(PerformanceUILayout.TabHud.HeaderRight, 0, PerformanceUILayout.ScreenWidth);
            Assert.InRange(PerformanceUILayout.TabHud.HeaderTop, 0, PerformanceUILayout.ScreenHeight);
            Assert.InRange(PerformanceUILayout.TabHud.HeaderBottom, 0, PerformanceUILayout.ScreenHeight);
            Assert.InRange(PerformanceUILayout.TabHud.LifeLeft, 0, PerformanceUILayout.ScreenWidth);
            Assert.InRange(PerformanceUILayout.TabHud.LifeRight, 0, PerformanceUILayout.ScreenWidth);
            Assert.InRange(PerformanceUILayout.TabHud.LifeTop, 0, PerformanceUILayout.ScreenHeight);
            Assert.InRange(PerformanceUILayout.TabHud.LifeBottom, 0, PerformanceUILayout.ScreenHeight);
            Assert.InRange(PerformanceUILayout.TabHud.ProgressLeft, 0, PerformanceUILayout.ScreenWidth);
            Assert.InRange(PerformanceUILayout.TabHud.ProgressRight, 0, PerformanceUILayout.ScreenWidth);
            Assert.InRange(PerformanceUILayout.TabHud.ProgressTop, 0, PerformanceUILayout.ScreenHeight);
            Assert.InRange(PerformanceUILayout.TabHud.ProgressBottom, 0, PerformanceUILayout.ScreenHeight);
            Assert.InRange(PerformanceUILayout.TabHud.StatsLeft, 0, PerformanceUILayout.ScreenWidth);
            Assert.InRange(PerformanceUILayout.TabHud.StatsRight, 0, PerformanceUILayout.ScreenWidth);
            Assert.InRange(PerformanceUILayout.TabHud.StatsTop, 0, PerformanceUILayout.ScreenHeight);
            Assert.InRange(PerformanceUILayout.TabHud.StatsBottom, 0, PerformanceUILayout.ScreenHeight);
        }

        [Fact]
        public void TabHud_DepthKeyFlash_ShouldOrderBetweenKeyCapFillAndBorder()
        {
            Assert.Equal(0.35f, PerformanceUILayout.TabHud.DepthKeyFlash);
            Assert.True(
                PerformanceUILayout.NotationStrip.DepthKeyMask > PerformanceUILayout.NotationStrip.DepthKeyCap,
                "BackToFront: key mask (0.40) must draw behind the key caps (0.38)");
            Assert.True(
                PerformanceUILayout.NotationStrip.DepthKeyCap > PerformanceUILayout.TabHud.DepthKeyFlash,
                "BackToFront: the cap fill (0.38) must draw behind the key flash (0.35)");
            Assert.True(
                PerformanceUILayout.TabHud.DepthKeyFlash > PerformanceUILayout.NotationStrip.DepthKeyCapBorder,
                "BackToFront: the key flash (0.35) must draw behind the cap border (0.33)");
            Assert.True(
                PerformanceUILayout.NotationStrip.DepthKeyCapBorder > PerformanceUILayout.NotationStrip.DepthLabels,
                "BackToFront: the cap border (0.33) must draw behind the labels (0.30) so labels stay legible");
        }

        [Fact]
        public void TabHud_UiPassDepths_ShouldStackPanelBehindBarsBehindText()
        {
            Assert.True(PerformanceUILayout.TabHud.DepthPanel > PerformanceUILayout.TabHud.DepthPanelBorder);
            Assert.True(PerformanceUILayout.TabHud.DepthPanelBorder > PerformanceUILayout.TabHud.DepthBarBack);
            Assert.True(PerformanceUILayout.TabHud.DepthBarBack > PerformanceUILayout.TabHud.DepthBarFill);
            Assert.True(PerformanceUILayout.TabHud.DepthBarFill > PerformanceUILayout.TabHud.DepthTextShadow);
            Assert.True(PerformanceUILayout.TabHud.DepthTextShadow > PerformanceUILayout.TabHud.DepthText);
        }

        #endregion
    }
}
