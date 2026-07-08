using System;
using System.Collections.Generic;
using System.IO;
using DTXMania.Game.Lib.Resources;
using Xunit;

namespace DTXMania.Test.Resources
{
    /// <summary>
    /// Unit tests for <see cref="ManagedSound.GetFFmpegBinaryFolder"/> — the
    /// bundled ffmpeg directory resolver. These guard the macOS release path:
    /// the workflow ships a native arm64 ffmpeg to runtimes/osx-arm64/MMTools,
    /// so the resolver MUST look there and MUST prefer it over the x64 Rosetta
    /// fallback. These tests are pure logic (no files, no audio device) and do
    /// not carry the Audio trait.
    /// </summary>
    public class ManagedSoundFFmpegPathTests
    {
        /// <summary>
        /// The binary name the resolver probes for on the test host OS. The
        /// resolver computes this internally via OperatingSystem.IsWindows(),
        /// so tests replicate the same one-liner to build expected paths.
        /// </summary>
        private static string BinName => OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";

        private static string FFmpegPath(string folder) => Path.Combine(folder, BinName);

        [Fact]
        public void GetFFmpegBinaryFolder_WithArm64Present_PrefersArm64OverX64()
        {
            var dir = "/app";
            var arm64 = Path.Combine(dir, "runtimes", "osx-arm64", "MMTools");
            var x64 = Path.Combine(dir, "runtimes", "osx-x64", "MMTools");
            var existing = new HashSet<string>(StringComparer.Ordinal)
            {
                FFmpegPath(arm64),
                FFmpegPath(x64),
            };

            var result = ManagedSound.GetFFmpegBinaryFolder(dir, p => existing.Contains(p));

            Assert.Equal(arm64, result);
        }

        [Fact]
        public void GetFFmpegBinaryFolder_WithOnlyX64Present_ReturnsX64()
        {
            var dir = "/app";
            var x64 = Path.Combine(dir, "runtimes", "osx-x64", "MMTools");
            var existing = new HashSet<string>(StringComparer.Ordinal) { FFmpegPath(x64) };

            var result = ManagedSound.GetFFmpegBinaryFolder(dir, p => existing.Contains(p));

            Assert.Equal(x64, result);
        }

        [Fact]
        public void GetFFmpegBinaryFolder_WithNoBinariesPresent_ReturnsNull()
        {
            var dir = "/app";

            var result = ManagedSound.GetFFmpegBinaryFolder(dir, _ => false);

            Assert.Null(result);
        }

        [Fact]
        public void GetFFmpegBinaryFolder_WithNullOrEmptyAssemblyDir_ReturnsNull()
        {
            Assert.Null(ManagedSound.GetFFmpegBinaryFolder(null, _ => true));
            Assert.Null(ManagedSound.GetFFmpegBinaryFolder(string.Empty, _ => true));
        }

        [Fact]
        public void GetFFmpegBinaryFolder_WithNullPredicate_ReturnsNull()
        {
            var result = ManagedSound.GetFFmpegBinaryFolder("/app", null);

            Assert.Null(result);
        }

        /// <summary>
        /// Regression guard for the macOS release workflow. The workflow copies a
        /// native arm64 ffmpeg to runtimes/osx-arm64/MMTools; the resolver MUST
        /// probe that candidate, and MUST probe it BEFORE the osx-x64 Rosetta
        /// fallback. This test fails against the pre-fix resolver, which never
        /// looked at osx-arm64 at all.
        /// </summary>
        [Fact]
        public void GetFFmpegBinaryFolder_ProbesArm64CandidateBeforeX64()
        {
            var dir = "/app";
            var probed = new List<string>();
            var arm64 = FFmpegPath(Path.Combine(dir, "runtimes", "osx-arm64", "MMTools"));
            var x64 = FFmpegPath(Path.Combine(dir, "runtimes", "osx-x64", "MMTools"));

            ManagedSound.GetFFmpegBinaryFolder(dir, p =>
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
