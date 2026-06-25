using DTXMania.Game.Lib.Stage.Performance;
using Microsoft.Xna.Framework;
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

        // Difficulty badge cell selection mirrors Script/difficult.dtxs scene 7 (7_Difficulty.png is a
        // 60x720 vertical strip of twelve 60x60 cells; all cells share X=0, the label picks the Y row).
        [Theory]
        [InlineData("DTX", 0)]
        [InlineData("DEBUT", 60)]
        [InlineData("NOVICE", 120)]
        [InlineData("REGULAR", 180)]
        [InlineData("EXPERT", 240)]
        [InlineData("MASTER", 300)]
        [InlineData("BASIC", 360)]
        [InlineData("ADVANCED", 420)]
        [InlineData("EXTREME", 480)]
        [InlineData("RAW", 540)]
        [InlineData("RWS", 600)]
        [InlineData("REAL", 660)]
        [InlineData("extreme", 480)]   // case-insensitive
        [InlineData("  MASTER  ", 300)] // trimmed
        public void GetDifficultyPanelSourceRect_KnownLabels_SelectExpectedCell(string label, int expectedY)
        {
            var rect = SkillPanelDisplay.GetDifficultyPanelSourceRect(label);
            Assert.Equal(new Rectangle(0, expectedY, 60, 60), rect);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("Level 3")]
        [InlineData("SPECIAL")]
        public void GetDifficultyPanelSourceRect_UnknownLabel_FallsBackToFirstCell(string? label)
        {
            var rect = SkillPanelDisplay.GetDifficultyPanelSourceRect(label);
            Assert.Equal(new Rectangle(0, 0, 60, 60), rect);
        }

        [Theory]
        [InlineData("DTX")]
        [InlineData("BASIC")]
        [InlineData("ADVANCED")]
        [InlineData("EXTREME")]
        [InlineData("MASTER")]
        [InlineData("REAL")]
        [InlineData("extreme")]      // case-insensitive
        [InlineData("  BASIC  ")]    // trimmed
        public void IsKnownDifficultyTier_AuthenticTierName_ShouldReturnTrue(string? label)
        {
            Assert.True(SkillPanelDisplay.IsKnownDifficultyTier(label));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("DRUMS Lv.36")]   // synthetic song-select display label, not a tier name
        [InlineData("GUITAR Lv.50")]
        [InlineData("Level 3")]
        [InlineData("SPECIAL")]
        public void IsKnownDifficultyTier_SyntheticOrUnknownLabel_ShouldReturnFalse(string? label)
        {
            Assert.False(SkillPanelDisplay.IsKnownDifficultyTier(label));
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
