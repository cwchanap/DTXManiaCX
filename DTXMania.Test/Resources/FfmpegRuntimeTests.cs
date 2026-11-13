#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using DTXMania.Game.Lib.Resources;
using Xunit;

namespace DTXMania.Test.Resources
{
    [Collection("FfmpegRuntimeState")]
    [Trait("Category", "Unit")]
    public class FfmpegRuntimeTests
    {
        private static string BinaryName(string command) =>
            OperatingSystem.IsWindows() ? $"{command}.exe" : command;

        [Fact]
        public void ProbePathAvailability_WithBothBinaries_ShouldBeAvailable()
        {
            var first = Path.Combine(Path.GetTempPath(), "ffmpeg-runtime-first");
            var second = Path.Combine(Path.GetTempPath(), "ffmpeg-runtime-second");
            var existing = new HashSet<string>(StringComparer.Ordinal)
            {
                Path.Combine(first, BinaryName("ffmpeg")),
                Path.Combine(second, BinaryName("ffprobe")),
            };
            var path = string.Join(Path.PathSeparator, first, second);

            var result = FfmpegRuntime.ProbePathAvailability(
                path,
                existing.Contains);

            Assert.True(result.IsAvailable);
            Assert.Null(result.DiagnosticReason);
            Assert.Null(result.BinaryFolder);
        }

        [Fact]
        public void ProbePathAvailability_WithMissingFFprobe_ShouldReturnDiagnostic()
        {
            var folder = Path.Combine(Path.GetTempPath(), "ffmpeg-runtime");
            var ffmpeg = Path.Combine(folder, BinaryName("ffmpeg"));

            var result = FfmpegRuntime.ProbePathAvailability(
                folder,
                path => path == ffmpeg);

            Assert.False(result.IsAvailable);
            Assert.Contains(BinaryName("ffprobe"), result.DiagnosticReason);
            Assert.Null(result.BinaryFolder);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void ProbePathAvailability_WithMissingPath_ShouldReturnDiagnostic(string? path)
        {
            var result = FfmpegRuntime.ProbePathAvailability(path, _ => false);

            Assert.False(result.IsAvailable);
            Assert.Contains(BinaryName("ffmpeg"), result.DiagnosticReason);
            Assert.Contains(BinaryName("ffprobe"), result.DiagnosticReason);
        }

        [Fact]
        public void EnsureConfigured_ShouldBeThreadSafeAndIdempotent()
        {
            FfmpegRuntimeAvailability? first = null;
            FfmpegRuntimeAvailability? second = null;

            var exception = Record.Exception(() =>
            {
                first = FfmpegRuntime.EnsureConfigured();
                second = FfmpegRuntime.EnsureConfigured();
            });

            Assert.Null(exception);
            Assert.NotNull(first);
            Assert.Same(first, second);
        }

        [Fact]
        public void TryGetAvailability_ShouldMatchConfiguredResult()
        {
            var configured = FfmpegRuntime.EnsureConfigured();

            var isAvailable = FfmpegRuntime.TryGetAvailability(out var diagnostic);

            Assert.Equal(configured.IsAvailable, isAvailable);
            Assert.Equal(configured.DiagnosticReason, diagnostic);
        }

        [Fact]
        public void GetFFmpegBinaryFolder_InvalidInputs_ShouldReturnNull()
        {
            Assert.Null(FfmpegRuntime.GetFFmpegBinaryFolder(null, _ => true));
            Assert.Null(FfmpegRuntime.GetFFmpegBinaryFolder("   ", _ => true));
            Assert.Null(FfmpegRuntime.GetFFmpegBinaryFolder(Path.GetTempPath(), null));
        }

        [Fact]
        public void GetFFmpegBinaryFolder_ShouldPreferFirstCompleteCandidate()
        {
            var assemblyDirectory = Path.Combine(Path.GetTempPath(), "ffmpeg-bundled");
            var preferred = Path.Combine(
                assemblyDirectory,
                "runtimes",
                "osx-x64",
                "MMTools");
            var later = Path.Combine(
                assemblyDirectory,
                "runtimes",
                "win-x64",
                "MMTools");
            var existing = new HashSet<string>(StringComparer.Ordinal)
            {
                Path.Combine(preferred, BinaryName("ffmpeg")),
                Path.Combine(preferred, BinaryName("ffprobe")),
                Path.Combine(later, BinaryName("ffmpeg")),
                Path.Combine(later, BinaryName("ffprobe")),
            };

            var result = FfmpegRuntime.GetFFmpegBinaryFolder(
                assemblyDirectory,
                existing.Contains);

            Assert.Equal(preferred, result);
        }

        [Fact]
        public void GetFFmpegBinaryFolder_BinariesInDifferentFolders_ShouldReturnNull()
        {
            var assemblyDirectory = Path.Combine(Path.GetTempPath(), "ffmpeg-split");
            var arm64 = Path.Combine(
                assemblyDirectory,
                "runtimes",
                "osx-arm64",
                "MMTools");
            var x64 = Path.Combine(
                assemblyDirectory,
                "runtimes",
                "osx-x64",
                "MMTools");
            var existing = new HashSet<string>(StringComparer.Ordinal)
            {
                Path.Combine(arm64, BinaryName("ffmpeg")),
                Path.Combine(x64, BinaryName("ffprobe")),
            };

            Assert.Null(FfmpegRuntime.GetFFmpegBinaryFolder(
                assemblyDirectory,
                existing.Contains));
        }

        [Fact]
        public void ProbePathAvailability_MissingFFmpeg_ShouldNameOnlyMissingBinary()
        {
            var folder = Path.Combine(Path.GetTempPath(), "ffmpeg-probe-only");
            var ffprobe = Path.Combine(folder, BinaryName("ffprobe"));

            var result = FfmpegRuntime.ProbePathAvailability(
                folder,
                path => path == ffprobe);

            Assert.False(result.IsAvailable);
            Assert.Contains(BinaryName("ffmpeg"), result.DiagnosticReason);
            Assert.DoesNotContain(BinaryName("ffprobe"), result.DiagnosticReason);
        }

        [Fact]
        public void ProbePathAvailability_NullPredicate_ShouldThrow()
        {
            Assert.Throws<ArgumentNullException>(() =>
                FfmpegRuntime.ProbePathAvailability(Path.GetTempPath(), null!));
        }

        [Fact]
        public void ProbePathAvailability_InvalidEntryException_ShouldBeIgnored()
        {
            var bad = Path.Combine(Path.GetTempPath(), "bad-entry");
            var good = Path.Combine(Path.GetTempPath(), "good-entry");
            var pathValue = string.Join(Path.PathSeparator, bad, good);

            bool Exists(string candidate)
            {
                if (candidate.StartsWith(bad, StringComparison.Ordinal))
                    throw new ArgumentException("invalid path entry");
                return candidate == Path.Combine(good, BinaryName("ffmpeg")) ||
                    candidate == Path.Combine(good, BinaryName("ffprobe"));
            }

            var result = FfmpegRuntime.ProbePathAvailability(pathValue, Exists);

            Assert.True(result.IsAvailable);
        }
    }
}