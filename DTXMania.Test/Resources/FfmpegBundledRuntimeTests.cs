using System;
using System.IO;
using System.Runtime.InteropServices;
using DTXMania.Game.Lib.Resources;
using DTXMania.Test.Utilities;
using Xunit;

namespace DTXMania.Test.Resources
{
    [Trait("Category", AudioTestUtils.AudioTestCategory)]
    public class FfmpegBundledRuntimeTests
    {
        [Fact]
        public void EnsureConfigured_OnNativeAppleSilicon_ShouldUseBundledExecutableRuntime()
        {
            if (!OperatingSystem.IsMacOS() ||
                RuntimeInformation.ProcessArchitecture != Architecture.Arm64)
            {
                return;
            }

            var result = FfmpegRuntime.EnsureConfigured();
            Assert.True(result.IsAvailable, result.DiagnosticReason);
            Assert.NotNull(result.BinaryFolder);

            var expected = Path.Combine(
                AppContext.BaseDirectory,
                "runtimes",
                "osx-arm64",
                "MMTools");

            Assert.Equal(
                Path.GetFullPath(expected),
                Path.GetFullPath(result.BinaryFolder!));

            AssertExecutable(Path.Combine(result.BinaryFolder!, "ffmpeg"));
            AssertExecutable(Path.Combine(result.BinaryFolder!, "ffprobe"));
        }

        private static void AssertExecutable(string path)
        {
            Assert.True(File.Exists(path), $"Missing bundled executable: {path}");

            var executeBits = UnixFileMode.UserExecute |
                UnixFileMode.GroupExecute |
                UnixFileMode.OtherExecute;
            Assert.True(
                (File.GetUnixFileMode(path) & executeBits) != 0,
                $"Bundled executable has no Unix execute bit: {path}");
        }
    }
}
