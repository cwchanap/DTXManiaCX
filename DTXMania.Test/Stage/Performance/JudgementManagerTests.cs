using System;
using DTXMania.Game.Lib.Input;
using DTXMania.Test.Helpers;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Stage.Performance;
using DTXMania.Game.Lib.Song.Entities;
using Xunit;

namespace DTXMania.Test.Stage.Performance
{
    /// <summary>
    /// Unit tests for JudgementManager implementation
    /// Tests the keyboard mapping, note hit detection, and judgement logic
    /// </summary>
    public class JudgementManagerTests
    {
        [Fact]
        public void Constructor_ValidInputs_InitializesCorrectly()
        {
            // Arrange
            var mockInputManager = CreateMockInputManager();
            var chartManager = CreateTestChartManager();

            // Act
            var judgementManager = new JudgementManager(mockInputManager, chartManager);

            // Assert
            Assert.True(judgementManager.IsActive);
            Assert.NotNull(judgementManager.GetStatistics());
        }

        [Fact]
        public void Constructor_NullInputManager_ThrowsArgumentNullException()
        {
            // Arrange
            var chartManager = CreateTestChartManager();

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new JudgementManager(null, chartManager));
        }

        [Fact]
        public void Constructor_NullChartManager_ThrowsArgumentNullException()
        {
            // Arrange
            var mockInputManager = CreateMockInputManager();

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new JudgementManager(mockInputManager, null));
        }

        [Fact]
        public void Update_WhenLaneHitEventQueued_ShouldProcessPerfectHit()
        {
            // Arrange
            var compat = CreateMockInputManagerWithEvents();
            var judgementManager = new JudgementManager(compat, CreateTestChartManager());

            JudgementEvent? captured = null;
            judgementManager.JudgementMade += (_, e) => captured = e;

            // Act
            compat.TriggerLaneHit(0);
            judgementManager.Update(1000.0);

            // Assert
            Assert.NotNull(captured);
            Assert.Equal(JudgementType.Perfect, captured.Type);
        }

        [Fact]
        public void Update_WhenLaneHitEventQueued_ProcessesGoodHit()
        {
            // Arrange
            var compat = CreateMockInputManagerWithEvents();
            var judgementManager = new JudgementManager(compat, CreateTestChartManager());

            JudgementEvent? captured = null;
            judgementManager.JudgementMade += (_, e) => captured = e;

            // Act
            compat.TriggerLaneHit(0);
            judgementManager.Update(1089.0);

            // Assert
            Assert.NotNull(captured);
            Assert.Equal(JudgementType.Good, captured.Type);
        }

        [Fact]
        public void OnLaneHit_WhenLaneIsIgnored_DropsIgnoredInputButJudgesManualLane()
        {
            var compat = CreateMockInputManagerWithEvents();
            var judgementManager = new JudgementManager(compat, CreateTestChartManager());
            judgementManager.SetIgnoredPlayerInputLanes(new[] { 0 });

            JudgementEvent? captured = null;
            judgementManager.JudgementMade += (_, e) => captured = e;

            compat.TriggerLaneHit(0);
            judgementManager.Update(1000.0, 1000.0);

            Assert.Null(captured);

            compat.TriggerLaneHit(1);
            judgementManager.Update(1500.0, 1000.0);

            Assert.NotNull(captured);
            Assert.Equal(1, captured.Lane);
        }

        [Fact]
        public void SetIgnoredPlayerInputLanes_CopiesCallerCollection()
        {
            var compat = CreateMockInputManagerWithEvents();
            var judgementManager = new JudgementManager(compat, CreateTestChartManager());
            var ignoredLanes = new[] { 0 };
            judgementManager.SetIgnoredPlayerInputLanes(ignoredLanes);

            ignoredLanes[0] = 1;

            JudgementEvent? captured = null;
            judgementManager.JudgementMade += (_, e) => captured = e;

            compat.TriggerLaneHit(0);
            judgementManager.Update(1000.0, 1000.0);

            Assert.Null(captured);

            compat.TriggerLaneHit(1);
            judgementManager.Update(1500.0, 1000.0);

            Assert.NotNull(captured);
            Assert.Equal(1, captured.Lane);
        }

        [Fact]
        public void Update_MissedNote_GeneratesMissEvent()
        {
            // Arrange
            var mockInputManager = CreateMockInputManager();
            var chartManager = CreateTestChartManager();
            var judgementManager = new JudgementManager(mockInputManager, chartManager);

            JudgementEvent? capturedEvent = null;
            judgementManager.JudgementMade += (sender, e) => capturedEvent = e;

            // Act - Time passes beyond hit window (>200ms, HitDetectionWindowMs)
            judgementManager.Update(1250.0); // 250ms late (note at 1000ms)

            // Assert
            Assert.NotNull(capturedEvent);
            Assert.Equal(JudgementType.Miss, capturedEvent.Type);
            Assert.Equal(250.0, capturedEvent.DeltaMs, 1.0);
        }

        [Fact]
        public void Update_DoubleHit_PreventsDoubleJudgement()
        {
            // Arrange
            // compat.TriggerLaneHit validates the OnLaneHit subscription wiring:
            // JudgementManager must have subscribed to ModularInputManager.OnLaneHit so
            // that queued hits drive judgement, unlike the direct hit helper used in HitWindowTests.
            var compat = CreateMockInputManagerWithEvents();
            var chartManager = CreateTestChartManager();
            var judgementManager = new JudgementManager(compat, chartManager);

            int eventCount = 0;
            judgementManager.JudgementMade += (sender, e) => eventCount++;

            // Simulate lane hit event for lane 0
            compat.TriggerLaneHit(0);

            // Act - First hit
            judgementManager.Update(1000.0);

            // Simulate second hit on same note
            compat.TriggerLaneHit(0);

            // Second hit attempt on same note
            judgementManager.Update(1010.0);

            // Assert - Should only have one judgement event
            Assert.Equal(1, eventCount);
        }

        [Fact]
        public void GetStatistics_AfterHits_ReturnsCorrectCounts()
        {
            // Arrange
            // compat.TriggerLaneHit validates the OnLaneHit subscription wiring:
            // JudgementManager must have subscribed to ModularInputManager.OnLaneHit so
            // that queued hits drive judgement, unlike the direct hit helper used in HitWindowTests.
            var compat = CreateMockInputManagerWithEvents();
            var chartManager = CreateTestChartManager();
            var judgementManager = new JudgementManager(compat, chartManager);

            // Simulate lane hit for lane 0 (Perfect hit)
            compat.TriggerLaneHit(0);
            judgementManager.Update(1000.0); // Perfect hit

            // Simulate lane hit for lane 1 (Great hit, 40ms late)
            compat.TriggerLaneHit(1);
            judgementManager.Update(1540.0); // Great hit (40ms late)

            // Act
            var stats = judgementManager.GetStatistics();

            // Assert
            Assert.Equal(1, stats.PerfectCount);
            Assert.Equal(1, stats.GreatCount);
            Assert.Equal(0, stats.GoodCount);
            Assert.Equal(0, stats.PoorCount);
            Assert.Equal(0, stats.MissCount);
            Assert.Equal(2, stats.TotalHits);
            Assert.Equal(100.0, stats.Accuracy);
        }

        [Fact]
        public void KeyboardMapping_AllLanes_MappedCorrectly()
        {
            // Test that all lanes map to correct indices
            var expectedLanes = new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8 };

            var compat = CreateMockInputManagerWithEvents();
            var chartManager = CreateMultiLaneTestChart();
            var judgementManager = new JudgementManager(compat, chartManager);

            foreach (var expectedLane in expectedLanes)
            {
                JudgementEvent? capturedEvent = null;
                EventHandler<JudgementEvent> handler = (sender, e) => capturedEvent = e;
                judgementManager.JudgementMade += handler;

                // Simulate lane hit event for the expected lane
                compat.TriggerLaneHit(expectedLane);
                judgementManager.Update(1000.0 + expectedLane * 100); // Each lane has note at different time

                Assert.NotNull(capturedEvent);
                Assert.Equal(expectedLane, capturedEvent.Lane);

                // Reset for next test
                judgementManager.JudgementMade -= handler;
            }
        }

        private static ChartManager CreateTestChartManager()
        {
            var parsedChart = new ParsedChart("test.dtx")
            {
                Bpm = 120.0
            };

            // Add test notes at different times
            parsedChart.AddNote(new Note(0, 0, 96, 0x11, "01"));   // Lane 0 at 1000ms
            parsedChart.AddNote(new Note(1, 0, 144, 0x12, "01"));  // Lane 1 at 1500ms
            parsedChart.AddNote(new Note(2, 1, 0, 0x13, "01"));    // Lane 2 at 2000ms

            parsedChart.FinalizeChart();
            return new ChartManager(parsedChart);
        }

        private static ChartManager CreateMultiLaneTestChart()
        {
            var parsedChart = new ParsedChart("multi-lane-test.dtx")
            {
                Bpm = 120.0
            };

            // Add one note per lane using CORRECT channel-to-lane mapping
            // Based on final corrected DTXChartParser.ChannelToLaneMap:
            // LC, HH, LP, SN, HT, DB, LT, FT, CY
            var channelsForLanes = new int[]
            {
                0x1A, // Lane 0: Left Crash
                0x18, // Lane 1: Hi-Hat Open (primary for lane 1)
                0x1B, // Lane 2: Left Pedal
                0x12, // Lane 3: Snare (now in correct position)
                0x14, // Lane 4: High Tom
                0x13, // Lane 5: Bass Drum (now in correct position)
                0x15, // Lane 6: Low Tom (now in correct position)
                0x17, // Lane 7: Floor Tom
                0x16  // Lane 8: Crash/CY (primary for lane 8)
            };

            for (int lane = 0; lane < 9; lane++)
            {
                int channel = channelsForLanes[lane];
                double targetTimeMs = 1000.0 + lane * 100.0;
                var (bar, tick) = ToPositionAt120Bpm(targetTimeMs);
                parsedChart.AddNote(new Note(lane, bar, tick, channel, "01"));
            }

            parsedChart.FinalizeChart();
            return new ChartManager(parsedChart);
        }

        private static (int Bar, int Tick) ToPositionAt120Bpm(double timeMs)
        {
            const double measureMs = 2000.0;
            var totalTicks = (int)Math.Round(
                timeMs * ChartTimingMap.TicksPerMeasure / measureMs);

            return (
                totalTicks / ChartTimingMap.TicksPerMeasure,
                totalTicks % ChartTimingMap.TicksPerMeasure);
        }

        /// <summary>
        /// Creates a simple mock IInputManagerCompat for basic tests
        /// </summary>
        private static IInputManagerCompat CreateMockInputManager()
        {
            return new DTXMania.Test.Helpers.MockInputManagerCompat();
        }

        /// <summary>
        /// Creates a mock IInputManagerCompat with event simulation capabilities
        /// </summary>
        private static DTXMania.Test.Helpers.MockInputManagerCompat CreateMockInputManagerWithEvents()
        {
            return new DTXMania.Test.Helpers.MockInputManagerCompat();
        }

        [Fact]
        public void EnqueueLaneHit_WhenIsActiveFalse_DropsHit()
        {
            var compat = CreateMockInputManagerWithEvents();
            var judgementManager = new JudgementManager(compat, CreateTestChartManager());
            judgementManager.IsActive = false;

            JudgementEvent? captured = null;
            judgementManager.JudgementMade += (_, e) => captured = e;

            judgementManager.EnqueueLaneHit(0, "AutoPlay");
            judgementManager.Update(1000.0);

            Assert.Null(captured);
        }

        [Fact]
        public void ResolveAutoHit_WhenNoteIsPending_MarksAsHitWithPerfectTiming()
        {
            var compat = CreateMockInputManagerWithEvents();
            var chartManager = CreateTestChartManager();
            var judgementManager = new JudgementManager(compat, chartManager);
            var noteId = chartManager.AllNotes[0].Id;

            JudgementEvent? captured = null;
            judgementManager.JudgementMade += (_, e) => captured = e;

            var result = judgementManager.ResolveAutoHit(noteId);

            Assert.True(result);
            Assert.NotNull(captured);
            Assert.Equal(JudgementType.Perfect, captured.Type);
            Assert.Equal(0.0, captured.DeltaMs);
            Assert.Equal(NoteStatus.Hit, judgementManager.GetNoteRuntimeData(noteId)!.Status);
        }

        [Fact]
        public void ResolveAutoHit_WhenNoteAlreadyHit_ReturnsFalse()
        {
            var compat = CreateMockInputManagerWithEvents();
            var chartManager = CreateTestChartManager();
            var judgementManager = new JudgementManager(compat, chartManager);
            var noteId = chartManager.AllNotes[0].Id;

            // Hit it once
            judgementManager.ResolveAutoHit(noteId);

            JudgementEvent? captured = null;
            judgementManager.JudgementMade += (_, e) => captured = e;

            // Try again
            var result = judgementManager.ResolveAutoHit(noteId);

            Assert.False(result);
            Assert.Null(captured);
        }

        [Fact]
        public void ResolveAutoHit_WhenNoteAlreadyMissed_ReturnsFalse()
        {
            var compat = CreateMockInputManagerWithEvents();
            var chartManager = CreateTestChartManager();
            var judgementManager = new JudgementManager(compat, chartManager);
            var noteId = chartManager.AllNotes[0].Id;

            // Run update far past the note to trigger miss detection
            // The test chart has a note at 1000ms, so advance well past the 200ms miss window
            judgementManager.Update(1500.0);

            Assert.Equal(NoteStatus.Missed, judgementManager.GetNoteRuntimeData(noteId)!.Status);

            // Now try to resolve it with autoplay
            var result = judgementManager.ResolveAutoHit(noteId);

            Assert.False(result);
            Assert.Equal(NoteStatus.Missed, judgementManager.GetNoteRuntimeData(noteId)!.Status);
        }

        [Fact]
        public void ResolveAutoHit_WhenIsActiveFalse_ReturnsFalse()
        {
            var compat = CreateMockInputManagerWithEvents();
            var chartManager = CreateTestChartManager();
            var judgementManager = new JudgementManager(compat, chartManager);
            judgementManager.IsActive = false;
            var noteId = chartManager.AllNotes[0].Id;

            var result = judgementManager.ResolveAutoHit(noteId);

            Assert.False(result);
            Assert.Equal(NoteStatus.Pending, judgementManager.GetNoteRuntimeData(noteId)!.Status);
        }

        [Fact]
        public void ResolveAutoHit_WhenNoteIdNotFound_ReturnsFalse()
        {
            var compat = CreateMockInputManagerWithEvents();
            var chartManager = CreateTestChartManager();
            var judgementManager = new JudgementManager(compat, chartManager);

            var result = judgementManager.ResolveAutoHit(99999);

            Assert.False(result);
        }

        private static MockInputManagerCompat CreateMockInput() => new MockInputManagerCompat();

        private static ChartManager CreateTestChart(double noteTimeMs = 1000.0)
        {
            var chart = new ParsedChart("test.dtx") { Bpm = 120.0 };
            var (bar, tick) = ToPositionAt120Bpm(noteTimeMs);
            chart.AddNote(new Note(0, bar, tick, 0x11, "01"));
            chart.FinalizeChart();
            return new ChartManager(chart);
        }


        private static ChartManager CreateMultiNoteChart()
        {
            var chart = new ParsedChart("multi.dtx") { Bpm = 120.0 };
            chart.AddNote(new Note(0, 0, 96, 0x11, "01"));  // Lane 0 at 1000ms
            chart.AddNote(new Note(1, 0, 144, 0x12, "01")); // Lane 1 at 1500ms
            chart.AddNote(new Note(0, 1, 0, 0x13, "01"));   // Lane 0 at 2000ms
            chart.FinalizeChart();
            return new ChartManager(chart);
        }

        #region Constructor Null Tests

        [Fact]
        public void Constructor_NullInputManager_ShouldThrow()
        {
            var chartManager = CreateTestChart();
            Assert.Throws<ArgumentNullException>(() => new JudgementManager(null, chartManager));
        }

        [Fact]
        public void Constructor_NullChartManager_ShouldThrow()
        {
            var input = CreateMockInput();
            Assert.Throws<ArgumentNullException>(() => new JudgementManager(input, null));
        }

        #endregion

        #region GetNoteRuntimeData Tests

        [Fact]
        public void GetNoteRuntimeData_ExistingNote_ShouldReturnData()
        {
            var input = CreateMockInput();
            var chartManager = CreateTestChart();
            var manager = new JudgementManager(input, chartManager);

            // The note should have ID 0 (first note)
            var data = manager.GetNoteRuntimeData(0);
            Assert.NotNull(data);
        }

        [Fact]
        public void GetNoteRuntimeData_NonExistentNote_ShouldReturnNull()
        {
            var input = CreateMockInput();
            var chartManager = CreateTestChart();
            var manager = new JudgementManager(input, chartManager);

            var data = manager.GetNoteRuntimeData(9999);
            Assert.Null(data);
        }

        #endregion

        #region GetJudgementCount Tests

        [Fact]
        public void GetJudgementCount_BeforeAnyHits_ShouldReturnZero()
        {
            var input = CreateMockInput();
            var chartManager = CreateTestChart();
            var manager = new JudgementManager(input, chartManager);

            Assert.Equal(0, manager.GetJudgementCount(JudgementType.Perfect));
            Assert.Equal(0, manager.GetJudgementCount(JudgementType.Miss));
        }

        [Fact]
        public void GetJudgementCount_AfterPerfectHit_ShouldReturnOne()
        {
            var input = CreateMockInput();
            var chartManager = CreateTestChart(1000.0);
            var manager = new JudgementManager(input, chartManager);

            // Perfect hit at exactly 1000ms
            manager.EnqueueLaneHit(0);
            manager.Update(1000.0);

            Assert.Equal(1, manager.GetJudgementCount(JudgementType.Perfect));
        }

        [Fact]
        public void GetJudgementCount_AfterMiss_ShouldReturnOne()
        {
            var input = CreateMockInput();
            var chartManager = CreateTestChart(1000.0);
            var manager = new JudgementManager(input, chartManager);

            // Miss at 1250ms (250ms past note)
            manager.Update(1250.0);

            Assert.Equal(1, manager.GetJudgementCount(JudgementType.Miss));
        }

        #endregion

        #region GetStatistics Tests with Different Judgements

        [Fact]
        public void GetStatistics_AfterGoodHit_ShouldCountGood()
        {
            var input = CreateMockInput();
            var chartManager = CreateTestChart(1000.0);
            var manager = new JudgementManager(input, chartManager);

            // Good hit - 60ms delta (beyond Great window of 50ms)
            manager.EnqueueLaneHit(0);
            manager.Update(1060.0);

            var stats = manager.GetStatistics();
            Assert.Equal(1, stats.GoodCount);
        }

        [Fact]
        public void GetStatistics_AfterPoorHit_ShouldCountPoor()
        {
            var input = CreateMockInput();
            var chartManager = CreateTestChart(1000.0);
            var manager = new JudgementManager(input, chartManager);

            // Poor hit - 120ms delta (beyond Good window of 100ms)
            manager.EnqueueLaneHit(0);
            manager.Update(1120.0);

            var stats = manager.GetStatistics();
            Assert.Equal(1, stats.PoorCount);
        }

        [Fact]
        public void GetStatistics_TotalNotes_ShouldSumAllJudgements()
        {
            var input = CreateMockInput();
            var chart = new ParsedChart("test.dtx") { Bpm = 120.0 };
            chart.AddNote(new Note(0, 0, 96, 0x11, "01"));  // Lane 0 at 1000ms
            chart.AddNote(new Note(0, 1, 0, 0x11, "01"));   // Lane 0 at 2000ms
            chart.FinalizeChart();
            var chartManager = new ChartManager(chart);
            var manager = new JudgementManager(input, chartManager);

            // Hit first note
            manager.EnqueueLaneHit(0);
            manager.Update(1000.0);

            // Miss second note
            manager.Update(2250.0);

            var stats = manager.GetStatistics();
            Assert.Equal(2, stats.TotalNotes);
            Assert.Equal(1, stats.TotalHits);
        }

        #endregion

        #region IsActive Tests

        [Fact]
        public void IsActive_SetToFalse_ShouldPreventHitProcessing()
        {
            var input = CreateMockInput();
            var chartManager = CreateTestChart(1000.0);
            var manager = new JudgementManager(input, chartManager);
            manager.IsActive = false;

            int eventCount = 0;
            manager.JudgementMade += (s, e) => {
                if (e.Type != JudgementType.Miss)
                    eventCount++;
            };

            // Try to trigger a hit - should be ignored because IsActive is false
            manager.EnqueueLaneHit(0);
            manager.Update(1000.0);

            Assert.Equal(0, eventCount);
        }

        #endregion

        #region Dispose Tests

        [Fact]
        public void Dispose_ShouldNotThrow()
        {
            var input = CreateMockInput();
            var chartManager = CreateTestChart();
            var manager = new JudgementManager(input, chartManager);
            manager.Dispose();
        }

        [Fact]
        public void Dispose_CalledTwice_ShouldNotThrow()
        {
            var input = CreateMockInput();
            var chartManager = CreateTestChart();
            var manager = new JudgementManager(input, chartManager);
            manager.Dispose();
            manager.Dispose();
        }

        [Fact]
        public void Dispose_AfterDispose_UpdateShouldDoNothing()
        {
            var input = CreateMockInput();
            var chartManager = CreateTestChart();
            var manager = new JudgementManager(input, chartManager);

            int eventCount = 0;
            manager.JudgementMade += (s, e) => eventCount++;

            manager.Dispose();
            manager.Update(1000.0);

            Assert.Equal(0, eventCount);
        }

        #endregion

        #region JudgementStatistics Tests

        [Fact]
        public void JudgementStatistics_Accuracy_ZeroNotes_ShouldReturnZero()
        {
            var stats = new JudgementStatistics();
            Assert.Equal(0.0, stats.Accuracy);
        }

        [Fact]
        public void JudgementStatistics_Accuracy_AllPerfect_ShouldReturn100()
        {
            var stats = new JudgementStatistics
            {
                PerfectCount = 10,
                GreatCount = 0,
                GoodCount = 0,
                PoorCount = 0,
                MissCount = 0
            };
            Assert.Equal(100.0, stats.Accuracy);
        }

        [Fact]
        public void JudgementStatistics_Accuracy_HalfMiss_ShouldReturn50()
        {
            var stats = new JudgementStatistics
            {
                PerfectCount = 5,
                MissCount = 5
            };
            Assert.Equal(50.0, stats.Accuracy);
        }

        [Fact]
        public void JudgementStatistics_TotalNotes_ShouldSumAll()
        {
            var stats = new JudgementStatistics
            {
                PerfectCount = 1,
                GreatCount = 2,
                GoodCount = 3,
                PoorCount = 4,
                MissCount = 5
            };
            Assert.Equal(15, stats.TotalNotes);
        }

        [Fact]
        public void JudgementStatistics_TotalHits_ShouldExcludeMiss()
        {
            var stats = new JudgementStatistics
            {
                PerfectCount = 1,
                GreatCount = 2,
                GoodCount = 3,
                PoorCount = 4,
                MissCount = 5
            };
            Assert.Equal(10, stats.TotalHits);
        }

        #endregion
    }
}
