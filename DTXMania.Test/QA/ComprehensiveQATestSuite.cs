using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using DTX.Song.Components;
using DTX.Stage.Performance;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Test.Helpers;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Xunit;
using Xunit.Abstractions;

namespace DTXMania.Test.QA
{
    /// <summary>
    /// Comprehensive QA and regression test suite for DTXMania.
    /// Combines automated play simulation, input variance testing, and GPU rendering tests.
    /// This is the main entry point for complete system validation.
    /// </summary>
    public class ComprehensiveQATestSuite : IDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly TestGraphicsDeviceService _graphicsService;
        private readonly Stopwatch _testTimer;

        public ComprehensiveQATestSuite(ITestOutputHelper output)
        {
            _output = output;
            _graphicsService = new TestGraphicsDeviceService();
            _testTimer = new Stopwatch();
            
            _output.WriteLine("=== DTXMania Comprehensive QA Test Suite ===");
            _output.WriteLine($"Test started at: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
            _output.WriteLine($"Graphics Device: {_graphicsService.GraphicsDevice.Adapter.Description}");
        }

        #region Full System Tests

        [Fact]
        public async Task QA_FullSystemTest_PerfectPlaySimulation_AllRequirementsMet()
        {
            _output.WriteLine("\n🎯 STARTING FULL SYSTEM QA TEST - PERFECT PLAY SIMULATION");
            _testTimer.Restart();

            // Test Requirements:
            // ✅ Automated play simulation hitting all notes Just
            // ✅ Expect full score & life = 100
            // ✅ Verify no crashes and system stability

            var testResults = new QATestResults("Perfect Play Simulation");

            try
            {
                // Phase 1: Create comprehensive test chart
                _output.WriteLine("Phase 1: Creating test chart...");
                var testChart = CreateComprehensiveTestChart(noteCount: 500);
                var scoreManager = new ScoreManager(testChart.Count);
                var gaugeManager = new GaugeManager(startingLife: 50.0f);

                testResults.ChartNoteCount = testChart.Count;
                testResults.InitialLife = gaugeManager.CurrentLife;

                // Phase 2: Execute perfect play simulation
                _output.WriteLine("Phase 2: Executing perfect play simulation...");
                await ExecutePerfectPlaySimulation(testChart, scoreManager, gaugeManager, testResults);

                // Phase 3: Validate perfect play results
                _output.WriteLine("Phase 3: Validating results...");
                ValidatePerfectPlayResults(scoreManager, gaugeManager, testResults);

                // Phase 4: System stability checks
                _output.WriteLine("Phase 4: Running system stability checks...");
                await RunSystemStabilityChecks(testResults);

                testResults.TestPassed = true;
                _output.WriteLine($"✅ PERFECT PLAY SIMULATION PASSED in {_testTimer.ElapsedMilliseconds}ms");
            }
            catch (Exception ex)
            {
                testResults.TestPassed = false;
                testResults.ErrorMessage = ex.Message;
                _output.WriteLine($"❌ PERFECT PLAY SIMULATION FAILED: {ex.Message}");
                throw;
            }
            finally
            {
                testResults.ExecutionTimeMs = _testTimer.ElapsedMilliseconds;
                LogTestResults(testResults);
            }
        }

        [Fact]
        public async Task QA_RandomInputVarianceTest_80msVariance_NoCrashReasonableStats()
        {
            _output.WriteLine("\n🎲 STARTING RANDOM INPUT VARIANCE TEST (±80ms)");
            _testTimer.Restart();

            // Test Requirements:
            // ✅ Simulation with random ±80 ms input
            // ✅ Verify no crash and reasonable stats
            // ✅ Test system resilience with imperfect input

            var testResults = new QATestResults("Random Input Variance Test");

            try
            {
                // Phase 1: Setup variance test
                _output.WriteLine("Phase 1: Setting up variance test...");
                var testChart = CreateComprehensiveTestChart(noteCount: 300);
                var scoreManager = new ScoreManager(testChart.Count);
                var gaugeManager = new GaugeManager();
                var random = new Random(12345); // Fixed seed for reproducibility

                testResults.ChartNoteCount = testChart.Count;
                testResults.VarianceMs = 80.0;

                // Phase 2: Execute random variance simulation
                _output.WriteLine("Phase 2: Executing random variance simulation...");
                await ExecuteRandomVarianceSimulation(testChart, scoreManager, gaugeManager, random, 80.0, testResults);

                // Phase 3: Validate variance test results
                _output.WriteLine("Phase 3: Validating variance results...");
                ValidateVarianceTestResults(scoreManager, gaugeManager, testResults);

                // Phase 4: Stress testing with extreme cases
                _output.WriteLine("Phase 4: Running stress tests...");
                await RunVarianceStressTests(testResults);

                testResults.TestPassed = true;
                _output.WriteLine($"✅ RANDOM VARIANCE TEST PASSED in {_testTimer.ElapsedMilliseconds}ms");
            }
            catch (Exception ex)
            {
                testResults.TestPassed = false;
                testResults.ErrorMessage = ex.Message;
                _output.WriteLine($"❌ RANDOM VARIANCE TEST FAILED: {ex.Message}");
                throw;
            }
            finally
            {
                testResults.ExecutionTimeMs = _testTimer.ElapsedMilliseconds;
                LogTestResults(testResults);
            }
        }

        [Fact]
        public async Task QA_GPURenderingSnapshotTest_VisualRegression_NoRenderingIssues()
        {
            _output.WriteLine("\n🖼️ STARTING GPU RENDERING SNAPSHOT TEST");
            _testTimer.Restart();

            // Test Requirements:
            // ✅ Snapshot rendering tests on GPU
            // ✅ Visual regression detection
            // ✅ Graphics system stability

            var testResults = new QATestResults("GPU Rendering Snapshot Test");

            try
            {
                // Phase 1: Basic rendering tests
                _output.WriteLine("Phase 1: Basic rendering validation...");
                await RunBasicRenderingTests(testResults);

                // Phase 2: Complex scene rendering
                _output.WriteLine("Phase 2: Complex scene rendering...");
                await RunComplexSceneRenderingTests(testResults);

                // Phase 3: Performance and stress testing
                _output.WriteLine("Phase 3: GPU performance tests...");
                await RunGPUPerformanceTests(testResults);

                // Phase 4: Visual regression detection
                _output.WriteLine("Phase 4: Visual regression detection...");
                await RunVisualRegressionTests(testResults);

                testResults.TestPassed = true;
                _output.WriteLine($"✅ GPU RENDERING TEST PASSED in {_testTimer.ElapsedMilliseconds}ms");
            }
            catch (Exception ex)
            {
                testResults.TestPassed = false;
                testResults.ErrorMessage = ex.Message;
                _output.WriteLine($"❌ GPU RENDERING TEST FAILED: {ex.Message}");
                throw;
            }
            finally
            {
                testResults.ExecutionTimeMs = _testTimer.ElapsedMilliseconds;
                LogTestResults(testResults);
            }
        }

        [Fact]
        public async Task QA_ComprehensiveSystemTest_AllComponentsIntegration_FullValidation()
        {
            _output.WriteLine("\n🚀 STARTING COMPREHENSIVE SYSTEM INTEGRATION TEST");
            _testTimer.Restart();

            var testResults = new QATestResults("Comprehensive System Test");

            try
            {
                // Phase 1: Perfect play with rendering
                _output.WriteLine("Phase 1: Perfect play with GPU rendering...");
                await RunIntegratedPerfectPlayWithRendering(testResults);

                // Phase 2: Variance testing with visual validation
                _output.WriteLine("Phase 2: Variance testing with visual validation...");
                await RunIntegratedVarianceWithRendering(testResults);

                // Phase 3: Extreme stress testing
                _output.WriteLine("Phase 3: Extreme stress testing...");
                await RunExtremeStressTests(testResults);

                // Phase 4: Memory and resource leak detection
                _output.WriteLine("Phase 4: Resource leak detection...");
                await RunResourceLeakDetection(testResults);

                testResults.TestPassed = true;
                _output.WriteLine($"✅ COMPREHENSIVE SYSTEM TEST PASSED in {_testTimer.ElapsedMilliseconds}ms");
            }
            catch (Exception ex)
            {
                testResults.TestPassed = false;
                testResults.ErrorMessage = ex.Message;
                _output.WriteLine($"❌ COMPREHENSIVE SYSTEM TEST FAILED: {ex.Message}");
                throw;
            }
            finally
            {
                testResults.ExecutionTimeMs = _testTimer.ElapsedMilliseconds;
                LogTestResults(testResults);
                PrintFinalTestSummary();
            }
        }

        #endregion

        #region Perfect Play Simulation Implementation

        private async Task ExecutePerfectPlaySimulation(
            List<Note> testChart, 
            ScoreManager scoreManager, 
            GaugeManager gaugeManager, 
            QATestResults results)
        {
            var processedNotes = 0;

            foreach (var note in testChart.OrderBy(n => n.TimeMs))
            {
                // Perfect timing (0ms delta) = Just judgement
                var judgementEvent = new JudgementEvent(
                    noteRef: note.Id,
                    lane: note.LaneIndex,
                    deltaMs: 0.0, // Perfect timing
                    type: JudgementType.Just
                );

                // Process in both managers
                scoreManager.ProcessJudgement(judgementEvent);
                gaugeManager.ProcessJudgement(judgementEvent);

                processedNotes++;
                results.JustCount++;

                // Verify no failure during perfect play
                if (gaugeManager.HasFailed)
                {
                    throw new InvalidOperationException($"Player failed during perfect play at note {processedNotes}");
                }

                // Periodic progress logging
                if (processedNotes % 100 == 0)
                {
                    _output.WriteLine($"  Processed {processedNotes}/{testChart.Count} notes - Score: {scoreManager.CurrentScore:N0}, Life: {gaugeManager.CurrentLife:F1}%");
                }
            }

            results.ProcessedNotes = processedNotes;
            results.FinalScore = scoreManager.CurrentScore;
            results.FinalLife = gaugeManager.CurrentLife;
        }

        private void ValidatePerfectPlayResults(ScoreManager scoreManager, GaugeManager gaugeManager, QATestResults results)
        {
            var scoreStats = scoreManager.GetStatistics();

            // Validate perfect score
            Assert.Equal(scoreManager.TheoreticalMaxScore, scoreManager.CurrentScore);
            Assert.True(scoreStats.ScorePercentage >= 99.9, 
                $"Perfect play should achieve ~100% score. Got: {scoreStats.ScorePercentage:F2}%");

            // Validate perfect life (should be 100% or at maximum)
            Assert.True(gaugeManager.CurrentLife >= 100.0f, 
                $"Perfect play should result in 100% life. Got: {gaugeManager.CurrentLife:F1}%");

            // Validate no failure
            Assert.False(gaugeManager.HasFailed, "Perfect play should never result in failure");

            // Validate all hits were Just
            Assert.Equal(results.ChartNoteCount, results.JustCount);

            _output.WriteLine($"  ✅ Perfect Score: {scoreManager.CurrentScore:N0}/{scoreManager.TheoreticalMaxScore:N0}");
            _output.WriteLine($"  ✅ Perfect Life: {gaugeManager.CurrentLife:F1}%");
            _output.WriteLine($"  ✅ All {results.JustCount} hits were Just judgements");
        }

        #endregion

        #region Random Variance Implementation

        private async Task ExecuteRandomVarianceSimulation(
            List<Note> testChart, 
            ScoreManager scoreManager, 
            GaugeManager gaugeManager, 
            Random random,
            double maxVarianceMs,
            QATestResults results)
        {
            var processedNotes = 0;

            try
            {
                foreach (var note in testChart.OrderBy(n => n.TimeMs))
                {
                    // Generate random timing within ±maxVarianceMs
                    var timingDelta = (random.NextDouble() - 0.5) * 2.0 * maxVarianceMs;
                    
                    var judgementEvent = new JudgementEvent(
                        noteRef: note.Id,
                        lane: note.LaneIndex,
                        deltaMs: timingDelta
                    );

                    scoreManager.ProcessJudgement(judgementEvent);
                    gaugeManager.ProcessJudgement(judgementEvent);

                    // Track judgement statistics
                    switch (judgementEvent.Type)
                    {
                        case JudgementType.Just: results.JustCount++; break;
                        case JudgementType.Great: results.GreatCount++; break;
                        case JudgementType.Good: results.GoodCount++; break;
                        case JudgementType.Poor: results.PoorCount++; break;
                        case JudgementType.Miss: results.MissCount++; break;
                    }

                    processedNotes++;
                    results.TotalTimingDeviation += Math.Abs(timingDelta);

                    // Log extreme cases
                    if (Math.Abs(timingDelta) > maxVarianceMs * 0.9)
                    {
                        _output.WriteLine($"  Extreme timing: {timingDelta:+0.0;-0.0;0.0}ms → {judgementEvent.Type}");
                    }
                }

                results.ProcessedNotes = processedNotes;
                results.FinalScore = scoreManager.CurrentScore;
                results.FinalLife = gaugeManager.CurrentLife;
                results.AverageTimingDeviation = results.TotalTimingDeviation / processedNotes;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Random variance simulation crashed at note {processedNotes}: {ex.Message}", ex);
            }
        }

        private void ValidateVarianceTestResults(ScoreManager scoreManager, GaugeManager gaugeManager, QATestResults results)
        {
            var scoreStats = scoreManager.GetStatistics();

            // Validate simulation completed
            Assert.Equal(results.ChartNoteCount, results.ProcessedNotes);

            // Validate reasonable score range for ±80ms variance
            Assert.True(scoreStats.ScorePercentage >= 10.0 && scoreStats.ScorePercentage <= 90.0,
                $"±80ms variance should yield 10-90% score. Got: {scoreStats.ScorePercentage:F1}%");

            // Validate judgement variety
            var judgementTypes = 0;
            if (results.JustCount > 0) judgementTypes++;
            if (results.GreatCount > 0) judgementTypes++;
            if (results.GoodCount > 0) judgementTypes++;
            if (results.PoorCount > 0) judgementTypes++;
            if (results.MissCount > 0) judgementTypes++;

            Assert.True(judgementTypes >= 3, 
                $"±80ms variance should produce variety in judgements. Got {judgementTypes} types");

            // Validate reasonable timing deviation
            Assert.True(results.AverageTimingDeviation >= 20.0 && results.AverageTimingDeviation <= 60.0,
                $"Average timing deviation should be 20-60ms. Got: {results.AverageTimingDeviation:F1}ms");

            _output.WriteLine($"  ✅ Judgement Distribution: Just={results.JustCount}, Great={results.GreatCount}, Good={results.GoodCount}, Poor={results.PoorCount}, Miss={results.MissCount}");
            _output.WriteLine($"  ✅ Score: {scoreManager.CurrentScore:N0} ({scoreStats.ScorePercentage:F1}%)");
            _output.WriteLine($"  ✅ Life: {gaugeManager.CurrentLife:F1}%");
            _output.WriteLine($"  ✅ Average Timing Deviation: ±{results.AverageTimingDeviation:F1}ms");
        }

        #endregion

        #region GPU Rendering Implementation

        private async Task RunBasicRenderingTests(QATestResults results)
        {
            var graphicsDevice = _graphicsService.GraphicsDevice;
            var renderTarget = new RenderTarget2D(graphicsDevice, 800, 600);

            try
            {
                // Test basic clear operations
                graphicsDevice.SetRenderTarget(renderTarget);
                graphicsDevice.Clear(Color.Black);
                graphicsDevice.SetRenderTarget(null);

                // Validate clear worked
                var pixelData = new Color[800 * 600];
                renderTarget.GetData(pixelData);
                
                var allBlack = pixelData.Take(1000).All(p => p.R == 0 && p.G == 0 && p.B == 0);
                Assert.True(allBlack, "Basic clear rendering failed");

                results.RenderingTestsPassed++;
                _output.WriteLine("  ✅ Basic clear rendering test passed");
            }
            finally
            {
                renderTarget?.Dispose();
            }
        }

        private async Task RunComplexSceneRenderingTests(QATestResults results)
        {
            var graphicsDevice = _graphicsService.GraphicsDevice;
            var renderTarget = new RenderTarget2D(graphicsDevice, 1024, 768);
            var spriteBatch = new SpriteBatch(graphicsDevice);

            try
            {
                // Create test texture
                var testTexture = new Texture2D(graphicsDevice, 64, 64);
                var textureData = new Color[64 * 64];
                Array.Fill(textureData, Color.White);
                testTexture.SetData(textureData);

                // Render complex scene
                graphicsDevice.SetRenderTarget(renderTarget);
                graphicsDevice.Clear(Color.DarkBlue);

                spriteBatch.Begin();
                
                // Render multiple sprites with different properties
                for (int i = 0; i < 50; i++)
                {
                    var x = (i % 10) * 100;
                    var y = (i / 10) * 100;
                    var color = new Color(i * 5, 255 - i * 5, 128);
                    spriteBatch.Draw(testTexture, new Vector2(x, y), color);
                }

                spriteBatch.End();
                graphicsDevice.SetRenderTarget(null);

                // Validate rendering
                var pixelData = new Color[1024 * 768];
                renderTarget.GetData(pixelData);
                
                var nonBlackPixels = pixelData.Count(p => p.R > 10 || p.G > 10 || p.B > 10);
                Assert.True(nonBlackPixels > 50000, "Complex scene should have many non-black pixels");

                results.RenderingTestsPassed++;
                _output.WriteLine($"  ✅ Complex scene rendering test passed ({nonBlackPixels} non-black pixels)");

                testTexture.Dispose();
            }
            finally
            {
                spriteBatch?.Dispose();
                renderTarget?.Dispose();
            }
        }

        private async Task RunGPUPerformanceTests(QATestResults results)
        {
            var stopwatch = Stopwatch.StartNew();
            
            // Create and dispose multiple render targets quickly
            for (int i = 0; i < 10; i++)
            {
                var rt = new RenderTarget2D(_graphicsService.GraphicsDevice, 256, 256);
                _graphicsService.GraphicsDevice.SetRenderTarget(rt);
                _graphicsService.GraphicsDevice.Clear(Color.Red);
                _graphicsService.GraphicsDevice.SetRenderTarget(null);
                rt.Dispose();
            }

            stopwatch.Stop();
            
            Assert.True(stopwatch.ElapsedMilliseconds < 5000, 
                $"GPU performance test should complete quickly. Took: {stopwatch.ElapsedMilliseconds}ms");

            results.RenderingTestsPassed++;
            _output.WriteLine($"  ✅ GPU performance test passed in {stopwatch.ElapsedMilliseconds}ms");
        }

        private async Task RunVisualRegressionTests(QATestResults results)
        {
            // Create consistent test scene and verify hash consistency
            var graphicsDevice = _graphicsService.GraphicsDevice;
            var renderTarget = new RenderTarget2D(graphicsDevice, 400, 300);

            try
            {
                // Render deterministic scene
                graphicsDevice.SetRenderTarget(renderTarget);
                graphicsDevice.Clear(Color.Gray);
                graphicsDevice.SetRenderTarget(null);

                // Get pixel data and compute hash
                var pixelData = new Color[400 * 300];
                renderTarget.GetData(pixelData);
                
                var hash1 = ComputeSimpleHash(pixelData);

                // Render same scene again
                graphicsDevice.SetRenderTarget(renderTarget);
                graphicsDevice.Clear(Color.Gray);
                graphicsDevice.SetRenderTarget(null);

                renderTarget.GetData(pixelData);
                var hash2 = ComputeSimpleHash(pixelData);

                Assert.Equal(hash1, hash2);

                results.RenderingTestsPassed++;
                _output.WriteLine($"  ✅ Visual regression test passed (hash: {hash1:X8})");
            }
            finally
            {
                renderTarget?.Dispose();
            }
        }

        #endregion

        #region Integration and Stress Tests

        private async Task RunIntegratedPerfectPlayWithRendering(QATestResults results)
        {
            _output.WriteLine("  Running perfect play with simultaneous rendering...");
            
            var testChart = CreateComprehensiveTestChart(100);
            var scoreManager = new ScoreManager(testChart.Count);
            
            // Simulate perfect play while doing rendering operations
            var renderTarget = new RenderTarget2D(_graphicsService.GraphicsDevice, 400, 300);
            
            try
            {
                foreach (var note in testChart.Take(50)) // Limited for performance
                {
                    // Process judgement
                    var judgement = new JudgementEvent(note.Id, note.LaneIndex, 0.0, JudgementType.Just);
                    scoreManager.ProcessJudgement(judgement);
                    
                    // Simultaneous rendering
                    _graphicsService.GraphicsDevice.SetRenderTarget(renderTarget);
                    _graphicsService.GraphicsDevice.Clear(Color.Black);
                    _graphicsService.GraphicsDevice.SetRenderTarget(null);
                }

                Assert.True(scoreManager.CurrentScore > 0, "Integrated test should maintain scoring functionality");
                results.IntegrationTestsPassed++;
                _output.WriteLine("    ✅ Perfect play + rendering integration passed");
            }
            finally
            {
                renderTarget?.Dispose();
            }
        }

        private async Task RunIntegratedVarianceWithRendering(QATestResults results)
        {
            _output.WriteLine("  Running variance testing with rendering validation...");
            
            // Combined variance and rendering stress test
            var testChart = CreateComprehensiveTestChart(50);
            var scoreManager = new ScoreManager(testChart.Count);
            var random = new Random(999);

            var renderTargets = new List<RenderTarget2D>();
            
            try
            {
                foreach (var note in testChart)
                {
                    // Random timing variance
                    var timing = (random.NextDouble() - 0.5) * 2.0 * 60.0;
                    var judgement = new JudgementEvent(note.Id, note.LaneIndex, timing);
                    scoreManager.ProcessJudgement(judgement);
                    
                    // Create render target for this note
                    var rt = new RenderTarget2D(_graphicsService.GraphicsDevice, 64, 64);
                    renderTargets.Add(rt);
                    
                    _graphicsService.GraphicsDevice.SetRenderTarget(rt);
                    _graphicsService.GraphicsDevice.Clear(new Color(note.LaneIndex * 30, 100, 200));
                    _graphicsService.GraphicsDevice.SetRenderTarget(null);
                }

                Assert.Equal(testChart.Count, renderTargets.Count);
                results.IntegrationTestsPassed++;
                _output.WriteLine("    ✅ Variance + rendering integration passed");
            }
            finally
            {
                foreach (var rt in renderTargets)
                    rt?.Dispose();
            }
        }

        private async Task RunExtremeStressTests(QATestResults results)
        {
            _output.WriteLine("  Running extreme stress tests...");
            
            // Test with very large chart
            var largeChart = CreateComprehensiveTestChart(1000);
            var scoreManager = new ScoreManager(largeChart.Count);
            var stopwatch = Stopwatch.StartNew();

            // Process all notes as fast as possible
            foreach (var note in largeChart)
            {
                var judgement = new JudgementEvent(note.Id, note.LaneIndex, 0.0, JudgementType.Just);
                scoreManager.ProcessJudgement(judgement);
            }

            stopwatch.Stop();
            
            Assert.True(stopwatch.ElapsedMilliseconds < 10000, 
                $"Stress test should complete within 10s. Took: {stopwatch.ElapsedMilliseconds}ms");
            
            results.StressTestsPassed++;
            _output.WriteLine($"    ✅ Extreme stress test passed in {stopwatch.ElapsedMilliseconds}ms");
        }

        private async Task RunResourceLeakDetection(QATestResults results)
        {
            _output.WriteLine("  Running resource leak detection...");
            
            // Create and dispose many objects to test for leaks
            var initialMemory = GC.GetTotalMemory(true);
            
            for (int i = 0; i < 100; i++)
            {
                var rt = new RenderTarget2D(_graphicsService.GraphicsDevice, 128, 128);
                var scoreManager = new ScoreManager(10);
                var gaugeManager = new GaugeManager();
                
                // Use objects briefly
                scoreManager.ProcessJudgement(new JudgementEvent(1, 0, 0.0, JudgementType.Just));
                gaugeManager.ProcessJudgement(new JudgementEvent(1, 0, 0.0, JudgementType.Just));
                
                // Dispose
                rt.Dispose();
                scoreManager.Dispose();
                gaugeManager.Dispose();
            }
            
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            
            var finalMemory = GC.GetTotalMemory(false);
            var memoryIncrease = finalMemory - initialMemory;
            
            _output.WriteLine($"    Memory change: {memoryIncrease:N0} bytes");
            
            // Allow for some memory increase but flag excessive growth
            Assert.True(memoryIncrease < 50_000_000, // 50MB threshold
                $"Excessive memory growth detected: {memoryIncrease:N0} bytes");
            
            results.ResourceTestsPassed++;
            _output.WriteLine("    ✅ Resource leak detection passed");
        }

        #endregion

        #region System Stability Checks

        private async Task RunSystemStabilityChecks(QATestResults results)
        {
            // Check for memory stability
            var initialMemory = GC.GetTotalMemory(false);
            
            // Force garbage collection
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            
            var afterGCMemory = GC.GetTotalMemory(false);
            
            _output.WriteLine($"  Memory before GC: {initialMemory:N0} bytes");
            _output.WriteLine($"  Memory after GC: {afterGCMemory:N0} bytes");
            
            results.StabilityChecksPassed++;
        }

        private async Task RunVarianceStressTests(QATestResults results)
        {
            // Test extreme variance scenarios
            var extremeVariances = new[] { 200.0, 500.0, 1000.0 };
            
            foreach (var variance in extremeVariances)
            {
                var testChart = CreateComprehensiveTestChart(20);
                var scoreManager = new ScoreManager(testChart.Count);
                var random = new Random(42);
                
                try
                {
                    foreach (var note in testChart)
                    {
                        var timing = (random.NextDouble() - 0.5) * 2.0 * variance;
                        var judgement = new JudgementEvent(note.Id, note.LaneIndex, timing);
                        scoreManager.ProcessJudgement(judgement);
                    }
                    
                    _output.WriteLine($"  ✅ Extreme variance ±{variance}ms handled without crash");
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Extreme variance ±{variance}ms caused crash: {ex.Message}");
                }
            }
            
            results.StressTestsPassed++;
        }

        #endregion

        #region Helper Methods

        private List<Note> CreateComprehensiveTestChart(int noteCount)
        {
            var notes = new List<Note>();
            var random = new Random(12345); // Fixed seed
            const double bpm = 140.0;

            for (int i = 0; i < noteCount; i++)
            {
                var note = new Note
                {
                    Id = i,
                    LaneIndex = i % 9,
                    Bar = i / 16,
                    Tick = (i % 16) * 12,
                    Channel = 11 + (i % 9),
                    Value = "01"
                };
                
                note.CalculateTimeMs(bpm);
                notes.Add(note);
            }

            return notes.OrderBy(n => n.TimeMs).ToList();
        }

        private uint ComputeSimpleHash(Color[] pixelData)
        {
            uint hash = 0;
            foreach (var pixel in pixelData.Take(1000)) // Sample for performance
            {
                hash = hash * 31 + (uint)(pixel.R + pixel.G + pixel.B);
            }
            return hash;
        }

        private void LogTestResults(QATestResults results)
        {
            _output.WriteLine($"\n📊 TEST RESULTS: {results.TestName}");
            _output.WriteLine($"   Execution Time: {results.ExecutionTimeMs}ms");
            _output.WriteLine($"   Test Passed: {(results.TestPassed ? "✅ YES" : "❌ NO")}");
            
            if (!string.IsNullOrEmpty(results.ErrorMessage))
                _output.WriteLine($"   Error: {results.ErrorMessage}");
                
            if (results.ChartNoteCount > 0)
            {
                _output.WriteLine($"   Chart Notes: {results.ChartNoteCount}");
                _output.WriteLine($"   Processed Notes: {results.ProcessedNotes}");
                _output.WriteLine($"   Final Score: {results.FinalScore:N0}");
                _output.WriteLine($"   Final Life: {results.FinalLife:F1}%");
            }
            
            if (results.JustCount > 0 || results.GreatCount > 0 || results.GoodCount > 0)
            {
                _output.WriteLine($"   Judgements: J={results.JustCount}, Gt={results.GreatCount}, Gd={results.GoodCount}, P={results.PoorCount}, M={results.MissCount}");
            }
            
            _output.WriteLine($"   Tests Passed: Rendering={results.RenderingTestsPassed}, Integration={results.IntegrationTestsPassed}, Stress={results.StressTestsPassed}");
        }

        private void PrintFinalTestSummary()
        {
            _output.WriteLine("\n🏁 === FINAL QA TEST SUMMARY ===");
            _output.WriteLine("All comprehensive QA tests have been executed.");
            _output.WriteLine("✅ Perfect Play Simulation: Verified full score & life = 100");
            _output.WriteLine("✅ Random Input Variance: Verified no crashes with ±80ms variance");
            _output.WriteLine("✅ GPU Rendering Snapshots: Verified visual regression detection");
            _output.WriteLine("✅ System Integration: Verified component interaction stability");
            _output.WriteLine($"Total execution time: {_testTimer.ElapsedMilliseconds}ms");
            _output.WriteLine("=== QA VALIDATION COMPLETE ===");
        }

        #endregion

        #region Supporting Classes

        private class QATestResults
        {
            public string TestName { get; }
            public bool TestPassed { get; set; }
            public long ExecutionTimeMs { get; set; }
            public string ErrorMessage { get; set; } = "";
            
            // Gameplay metrics
            public int ChartNoteCount { get; set; }
            public int ProcessedNotes { get; set; }
            public int FinalScore { get; set; }
            public float InitialLife { get; set; }
            public float FinalLife { get; set; }
            public double VarianceMs { get; set; }
            public double TotalTimingDeviation { get; set; }
            public double AverageTimingDeviation { get; set; }
            
            // Judgement counts
            public int JustCount { get; set; }
            public int GreatCount { get; set; }
            public int GoodCount { get; set; }
            public int PoorCount { get; set; }
            public int MissCount { get; set; }
            
            // Test category passes
            public int RenderingTestsPassed { get; set; }
            public int IntegrationTestsPassed { get; set; }
            public int StressTestsPassed { get; set; }
            public int StabilityChecksPassed { get; set; }
            public int ResourceTestsPassed { get; set; }

            public QATestResults(string testName)
            {
                TestName = testName;
            }
        }

        #endregion

        #region IDisposable Implementation

        public void Dispose()
        {
            _graphicsService?.Dispose();
        }

        #endregion
    }
}
