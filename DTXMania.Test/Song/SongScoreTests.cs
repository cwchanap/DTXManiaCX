using System;
using DTX.Song;
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

        [Fact]
        public void Constructor_ShouldInitializeWithDefaults()
        {
            // Arrange & Act
            var score = new SongScore();

            // Assert
            Assert.Equal(EInstrumentPart.DRUMS, score.Instrument); // Default to DRUMS
            Assert.Equal(0, score.DifficultyLevel);
            Assert.Equal("", score.DifficultyLabel);
            Assert.Equal(0, score.BestScore);
            Assert.Equal(0, score.BestRank);
            Assert.False(score.FullCombo);
            Assert.Equal(0, score.PlayCount);
            Assert.Null(score.LastPlayedAt);
            Assert.Equal(0, score.HighSkill);
            Assert.Equal(0, score.SongSkill);
            Assert.Equal(0, score.TotalNotes);
        }

        [Theory]
        [InlineData("DRUMS", 85, "EXTREME")]
        [InlineData("GUITAR", 78, "ADVANCED")]
        [InlineData("BASS", 65, "BASIC")]
        public void BasicProperties_ShouldSetAndGetCorrectly(string instrument, int difficultyLevel, string difficultyLabel)
        {
            // Arrange
            var score = new SongScore();

            // Act
            score.Instrument = Enum.Parse<EInstrumentPart>(instrument);
            score.DifficultyLevel = difficultyLevel;
            score.DifficultyLabel = difficultyLabel;

            // Assert
            Assert.Equal(Enum.Parse<EInstrumentPart>(instrument), score.Instrument);
            Assert.Equal(difficultyLevel, score.DifficultyLevel);
            Assert.Equal(difficultyLabel, score.DifficultyLabel);
        }

        #endregion

        #region Rank Tests

        [Theory]
        [InlineData(0, "SS")]
        [InlineData(1, "S")]
        [InlineData(2, "A")]
        [InlineData(3, "B")]
        [InlineData(4, "C")]
        [InlineData(5, "D")]
        [InlineData(6, "E")]
        [InlineData(7, "F")]
        [InlineData(8, "---")]
        [InlineData(-1, "---")]
        public void RankName_ShouldReturnCorrectString(int rank, string expected)
        {
            // Arrange
            var score = new SongScore { BestRank = rank };

            // Act
            var result = score.RankName;

            // Assert
            Assert.Equal(expected, result);
        }

        #endregion

        #region Accuracy Tests

        [Theory]
        [InlineData(100, 80, 15, 5, 0, 100.0)] // Perfect accuracy
        [InlineData(100, 50, 25, 15, 10, 100.0)] // All notes hit
        [InlineData(100, 50, 25, 15, 0, 90.0)] // 10 missed
        [InlineData(0, 0, 0, 0, 0, 0.0)] // No notes
        public void Accuracy_ShouldCalculateCorrectly(int totalNotes, int perfect, int great, int good, int poor, double expected)
        {
            // Arrange
            var score = new SongScore
            {
                TotalNotes = totalNotes,
                BestPerfect = perfect,
                BestGreat = great,
                BestGood = good,
                BestPoor = poor
            };

            // Act
            var result = score.Accuracy;

            // Assert
            Assert.Equal(expected, result, 1); // 1 decimal place precision
        }

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

        [Theory]
        [InlineData(0, false)]
        [InlineData(1, true)]
        [InlineData(10, true)]
        public void HasBeenPlayed_ShouldReturnCorrectValue(int playCount, bool expected)
        {
            // Arrange
            var score = new SongScore { PlayCount = playCount };

            // Act
            var result = score.HasBeenPlayed;

            // Assert
            Assert.Equal(expected, result);
        }

        #endregion

        #region Clone Tests

        [Fact]
        public void Clone_ShouldCreateDeepCopy()
        {
            // Arrange
            var original = new SongScore
            {
                Instrument = EInstrumentPart.DRUMS,
                DifficultyLevel = 85,
                DifficultyLabel = "EXTREME",
                BestScore = 950000,
                BestRank = 1,
                FullCombo = true,
                PlayCount = 10,
                LastPlayedAt = DateTime.Now,
                HighSkill = 80.75,
                SongSkill = 80.75,
                TotalNotes = 1000,
                BestPerfect = 950,
                BestGreat = 40,
                BestGood = 10,
                BestPoor = 0,
                BestMiss = 0,
                IsNewRecord = true
            };

            // Act
            var clone = original.Clone();

            // Assert
            Assert.NotSame(original, clone);
            Assert.Equal(original.Instrument, clone.Instrument);
            Assert.Equal(original.DifficultyLevel, clone.DifficultyLevel);
            Assert.Equal(original.DifficultyLabel, clone.DifficultyLabel);
            Assert.Equal(original.BestScore, clone.BestScore);
            Assert.Equal(original.BestRank, clone.BestRank);
            Assert.Equal(original.FullCombo, clone.FullCombo);
            Assert.Equal(original.PlayCount, clone.PlayCount);
            Assert.Equal(original.LastPlayedAt, clone.LastPlayedAt);
            Assert.Equal(original.HighSkill, clone.HighSkill);
            Assert.Equal(original.SongSkill, clone.SongSkill);
            Assert.Equal(original.TotalNotes, clone.TotalNotes);
            Assert.Equal(original.BestPerfect, clone.BestPerfect);
            Assert.Equal(original.BestGreat, clone.BestGreat);
            Assert.Equal(original.BestGood, clone.BestGood);
            Assert.Equal(original.BestPoor, clone.BestPoor);
            Assert.Equal(original.BestMiss, clone.BestMiss);
            Assert.Equal(original.IsNewRecord, clone.IsNewRecord);
        }

        [Fact]
        public void Clone_ModifyingClone_ShouldNotAffectOriginal()
        {
            // Arrange
            var original = new SongScore { BestScore = 800000 };
            var clone = original.Clone();

            // Act
            clone.BestScore = 900000;

            // Assert
            Assert.Equal(800000, original.BestScore);
            Assert.Equal(900000, clone.BestScore);
        }

        #endregion
    }
}
