using System;
using DTX.Stage.Performance;
using DTXMania.Game.Lib.Song.Entities;
using Xunit;

namespace DTXMania.Test.Stage.Performance
{
    /// <summary>
    /// Unit tests for GaugeManager fail threshold functionality.
    /// Tests that the GaugeManager correctly handles failure when life drops below 2%.
    /// </summary>
    public class GaugeManagerFailThresholdTests
    {
        [Fact]
        public void GaugeManager_FailureThreshold_IsSetTo2Percent()
        {
            // Assert
            Assert.Equal(2.0f, GaugeManager.FailureThreshold);
        }

        [Fact]
        public void GaugeManager_LifeDropsBelowThreshold_TriggersFailure()
        {
            // Arrange
            var gaugeManager = new GaugeManager(10.0f); // Start with low life
            
            bool failureTriggered = false;
            FailureEventArgs? capturedFailureArgs = null;
            
            gaugeManager.Failed += (sender, e) =>
            {
                failureTriggered = true;
                capturedFailureArgs = e;
            };

            // Act - Process enough Miss events to drop below 2%
            // Miss events cause -3% life each, so 3 misses should drop from 10% to 1%
            var missEvent1 = new JudgementEvent(0, 0, 200.0, JudgementType.Miss);
            var missEvent2 = new JudgementEvent(1, 0, 200.0, JudgementType.Miss);
            var missEvent3 = new JudgementEvent(2, 0, 200.0, JudgementType.Miss);
            
            gaugeManager.ProcessJudgement(missEvent1); // 10% - 3% = 7%
            gaugeManager.ProcessJudgement(missEvent2); // 7% - 3% = 4%
            gaugeManager.ProcessJudgement(missEvent3); // 4% - 3% = 1% (below 2% threshold)

            // Assert
            Assert.True(failureTriggered);
            Assert.True(gaugeManager.HasFailed);
            Assert.True(gaugeManager.CurrentLife < GaugeManager.FailureThreshold);
            Assert.NotNull(capturedFailureArgs);
            Assert.Equal(JudgementType.Miss, capturedFailureArgs.JudgementType);
        }

        [Fact]
        public void GaugeManager_LifeExactlyAtThreshold_DoesNotTriggerFailure()
        {
            // Arrange
            var gaugeManager = new GaugeManager(5.0f); // Start with 5% life
            
            bool failureTriggered = false;
            gaugeManager.Failed += (sender, e) => failureTriggered = true;

            // Act - Drop life to exactly 2% (threshold)
            var missEvent = new JudgementEvent(0, 0, 200.0, JudgementType.Miss); // -3% life
            gaugeManager.ProcessJudgement(missEvent); // 5% - 3% = 2%

            // Assert
            Assert.False(failureTriggered);
            Assert.False(gaugeManager.HasFailed);
            Assert.Equal(2.0f, gaugeManager.CurrentLife, 0.1f);
        }

        [Fact]
        public void GaugeManager_LifeJustBelowThreshold_TriggersFailure()
        {
            // Arrange
            var gaugeManager = new GaugeManager(4.0f); // Start with 4% life
            
            bool failureTriggered = false;
            gaugeManager.Failed += (sender, e) => failureTriggered = true;

            // Act - Drop life to just below 2%
            var poorEvent = new JudgementEvent(0, 0, 120.0, JudgementType.Poor); // -1.5% life
            gaugeManager.ProcessJudgement(poorEvent); // 4% - 1.5% = 2.5% (still above)
            
            Assert.False(failureTriggered); // Should not fail yet
            
            var anotherPoorEvent = new JudgementEvent(1, 0, 120.0, JudgementType.Poor); // -1.5% life
            gaugeManager.ProcessJudgement(anotherPoorEvent); // 2.5% - 1.5% = 1.0% (below threshold)

            // Assert
            Assert.True(failureTriggered);
            Assert.True(gaugeManager.HasFailed);
            Assert.True(gaugeManager.CurrentLife < GaugeManager.FailureThreshold);
        }

        [Theory]
        [InlineData(1.9f, true)]    // Below threshold
        [InlineData(1.5f, true)]    // Below threshold
        [InlineData(1.0f, true)]    // Below threshold
        [InlineData(0.5f, true)]    // Below threshold
        [InlineData(0.0f, true)]    // At minimum
        [InlineData(2.0f, false)]   // Exactly at threshold
        [InlineData(2.1f, false)]   // Above threshold
        [InlineData(5.0f, false)]   // Well above threshold
        public void GaugeManager_VariousLifeValues_CorrectFailureState(float lifeValue, bool shouldFail)
        {
            // Arrange
            var gaugeManager = new GaugeManager(100.0f); // Start with full life
            
            bool failureTriggered = false;
            gaugeManager.Failed += (sender, e) => failureTriggered = true;

            // Manually set life to test value by calculating required damage
            float targetLife = lifeValue;
            float damageNeeded = 100.0f - targetLife;
            
            // Apply damage through Miss events (-3% each)
            while (gaugeManager.CurrentLife > targetLife + 3.0f)
            {
                var missEvent = new JudgementEvent(0, 0, 200.0, JudgementType.Miss);
                gaugeManager.ProcessJudgement(missEvent);
            }
            
            // Fine-tune with Poor events (-1.5% each) if needed
            while (gaugeManager.CurrentLife > targetLife + 1.5f)
            {
                var poorEvent = new JudgementEvent(0, 0, 120.0, JudgementType.Poor);
                gaugeManager.ProcessJudgement(poorEvent);
            }

            // Reset failure trigger flag since we may have triggered it during setup
            failureTriggered = false;
            
            // Act - One more small damage to reach exact target if needed
            if (Math.Abs(gaugeManager.CurrentLife - targetLife) > 0.1f)
            {
                var finalPoorEvent = new JudgementEvent(0, 0, 120.0, JudgementType.Poor);
                gaugeManager.ProcessJudgement(finalPoorEvent);
            }

            // Assert
            if (shouldFail)
            {
                Assert.True(gaugeManager.HasFailed);
                Assert.True(gaugeManager.CurrentLife < GaugeManager.FailureThreshold);
            }
            else
            {
                Assert.False(gaugeManager.HasFailed);
                Assert.True(gaugeManager.CurrentLife >= GaugeManager.FailureThreshold);
            }
        }

        [Fact]
        public void GaugeManager_FailureEvent_ContainsCorrectInformation()
        {
            // Arrange
            var gaugeManager = new GaugeManager(5.0f);
            
            FailureEventArgs? capturedArgs = null;
            gaugeManager.Failed += (sender, e) => capturedArgs = e;

            // Act - Trigger failure with a Miss event
            var missEvent = new JudgementEvent(0, 0, 200.0, JudgementType.Miss);
            gaugeManager.ProcessJudgement(missEvent); // 5% - 3% = 2%
            
            var anotherMissEvent = new JudgementEvent(1, 0, 200.0, JudgementType.Miss);
            gaugeManager.ProcessJudgement(anotherMissEvent); // 2% - 3% = -1% (clamped to 0%, triggers failure)

            // Assert
            Assert.NotNull(capturedArgs);
            Assert.Equal(0.0f, capturedArgs.FinalLife); // Should be clamped to 0
            Assert.Equal(JudgementType.Miss, capturedArgs.JudgementType);
        }

        [Fact]
        public void GaugeManager_AfterFailure_StopsProcessingJudgements()
        {
            // Arrange
            var gaugeManager = new GaugeManager(3.0f);
            
            // Trigger failure
            var missEvent = new JudgementEvent(0, 0, 200.0, JudgementType.Miss);
            gaugeManager.ProcessJudgement(missEvent); // 3% - 3% = 0% (triggers failure)
            
            Assert.True(gaugeManager.HasFailed);
            float lifeAfterFailure = gaugeManager.CurrentLife;

            // Act - Try to process more judgements after failure
            var justEvent = new JudgementEvent(1, 0, 0.0, JudgementType.Just); // Should normally add +2% life
            gaugeManager.ProcessJudgement(justEvent);

            // Assert - Life should not change after failure
            Assert.Equal(lifeAfterFailure, gaugeManager.CurrentLife);
            Assert.True(gaugeManager.HasFailed);
        }

        [Fact]
        public void GaugeManager_GaugeChangedEvent_JustFailedFlag()
        {
            // Arrange
            var gaugeManager = new GaugeManager(3.0f);
            
            GaugeChangedEventArgs? capturedArgs = null;
            gaugeManager.GaugeChanged += (sender, e) => capturedArgs = e;

            // Act - Trigger failure
            var missEvent = new JudgementEvent(0, 0, 200.0, JudgementType.Miss);
            gaugeManager.ProcessJudgement(missEvent); // 3% - 3% = 0% (triggers failure)

            // Assert
            Assert.NotNull(capturedArgs);
            Assert.True(capturedArgs.JustFailed);
            Assert.Equal(JudgementType.Miss, capturedArgs.JudgementType);
            Assert.True(capturedArgs.CurrentLife < GaugeManager.FailureThreshold);
        }

        [Fact]
        public void GaugeManager_IsInDanger_CorrectlyIdentifiesDangerZone()
        {
            // Test the IsInDanger property (below 20% but not failed)
            var gaugeManager = new GaugeManager(25.0f);
            
            Assert.False(gaugeManager.IsInDanger); // 25% is not in danger
            
            // Drop to danger zone (below 20%)
            for (int i = 0; i < 3; i++) // 3 * -1.5% = -4.5%, so 25% - 4.5% = 20.5%
            {
                var poorEvent = new JudgementEvent(i, 0, 120.0, JudgementType.Poor);
                gaugeManager.ProcessJudgement(poorEvent);
            }
            
            // Should be in danger now (around 20.5%)
            var finalPoorEvent = new JudgementEvent(3, 0, 120.0, JudgementType.Poor);
            gaugeManager.ProcessJudgement(finalPoorEvent); // Should be around 19%
            
            Assert.True(gaugeManager.IsInDanger);
            Assert.False(gaugeManager.HasFailed);
        }

        [Fact]
        public void GaugeManager_Reset_ClearsFailureState()
        {
            // Arrange
            var gaugeManager = new GaugeManager(3.0f);
            
            // Trigger failure
            var missEvent = new JudgementEvent(0, 0, 200.0, JudgementType.Miss);
            gaugeManager.ProcessJudgement(missEvent);
            
            Assert.True(gaugeManager.HasFailed);

            // Act - Reset the gauge
            gaugeManager.Reset(50.0f); // Reset to 50% life

            // Assert
            Assert.False(gaugeManager.HasFailed);
            Assert.Equal(50.0f, gaugeManager.CurrentLife);
            Assert.False(gaugeManager.IsInDanger);
        }

        [Fact]
        public void GaugeManager_LifeAdjustments_CorrectValues()
        {
            // Test that life adjustments are correct for each judgement type
            var gaugeManager = new GaugeManager(50.0f);

            // Test each judgement type's life adjustment
            Assert.Equal(2.0f, gaugeManager.GetLifeAdjustment(JudgementType.Just));
            Assert.Equal(1.5f, gaugeManager.GetLifeAdjustment(JudgementType.Great));
            Assert.Equal(1.0f, gaugeManager.GetLifeAdjustment(JudgementType.Good));
            Assert.Equal(-1.5f, gaugeManager.GetLifeAdjustment(JudgementType.Poor));
            Assert.Equal(-3.0f, gaugeManager.GetLifeAdjustment(JudgementType.Miss));
        }

        [Fact]
        public void GaugeManager_Statistics_ReflectFailureState()
        {
            // Arrange
            var gaugeManager = new GaugeManager(3.0f);
            
            // Trigger failure
            var missEvent = new JudgementEvent(0, 0, 200.0, JudgementType.Miss);
            gaugeManager.ProcessJudgement(missEvent);

            // Act
            var stats = gaugeManager.GetStatistics();

            // Assert
            Assert.True(stats.HasFailed);
            Assert.True(stats.CurrentLife < GaugeManager.FailureThreshold);
            Assert.False(stats.IsInDanger); // If failed, should not be "in danger"
        }

        [Fact]
        public void GaugeManager_StartingLife_DefaultValue()
        {
            // Test default starting life
            var gaugeManager = new GaugeManager();
            
            Assert.Equal(GaugeManager.StartingLife, gaugeManager.CurrentLife);
            Assert.Equal(50.0f, gaugeManager.CurrentLife); // StartingLife constant should be 50%
        }

        [Fact]
        public void GaugeManager_MultipleFailureEvents_OnlyFirstTriggersEvent()
        {
            // Arrange
            var gaugeManager = new GaugeManager(4.0f);
            
            int failureEventCount = 0;
            gaugeManager.Failed += (sender, e) => failureEventCount++;

            // Act - Multiple events that would trigger failure
            var missEvent1 = new JudgementEvent(0, 0, 200.0, JudgementType.Miss); // 4% - 3% = 1% (triggers failure)
            var missEvent2 = new JudgementEvent(1, 0, 200.0, JudgementType.Miss); // Should not process (already failed)
            
            gaugeManager.ProcessJudgement(missEvent1);
            gaugeManager.ProcessJudgement(missEvent2);

            // Assert
            Assert.Equal(1, failureEventCount); // Should only fire once
            Assert.True(gaugeManager.HasFailed);
        }
    }
}
