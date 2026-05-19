using DTXMania.Game.Lib.Stage.Performance;
using Xunit;

namespace DTXMania.Test.Stage.Performance
{
    /// <summary>
    /// Pure logic tests for SkillPanelDisplay formatting helpers — runs on all platforms.
    /// </summary>
    [Trait("Category", "Unit")]
    public class SkillPanelDisplayLogicTests
    {
        // Level encoding (DTXManiaNX-faithful):
        //   level <  100 → displayed = level/10 + levelDec/100  (e.g. 80+50 → 8.50)
        //   level >= 100 → displayed = level/100                (e.g. 850 → 8.50)
        [Theory]
        [InlineData(78,  0, "7.80")]
        [InlineData(80, 50, "8.50")]
        [InlineData(78, 33, "8.13")]
        [InlineData(50,  0, "5.00")]
        [InlineData(850, 0, "8.50")]
        [InlineData( 0,  0, "--")]
        [InlineData(-1,  0, "--")]
        public void FormatLevelText_WithVariousInputs_ShouldReturnExpected(int level, int levelDec, string expected)
        {
            Assert.Equal(expected, SkillPanelDisplay.FormatLevelText(level, levelDec));
        }

        [Theory]
        [InlineData(  0.0,  "  0.00")]
        [InlineData( 87.42, " 87.42")]
        [InlineData(100.0, "100.00")]
        [InlineData( 50.5,  " 50.50")]
        public void FormatSkillText_WithVariousInputs_ShouldReturnExpected(double skill, string expected)
        {
            Assert.Equal(expected, SkillPanelDisplay.FormatSkillText(skill));
        }
    }
}
