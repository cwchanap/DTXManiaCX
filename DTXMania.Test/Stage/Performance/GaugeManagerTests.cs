using System;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.Stage.Performance;
using Xunit;

namespace DTXMania.Test.Stage.Performance
{
    /// <summary>
    /// Tests for GaugeManager
    /// </summary>
    public class GaugeManagerTests
    {
        #region Constructor Tests

        [Fact]
        public void Constructor_Default_ShouldStartAt50Percent()
        {
            var manager = new GaugeManager();
            Assert.Equal(GaugeManager.StartingLife, manager.CurrentLife);
            Assert.False(manager.HasFailed);
        }

        [Fact]
        public void Constructor_CustomStartingLife_ShouldUseProvidedValue()
        {
            var manager = new GaugeManager(80.0f);
            Assert.Equal(80.0f, manager.CurrentLife);
        }

        [Fact]
        public void Constructor_OverMaxLife_ShouldClampToMax()
        {
            var manager = new GaugeManager(150.0f);
            Assert.Equal(GaugeManager.MaxLife, manager.CurrentLife);
        }

        [Fact]
        public void Constructor_BelowMinLife_ShouldClampToMin()
        {
            var manager = new GaugeManager(-10.0f);
            Assert.Equal(GaugeManager.MinLife, manager.CurrentLife);
        }

        #endregion

        #region ProcessJudgement Tests

        [Fact]
        public void ProcessJudgement_Just_ShouldIncreaseLife()
        {
            var manager = new GaugeManager(50.0f);
            var initial = manager.CurrentLife;
            manager.ProcessJudgement(new JudgementEvent(0, 0, 0.0, JudgementType.Just));
            Assert.True(manager.CurrentLife > initial);
        }

        [Fact]
        public void ProcessJudgement_Great_ShouldIncreaseLife()
        {
            var manager = new GaugeManager(50.0f);
            var initial = manager.CurrentLife;
            manager.ProcessJudgement(new JudgementEvent(0, 0, 0.0, JudgementType.Great));
            Assert.True(manager.CurrentLife > initial);
        }

        [Fact]
        public void ProcessJudgement_Good_ShouldIncreaseLife()
        {
            var manager = new GaugeManager(50.0f);
            var initial = manager.CurrentLife;
            manager.ProcessJudgement(new JudgementEvent(0, 0, 0.0, JudgementType.Good));
            Assert.True(manager.CurrentLife > initial);
        }

        [Fact]
        public void ProcessJudgement_Poor_ShouldDecreaseLife()
        {
            var manager = new GaugeManager(50.0f);
            var initial = manager.CurrentLife;
            manager.ProcessJudgement(new JudgementEvent(0, 0, 0.0, JudgementType.Poor));
            Assert.True(manager.CurrentLife < initial);
        }

        [Fact]
        public void ProcessJudgement_Miss_ShouldDecreaseLife()
        {
            var manager = new GaugeManager(50.0f);
            var initial = manager.CurrentLife;
            manager.ProcessJudgement(new JudgementEvent(0, 0, 0.0, JudgementType.Miss));
            Assert.True(manager.CurrentLife < initial);
        }

        [Fact]
        public void ProcessJudgement_Null_ShouldNotThrow()
        {
            var manager = new GaugeManager(50.0f);
            var initial = manager.CurrentLife;
            manager.ProcessJudgement(null);
            Assert.Equal(initial, manager.CurrentLife);
        }

        [Fact]
        public void ProcessJudgement_LifeExceedsMax_ShouldClampToMax()
        {
            var manager = new GaugeManager(99.9f);
            manager.ProcessJudgement(new JudgementEvent(0, 0, 0.0, JudgementType.Just));
            Assert.Equal(GaugeManager.MaxLife, manager.CurrentLife);
        }

        [Fact]
        public void ProcessJudgement_LifeBelowFailureThreshold_ShouldSetHasFailed()
        {
            var manager = new GaugeManager(GaugeManager.FailureThreshold - 0.01f);
            manager.ProcessJudgement(new JudgementEvent(0, 0, 0.0, JudgementType.Miss));
            Assert.True(manager.HasFailed);
        }

        [Fact]
        public void ProcessJudgement_WhenAlreadyFailed_ShouldNotProcess()
        {
            var manager = new GaugeManager(1.0f);
            // First miss causes failure
            manager.ProcessJudgement(new JudgementEvent(0, 0, 0.0, JudgementType.Miss));
            Assert.True(manager.HasFailed);
            var lifeAfterFail = manager.CurrentLife;

            // Second judgement should be ignored
            manager.ProcessJudgement(new JudgementEvent(1, 0, 0.0, JudgementType.Just));
            Assert.Equal(lifeAfterFail, manager.CurrentLife);
        }

        #endregion

        #region GetLifeAdjustment Tests

        [Theory]
        [InlineData(JudgementType.Just, 2.0f)]
        [InlineData(JudgementType.Great, 1.5f)]
        [InlineData(JudgementType.Good, 1.0f)]
        [InlineData(JudgementType.Poor, -1.5f)]
        [InlineData(JudgementType.Miss, -3.0f)]
        public void GetLifeAdjustment_ShouldReturnCorrectValue(JudgementType type, float expected)
        {
            var manager = new GaugeManager();
            Assert.Equal(expected, manager.GetLifeAdjustment(type));
        }

        [Fact]
        public void GetLifeAdjustment_UnknownType_ShouldReturnZero()
        {
            var manager = new GaugeManager();
            Assert.Equal(0.0f, manager.GetLifeAdjustment((JudgementType)99));
        }

        #endregion

        #region Event Tests

        [Fact]
        public void ProcessJudgement_ShouldRaiseGaugeChangedEvent()
        {
            var manager = new GaugeManager(50.0f);
            GaugeChangedEventArgs received = null;
            manager.GaugeChanged += (s, e) => received = e;

            manager.ProcessJudgement(new JudgementEvent(0, 0, 0.0, JudgementType.Just));

            Assert.NotNull(received);
            Assert.Equal(JudgementType.Just, received.JudgementType);
            Assert.False(received.JustFailed);
        }

        [Fact]
        public void ProcessJudgement_WhenFailing_ShouldRaiseFailedEvent()
        {
            var manager = new GaugeManager(1.0f);
            FailureEventArgs receivedFailure = null;
            manager.Failed += (s, e) => receivedFailure = e;

            manager.ProcessJudgement(new JudgementEvent(0, 0, 0.0, JudgementType.Miss));

            Assert.NotNull(receivedFailure);
            Assert.Equal(JudgementType.Miss, receivedFailure.JudgementType);
        }

        [Fact]
        public void ProcessJudgement_WhenFailing_GaugeChangedShouldHaveJustFailed()
        {
            var manager = new GaugeManager(1.0f);
            GaugeChangedEventArgs received = null;
            manager.GaugeChanged += (s, e) => received = e;

            manager.ProcessJudgement(new JudgementEvent(0, 0, 0.0, JudgementType.Miss));

            Assert.NotNull(received);
            Assert.True(received.JustFailed);
        }

        #endregion

        #region Reset Tests

        [Fact]
        public void Reset_ShouldRestoreToDefaultStartingLife()
        {
            var manager = new GaugeManager(80.0f);
            manager.Reset();
            Assert.Equal(GaugeManager.StartingLife, manager.CurrentLife);
        }

        [Fact]
        public void Reset_WithCustomLife_ShouldRestoreToCustomValue()
        {
            var manager = new GaugeManager(50.0f);
            manager.Reset(70.0f);
            Assert.Equal(70.0f, manager.CurrentLife);
        }

        [Fact]
        public void Reset_ShouldClearFailedState()
        {
            var manager = new GaugeManager(1.0f);
            manager.ProcessJudgement(new JudgementEvent(0, 0, 0.0, JudgementType.Miss));
            Assert.True(manager.HasFailed);

            manager.Reset();
            Assert.False(manager.HasFailed);
        }

        [Fact]
        public void Reset_ShouldRaiseGaugeChangedEvent()
        {
            var manager = new GaugeManager(80.0f);
            GaugeChangedEventArgs received = null;
            manager.GaugeChanged += (s, e) => received = e;

            manager.Reset();

            Assert.NotNull(received);
            Assert.Null(received.JudgementType);
        }

        [Fact]
        public void Reset_WhenDisposed_ShouldNotThrow()
        {
            var manager = new GaugeManager();
            manager.Dispose();
            manager.Reset(); // Should not throw
        }

        #endregion

        #region Properties Tests

        [Fact]
        public void LifePercentage_ShouldBeBetweenZeroAndOne()
        {
            var manager = new GaugeManager(75.0f);
            Assert.Equal(0.75f, manager.LifePercentage, 3);
        }

        [Fact]
        public void IsInDanger_WhenLifeBelow20_ShouldBeTrue()
        {
            var manager = new GaugeManager(15.0f);
            Assert.True(manager.IsInDanger);
        }

        [Fact]
        public void IsInDanger_WhenLifeAbove20_ShouldBeFalse()
        {
            var manager = new GaugeManager(50.0f);
            Assert.False(manager.IsInDanger);
        }

        [Fact]
        public void IsInDanger_WhenFailed_ShouldBeFalse()
        {
            var manager = new GaugeManager(1.0f);
            manager.ProcessJudgement(new JudgementEvent(0, 0, 0.0, JudgementType.Miss));
            // After failing, IsInDanger should be false (already failed)
            Assert.False(manager.IsInDanger);
        }

        #endregion

        #region GetStatistics Tests

        [Fact]
        public void GetStatistics_ShouldReturnCurrentState()
        {
            var manager = new GaugeManager(75.0f);
            var stats = manager.GetStatistics();

            Assert.Equal(75.0f, stats.CurrentLife);
            Assert.Equal(0.75f, stats.LifePercentage, 3);
            Assert.False(stats.HasFailed);
        }

        [Fact]
        public void GetStatistics_WhenInDanger_ShouldReflectDangerState()
        {
            var manager = new GaugeManager(15.0f);
            var stats = manager.GetStatistics();
            Assert.True(stats.IsInDanger);
        }

        [Fact]
        public void GetStatistics_WhenFailed_ShouldReflectFailedState()
        {
            var manager = new GaugeManager(1.0f);
            manager.ProcessJudgement(new JudgementEvent(0, 0, 0.0, JudgementType.Miss));
            var stats = manager.GetStatistics();
            Assert.True(stats.HasFailed);
        }

        #endregion

        #region GaugeStatistics.ToString Tests

        [Fact]
        public void GaugeStatistics_ToString_WhenOK_ShouldContainOK()
        {
            var stats = new GaugeStatistics
            {
                CurrentLife = 75.0f,
                LifePercentage = 0.75f,
                HasFailed = false,
                IsInDanger = false
            };
            var result = stats.ToString();
            Assert.Contains("OK", result);
            Assert.Contains("75", result);
        }

        [Fact]
        public void GaugeStatistics_ToString_WhenDanger_ShouldContainDanger()
        {
            var stats = new GaugeStatistics
            {
                CurrentLife = 15.0f,
                LifePercentage = 0.15f,
                HasFailed = false,
                IsInDanger = true
            };
            var result = stats.ToString();
            Assert.Contains("DANGER", result);
        }

        [Fact]
        public void GaugeStatistics_ToString_WhenFailed_ShouldContainFailed()
        {
            var stats = new GaugeStatistics
            {
                CurrentLife = 0.0f,
                LifePercentage = 0.0f,
                HasFailed = true,
                IsInDanger = false
            };
            var result = stats.ToString();
            Assert.Contains("FAILED", result);
        }

        #endregion

        #region Dispose Tests

        [Fact]
        public void Dispose_ShouldNotThrow()
        {
            var manager = new GaugeManager();
            manager.Dispose(); // Should not throw
        }

        [Fact]
        public void Dispose_CalledTwice_ShouldNotThrow()
        {
            var manager = new GaugeManager();
            manager.Dispose();
            manager.Dispose(); // Double dispose should not throw
        }

        [Fact]
        public void ProcessJudgement_WhenDisposed_ShouldNotProcess()
        {
            var manager = new GaugeManager(50.0f);
            manager.Dispose();
            var lifeBeforeDisposed = manager.CurrentLife;
            manager.ProcessJudgement(new JudgementEvent(0, 0, 0.0, JudgementType.Just));
            Assert.Equal(lifeBeforeDisposed, manager.CurrentLife);
        }

        #endregion
    }
}
