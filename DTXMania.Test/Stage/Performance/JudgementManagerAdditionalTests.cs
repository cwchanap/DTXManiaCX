using System;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Stage.Performance;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Test.Helpers;
using Xunit;

namespace DTXMania.Test.Stage.Performance
{
    /// <summary>
    /// Additional tests for JudgementManager covering uncovered paths
    /// </summary>
    public class JudgementManagerAdditionalTests
    {
        private static MockInputManagerCompat CreateMockInput() => new MockInputManagerCompat();

        private static ChartManager CreateTestChart(double noteTimeMs = 1000.0)
        {
            var chart = new ParsedChart("test.dtx") { Bpm = 120.0 };
            var tick = (int)((noteTimeMs / 2000.0) * 192);
            chart.AddNote(new Note(0, 0, tick, 0x11, "01"));
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

            Assert.Equal(0, manager.GetJudgementCount(JudgementType.Just));
            Assert.Equal(0, manager.GetJudgementCount(JudgementType.Miss));
        }

        [Fact]
        public void GetJudgementCount_AfterJustHit_ShouldReturnOne()
        {
            var input = CreateMockInput();
            var chartManager = CreateTestChart(1000.0);
            var manager = new JudgementManager(input, chartManager);

            // Just hit at exactly 1000ms
            manager.TestTriggerLaneHit(0);
            manager.Update(1000.0);

            Assert.Equal(1, manager.GetJudgementCount(JudgementType.Just));
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
            manager.TestTriggerLaneHit(0);
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
            manager.TestTriggerLaneHit(0);
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
            manager.TestTriggerLaneHit(0);
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
            manager.TestTriggerLaneHit(0);
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
        public void JudgementStatistics_Accuracy_AllJust_ShouldReturn100()
        {
            var stats = new JudgementStatistics
            {
                JustCount = 10,
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
                JustCount = 5,
                MissCount = 5
            };
            Assert.Equal(50.0, stats.Accuracy);
        }

        [Fact]
        public void JudgementStatistics_TotalNotes_ShouldSumAll()
        {
            var stats = new JudgementStatistics
            {
                JustCount = 1,
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
                JustCount = 1,
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
