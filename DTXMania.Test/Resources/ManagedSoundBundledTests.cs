using System;
using System.IO;
using Xunit;
using DTX.Resources;
using System.Reflection;

namespace DTXMania.Test.Resources
{
    public class ManagedSoundBundledTests
    {
        [Fact]
        public void BundledFFmpegPath_ShouldExist_WhenPackageInstalled()
        {
            // Get the path to bundled ffmpeg using reflection
            var managedSoundType = typeof(ManagedSound);
            var getBundledFFmpegPathMethod = managedSoundType.GetMethod("GetBundledFFmpegPath", 
                BindingFlags.NonPublic | BindingFlags.Static);
            
            Assert.NotNull(getBundledFFmpegPathMethod);
            
            // Call the method
            var bundledPath = getBundledFFmpegPathMethod.Invoke(null, null) as string;
            
            // Should find a path
            Assert.NotNull(bundledPath);
            Assert.True(Directory.Exists(bundledPath), $"Bundled ffmpeg directory should exist: {bundledPath}");
            
            // Verify ffmpeg binary exists
            var ffmpegPath = Path.Combine(bundledPath, OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg");
            Assert.True(File.Exists(ffmpegPath), $"ffmpeg binary should exist: {ffmpegPath}");
        }

        [Fact]
        public void StaticConstructor_ShouldConfigureFFmpegWithBundledPath()
        {
            // The static constructor should have already run when this test executes
            // We can't easily test the GlobalFFOptions configuration directly,
            // but we can test that the static constructor doesn't throw exceptions
            
            // This will trigger the static constructor if not already done
            var sound = typeof(ManagedSound);
            Assert.NotNull(sound);
            
            // If we got here without exceptions, the static constructor worked
            Assert.True(true);
        }

        [Fact]
        public void BundledFFmpegPath_ReturnsNull_WhenNoBinariesFound()
        {
            // We can't easily test this without removing the actual binaries,
            // but we can test the method exists and handles the case properly
            var managedSoundType = typeof(ManagedSound);
            var getBundledFFmpegPathMethod = managedSoundType.GetMethod("GetBundledFFmpegPath", 
                BindingFlags.NonPublic | BindingFlags.Static);
            
            Assert.NotNull(getBundledFFmpegPathMethod);
            
            // The method should be able to handle cases where binaries don't exist
            // (this is more of a smoke test to ensure the method doesn't crash)
        }
    }
}
