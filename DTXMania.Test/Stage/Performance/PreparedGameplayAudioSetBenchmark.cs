using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Stage.Performance;
using Xunit;

namespace DTXMania.Test.Stage.Performance
{
    /// <summary>
    /// Cold/warm-cache preparation benchmark for <see cref="PreparedGameplayAudioSet"/>.
    ///
    /// This is the Phase B benchmark artifact required by the play-speed/pitch plan
    /// (docs/superpowers/plans/2026-07-22-play-speed-pitch-adjustment.md, "Preparation
    /// performance gate"). It measures the real serial decode path that the default
    /// profile exercises (OriginalAudioPcmDecoder + new SoundEffect, one ffmpeg spawn
    /// per MP3 source) against a dense 128-chip fixture, plus a non-default profile
    /// cold/warm pair through FfmpegAudioVariantProcessor + PlaybackAudioVariantCache.
    ///
    /// Opt-in only: skipped unless DTXMANIA_RUN_BENCHMARKS=1 is set, so it never slows
    /// the normal test run. Writes a markdown artifact to
    /// TestResults/benchmarks/cold-warm-audio-prep.md and asserts the plan's 30 s
    /// dense cold-cache budget so a regression fails loudly when explicitly run.
    /// </summary>
    [Trait("Category", "Benchmark")]
    public sealed class PreparedGameplayAudioSetBenchmark : IDisposable
    {
        private const int DenseChipCount = 128;
        private static readonly TimeSpan DenseChipDuration = TimeSpan.FromSeconds(2);

        private readonly string _scratchDir;
        private readonly string _artifactDirectory;
        private readonly List<string> _mp3Sources;

        public PreparedGameplayAudioSetBenchmark()
        {
            if (Environment.GetEnvironmentVariable("DTXMANIA_RUN_BENCHMARKS") != "1")
            {
                _scratchDir = string.Empty;
                _artifactDirectory = string.Empty;
                _mp3Sources = new List<string>();
                return;
            }

            _scratchDir = Path.Combine(
                Path.GetTempPath(),
                "DTXMania_Benchmark_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_scratchDir);

            _artifactDirectory = Path.Combine(
                AppContext.BaseDirectory, "TestResults", "benchmarks");
            Directory.CreateDirectory(_artifactDirectory);

            _mp3Sources = GenerateDenseMp3Sources(_scratchDir, DenseChipCount, DenseChipDuration);
        }

        [Fact]
        public async Task Benchmark_ColdCache_DefaultProfile_DenseChart_MeetsThirtySecondBudget()
        {
            if (Environment.GetEnvironmentVariable("DTXMANIA_RUN_BENCHMARKS") != "1")
                return; // Opt-in benchmark: no-op unless DTXMANIA_RUN_BENCHMARKS=1.

            // Default profile (1.00x / 0 st) exercises the serial ManagedSound loader
            // path: one ffmpeg spawn per MP3 source (via ManagedSound.LoadMp3File),
            // awaited one at a time. This is the path the review flagged as sitting
            // right at the 30 s dense-chart gate.
            var modifiers = new PlaybackModifiers(100, 0);
            var chipPaths = _mp3Sources
                .Select((path, index) => (WavId: (index + 1).ToString("D2"), Path: path))
                .ToDictionary(pair => pair.WavId, pair => pair.Path);

            var stopwatch = Stopwatch.StartNew();
            PreparedGameplayAudioSet prepared;
            try
            {
                prepared = await PreparedGameplayAudioSet.PrepareAsync(
                    mainBackgroundPath: null,
                    scheduledBgmSourcePaths: Array.Empty<string>(),
                    chipSourcePathsByWavId: chipPaths,
                    modifiers: modifiers,
                    processor: null,
                    cache: null,
                    progress: null,
                    CancellationToken.None,
                    decodedPcmBudgetBytes: 2L * 1024L * 1024L * 1024L);
            }
            finally
            {
                stopwatch.Stop();
            }

            using (prepared)
            {
                var coldMs = stopwatch.Elapsed.TotalMilliseconds;
                var decodedBytes = prepared.DecodedPcmBytes;

                WriteArtifact(
                    "cold-cache default profile (1.00x / 0 st) — serial ManagedSound loader",
                    DenseChipCount,
                    modifiers,
                    coldMs,
                    decodedBytes,
                    cacheHits: 0);

                // Plan acceptance budget: dense cold-cache completes within 30 s.
                Assert.True(
                    coldMs <= 30_000,
                    $"Cold-cache default-profile preparation took {coldMs:F0} ms, exceeding the 30 s budget. " +
                    "Fan out decode in bounded batches or prepare earlier in the transition stage.");
            }
        }

        [Fact]
        public async Task Benchmark_NonDefaultProfile_ColdThenWarm_MeasuresCacheEffect()
        {
            if (Environment.GetEnvironmentVariable("DTXMANIA_RUN_BENCHMARKS") != "1")
                return; // Opt-in benchmark: no-op unless DTXMANIA_RUN_BENCHMARKS=1.

            var modifiers = new PlaybackModifiers(75, 0);
            var chipPaths = _mp3Sources
                .Select((path, index) => (WavId: (index + 1).ToString("D2"), Path: path))
                .ToDictionary(pair => pair.WavId, pair => pair.Path);

            var processor = new FfmpegAudioVariantProcessor();
            var cache = new PlaybackAudioVariantCache(
                cacheRoot: Path.Combine(_scratchDir, "variant-cache"),
                maxCacheBytes: PlaybackAudioVariantCache.DefaultMaxCacheBytes);

            var coldStopwatch = Stopwatch.StartNew();
            PreparedGameplayAudioSet cold;
            try
            {
                cold = await PreparedGameplayAudioSet.PrepareAsync(
                    mainBackgroundPath: null,
                    scheduledBgmSourcePaths: Array.Empty<string>(),
                    chipSourcePathsByWavId: chipPaths,
                    modifiers: modifiers,
                    processor: processor,
                    cache: cache,
                    progress: null,
                    CancellationToken.None,
                    decodedPcmBudgetBytes: 2L * 1024L * 1024L * 1024L);
            }
            finally
            {
                coldStopwatch.Stop();
            }

            using (cold)
            {
                WriteArtifact(
                    "cold-cache non-default profile (0.75x / 0 st) — FfmpegAudioVariantProcessor",
                    DenseChipCount,
                    modifiers,
                    coldStopwatch.Elapsed.TotalMilliseconds,
                    cold.DecodedPcmBytes,
                    cacheHits: 0);
            }

            // Warm run: the variant cache should now serve every chip from disk.
            var warmStopwatch = Stopwatch.StartNew();
            PreparedGameplayAudioSet warm;
            int warmCacheHits = 0;
            try
            {
                var progress = new InlineProgress<AudioPreparationProgress>(p =>
                {
                    if (p.CacheHitCount > warmCacheHits)
                        warmCacheHits = p.CacheHitCount;
                });
                warm = await PreparedGameplayAudioSet.PrepareAsync(
                    mainBackgroundPath: null,
                    scheduledBgmSourcePaths: Array.Empty<string>(),
                    chipSourcePathsByWavId: chipPaths,
                    modifiers: modifiers,
                    processor: processor,
                    cache: cache,
                    progress: progress,
                    CancellationToken.None,
                    decodedPcmBudgetBytes: 2L * 1024L * 1024L * 1024L);
            }
            finally
            {
                warmStopwatch.Stop();
            }

            using (warm)
            {
                WriteArtifact(
                    "warm-cache non-default profile (0.75x / 0 st) — PlaybackAudioVariantCache hits",
                    DenseChipCount,
                    modifiers,
                    warmStopwatch.Elapsed.TotalMilliseconds,
                    warm.DecodedPcmBytes,
                    cacheHits: warmCacheHits);

                // Plan acceptance budget: warm-cache completes within 2 s.
                Assert.True(
                    warmStopwatch.Elapsed.TotalMilliseconds <= 2_000,
                    $"Warm-cache non-default preparation took {warmStopwatch.Elapsed.TotalMilliseconds:F0} ms, exceeding the 2 s budget.");
            }
        }

        public void Dispose()
        {
            if (!string.IsNullOrEmpty(_scratchDir) && Directory.Exists(_scratchDir))
            {
                try { Directory.Delete(_scratchDir, recursive: true); }
                catch { }
            }
        }

        private void WriteArtifact(
            string label,
            int chipCount,
            PlaybackModifiers modifiers,
            double elapsedMs,
            long decodedBytes,
            int cacheHits)
        {
            var path = Path.Combine(_artifactDirectory, "cold-warm-audio-prep.md");
            var entry = new StringBuilder()
                .AppendLine($"## {label}")
                .AppendLine()
                .AppendLine($"- Timestamp (UTC): {DateTime.UtcNow:O}")
                .AppendLine($"- Machine: {Environment.MachineName}")
                .AppendLine($"- OS: {Environment.OSVersion.VersionString}")
                .AppendLine($"- Chip count: {chipCount}")
                .AppendLine($"- Modifiers: PlaySpeed={modifiers.PlaySpeedPercent}% Pitch={modifiers.PitchSemitones}st")
                .AppendLine($"- Elapsed: {elapsedMs:F0} ms ({elapsedMs / 1000.0:F2} s)")
                .AppendLine($"- Decoded PCM: {decodedBytes:N0} bytes ({decodedBytes / (1024.0 * 1024.0):F1} MiB)")
                .AppendLine($"- Cache hits: {cacheHits}")
                .AppendLine()
                .ToString();

            File.AppendAllText(path, entry);
        }

        private static List<string> GenerateDenseMp3Sources(
            string directory,
            int count,
            TimeSpan duration)
        {
            var sources = new List<string>(count);
            for (var i = 0; i < count; i++)
            {
                var path = Path.Combine(directory, $"chip_{i:D3}.mp3");
                // Distinct sine frequency per chip so each file is genuinely unique.
                var frequency = 220 + (i % 24) * 40;
                var exitCode = RunFfmpeg(
                    $"-f lavfi -i \"sine=frequency={frequency}:duration={duration.TotalSeconds:F0}\" " +
                    $"-c:a libmp3lame -b:a 128k -y \"{path}\"",
                    waitForExit: true);
                if (exitCode != 0 || !File.Exists(path))
                {
                    throw new InvalidOperationException(
                        $"ffmpeg failed to generate benchmark source {path} (exit {exitCode}). " +
                        "Set DTXMANIA_RUN_BENCHMARKS=1 only on a machine with ffmpeg on PATH.");
                }
                sources.Add(Path.GetFullPath(path));
            }
            return sources;
        }

        private static int RunFfmpeg(string arguments, bool waitForExit)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            try
            {
                using var process = Process.Start(psi);
                if (process == null)
                    return -1;
                if (waitForExit)
                {
                    process.WaitForExit();
                    return process.ExitCode;
                }
                return 0;
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                return -1;
            }
        }

        private sealed class InlineProgress<T> : IProgress<T>
        {
            private readonly Action<T> _callback;
            public InlineProgress(Action<T> callback) => _callback = callback;
            public void Report(T value) => _callback(value);
        }
    }
}
