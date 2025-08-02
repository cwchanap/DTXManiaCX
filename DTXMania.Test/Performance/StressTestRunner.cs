using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Xunit;
using Xunit.Abstractions;
using DTX.Song;
using DTX.Song.Components;
using DTX.Stage;
using DTX.Stage.Performance;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Test.Helpers;

namespace DTXMania.Test.Performance
{
    /// <summary>
    /// Stress testing runner for performance validation with 100k-note synthetic charts
    /// Task 3-B-5: Load 100 k-note synthetic chart, run 5 minutes, ensure frame time < 16 ms on reference GPU
    /// Profile allocations; pool Effect instances.
    /// </summary>
    public class StressTestRunner : IDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly TestGraphicsDeviceService _graphicsService;
        private readonly MockResourceManager _resourceManager;
        private readonly string _tempDir;
        private readonly List<long> _frameTimings = new List<long>();
        private readonly List<long> _memorySnapshots = new List<long>();
        
        // Performance targets
        private const double TARGET_FRAME_TIME_MS = 16.0; // 60 FPS = 16.67ms per frame
        private const int TEST_DURATION_MINUTES = 5;
        private const int SYNTHETIC_NOTE_COUNT = 100000;
        private const int MEMORY_SNAPSHOT_INTERVAL_MS = 1000; // Every second

        public StressTestRunner(ITestOutputHelper output)
        {
            _output = output;
            _graphicsService = new TestGraphicsDeviceService();
            _resourceManager = new MockResourceManager(_graphicsService.GraphicsDevice);
            _tempDir = Path.Combine(Path.GetTempPath(), "DTXMania_StressTest_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(_tempDir);
        }

        /// <summary>
        /// Main stress test: Load 100k-note synthetic chart and run performance validation
        /// </summary>
        [Fact]
        public async Task StressTest_100kNoteChart_MaintainsPerformanceTargets()
        {
            _output.WriteLine("=== DTXMania Stress Test: 100k-Note Chart ===");
            _output.WriteLine($"Target frame time: {TARGET_FRAME_TIME_MS}ms");
            _output.WriteLine($"Test duration: {TEST_DURATION_MINUTES} minutes");
            _output.WriteLine($"Synthetic note count: {SYNTHETIC_NOTE_COUNT:N0}");

            // Phase 1: Generate synthetic chart
            _output.WriteLine("\n[Phase 1] Generating synthetic chart...");
            var stopwatch = Stopwatch.StartNew();
            var syntheticChart = await GenerateSyntheticChart(SYNTHETIC_NOTE_COUNT);
            stopwatch.Stop();
            _output.WriteLine($"Chart generation completed in {stopwatch.ElapsedMilliseconds}ms");
            _output.WriteLine($"Generated {syntheticChart.Notes.Count:N0} notes (estimated bars: {syntheticChart.Notes.Count / 20})");

            // Phase 2: Initialize performance stage with pooled effects
            _output.WriteLine("\n[Phase 2] Initializing performance stage with Effect pooling...");
            var pooledEffectsManager = new PooledEffectsManager(_graphicsService.GraphicsDevice, _resourceManager);
            var performanceStage = CreatePerformanceStage(syntheticChart, pooledEffectsManager);

            // Phase 3: Run stress test
            _output.WriteLine("\n[Phase 3] Starting stress test...");
            var testResults = await RunStressTest(performanceStage, TimeSpan.FromMinutes(TEST_DURATION_MINUTES));

            // Phase 4: Analyze results
            _output.WriteLine("\n[Phase 4] Analyzing performance results...");
            AnalyzePerformanceResults(testResults);

            // Phase 5: Memory profiling results
            _output.WriteLine("\n[Phase 5] Memory allocation profiling...");
            AnalyzeMemoryAllocation(testResults);

            // Cleanup
            performanceStage?.Dispose();
            pooledEffectsManager?.Dispose();
        }

        /// <summary>
        /// Generates a synthetic chart with the specified note count for stress testing
        /// </summary>
        private async Task<ParsedChart> GenerateSyntheticChart(int noteCount)
        {
            var chartPath = Path.Combine(_tempDir, "synthetic_100k.dtx");
            var chart = new ParsedChart(chartPath);
            
            // Set BPM
            chart.Bpm = 180.0; // High BPM for dense note patterns
            var estimatedDuration = CalculateChartDuration(noteCount, 180.0);

            var random = new Random(42); // Fixed seed for reproducible tests
            var noteCounter = 0;
            var bar = 0;

            _output.WriteLine($"Generating {noteCount:N0} notes with BPM {chart.Bpm}...");

            while (noteCounter < noteCount)
            {
                var notesInBar = Math.Min(20, noteCount - noteCounter); // Up to 20 notes per bar

                for (int i = 0; i < notesInBar; i++)
                {
                    var laneIndex = random.Next(0, 9); // 9 lanes (0-8)
                    var tick = random.Next(0, 192); // 192 ticks per bar
                    var channel = 11 + laneIndex; // DTX channels 11-19

                    var note = new Note
                    {
                        LaneIndex = laneIndex,
                        Bar = bar,
                        Tick = tick,
                        Channel = channel,
                        Value = "01" // Default WAV reference
                    };

                    // Calculate timing
                    note.CalculateTimeMs(chart.Bpm);
                    
                    chart.AddNote(note);
                    noteCounter++;
                }

                bar++;

                // Progress reporting
                if (bar % 100 == 0)
                {
                    _output.WriteLine($"Generated {noteCounter:N0}/{noteCount:N0} notes ({(double)noteCounter/noteCount*100:F1}%)");
                }
            }

            // Finalize the chart
            chart.FinalizeChart();

            _output.WriteLine($"Synthetic chart generation complete: {noteCounter:N0} notes in {bar} bars");
            return chart;
        }

        /// <summary>
        /// Creates a performance stage with the synthetic chart and pooled effects manager
        /// </summary>
        private MockPerformanceStage CreatePerformanceStage(ParsedChart chart, PooledEffectsManager effectsManager)
        {
            var stage = new MockPerformanceStage(_graphicsService.GraphicsDevice, _resourceManager);
            stage.LoadChart(chart);
            stage.SetEffectsManager(effectsManager);
            return stage;
        }

        /// <summary>
        /// Runs the actual stress test for the specified duration
        /// </summary>
        private async Task<StressTestResults> RunStressTest(MockPerformanceStage stage, TimeSpan duration)
        {
            var results = new StressTestResults();
            var testStopwatch = Stopwatch.StartNew();
            var frameStopwatch = new Stopwatch();
            var memoryStopwatch = Stopwatch.StartNew();
            
            _frameTimings.Clear();
            _memorySnapshots.Clear();

            long frameCount = 0;
            var lastMemorySnapshot = 0L;

            _output.WriteLine($"Running stress test for {duration.TotalMinutes} minutes...");

            while (testStopwatch.Elapsed < duration)
            {
                frameStopwatch.Restart();

                // Simulate game update and draw cycle
                var gameTime = new GameTime(testStopwatch.Elapsed, TimeSpan.FromMilliseconds(16.67));
                stage.Update(gameTime);
                stage.Draw(gameTime);

                frameStopwatch.Stop();
                _frameTimings.Add(frameStopwatch.ElapsedTicks);
                frameCount++;

                // Memory snapshots
                if (memoryStopwatch.ElapsedMilliseconds - lastMemorySnapshot >= MEMORY_SNAPSHOT_INTERVAL_MS)
                {
                    _memorySnapshots.Add(GC.GetTotalMemory(false));
                    lastMemorySnapshot = memoryStopwatch.ElapsedMilliseconds;
                }

                // Progress reporting every 30 seconds
                if (frameCount % (60 * 30) == 0) // Assuming 60 FPS
                {
                    var elapsed = testStopwatch.Elapsed;
                    var remaining = duration - elapsed;
                    var avgFrameTime = _frameTimings.Skip(Math.Max(0, _frameTimings.Count - 1800)).Average() / 10000.0; // Last 30 seconds
                    
                    _output.WriteLine($"Progress: {elapsed.TotalMinutes:F1}/{duration.TotalMinutes}min remaining, " +
                                    $"Avg frame time: {avgFrameTime:F2}ms");
                }

                // Simulate realistic frame timing
                await Task.Delay(1);
            }

            testStopwatch.Stop();

            results.TotalFrames = frameCount;
            results.TestDuration = testStopwatch.Elapsed;
            results.FrameTimings = _frameTimings.ToArray();
            results.MemorySnapshots = _memorySnapshots.ToArray();
            var poolStats = stage.GetEffectsManager()?.GetPoolingStats();
            if (poolStats != null)
            {
                results.EffectsPoolStats = new EffectPoolingStats
                {
                    PoolSize = poolStats.PoolSize,
                    ActiveInstances = poolStats.ActiveInstances,
                    TotalRequests = poolStats.TotalRequests,
                    PoolHits = poolStats.PoolHits,
                    PoolMisses = poolStats.PoolMisses
                };
            }

            return results;
        }

        /// <summary>
        /// Analyzes performance results and validates against targets
        /// </summary>
        private void AnalyzePerformanceResults(StressTestResults results)
        {
            var frameTimesMs = results.FrameTimings.Select(ticks => ticks / 10000.0).ToArray();
            
            var avgFrameTime = frameTimesMs.Average();
            var maxFrameTime = frameTimesMs.Max();
            var minFrameTime = frameTimesMs.Min();
            var p95FrameTime = frameTimesMs.OrderBy(x => x).Skip((int)(frameTimesMs.Length * 0.95)).First();
            var p99FrameTime = frameTimesMs.OrderBy(x => x).Skip((int)(frameTimesMs.Length * 0.99)).First();
            
            var framesOverTarget = frameTimesMs.Count(ft => ft > TARGET_FRAME_TIME_MS);
            var percentOverTarget = (double)framesOverTarget / frameTimesMs.Length * 100;

            _output.WriteLine("=== PERFORMANCE ANALYSIS ===");
            _output.WriteLine($"Total frames: {results.TotalFrames:N0}");
            _output.WriteLine($"Test duration: {results.TestDuration.TotalMinutes:F2} minutes");
            _output.WriteLine($"Average FPS: {results.TotalFrames / results.TestDuration.TotalSeconds:F1}");
            _output.WriteLine("");
            _output.WriteLine("Frame Time Statistics:");
            _output.WriteLine($"  Average: {avgFrameTime:F2}ms");
            _output.WriteLine($"  Minimum: {minFrameTime:F2}ms");
            _output.WriteLine($"  Maximum: {maxFrameTime:F2}ms");
            _output.WriteLine($"  95th percentile: {p95FrameTime:F2}ms");
            _output.WriteLine($"  99th percentile: {p99FrameTime:F2}ms");
            _output.WriteLine("");
            _output.WriteLine($"Frames over target ({TARGET_FRAME_TIME_MS}ms): {framesOverTarget:N0} ({percentOverTarget:F2}%)");

            // Performance assertions
            Assert.True(avgFrameTime < TARGET_FRAME_TIME_MS, 
                $"Average frame time {avgFrameTime:F2}ms exceeds target {TARGET_FRAME_TIME_MS}ms");
            Assert.True(p95FrameTime < TARGET_FRAME_TIME_MS * 1.5, 
                $"95th percentile frame time {p95FrameTime:F2}ms exceeds acceptable threshold");
            Assert.True(percentOverTarget < 5, 
                $"Too many frames ({percentOverTarget:F2}%) exceed target frame time");
        }

        /// <summary>
        /// Analyzes memory allocation patterns and validates pooling effectiveness
        /// </summary>
        private void AnalyzeMemoryAllocation(StressTestResults results)
        {
            if (results.MemorySnapshots.Length < 2)
            {
                _output.WriteLine("Insufficient memory snapshots for analysis");
                return;
            }

            var initialMemory = results.MemorySnapshots[0];
            var finalMemory = results.MemorySnapshots[^1];
            var maxMemory = results.MemorySnapshots.Max();
            var memoryGrowth = (finalMemory - initialMemory) / 1024.0 / 1024.0; // MB

            _output.WriteLine("=== MEMORY ALLOCATION ANALYSIS ===");
            _output.WriteLine($"Initial memory: {initialMemory / 1024.0 / 1024.0:F2} MB");
            _output.WriteLine($"Final memory: {finalMemory / 1024.0 / 1024.0:F2} MB");
            _output.WriteLine($"Peak memory: {maxMemory / 1024.0 / 1024.0:F2} MB");
            _output.WriteLine($"Memory growth: {memoryGrowth:F2} MB");

            if (results.EffectsPoolStats != null)
            {
                _output.WriteLine("");
                _output.WriteLine("=== EFFECT POOLING STATISTICS ===");
                _output.WriteLine($"Pool size: {results.EffectsPoolStats.PoolSize}");
                _output.WriteLine($"Active instances: {results.EffectsPoolStats.ActiveInstances}");
                _output.WriteLine($"Total requests: {results.EffectsPoolStats.TotalRequests:N0}");
                _output.WriteLine($"Pool hits: {results.EffectsPoolStats.PoolHits:N0}");
                _output.WriteLine($"Pool misses: {results.EffectsPoolStats.PoolMisses:N0}");
                _output.WriteLine($"Hit rate: {(double)results.EffectsPoolStats.PoolHits / results.EffectsPoolStats.TotalRequests * 100:F1}%");
            }

            // Memory growth should be reasonable (less than 100MB for a 5-minute test)
            Assert.True(memoryGrowth < 100, 
                $"Memory growth {memoryGrowth:F2}MB is excessive for stress test duration");
        }

        /// <summary>
        /// Calculates estimated chart duration based on note count and BPM
        /// </summary>
        private static double CalculateChartDuration(int noteCount, double bpm)
        {
            // Rough estimation: assuming average note density
            var beatsPerSecond = bpm / 60.0;
            var averageNotesPerBeat = 4.0; // Estimate
            var estimatedSeconds = noteCount / (beatsPerSecond * averageNotesPerBeat);
            return Math.Max(estimatedSeconds, 180); // Minimum 3 minutes
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDir))
            {
                try
                {
                    Directory.Delete(_tempDir, true);
                }
                catch
                {
                    // Ignore cleanup errors in tests
                }
            }
            
            _graphicsService?.Dispose();
            _resourceManager?.Dispose();
        }
    }

    /// <summary>
    /// Results container for stress test data
    /// </summary>
    public class StressTestResults
    {
        public long TotalFrames { get; set; }
        public TimeSpan TestDuration { get; set; }
        public long[] FrameTimings { get; set; } = Array.Empty<long>();
        public long[] MemorySnapshots { get; set; } = Array.Empty<long>();
        public EffectPoolingStats? EffectsPoolStats { get; set; }
    }

    /// <summary>
    /// Statistics for Effect instance pooling
    /// </summary>
    public class EffectPoolingStats
    {
        public int PoolSize { get; set; }
        public int ActiveInstances { get; set; }
        public long TotalRequests { get; set; }
        public long PoolHits { get; set; }
        public long PoolMisses { get; set; }
    }
}
