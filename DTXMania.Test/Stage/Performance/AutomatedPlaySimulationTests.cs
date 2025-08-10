using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DTX.Song.Components;
using DTX.Stage;
using DTX.Stage.Performance;
using DTXMania.Game.Lib.Song.Entities;
using Xunit;
using Xunit.Abstractions;

namespace DTXMania.Test.Stage.Performance
{
    /// <summary>
    /// Comprehensive automated play simulation tests for QA and regression testing.
    /// Tests perfect play scenarios and random input variance scenarios.
    /// </summary>
    public class AutomatedPlaySimulationTests
    {
        private readonly ITestOutputHelper _output;

        public AutomatedPlaySimulationTests(ITestOutputHelper output)
        {
            _output = output;
        }

        #region Perfect Play Simulation Tests

        [Fact]
        public async Task AutomatedPlay_PerfectTiming_AllNotesJust_ExpectFullScoreAndLife100()
        {
            // Arrange - Create a test chart with multiple notes across all lanes
            var testChart = CreateTestChart(totalNotes: 100);
            var scoreManager = new ScoreManager(testChart.Count);
            var gaugeManager = new GaugeManager(startingLife: 50.0f);
            
            var simulationResults = new SimulationResults();
            var random = new Random(42); // Fixed seed for reproducible tests

            _output.WriteLine($"Starting perfect play simulation with {testChart.Count} notes");
            _output.WriteLine($"Initial conditions: Score=0, Life={gaugeManager.CurrentLife}%");

            // Act - Simulate perfect play (all hits within Just timing window)
            foreach (var note in testChart.OrderBy(n => n.TimeMs))
            {
                // Perfect timing (0ms delta) - should always result in Just judgement
                var judgementEvent = new JudgementEvent(
                    noteRef: note.Id,
                    lane: note.LaneIndex,
                    deltaMs: 0.0, // Perfect timing
                    type: JudgementType.Just
                );

                // Process judgement in both managers
                scoreManager.ProcessJudgement(judgementEvent);
                gaugeManager.ProcessJudgement(judgementEvent);

                // Track results
                simulationResults.JudgementCounts[JudgementType.Just]++;
                simulationResults.TotalScore = scoreManager.CurrentScore;
                simulationResults.FinalLife = gaugeManager.CurrentLife;
                simulationResults.ProcessedNotes++;

                // Verify no failure occurred during perfect play
                Assert.False(gaugeManager.HasFailed, 
                    $"Player should never fail during perfect play. Failed at note {simulationResults.ProcessedNotes}");
            }

            // Assert - Verify perfect play results
            var scoreStats = scoreManager.GetStatistics();
            var gaugeStats = gaugeManager.GetStatistics();

            _output.WriteLine("\n=== PERFECT PLAY SIMULATION RESULTS ===");
            _output.WriteLine($"Total Notes Processed: {simulationResults.ProcessedNotes}");
            _output.WriteLine($"Just Hits: {simulationResults.JudgementCounts[JudgementType.Just]}");
            _output.WriteLine($"Final Score: {simulationResults.TotalScore:N0}");
            _output.WriteLine($"Score Percentage: {scoreStats.ScorePercentage:F2}%");
            _output.WriteLine($"Final Life: {simulationResults.FinalLife:F1}%");
            _output.WriteLine($"Player Failed: {gaugeStats.HasFailed}");

            // Verify all notes were hit with Just timing
            Assert.Equal(testChart.Count, simulationResults.JudgementCounts[JudgementType.Just]);
            Assert.Equal(0, simulationResults.JudgementCounts[JudgementType.Great]);
            Assert.Equal(0, simulationResults.JudgementCounts[JudgementType.Good]);
            Assert.Equal(0, simulationResults.JudgementCounts[JudgementType.Poor]);
            Assert.Equal(0, simulationResults.JudgementCounts[JudgementType.Miss]);

            // Verify perfect score achieved
            Assert.Equal(scoreManager.TheoreticalMaxScore, simulationResults.TotalScore);
            Assert.True(scoreStats.ScorePercentage >= 99.9, 
                $"Perfect play should achieve nearly 100% score. Got: {scoreStats.ScorePercentage:F2}%");

            // Verify life is at 100% (or very close due to starting at 50% + perfect play bonuses)
            Assert.True(simulationResults.FinalLife >= 100.0f, 
                $"Perfect play should result in 100% life. Got: {simulationResults.FinalLife:F1}%");
            
            // Verify no failure occurred
            Assert.False(gaugeStats.HasFailed, "Player should never fail during perfect play");

            _output.WriteLine("✓ Perfect play simulation passed all assertions");
        }

        [Theory]
        [InlineData(50, 25)] // Small chart
        [InlineData(200, 25)] // Medium chart  
        [InlineData(500, 25)] // Large chart
        public async Task AutomatedPlay_PerfectTiming_VariousChartSizes_ExpectConsistentResults(
            int totalNotes, int expectedJustWindow)
        {
            // Arrange
            var testChart = CreateTestChart(totalNotes);
            var scoreManager = new ScoreManager(testChart.Count);
            var gaugeManager = new GaugeManager();

            _output.WriteLine($"Testing chart with {totalNotes} notes (Just window: ±{expectedJustWindow}ms)");

            // Act - Perfect play simulation
            foreach (var note in testChart.OrderBy(n => n.TimeMs))
            {
                var judgementEvent = new JudgementEvent(note.Id, note.LaneIndex, 0.0, JudgementType.Just);
                scoreManager.ProcessJudgement(judgementEvent);
                gaugeManager.ProcessJudgement(judgementEvent);
            }

            // Assert
            var scoreStats = scoreManager.GetStatistics();
            Assert.Equal(scoreManager.TheoreticalMaxScore, scoreManager.CurrentScore);
            Assert.True(scoreStats.ScorePercentage >= 99.9);
            Assert.True(gaugeManager.CurrentLife >= 100.0f);
            Assert.False(gaugeManager.HasFailed);

            _output.WriteLine($"✓ Chart size {totalNotes}: Score={scoreManager.CurrentScore:N0}, Life={gaugeManager.CurrentLife:F1}%");
        }

        #endregion

        #region Random Input Variance Tests

        [Fact]
        public async Task AutomatedPlay_RandomInputVariance_Plus80msVariance_VerifyNoCrashAndReasonableStats()
        {
            // Arrange - Create test chart and managers
            var testChart = CreateTestChart(totalNotes: 200);
            var scoreManager = new ScoreManager(testChart.Count);
            var gaugeManager = new GaugeManager();
            
            var simulationResults = new SimulationResults();
            var random = new Random(123); // Fixed seed for reproducible tests
            const double maxVarianceMs = 80.0; // ±80ms as specified

            _output.WriteLine($"Starting random input variance simulation");
            _output.WriteLine($"Chart size: {testChart.Count} notes");
            _output.WriteLine($"Input variance: ±{maxVarianceMs}ms");
            _output.WriteLine($"Initial life: {gaugeManager.CurrentLife}%");

            // Act - Simulate gameplay with random timing variance
            try
            {
                foreach (var note in testChart.OrderBy(n => n.TimeMs))
                {
                    // Generate random timing delta within ±80ms
                    var timingDelta = (random.NextDouble() - 0.5) * 2.0 * maxVarianceMs; // Range: -80 to +80
                    
                    // Create judgement event with calculated timing
                    var judgementEvent = new JudgementEvent(
                        noteRef: note.Id,
                        lane: note.LaneIndex,
                        deltaMs: timingDelta
                    );

                    // Process judgement (timing determines judgement type automatically)
                    scoreManager.ProcessJudgement(judgementEvent);
                    gaugeManager.ProcessJudgement(judgementEvent);

                    // Track statistics
                    simulationResults.JudgementCounts[judgementEvent.Type]++;
                    simulationResults.TotalScore = scoreManager.CurrentScore;
                    simulationResults.FinalLife = gaugeManager.CurrentLife;
                    simulationResults.ProcessedNotes++;
                    simulationResults.TotalTimingDeviation += Math.Abs(timingDelta);

                    // Log extreme timing cases
                    if (Math.Abs(timingDelta) > 70.0)
                    {
                        _output.WriteLine($"Extreme timing: {timingDelta:+0.0;-0.0;0.0}ms → {judgementEvent.Type}");
                    }
                }

                simulationResults.AverageTimingDeviation = simulationResults.TotalTimingDeviation / simulationResults.ProcessedNotes;
            }
            catch (Exception ex)
            {
                Assert.True(false, $"Simulation crashed with random input variance: {ex.Message}\n{ex.StackTrace}");
            }

            // Assert - Verify no crash and reasonable statistics
            var scoreStats = scoreManager.GetStatistics();
            var gaugeStats = gaugeManager.GetStatistics();

            _output.WriteLine("\n=== RANDOM INPUT VARIANCE RESULTS ===");
            _output.WriteLine($"Notes Processed: {simulationResults.ProcessedNotes}");
            _output.WriteLine($"Average Timing Deviation: ±{simulationResults.AverageTimingDeviation:F1}ms");
            _output.WriteLine($"Judgement Distribution:");
            _output.WriteLine($"  Just:  {simulationResults.JudgementCounts[JudgementType.Just]} ({(double)simulationResults.JudgementCounts[JudgementType.Just]/simulationResults.ProcessedNotes*100:F1}%)");
            _output.WriteLine($"  Great: {simulationResults.JudgementCounts[JudgementType.Great]} ({(double)simulationResults.JudgementCounts[JudgementType.Great]/simulationResults.ProcessedNotes*100:F1}%)");
            _output.WriteLine($"  Good:  {simulationResults.JudgementCounts[JudgementType.Good]} ({(double)simulationResults.JudgementCounts[JudgementType.Good]/simulationResults.ProcessedNotes*100:F1}%)");
            _output.WriteLine($"  Poor:  {simulationResults.JudgementCounts[JudgementType.Poor]} ({(double)simulationResults.JudgementCounts[JudgementType.Poor]/simulationResults.ProcessedNotes*100:F1}%)");
            _output.WriteLine($"  Miss:  {simulationResults.JudgementCounts[JudgementType.Miss]} ({(double)simulationResults.JudgementCounts[JudgementType.Miss]/simulationResults.ProcessedNotes*100:F1}%)");
            _output.WriteLine($"Final Score: {simulationResults.TotalScore:N0} ({scoreStats.ScorePercentage:F1}%)");
            _output.WriteLine($"Final Life: {simulationResults.FinalLife:F1}%");
            _output.WriteLine($"Player Failed: {gaugeStats.HasFailed}");

            // Verify simulation completed without crash
            Assert.Equal(testChart.Count, simulationResults.ProcessedNotes);

            // Verify reasonable score distribution (with ±80ms variance, we should see mixed results)
            Assert.True(simulationResults.TotalScore > 0, "Should have achieved some score");
            Assert.True(scoreStats.ScorePercentage >= 10.0, 
                $"With ±80ms variance, should achieve at least 10% score. Got: {scoreStats.ScorePercentage:F1}%");
            Assert.True(scoreStats.ScorePercentage <= 90.0, 
                $"With ±80ms variance, shouldn't achieve more than 90% score. Got: {scoreStats.ScorePercentage:F1}%");

            // Verify reasonable judgement distribution
            var totalHits = simulationResults.JudgementCounts.Values.Sum();
            Assert.Equal(testChart.Count, totalHits);

            // With ±80ms variance, we should see variety in judgements
            var judgementTypes = simulationResults.JudgementCounts.Where(kvp => kvp.Value > 0).Count();
            Assert.True(judgementTypes >= 3, $"Expected variety in judgements with ±80ms variance. Got {judgementTypes} different types");

            // Verify average timing deviation is reasonable
            Assert.True(simulationResults.AverageTimingDeviation >= 20.0 && simulationResults.AverageTimingDeviation <= 60.0,
                $"Average timing deviation should be 20-60ms with ±80ms variance. Got: {simulationResults.AverageTimingDeviation:F1}ms");

            _output.WriteLine("✓ Random variance simulation passed all assertions");
        }

        [Theory]
        [InlineData(20.0)] // Small variance
        [InlineData(50.0)] // Medium variance
        [InlineData(100.0)] // Large variance
        public async Task AutomatedPlay_VariousInputVariance_VerifyStabilityAndStats(double maxVarianceMs)
        {
            // Arrange
            var testChart = CreateTestChart(totalNotes: 100);
            var scoreManager = new ScoreManager(testChart.Count);
            var gaugeManager = new GaugeManager();
            var random = new Random(456);

            _output.WriteLine($"Testing input variance: ±{maxVarianceMs}ms");

            // Act
            var results = new SimulationResults();
            
            foreach (var note in testChart.OrderBy(n => n.TimeMs))
            {
                var timingDelta = (random.NextDouble() - 0.5) * 2.0 * maxVarianceMs;
                var judgementEvent = new JudgementEvent(note.Id, note.LaneIndex, timingDelta);
                
                scoreManager.ProcessJudgement(judgementEvent);
                gaugeManager.ProcessJudgement(judgementEvent);
                
                results.JudgementCounts[judgementEvent.Type]++;
                results.ProcessedNotes++;
            }

            // Assert - Basic stability checks
            Assert.Equal(testChart.Count, results.ProcessedNotes);
            Assert.True(scoreManager.CurrentScore >= 0);
            Assert.True(gaugeManager.CurrentLife >= 0.0f);

            // Variance-specific expectations
            var scorePercentage = scoreManager.GetStatistics().ScorePercentage;
            if (maxVarianceMs <= 25.0) // Within Just window
            {
                Assert.True(scorePercentage >= 70.0, $"Small variance should yield good scores: {scorePercentage:F1}%");
            }
            else if (maxVarianceMs <= 100.0) // Within Good window
            {
                Assert.True(scorePercentage >= 20.0, $"Medium variance should yield decent scores: {scorePercentage:F1}%");
            }

            _output.WriteLine($"✓ Variance ±{maxVarianceMs}ms: Score={scorePercentage:F1}%, Life={gaugeManager.CurrentLife:F1}%");
        }

        #endregion

        #region Stress and Edge Case Tests

        [Fact]
        public async Task AutomatedPlay_ExtremeInputVariance_Plus200ms_VerifyAllMisses()
        {
            // Arrange - Test extreme variance beyond Miss threshold
            var testChart = CreateTestChart(totalNotes: 50);
            var scoreManager = new ScoreManager(testChart.Count);
            var gaugeManager = new GaugeManager();
            
            _output.WriteLine("Testing extreme input variance (±200ms - all misses expected)");

            // Act - All inputs way outside timing windows
            var results = new SimulationResults();
            foreach (var note in testChart)
            {
                var judgementEvent = new JudgementEvent(note.Id, note.LaneIndex, 250.0); // Way too late
                scoreManager.ProcessJudgement(judgementEvent);
                gaugeManager.ProcessJudgement(judgementEvent);
                results.JudgementCounts[judgementEvent.Type]++;
            }

            // Assert
            Assert.Equal(testChart.Count, results.JudgementCounts[JudgementType.Miss]);
            Assert.Equal(0, scoreManager.CurrentScore);
            Assert.True(gaugeManager.HasFailed, "Should fail with all misses");
            
            _output.WriteLine($"✓ Extreme variance test: All {testChart.Count} notes missed as expected");
        }

        [Fact]
        public async Task AutomatedPlay_MixedTimingPrecision_VerifyReasonableDistribution()
        {
            // Arrange - Test realistic mixed timing scenario
            var testChart = CreateTestChart(totalNotes: 300);
            var scoreManager = new ScoreManager(testChart.Count);
            var gaugeManager = new GaugeManager();
            var random = new Random(789);

            _output.WriteLine("Testing mixed timing precision simulation");

            // Act - Simulate more realistic play with weighted timing distribution
            var results = new SimulationResults();
            foreach (var note in testChart)
            {
                double timingDelta;
                var roll = random.NextDouble();

                // Weighted distribution: some perfect, some good, some poor
                if (roll < 0.2) // 20% perfect timing
                    timingDelta = (random.NextDouble() - 0.5) * 2.0 * TimingConstants.JustWindowMs;
                else if (roll < 0.5) // 30% great timing
                    timingDelta = (random.NextDouble() - 0.5) * 2.0 * TimingConstants.GreatWindowMs;
                else if (roll < 0.8) // 30% good timing
                    timingDelta = (random.NextDouble() - 0.5) * 2.0 * TimingConstants.GoodWindowMs;
                else // 20% poor/miss timing
                    timingDelta = (random.NextDouble() - 0.5) * 2.0 * TimingConstants.PoorWindowMs * 1.5;

                var judgementEvent = new JudgementEvent(note.Id, note.LaneIndex, timingDelta);
                scoreManager.ProcessJudgement(judgementEvent);
                gaugeManager.ProcessJudgement(judgementEvent);
                results.JudgementCounts[judgementEvent.Type]++;
            }

            // Assert
            var scoreStats = scoreManager.GetStatistics();
            _output.WriteLine($"Mixed timing results: Score={scoreStats.ScorePercentage:F1}%, Life={gaugeManager.CurrentLife:F1}%");
            
            // Should see reasonable distribution across all judgement types
            Assert.True(results.JudgementCounts[JudgementType.Just] > 0, "Should have some Just hits");
            Assert.True(results.JudgementCounts.Count(kvp => kvp.Value > 0) >= 4, "Should have variety in judgements");
            Assert.True(scoreStats.ScorePercentage >= 30.0 && scoreStats.ScorePercentage <= 80.0, 
                $"Mixed play should yield 30-80% score: {scoreStats.ScorePercentage:F1}%");

            _output.WriteLine("✓ Mixed timing precision test passed");
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Creates a test chart with specified number of notes distributed across all lanes
        /// </summary>
        private List<Note> CreateTestChart(int totalNotes)
        {
            var notes = new List<Note>();
            var random = new Random(999); // Fixed seed for reproducible charts
            const double bpm = 120.0; // Standard BPM for timing calculations

            for (int i = 0; i < totalNotes; i++)
            {
                var note = new Note
                {
                    Id = i,
                    LaneIndex = i % 9, // Cycle through all 9 lanes
                    Bar = i / 8, // Distribute across bars
                    Tick = (i % 8) * 24, // Distribute ticks within bars
                    Channel = 11 + (i % 9), // DTX channels 11-19
                    Value = "01"
                };

                // Calculate timing
                note.CalculateTimeMs(bpm);
                notes.Add(note);
            }

            return notes.OrderBy(n => n.TimeMs).ToList();
        }

        /// <summary>
        /// Results tracking for simulation runs
        /// </summary>
        private class SimulationResults
        {
            public Dictionary<JudgementType, int> JudgementCounts { get; } = new()
            {
                { JudgementType.Just, 0 },
                { JudgementType.Great, 0 },
                { JudgementType.Good, 0 },
                { JudgementType.Poor, 0 },
                { JudgementType.Miss, 0 }
            };

            public int ProcessedNotes { get; set; }
            public int TotalScore { get; set; }
            public float FinalLife { get; set; }
            public double TotalTimingDeviation { get; set; }
            public double AverageTimingDeviation { get; set; }
        }

        #endregion
    }
}
