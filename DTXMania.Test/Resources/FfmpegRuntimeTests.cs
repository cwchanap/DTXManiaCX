#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using DTXMania.Game.Lib.Resources;
using Xunit;

namespace DTXMania.Test.Resources
{
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
    }
}