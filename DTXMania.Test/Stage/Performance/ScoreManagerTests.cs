using System;
using DTXMania.Game.Lib.Stage.Performance;
using DTXMania.Game.Lib.Song.Entities;
using Xunit;

namespace DTXMania.Test.Stage.Performance
{
    /// <summary>
    /// Comprehensive tests for ScoreManager
    /// </summary>
    [Trait("Category", "Unit")]
    public class ScoreManagerTests
    {
        #region Constructor Tests

        [Fact]
        public void Constructor_ValidTotalNotes_ShouldInitialize()
        {
            var manager = new ScoreManager(100);
            Assert.Equal(0, manager.CurrentScore);
            Assert.Equal(100, manager.TotalNotes);
            Assert.Equal(ScoreManager.MaxScore / 100, manager.BaseScore);
        }

        [Fact]
        public void Constructor_ZeroTotalNotes_ShouldThrow()
        {
            Assert.Throws<ArgumentException>(() => new ScoreManager(0));
        }

        [Fact]
        public void Constructor_NegativeTotalNotes_ShouldThrow()
        {
            Assert.Throws<ArgumentException>(() => new ScoreManager(-1));
        }

        #endregion

        #region ProcessJudgement Tests

        [Fact]
        public void ProcessJudgement_Just_ShouldAddFullBaseScore()
        {
            var manager = new ScoreManager(100);
            manager.ProcessJudgement(new JudgementEvent(0, 0, 0.0, JudgementType.Just));
            Assert.Equal(manager.BaseScore, manager.CurrentScore);
        }

        [Fact]
        public void ProcessJudgement_Great_ShouldAdd90Percent()
        {
            var manager = new ScoreManager(100);
            manager.ProcessJudgement(new JudgementEvent(0, 0, 20.0, JudgementType.Great));
            Assert.Equal((int)Math.Floor(manager.BaseScore * 0.9), manager.CurrentScore);
        }

        [Fact]
        public void ProcessJudgement_Good_ShouldAdd50Percent()
        {
            var manager = new ScoreManager(100);
            manager.ProcessJudgement(new JudgementEvent(0, 0, 40.0, JudgementType.Good));
            Assert.Equal((int)Math.Floor(manager.BaseScore * 0.5), manager.CurrentScore);
        }

        [Fact]
        public void ProcessJudgement_Poor_ShouldAddZero()
        {
            var manager = new ScoreManager(100);
            manager.ProcessJudgement(new JudgementEvent(0, 0, 120.0, JudgementType.Poor));
            Assert.Equal(0, manager.CurrentScore);
        }

        [Fact]
        public void ProcessJudgement_Miss_ShouldAddZero()
        {
            var manager = new ScoreManager(100);
            manager.ProcessJudgement(new JudgementEvent(0, 0, 200.0, JudgementType.Miss));
            Assert.Equal(0, manager.CurrentScore);
        }

        [Fact]
        public void ProcessJudgement_Null_ShouldNotThrow()
        {
            var manager = new ScoreManager(100);
            manager.ProcessJudgement(null);
            Assert.Equal(0, manager.CurrentScore);
        }

        [Fact]
        public void ProcessJudgement_RaisesScoreChangedEvent()
        {
            var manager = new ScoreManager(100);
            ScoreChangedEventArgs receivedArgs = null;
            manager.ScoreChanged += (s, e) => receivedArgs = e;

            manager.ProcessJudgement(new JudgementEvent(0, 0, 0.0, JudgementType.Just));

            Assert.NotNull(receivedArgs);
            Assert.Equal(0, receivedArgs.PreviousScore);
            Assert.Equal(manager.BaseScore, receivedArgs.CurrentScore);
        }

        #endregion

        #region GetScoreMultiplier Tests

        [Theory]
        [InlineData(JudgementType.Just, 1.0)]
        [InlineData(JudgementType.Great, 0.9)]
        [InlineData(JudgementType.Good, 0.5)]
        [InlineData(JudgementType.Poor, 0.0)]
        [InlineData(JudgementType.Miss, 0.0)]
        public void GetScoreMultiplier_ShouldReturnCorrectMultiplier(JudgementType type, double expected)
        {
            var manager = new ScoreManager(100);
            Assert.Equal(expected, manager.GetScoreMultiplier(type));
        }

        [Theory]
        [InlineData(JudgementType.Just, 1.0)]
        [InlineData(JudgementType.Great, 0.9)]
        [InlineData(JudgementType.Miss, 0.0)]
        public void GetScoreMultiplierStatic_ShouldReturnCorrectMultiplier(JudgementType type, double expected)
        {
            Assert.Equal(expected, ScoreManager.GetScoreMultiplierStatic(type));
        }

        #endregion

        #region CalculateScoreForJudgement Tests

        [Fact]
        public void CalculateScoreForJudgement_Just_ShouldEqualBaseScore()
        {
            var manager = new ScoreManager(100);
            Assert.Equal(manager.BaseScore, manager.CalculateScoreForJudgement(JudgementType.Just));
        }

        [Fact]
        public void CalculateScoreForJudgement_Miss_ShouldReturnZero()
        {
            var manager = new ScoreManager(100);
            Assert.Equal(0, manager.CalculateScoreForJudgement(JudgementType.Miss));
        }

        #endregion

        #region GetStatistics Tests

        [Fact]
        public void GetStatistics_ShouldReturnCurrentState()
        {
            var manager = new ScoreManager(100);
            manager.ProcessJudgement(new JudgementEvent(0, 0, 0.0, JudgementType.Just));
            manager.ProcessJudgement(new JudgementEvent(1, 0, 0.0, JudgementType.Just));

            var stats = manager.GetStatistics();

            Assert.Equal(manager.CurrentScore, stats.CurrentScore);
            Assert.Equal(manager.BaseScore, stats.BaseScore);
            Assert.Equal(100, stats.TotalNotes);
        }

        [Fact]
        public void GetStatistics_ZeroScore_ShouldReturnZeroPercentage()
        {
            var manager = new ScoreManager(100);
            var stats = manager.GetStatistics();
            Assert.Equal(0.0, stats.ScorePercentage);
        }

        #endregion

        #region TheoreticalMaxScore Tests

        [Fact]
        public void TheoreticalMaxScore_ShouldEqualBaseScoreTimesTotalNotes()
        {
            var manager = new ScoreManager(100);
            Assert.Equal(manager.BaseScore * 100, manager.TheoreticalMaxScore);
        }

        #endregion

        #region Reset Tests

        [Fact]
        public void Reset_ShouldSetScoreToZero()
        {
            var manager = new ScoreManager(100);
            manager.ProcessJudgement(new JudgementEvent(0, 0, 0.0, JudgementType.Just));
            Assert.True(manager.CurrentScore > 0);

            manager.Reset();

            Assert.Equal(0, manager.CurrentScore);
        }

        [Fact]
        public void Reset_ShouldRaiseScoreChangedEvent()
        {
            var manager = new ScoreManager(100);
            manager.ProcessJudgement(new JudgementEvent(0, 0, 0.0, JudgementType.Just));

            ScoreChangedEventArgs receivedArgs = null;
            manager.ScoreChanged += (s, e) => receivedArgs = e;
            manager.Reset();

            Assert.NotNull(receivedArgs);
            Assert.Equal(0, receivedArgs.CurrentScore);
        }

        #endregion

        #region Dispose Tests

        [Fact]
        public void Dispose_ShouldNotThrow()
        {
            var manager = new ScoreManager(100);
            manager.Dispose();
        }

        [Fact]
        public void Dispose_CalledTwice_ShouldNotThrow()
        {
            var manager = new ScoreManager(100);
            manager.Dispose();
            manager.Dispose();
        }

        [Fact]
        public void Dispose_AfterDispose_ProcessJudgement_ShouldDoNothing()
        {
            var manager = new ScoreManager(100);
            manager.Dispose();
            manager.ProcessJudgement(new JudgementEvent(0, 0, 0.0, JudgementType.Just));
            Assert.Equal(0, manager.CurrentScore);
        }

        #endregion
    }
}
