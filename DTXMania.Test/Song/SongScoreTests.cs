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
    [Trait("Category", "SongScore")]
    public class SongScoreTests
    {
        #region Basic Property Tests

        [Fact]
        public void DefaultValues_ShouldBeZeroOrEmpty()
        {
            var score = new SongScore();

            Assert.Equal(0, score.BestScore);
            Assert.Equal(0, score.BestRank);
            Assert.Equal(0, score.PlayCount);
            Assert.Equal(0, score.MaxCombo);
            Assert.Equal(0, score.ClearCount);
            Assert.Equal(0.0, score.BestSkillPoint);
            Assert.Equal(0.0, score.BestAchievementRate);
            Assert.Equal(0.0, score.HighSkill);
            Assert.Equal(0.0, score.SongSkill);
            Assert.Equal("", score.DifficultyLabel);
            Assert.Equal("", score.ProgressBar);
            Assert.False(score.FullCombo);
            Assert.False(score.Excellent);
            Assert.False(score.HasBeenPlayed);
            Assert.Null(score.LastPlayedAt);
        }

        [Fact]
        public void DifficultyLevel_WithoutChart_ShouldReturnPrivateField()
        {
            var score = new SongScore { DifficultyLevel = 75 };
            Assert.Equal(75, score.DifficultyLevel);
        }

        [Fact]
        public void DifficultyLevel_WithDrumsInstrumentAndChart_ShouldReturnChartDrumLevel()
        {
            var chart = new SongChart { DrumLevel = 88, GuitarLevel = 72, BassLevel = 60 };
            var score = new SongScore
            {
                Chart = chart,
                Instrument = EInstrumentPart.DRUMS
            };

            Assert.Equal(88, score.DifficultyLevel);
        }

        [Fact]
        public void DifficultyLevel_WithGuitarInstrumentAndChart_ShouldReturnChartGuitarLevel()
        {
            var chart = new SongChart { DrumLevel = 88, GuitarLevel = 72, BassLevel = 60 };
            var score = new SongScore
            {
                Chart = chart,
                Instrument = EInstrumentPart.GUITAR
            };

            Assert.Equal(72, score.DifficultyLevel);
        }

        [Fact]
        public void DifficultyLevel_WithBassInstrumentAndChart_ShouldReturnChartBassLevel()
        {
            var chart = new SongChart { DrumLevel = 88, GuitarLevel = 72, BassLevel = 60 };
            var score = new SongScore
            {
                Chart = chart,
                Instrument = EInstrumentPart.BASS
            };

            Assert.Equal(60, score.DifficultyLevel);
        }

        [Fact]
        public void IsNewRecord_DefaultValue_ShouldBeFalse()
        {
            var score = new SongScore();
            Assert.False(score.IsNewRecord);
        }

        [Fact]
        public void InputMethodFlags_DefaultValues_ShouldBeFalse()
        {
            var score = new SongScore();
            Assert.False(score.UsedDrumPad);
            Assert.False(score.UsedKeyboard);
            Assert.False(score.UsedMidi);
            Assert.False(score.UsedJoypad);
            Assert.False(score.UsedMouse);
        }

        #endregion

        #region Rank Tests

        [Fact]
        public void RankName_WhenPlayedWithSSRank_ShouldReturnSS()
        {
            var score = new SongScore { PlayCount = 1, BestRank = 95 };
            Assert.Equal("SS", score.RankName);
        }

        [Fact]
        public void RankName_WhenPlayedWithSRank_ShouldReturnS()
        {
            var score = new SongScore { PlayCount = 1, BestRank = 90 };
            Assert.Equal("S", score.RankName);
        }

        [Fact]
        public void RankName_WhenPlayedWithARank_ShouldReturnA()
        {
            var score = new SongScore { PlayCount = 1, BestRank = 80 };
            Assert.Equal("A", score.RankName);
        }

        [Fact]
        public void RankName_WhenPlayedWithBRank_ShouldReturnB()
        {
            var score = new SongScore { PlayCount = 1, BestRank = 70 };
            Assert.Equal("B", score.RankName);
        }

        [Fact]
        public void RankName_WhenPlayedWithCRank_ShouldReturnC()
        {
            var score = new SongScore { PlayCount = 1, BestRank = 60 };
            Assert.Equal("C", score.RankName);
        }

        [Fact]
        public void RankName_WhenPlayedWithDRank_ShouldReturnD()
        {
            var score = new SongScore { PlayCount = 1, BestRank = 50 };
            Assert.Equal("D", score.RankName);
        }

        [Fact]
        public void RankName_WhenPlayedWithERank_ShouldReturnE()
        {
            var score = new SongScore { PlayCount = 1, BestRank = 40 };
            Assert.Equal("E", score.RankName);
        }

        [Fact]
        public void RankName_WhenPlayedWithFRank_ShouldReturnF()
        {
            var score = new SongScore { PlayCount = 1, BestRank = 0 };
            Assert.Equal("F", score.RankName);
        }

        #endregion

        #region Accuracy Tests

        [Fact]
        public void Accuracy_WhenNoNotes_ShouldReturnZero()
        {
            var score = new SongScore { TotalNotes = 0 };
            Assert.Equal(0.0, score.Accuracy);
        }

        [Fact]
        public void Accuracy_AllPerfect_ShouldReturn100()
        {
            var score = new SongScore
            {
                TotalNotes = 100,
                BestPerfect = 100,
                BestGreat = 0,
                BestGood = 0,
                BestPoor = 0,
                BestMiss = 0
            };
            Assert.Equal(100.0, score.Accuracy);
        }

        [Fact]
        public void Accuracy_AllMiss_ShouldReturnZero()
        {
            var score = new SongScore
            {
                TotalNotes = 100,
                BestPerfect = 0,
                BestGreat = 0,
                BestGood = 0,
                BestPoor = 0,
                BestMiss = 100
            };
            Assert.Equal(0.0, score.Accuracy);
        }

        [Fact]
        public void Accuracy_MixedResults_ShouldCalculateCorrectPercentage()
        {
            // 60 out of 100 notes hit (Perfect + Great + Good + Poor = 60)
            var score = new SongScore
            {
                TotalNotes = 100,
                BestPerfect = 30,
                BestGreat = 20,
                BestGood = 5,
                BestPoor = 5,
                BestMiss = 40
            };
            Assert.Equal(60.0, score.Accuracy);
        }

        [Fact]
        public void Accuracy_HalfHit_ShouldReturnFiftyPercent()
        {
            var score = new SongScore
            {
                TotalNotes = 200,
                BestPerfect = 100,
                BestGreat = 0,
                BestGood = 0,
                BestPoor = 0,
                BestMiss = 100
            };
            Assert.Equal(50.0, score.Accuracy);
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

        [Fact]
        public void UpdateScore_WithPercentageRank_ShouldStoreNormalizedRankBucket()
        {
            var score = new SongScore();

            var result = score.UpdateScore(900000, 92, false, 90, 5, 3, 1, 1);

            Assert.True(result);
            Assert.Equal(90, score.BestRank);
        }

        [Fact]
        public void UpdateScore_FirstPlay_WithFailingRank_ShouldStoreZeroBucket()
        {
            var score = new SongScore(); // PlayCount = 0, fresh record

            var result = score.UpdateScore(300000, 20, false, 50, 30, 20, 10, 10);

            Assert.True(result); // first play is always a new best
            Assert.Equal(0, score.BestRank);  // 20% normalizes to F bucket (0)
            Assert.Equal(1, score.PlayCount);
        }

        [Theory]
        [InlineData(40, 40)]  // E bucket boundary (exact)
        [InlineData(95, 95)]  // SS bucket boundary (exact)
        [InlineData(39, 0)]   // just below E → F bucket
        public void UpdateScore_FirstPlay_AtBucketBoundaries_ShouldNormalizeCorrectly(int rawRank, int expectedBucket)
        {
            var score = new SongScore();

            score.UpdateScore(500000, rawRank, false, 50, 20, 10, 5, 5);

            Assert.Equal(expectedBucket, score.BestRank);
        }

        [Fact]
        public void UpdateScore_WithLegacyStoredBestRank_ShouldNormalizeBeforeComparing()
        {
            var score = new SongScore
            {
                BestScore = 900000,
                BestRank = 2,
                PlayCount = 5
            };

            var result = score.UpdateScore(850000, 70, false, 70, 20, 10, 0, 0);

            Assert.False(result);
            Assert.Equal(80, score.BestRank);
            Assert.Equal(6, score.PlayCount);
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
        [InlineData(2, 7, "F", 0.65)]    // 2 is a percentage (2%), NOT a legacy ordinal — NormalizeRankPercentage maps it to F bucket
        [InlineData(95, 0, "SS", 1.0)]   // SS boundary
        [InlineData(100, 0, "SS", 1.0)]  // above SS
        [InlineData(40, 6, "E", 0.7)]    // E bucket
        [InlineData(0, 7, "F", 0.65)]    // F bucket (new scale — below 40)
        [InlineData(39, 7, "F", 0.65)]   // just below E
        public void RankHelpers_ShouldUseNormalizedPercentageDomain(int rankValue, int expectedRankIndex, string expectedRankName, double expectedMultiplier)
        {
            Assert.Equal(expectedRankIndex, SongScore.ComputeRankIndex(rankValue));
            Assert.Equal(expectedRankName, SongScore.RankString(rankValue));
            Assert.Equal(expectedMultiplier, SongScore.RankMultiplier(rankValue), 3);
        }

        [Fact]
        public void NormalizeStoredBestRank_WithZero_ShouldReturnFBucket()
        {
            // 0 is the F bucket on the new percentage scale.
            // There is no way to distinguish a persisted legacy ordinal SS (0) from
            // a new-system F bucket (0) — 0 is treated as F.
            Assert.Equal(0, SongScore.NormalizeStoredBestRank(0));
        }

        [Fact]
        public void NormalizeStoredBestRank_WithLegacyOrdinals_ShouldMapToBuckets()
        {
            Assert.Equal(90, SongScore.NormalizeStoredBestRank(1));  // legacy S
            Assert.Equal(80, SongScore.NormalizeStoredBestRank(2));  // legacy A
            Assert.Equal(70, SongScore.NormalizeStoredBestRank(3));  // legacy B
            Assert.Equal(60, SongScore.NormalizeStoredBestRank(4));  // legacy C
            Assert.Equal(50, SongScore.NormalizeStoredBestRank(5));  // legacy D
            Assert.Equal(40, SongScore.NormalizeStoredBestRank(6));  // legacy E
            Assert.Equal(0,  SongScore.NormalizeStoredBestRank(7));  // legacy F
        }

        [Theory]
        [InlineData(8)]
        [InlineData(20)]
        [InlineData(39)]
        public void NormalizeStoredBestRank_WithValuesBetween8And39_ShouldReturnFBucket(int value)
        {
            // Values 8-39 are not legacy ordinals (1-7) and are below the E bucket (40),
            // so they normalize to F (0). Extending IsLegacyOrdinal to cover this range
            // would corrupt low-achievement records.
            Assert.Equal(0, SongScore.NormalizeStoredBestRank(value));
        }

        [Fact]
        public void RankName_WhenNotPlayed_ShouldReturnDashes()
        {
            var score = new SongScore { PlayCount = 0, BestRank = 95 };
            Assert.Equal("---", score.RankName);
        }

        [Fact]
        public void RankName_WhenPlayedWithLegacyOrdinalA_ShouldReturnA()
        {
            var score = new SongScore { PlayCount = 1, BestRank = 2 }; // legacy ordinal 2 = A
            Assert.Equal("A", score.RankName);
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

        [Fact]
        public void HasBeenPlayed_WhenPlayCountIsZero_ShouldReturnFalse()
        {
            var score = new SongScore { PlayCount = 0 };
            Assert.False(score.HasBeenPlayed);
        }

        [Fact]
        public void HasBeenPlayed_WhenPlayCountIsPositive_ShouldReturnTrue()
        {
            var score = new SongScore { PlayCount = 1 };
            Assert.True(score.HasBeenPlayed);
        }

        #endregion

        #region Clone Tests

        [Fact]
        public void Clone_ShouldNormalizeLegacyOrdinalBestRank()
        {
            var score = new SongScore { BestRank = 2, PlayCount = 3 }; // legacy ordinal A
            var clone = score.Clone();
            Assert.Equal(80, clone.BestRank);
        }

        [Fact]
        public void Clone_ShouldPreserveAllFields()
        {
            var score = new SongScore
            {
                BestScore = 950000,
                BestRank = 90,
                PlayCount = 5,
                FullCombo = true,
                BestPerfect = 100,
                BestGreat = 10
            };
            var clone = score.Clone();
            Assert.Equal(score.BestScore, clone.BestScore);
            Assert.Equal(score.BestRank, clone.BestRank);
            Assert.Equal(score.PlayCount, clone.PlayCount);
            Assert.Equal(score.FullCombo, clone.FullCombo);
            Assert.Equal(score.BestPerfect, clone.BestPerfect);
            Assert.Equal(score.BestGreat, clone.BestGreat);
        }


        #endregion
    }
}
