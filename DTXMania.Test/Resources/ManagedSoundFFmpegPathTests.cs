using System;
using System.Collections.Generic;
using System.IO;
using DTXMania.Game.Lib.Resources;
using Xunit;

namespace DTXMania.Test.Resources
{
    /// <summary>
    /// Unit tests for <see cref="FfmpegRuntime.GetFFmpegBinaryFolder"/> — the
    /// bundled ffmpeg directory resolver. These guard the macOS release path:
    /// the workflow ships a native arm64 ffmpeg to runtimes/osx-arm64/MMTools,
    /// so the resolver MUST look there and MUST prefer it over the x64 Rosetta
    /// fallback. These tests are pure logic (no files, no audio device) and do
    /// not carry the Audio trait.
    /// </summary>
    [Trait("Category", "Unit")]
    public class ManagedSoundFFmpegPathTests
    {
        private static string BinaryPath(string folder, string command) =>
            Path.Combine(folder, OperatingSystem.IsWindows() ? $"{command}.exe" : command);

        private static void AddRuntimePair(HashSet<string> existing, string folder)
        {
            existing.Add(BinaryPath(folder, "ffmpeg"));
            existing.Add(BinaryPath(folder, "ffprobe"));
        }

        [Fact]
        public void WithArm64Present_ShouldPreferArm64OverX64()
        {
            var dir = "/app";
            var arm64 = Path.Combine(dir, "runtimes", "osx-arm64", "MMTools");
            var x64 = Path.Combine(dir, "runtimes", "osx-x64", "MMTools");
            var existing = new HashSet<string>(StringComparer.Ordinal);
            AddRuntimePair(existing, arm64);
            AddRuntimePair(existing, x64);

            var result = FfmpegRuntime.GetFFmpegBinaryFolder(dir, p => existing.Contains(p));

            Assert.Equal(arm64, result);
        }

        [Fact]
        public void WithOnlyX64Present_ShouldReturnX64()
        {
            var dir = "/app";
            var x64 = Path.Combine(dir, "runtimes", "osx-x64", "MMTools");
            var existing = new HashSet<string>(StringComparer.Ordinal);
            AddRuntimePair(existing, x64);

            var result = FfmpegRuntime.GetFFmpegBinaryFolder(dir, p => existing.Contains(p));

            Assert.Equal(x64, result);
        }

        [Fact]
        public void WithNoBinariesPresent_ShouldReturnNull()
        {
            var dir = "/app";

            var result = FfmpegRuntime.GetFFmpegBinaryFolder(dir, _ => false);

            Assert.Null(result);
        }

        [Fact]
        public void WithNullOrEmptyAssemblyDir_ShouldReturnNull()
        {
            Assert.Null(FfmpegRuntime.GetFFmpegBinaryFolder(null, _ => true));
            Assert.Null(FfmpegRuntime.GetFFmpegBinaryFolder(string.Empty, _ => true));
        }

        [Fact]
        public void WithNullPredicate_ShouldReturnNull()
        {
            var result = FfmpegRuntime.GetFFmpegBinaryFolder("/app", null);

            Assert.Null(result);
        }

        [Fact]
        public void WithOnlyFFmpegPresent_ShouldSkipIncompleteCandidate()
        {
            var dir = "/app";
            var arm64 = Path.Combine(dir, "runtimes", "osx-arm64", "MMTools");
            var x64 = Path.Combine(dir, "runtimes", "osx-x64", "MMTools");
            var existing = new HashSet<string>(StringComparer.Ordinal)
            {
                BinaryPath(arm64, "ffmpeg"),
            };
            AddRuntimePair(existing, x64);

            var result = FfmpegRuntime.GetFFmpegBinaryFolder(dir, existing.Contains);

            Assert.Equal(x64, result);
        }

        /// <summary>
        /// Regression guard for the macOS release workflow. The workflow copies a
        /// native arm64 ffmpeg to runtimes/osx-arm64/MMTools; the resolver MUST
        /// probe that candidate, and MUST probe it BEFORE the osx-x64 Rosetta
        /// fallback. This test fails against the pre-fix resolver, which never
        /// looked at osx-arm64 at all.
        /// </summary>
        [Fact]
        public void ProbesArm64CandidateBeforeX64_ShouldProbeArm64First()
        {
            var dir = "/app";
            var probed = new List<string>();
            var arm64 = BinaryPath(Path.Combine(dir, "runtimes", "osx-arm64", "MMTools"), "ffmpeg");
            var x64 = BinaryPath(Path.Combine(dir, "runtimes", "osx-x64", "MMTools"), "ffmpeg");

            FfmpegRuntime.GetFFmpegBinaryFolder(dir, p =>
            {
                probed.Add(p);
                return false; // force the resolver to walk the whole candidate list
            });

            Assert.Contains(arm64, probed);
            Assert.True(
                probed.IndexOf(arm64) < probed.IndexOf(x64),
                "arm64 candidate must be probed before the x64 Rosetta fallback");
        }
    }
}
