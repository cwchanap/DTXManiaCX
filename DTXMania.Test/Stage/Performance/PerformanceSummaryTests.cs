using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.Stage.Performance;
using Xunit;

namespace DTXMania.Test.Stage.Performance
{
    /// <summary>
    /// Tests for the PerformanceSummary class
    /// </summary>
    [Trait("Category", "Performance")]
    public class PerformanceSummaryTests
    {
        #region Constructor / Default Values Tests

        [Fact]
        public void DefaultConstructor_ShouldSetAllFieldsToDefaultValues()
        {
            var summary = new PerformanceSummary();

            Assert.Equal(0, summary.Score);
            Assert.Equal(0, summary.MaxCombo);
            Assert.False(summary.ClearFlag);
            Assert.Equal(0, summary.JustCount);
            Assert.Equal(0, summary.GreatCount);
            Assert.Equal(0, summary.GoodCount);
            Assert.Equal(0, summary.PoorCount);
            Assert.Equal(0, summary.MissCount);
            Assert.Equal(0, summary.TotalNotes);
            Assert.Equal(0.0f, summary.FinalLife);
            Assert.Equal(CompletionReason.Unknown, summary.CompletionReason);
        }

        #endregion

        #region TotalJudgements Tests

        [Fact]
        public void TotalJudgements_WithNoJudgements_ShouldReturnZero()
        {
            var summary = new PerformanceSummary();
            Assert.Equal(0, summary.TotalJudgements);
        }

        [Fact]
        public void TotalJudgements_ShouldSumAllJudgementTypes()
        {
            var summary = new PerformanceSummary
            {
                JustCount = 10,
                GreatCount = 20,
                GoodCount = 5,
                PoorCount = 3,
                MissCount = 2
            };

            Assert.Equal(40, summary.TotalJudgements);
        }

        [Fact]
        public void TotalJudgements_AllJust_ShouldReturnJustCount()
        {
            var summary = new PerformanceSummary { JustCount = 100 };
            Assert.Equal(100, summary.TotalJudgements);
        }

        #endregion

        #region Accuracy Tests

        [Fact]
        public void Accuracy_WithNoNotes_ShouldReturnZero()
        {
            var summary = new PerformanceSummary { TotalNotes = 0 };
            Assert.Equal(0.0, summary.Accuracy);
        }

        [Fact]
        public void Accuracy_AllJust_ShouldReturn100()
        {
            var summary = new PerformanceSummary
            {
                TotalNotes = 100,
                JustCount = 100,
                GreatCount = 0,
                GoodCount = 0,
                PoorCount = 0,
                MissCount = 0
            };

            Assert.Equal(100.0, summary.Accuracy, 3);
        }

        [Fact]
        public void Accuracy_AllMiss_ShouldReturnZero()
        {
            var summary = new PerformanceSummary
            {
                TotalNotes = 100,
                JustCount = 0,
                GreatCount = 0,
                GoodCount = 0,
                PoorCount = 0,
                MissCount = 100
            };

            Assert.Equal(0.0, summary.Accuracy);
        }

        [Fact]
        public void Accuracy_MixedJudgements_ShouldCalculateWeightedAccuracy()
        {
            // 100 Just (weight 1.0) + 0 others = 100 weighted / 100 total = 100%
            // 50 Just (weight 1.0) + 50 Great (weight 0.9) = 95 weighted / 100 total = 95%
            var summary = new PerformanceSummary
            {
                TotalNotes = 100,
                JustCount = 50,
                GreatCount = 50,
                GoodCount = 0,
                PoorCount = 0,
                MissCount = 0
            };

            double expected = ((50 * 1.0) + (50 * 0.9)) / 100.0 * 100.0;
            Assert.Equal(expected, summary.Accuracy, 3);
        }

        [Fact]
        public void Accuracy_AllGood_ShouldReturn50()
        {
            var summary = new PerformanceSummary
            {
                TotalNotes = 100,
                JustCount = 0,
                GreatCount = 0,
                GoodCount = 100,
                PoorCount = 0,
                MissCount = 0
            };

            // Good weight = 0.5, so 100 * 0.5 / 100 * 100 = 50%
            Assert.Equal(50.0, summary.Accuracy, 3);
        }

        #endregion

        #region HitRate Tests

        [Fact]
        public void HitRate_WithNoNotes_ShouldReturnZero()
        {
            var summary = new PerformanceSummary { TotalNotes = 0 };
            Assert.Equal(0.0, summary.HitRate);
        }

        [Fact]
        public void HitRate_AllHit_ShouldReturn100()
        {
            var summary = new PerformanceSummary
            {
                TotalNotes = 50,
                JustCount = 20,
                GreatCount = 20,
                GoodCount = 10,
                MissCount = 0
            };

            Assert.Equal(100.0, summary.HitRate, 3);
        }

        [Fact]
        public void HitRate_HalfMissed_ShouldReturn50()
        {
            var summary = new PerformanceSummary
            {
                TotalNotes = 100,
                JustCount = 50,
                GreatCount = 0,
                GoodCount = 0,
                PoorCount = 0,
                MissCount = 50
            };

            Assert.Equal(50.0, summary.HitRate, 3);
        }

        [Fact]
        public void HitRate_PoorNotCountedAsHit_ShouldExcludePoor()
        {
            // Poor notes don't count as successful hits in HitRate
            var summary = new PerformanceSummary
            {
                TotalNotes = 100,
                JustCount = 0,
                GreatCount = 0,
                GoodCount = 0,
                PoorCount = 100,
                MissCount = 0
            };

            Assert.Equal(0.0, summary.HitRate, 3);
        }

        #endregion

        #region IncrementJudgement Tests

        [Theory]
        [InlineData(JudgementType.Just)]
        [InlineData(JudgementType.Great)]
        [InlineData(JudgementType.Good)]
        [InlineData(JudgementType.Poor)]
        [InlineData(JudgementType.Miss)]
        public void IncrementJudgement_EachType_ShouldIncrementCorrectCounter(JudgementType type)
        {
            var summary = new PerformanceSummary();

            summary.IncrementJudgement(type);

            Assert.Equal(1, summary.GetJudgementCount(type));
        }

        [Fact]
        public void IncrementJudgement_MultipleIncrements_ShouldAccumulate()
        {
            var summary = new PerformanceSummary();

            for (int i = 0; i < 5; i++)
                summary.IncrementJudgement(JudgementType.Just);
            for (int i = 0; i < 3; i++)
                summary.IncrementJudgement(JudgementType.Great);

            Assert.Equal(5, summary.JustCount);
            Assert.Equal(3, summary.GreatCount);
            Assert.Equal(8, summary.TotalJudgements);
        }

        [Fact]
        public void IncrementJudgement_ShouldNotAffectOtherCounters()
        {
            var summary = new PerformanceSummary();
            summary.IncrementJudgement(JudgementType.Just);

            Assert.Equal(0, summary.GreatCount);
            Assert.Equal(0, summary.GoodCount);
            Assert.Equal(0, summary.PoorCount);
            Assert.Equal(0, summary.MissCount);
        }

        #endregion

        #region GetJudgementCount Tests

        [Theory]
        [InlineData(JudgementType.Just, 10)]
        [InlineData(JudgementType.Great, 20)]
        [InlineData(JudgementType.Good, 15)]
        [InlineData(JudgementType.Poor, 5)]
        [InlineData(JudgementType.Miss, 8)]
        public void GetJudgementCount_ShouldReturnCorrectCount(JudgementType type, int count)
        {
            var summary = new PerformanceSummary
            {
                JustCount = 10,
                GreatCount = 20,
                GoodCount = 15,
                PoorCount = 5,
                MissCount = 8
            };

            Assert.Equal(count, summary.GetJudgementCount(type));
        }

        [Fact]
        public void GetJudgementCount_InvalidType_ShouldReturnZero()
        {
            var summary = new PerformanceSummary { JustCount = 50 };
            Assert.Equal(0, summary.GetJudgementCount((JudgementType)999));
        }

        #endregion

        #region ToString Tests

        [Fact]
        public void ToString_ShouldContainScore()
        {
            var summary = new PerformanceSummary { Score = 950000 };

            // Force invariant culture so the formatted score is locale-independent.
            string result;
            var prevCulture = System.Threading.Thread.CurrentThread.CurrentCulture;
            var prevUICulture = System.Threading.Thread.CurrentThread.CurrentUICulture;
            try
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
                System.Threading.Thread.CurrentThread.CurrentUICulture = System.Globalization.CultureInfo.InvariantCulture;
                result = summary.ToString();
            }
            finally
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = prevCulture;
                System.Threading.Thread.CurrentThread.CurrentUICulture = prevUICulture;
            }

            Assert.Contains("950,000", result);
        }

        [Fact]
        public void ToString_ShouldContainMaxCombo()
        {
            var summary = new PerformanceSummary { MaxCombo = 123 };
            var result = summary.ToString();
            Assert.Contains("123", result);
        }

        [Fact]
        public void ToString_ShouldNotThrowForDefaultSummary()
        {
            var summary = new PerformanceSummary();
            var result = summary.ToString();
            Assert.NotNull(result);
            Assert.NotEmpty(result);
        }

        #endregion

        #region CompletionReason Tests

        [Fact]
        public void CompletionReason_SongComplete_ShouldSetCorrectly()
        {
            var summary = new PerformanceSummary { CompletionReason = CompletionReason.SongComplete };
            Assert.Equal(CompletionReason.SongComplete, summary.CompletionReason);
        }

        [Fact]
        public void CompletionReason_PlayerFailed_ShouldSetCorrectly()
        {
            var summary = new PerformanceSummary { CompletionReason = CompletionReason.PlayerFailed };
            Assert.Equal(CompletionReason.PlayerFailed, summary.CompletionReason);
        }

        [Fact]
        public void CompletionReason_PlayerQuit_ShouldSetCorrectly()
        {
            var summary = new PerformanceSummary { CompletionReason = CompletionReason.PlayerQuit };
            Assert.Equal(CompletionReason.PlayerQuit, summary.CompletionReason);
        }

        [Fact]
        public void ClearFlag_WhenSet_ShouldReturnTrue()
        {
            var summary = new PerformanceSummary { ClearFlag = true };
            Assert.True(summary.ClearFlag);
        }

        #endregion
    }
}
