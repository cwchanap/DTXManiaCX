using DTXMania.Game.Lib.Stage.Performance;
using Xunit;

namespace DTXMania.Test.Stage.Performance
{
    /// <summary>
    /// Pure logic tests for SkillMeterDisplay math helpers — runs on all platforms.
    /// </summary>
    [Trait("Category", "Unit")]
    public class SkillMeterDisplayLogicTests
    {
        [Theory]
        [InlineData(  0.0,   0)]
        [InlineData( 50.0, 217)]
        [InlineData(100.0, 434)]
        [InlineData(150.0, 434)]   // clamped
        [InlineData( -5.0,   0)]   // clamped low
        [InlineData( 33.33, 144)]  // 434 * 33.33 / 100 = 144.6522 -> truncates to 144
        public void ComputeGaugeHeight_ReturnsExpected(double skill, int expected)
        {
            Assert.Equal(expected, SkillMeterDisplay.ComputeGaugeHeight(skill));
        }

        [Fact]
        public void ComputeBarTopY_FullGauge_ShouldEqualBackgroundTop()
        {
            // GaugeBaselineY (527) - GaugeMaxHeight (434) = 93
            Assert.Equal(93, SkillMeterDisplay.ComputeBarTopY(100.0));
        }

        [Fact]
        public void ComputeBarTopY_EmptyGauge_ShouldEqualBaseline()
        {
            Assert.Equal(527, SkillMeterDisplay.ComputeBarTopY(0.0));
        }

        [Fact]
        public void ShouldDrawBar_ZeroSkill_ShouldBeFalse()
        {
            Assert.False(SkillMeterDisplay.ShouldDrawBar(0.0));
        }

        [Fact]
        public void ShouldDrawBar_AnyPositiveSkill_ShouldBeTrue()
        {
            Assert.True(SkillMeterDisplay.ShouldDrawBar(0.01));
            Assert.True(SkillMeterDisplay.ShouldDrawBar(100.0));
        }
    }
}
