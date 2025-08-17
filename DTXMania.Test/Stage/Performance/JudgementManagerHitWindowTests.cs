using System;
using System.Collections.Generic;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Stage.Performance;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Test.Helpers;
using Microsoft.Xna.Framework.Input;
using Moq;
using Xunit;

namespace DTXMania.Test.Stage.Performance
{
    /// <summary>
    /// Unit tests for JudgementManager hit window boundaries.
    /// Tests that the JudgementManager correctly processes hits at the boundaries of timing windows.
    /// </summary>
    public class JudgementManagerHitWindowTests
    {
        [Theory]
        [InlineData(0.0, JudgementType.Just)]           // Perfect timing
        [InlineData(25.0, JudgementType.Just)]          // Just boundary (±25ms)
        [InlineData(-25.0, JudgementType.Just)]         // Just boundary early
        [InlineData(25.1, JudgementType.Great)]         // Just over Just boundary
        [InlineData(-25.1, JudgementType.Great)]        // Just over Just boundary early
        [InlineData(50.0, JudgementType.Great)]         // Great boundary (±50ms)
        [InlineData(-50.0, JudgementType.Great)]        // Great boundary early
        [InlineData(50.1, JudgementType.Good)]          // Just over Great boundary
        [InlineData(-50.1, JudgementType.Good)]         // Just over Great boundary early
        [InlineData(100.0, JudgementType.Good)]         // Good boundary (±100ms)
        [InlineData(-100.0, JudgementType.Good)]        // Good boundary early
        [InlineData(100.1, JudgementType.Poor)]         // Just over Good boundary
        [InlineData(-100.1, JudgementType.Poor)]        // Just over Good boundary early
        [InlineData(150.0, JudgementType.Poor)]         // Poor boundary (±150ms)
        [InlineData(-150.0, JudgementType.Poor)]        // Poor boundary early
        [InlineData(150.1, JudgementType.Miss)]         // Just over Poor boundary
        [InlineData(-150.1, JudgementType.Miss)]        // Just over Poor boundary early
        [InlineData(200.0, JudgementType.Miss)]         // Miss threshold
        [InlineData(-200.0, JudgementType.Miss)]        // Miss threshold early
        public void HitWindowBoundaries_VariousTimings_ReturnsCorrectJudgement(double deltaMs, JudgementType expectedJudgement)
        {
            // Arrange
            var mockInputManager = new MockInputManagerCompat();
            var chartManager = CreateSingleNoteChartManager(1000.0); // Note at 1000ms
            var judgementManager = new JudgementManager(mockInputManager, chartManager);

            JudgementEvent? capturedEvent = null;
            judgementManager.JudgementMade += (sender, e) => capturedEvent = e;

            // Act - Trigger lane hit at note time + deltaMs
            double hitTime = 1000.0 + deltaMs;
            
            // Trigger a hit on lane 0 directly using test method
            judgementManager.TestTriggerLaneHit(0);
            
            judgementManager.Update(hitTime);

            // Assert
            Assert.NotNull(capturedEvent);
            Assert.Equal(expectedJudgement, capturedEvent.Type);
            Assert.Equal(deltaMs, capturedEvent.DeltaMs, 0.1); // Allow small floating point tolerance
            Assert.Equal(0, capturedEvent.Lane); // Lane A = 0
        }

        [Fact]
        public void HitWindowBoundaries_ExactlyAtJustWindow_ReturnsJust()
        {
            // Arrange
            var mockInputManager = new MockInputManagerCompat();
            var chartManager = CreateSingleNoteChartManager(1000.0);
            var judgementManager = new JudgementManager(mockInputManager, chartManager);

            JudgementEvent? capturedEvent = null;
            judgementManager.JudgementMade += (sender, e) => capturedEvent = e;

            // Act - Hit exactly at Just window boundary (25ms)
            judgementManager.TestTriggerLaneHit(0); // Direct test trigger for lane 0
            judgementManager.Update(1025.0);

            // Assert
            Assert.NotNull(capturedEvent);
            Assert.Equal(JudgementType.Just, capturedEvent.Type);
            Assert.Equal(25.0, capturedEvent.DeltaMs, 0.1);
        }

        [Fact]
        public void HitWindowBoundaries_ExactlyAtGreatWindow_ReturnsGreat()
        {
            // Arrange
            var mockInputManager = new MockInputManagerCompat();
            var chartManager = CreateSingleNoteChartManager(1000.0);
            var judgementManager = new JudgementManager(mockInputManager, chartManager);

            JudgementEvent? capturedEvent = null;
            judgementManager.JudgementMade += (sender, e) => capturedEvent = e;

            // Act - Hit exactly at Great window boundary (50ms)
            judgementManager.TestTriggerLaneHit(0); // Direct test trigger for lane 0
            judgementManager.Update(1050.0);

            // Assert
            Assert.NotNull(capturedEvent);
            Assert.Equal(JudgementType.Great, capturedEvent.Type);
            Assert.Equal(50.0, capturedEvent.DeltaMs, 0.1);
        }

        [Fact]
        public void HitWindowBoundaries_BeyondDetectionWindow_NoHit()
        {
            // Arrange
            var mockInputManager = new MockInputManagerCompat();
            var chartManager = CreateSingleNoteChartManager(1000.0);
            var judgementManager = new JudgementManager(mockInputManager, chartManager);

            JudgementEvent? capturedEvent = null;
            judgementManager.JudgementMade += (sender, e) => capturedEvent = e;

            // Act - Hit beyond detection window (±200ms as per JudgementManager spec)
            judgementManager.TestTriggerLaneHit(0); // Direct test trigger for lane 0
            judgementManager.Update(1251.0); // 251ms late, beyond ±200ms detection window

            // Assert - Note should be auto-marked as Miss due to being beyond hit window
            Assert.NotNull(capturedEvent);
            Assert.Equal(JudgementType.Miss, capturedEvent.Type);
        }

        [Fact]
        public void HitWindowBoundaries_WithinDetectionAndWithinGood_ReturnsGood()
        {
            // Arrange
            var mockInputManager = new MockInputManagerCompat();
            var chartManager = CreateSingleNoteChartManager(1000.0);
            var judgementManager = new JudgementManager(mockInputManager, chartManager);

            JudgementEvent? capturedEvent = null;
            judgementManager.JudgementMade += (sender, e) => capturedEvent = e;

            // Act - Hit within detection window (200ms) and within Good threshold (≤100ms)
            judgementManager.TestTriggerLaneHit(0); // Direct test trigger for lane 0
            judgementManager.Update(1089.0); // 89ms late, within detection window

            // Assert
            Assert.NotNull(capturedEvent);
            Assert.Equal(JudgementType.Good, capturedEvent.Type);
            Assert.Equal(89.0, capturedEvent.DeltaMs, 0.1);
        }

        [Fact]
        public void HitWindowBoundaries_MultipleNotesInWindow_HitsNearest()
        {
            // Arrange
            var mockInputManager = new MockInputManagerCompat();
            var chartManager = CreateMultipleNotesChartManager();
            var judgementManager = new JudgementManager(mockInputManager, chartManager);

            JudgementEvent? capturedEvent = null;
            judgementManager.JudgementMade += (sender, e) => capturedEvent = e;

            // Act - Hit between two notes, should hit the nearer one
            judgementManager.TestTriggerLaneHit(0); // Direct test trigger for lane 0
            judgementManager.Update(1025.0); // 25ms after first note, 25ms before second note

            // Assert - Should hit the first note (at 1000ms)
            Assert.NotNull(capturedEvent);
            Assert.Equal(25.0, capturedEvent.DeltaMs, 0.1);
            Assert.Equal(JudgementType.Just, capturedEvent.Type);
        }

        [Fact]
        public void HitWindowBoundaries_EarlyHitNegativeDelta_CorrectJudgement()
        {
            // Arrange
            var mockInputManager = new MockInputManagerCompat();
            var chartManager = CreateSingleNoteChartManager(1000.0);
            var judgementManager = new JudgementManager(mockInputManager, chartManager);

            JudgementEvent? capturedEvent = null;
            judgementManager.JudgementMade += (sender, e) => capturedEvent = e;

            // Act - Hit early (negative delta)
            judgementManager.TestTriggerLaneHit(0); // Direct test trigger for lane 0
            judgementManager.Update(960.0); // 40ms early

            // Assert
            Assert.NotNull(capturedEvent);
            Assert.Equal(-40.0, capturedEvent.DeltaMs, 0.1);
            Assert.Equal(JudgementType.Great, capturedEvent.Type);
        }

        [Theory]
        [InlineData(24.9, JudgementType.Just)]          // Just under Just boundary
        [InlineData(25.1, JudgementType.Great)]         // Just over Just boundary
        [InlineData(49.9, JudgementType.Great)]         // Just under Great boundary
        [InlineData(50.1, JudgementType.Good)]          // Just over Great boundary
        [InlineData(99.9, JudgementType.Good)]          // Just under Good boundary
        [InlineData(100.1, JudgementType.Poor)]         // Just over Good boundary
        [InlineData(149.9, JudgementType.Poor)]         // Just under Poor boundary
        [InlineData(150.1, JudgementType.Miss)]         // Just over Poor boundary
        public void HitWindowBoundaries_EdgeCases_CorrectJudgements(double deltaMs, JudgementType expectedJudgement)
        {
            // Arrange
            var mockInputManager = new MockInputManagerCompat();
            var chartManager = CreateSingleNoteChartManager(1000.0);
            var judgementManager = new JudgementManager(mockInputManager, chartManager);

            JudgementEvent? capturedEvent = null;
            judgementManager.JudgementMade += (sender, e) => capturedEvent = e;

            // Act
            judgementManager.TestTriggerLaneHit(0); // Direct test trigger for lane 0
            judgementManager.Update(1000.0 + deltaMs);

            // Assert
            Assert.NotNull(capturedEvent);
            Assert.Equal(expectedJudgement, capturedEvent.Type);
            Assert.Equal(deltaMs, capturedEvent.DeltaMs, 0.1);
        }

        #region Helper Methods

        private static ChartManager CreateSingleNoteChartManager(double noteTimeMs)
        {
            var parsedChart = new ParsedChart("single-note-test.dtx")
            {
                Bpm = 120.0
            };

            // Calculate tick position for the given time
            // At 120 BPM: 192 ticks = 2000ms (one measure), so tick = (timeMs / 2000.0) * 192
            int tickPosition = (int)((noteTimeMs / 2000.0) * 192);

            parsedChart.AddNote(new Note(0, 0, tickPosition, 0x11, "01")); // Lane 0
            parsedChart.FinalizeChart();
            
            return new ChartManager(parsedChart);
        }

        private static ChartManager CreateMultipleNotesChartManager()
        {
            var parsedChart = new ParsedChart("multiple-notes-test.dtx")
            {
                Bpm = 120.0
            };

            // Add two notes 50ms apart in the same lane
            // At 120 BPM: 192 ticks = 2000ms (one measure), so tick = (timeMs / 2000.0) * 192
            parsedChart.AddNote(new Note(0, 0, (int)((1000.0 / 2000.0) * 192), 0x11, "01"));      // At 1000ms
            parsedChart.AddNote(new Note(1, 0, (int)((1050.0 / 2000.0) * 192), 0x11, "01"));     // At 1050ms
            
            parsedChart.FinalizeChart();
            return new ChartManager(parsedChart);
        }

        #endregion
    }
}
