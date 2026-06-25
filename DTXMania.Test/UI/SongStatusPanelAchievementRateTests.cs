using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Song.Entities;
using Xunit;

namespace DTXMania.Test.UI
{
    /// <summary>
    /// Pure-logic tests for <see cref="SongStatusPanel.ClassifyAchievementRate"/>, extracted from
    /// DrawAchievementRate so the boundary/source-field logic is verifiable without a graphics device.
    /// Guards the regression this path fixes: the MAX badge must be driven by BestAchievementRate
    /// (a 0-100 achievement percentage), not HighSkill (a level-weighted skill that routinely
    /// exceeds 100 and would wrongly show MAX for non-perfect plays).
    /// </summary>
    [Trait("Category", "Unit")]
    public class SongStatusPanelAchievementRateTests
    {
        private static SongScore Score(double bestAchievementRate, double highSkill = 0.0) =>
            new SongScore { BestAchievementRate = bestAchievementRate, HighSkill = highSkill };

        [Fact]
        public void Classify_NullScore_ReturnsSkip()
        {
            Assert.Equal(SongStatusPanel.AchievementRateMode.Skip,
                SongStatusPanel.ClassifyAchievementRate(null));
        }

        [Theory]
        [InlineData(-5)]   // clamps to 0
        [InlineData(0)]    // NX draws only when non-zero
        public void Classify_NonPositiveRate_ReturnsSkip(double rate)
        {
            Assert.Equal(SongStatusPanel.AchievementRateMode.Skip,
                SongStatusPanel.ClassifyAchievementRate(Score(rate)));
        }

        [Theory]
        [InlineData(0.01)]
        [InlineData(50.0)]
        [InlineData(99.99)]
        public void Classify_PartialRate_ReturnsDigits(double rate)
        {
            Assert.Equal(SongStatusPanel.AchievementRateMode.Digits,
                SongStatusPanel.ClassifyAchievementRate(Score(rate)));
        }

        [Theory]
        [InlineData(100.0)]
        [InlineData(150.0)] // clamps to 100
        public void Classify_PerfectOrAbove_ReturnsMax(double rate)
        {
            Assert.Equal(SongStatusPanel.AchievementRateMode.Max,
                SongStatusPanel.ClassifyAchievementRate(Score(rate)));
        }

        [Fact]
        public void Classify_Regression_SourceIsBestAchievementRateNotHighSkill()
        {
            // The bug this guards against: reading HighSkill (level-weighted, often > 100) would
            // classify a non-perfect 85% play as MAX. With BestAchievementRate=85 the cell must show
            // Digits even though HighSkill=120 would, if used, force the MAX badge.
            var score = Score(bestAchievementRate: 85.0, highSkill: 120.0);

            var mode = SongStatusPanel.ClassifyAchievementRate(score);

            Assert.Equal(SongStatusPanel.AchievementRateMode.Digits, mode);
        }
    }
}
