using Microsoft.Xna.Framework;
using DTXMania.Game.Lib.UI.Layout;
using Xunit;

namespace DTXMania.Test.UI
{
    [Trait("Category", "Unit")]
    public class PerformanceUILayoutSkillMeterTests
    {
        [Fact]
        public void SkillMeter_BackgroundPosition_ShouldMatchExpected()
        {
            Assert.Equal(new Vector2(900, 50), PerformanceUILayout.SkillMeter.BackgroundPosition);
        }

        [Fact]
        public void SkillMeter_BackgroundSourceRect_ShouldMatchExpected()
        {
            Assert.Equal(new Rectangle(2, 2, 251, 584), PerformanceUILayout.SkillMeter.BackgroundSourceRect);
        }

        [Fact]
        public void SkillMeter_GaugeOffset_ShouldMatchExpected()
        {
            Assert.Equal(new Vector2(45, 0), PerformanceUILayout.SkillMeter.GaugeOffset);
        }

        [Fact]
        public void SkillMeter_GaugeWidth_ShouldBe30()
        {
            Assert.Equal(30, PerformanceUILayout.SkillMeter.GaugeWidth);
        }

        [Fact]
        public void SkillMeter_GaugeSourceXY_ShouldMatchExpected()
        {
            Assert.Equal(new Vector2(2, 2), PerformanceUILayout.SkillMeter.GaugeSourceXY);
        }

        [Fact]
        public void SkillMeter_GaugeMaxHeight_ShouldBe434()
        {
            Assert.Equal(434, PerformanceUILayout.SkillMeter.GaugeMaxHeight);
        }

        [Fact]
        public void SkillMeter_GaugeBaselineY_ShouldBe527()
        {
            Assert.Equal(527, PerformanceUILayout.SkillMeter.GaugeBaselineY);
        }

        [Fact]
        public void SkillMeter_NumberOffsetFromTopOfBar_ShouldBeNegative10()
        {
            Assert.Equal(-10, PerformanceUILayout.SkillMeter.NumberOffsetFromTopOfBar);
        }

        [Fact]
        public void SkillMeter_LabelSourceRect_ShouldMatchExpected()
        {
            Assert.Equal(new Rectangle(260, 2, 30, 120), PerformanceUILayout.SkillMeter.LabelSourceRect);
        }

        [Fact]
        public void SkillMeter_LabelPosition_ShouldMatchExpected()
        {
            Assert.Equal(new Vector2(945, 407), PerformanceUILayout.SkillMeter.LabelPosition);
        }

        [Fact]
        public void SkillMeter_GaugeBaselineY_ShouldEqualBackgroundPositionY_Plus477()
        {
            Assert.Equal(
                PerformanceUILayout.SkillMeter.BackgroundPosition.Y + 477,
                PerformanceUILayout.SkillMeter.GaugeBaselineY);
        }

        [Fact]
        public void SkillMeter_LabelPositionX_ShouldEqualBackgroundPositionX_PlusGaugeOffsetX()
        {
            Assert.Equal(
                PerformanceUILayout.SkillMeter.BackgroundPosition.X + PerformanceUILayout.SkillMeter.GaugeOffset.X,
                PerformanceUILayout.SkillMeter.LabelPosition.X);
        }
    }
}
