#nullable enable

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Stage.Performance;
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

        [Fact]
        public async Task PublicKeyApis_NullArguments_ShouldThrow()
        {
            var cache = CreateCache();
            var source = WriteSource("key.wav");
            var key = await AudioVariantKey.CreateAsync(
                source,
                new PlaybackModifiers(75, 0));

            Assert.Throws<ArgumentNullException>(() => cache.GetArtifactPath(null!));
            Assert.Throws<ArgumentNullException>(() => cache.TryGetAsync(null!));
            await Assert.ThrowsAsync<ArgumentNullException>(() =>
                cache.GetOrCreateWithStatusAsync(
                    null!,
                    (_, _) => Task.FromResult(CreateArtifact(1)),
                    CancellationToken.None));
            await Assert.ThrowsAsync<ArgumentNullException>(() =>
                cache.GetOrCreateWithStatusAsync(
                    key,
                    null!,
                    CancellationToken.None));
        }

        private PlaybackAudioVariantCache CreateCache() =>
            new(CacheRoot("cache"), 1024 * 1024);

        private string CacheRoot(string name) =>
            Path.Combine(_tempDirectory, name);

        private string WriteSource(string name)
        {
            var path = Path.Combine(_tempDirectory, name);
            File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
            return path;
        }

        private static PreparedAudioArtifact CreateArtifact(byte marker) =>
            new(44100, 1, new byte[] { marker, 0 });
    }
}
