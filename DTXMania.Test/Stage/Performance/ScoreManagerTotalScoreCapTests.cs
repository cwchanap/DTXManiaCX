using System;
using DTX.Stage.Performance;
using DTXMania.Game.Lib.Song.Entities;
using Xunit;

namespace DTXMania.Test.Stage.Performance
{
    /// <summary>
    /// Unit tests for ScoreManager total score cap functionality.
    /// Tests that the ScoreManager never exceeds the maximum score of 1,000,000 points.
    /// </summary>
    public class ScoreManagerTotalScoreCapTests
    {
        [Fact]
        public void ScoreManager_MaxScoreConstant_IsOneMillion()
        {
            // Assert
            Assert.Equal(1000000, ScoreManager.MaxScore);
        }

        [Fact]
        public void ScoreManager_SingleNote_CannotExceedMaxScore()
        {
            // Arrange
            var scoreManager = new ScoreManager(1); // Single note chart
            var justEvent = new JudgementEvent(0, 0, 0.0, JudgementType.Just);

            // Act
            scoreManager.ProcessJudgement(justEvent);

            // Assert
            Assert.True(scoreManager.CurrentScore <= ScoreManager.MaxScore);
            Assert.Equal(ScoreManager.MaxScore, scoreManager.CurrentScore); // Single note should give max score
        }

        [Fact]
        public void ScoreManager_MultipleJustHits_NeverExceedsMaxScore()
        {
            // Arrange
            var scoreManager = new ScoreManager(100); // 100 note chart
            
            // Act - Process more than enough Just hits to theoretically exceed max score
            for (int i = 0; i < 150; i++) // More hits than notes in chart
            {
                var justEvent = new JudgementEvent(i, 0, 0.0, JudgementType.Just);
                scoreManager.ProcessJudgement(justEvent);
            }

            // Assert
            Assert.True(scoreManager.CurrentScore <= ScoreManager.MaxScore);
            Assert.Equal(ScoreManager.MaxScore, scoreManager.CurrentScore);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(10)]
        [InlineData(100)]
        [InlineData(1000)]
        [InlineData(10000)]
        public void ScoreManager_VariousNoteCounts_MaxScoreNeverExceeded(int totalNotes)
        {
            // Arrange
            var scoreManager = new ScoreManager(totalNotes);
            
            // Act - Process perfect play (all Just hits)
            for (int i = 0; i < totalNotes; i++)
            {
                var justEvent = new JudgementEvent(i, 0, 0.0, JudgementType.Just);
                scoreManager.ProcessJudgement(justEvent);
            }

            // Assert
            Assert.True(scoreManager.CurrentScore <= ScoreManager.MaxScore);
            Assert.Equal(ScoreManager.MaxScore, scoreManager.CurrentScore);
        }

        [Fact]
        public void ScoreManager_ForceScoreOverflow_ClampedToMaxScore()
        {
            // Arrange
            var scoreManager = new ScoreManager(1); // Single note gives 1,000,000 points
            
            // Act - Try to add more score after already at max
            var firstJustEvent = new JudgementEvent(0, 0, 0.0, JudgementType.Just);
            scoreManager.ProcessJudgement(firstJustEvent);
            
            // Verify we're at max
            Assert.Equal(ScoreManager.MaxScore, scoreManager.CurrentScore);
            
            // Try to add more (this should be clamped)
            var secondJustEvent = new JudgementEvent(1, 0, 0.0, JudgementType.Just);
            scoreManager.ProcessJudgement(secondJustEvent);

            // Assert
            Assert.Equal(ScoreManager.MaxScore, scoreManager.CurrentScore);
        }

        [Fact]
        public void ScoreManager_HighNoteCountWithMixedJudgements_NeverExceedsMax()
        {
            // Arrange
            var scoreManager = new ScoreManager(2000); // High note count
            
            // Act - Mix of all judgement types, heavily weighted toward Just
            for (int i = 0; i < 1500; i++)
            {
                var justEvent = new JudgementEvent(i, 0, 0.0, JudgementType.Just);
                scoreManager.ProcessJudgement(justEvent);
            }
            
            for (int i = 1500; i < 1800; i++)
            {
                var greatEvent = new JudgementEvent(i, 0, 40.0, JudgementType.Great);
                scoreManager.ProcessJudgement(greatEvent);
            }
            
            for (int i = 1800; i < 1900; i++)
            {
                var goodEvent = new JudgementEvent(i, 0, 80.0, JudgementType.Good);
                scoreManager.ProcessJudgement(goodEvent);
            }
            
            for (int i = 1900; i < 2000; i++)
            {
                var poorEvent = new JudgementEvent(i, 0, 120.0, JudgementType.Poor);
                scoreManager.ProcessJudgement(poorEvent);
            }

            // Assert
            Assert.True(scoreManager.CurrentScore <= ScoreManager.MaxScore);
        }

        [Fact]
        public void ScoreManager_BaseScoreCalculation_ProducesCorrectMax()
        {
            // Test various note counts to ensure base score calculation works correctly
            var testCases = new[] { 1, 10, 100, 1000, 10000, 100000 };
            
            foreach (var noteCount in testCases)
            {
                // Arrange
                var scoreManager = new ScoreManager(noteCount);
                
                // Assert base score calculation
                var expectedBaseScore = ScoreManager.MaxScore / noteCount;
                Assert.Equal(expectedBaseScore, scoreManager.BaseScore);
                
                // Assert theoretical max is correct
                var theoreticalMax = expectedBaseScore * noteCount;
                Assert.True(theoreticalMax <= ScoreManager.MaxScore);
                Assert.Equal(theoreticalMax, scoreManager.TheoreticalMaxScore);
            }
        }

        [Fact]
        public void ScoreManager_ExtremeNoteCount_HandlesCorrectly()
        {
            // Arrange - Very high note count that could cause integer overflow issues
            var scoreManager = new ScoreManager(1000000); // 1 million notes
            
            // Act - Process some hits
            for (int i = 0; i < 1000; i++)
            {
                var justEvent = new JudgementEvent(i, 0, 0.0, JudgementType.Just);
                scoreManager.ProcessJudgement(justEvent);
            }

            // Assert
            Assert.True(scoreManager.CurrentScore <= ScoreManager.MaxScore);
            Assert.True(scoreManager.CurrentScore >= 0);
            
            // Base score should be 1 for 1 million notes (1,000,000 / 1,000,000 = 1)
            Assert.Equal(1, scoreManager.BaseScore);
        }

        [Fact]
        public void ScoreManager_ScoreChangedEvent_RespectsMaxCap()
        {
            // Arrange
            var scoreManager = new ScoreManager(1);
            
            ScoreChangedEventArgs? lastEventArgs = null;
            scoreManager.ScoreChanged += (sender, e) => lastEventArgs = e;
            
            var justEvent = new JudgementEvent(0, 0, 0.0, JudgementType.Just);

            // Act
            scoreManager.ProcessJudgement(justEvent);

            // Assert
            Assert.NotNull(lastEventArgs);
            Assert.Equal(ScoreManager.MaxScore, lastEventArgs.CurrentScore);
            Assert.True(lastEventArgs.CurrentScore <= ScoreManager.MaxScore);
        }

        [Fact]
        public void ScoreManager_Reset_AllowsScoreToRebuildToMax()
        {
            // Arrange
            var scoreManager = new ScoreManager(2);
            
            // Build up to max score
            var firstJustEvent = new JudgementEvent(0, 0, 0.0, JudgementType.Just);
            var secondJustEvent = new JudgementEvent(1, 0, 0.0, JudgementType.Just);
            
            scoreManager.ProcessJudgement(firstJustEvent);
            scoreManager.ProcessJudgement(secondJustEvent);
            
            Assert.Equal(ScoreManager.MaxScore, scoreManager.CurrentScore);

            // Act - Reset and rebuild
            scoreManager.Reset();
            Assert.Equal(0, scoreManager.CurrentScore);
            
            scoreManager.ProcessJudgement(firstJustEvent);
            scoreManager.ProcessJudgement(secondJustEvent);

            // Assert
            Assert.Equal(ScoreManager.MaxScore, scoreManager.CurrentScore);
        }

        [Theory]
        [InlineData(JudgementType.Just, 1.0)]
        [InlineData(JudgementType.Great, 0.9)]
        [InlineData(JudgementType.Good, 0.5)]
        [InlineData(JudgementType.Poor, 0.0)]
        [InlineData(JudgementType.Miss, 0.0)]
        public void ScoreManager_ScoreMultipliers_NeverCauseOverflow(JudgementType judgementType, double expectedMultiplier)
        {
            // Arrange
            var scoreManager = new ScoreManager(1);
            
            // Act
            var multiplier = scoreManager.GetScoreMultiplier(judgementType);
            var theoreticalScore = (int)Math.Floor(ScoreManager.MaxScore * multiplier);
            
            var judgementEvent = new JudgementEvent(0, 0, 0.0, judgementType);
            scoreManager.ProcessJudgement(judgementEvent);

            // Assert
            Assert.Equal(expectedMultiplier, multiplier, 0.01);
            Assert.True(scoreManager.CurrentScore <= ScoreManager.MaxScore);
            Assert.Equal(theoreticalScore, scoreManager.CurrentScore);
        }

        [Fact]
        public void ScoreManager_Statistics_ReflectMaxScoreCap()
        {
            // Arrange
            var scoreManager = new ScoreManager(100);
            
            // Process perfect play
            for (int i = 0; i < 100; i++)
            {
                var justEvent = new JudgementEvent(i, 0, 0.0, JudgementType.Just);
                scoreManager.ProcessJudgement(justEvent);
            }

            // Act
            var stats = scoreManager.GetStatistics();

            // Assert
            Assert.Equal(ScoreManager.MaxScore, stats.CurrentScore);
            Assert.True(stats.CurrentScore <= ScoreManager.MaxScore);
            Assert.Equal(100.0, stats.ScorePercentage, 0.1);
            Assert.Equal(stats.TheoreticalMaxScore, stats.CurrentScore);
        }
    }
}
