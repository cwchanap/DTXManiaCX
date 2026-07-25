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
        private readonly TimeSpan _orphanStalenessThreshold;
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
            // Only sweep temp files proven stale by age. A file being actively
            // written by another processor instance (e.g. during rapid stage
            // reactivation where the cancelled processor's FFmpeg child is still
            // draining) will have a recent LastWriteTime; blindly deleting every
            // matching file from each constructor unlinks that open output on
            // macOS/Linux and the old operation later fails when it inspects or
            // reads the path. 2× the operation timeout is a safe staleness
            // threshold: any operation still running past its timeout has been
            // cancelled by the linked CTS in PrepareCoreAsync, and its finally
            // block has already attempted to delete its own temp file. A file
            // older than that can only be an orphan from a crashed/killed process.
            _orphanStalenessThreshold = operationTimeout + operationTimeout;
            CleanupOrphanedTempFilesBestEffort();
        }

        /// <summary>
        /// Sweeps orphaned <c>.s16le.tmp</c> files left by a previous process
        /// that crashed or was killed mid-transform. The per-call <c>finally</c>
        /// block in <see cref="PrepareCoreAsync"/> deletes its own temp file on
        /// normal exit, so only crash orphans remain. Each temp file carries a
        /// unique GUID, so name collisions never occur — but multiple processor
        /// instances can coexist (one per stage activation), and a cancelled
        /// processor's FFmpeg child may still be draining into its open output
        /// when the next processor is constructed. To avoid unlinking that live
        /// output, only files whose <see cref="FileInfo.LastWriteTime"/> is older
        /// than <see cref="_orphanStalenessThreshold"/> are deleted. A live
        /// operation cannot run longer than its timeout (the linked CTS cancels
        /// it), and the finally block deletes the file on cancellation/timeout,
        /// so any file older than 2× the timeout is provably an orphan.
        /// </summary>
        private void CleanupOrphanedTempFilesBestEffort()
        {
            try
            {
                if (!Directory.Exists(_temporaryDirectory))
                    return;

                var stalenessCutoff = DateTime.UtcNow - _orphanStalenessThreshold;
                foreach (var path in Directory.EnumerateFiles(
                    _temporaryDirectory,
                    "*.s16le.tmp",
                    SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        // Skip files that might still belong to an active
                        // operation (recent LastWriteTime). Only delete files
                        // proven stale by age.
                        if (File.GetLastWriteTimeUtc(path) > stalenessCutoff)
                            continue;
                        File.Delete(path);
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
            catch
            {
                // Cleanup is best effort; processor correctness does not rely on it.
            }
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

            var targetFrameCount = ComputeTargetFrameCount(tempoFactor, sourceFrameCount);
            return FormattableString.Invariant(
                $"apad=pad_len={sampleRate},{atempoFilter},atrim=end_sample={targetFrameCount}");
        }

        /// <summary>
        /// Computes the expected output byte count for one atempo transform:
        /// targetFrameCount × frameSize, where targetFrameCount is the ceiling
        /// of sourceFrameCount / tempoFactor and frameSize is channelCount × 2
        /// (pcm_s16le). Used to detect truncated FFmpeg output that is non-empty
        /// but shorter than the expected duration.
        /// </summary>
        internal static long ComputeExpectedOutputBytes(
            int channelCount,
            double tempoFactor,
            long sourceFrameCount)
        {
            if (channelCount is < 1 or > 2)
                throw new ArgumentOutOfRangeException(nameof(channelCount));
            if (!double.IsFinite(tempoFactor) || tempoFactor <= 0.0)
                throw new ArgumentOutOfRangeException(nameof(tempoFactor));
            if (sourceFrameCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(sourceFrameCount));

            var targetFrameCount = ComputeTargetFrameCount(tempoFactor, sourceFrameCount);
            return checked(targetFrameCount * channelCount * sizeof(short));
        }

        private static long ComputeTargetFrameCount(double tempoFactor, long sourceFrameCount)
        {
            return Math.Max(1L, checked((long)Math.Ceiling(sourceFrameCount / tempoFactor)));
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
                    // Bound the read before pulling the file into memory. A
                    // non-default profile on a long or slow source can produce
                    // PCM larger than the per-artifact budget; reading it all
                    // into a byte[] (and again inside PreparedAudioArtifact)
                    // risks OOM, and the only session-budget check runs after
                    // the cache has already published the artifact. Reject
                    // based on the on-disk length first.
                    var tempFileLength = new FileInfo(temporaryRawPath).Length;
                    if (tempFileLength > PreparedAudioArtifact.MaxPcmByteLength)
                    {
                        throw CreateFailure(
                            AudioVariantPreparationFailure.InvalidOutput,
                            sourcePath,
                            modifiers,
                            $"FFmpeg produced {tempFileLength} bytes of PCM, " +
                            $"exceeding the {PreparedAudioArtifact.MaxPcmByteLength} " +
                            $"byte per-artifact budget ceiling.");
                    }
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
                    // Take ownership of the freshly-read buffer instead of
                    // routing through the public constructor, which clones
                    // the payload. A payload near the 512 MiB per-artifact
                    // ceiling would otherwise require two simultaneous
                    // hundreds-of-megabytes allocations (the ReadAllBytesAsync
                    // buffer plus the defensive copy). FromOwnedBytes adopts
                    // the buffer directly; no second allocation is needed.
                    return PreparedAudioArtifact.FromOwnedBytes(
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

    internal readonly record struct AudioTransformMetadata
    {
        /// <summary>
        /// Fallback frame count (one second at the canonical 44.1 kHz rate) used
        /// when a caller does not supply a measured source frame count. This is
        /// only a sizing hint for the padded-atempo filter, not a playback value.
        /// </summary>
        public const long DefaultSourceFrameCount = 44100;

        public int SampleRate { get; init; }
        public int ChannelCount { get; init; }
        public long SourceFrameCount { get; init; } = DefaultSourceFrameCount;

        public AudioTransformMetadata(
            int sampleRate,
            int channelCount,
            long sourceFrameCount = DefaultSourceFrameCount)
        {
            SampleRate = sampleRate;
            ChannelCount = channelCount;
            SourceFrameCount = sourceFrameCount;
        }
    }

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

                    // Retry when the first pass produced no output or a severely
                    // truncated (less than 80% of expected) one. atempo at slow
                    // speeds can drop trailing samples on short chips, yielding a
                    // non-empty but incomplete PCM buffer. The 80% tolerance
                    // avoids false retries from FFProbe duration overestimation,
                    // which is common for short MP3s with LAME encoder
                    // delay/padding. The padded-atempo retry (apad + atrim)
                    // produces exactly targetFrameCount frames.
                    var expectedBytes = FfmpegAudioVariantProcessor
                        .ComputeExpectedOutputBytes(
                            metadata.ChannelCount,
                            modifiers.FfmpegTempoFactor,
                            metadata.SourceFrameCount);
                    var outputBytes = File.Exists(outputPath)
                        ? new FileInfo(outputPath).Length
                        : 0;
                    if (outputBytes < expectedBytes * 4 / 5)
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
                        .WithCustomArgument($"-af {filter}")
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
            var normalizedSampleRate = NormalizeSampleRate(sampleRate);
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
            var normalizedSampleRate = NormalizeSampleRate(sampleRate);
            var sourceFrameCount = checked((long)Math.Ceiling(
                Math.Max(0.0, duration.TotalSeconds) *
                normalizedSampleRate));
            return NormalizeMetadata(
                sampleRate,
                channelCount,
                sourceFrameCount);
        }

        /// <summary>
        /// Clamps a source sample rate into MonoGame's supported 8000–48000 Hz
        /// range, preserving the source rate when it is already valid. Rates
        /// above 48000 are clamped down to 48000 (not 44100) to retain maximum
        /// bandwidth; rates below 8000 are clamped up to 8000. An invalid/
        /// unknown rate (≤ 0, e.g. FFProbe reported nothing) falls back to
        /// 44100. This matches the ceiling enforced by
        /// <see cref="PreparedAudioArtifact.Validate"/> and MonoGame's
        /// SoundEffect, so a source plays at the same rate at default and
        /// non-default playback speeds.
        /// </summary>
        private static int NormalizeSampleRate(int sampleRate)
        {
            if (sampleRate <= 0)
                return 44100;
            return Math.Clamp(sampleRate, 8000, 48000);
        }
    }
}