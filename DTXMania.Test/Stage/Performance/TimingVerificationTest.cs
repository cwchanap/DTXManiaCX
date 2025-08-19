using System;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Stage.Performance;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Test.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace DTXMania.Test.Stage.Performance
{
    public class TimingVerificationTest
    {
        private readonly ITestOutputHelper _output;

        public TimingVerificationTest(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void VerifyNoteTimingCalculation()
        {
            // Test timing calculation directly
            var note = new Note(0, 0, 48, 0x11, "01"); // 48 ticks, should be 500ms at 120 BPM
            
            _output.WriteLine($"Before timing calculation: TimeMs = {note.TimeMs}");
            
            note.CalculateTimeMs(120.0);
            
            _output.WriteLine($"After timing calculation: TimeMs = {note.TimeMs}");
            
            // 48 ticks = 48/192 = 0.25 measures = 0.25 * (60000/120) * 4 = 0.25 * 500 * 4 = 500ms
            Assert.Equal(500.0, note.TimeMs, 1);
        }

        [Fact]
        public void VerifyChartManagerProcessesTimingCorrectly()
        {
            // Create chart and verify timing is processed
            var parsedChart = new ParsedChart("timing-test.dtx")
            {
                Bpm = 120.0
            };

            var note = new Note(0, 0, 48, 0x11, "01");
            _output.WriteLine($"Raw note before adding: Bar={note.Bar}, Tick={note.Tick}, TimeMs={note.TimeMs}");
            
            parsedChart.AddNote(note);
            
            _output.WriteLine($"Note after adding to chart: Bar={note.Bar}, Tick={note.Tick}, TimeMs={note.TimeMs}");
            
            parsedChart.FinalizeChart();
            
            _output.WriteLine($"Note after FinalizeChart: Bar={note.Bar}, Tick={note.Tick}, TimeMs={note.TimeMs}");
            
            var chartManager = new ChartManager(parsedChart);
            
            _output.WriteLine($"ChartManager notes count: {chartManager.AllNotes.Count}");
            if (chartManager.AllNotes.Count > 0)
            {
                var chartNote = chartManager.AllNotes[0];
                _output.WriteLine($"First note in chartManager: ID={chartNote.Id}, TimeMs={chartNote.TimeMs}, Lane={chartNote.LaneIndex}");
            }
            
            Assert.Equal(1, chartManager.AllNotes.Count);
            Assert.Equal(500.0, chartManager.AllNotes[0].TimeMs, 1);
        }

        [Fact]
        public void VerifyMissDetectionWithExactTiming()
        {
            // Use the exact same chart creation as the working test
            var mockInputManager = new MockInputManagerCompat();
            var chartManager = CreateExactTestChart();
            var judgementManager = new JudgementManager(mockInputManager, chartManager);

            var events = new System.Collections.Generic.List<JudgementEvent>();
            judgementManager.JudgementMade += (sender, e) => 
            {
                events.Add(e);
                _output.WriteLine($"EVENT: {e.Type} - Note {e.NoteRef}, Lane {e.Lane}, Delta {e.DeltaMs:F1}ms");
            };

            _output.WriteLine($"Chart has {chartManager.AllNotes.Count} notes");
            foreach (var note in chartManager.AllNotes)
            {
                _output.WriteLine($"  Note {note.Id}: TimeMs={note.TimeMs:F1}, Lane={note.LaneIndex}");
            }

            // Test the exact scenario from the working test: note at 1000ms, update at 1250ms
            _output.WriteLine("\n--- Calling Update(1250.0) ---");
            judgementManager.Update(1250.0);

            _output.WriteLine($"Events generated: {events.Count}");
            
            Assert.True(events.Count > 0, "Should generate miss event");
            Assert.Equal(JudgementType.Miss, events[0].Type);
        }

        private static ChartManager CreateExactTestChart()
        {
            // Replicate the exact chart from JudgementManagerTests.CreateTestChartManager()
            var parsedChart = new ParsedChart("test-chart.dtx")
            {
                Bpm = 120.0
            };

            // Add a note at 1000ms (96 ticks) - matching the working test
            parsedChart.AddNote(new Note(0, 0, 96, 0x11, "01"));

            parsedChart.FinalizeChart();
            return new ChartManager(parsedChart);
        }
    }
}