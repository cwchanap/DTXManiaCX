#nullable enable

using System;
using System.IO;
using System.Linq;
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
        public async Task GetOrCreateWithStatusAsync_ColdThenWarm_ShouldReportCacheStatus()
        {
            var cache = CreateCache();
            var source = WriteSource("status.wav");
            var key = await AudioVariantKey.CreateAsync(
                source,
                new PlaybackModifiers(75, 0));
            var calls = 0;

            Task<PreparedAudioArtifact> Factory(
                AudioVariantKey _,
                CancellationToken __)
            {
                calls++;
                return Task.FromResult(CreateArtifact(2));
            }

            var cold = await cache.GetOrCreateWithStatusAsync(
                key,
                Factory,
                CancellationToken.None);
            var warm = await cache.GetOrCreateWithStatusAsync(
                key,
                Factory,
                CancellationToken.None);

            Assert.False(cold.CacheHit);
            Assert.True(warm.CacheHit);
            Assert.Equal(1, calls);
            Assert.Equal(cold.Artifact.PcmData.ToArray(), warm.Artifact.PcmData.ToArray());
        }

        [Fact]
        public async Task GetOrCreateWithStatusAsync_NullFactoryResult_ShouldNotPublishArtifact()
        {
            var cache = CreateCache();
            var source = WriteSource("null.wav");
            var key = await AudioVariantKey.CreateAsync(
                source,
                new PlaybackModifiers(75, 0));

            await Assert.ThrowsAsync<InvalidDataException>(() =>
                cache.GetOrCreateWithStatusAsync(
                    key,
                    (_, _) => Task.FromResult<PreparedAudioArtifact>(null!),
                    CancellationToken.None));

            Assert.False(File.Exists(cache.GetArtifactPath(key)));
        }

        [Fact]
        public async Task TryGetAsync_MissingArtifact_ShouldReturnNull()
        {
            var cache = CreateCache();
            var source = WriteSource("missing.wav");
            var key = await AudioVariantKey.CreateAsync(
                source,
                new PlaybackModifiers(75, 0));

            Assert.Null(await cache.TryGetAsync(key));
        }

        [Fact]
        public void StartupCleanup_ShouldDeleteOrphanedTemporaryFiles()
        {
            var root = CacheRoot("cleanup");
            Directory.CreateDirectory(root);
            var orphan = Path.Combine(root, "artifact.dtxpcm.tmp-deadbeef");
            File.WriteAllBytes(orphan, new byte[] { 1 });

            _ = new PlaybackAudioVariantCache(root, 1024 * 1024);

            Assert.False(File.Exists(orphan));
        }

        [Fact]
        public async Task Prune_WhenUnderBudget_ShouldKeepPublishedArtifact()
        {
            var cache = CreateCache();
            var source = WriteSource("prune.wav");
            var key = await AudioVariantKey.CreateAsync(
                source,
                new PlaybackModifiers(75, 0));
            await cache.GetOrCreateAsync(
                key,
                (_, _) => Task.FromResult(CreateArtifact(4)),
                CancellationToken.None);

            cache.Prune();

            Assert.True(File.Exists(cache.GetArtifactPath(key)));
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
