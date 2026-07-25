#nullable enable

using System;
using System.IO;
using DTXMania.Game.Lib.Resources;
using Xunit;

namespace DTXMania.Test.Resources
{
    [Collection("AppPaths")]
    [Trait("Category", "Unit")]
    public sealed class PlaybackAudioVariantCacheCoverageTests : IDisposable
    {
        private readonly string _tempDirectory = Path.Combine(
            Path.GetTempPath(),
            "dtxmania-cache-coverage-" + Guid.NewGuid().ToString("N"));

        public PlaybackAudioVariantCacheCoverageTests()
        {
            Directory.CreateDirectory(_tempDirectory);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDirectory))
                Directory.Delete(_tempDirectory, recursive: true);
        }

        [Fact]
        public void Constructor_NonPositiveCap_ShouldThrow()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new PlaybackAudioVariantCache(CacheRoot("zero"), 0));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new PlaybackAudioVariantCache(CacheRoot("negative"), -1));
        }

        private string CacheRoot(string name) =>
            Path.Combine(_tempDirectory, name);
    }
}
