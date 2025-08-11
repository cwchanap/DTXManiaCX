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
                BestRank = 3,
                PlayCount = 5
            };

            // Act
            var result = score.UpdateScore(900000, 2, false, 80, 15, 5, 0, 0);

            // Assert
            Assert.True(result);
            Assert.Equal(900000, score.BestScore);
            Assert.Equal(2, score.BestRank);
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
                BestRank = 2,
                PlayCount = 5
            };

            // Act
            var result = score.UpdateScore(800000, 3, false, 70, 20, 10, 0, 0);

            // Assert
            Assert.False(result);
            Assert.Equal(900000, score.BestScore); // Should not change
            Assert.Equal(2, score.BestRank); // Should not change
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
            var result = score.UpdateScore(850000, 2, true, 100, 0, 0, 0, 0);

            // Assert
            Assert.True(result);
            Assert.True(score.FullCombo);
            Assert.True(score.IsNewRecord);
        }

        [Theory]
        [InlineData(1000000, 85, 1.0, 85.0)] // Perfect score, SS rank
        [InlineData(950000, 85, 0.95, 76.71)] // S rank (adjusted for actual calculation)
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
                BestRank = GetRankFromMultiplier(rankMultiplier)
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

        private int GetRankFromMultiplier(double multiplier)
        {
            return multiplier switch
            {
                1.0 => 0,   // SS
                0.95 => 1,  // S
                0.9 => 2,   // A
                0.85 => 3,  // B
                0.8 => 4,   // C
                0.75 => 5,  // D
                0.7 => 6,   // E
                0.65 => 7,  // F
                _ => 8
            };
        }

        #endregion

        #region HasBeenPlayed Tests



        #endregion

        #region Clone Tests





        #endregion
    }
}
