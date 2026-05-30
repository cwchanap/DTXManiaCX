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

        [Theory]
        [InlineData(0, "   0")]
        [InlineData(7, "   7")]
        [InlineData(123, " 123")]
        [InlineData(1234, "1234")]
        [InlineData(12000, "9999")]
        public void FormatJudgementCount_WithVariousInputs_ShouldReturnFourColumnText(int count, string expected)
        {
            Assert.Equal(expected, SkillPanelDisplay.FormatJudgementCount(count));
        }

        [Theory]
        [InlineData(0, 0, "  0%")]
        [InlineData(1, 4, " 25%")]
        [InlineData(2, 3, " 67%")]
        [InlineData(10, 10, "100%")]
        public void FormatJudgementPercent_WithVariousInputs_ShouldReturnThreeColumnPercent(int count, int total, string expected)
        {
            Assert.Equal(expected, SkillPanelDisplay.FormatJudgementPercent(count, total));
        }

        [Fact]
        public void GetProcessedJudgementCount_ShouldSumJudgementCounts()
        {
            Assert.Equal(15, SkillPanelDisplay.GetProcessedJudgementCount(
                perfectCount: 5,
                greatCount: 4,
                goodCount: 3,
                poorCount: 2,
                missCount: 1));
        }

        // CanRenderWithLevelTexture is internal — test via GetLevelNumberSourceRectangle
        // which is the gating check. Digits and '.' should be renderable; '-' should not.
        [Theory]
        [InlineData('0', true)]
        [InlineData('5', true)]
        [InlineData('9', true)]
        [InlineData('.', true)]
        [InlineData('-', false)]
        [InlineData('a', false)]
        [InlineData(' ', false)]
        public void GetLevelNumberSourceRectangle_ShouldOnlySupportDigitsAndDot(char ch, bool shouldHaveSource)
        {
            // Access the static method via reflection since it's private
            var method = typeof(SkillPanelDisplay).GetMethod(
                "GetLevelNumberSourceRectangle",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.NotNull(method);
            var result = method.Invoke(null, new object[] { ch });
            if (shouldHaveSource)
                Assert.NotNull(result);
            else
                Assert.Null(result);
        }

        [Fact]
        public void CanRenderWithLevelTexture_DashText_ShouldReturnFalse()
        {
            var method = typeof(SkillPanelDisplay).GetMethod(
                "CanRenderWithLevelTexture",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.NotNull(method);

            // "--" (unknown difficulty) should not be renderable via level texture
            Assert.False((bool)method.Invoke(null, new object[] { "--" })!);
            // "8.50" (normal level) should be renderable
            Assert.True((bool)method.Invoke(null, new object[] { "8.50" })!);
        }
    }
}
