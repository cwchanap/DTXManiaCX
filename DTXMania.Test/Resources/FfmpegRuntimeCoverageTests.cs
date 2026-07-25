#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Stage.Performance;
using Moq;
using Xunit;
namespace DTXMania.Test.Resources
{
    [Trait("Category", "Unit")]
    public sealed class FfmpegRuntimeCoverageTests
    {
        private static string BinaryName(string command) =>
            OperatingSystem.IsWindows() ? $"{command}.exe" : command;

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
