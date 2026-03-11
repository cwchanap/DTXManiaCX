using System;
using DTXMania.Game.Lib.Song;
using Xunit;
using DTXMania.Game.Lib.Song.Entities;

// Type alias for EF Core entity
using SongScore = DTXMania.Game.Lib.Song.Entities.SongScore;

namespace DTXMania.Test.Song
{
    /// <summary>
    /// Unit tests for SongScore class
    /// Tests score tracking, rank calculation, and skill computation
    /// </summary>
    public class SongScoreTests
    {
        #region Basic Property Tests



        #endregion

        #region Rank Tests



        #endregion

        #region Accuracy Tests



        #endregion

        #region Score Update Tests

        [Fact]
        public void UpdateScore_WithBetterScore_ShouldReturnTrueAndUpdateValues()
        {
            // Arrange
            var score = new SongScore
            {
                BestScore = 800000,
                BestRank = 70,
                PlayCount = 5
            };

            // Act
            var result = score.UpdateScore(900000, 80, false, 80, 15, 5, 0, 0);

            // Assert
            Assert.True(result);
            Assert.Equal(900000, score.BestScore);
            Assert.Equal(80, score.BestRank);
            Assert.Equal(6, score.PlayCount);
            Assert.NotNull(score.LastPlayedAt);
            Assert.True(score.IsNewRecord);
        }

        [Fact]
        public void UpdateScore_WithWorseScore_ShouldReturnFalseButUpdatePlayCount()
        {
            // Arrange
            var score = new SongScore
            {
                BestScore = 900000,
                BestRank = 80,
                PlayCount = 5
            };

            // Act
            var result = score.UpdateScore(800000, 70, false, 70, 20, 10, 0, 0);

            // Assert
            Assert.False(result);
            Assert.Equal(900000, score.BestScore); // Should not change
            Assert.Equal(80, score.BestRank); // Should not change
            Assert.Equal(6, score.PlayCount); // Should increment
            Assert.NotNull(score.LastPlayedAt);
            Assert.False(score.IsNewRecord);
        }

        [Fact]
        public void UpdateScore_WithFullCombo_ShouldSetFullComboFlag()
        {
            // Arrange
            var score = new SongScore
            {
                BestScore = 800000,
                FullCombo = false
            };

            // Act
            var result = score.UpdateScore(850000, 80, true, 100, 0, 0, 0, 0);

            // Assert
            Assert.True(result);
            Assert.True(score.FullCombo);
            Assert.True(score.IsNewRecord);
        }

        [Theory]
        [InlineData(1000000, 85, 1.0, 85.0)] // Perfect score, SS rank
        [InlineData(950000, 85, 0.95, 76.71)] // S rank
        [InlineData(900000, 85, 0.9, 68.85)] // A rank
        [InlineData(0, 85, 0.5, 0.0)] // No score
        [InlineData(1000000, 0, 1.0, 0.0)] // No difficulty
        public void CalculateSkill_ShouldComputeCorrectValue(int bestScore, int difficultyLevel, double rankMultiplier, double expectedSkill)
        {
            // Arrange
            var score = new SongScore
            {
                BestScore = bestScore,
                DifficultyLevel = difficultyLevel,
                BestRank = GetRankPercentageFromMultiplier(rankMultiplier)
            };

            // Act
            score.CalculateSkill();

            // Assert
            Assert.Equal(expectedSkill, score.SongSkill, 2); // 2 decimal places precision
            
            if (expectedSkill > score.HighSkill)
            {
                Assert.Equal(expectedSkill, score.HighSkill, 2);
            }
        }

        [Theory]
        [InlineData(92, 1, "S", 0.95)]
        [InlineData(80, 2, "A", 0.9)]
        [InlineData(2, 2, "A", 0.9)]
        public void RankHelpers_ShouldAcceptPercentageAndOrdinal(int rankValue, int expectedRankIndex, string expectedRankName, double expectedMultiplier)
        {
            Assert.Equal(expectedRankIndex, SongScore.ComputeRankIndex(rankValue));
            Assert.Equal(expectedRankName, SongScore.RankString(rankValue));
            Assert.Equal(expectedMultiplier, SongScore.RankMultiplier(rankValue), 3);
        }

        private int GetRankPercentageFromMultiplier(double multiplier)
        {
            return multiplier switch
            {
                1.0 => 95,  // SS
                0.95 => 90, // S
                0.9 => 80,  // A
                0.85 => 70, // B
                0.8 => 60,  // C
                0.75 => 50, // D
                0.7 => 40,  // E
                0.65 => 0,  // F
                _ => 0
            };
        }

        #endregion

        #region HasBeenPlayed Tests



        #endregion

        #region Clone Tests





        #endregion
    }
}
