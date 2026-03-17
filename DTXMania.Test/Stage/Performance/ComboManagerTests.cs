using System;
using DTXMania.Game.Lib.Stage.Performance;
using DTXMania.Game.Lib.Song.Entities;
using Xunit;

namespace DTXMania.Test.Stage.Performance
{
    /// <summary>
    /// Comprehensive tests for ComboManager
    /// </summary>
    public class ComboManagerTests
    {
        #region Constructor Tests

        [Fact]
        public void Constructor_ShouldInitializeWithZeroCombo()
        {
            var manager = new ComboManager();
            Assert.Equal(0, manager.CurrentCombo);
            Assert.Equal(0, manager.MaxCombo);
            Assert.False(manager.HasCombo);
        }

        #endregion

        #region ProcessJudgement Tests

        [Fact]
        public void ProcessJudgement_Just_ShouldIncrementCombo()
        {
            var manager = new ComboManager();
            manager.ProcessJudgement(new JudgementEvent(0, 0, 0.0, JudgementType.Just));
            Assert.Equal(1, manager.CurrentCombo);
            Assert.True(manager.HasCombo);
        }

        [Fact]
        public void ProcessJudgement_Great_ShouldIncrementCombo()
        {
            var manager = new ComboManager();
            manager.ProcessJudgement(new JudgementEvent(0, 0, 20.0, JudgementType.Great));
            Assert.Equal(1, manager.CurrentCombo);
        }

        [Fact]
        public void ProcessJudgement_Good_ShouldIncrementCombo()
        {
            var manager = new ComboManager();
            manager.ProcessJudgement(new JudgementEvent(0, 0, 40.0, JudgementType.Good));
            Assert.Equal(1, manager.CurrentCombo);
        }

        [Fact]
        public void ProcessJudgement_Miss_ShouldNotIncrementCombo()
        {
            var manager = new ComboManager();
            manager.ProcessJudgement(new JudgementEvent(0, 0, 200.0, JudgementType.Miss));
            Assert.Equal(0, manager.CurrentCombo);
        }

        [Fact]
        public void ProcessJudgement_Null_ShouldNotThrow()
        {
            var manager = new ComboManager();
            manager.ProcessJudgement(null); // Should not throw
            Assert.Equal(0, manager.CurrentCombo);
        }

        [Fact]
        public void ProcessJudgement_MultipleJust_ShouldIncrementEachTime()
        {
            var manager = new ComboManager();
            for (int i = 0; i < 5; i++)
            {
                manager.ProcessJudgement(new JudgementEvent(i, 0, 0.0, JudgementType.Just));
            }
            Assert.Equal(5, manager.CurrentCombo);
        }

        [Fact]
        public void ProcessJudgement_UpdatesMaxCombo()
        {
            var manager = new ComboManager();
            for (int i = 0; i < 10; i++)
            {
                manager.ProcessJudgement(new JudgementEvent(i, 0, 0.0, JudgementType.Just));
            }
            Assert.Equal(10, manager.MaxCombo);

            // Miss resets current but not max
            manager.ProcessJudgement(new JudgementEvent(10, 0, 200.0, JudgementType.Miss));
            Assert.Equal(0, manager.CurrentCombo);
            Assert.Equal(10, manager.MaxCombo);
        }

        [Fact]
        public void ProcessJudgement_RaisesComboChangedEvent()
        {
            var manager = new ComboManager();
            ComboChangedEventArgs receivedArgs = null;
            manager.ComboChanged += (s, e) => receivedArgs = e;

            manager.ProcessJudgement(new JudgementEvent(0, 0, 0.0, JudgementType.Just));

            Assert.NotNull(receivedArgs);
            Assert.Equal(0, receivedArgs.PreviousCombo);
            Assert.Equal(1, receivedArgs.CurrentCombo);
            Assert.False(receivedArgs.WasReset);
        }

        [Fact]
        public void ProcessJudgement_Miss_RaisesComboChangedWithWasReset()
        {
            var manager = new ComboManager();
            manager.ProcessJudgement(new JudgementEvent(0, 0, 0.0, JudgementType.Just));
            manager.ProcessJudgement(new JudgementEvent(1, 0, 0.0, JudgementType.Just));

            ComboChangedEventArgs receivedArgs = null;
            manager.ComboChanged += (s, e) => receivedArgs = e;

            manager.ProcessJudgement(new JudgementEvent(2, 0, 200.0, JudgementType.Miss));

            Assert.NotNull(receivedArgs);
            Assert.True(receivedArgs.WasReset);
            Assert.Equal(0, receivedArgs.CurrentCombo);
        }

        [Fact]
        public void ProcessJudgement_RaisesMaxComboChangedEvent()
        {
            var manager = new ComboManager();
            MaxComboChangedEventArgs receivedArgs = null;
            manager.MaxComboChanged += (s, e) => receivedArgs = e;

            manager.ProcessJudgement(new JudgementEvent(0, 0, 0.0, JudgementType.Just));

            Assert.NotNull(receivedArgs);
            Assert.Equal(0, receivedArgs.PreviousMaxCombo);
            Assert.Equal(1, receivedArgs.NewMaxCombo);
        }

        [Fact]
        public void ProcessJudgement_Miss_DoesNotRaiseComboChangedIfAlreadyZero()
        {
            var manager = new ComboManager();
            int eventCount = 0;
            manager.ComboChanged += (s, e) => eventCount++;

            manager.ProcessJudgement(new JudgementEvent(0, 0, 200.0, JudgementType.Miss));

            Assert.Equal(0, eventCount);
        }

        #endregion

        #region Reset Tests

        [Fact]
        public void Reset_ShouldClearCurrentAndMaxCombo()
        {
            var manager = new ComboManager();
            for (int i = 0; i < 5; i++)
            {
                manager.ProcessJudgement(new JudgementEvent(i, 0, 0.0, JudgementType.Just));
            }
            Assert.Equal(5, manager.MaxCombo);

            manager.Reset();

            Assert.Equal(0, manager.CurrentCombo);
            Assert.Equal(0, manager.MaxCombo);
        }

        [Fact]
        public void Reset_WithNonZeroCombo_RaisesComboChangedEvent()
        {
            var manager = new ComboManager();
            manager.ProcessJudgement(new JudgementEvent(0, 0, 0.0, JudgementType.Just));
            manager.ProcessJudgement(new JudgementEvent(1, 0, 0.0, JudgementType.Just));

            ComboChangedEventArgs receivedArgs = null;
            manager.ComboChanged += (s, e) => receivedArgs = e;

            manager.Reset();

            Assert.NotNull(receivedArgs);
            Assert.True(receivedArgs.WasReset);
        }

        [Fact]
        public void Reset_WithNonZeroMaxCombo_RaisesMaxComboChangedEvent()
        {
            var manager = new ComboManager();
            for (int i = 0; i < 3; i++)
            {
                manager.ProcessJudgement(new JudgementEvent(i, 0, 0.0, JudgementType.Just));
            }

            MaxComboChangedEventArgs receivedArgs = null;
            manager.MaxComboChanged += (s, e) => receivedArgs = e;

            manager.Reset();

            Assert.NotNull(receivedArgs);
            Assert.Equal(0, receivedArgs.NewMaxCombo);
        }

        [Fact]
        public void Reset_OnZeroCombo_ShouldNotRaiseEvents()
        {
            var manager = new ComboManager();
            int eventCount = 0;
            manager.ComboChanged += (s, e) => eventCount++;
            manager.MaxComboChanged += (s, e) => eventCount++;

            manager.Reset();

            Assert.Equal(0, eventCount);
        }

        #endregion

        #region GetStatistics Tests

        [Fact]
        public void GetStatistics_ShouldReturnCurrentState()
        {
            var manager = new ComboManager();
            for (int i = 0; i < 5; i++)
            {
                manager.ProcessJudgement(new JudgementEvent(i, 0, 0.0, JudgementType.Just));
            }

            var stats = manager.GetStatistics();

            Assert.Equal(5, stats.CurrentCombo);
            Assert.Equal(5, stats.MaxCombo);
            Assert.True(stats.HasCombo);
        }

        #endregion

        #region Dispose Tests

        [Fact]
        public void Dispose_ShouldNotThrow()
        {
            var manager = new ComboManager();
            manager.Dispose(); // Should not throw
        }

        [Fact]
        public void Dispose_ClearsEventSubscribers()
        {
            var manager = new ComboManager();
            int eventCount = 0;
            manager.ComboChanged += (s, e) => eventCount++;

            manager.Dispose();

            // After dispose, ProcessJudgement should do nothing
            manager.ProcessJudgement(new JudgementEvent(0, 0, 0.0, JudgementType.Just));
            Assert.Equal(0, eventCount);
        }

        [Fact]
        public void Dispose_CalledTwice_ShouldNotThrow()
        {
            var manager = new ComboManager();
            manager.Dispose();
            manager.Dispose(); // Should not throw on second call
        }

        #endregion
    }
}
