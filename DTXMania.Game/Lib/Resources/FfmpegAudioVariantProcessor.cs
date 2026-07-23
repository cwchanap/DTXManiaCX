#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Stage.Performance;
using FFMpegCore;
using FFMpegCore.Exceptions;
using FFMpegCore.Pipes;

namespace DTXMania.Game.Lib.Resources
{
    /// <summary>
    /// Uses FFmpeg's atempo filter to compensate for the runtime pitch shift.
    /// The emitted PCM keeps the source pitch; SongTimer/MonoGame applies the
    /// requested pitch while restoring the final gameplay duration.
    /// </summary>
    public sealed class FfmpegAudioVariantProcessor : IAudioVariantProcessor
    {
        private static readonly HashSet<string> SupportedExtensions =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ".wav",
                ".mp3",
                ".ogg",
                ".xa",
            };

        private static readonly SemaphoreSlim SharedTransformGate = new(2, 2);

        private readonly ConcurrentDictionary<
            PreparationKey,
            WaiterSharedOperation<PreparedAudioArtifact>> _inFlight = new();
        private readonly IAudioVariantBackend _backend;
        private readonly Func<FfmpegRuntimeAvailability> _runtimeAvailability;
        private readonly SemaphoreSlim _transformGate;
        private readonly TimeSpan _operationTimeout;
        private readonly string _temporaryDirectory;

        public FfmpegAudioVariantProcessor()
            : this(
                new FfmpegCoreAudioVariantBackend(),
                FfmpegRuntime.EnsureConfigured,
                SharedTransformGate,
                TimeSpan.FromSeconds(60),
                Path.Combine(
                    Path.GetTempPath(),
                    "DTXManiaCX",
                    "PlaybackAudioVariants"))
        {
        }

        internal FfmpegAudioVariantProcessor(
            IAudioVariantBackend backend,
            Func<FfmpegRuntimeAvailability> runtimeAvailability,
            SemaphoreSlim transformGate,
            TimeSpan operationTimeout,
            string temporaryDirectory)
        {
            _backend = backend ?? throw new ArgumentNullException(nameof(backend));
            _runtimeAvailability =
                runtimeAvailability ?? throw new ArgumentNullException(nameof(runtimeAvailability));
            _transformGate =
                transformGate ?? throw new ArgumentNullException(nameof(transformGate));
            if (operationTimeout <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(operationTimeout));
            if (string.IsNullOrWhiteSpace(temporaryDirectory))
                throw new ArgumentException(
                    "A temporary directory is required.",
                    nameof(temporaryDirectory));

            _operationTimeout = operationTimeout;
            _temporaryDirectory = temporaryDirectory;
        }

        public async Task<PreparedAudioArtifact> PrepareAsync(
            string sourcePath,
            PlaybackModifiers modifiers,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateRequest(sourcePath, modifiers);

            var fullSourcePath = Path.GetFullPath(sourcePath);
            var key = new PreparationKey(fullSourcePath, modifiers);
            WaiterSharedOperation<PreparedAudioArtifact> operation;
            while (true)
            {
                operation = _inFlight.GetOrAdd(
                    key,
                    _ => new WaiterSharedOperation<PreparedAudioArtifact>(
                        operationToken => PrepareCoreAsync(
                            fullSourcePath,
                            modifiers,
                            operationToken)));
                if (operation.TryAddWaiter())
                    break;

                RemoveInFlightIfCurrent(key, operation);
            }

            try
            {
                return await operation
                    .GetTask()
                    .WaitAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                if (operation.ReleaseWaiter())
                    RemoveInFlightIfCurrent(key, operation);
            }
        }

        private void RemoveInFlightIfCurrent(
            PreparationKey key,
            WaiterSharedOperation<PreparedAudioArtifact> operation)
        {
            ((ICollection<KeyValuePair<
                PreparationKey,
                WaiterSharedOperation<PreparedAudioArtifact>>>)_inFlight)
                .Remove(new KeyValuePair<
                    PreparationKey,
                    WaiterSharedOperation<PreparedAudioArtifact>>(key, operation));
        }

        internal static IReadOnlyList<double> BuildAtempoFactors(double tempoFactor)
        {
            if (!double.IsFinite(tempoFactor) || tempoFactor <= 0.0)
                throw new ArgumentOutOfRangeException(nameof(tempoFactor));

            const double epsilon = 1e-12;
            var remaining = tempoFactor;
            var factors = new List<double>();

            while (remaining < 0.5 - epsilon)
            {
                factors.Add(0.5);
                remaining /= 0.5;
            }

            while (remaining > 2.0 + epsilon)
            {
                factors.Add(2.0);
                remaining /= 2.0;
            }

            factors.Add(Math.Clamp(remaining, 0.5, 2.0));
            return factors;
        }

        internal static string BuildAtempoFilter(double tempoFactor)
        {
            var factors = BuildAtempoFactors(tempoFactor);
            var parts = new string[factors.Count];
            for (var index = 0; index < factors.Count; index++)
            {
                parts[index] =
                    "atempo=" + factors[index].ToString(
                        "0.###############",
                        CultureInfo.InvariantCulture);
            }

            return string.Join(",", parts);
        }

        internal static string BuildXaInputArguments(
            int sampleRate,
            int channelCount)
        {
            return FormattableString.Invariant(
                $"-f s16le -ar {sampleRate} -ac {channelCount}");
        }

        internal static string BuildPaddedAtempoFilter(
            double tempoFactor,
            int sampleRate,
            long sourceFrameCount)
        {
            var atempoFilter = BuildAtempoFilter(tempoFactor);
            if (sampleRate <= 0)
                throw new ArgumentOutOfRangeException(nameof(sampleRate));
            if (sourceFrameCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(sourceFrameCount));

            var targetFrameCount = Math.Max(
                1L,
                checked((long)Math.Ceiling(sourceFrameCount / tempoFactor)));
            return FormattableString.Invariant(
                $"apad=pad_len={sampleRate},{atempoFilter},atrim=end_sample={targetFrameCount}");
        }

        private async Task<PreparedAudioArtifact> PrepareCoreAsync(
            string sourcePath,
            PlaybackModifiers modifiers,
            CancellationToken cancellationToken)
        {
            var runtime = _runtimeAvailability();
            if (!runtime.IsAvailable)
            {
                throw CreateFailure(
                    AudioVariantPreparationFailure.RuntimeUnavailable,
                    sourcePath,
                    modifiers,
                    runtime.DiagnosticReason ?? "FFmpeg runtime is unavailable.");
            }

            var acquired = false;
            string? temporaryRawPath = null;
            try
            {
                await _transformGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                acquired = true;

                using var timeout = new CancellationTokenSource(_operationTimeout);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    timeout.Token);

                Directory.CreateDirectory(_temporaryDirectory);
                temporaryRawPath = Path.Combine(
                    _temporaryDirectory,
                    $"variant-{Guid.NewGuid():N}.s16le.tmp");

                AudioTransformMetadata metadata;
                try
                {
                    metadata = await _backend.TransformAsync(
                        sourcePath,
                        temporaryRawPath,
                        modifiers,
                        linked.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (
                    cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (OperationCanceledException ex) when (
                    timeout.IsCancellationRequested)
                {
                    throw CreateFailure(
                        AudioVariantPreparationFailure.TimedOut,
                        sourcePath,
                        modifiers,
                        $"Audio preparation exceeded {_operationTimeout.TotalSeconds:0.###} seconds.",
                        ex);
                }
                catch (AudioVariantBackendException ex)
                {
                    throw CreateFailure(
                        ex.Failure,
                        sourcePath,
                        modifiers,
                        ex.Message,
                        ex);
                }

                byte[] pcm;
                try
                {
                    pcm = await File.ReadAllBytesAsync(
                        temporaryRawPath,
                        linked.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (
                    cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (OperationCanceledException ex) when (
                    timeout.IsCancellationRequested)
                {
                    throw CreateFailure(
                        AudioVariantPreparationFailure.TimedOut,
                        sourcePath,
                        modifiers,
                        $"Audio preparation exceeded {_operationTimeout.TotalSeconds:0.###} seconds.",
                        ex);
                }
                catch (IOException ex)
                {
                    throw CreateFailure(
                        AudioVariantPreparationFailure.InvalidOutput,
                        sourcePath,
                        modifiers,
                        $"FFmpeg did not produce readable PCM output: {ex.Message}",
                        ex);
                }

                ValidatePcm(pcm, metadata, sourcePath, modifiers);

                try
                {
                    return new PreparedAudioArtifact(
                        metadata.SampleRate,
                        metadata.ChannelCount,
                        pcm);
                }
                catch (InvalidDataException ex)
                {
                    throw CreateFailure(
                        AudioVariantPreparationFailure.InvalidOutput,
                        sourcePath,
                        modifiers,
                        $"Prepared PCM metadata is invalid: {ex.Message}",
                        ex);
                }
            }
            finally
            {
                if (temporaryRawPath != null)
                {
                    try
                    {
                        File.Delete(temporaryRawPath);
                    }
                    catch (IOException)
                    {
                        // Cleanup is best-effort; the unique file is never a cache hit.
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // Cleanup is best-effort; the unique file is never a cache hit.
                    }
                }

                if (acquired)
                    _transformGate.Release();
            }
        }

        private static void ValidateRequest(
            string sourcePath,
            PlaybackModifiers modifiers)
        {
            if (string.IsNullOrWhiteSpace(sourcePath))
                throw new ArgumentException("A source path is required.", nameof(sourcePath));

            if (modifiers.IsDefault)
            {
                throw CreateFailure(
                    AudioVariantPreparationFailure.DefaultProfile,
                    sourcePath,
                    modifiers,
                    "The exact default profile must bypass audio preparation.");
            }

            if (!File.Exists(sourcePath))
            {
                throw CreateFailure(
                    AudioVariantPreparationFailure.SourceNotFound,
                    sourcePath,
                    modifiers,
                    $"Audio source was not found: {sourcePath}");
            }

            var extension = Path.GetExtension(sourcePath);
            if (!SupportedExtensions.Contains(extension))
            {
                throw CreateFailure(
                    AudioVariantPreparationFailure.UnsupportedFormat,
                    sourcePath,
                    modifiers,
                    $"Unsupported audio source extension: {extension}");
            }
        }

        private static void ValidatePcm(
            byte[] pcm,
            AudioTransformMetadata metadata,
            string sourcePath,
            PlaybackModifiers modifiers)
        {
            var frameSize = metadata.ChannelCount * sizeof(short);
            if (pcm.Length == 0 ||
                metadata.ChannelCount is < 1 or > 2 ||
                metadata.SampleRate is < 8000 or > 48000 ||
                pcm.Length % frameSize != 0)
            {
                throw CreateFailure(
                    AudioVariantPreparationFailure.InvalidOutput,
                    sourcePath,
                    modifiers,
                    "FFmpeg produced empty, unsupported, or frame-misaligned PCM.");
            }
        }

        private static AudioVariantPreparationException CreateFailure(
            AudioVariantPreparationFailure failure,
            string sourcePath,
            PlaybackModifiers modifiers,
            string message,
            Exception? innerException = null)
        {
            return new AudioVariantPreparationException(
                failure,
                sourcePath,
                modifiers,
                message,
                innerException);
        }

        private readonly record struct PreparationKey(
            string SourcePath,
            PlaybackModifiers Modifiers);
    }

    internal readonly record struct AudioTransformMetadata(
        int SampleRate,
        int ChannelCount,
        long SourceFrameCount = 44100);

    internal interface IAudioVariantBackend
    {
        Task<AudioTransformMetadata> TransformAsync(
            string sourcePath,
            string outputPath,
            PlaybackModifiers modifiers,
            CancellationToken cancellationToken);
    }

    internal sealed class AudioVariantBackendException : Exception
    {
        public AudioVariantBackendException(
            AudioVariantPreparationFailure failure,
            string message,
            Exception? innerException = null)
            : base(message, innerException)
        {
            Failure = failure;
        }

        public AudioVariantPreparationFailure Failure { get; }
    }

    internal sealed class FfmpegCoreAudioVariantBackend : IAudioVariantBackend
    {
        public async Task<AudioTransformMetadata> TransformAsync(
            string sourcePath,
            string outputPath,
            PlaybackModifiers modifiers,
            CancellationToken cancellationToken)
        {
            List<MemoryStream>? xaStreams = null;
            try
            {
                Func<FFMpegArguments> createArguments;
                AudioTransformMetadata metadata;
                if (string.Equals(
                    Path.GetExtension(sourcePath),
                    ".xa",
                    StringComparison.OrdinalIgnoreCase))
                {
                    XaDecodedSound decoded;
                    try
                    {
                        decoded = XaDecoder.Decode(
                            await File.ReadAllBytesAsync(
                                sourcePath,
                                cancellationToken).ConfigureAwait(false));
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex) when (
                        ex is InvalidDataException or IOException)
                    {
                        throw new AudioVariantBackendException(
                            AudioVariantPreparationFailure.DecodeFailed,
                            $"XA decoding failed: {ex.Message}",
                            ex);
                    }

                    metadata = NormalizeMetadata(
                        decoded.SampleRate,
                        decoded.Channels,
                        decoded.PcmData.LongLength /
                        (decoded.Channels * sizeof(short)));
                    xaStreams = new List<MemoryStream>(2);
                    createArguments = () =>
                    {
                        var stream =
                            new MemoryStream(decoded.PcmData, writable: false);
                        xaStreams.Add(stream);
                        return FFMpegArguments.FromPipeInput(
                            new StreamPipeSource(stream),
                            options => options.WithCustomArgument(
                                FfmpegAudioVariantProcessor.BuildXaInputArguments(
                                    decoded.SampleRate,
                                    decoded.Channels)));
                    };
                }
                else
                {
                    try
                    {
                        var analysis = await FFProbe.AnalyseAsync(
                            sourcePath,
                            cancellationToken: cancellationToken).ConfigureAwait(false);
                        var audioStream = analysis.PrimaryAudioStream;
                        if (audioStream == null)
                        {
                            throw new InvalidDataException(
                                "The source contains no audio stream.");
                        }

                        metadata = NormalizeMetadata(
                            audioStream.SampleRateHz,
                            audioStream.Channels,
                            analysis.Duration);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex) when (
                        ex is FFProbeException or FFMpegException or InvalidDataException)
                    {
                        throw new AudioVariantBackendException(
                            AudioVariantPreparationFailure.ProbeFailed,
                            $"Audio probing failed: {ex.Message}",
                            ex);
                    }

                    createArguments =
                        () => FFMpegArguments.FromFileInput(sourcePath);
                }

                var filter =
                    FfmpegAudioVariantProcessor.BuildAtempoFilter(
                        modifiers.FfmpegTempoFactor);

                try
                {
                    await ProcessAsync(
                        createArguments(),
                        outputPath,
                        metadata,
                        filter,
                        cancellationToken).ConfigureAwait(false);

                    if (!File.Exists(outputPath) ||
                        new FileInfo(outputPath).Length == 0)
                    {
                        File.Delete(outputPath);
                        var paddedFilter =
                            FfmpegAudioVariantProcessor.BuildPaddedAtempoFilter(
                                modifiers.FfmpegTempoFactor,
                                metadata.SampleRate,
                                metadata.SourceFrameCount);
                        await ProcessAsync(
                            createArguments(),
                            outputPath,
                            metadata,
                            paddedFilter,
                            cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (FFMpegException ex)
                {
                    throw new AudioVariantBackendException(
                        AudioVariantPreparationFailure.TransformFailed,
                        $"FFmpeg transformation failed: {ex.Message}",
                        ex);
                }

                return metadata;
            }
            finally
            {
                if (xaStreams != null)
                {
                    foreach (var stream in xaStreams)
                        stream.Dispose();
                }
            }
        }

        private static async Task ProcessAsync(
            FFMpegArguments arguments,
            string outputPath,
            AudioTransformMetadata metadata,
            string filter,
            CancellationToken cancellationToken)
        {
            await arguments
                .OutputToFile(
                    outputPath,
                    overwrite: false,
                    options => options
                        .WithAudioCodec("pcm_s16le")
                        .WithAudioSamplingRate(metadata.SampleRate)
                        .WithCustomArgument($"-ac {metadata.ChannelCount}")
                        .WithCustomArgument($"-af \"{filter}\"")
                        .ForceFormat("s16le"))
                .CancellableThrough(cancellationToken)
                .ProcessAsynchronously()
                .ConfigureAwait(false);
        }

        private static AudioTransformMetadata NormalizeMetadata(
            int sampleRate,
            int channelCount,
            long sourceFrameCount)
        {
            var normalizedSampleRate =
                sampleRate is >= 8000 and <= 48000 ? sampleRate : 44100;
            var normalizedChannelCount = channelCount == 1 ? 1 : 2;
            return new AudioTransformMetadata(
                normalizedSampleRate,
                normalizedChannelCount,
                Math.Max(1, sourceFrameCount));
        }

        private static AudioTransformMetadata NormalizeMetadata(
            int sampleRate,
            int channelCount,
            TimeSpan duration)
        {
            var normalizedSampleRate =
                sampleRate is >= 8000 and <= 48000 ? sampleRate : 44100;
            var sourceFrameCount = checked((long)Math.Ceiling(
                Math.Max(0.0, duration.TotalSeconds) *
                normalizedSampleRate));
            return NormalizeMetadata(
                sampleRate,
                channelCount,
                sourceFrameCount);
        }
    }
}