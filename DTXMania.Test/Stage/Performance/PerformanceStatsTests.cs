using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.Stage.Performance;
using Xunit;

namespace DTXMania.Test.Stage.Performance
{
    /// <summary>
    /// Tests for performance statistics data classes
    /// </summary>
    public class ComboStatisticsTests
    {
        [Fact]
        public void ComboStatistics_DefaultValues_ShouldBeZeroAndFalse()
        {
            var stats = new ComboStatistics();
            Assert.Equal(0, stats.CurrentCombo);
            Assert.Equal(0, stats.MaxCombo);
            Assert.False(stats.HasCombo);
        }

        [Fact]
        public void ComboStatistics_SetProperties_ShouldRetainValues()
        {
            var stats = new ComboStatistics
            {
                CurrentCombo = 50,
                MaxCombo = 100,
                HasCombo = true
            };
            Assert.Equal(50, stats.CurrentCombo);
            Assert.Equal(100, stats.MaxCombo);
            Assert.True(stats.HasCombo);
        }

        [Fact]
        public void ComboStatistics_ToString_ShouldContainComboValues()
        {
            var stats = new ComboStatistics { CurrentCombo = 42, MaxCombo = 100 };
            var result = stats.ToString();
            Assert.Contains("42", result);
            Assert.Contains("100", result);
        }
    }

    /// <summary>
    /// Tests for GaugeStatistics
    /// </summary>
    public class GaugeStatisticsTests
    {
        [Fact]
        public void GaugeStatistics_DefaultValues_ShouldBeCorrect()
        {
            var stats = new GaugeStatistics();
            Assert.Equal(0f, stats.CurrentLife);
            Assert.Equal(0f, stats.LifePercentage);
            Assert.False(stats.HasFailed);
            Assert.False(stats.IsInDanger);
        }

        [Fact]
        public void GaugeStatistics_SetProperties_ShouldRetainValues()
        {
            var stats = new GaugeStatistics
            {
                CurrentLife = 75.5f,
                LifePercentage = 0.755f,
                HasFailed = false,
                IsInDanger = true
            };
            Assert.Equal(75.5f, stats.CurrentLife);
            Assert.Equal(0.755f, stats.LifePercentage);
            Assert.False(stats.HasFailed);
            Assert.True(stats.IsInDanger);
        }

        [Fact]
        public void GaugeStatistics_ToString_WhenOK_ShouldContainOK()
        {
            var stats = new GaugeStatistics { CurrentLife = 80f, HasFailed = false, IsInDanger = false };
            var result = stats.ToString();
            Assert.Contains("OK", result);
        }

        [Fact]
        public void GaugeStatistics_ToString_WhenDanger_ShouldContainDanger()
        {
            var stats = new GaugeStatistics { CurrentLife = 20f, HasFailed = false, IsInDanger = true };
            var result = stats.ToString();
            Assert.Contains("DANGER", result);
        }

        [Fact]
        public void GaugeStatistics_ToString_WhenFailed_ShouldContainFailed()
        {
            var stats = new GaugeStatistics { CurrentLife = 0f, HasFailed = true };
            var result = stats.ToString();
            Assert.Contains("FAILED", result);
        }
    }

    /// <summary>
    /// Tests for ScoreStatistics
    /// </summary>
    public class ScoreStatisticsTests
    {
        [Fact]
        public void ScoreStatistics_DefaultValues_ShouldBeZero()
        {
            var stats = new ScoreStatistics();
            Assert.Equal(0, stats.CurrentScore);
            Assert.Equal(0, stats.BaseScore);
            Assert.Equal(0, stats.TotalNotes);
            Assert.Equal(0, stats.TheoreticalMaxScore);
            Assert.Equal(0.0, stats.ScorePercentage);
        }

        [Fact]
        public void ScoreStatistics_SetProperties_ShouldRetainValues()
        {
            var stats = new ScoreStatistics
            {
                CurrentScore = 500000,
                BaseScore = 1000,
                TotalNotes = 500,
                TheoreticalMaxScore = 1000000,
                ScorePercentage = 50.0
            };
            Assert.Equal(500000, stats.CurrentScore);
            Assert.Equal(1000, stats.BaseScore);
            Assert.Equal(500, stats.TotalNotes);
            Assert.Equal(1000000, stats.TheoreticalMaxScore);
            Assert.Equal(50.0, stats.ScorePercentage);
        }

        [Fact]
        public void ScoreStatistics_ToString_ShouldContainScoreInfo()
        {
            var prevCulture = System.Threading.Thread.CurrentThread.CurrentCulture;
            var prevUICulture = System.Threading.Thread.CurrentThread.CurrentUICulture;
            try
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
                System.Threading.Thread.CurrentThread.CurrentUICulture = System.Globalization.CultureInfo.InvariantCulture;

                var stats = new ScoreStatistics
                {
                    CurrentScore = 750000,
                    TheoreticalMaxScore = 1000000,
                    ScorePercentage = 75.0
                };
                var result = stats.ToString();
                Assert.Contains("750,000", result);
                Assert.Contains("1,000,000", result);
                Assert.Contains("75.0", result);
            }
            finally
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = prevCulture;
                System.Threading.Thread.CurrentThread.CurrentUICulture = prevUICulture;
            }
        }
    }

    /// <summary>
    /// Tests for EffectPoolingStats
    /// </summary>
    public class EffectPoolingStatsTests
    {
        [Fact]
        public void EffectPoolingStats_DefaultValues_ShouldBeZero()
        {
            var stats = new EffectPoolingStats();
            Assert.Equal(0, stats.PoolSize);
            Assert.Equal(0, stats.ActiveInstances);
            Assert.Equal(0L, stats.TotalRequests);
            Assert.Equal(0L, stats.PoolHits);
            Assert.Equal(0L, stats.PoolMisses);
        }

        [Fact]
        public void EffectPoolingStats_SetProperties_ShouldRetainValues()
        {
            var stats = new EffectPoolingStats
            {
                PoolSize = 50,
                ActiveInstances = 10,
                TotalRequests = 1000L,
                PoolHits = 900L,
                PoolMisses = 100L
            };
            Assert.Equal(50, stats.PoolSize);
            Assert.Equal(10, stats.ActiveInstances);
            Assert.Equal(1000L, stats.TotalRequests);
            Assert.Equal(900L, stats.PoolHits);
            Assert.Equal(100L, stats.PoolMisses);
        }
    }

    /// <summary>
    /// Tests for NoteRuntimeState
    /// </summary>
    public class NoteRuntimeStateTests
    {
        [Fact]
        public void NoteRuntimeState_DefaultValues_ShouldBeCorrect()
        {
            var state = new NoteRuntimeState();
            Assert.Equal(0, state.NoteId);
            Assert.Equal(NoteStatus.Pending, state.Status);
            Assert.Null(state.JudgementEvent);
        }

        [Fact]
        public void NoteRuntimeState_SetProperties_ShouldRetainValues()
        {
            var judgeEvent = new JudgementEvent(1, 3, 10.0, JudgementType.Great);
            var state = new NoteRuntimeState
            {
                NoteId = 42,
                Status = NoteStatus.Hit,
                JudgementEvent = judgeEvent
            };
            Assert.Equal(42, state.NoteId);
            Assert.Equal(NoteStatus.Hit, state.Status);
            Assert.Equal(judgeEvent, state.JudgementEvent);
        }

        [Fact]
        public void NoteStatus_AllValues_ShouldBeDistinct()
        {
            Assert.NotEqual(NoteStatus.Pending, NoteStatus.Hit);
            Assert.NotEqual(NoteStatus.Pending, NoteStatus.Missed);
            Assert.NotEqual(NoteStatus.Hit, NoteStatus.Missed);
        }
    }
}
