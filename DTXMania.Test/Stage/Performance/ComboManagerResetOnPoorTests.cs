using System;
using DTXMania.Game.Lib.Stage.Performance;
using DTXMania.Game.Lib.Song.Entities;
using Xunit;

namespace DTXMania.Test.Stage.Performance
{
    /// <summary>
    /// Unit tests for ComboManager reset on Poor judgements.
    /// Tests that the ComboManager correctly resets combo when Poor or Miss judgements occur.
    /// </summary>
    public class ComboManagerResetOnPoorTests
    {
        [Fact]
        public void ComboManager_PoorJudgement_ResetsCombo()
        {
            // Arrange
            var comboManager = new ComboManager();
            
            // Build up a combo first
            var justEvent = new JudgementEvent(0, 0, 0.0, JudgementType.Just);
            var greatEvent = new JudgementEvent(1, 0, 40.0, JudgementType.Great);
            var goodEvent = new JudgementEvent(2, 0, 80.0, JudgementType.Good);
            
            comboManager.ProcessJudgement(justEvent);
            comboManager.ProcessJudgement(greatEvent);
            comboManager.ProcessJudgement(goodEvent);
            
            Assert.Equal(3, comboManager.CurrentCombo);

            // Act - Poor judgement should reset combo
            var poorEvent = new JudgementEvent(3, 0, 120.0, JudgementType.Poor);
            comboManager.ProcessJudgement(poorEvent);

            // Assert
            Assert.Equal(0, comboManager.CurrentCombo);
            Assert.False(comboManager.HasCombo);
        }

        [Fact]
        public void ComboManager_MissJudgement_ResetsCombo()
        {
            // Arrange
            var comboManager = new ComboManager();
            
            // Build up a combo first
            var justEvent = new JudgementEvent(0, 0, 0.0, JudgementType.Just);
            var greatEvent = new JudgementEvent(1, 0, 40.0, JudgementType.Great);
            
            comboManager.ProcessJudgement(justEvent);
            comboManager.ProcessJudgement(greatEvent);
            
            Assert.Equal(2, comboManager.CurrentCombo);

            // Act - Miss judgement should reset combo
            var missEvent = new JudgementEvent(2, 0, 200.0, JudgementType.Miss);
            comboManager.ProcessJudgement(missEvent);

            // Assert
            Assert.Equal(0, comboManager.CurrentCombo);
            Assert.False(comboManager.HasCombo);
        }

        [Theory]
        [InlineData(JudgementType.Just, true)]      // Should increment combo
        [InlineData(JudgementType.Great, true)]    // Should increment combo
        [InlineData(JudgementType.Good, true)]     // Should increment combo
        [InlineData(JudgementType.Poor, false)]    // Should reset combo
        [InlineData(JudgementType.Miss, false)]    // Should reset combo
        public void ComboManager_VariousJudgements_CorrectComboHandling(JudgementType judgementType, bool shouldMaintainCombo)
        {
            // Arrange
            var comboManager = new ComboManager();
            
            // Build up initial combo
            var initialEvent = new JudgementEvent(0, 0, 0.0, JudgementType.Just);
            comboManager.ProcessJudgement(initialEvent);
            Assert.Equal(1, comboManager.CurrentCombo);

            // Act
            var testEvent = new JudgementEvent(1, 0, 0.0, judgementType);
            comboManager.ProcessJudgement(testEvent);

            // Assert
            if (shouldMaintainCombo)
            {
                Assert.Equal(2, comboManager.CurrentCombo);
                Assert.True(comboManager.HasCombo);
            }
            else
            {
                Assert.Equal(0, comboManager.CurrentCombo);
                Assert.False(comboManager.HasCombo);
            }
        }

        [Fact]
        public void ComboManager_PoorAfterLongCombo_ResetsToZero()
        {
            // Arrange
            var comboManager = new ComboManager();
            
            // Build up a long combo (50 hits)
            for (int i = 0; i < 50; i++)
            {
                var event1 = new JudgementEvent(i, 0, 0.0, JudgementType.Just);
                comboManager.ProcessJudgement(event1);
            }
            
            Assert.Equal(50, comboManager.CurrentCombo);
            Assert.Equal(50, comboManager.MaxCombo);

            // Act - Poor judgement should reset combo completely
            var poorEvent = new JudgementEvent(50, 0, 120.0, JudgementType.Poor);
            comboManager.ProcessJudgement(poorEvent);

            // Assert
            Assert.Equal(0, comboManager.CurrentCombo);
            Assert.Equal(50, comboManager.MaxCombo); // Max combo should be preserved
            Assert.False(comboManager.HasCombo);
        }

        [Fact]
        public void ComboManager_ComboResetEvent_FiresCorrectly()
        {
            // Arrange
            var comboManager = new ComboManager();
            
            ComboChangedEventArgs? capturedEventArgs = null;
            comboManager.ComboChanged += (sender, e) => capturedEventArgs = e;
            
            // Build up combo
            var justEvent = new JudgementEvent(0, 0, 0.0, JudgementType.Just);
            comboManager.ProcessJudgement(justEvent);
            
            // Reset captured event
            capturedEventArgs = null;

            // Act - Poor judgement should trigger combo reset event
            var poorEvent = new JudgementEvent(1, 0, 120.0, JudgementType.Poor);
            comboManager.ProcessJudgement(poorEvent);

            // Assert
            Assert.NotNull(capturedEventArgs);
            Assert.Equal(1, capturedEventArgs.PreviousCombo);
            Assert.Equal(0, capturedEventArgs.CurrentCombo);
            Assert.Equal(JudgementType.Poor, capturedEventArgs.JudgementType);
            Assert.True(capturedEventArgs.WasReset);
        }

        [Fact]
        public void ComboManager_MultiplePoorHits_ComboStaysAtZero()
        {
            // Arrange
            var comboManager = new ComboManager();
            
            // Build up combo
            var justEvent = new JudgementEvent(0, 0, 0.0, JudgementType.Just);
            comboManager.ProcessJudgement(justEvent);
            Assert.Equal(1, comboManager.CurrentCombo);

            // Act - Multiple Poor/Miss hits
            var poorEvent1 = new JudgementEvent(1, 0, 120.0, JudgementType.Poor);
            var missEvent = new JudgementEvent(2, 0, 200.0, JudgementType.Miss);
            var poorEvent2 = new JudgementEvent(3, 0, 130.0, JudgementType.Poor);
            
            comboManager.ProcessJudgement(poorEvent1);
            comboManager.ProcessJudgement(missEvent);
            comboManager.ProcessJudgement(poorEvent2);

            // Assert
            Assert.Equal(0, comboManager.CurrentCombo);
            Assert.False(comboManager.HasCombo);
        }

        [Fact]
        public void ComboManager_ComboRebuildAfterPoor_StartsFromZero()
        {
            // Arrange
            var comboManager = new ComboManager();
            
            // Build up combo
            var justEvent1 = new JudgementEvent(0, 0, 0.0, JudgementType.Just);
            var greatEvent = new JudgementEvent(1, 0, 40.0, JudgementType.Great);
            comboManager.ProcessJudgement(justEvent1);
            comboManager.ProcessJudgement(greatEvent);
            Assert.Equal(2, comboManager.CurrentCombo);

            // Reset with Poor
            var poorEvent = new JudgementEvent(2, 0, 120.0, JudgementType.Poor);
            comboManager.ProcessJudgement(poorEvent);
            Assert.Equal(0, comboManager.CurrentCombo);

            // Act - Rebuild combo
            var justEvent2 = new JudgementEvent(3, 0, 0.0, JudgementType.Just);
            var justEvent3 = new JudgementEvent(4, 0, 0.0, JudgementType.Just);
            comboManager.ProcessJudgement(justEvent2);
            comboManager.ProcessJudgement(justEvent3);

            // Assert
            Assert.Equal(2, comboManager.CurrentCombo);
            Assert.True(comboManager.HasCombo);
        }

        [Fact]
        public void ComboManager_PoorResetDoesNotAffectMaxCombo()
        {
            // Arrange
            var comboManager = new ComboManager();
            
            // Build up to high combo
            for (int i = 0; i < 25; i++)
            {
                var justEvent = new JudgementEvent(i, 0, 0.0, JudgementType.Just);
                comboManager.ProcessJudgement(justEvent);
            }
            
            Assert.Equal(25, comboManager.CurrentCombo);
            Assert.Equal(25, comboManager.MaxCombo);

            // Act - Poor hit resets combo
            var poorEvent = new JudgementEvent(25, 0, 120.0, JudgementType.Poor);
            comboManager.ProcessJudgement(poorEvent);

            // Assert
            Assert.Equal(0, comboManager.CurrentCombo);
            Assert.Equal(25, comboManager.MaxCombo); // MaxCombo should be preserved
        }

        [Fact]
        public void ComboManager_AlternatingPoorAndGood_ComboNeverBuilds()
        {
            // Arrange
            var comboManager = new ComboManager();

            // Act - Alternating pattern of Good and Poor hits
            for (int i = 0; i < 10; i++)
            {
                var goodEvent = new JudgementEvent(i * 2, 0, 80.0, JudgementType.Good);
                var poorEvent = new JudgementEvent(i * 2 + 1, 0, 120.0, JudgementType.Poor);
                
                comboManager.ProcessJudgement(goodEvent);
                Assert.Equal(1, comboManager.CurrentCombo); // Should increment to 1
                
                comboManager.ProcessJudgement(poorEvent);
                Assert.Equal(0, comboManager.CurrentCombo); // Should reset to 0
            }

            // Assert
            Assert.Equal(0, comboManager.CurrentCombo);
            Assert.Equal(1, comboManager.MaxCombo); // Max should only ever be 1
            Assert.False(comboManager.HasCombo);
        }

        [Fact]
        public void ComboManager_Statistics_ReflectPoorResets()
        {
            // Arrange
            var comboManager = new ComboManager();
            
            // Build combo, reset with Poor, build again
            var justEvent1 = new JudgementEvent(0, 0, 0.0, JudgementType.Just);
            var justEvent2 = new JudgementEvent(1, 0, 0.0, JudgementType.Just);
            var poorEvent = new JudgementEvent(2, 0, 120.0, JudgementType.Poor);
            var justEvent3 = new JudgementEvent(3, 0, 0.0, JudgementType.Just);
            
            comboManager.ProcessJudgement(justEvent1);
            comboManager.ProcessJudgement(justEvent2);
            comboManager.ProcessJudgement(poorEvent);
            comboManager.ProcessJudgement(justEvent3);

            // Act
            var stats = comboManager.GetStatistics();

            // Assert
            Assert.Equal(1, stats.CurrentCombo);
            Assert.Equal(2, stats.MaxCombo);
            Assert.True(stats.HasCombo);
        }

        [Fact]
        public void ComboManager_ZeroComboAfterPoor_HasComboIsFalse()
        {
            // Arrange
            var comboManager = new ComboManager();
            
            // Build combo
            var justEvent = new JudgementEvent(0, 0, 0.0, JudgementType.Just);
            comboManager.ProcessJudgement(justEvent);
            Assert.True(comboManager.HasCombo);

            // Act - Poor judgement
            var poorEvent = new JudgementEvent(1, 0, 120.0, JudgementType.Poor);
            comboManager.ProcessJudgement(poorEvent);

            // Assert
            Assert.False(comboManager.HasCombo);
            Assert.Equal(0, comboManager.CurrentCombo);
        }

        [Fact]
        public void ComboManager_MaxComboChangedEvent_NotFiredOnReset()
        {
            // Arrange
            var comboManager = new ComboManager();
            
            bool maxComboChangedFired = false;
            comboManager.MaxComboChanged += (sender, e) => maxComboChangedFired = true;
            
            // Build combo to establish max
            var justEvent = new JudgementEvent(0, 0, 0.0, JudgementType.Just);
            comboManager.ProcessJudgement(justEvent);
            
            // Reset flag after initial max combo established
            maxComboChangedFired = false;

            // Act - Poor reset should not fire MaxComboChanged
            var poorEvent = new JudgementEvent(1, 0, 120.0, JudgementType.Poor);
            comboManager.ProcessJudgement(poorEvent);

            // Assert
            Assert.False(maxComboChangedFired);
        }
    }
}
