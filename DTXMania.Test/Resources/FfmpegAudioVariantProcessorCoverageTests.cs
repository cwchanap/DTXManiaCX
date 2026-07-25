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
    public sealed class FfmpegAudioVariantProcessorCoverageTests : IDisposable
    {
        private readonly string _tempDirectory = Path.Combine(
            Path.GetTempPath(),
            "dtxmania-ffmpeg-coverage-" + Guid.NewGuid().ToString("N"));

        public FfmpegAudioVariantProcessorCoverageTests()
        {
            Directory.CreateDirectory(_tempDirectory);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDirectory))
                Directory.Delete(_tempDirectory, recursive: true);
        }

        [Fact]
        public void Constructor_InvalidDependenciesAndOptions_ShouldThrow()
        {
            var backend = new StubBackend();
            Func<FfmpegRuntimeAvailability> runtime = () =>
                new FfmpegRuntimeAvailability(true, null, null);
            using var gate = new SemaphoreSlim(1, 1);

            Assert.Throws<ArgumentNullException>(() => new FfmpegAudioVariantProcessor(
                null!, runtime, gate, TimeSpan.FromSeconds(1), _tempDirectory));
            Assert.Throws<ArgumentNullException>(() => new FfmpegAudioVariantProcessor(
                backend, null!, gate, TimeSpan.FromSeconds(1), _tempDirectory));
            Assert.Throws<ArgumentNullException>(() => new FfmpegAudioVariantProcessor(
                backend, runtime, null!, TimeSpan.FromSeconds(1), _tempDirectory));
            Assert.Throws<ArgumentOutOfRangeException>(() => new FfmpegAudioVariantProcessor(
                backend, runtime, gate, TimeSpan.Zero, _tempDirectory));
            Assert.Throws<ArgumentException>(() => new FfmpegAudioVariantProcessor(
                backend, runtime, gate, TimeSpan.FromSeconds(1), "   "));
        }

        [Fact]
        public void BuildAtempoFactors_InvalidFactor_ShouldThrow()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                FfmpegAudioVariantProcessor.BuildAtempoFactors(0));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                FfmpegAudioVariantProcessor.BuildAtempoFactors(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                FfmpegAudioVariantProcessor.BuildAtempoFactors(double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                FfmpegAudioVariantProcessor.BuildAtempoFactors(double.PositiveInfinity));
        }

        [Fact]
        public void BuildPaddedAtempoFilter_InvalidSizing_ShouldThrow()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                FfmpegAudioVariantProcessor.BuildPaddedAtempoFilter(1, 0, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                FfmpegAudioVariantProcessor.BuildPaddedAtempoFilter(1, 44100, 0));
        }

        [Fact]
        public void ComputeExpectedOutputBytes_InvalidInputs_ShouldThrow()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                FfmpegAudioVariantProcessor.ComputeExpectedOutputBytes(0, 1, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                FfmpegAudioVariantProcessor.ComputeExpectedOutputBytes(3, 1, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                FfmpegAudioVariantProcessor.ComputeExpectedOutputBytes(1, 0, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                FfmpegAudioVariantProcessor.ComputeExpectedOutputBytes(1, 1, 0));
        }

        [Fact]
        public async Task PrepareAsync_PreCancelledToken_ShouldStopBeforeRequestValidation()
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            var processor = CreateProcessor(new StubBackend());

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                processor.PrepareAsync(
                    Path.Combine(_tempDirectory, "missing.wav"),
                    new PlaybackModifiers(50, 0),
                    cancellation.Token));
        }

        [Fact]
        public async Task PrepareAsync_BlankSource_ShouldThrowBeforeBackendRuns()
        {
            var backend = new StubBackend();
            var processor = CreateProcessor(backend);

            await Assert.ThrowsAsync<ArgumentException>(() =>
                processor.PrepareAsync(
                    "   ",
                    new PlaybackModifiers(50, 0),
                    CancellationToken.None));

            Assert.Equal(0, backend.CallCount);
        }

        [Fact]
        public async Task PrepareAsync_MissingSource_ShouldReturnTypedFailure()
        {
            var backend = new StubBackend();
            var processor = CreateProcessor(backend);
            var source = Path.Combine(_tempDirectory, "missing.wav");

            var error = await Assert.ThrowsAsync<AudioVariantPreparationException>(() =>
                processor.PrepareAsync(
                    source,
                    new PlaybackModifiers(50, 0),
                    CancellationToken.None));

            Assert.Equal(AudioVariantPreparationFailure.SourceNotFound, error.Failure);
            Assert.Equal(0, backend.CallCount);
        }

        [Fact]
        public async Task PrepareAsync_BackendFailure_ShouldPreserveTypedFailure()
        {
            var source = WriteSource("decode.wav");
            var inner = new InvalidDataException("invalid encoded payload");
            var backend = new StubBackend((_, _, _, _) =>
                Task.FromException<AudioTransformMetadata>(
                    new AudioVariantBackendException(
                        AudioVariantPreparationFailure.DecodeFailed,
                        "decode failed",
                        inner)));
            var processor = CreateProcessor(backend);

            var error = await Assert.ThrowsAsync<AudioVariantPreparationException>(() =>
                processor.PrepareAsync(
                    source,
                    new PlaybackModifiers(50, 0),
                    CancellationToken.None));

            Assert.Equal(AudioVariantPreparationFailure.DecodeFailed, error.Failure);
            Assert.Contains("decode failed", error.Message);
            var backendError = Assert.IsType<AudioVariantBackendException>(error.InnerException);
            Assert.Same(inner, backendError.InnerException);
        }

        [Theory]
        [InlineData(44100, 1, 0)]
        [InlineData(44100, 0, 2)]
        [InlineData(7999, 1, 2)]
        [InlineData(48001, 1, 2)]
        [InlineData(44100, 1, 1)]
        public async Task PrepareAsync_InvalidPcm_ShouldReturnTypedFailure(
            int sampleRate,
            int channelCount,
            int outputLength)
        {
            var source = WriteSource($"invalid-{sampleRate}-{channelCount}-{outputLength}.wav");
            var backend = new StubBackend(async (_, outputPath, _, cancellationToken) =>
            {
                await File.WriteAllBytesAsync(
                    outputPath,
                    new byte[outputLength],
                    cancellationToken);
                return new AudioTransformMetadata(sampleRate, channelCount);
            });
            var processor = CreateProcessor(backend);

            var error = await Assert.ThrowsAsync<AudioVariantPreparationException>(() =>
                processor.PrepareAsync(
                    source,
                    new PlaybackModifiers(50, 0),
                    CancellationToken.None));

            Assert.Equal(AudioVariantPreparationFailure.InvalidOutput, error.Failure);
            Assert.Empty(Directory.GetFiles(_tempDirectory, "*.tmp"));
        }

        private FfmpegAudioVariantProcessor CreateProcessor(IAudioVariantBackend backend)
        {
            return new FfmpegAudioVariantProcessor(
                backend,
                () => new FfmpegRuntimeAvailability(true, null, null),
                new SemaphoreSlim(1, 1),
                TimeSpan.FromSeconds(2),
                _tempDirectory);
        }

        private string WriteSource(string fileName)
        {
            var path = Path.Combine(_tempDirectory, fileName);
            File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
            return path;
        }

        private sealed class StubBackend : IAudioVariantBackend
        {
            private readonly Func<
                string,
                string,
                PlaybackModifiers,
                CancellationToken,
                Task<AudioTransformMetadata>> _transform;
            private int _callCount;

            public StubBackend(
                Func<
                    string,
                    string,
                    PlaybackModifiers,
                    CancellationToken,
                    Task<AudioTransformMetadata>>? transform = null)
            {
                _transform = transform ?? DefaultTransformAsync;
            }

            public int CallCount => Volatile.Read(ref _callCount);

            public Task<AudioTransformMetadata> TransformAsync(
                string sourcePath,
                string outputPath,
                PlaybackModifiers modifiers,
                CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref _callCount);
                return _transform(sourcePath, outputPath, modifiers, cancellationToken);
            }

            private static async Task<AudioTransformMetadata> DefaultTransformAsync(
                string sourcePath,
                string outputPath,
                PlaybackModifiers modifiers,
                CancellationToken cancellationToken)
            {
                var metadata = new AudioTransformMetadata(44100, 1);
                var outputLength = FfmpegAudioVariantProcessor.ComputeExpectedOutputBytes(
                    metadata.ChannelCount,
                    modifiers.FfmpegTempoFactor,
                    metadata.SourceFrameCount);
                await File.WriteAllBytesAsync(
                    outputPath,
                    new byte[outputLength],
                    cancellationToken);
                return metadata;
            }
        }
    }
}
