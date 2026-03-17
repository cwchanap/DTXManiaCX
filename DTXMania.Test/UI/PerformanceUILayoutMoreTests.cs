using Microsoft.Xna.Framework;
using DTXMania.Game.Lib.UI.Layout;
using DTXMania.Game.Lib.Song.Components;
using Xunit;

namespace DTXMania.Test.UI
{
    /// <summary>
    /// Additional tests for PerformanceUILayout static methods and inner classes
    /// </summary>
    public class PerformanceUILayoutMoreTests
    {
        #region LaneStrips Tests

        [Theory]
        [InlineData(0)]
        [InlineData(5)]
        [InlineData(9)]
        public void LaneStrips_GetDestinationRect_ShouldMatchLaneData(int laneIndex)
        {
            var rect = PerformanceUILayout.LaneStrips.GetDestinationRect(laneIndex);
            Assert.Equal(PerformanceUILayout.LaneLeftX[laneIndex], rect.X);
            Assert.Equal(0, rect.Y);
            Assert.Equal(PerformanceUILayout.LaneWidths[laneIndex], rect.Width);
            Assert.Equal(PerformanceUILayout.LaneHeight, rect.Height);
        }

        [Fact]
        public void LaneStrips_SourceRects_ShouldHave10Entries()
        {
            Assert.Equal(10, PerformanceUILayout.LaneStrips.SourceRects.Length);
        }

        #endregion

        #region HitSparks Tests

        [Theory]
        [InlineData(0)]
        [InlineData(5)]
        [InlineData(9)]
        public void HitSparks_GetSparkPosition_ShouldMatchLaneCenter(int laneIndex)
        {
            var pos = PerformanceUILayout.HitSparks.GetSparkPosition(laneIndex);
            Assert.Equal(PerformanceUILayout.LaneCenterX[laneIndex], pos.X);
            Assert.Equal(PerformanceUILayout.JudgelineY, pos.Y);
        }

        #endregion

        #region Legacy Asset Layout Tests

        [Fact]
        public void JudgementTextAssets_FrameConstants_ShouldBeValid()
        {
            Assert.Equal(0, PerformanceUILayout.JudgementTextAssets.PerfectFrame);
            Assert.Equal(1, PerformanceUILayout.JudgementTextAssets.GreatFrame);
            Assert.Equal(2, PerformanceUILayout.JudgementTextAssets.GoodFrame);
            Assert.Equal(3, PerformanceUILayout.JudgementTextAssets.PoorFrame);
            Assert.Equal(4, PerformanceUILayout.JudgementTextAssets.MissFrame);
        }

        [Fact]
        public void TimingIndicatorAssets_FrameConstants_ShouldBeValid()
        {
            Assert.Equal(0, PerformanceUILayout.TimingIndicatorAssets.EarlyFrame);
            Assert.Equal(1, PerformanceUILayout.TimingIndicatorAssets.LateFrame);
        }

        [Fact]
        public void ExplosionAssets_TotalFrames_ShouldMatchRowsTimesCols()
        {
            Assert.Equal(PerformanceUILayout.ExplosionAssets.AtlasRows * PerformanceUILayout.ExplosionAssets.AtlasCols,
                PerformanceUILayout.ExplosionAssets.TotalFrames);
        }

        [Fact]
        public void LaneStrips_SourceRects_AllShouldHavePositiveWidth()
        {
            foreach (var rect in PerformanceUILayout.LaneStrips.SourceRects)
            {
                Assert.True(rect.Width > 0);
            }
        }

        [Fact]
        public void LaneCovers_CoverSourceRects_ShouldNotBeEmpty()
        {
            Assert.NotEmpty(PerformanceUILayout.LaneCovers.CoverSourceRects);
        }

        [Fact]
        public void LifeGaugeAssets_Background_ShouldBeAccessible()
        {
            var bg = PerformanceUILayout.LifeGaugeAssets.Background;
            // Should not throw
            Assert.True(bg.Width >= 0);
        }

        [Fact]
        public void SongProgressAssets_Background_ShouldMatchProgress()
        {
            Assert.Equal(PerformanceUILayout.Progress.FrameBounds, PerformanceUILayout.SongProgressAssets.Background);
        }

        #endregion

        #region ToRectangle Extension Method Tests

        [Fact]
        public void ToRectangle_FromVector2_ShouldReturnZeroSize()
        {
            var pos = new Vector2(100, 200);
            var rect = pos.ToRectangle();

            Assert.Equal(100, rect.X);
            Assert.Equal(200, rect.Y);
            Assert.Equal(0, rect.Width);
            Assert.Equal(0, rect.Height);
        }

        [Fact]
        public void ToRectangle_FromVector2WithSize_ShouldReturnCorrectRectangle()
        {
            var pos = new Vector2(50, 75);
            var size = new Vector2(200, 100);
            var rect = pos.ToRectangle(size);

            Assert.Equal(50, rect.X);
            Assert.Equal(75, rect.Y);
            Assert.Equal(200, rect.Width);
            Assert.Equal(100, rect.Height);
        }

        #endregion

        #region Additional Constant Tests

        [Fact]
        public void BGMTimingToleranceMs_ShouldBePositive()
        {
            Assert.True(PerformanceUILayout.BGMTimingToleranceMs > 0.0);
        }

        [Fact]
        public void NoteDefaultLookAheadMs_ShouldBePositive()
        {
            Assert.True(PerformanceUILayout.NoteDefaultLookAheadMs > 0.0);
        }

        [Fact]
        public void LaneFlush_FrameAdvance_ShouldBePositive()
        {
            Assert.True(PerformanceUILayout.LaneFlush.FrameAdvance > 0);
        }

        [Fact]
        public void FallbackBackgroundColor_ShouldNotBeTransparent()
        {
            var color = PerformanceUILayout.FallbackBackgroundColor;
            Assert.NotEqual(Color.Transparent, color);
        }

        [Fact]
        public void ReadyCountdownSeconds_ShouldBePositive()
        {
            Assert.True(PerformanceUILayout.ReadyCountdownSeconds > 0);
        }

        [Fact]
        public void ScreenResolution_ShouldMatchWidthHeight()
        {
            Assert.Equal(PerformanceUILayout.ScreenWidth, PerformanceUILayout.ScreenResolution.X);
            Assert.Equal(PerformanceUILayout.ScreenHeight, PerformanceUILayout.ScreenResolution.Y);
        }

        #endregion
    }

    /// <summary>
    /// Tests for ChartStatistics ToString
    /// </summary>
    public class ChartStatisticsTests
    {
        [Fact]
        public void ChartStatistics_DefaultValues_ShouldBeZero()
        {
            var stats = new ChartStatistics();
            Assert.Equal(0, stats.TotalNotes);
            Assert.Equal(0.0, stats.DurationMs);
            Assert.Equal(0.0, stats.Bpm);
            Assert.Equal(0.0, stats.NoteDensity);
            Assert.Equal(10, stats.NotesPerLane.Length);
        }

        [Fact]
        public void ChartStatistics_SetProperties_ShouldRetainValues()
        {
            var stats = new ChartStatistics
            {
                TotalNotes = 500,
                DurationMs = 180000.0,
                Bpm = 145.0,
                NoteDensity = 2.78
            };
            Assert.Equal(500, stats.TotalNotes);
            Assert.Equal(180000.0, stats.DurationMs);
            Assert.Equal(145.0, stats.Bpm);
            Assert.Equal(2.78, stats.NoteDensity);
        }

        [Fact]
        public void ChartStatistics_ToString_ShouldContainKeyInfo()
        {
            var stats = new ChartStatistics
            {
                TotalNotes = 300,
                DurationMs = 90000.0,
                Bpm = 120.0,
                NoteDensity = 3.33
            };
            var result = stats.ToString();
            Assert.Contains("300", result);
            Assert.Contains("120", result);
        }
    }
}
