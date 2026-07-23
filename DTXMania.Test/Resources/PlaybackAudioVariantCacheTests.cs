using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Stage.Performance;
using DTXMania.Game.Lib.Utilities;
using Xunit;

namespace DTXMania.Test.Resources
{
    [Collection("AppPaths")]
    public sealed class PlaybackAudioVariantCacheTests : IDisposable
    {
        private readonly string _tempDirectory = Path.Combine(
            Path.GetTempPath(),
            "dtxmania-audio-cache-" + Guid.NewGuid().ToString("N"));

        public PlaybackAudioVariantCacheTests()
        {
            Directory.CreateDirectory(_tempDirectory);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDirectory))
                Directory.Delete(_tempDirectory, recursive: true);
        }

        [Fact]
        public async Task AudioVariantKey_SameContentAndCanonicalExtensionProducesSameKey()
        {
            var lowerPath = WriteSource("first.wav", new byte[] { 1, 2, 3 });
            var upperPath = WriteSource("second.WAV", new byte[] { 1, 2, 3 });
            var modifiers = new PlaybackModifiers(125, -3);

            var first = await AudioVariantKey.CreateAsync(lowerPath, modifiers);
            var second = await AudioVariantKey.CreateAsync(upperPath, modifiers);

            Assert.Equal(first, second);
            Assert.Equal("ffmpeg:wav", first.DecoderIdentity);
            Assert.EndsWith(PreparedAudioArtifact.FileExtension, first.ToCacheFileName());
        }

        [Fact]
        public async Task AudioVariantKey_SourceContentChangeInvalidatesKey()
        {
            var sourcePath = WriteSource("source.wav", new byte[] { 1, 2, 3 });
            var modifiers = new PlaybackModifiers(100, 2);
            var first = await AudioVariantKey.CreateAsync(sourcePath, modifiers);

            await File.WriteAllBytesAsync(sourcePath, new byte[] { 1, 2, 4 });
            var second = await AudioVariantKey.CreateAsync(sourcePath, modifiers);

            Assert.NotEqual(first, second);
        }

        [Fact]
        public async Task AudioVariantKey_PipelineVersionInvalidatesKey()
        {
            var sourcePath = WriteSource("source.ogg", new byte[] { 1, 2, 3 });
            var modifiers = new PlaybackModifiers(80, 0);

            var first = await AudioVariantKey.CreateAsync(sourcePath, modifiers, pipelineVersion: 1);
            var second = await AudioVariantKey.CreateAsync(sourcePath, modifiers, pipelineVersion: 2);

            Assert.NotEqual(first, second);
            Assert.NotEqual(first.ToCacheFileName(), second.ToCacheFileName());
        }

        [Fact]
        public async Task GetOrCreateAsync_CacheHitDoesNotInvokeFactoryAgain()
        {
            var cache = CreateCache();
            var sourcePath = WriteSource("source.wav", new byte[] { 1, 2, 3 });
            var calls = 0;

            Task<PreparedAudioArtifact> Factory(AudioVariantKey _, CancellationToken __)
            {
                calls++;
                return Task.FromResult(CreateArtifact(0x10));
            }

            var first = await cache.GetOrCreateAsync(
                sourcePath, new PlaybackModifiers(75, 0), Factory, CancellationToken.None);
            var second = await cache.GetOrCreateAsync(
                sourcePath, new PlaybackModifiers(75, 0), Factory, CancellationToken.None);

            Assert.Equal(1, calls);
            Assert.Equal(first.PcmData.ToArray(), second.PcmData.ToArray());
        }

        [Fact]
        public async Task GetOrCreateAsync_ConcurrentDuplicatesShareOneFactoryTask()
        {
            var cache = CreateCache();
            var sourcePath = WriteSource("source.mp3", new byte[] { 1, 2, 3 });
            var release = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var calls = 0;

            async Task<PreparedAudioArtifact> Factory(
                AudioVariantKey _,
                CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref calls);
                await release.Task.WaitAsync(cancellationToken);
                return CreateArtifact(0x20);
            }

            var first = cache.GetOrCreateAsync(
                sourcePath, new PlaybackModifiers(110, 1), Factory, CancellationToken.None);
            var second = cache.GetOrCreateAsync(
                sourcePath, new PlaybackModifiers(110, 1), Factory, CancellationToken.None);
            release.SetResult(true);

            await Task.WhenAll(first, second);

            Assert.Equal(1, calls);
        }

        [Fact]
        public async Task GetOrCreateAsync_FirstDuplicateCancels_SecondWaiterStillCompletes()
        {
            var cache = CreateCache();
            var sourcePath = WriteSource(
                "first-cancels.mp3",
                new byte[] { 1, 2, 3 });
            var modifiers = new PlaybackModifiers(110, 1);
            var started = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var calls = 0;
            using var firstCancellation = new CancellationTokenSource();

            async Task<PreparedAudioArtifact> Factory(
                AudioVariantKey _,
                CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref calls);
                started.TrySetResult(true);
                await release.Task.WaitAsync(cancellationToken);
                return CreateArtifact(0x21);
            }

            var first = cache.GetOrCreateAsync(
                sourcePath,
                modifiers,
                Factory,
                firstCancellation.Token);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(1));
            var second = cache.GetOrCreateAsync(
                sourcePath,
                modifiers,
                Factory,
                CancellationToken.None);

            firstCancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => first.WaitAsync(TimeSpan.FromSeconds(1)));
            Assert.False(second.IsCompleted);

            release.SetResult(true);
            var artifact = await second.WaitAsync(TimeSpan.FromSeconds(1));

            Assert.Equal(1, calls);
            Assert.Equal(0x21, artifact.PcmData.Span[0]);
        }

        [Fact]
        public async Task GetOrCreateAsync_LaterDuplicateCancels_ReturnsPromptlyWithoutCancelingFirst()
        {
            var cache = CreateCache();
            var sourcePath = WriteSource(
                "later-cancels.mp3",
                new byte[] { 1, 2, 3 });
            var modifiers = new PlaybackModifiers(110, 1);
            var started = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var calls = 0;
            using var laterCancellation = new CancellationTokenSource();

            async Task<PreparedAudioArtifact> Factory(
                AudioVariantKey _,
                CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref calls);
                started.TrySetResult(true);
                await release.Task.WaitAsync(cancellationToken);
                return CreateArtifact(0x22);
            }

            var first = cache.GetOrCreateAsync(
                sourcePath,
                modifiers,
                Factory,
                CancellationToken.None);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(1));
            var later = cache.GetOrCreateAsync(
                sourcePath,
                modifiers,
                Factory,
                laterCancellation.Token);

            laterCancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => later.WaitAsync(TimeSpan.FromSeconds(1)));
            Assert.False(first.IsCompleted);

            release.SetResult(true);
            var artifact = await first.WaitAsync(TimeSpan.FromSeconds(1));

            Assert.Equal(1, calls);
            Assert.Equal(0x22, artifact.PcmData.Span[0]);
        }

        [Fact]
        public async Task GetOrCreateAsync_OnlyWaiterCancels_CancelsSharedFactory()
        {
            var cache = CreateCache();
            var sourcePath = WriteSource(
                "only-waiter-cancels.mp3",
                new byte[] { 1, 2, 3 });
            var modifiers = new PlaybackModifiers(110, 1);
            var started = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var factoryCanceled = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            using var cancellation = new CancellationTokenSource();

            async Task<PreparedAudioArtifact> Factory(
                AudioVariantKey _,
                CancellationToken cancellationToken)
            {
                started.TrySetResult(true);
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    factoryCanceled.TrySetResult(true);
                    throw;
                }

                return CreateArtifact(0x23);
            }

            var request = cache.GetOrCreateAsync(
                sourcePath,
                modifiers,
                Factory,
                cancellation.Token);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(1));

            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => request.WaitAsync(TimeSpan.FromSeconds(1)));
            await factoryCanceled.Task.WaitAsync(TimeSpan.FromSeconds(1));
            Assert.Empty(Directory.GetFiles(cache.CacheRoot));
        }

        [Fact]
        public async Task GetOrCreateAsync_CorruptHitIsDeletedAndRegenerated()
        {
            var cache = CreateCache();
            var sourcePath = WriteSource("source.wav", new byte[] { 4, 5, 6 });
            var modifiers = new PlaybackModifiers(90, 0);
            var calls = 0;

            Task<PreparedAudioArtifact> Factory(AudioVariantKey _, CancellationToken __)
            {
                calls++;
                return Task.FromResult(CreateArtifact((byte)calls));
            }

            await cache.GetOrCreateAsync(sourcePath, modifiers, Factory, CancellationToken.None);
            var key = await AudioVariantKey.CreateAsync(sourcePath, modifiers);
            await File.WriteAllBytesAsync(cache.GetArtifactPath(key), new byte[] { 1, 2, 3 });

            var regenerated = await cache.GetOrCreateAsync(
                sourcePath, modifiers, Factory, CancellationToken.None);

            Assert.Equal(2, calls);
            Assert.Equal(2, regenerated.PcmData.Span[0]);
        }

        [Fact]
        public async Task GetOrCreateAsync_FactoryFailureLeavesNoPartialArtifact()
        {
            var cache = CreateCache();
            var sourcePath = WriteSource("broken.wav", new byte[] { 1 });

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => cache.GetOrCreateAsync(
                    sourcePath,
                    new PlaybackModifiers(50, 0),
                    (_, _) => throw new InvalidOperationException("failed"),
                    CancellationToken.None));

            Assert.Empty(Directory.GetFiles(cache.CacheRoot));
        }

        [Fact]
        public async Task StartupCleanup_PrunesLeastRecentlyUsedFilesToConfiguredCap()
        {
            var cache = CreateCache();
            var paths = new string[3];
            for (var index = 0; index < paths.Length; index++)
            {
                var sourcePath = WriteSource($"source-{index}.wav", new byte[] { (byte)index });
                var modifiers = new PlaybackModifiers(100 + index * 5, 0);
                await cache.GetOrCreateAsync(
                    sourcePath,
                    modifiers,
                    (_, _) => Task.FromResult(CreateArtifact((byte)index)),
                    CancellationToken.None);
                var key = await AudioVariantKey.CreateAsync(sourcePath, modifiers);
                paths[index] = cache.GetArtifactPath(key);
                File.SetLastAccessTimeUtc(paths[index], DateTime.UtcNow.AddMinutes(index - 10));
            }

            var artifactLength = new FileInfo(paths[0]).Length;
            _ = new PlaybackAudioVariantCache(
                cache.CacheRoot,
                maxCacheBytes: artifactLength * 2);

            Assert.False(File.Exists(paths[0]));
            Assert.True(File.Exists(paths[1]));
            Assert.True(File.Exists(paths[2]));
        }

        [Fact]
        public async Task SuccessfulWrite_PrunesCacheToConfiguredCap()
        {
            const long twoArtifactCap = 60;
            var cache = new PlaybackAudioVariantCache(
                Path.Combine(_tempDirectory, "write-prune-cache"),
                maxCacheBytes: twoArtifactCap);

            for (var index = 0; index < 3; index++)
            {
                var sourcePath = WriteSource(
                    $"write-prune-{index}.wav",
                    new byte[] { (byte)index });
                await cache.GetOrCreateAsync(
                    sourcePath,
                    new PlaybackModifiers(100 + index * 5, 0),
                    (_, _) => Task.FromResult(CreateArtifact((byte)index)),
                    CancellationToken.None);
            }

            var files = Directory.GetFiles(
                cache.CacheRoot,
                "*" + PreparedAudioArtifact.FileExtension);
            Assert.Equal(2, files.Length);
            Assert.True(files.Sum(path => new FileInfo(path).Length) <= twoArtifactCap);
        }

        [Fact]
        public void AppPaths_PlaybackAudioCacheRootLivesBelowAppDataCacheDirectory()
        {
            const string variable = "DTXMANIA_APPDATA_ROOT";
            var previous = Environment.GetEnvironmentVariable(variable);
            try
            {
                Environment.SetEnvironmentVariable(variable, _tempDirectory);

                var path = AppPaths.GetPlaybackAudioCacheRoot();

                Assert.Equal(
                    Path.Combine(
                        Path.GetFullPath(_tempDirectory),
                        "Cache",
                        "PlaybackAudioVariants"),
                    path);
            }
            finally
            {
                Environment.SetEnvironmentVariable(variable, previous);
            }
        }

        private PlaybackAudioVariantCache CreateCache()
        {
            return new PlaybackAudioVariantCache(
                Path.Combine(_tempDirectory, "cache"),
                maxCacheBytes: 1024 * 1024);
        }

        private string WriteSource(string fileName, byte[] content)
        {
            var path = Path.Combine(_tempDirectory, fileName);
            File.WriteAllBytes(path, content);
            return path;
        }

        private static PreparedAudioArtifact CreateArtifact(byte marker)
        {
            return new PreparedAudioArtifact(
                44100,
                1,
                new[] { marker, (byte)0 });
        }
    }
}