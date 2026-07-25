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

namespace DTXMania.Test.Stage.Performance
{
    [Trait("Category", "Unit")]
    public sealed class PreparedGameplayAudioSetTests
    {
        [Fact]
        public async Task PrepareAsync_DefaultProfile_DeduplicatesRolesAndDisposesOwnedSoundOnce()
        {
            var directory = CreateTempDirectory();
            try
            {
                var sourcePath = CreatePlaceholderSource(directory, "shared.wav");
                var sound = new Mock<ISound>();
                var loadCount = 0;
                var factoryCount = 0;

                var prepared = await PreparedGameplayAudioSet.PrepareAsync(
                    sourcePath,
                    new[] { sourcePath },
                    new Dictionary<string, string> { ["01"] = sourcePath },
                    new PlaybackModifiers(100, 0),
                    processor: null,
                    cache: null,
                    progress: null,
                    CancellationToken.None,
                    path =>
                    {
                        loadCount++;
                        return sound.Object;
                    },
                    (value, path) =>
                    {
                        factoryCount++;
                        return sound.Object;
                    });

                Assert.Equal(1, loadCount);
                Assert.Equal(0, factoryCount);
                Assert.Equal(0, prepared.DecodedPcmBytes);
                Assert.Same(prepared.MainBackground, prepared.ScheduledBgmBySourcePath[sourcePath]);
                Assert.Same(prepared.MainBackground, prepared.ChipSoundsByWavId["01"]);

                prepared.Dispose();
                prepared.Dispose();
                sound.Verify(value => value.Dispose(), Times.Once);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Fact]
        public async Task PrepareAsync_DefaultProfile_FailingMainBgm_FallsBackSilentlyInsteadOfAborting()
        {
            var directory = CreateTempDirectory();
            try
            {
                var mainPath = CreatePlaceholderSource(directory, "main.wav");
                var chipPath = CreatePlaceholderSource(directory, "chip.wav");
                var chipSound = new Mock<ISound>();

                var prepared = await PreparedGameplayAudioSet.PrepareAsync(
                    mainPath,
                    Array.Empty<string>(),
                    new Dictionary<string, string> { ["01"] = chipPath },
                    new PlaybackModifiers(100, 0),
                    processor: null,
                    cache: null,
                    progress: null,
                    CancellationToken.None,
                    path =>
                    {
                        if (path == mainPath)
                            throw new InvalidOperationException("Simulated corrupt main BGM");
                        return chipSound.Object;
                    },
                    (artifact, path) => throw new InvalidOperationException(
                        "Prepared factory should not run for default profile."));

                // Invariant #2: a corrupt main BGM yields null (silent-clock fallback)
                // rather than aborting the run. The chip sound still loads.
                Assert.Null(prepared.MainBackground);
                Assert.Single(prepared.ChipSoundsByWavId);
                Assert.Same(chipSound.Object, prepared.ChipSoundsByWavId["01"]);

                prepared.Dispose();
                chipSound.Verify(value => value.Dispose(), Times.Once);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Fact]
        public async Task PrepareAsync_OverBudget_RejectsBeforeConstructingAnySounds()
        {
            var directory = CreateTempDirectory();
            try
            {
                var firstPath = CreatePlaceholderSource(directory, "first.wav");
                var secondPath = CreatePlaceholderSource(directory, "second.wav");
                var cache = new PlaybackAudioVariantCache(
                    Path.Combine(directory, "cache"),
                    maxCacheBytes: 1024 * 1024);
                var processor = new Mock<IAudioVariantProcessor>();
                processor
                    .Setup(value => value.PrepareAsync(
                        It.IsAny<string>(),
                        It.IsAny<PlaybackModifiers>(),
                        It.IsAny<CancellationToken>()))
                    .ReturnsAsync(CreateArtifact(8));
                var factoryCount = 0;

                var exception = await Assert.ThrowsAsync<AudioPreparationBudgetExceededException>(
                    () => PreparedGameplayAudioSet.PrepareAsync(
                        firstPath,
                        new[] { secondPath },
                        new Dictionary<string, string>(),
                        new PlaybackModifiers(50, 12),
                        processor.Object,
                        cache,
                        progress: null,
                        CancellationToken.None,
                        path => throw new InvalidOperationException("Default loader should not run for non-default profile."),
                        (artifact, path) =>
                        {
                            factoryCount++;
                            return Mock.Of<ISound>();
                        },
                        decodedPcmBudgetBytes: 12));

                Assert.Equal(16, exception.DecodedBytes);
                Assert.Equal(0, factoryCount);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Fact]
        public async Task PrepareAsync_WarmCache_ReportsCompletedTransformAndCacheHit()
        {
            var directory = CreateTempDirectory();
            try
            {
                var sourcePath = CreatePlaceholderSource(directory, "chip.wav");
                var cache = new PlaybackAudioVariantCache(
                    Path.Combine(directory, "cache"),
                    maxCacheBytes: 1024 * 1024);
                var processor = new Mock<IAudioVariantProcessor>();
                processor
                    .Setup(value => value.PrepareAsync(
                        sourcePath,
                        new PlaybackModifiers(50, 12),
                        It.IsAny<CancellationToken>()))
                    .ReturnsAsync(CreateArtifact(16));

                using (await PrepareNonDefaultAsync(
                    sourcePath,
                    processor.Object,
                    cache,
                    progress: null))
                {
                }

                var reports = new List<AudioPreparationProgress>();
                using (await PrepareNonDefaultAsync(
                    sourcePath,
                    processor.Object,
                    cache,
                    new InlineProgress<AudioPreparationProgress>(reports.Add)))
                {
                }

                Assert.Equal(2, reports.Count);
                var final = reports.Last();
                Assert.Equal(1, final.CompletedCount);
                Assert.Equal(1, final.TotalCount);
                Assert.Equal(1, final.CacheHitCount);
                Assert.Equal(16, final.DecodedByteEstimate);
                processor.VerifyAll();
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Fact]
        public async Task PrepareAsync_CancelledDuringVariantProcessing_DoesNotPublishSound()
        {
            var directory = CreateTempDirectory();
            try
            {
                var sourcePath = CreatePlaceholderSource(directory, "late.wav");
                var decodeStarted = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                var releaseDecode = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                var factoryCount = 0;
                using var cancellation = new CancellationTokenSource();

                var cacheRoot = Path.Combine(directory, "cache");
                var cache = new PlaybackAudioVariantCache(
                    cacheRoot,
                    maxCacheBytes: 1024 * 1024);
                var processor = new Mock<IAudioVariantProcessor>();
                processor
                    .Setup(value => value.PrepareAsync(
                        It.IsAny<string>(),
                        It.IsAny<PlaybackModifiers>(),
                        It.IsAny<CancellationToken>()))
                    .Returns(async (string path, PlaybackModifiers mods, CancellationToken token) =>
                    {
                        decodeStarted.SetResult();
                        await releaseDecode.Task;
                        return CreateArtifact(8);
                    });

                var preparation = PreparedGameplayAudioSet.PrepareAsync(
                    sourcePath,
                    Array.Empty<string>(),
                    new Dictionary<string, string>(),
                    new PlaybackModifiers(50, 12),
                    processor.Object,
                    cache,
                    progress: null,
                    cancellation.Token,
                    path => throw new InvalidOperationException("Default loader should not run for non-default profile."),
                    (artifact, path) =>
                    {
                        factoryCount++;
                        return Mock.Of<ISound>();
                    });

                await decodeStarted.Task;
                cancellation.Cancel();
                releaseDecode.SetResult();

                await Assert.ThrowsAnyAsync<OperationCanceledException>(
                    async () => await preparation);
                Assert.Equal(0, factoryCount);

                // The cancelled waiter returns promptly, while the shared cache
                // operation completes its asynchronous temp-file cleanup. A valid
                // final artifact may still win the cancellation race, so only
                // temporary files indicate leaked state.
                await WaitForTemporaryFilesToBeDeletedAsync(
                    cacheRoot,
                    TimeSpan.FromSeconds(2));
                Assert.Empty(Directory.GetFiles(cacheRoot, "*.tmp-*"));
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Fact]
        public async Task PrepareAsync_BlockingProcessor_ReportsInProgressBeforeCompletion()
        {
            var directory = CreateTempDirectory();
            try
            {
                var sourcePath = CreatePlaceholderSource(directory, "blocking.wav");
                var processorStarted = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                var releaseProcessor = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                var reports = new List<AudioPreparationProgress>();

                var cache = new PlaybackAudioVariantCache(
                    Path.Combine(directory, "cache"),
                    maxCacheBytes: 1024 * 1024);
                var processor = new Mock<IAudioVariantProcessor>();
                processor
                    .Setup(value => value.PrepareAsync(
                        It.IsAny<string>(),
                        It.IsAny<PlaybackModifiers>(),
                        It.IsAny<CancellationToken>()))
                    .Returns(async (string path, PlaybackModifiers mods, CancellationToken token) =>
                    {
                        processorStarted.SetResult();
                        await releaseProcessor.Task;
                        return CreateArtifact(8);
                    });

                var preparation = PreparedGameplayAudioSet.PrepareAsync(
                    sourcePath,
                    Array.Empty<string>(),
                    new Dictionary<string, string>(),
                    new PlaybackModifiers(50, 12),
                    processor.Object,
                    cache,
                    new InlineProgress<AudioPreparationProgress>(reports.Add),
                    CancellationToken.None,
                    path => throw new InvalidOperationException("Default loader should not run for non-default profile."),
                    (artifact, path) => Mock.Of<ISound>());

                await processorStarted.Task;

                var inProgress = Assert.Single(reports);
                Assert.Equal(0, inProgress.CompletedCount);
                Assert.Equal(1, inProgress.TotalCount);
                Assert.Equal("background", inProgress.CurrentRole);
                Assert.False(preparation.IsCompleted);

                releaseProcessor.SetResult();
                using var prepared = await preparation;

                Assert.Equal(2, reports.Count);
                Assert.Equal(1, reports[^1].CompletedCount);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        /// <summary>
        /// Regression: the variant-profile sound-construction loop must release
        /// each artifact's backing PCM buffer as soon as the SoundEffect has
        /// copied it. SoundEffect copies the supplied buffer internally, so
        /// retaining the artifacts dictionary entries past each conversion keeps
        /// every decoded PCM array alive simultaneously alongside the SoundEffect-
        /// owned copies. Near the 512 MiB session budget that roughly doubles
        /// peak memory and can OOM an otherwise valid profile. This test forces
        /// GC between sound constructions and asserts that prior artifact buffers
        /// are collectable — they would remain rooted if the dictionary still
        /// held them.
        /// </summary>
        /// <remarks>
        /// The cache is pre-warmed so the tracked run services every lookup as a
        /// disk cache hit. Cache misses route through WaiterSharedOperation, whose
        /// completion continuation task transiently roots the artifact for one GC
        /// cycle; cache hits return the artifact directly from TryReadAsync with
        /// no in-flight operation, so the only strong reference to each artifact
        /// during the sound-construction loop is the local artifacts dictionary.
        /// This isolates the dictionary-retention contract the fix addresses.
        /// </remarks>
        [Fact]
        public async Task PrepareAsync_VariantProfile_ReleasesArtifactBuffersAsSoundsAreCreated()
        {
            var directory = CreateTempDirectory();
            try
            {
                var paths = new[]
                {
                    CreatePlaceholderSource(directory, "bg.wav"),
                    CreatePlaceholderSource(directory, "sched.wav"),
                    CreatePlaceholderSource(directory, "chip.wav"),
                };
                var cache = new PlaybackAudioVariantCache(
                    Path.Combine(directory, "cache"),
                    maxCacheBytes: 1024 * 1024);
                var processor = new Mock<IAudioVariantProcessor>();
                processor
                    .Setup(value => value.PrepareAsync(
                        It.IsAny<string>(),
                        It.IsAny<PlaybackModifiers>(),
                        It.IsAny<CancellationToken>()))
                    .Returns((string path, PlaybackModifiers mods, CancellationToken token) =>
                    {
                        var buffer = new byte[4096];
                        return Task.FromResult<PreparedAudioArtifact>(
                            PreparedAudioArtifact.FromOwnedBytes(44100, 1, buffer));
                    });

                // Pre-warm: populate the disk cache so the tracked run services
                // every lookup as a cache hit, bypassing the WaiterSharedOperation
                // machinery whose continuation task transiently roots artifacts.
                using (await PreparedGameplayAudioSet.PrepareAsync(
                    mainBackgroundPath: paths[0],
                    scheduledBgmSourcePaths: new[] { paths[1] },
                    chipSourcePathsByWavId: new Dictionary<string, string> { ["01"] = paths[2] },
                    modifiers: new PlaybackModifiers(50, 12),
                    processor: processor.Object,
                    cache: cache,
                    progress: null,
                    CancellationToken.None,
                    path => throw new InvalidOperationException(
                        "Default loader should not run for non-default profile."),
                    (artifact, path) => Mock.Of<ISound>()))
                {
                }

                var priorBuffers = new List<WeakReference>();
                var maxLivePriorBuffers = 0;

                // Tracked run: every lookup is a cache hit. The only strong
                // reference to each artifact is the local artifacts dictionary
                // (and the async state machine's hoisted lookup field for the
                // last artifact, which is processed last and is therefore never
                // a "prior" buffer).
                using var prepared = await PreparedGameplayAudioSet.PrepareAsync(
                    mainBackgroundPath: paths[0],
                    scheduledBgmSourcePaths: new[] { paths[1] },
                    chipSourcePathsByWavId: new Dictionary<string, string> { ["01"] = paths[2] },
                    modifiers: new PlaybackModifiers(50, 12),
                    processor: processor.Object,
                    cache: cache,
                    progress: null,
                    CancellationToken.None,
                    path => throw new InvalidOperationException(
                        "Default loader should not run for non-default profile."),
                    (artifact, path) =>
                    {
                        // Force collection before tracking the current buffer so
                        // the liveness check reflects only references held across
                        // iterations (i.e. the artifacts dictionary). Prior
                        // artifacts must have been released by the per-iteration
                        // dictionary removal.
                        ForceGarbageCollection();
                        var livePrior = priorBuffers.Count(wr => wr.IsAlive);
                        if (livePrior > maxLivePriorBuffers)
                            maxLivePriorBuffers = livePrior;
                        priorBuffers.Add(new WeakReference(artifact.PcmDataBuffer));
                        return Mock.Of<ISound>();
                    });

                // During the loop, prior artifact buffers must be collectable.
                // maxLivePriorBuffers counts buffers from *previous* iterations,
                // so it must be zero with the fix in place. Without the fix, the
                // dictionary would root every prior artifact and this would equal
                // priorBuffers.Count - 1. This is the direct verification of the
                // peak-memory contract: near the session budget, only one artifact
                // buffer is retained at a time instead of the full set alongside
                // the SoundEffect copies.
                Assert.Equal(0, maxLivePriorBuffers);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        private static void ForceGarbageCollection()
        {
            for (int i = 0; i < 3; i++)
            {
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true);
                GC.WaitForPendingFinalizers();
            }
        }

        private static Task<PreparedGameplayAudioSet> PrepareNonDefaultAsync(
            string sourcePath,
            IAudioVariantProcessor processor,
            PlaybackAudioVariantCache cache,
            IProgress<AudioPreparationProgress>? progress)
        {
            return PreparedGameplayAudioSet.PrepareAsync(
                mainBackgroundPath: null,
                scheduledBgmSourcePaths: Array.Empty<string>(),
                chipSourcePathsByWavId: new Dictionary<string, string> { ["01"] = sourcePath },
                modifiers: new PlaybackModifiers(50, 12),
                processor,
                cache,
                progress,
                CancellationToken.None,
                path => throw new InvalidOperationException("Default loader should not run for non-default profile."),
                (artifact, path) => Mock.Of<ISound>());
        }

        private static PreparedAudioArtifact CreateArtifact(int byteCount) =>
            new(44100, 1, new byte[byteCount]);

        private static async Task WaitForTemporaryFilesToBeDeletedAsync(
            string directory,
            TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (!Directory.Exists(directory) ||
                    Directory.GetFiles(directory, "*.tmp-*").Length == 0)
                {
                    return;
                }

                await Task.Delay(10);
            }
        }

        private static string CreateTempDirectory()
        {
            var path = Path.Combine(Path.GetTempPath(), "DTXMania_PreparedAudio_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private static string CreatePlaceholderSource(string directory, string name)
        {
            var path = Path.Combine(directory, name);
            File.WriteAllBytes(path, new byte[] { 1 });
            return Path.GetFullPath(path);
        }

        private sealed class InlineProgress<T> : IProgress<T>
        {
            private readonly Action<T> _callback;

            public InlineProgress(Action<T> callback) => _callback = callback;

            public void Report(T value) => _callback(value);
        }
    }
}
