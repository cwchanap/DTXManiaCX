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
                var artifact = CreateArtifact(16);
                var sound = new Mock<ISound>();
                var decodeCount = 0;
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
                    (path, token) =>
                    {
                        decodeCount++;
                        return Task.FromResult(artifact);
                    },
                    (value, path) =>
                    {
                        factoryCount++;
                        return sound.Object;
                    });

                Assert.Equal(1, decodeCount);
                Assert.Equal(1, factoryCount);
                Assert.Equal(artifact.PcmByteLength, prepared.DecodedPcmBytes);
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
        public async Task PrepareAsync_OverBudget_RejectsBeforeConstructingAnySounds()
        {
            var directory = CreateTempDirectory();
            try
            {
                var firstPath = CreatePlaceholderSource(directory, "first.wav");
                var secondPath = CreatePlaceholderSource(directory, "second.wav");
                var factoryCount = 0;

                var exception = await Assert.ThrowsAsync<AudioPreparationBudgetExceededException>(
                    () => PreparedGameplayAudioSet.PrepareAsync(
                        firstPath,
                        new[] { secondPath },
                        new Dictionary<string, string>(),
                        new PlaybackModifiers(100, 0),
                        processor: null,
                        cache: null,
                        progress: null,
                        CancellationToken.None,
                        (path, token) => Task.FromResult(CreateArtifact(8)),
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
        public async Task PrepareAsync_CancelledAfterDecode_DoesNotPublishSound()
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

                var preparation = PreparedGameplayAudioSet.PrepareAsync(
                    sourcePath,
                    Array.Empty<string>(),
                    new Dictionary<string, string>(),
                    new PlaybackModifiers(100, 0),
                    processor: null,
                    cache: null,
                    progress: null,
                    cancellation.Token,
                    async (path, token) =>
                    {
                        decodeStarted.SetResult();
                        await releaseDecode.Task;
                        return CreateArtifact(8);
                    },
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
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Fact]
        public async Task PrepareAsync_BlockingDecoder_ReportsInProgressBeforeCompletion()
        {
            var directory = CreateTempDirectory();
            try
            {
                var sourcePath = CreatePlaceholderSource(directory, "blocking.wav");
                var decoderStarted = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                var releaseDecoder = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                var reports = new List<AudioPreparationProgress>();

                var preparation = PreparedGameplayAudioSet.PrepareAsync(
                    sourcePath,
                    Array.Empty<string>(),
                    new Dictionary<string, string>(),
                    new PlaybackModifiers(100, 0),
                    processor: null,
                    cache: null,
                    new InlineProgress<AudioPreparationProgress>(reports.Add),
                    CancellationToken.None,
                    async (path, token) =>
                    {
                        decoderStarted.SetResult();
                        await releaseDecoder.Task;
                        return CreateArtifact(8);
                    },
                    (artifact, path) => Mock.Of<ISound>());

                await decoderStarted.Task;

                var inProgress = Assert.Single(reports);
                Assert.Equal(0, inProgress.CompletedCount);
                Assert.Equal(1, inProgress.TotalCount);
                Assert.Equal("background", inProgress.CurrentRole);
                Assert.False(preparation.IsCompleted);

                releaseDecoder.SetResult();
                using var prepared = await preparation;

                Assert.Equal(2, reports.Count);
                Assert.Equal(1, reports[^1].CompletedCount);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Fact]
        public async Task OriginalAudioPcmDecoder_Wav_ReturnsExactPayloadMetadata()
        {
            var directory = CreateTempDirectory();
            try
            {
                var sourcePath = Path.Combine(directory, "exact.wav");
                WritePcmWav(sourcePath, sampleRate: 22050, channels: 1, pcm: new byte[] { 1, 0, 2, 0 });

                var artifact = await OriginalAudioPcmDecoder.DecodeAsync(sourcePath);

                Assert.Equal(22050, artifact.SampleRate);
                Assert.Equal(1, artifact.ChannelCount);
                Assert.Equal(new byte[] { 1, 0, 2, 0 }, artifact.PcmData.ToArray());
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
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
                (path, token) => throw new InvalidOperationException("Default decoder should not run."),
                (artifact, path) => Mock.Of<ISound>());
        }

        private static PreparedAudioArtifact CreateArtifact(int byteCount) =>
            new(44100, 1, new byte[byteCount]);

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

        private static void WritePcmWav(
            string path,
            int sampleRate,
            short channels,
            byte[] pcm)
        {
            using var stream = File.Create(path);
            using var writer = new BinaryWriter(stream);
            writer.Write("RIFF"u8.ToArray());
            writer.Write(36 + pcm.Length);
            writer.Write("WAVE"u8.ToArray());
            writer.Write("fmt "u8.ToArray());
            writer.Write(16);
            writer.Write((short)1);
            writer.Write(channels);
            writer.Write(sampleRate);
            writer.Write(sampleRate * channels * sizeof(short));
            writer.Write((short)(channels * sizeof(short)));
            writer.Write((short)16);
            writer.Write("data"u8.ToArray());
            writer.Write(pcm.Length);
            writer.Write(pcm);
        }

        private sealed class InlineProgress<T> : IProgress<T>
        {
            private readonly Action<T> _callback;

            public InlineProgress(Action<T> callback) => _callback = callback;

            public void Report(T value) => _callback(value);
        }
    }
}