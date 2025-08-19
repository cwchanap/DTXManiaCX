using System;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Stage.Performance;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Test.Helpers;
using Xunit;

namespace DTXMania.Test.Stage.Performance
{
    /// <summary>
    /// Integration tests for missing note detection in PerformanceStage context.
    /// These tests verify that the JudgementManager correctly detects missed notes
    /// in a realistic gameplay scenario with timing and activation flow.
    /// </summary>
    public class PerformanceStageJudgementIntegrationTests
    {
        [Fact]
        public void JudgementManager_WithInactiveState_StillDetectsMissedNotes()
        {
            // Arrange - Create a realistic scenario where JudgementManager starts inactive
            var mockInputManager = new MockInputManagerCompat();
            var chartManager = CreateSimpleTestChart();
            var judgementManager = new JudgementManager(mockInputManager, chartManager);

            // Simulate PerformanceStage behavior: start with IsActive = false
            judgementManager.IsActive = false;

            JudgementEvent? missEvent = null;
            judgementManager.JudgementMade += (sender, e) => 
            {
                if (e.Type == JudgementType.Miss)
                    missEvent = e;
            };

            // Act - Update with time that should cause a miss (note at 1000ms, check at 1300ms = 300ms late)
            // This simulates time passing while the song isn't playing yet (ready countdown, etc.)
            judgementManager.Update(1300.0);

            // Assert - Miss should be detected even when IsActive = false
            Assert.NotNull(missEvent);
            Assert.Equal(JudgementType.Miss, missEvent.Type);
            Assert.Equal(0, missEvent.NoteRef); // First note
            Assert.Equal(0, missEvent.Lane); // Lane 0
            // Delta should be 300ms (1300 - 1000)
            Assert.InRange(missEvent.DeltaMs, 299.0, 301.0);
        }

        [Fact]
        public void JudgementManager_InputProcessingDisabled_WhenInactive()
        {
            // Arrange
            var mockInputManager = new MockInputManagerCompat();
            var chartManager = CreateSimpleTestChart();
            var judgementManager = new JudgementManager(mockInputManager, chartManager);

            // Set inactive to simulate ready countdown phase
            judgementManager.IsActive = false;

            int totalJudgements = 0;
            judgementManager.JudgementMade += (sender, e) => totalJudgements++;

            // Act - Simulate input while inactive (should be ignored)
            mockInputManager.TriggerLaneHit(0); // Hit lane 0
            judgementManager.Update(1000.0); // Perfect timing

            // Assert - Input should be ignored, no hit judgement
            Assert.Equal(0, totalJudgements);
        }

        [Fact]
        public void JudgementManager_InputProcessingEnabled_WhenActive()
        {
            // Arrange
            var mockInputManager = new MockInputManagerCompat();
            var chartManager = CreateSimpleTestChart();
            var judgementManager = new JudgementManager(mockInputManager, chartManager);

            // Set active to simulate song playing
            judgementManager.IsActive = true;

            JudgementEvent? hitEvent = null;
            judgementManager.JudgementMade += (sender, e) => 
            {
                if (e.Type != JudgementType.Miss)
                    hitEvent = e;
            };

            // Act - Simulate input while active (should be processed)
            mockInputManager.TriggerLaneHit(0); // Hit lane 0
            judgementManager.Update(1000.0); // Perfect timing

            // Assert - Input should be processed, creating a hit judgement
            Assert.NotNull(hitEvent);
            Assert.Equal(JudgementType.Just, hitEvent.Type); // Perfect timing
            Assert.Equal(0, hitEvent.NoteRef);
            Assert.Equal(0, hitEvent.Lane);
        }

        [Fact]
        public void JudgementManager_StateTransition_PreservesNoteTracking()
        {
            // Arrange - Simulate the PerformanceStage lifecycle
            var mockInputManager = new MockInputManagerCompat();
            var chartManager = CreateMultiNoteTestChart();
            var judgementManager = new JudgementManager(mockInputManager, chartManager);

            // Start inactive (ready countdown phase)
            judgementManager.IsActive = false;

            int missCount = 0;
            int hitCount = 0;
            judgementManager.JudgementMade += (sender, e) => 
            {
                if (e.Type == JudgementType.Miss) missCount++;
                else hitCount++;
            };

            // Act & Assert - Simulate full lifecycle

            // Phase 1: Ready countdown (inactive) - first note should be missed
            judgementManager.Update(1300.0); // 300ms after first note at 1000ms
            Assert.Equal(1, missCount); // First note missed
            Assert.Equal(0, hitCount);

            // Phase 2: Song starts (activate input processing)
            judgementManager.IsActive = true;

            // Phase 3: Player hits second note perfectly (note is in lane 1, not 0)
            mockInputManager.TriggerLaneHit(1);
            judgementManager.Update(1500.0); // Perfect timing for second note
            Assert.Equal(1, missCount); // Still one miss
            Assert.Equal(1, hitCount); // One successful hit

            // Phase 4: Third note times out (should be missed)
            judgementManager.Update(2300.0); // 300ms after third note at 2000ms
            Assert.Equal(2, missCount); // Now two misses
            Assert.Equal(1, hitCount); // Still one hit
        }

        /// <summary>
        /// Creates a simple chart with one note at 1000ms for basic testing
        /// </summary>
        private static ChartManager CreateSimpleTestChart()
        {
            var parsedChart = new ParsedChart("simple-test.dtx")
            {
                Bpm = 120.0
            };

            // Add a single note at 1000ms in lane 0
            parsedChart.AddNote(new Note(0, 0, 96, 0x11, "01")); // 96 ticks ≈ 1000ms at 120 BPM

            parsedChart.FinalizeChart();
            return new ChartManager(parsedChart);
        }

        /// <summary>
        /// Creates a chart with multiple notes for lifecycle testing
        /// </summary>
        private static ChartManager CreateMultiNoteTestChart()
        {
            var parsedChart = new ParsedChart("multi-note-test.dtx")
            {
                Bpm = 120.0
            };

            // Add notes at 1000ms, 1500ms, and 2000ms intervals
            parsedChart.AddNote(new Note(0, 0, 96, 0x11, "01"));   // ~1000ms
            parsedChart.AddNote(new Note(1, 0, 144, 0x11, "01"));  // ~1500ms
            parsedChart.AddNote(new Note(2, 0, 192, 0x11, "01"));  // ~2000ms

            parsedChart.FinalizeChart();
            return new ChartManager(parsedChart);
        }
    }
}