#nullable enable

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Stage.Performance;
using DTXMania.Test.Utilities;
using FFMpegCore;
using Xunit;

namespace DTXMania.Test.Resources
{
    public sealed class FfmpegAudioVariantProcessorTests : IDisposable
    {
        private readonly string _tempDirectory = Path.Combine(
            Path.GetTempPath(),
            "dtxmania-ffmpeg-processor-" + Guid.NewGuid().ToString("N"));

        public FfmpegAudioVariantProcessorTests()
        {
            Directory.CreateDirectory(_tempDirectory);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDirectory))
                Directory.Delete(_tempDirectory, recursive: true);
        }

        [Theory]
        [InlineData(0.25, new[] { 0.5, 0.5 })]
        [InlineData(0.50, new[] { 0.5 })]
        [InlineData(1.00, new[] { 1.0 })]
        [InlineData(2.00, new[] { 2.0 })]
        [InlineData(3.00, new[] { 2.0, 1.5 })]
        public void BuildAtempoFactors_ShouldKeepEveryFactorInSupportedRange(
            double requested,
            double[] expected)
        {
            var factors =
                FfmpegAudioVariantProcessor.BuildAtempoFactors(requested);

            Assert.Equal(expected, factors);
            Assert.All(factors, factor => Assert.InRange(factor, 0.5, 2.0));
            Assert.Equal(requested, factors.Aggregate(1.0, (x, y) => x * y), 12);
        }

        [Theory]
        [InlineData(0.25, "atempo=0.5,atempo=0.5")]
        [InlineData(3.00, "atempo=2,atempo=1.5")]
        public void BuildAtempoFilter_ShouldUseInvariantArguments(
            double requested,
            string expected)
        {
            Assert.Equal(
                expected,
                FfmpegAudioVariantProcessor.BuildAtempoFilter(requested));
        }

        [Fact]
        public void BuildXaInputArguments_ShouldDescribeDecodedRawPcm()
        {
            Assert.Equal(
                "-f s16le -ar 44100 -ac 1",
                FfmpegAudioVariantProcessor.BuildXaInputArguments(44100, 1));
        }

        [Fact]
        public void BuildPaddedAtempoFilter_ShouldTrimFlushPaddingToTargetFrames()
        {
            Assert.Equal(
                "apad=pad_len=44100,atempo=0.5,atrim=end_sample=512",
                FfmpegAudioVariantProcessor.BuildPaddedAtempoFilter(
                    0.5,
                    44100,
                    256));
        }

        [Fact]
        public async Task PrepareAsync_DefaultProfile_ShouldFailBeforeBackendRuns()
        {
            var source = WriteSource("source.wav");
            var backend = new FakeBackend();
            var processor = CreateProcessor(backend);

            var error = await Assert.ThrowsAsync<AudioVariantPreparationException>(
                () => processor.PrepareAsync(
                    source,
                    new PlaybackModifiers(100, 0),
                    CancellationToken.None));

            Assert.Equal(AudioVariantPreparationFailure.DefaultProfile, error.Failure);
            Assert.Equal(0, backend.CallCount);
        }

        [Fact]
        public async Task PrepareAsync_RuntimeUnavailable_ShouldReturnDiagnosticFailure()
        {
            var source = WriteSource("source.wav");
            var backend = new FakeBackend();
            var processor = CreateProcessor(
                backend,
                new FfmpegRuntimeAvailability(false, "missing ffmpeg", null));

            var error = await Assert.ThrowsAsync<AudioVariantPreparationException>(
                () => processor.PrepareAsync(
                    source,
                    new PlaybackModifiers(50, 0),
                    CancellationToken.None));

            Assert.Equal(
                AudioVariantPreparationFailure.RuntimeUnavailable,
                error.Failure);
            Assert.Contains("missing ffmpeg", error.Message);
            Assert.Equal(source, error.SourcePath);
            Assert.Equal(0, backend.CallCount);
        }

        [Fact]
        public async Task PrepareAsync_ConcurrentDuplicateRequests_ShouldTransformOnce()
        {
            var source = WriteSource("source.wav");
            var release = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var backend = new FakeBackend(async cancellationToken =>
            {
                await release.Task.WaitAsync(cancellationToken);
            });
            var processor = CreateProcessor(backend);
            var modifiers = new PlaybackModifiers(75, 0);

            var first = processor.PrepareAsync(
                source,
                modifiers,
                CancellationToken.None);
            var second = processor.PrepareAsync(
                source,
                modifiers,
                CancellationToken.None);
            release.SetResult(true);

            await Task.WhenAll(first, second);

            Assert.Equal(1, backend.CallCount);
        }

        [Fact]
        public async Task PrepareAsync_FirstDuplicateCancels_SecondWaiterStillCompletes()
        {
            var source = WriteSource("first-cancels.wav");
            var started = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var backend = new FakeBackend(async cancellationToken =>
            {
                started.TrySetResult(true);
                await release.Task.WaitAsync(cancellationToken);
            });
            var processor = CreateProcessor(backend);
            var modifiers = new PlaybackModifiers(75, 0);
            using var firstCancellation = new CancellationTokenSource();

            var first = processor.PrepareAsync(
                source,
                modifiers,
                firstCancellation.Token);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(1));
            var second = processor.PrepareAsync(
                source,
                modifiers,
                CancellationToken.None);

            firstCancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => first.WaitAsync(TimeSpan.FromSeconds(1)));
            Assert.False(second.IsCompleted);

            release.SetResult(true);
            await second.WaitAsync(TimeSpan.FromSeconds(1));

            Assert.Equal(1, backend.CallCount);
        }

        [Fact]
        public async Task PrepareAsync_LaterDuplicateCancels_ReturnsPromptlyWithoutCancelingFirst()
        {
            var source = WriteSource("later-cancels.wav");
            var started = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var backend = new FakeBackend(async cancellationToken =>
            {
                started.TrySetResult(true);
                await release.Task.WaitAsync(cancellationToken);
            });
            var processor = CreateProcessor(backend);
            var modifiers = new PlaybackModifiers(75, 0);
            using var laterCancellation = new CancellationTokenSource();

            var first = processor.PrepareAsync(
                source,
                modifiers,
                CancellationToken.None);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(1));
            var later = processor.PrepareAsync(
                source,
                modifiers,
                laterCancellation.Token);

            laterCancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => later.WaitAsync(TimeSpan.FromSeconds(1)));
            Assert.False(first.IsCompleted);

            release.SetResult(true);
            await first.WaitAsync(TimeSpan.FromSeconds(1));

            Assert.Equal(1, backend.CallCount);
        }

        [Fact]
        public async Task PrepareAsync_DistinctRequests_ShouldLimitConcurrencyToTwo()
        {
            var release = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var backend = new FakeBackend(async cancellationToken =>
            {
                await release.Task.WaitAsync(cancellationToken);
            });
            var processor = CreateProcessor(
                backend,
                gate: new SemaphoreSlim(2, 2));
            var tasks = Enumerable.Range(0, 4)
                .Select(index => processor.PrepareAsync(
                    WriteSource($"source-{index}.wav"),
                    new PlaybackModifiers(50 + index * 5, 0),
                    CancellationToken.None))
                .ToArray();

            await EventuallyAsync(() => backend.CallCount == 2);
            Assert.Equal(2, backend.MaxActive);
            release.SetResult(true);
            await Task.WhenAll(tasks);

            Assert.Equal(2, backend.MaxActive);
        }

        [Fact]
        public async Task PrepareAsync_Cancellation_ShouldPropagateAndDeleteTemporaryOutput()
        {
            var source = WriteSource("source.wav");
            var backend = new FakeBackend(
                cancellationToken => Task.Delay(
                    Timeout.InfiniteTimeSpan,
                    cancellationToken));
            var processor = CreateProcessor(backend);
            using var cancellation = new CancellationTokenSource();

            var preparation = processor.PrepareAsync(
                source,
                new PlaybackModifiers(50, 0),
                cancellation.Token);
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => preparation);
            Assert.Empty(Directory.GetFiles(_tempDirectory, "*.tmp"));
        }

        [Fact]
        public async Task PrepareAsync_Timeout_ShouldReturnTypedFailure()
        {
            var source = WriteSource("source.wav");
            var backend = new FakeBackend(
                cancellationToken => Task.Delay(
                    Timeout.InfiniteTimeSpan,
                    cancellationToken));
            var processor = CreateProcessor(
                backend,
                timeout: TimeSpan.FromMilliseconds(25));

            var error = await Assert.ThrowsAsync<AudioVariantPreparationException>(
                () => processor.PrepareAsync(
                    source,
                    new PlaybackModifiers(50, 0),
                    CancellationToken.None));

            Assert.Equal(AudioVariantPreparationFailure.TimedOut, error.Failure);
            Assert.Empty(Directory.GetFiles(_tempDirectory, "*.tmp"));
        }

        [Fact]
        public async Task PrepareAsync_EmptyOutput_ShouldReturnTypedFailure()
        {
            var source = WriteSource("source.wav");
            var backend = new FakeBackend(writeOutput: false);
            var processor = CreateProcessor(backend);

            var error = await Assert.ThrowsAsync<AudioVariantPreparationException>(
                () => processor.PrepareAsync(
                    source,
                    new PlaybackModifiers(50, 0),
                    CancellationToken.None));

            Assert.Equal(AudioVariantPreparationFailure.InvalidOutput, error.Failure);
        }

        [Theory]
        [InlineData("source.flac")]
        [InlineData("source.txt")]
        public async Task PrepareAsync_UnsupportedExtension_ShouldReturnTypedFailure(
            string fileName)
        {
            var source = WriteSource(fileName);
            var processor = CreateProcessor(new FakeBackend());

            var error = await Assert.ThrowsAsync<AudioVariantPreparationException>(
                () => processor.PrepareAsync(
                    source,
                    new PlaybackModifiers(50, 0),
                    CancellationToken.None));

            Assert.Equal(
                AudioVariantPreparationFailure.UnsupportedFormat,
                error.Failure);
        }

        [Theory]
        [InlineData("wav")]
        [InlineData("mp3")]
        [InlineData("ogg")]
        [Trait("Category", AudioTestUtils.AudioTestCategory)]
        public async Task PrepareAsync_EncodedAudio_ShouldNormalizeToRawPcm(
            string extension)
        {
            Assert.True(FfmpegRuntime.EnsureConfigured().IsAvailable);
            var wav = WriteToneWav("tone.wav", durationSeconds: 0.25);
            var source = wav;
            if (extension != "wav")
            {
                source = Path.Combine(_tempDirectory, "tone." + extension);
                var codec = extension == "mp3" ? "libmp3lame" : "libvorbis";
                await FFMpegArguments
                    .FromFileInput(wav)
                    .OutputToFile(
                        source,
                        overwrite: true,
                        options => options.WithAudioCodec(codec))
                    .ProcessAsynchronously();
            }

            var modifiers = new PlaybackModifiers(50, 12);
            var artifact = await new FfmpegAudioVariantProcessor().PrepareAsync(
                source,
                modifiers,
                CancellationToken.None);

            Assert.Equal(44100, artifact.SampleRate);
            Assert.Equal(1, artifact.ChannelCount);
            Assert.True(artifact.PcmByteLength > 0);
            var outputDuration =
                artifact.PcmByteLength /
                (double)(artifact.SampleRate * artifact.ChannelCount * sizeof(short));
            Assert.InRange(outputDuration, 0.90, 1.10);
            Assert.InRange(
                outputDuration / modifiers.PitchFactor,
                0.45,
                0.55);
            Assert.InRange(EstimateFrequency(artifact), 420.0, 460.0);
        }

        [Fact]
        [Trait("Category", AudioTestUtils.AudioTestCategory)]
        public async Task PrepareAsync_SyntheticXa_ShouldUseDecodedRawInput()
        {
            Assert.True(FfmpegRuntime.EnsureConfigured().IsAvailable);
            var source = AudioTestUtils.CreateTestXaFile(
                Path.Combine(_tempDirectory, "synthetic.xa"),
                bits: 8,
                blocks: 8);

            var artifact = await new FfmpegAudioVariantProcessor().PrepareAsync(
                source,
                new PlaybackModifiers(50, 0),
                CancellationToken.None);

            Assert.Equal(44100, artifact.SampleRate);
            Assert.Equal(1, artifact.ChannelCount);
            Assert.True(artifact.PcmByteLength > 0);
        }

        [Fact]
        [Trait("Category", AudioTestUtils.AudioTestCategory)]
        public async Task PrepareAsync_CorruptInput_ShouldReturnTypedFailure()
        {
            Assert.True(FfmpegRuntime.EnsureConfigured().IsAvailable);
            var source = WriteSource("corrupt.ogg");

            var error = await Assert.ThrowsAsync<AudioVariantPreparationException>(
                () => new FfmpegAudioVariantProcessor().PrepareAsync(
                    source,
                    new PlaybackModifiers(50, 0),
                    CancellationToken.None));

            Assert.True(
                error.Failure is AudioVariantPreparationFailure.ProbeFailed or
                AudioVariantPreparationFailure.TransformFailed);
        }

        private FfmpegAudioVariantProcessor CreateProcessor(
            IAudioVariantBackend backend,
            FfmpegRuntimeAvailability? availability = null,
            SemaphoreSlim? gate = null,
            TimeSpan? timeout = null)
        {
            return new FfmpegAudioVariantProcessor(
                backend,
                () => availability ?? new FfmpegRuntimeAvailability(true, null, null),
                gate ?? new SemaphoreSlim(2, 2),
                timeout ?? TimeSpan.FromSeconds(5),
                _tempDirectory);
        }

        private string WriteSource(string fileName)
        {
            var path = Path.Combine(_tempDirectory, fileName);
            File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
            return path;
        }

        private string WriteToneWav(string fileName, double durationSeconds)
        {
            var path = Path.Combine(_tempDirectory, fileName);
            const int sampleRate = 44100;
            const short channelCount = 1;
            var sampleCount = (int)(sampleRate * durationSeconds);
            var dataLength = sampleCount * sizeof(short);
            using var writer = new BinaryWriter(File.Create(path));
            writer.Write("RIFF".ToCharArray());
            writer.Write(36 + dataLength);
            writer.Write("WAVE".ToCharArray());
            writer.Write("fmt ".ToCharArray());
            writer.Write(16);
            writer.Write((short)1);
            writer.Write(channelCount);
            writer.Write(sampleRate);
            writer.Write(sampleRate * sizeof(short));
            writer.Write((short)sizeof(short));
            writer.Write((short)16);
            writer.Write("data".ToCharArray());
            writer.Write(dataLength);
            for (var index = 0; index < sampleCount; index++)
            {
                var sample = (short)(
                    Math.Sin(2.0 * Math.PI * 440.0 * index / sampleRate) *
                    short.MaxValue *
                    0.25);
                writer.Write(sample);
            }

            return path;
        }

        private static async Task EventuallyAsync(Func<bool> predicate)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            while (!predicate())
            {
                await Task.Delay(5, timeout.Token);
            }
        }

        private static double EstimateFrequency(PreparedAudioArtifact artifact)
        {
            var pcm = artifact.PcmData.Span;
            var positiveCrossings = 0;
            var previous = BinaryPrimitives.ReadInt16LittleEndian(pcm);
            for (var offset = sizeof(short);
                 offset < pcm.Length;
                 offset += sizeof(short) * artifact.ChannelCount)
            {
                var current = BinaryPrimitives.ReadInt16LittleEndian(
                    pcm.Slice(offset, sizeof(short)));
                if (previous <= 0 && current > 0)
                    positiveCrossings++;
                previous = current;
            }

            var durationSeconds =
                artifact.PcmByteLength /
                (double)(
                    artifact.SampleRate *
                    artifact.ChannelCount *
                    sizeof(short));
            return positiveCrossings / durationSeconds;
        }

        private sealed class FakeBackend : IAudioVariantBackend
        {
            private readonly Func<CancellationToken, Task> _beforeWrite;
            private readonly bool _writeOutput;
            private int _active;
            private int _callCount;
            private int _maxActive;

            public FakeBackend(
                Func<CancellationToken, Task>? beforeWrite = null,
                bool writeOutput = true)
            {
                _beforeWrite =
                    beforeWrite ?? (_ => Task.CompletedTask);
                _writeOutput = writeOutput;
            }

            public int CallCount => Volatile.Read(ref _callCount);

            public int MaxActive => Volatile.Read(ref _maxActive);

            public async Task<AudioTransformMetadata> TransformAsync(
                string sourcePath,
                string outputPath,
                PlaybackModifiers modifiers,
                CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref _callCount);
                var active = Interlocked.Increment(ref _active);
                UpdateMaximum(active);
                try
                {
                    await _beforeWrite(cancellationToken);
                    if (_writeOutput)
                    {
                        await File.WriteAllBytesAsync(
                            outputPath,
                            new byte[] { 0, 0 },
                            cancellationToken);
                    }

                    return new AudioTransformMetadata(44100, 1);
                }
                finally
                {
                    Interlocked.Decrement(ref _active);
                }
            }

            private void UpdateMaximum(int active)
            {
                while (true)
                {
                    var observed = Volatile.Read(ref _maxActive);
                    if (active <= observed)
                        return;
                    if (Interlocked.CompareExchange(
                        ref _maxActive,
                        active,
                        observed) == observed)
                    {
                        return;
                    }
                }
            }
        }
    }
}