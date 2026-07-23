using System;
using System.Buffers.Binary;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FFMpegCore;
using FFMpegCore.Pipes;
using NVorbis;

namespace DTXMania.Game.Lib.Resources
{
    /// <summary>
    /// Decodes an original gameplay-audio source to the exact signed 16-bit PCM
    /// representation that will be supplied to MonoGame.
    /// </summary>
    internal static class OriginalAudioPcmDecoder
    {
        public static Task<PreparedAudioArtifact> DecodeAsync(
            string sourcePath,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(sourcePath))
                throw new ArgumentException("Source path cannot be null or blank.", nameof(sourcePath));

            var fullPath = Path.GetFullPath(sourcePath);
            if (!File.Exists(fullPath))
                throw new FileNotFoundException($"Sound file not found: {fullPath}", fullPath);

            return Task.Run(
                () => Decode(fullPath, cancellationToken),
                cancellationToken);
        }

        private static PreparedAudioArtifact Decode(
            string sourcePath,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
            var artifact = extension switch
            {
                ".wav" => DecodeWav(sourcePath),
                ".ogg" => DecodeOgg(sourcePath, cancellationToken),
                ".mp3" => DecodeMp3(sourcePath, cancellationToken),
                ".xa" => DecodeXa(sourcePath),
                _ => throw new NotSupportedException(
                    $"Audio format not supported: {extension}"),
            };
            cancellationToken.ThrowIfCancellationRequested();
            return artifact;
        }

        private static PreparedAudioArtifact DecodeWav(string sourcePath)
        {
            var data = File.ReadAllBytes(sourcePath);
            if (data.Length < 12 ||
                !data.AsSpan(0, 4).SequenceEqual("RIFF"u8) ||
                !data.AsSpan(8, 4).SequenceEqual("WAVE"u8))
            {
                throw new InvalidDataException("WAV RIFF/WAVE header is invalid.");
            }

            ushort format = 0;
            ushort channels = 0;
            int sampleRate = 0;
            ushort bitsPerSample = 0;
            ReadOnlySpan<byte> sampleData = default;

            var offset = 12;
            while (offset <= data.Length - 8)
            {
                var chunkId = data.AsSpan(offset, 4);
                var chunkLength = BinaryPrimitives.ReadUInt32LittleEndian(
                    data.AsSpan(offset + 4, 4));
                var payloadOffset = checked(offset + 8);
                var payloadEnd = checked((long)payloadOffset + chunkLength);
                if (payloadEnd > data.Length)
                    throw new InvalidDataException("WAV chunk is truncated.");

                if (chunkId.SequenceEqual("fmt "u8))
                {
                    if (chunkLength < 16)
                        throw new InvalidDataException("WAV format chunk is truncated.");
                    var formatSpan = data.AsSpan(payloadOffset, (int)chunkLength);
                    format = BinaryPrimitives.ReadUInt16LittleEndian(formatSpan);
                    channels = BinaryPrimitives.ReadUInt16LittleEndian(formatSpan[2..]);
                    sampleRate = BinaryPrimitives.ReadInt32LittleEndian(formatSpan[4..]);
                    bitsPerSample = BinaryPrimitives.ReadUInt16LittleEndian(formatSpan[14..]);
                }
                else if (chunkId.SequenceEqual("data"u8))
                {
                    sampleData = data.AsSpan(payloadOffset, (int)chunkLength);
                }

                offset = checked((int)(payloadEnd + (chunkLength & 1)));
            }

            if (format != 1)
                throw new InvalidDataException($"Unsupported WAV format tag: {format}.");
            if (bitsPerSample != 16)
                throw new InvalidDataException(
                    $"Unsupported WAV bit depth: {bitsPerSample}. Only signed 16-bit PCM is supported.");
            if (sampleData.IsEmpty)
                throw new InvalidDataException("WAV contains no PCM sample data.");

            return new PreparedAudioArtifact(
                sampleRate,
                channels,
                sampleData.ToArray());
        }

        private static PreparedAudioArtifact DecodeOgg(
            string sourcePath,
            CancellationToken cancellationToken)
        {
            using var reader = new VorbisReader(sourcePath);
            var totalSampleValues = checked((int)(reader.TotalSamples * reader.Channels));
            var floatBuffer = new float[totalSampleValues];
            var samplesRead = 0;
            while (samplesRead < floatBuffer.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var read = reader.ReadSamples(
                    floatBuffer,
                    samplesRead,
                    floatBuffer.Length - samplesRead);
                if (read == 0)
                    break;
                samplesRead += read;
            }

            var pcmData = new byte[checked(samplesRead * sizeof(short))];
            for (var index = 0; index < samplesRead; index++)
            {
                var sample = Math.Clamp(floatBuffer[index], -1.0f, 1.0f);
                var pcmSample = sample <= -1.0f
                    ? short.MinValue
                    : (short)Math.Round(sample * short.MaxValue);
                BinaryPrimitives.WriteInt16LittleEndian(
                    pcmData.AsSpan(index * sizeof(short), sizeof(short)),
                    pcmSample);
            }

            return new PreparedAudioArtifact(
                reader.SampleRate,
                reader.Channels,
                pcmData);
        }

        private static PreparedAudioArtifact DecodeXa(string sourcePath)
        {
            var decoded = XaDecoder.Decode(File.ReadAllBytes(sourcePath));
            return new PreparedAudioArtifact(
                decoded.SampleRate,
                decoded.Channels,
                decoded.PcmData);
        }

        private static PreparedAudioArtifact DecodeMp3(
            string sourcePath,
            CancellationToken cancellationToken)
        {
            var runtime = FfmpegRuntime.EnsureConfigured();
            if (!runtime.IsAvailable)
                throw new InvalidOperationException(runtime.DiagnosticReason);

            cancellationToken.ThrowIfCancellationRequested();
            var mediaInfo = FFProbe.Analyse(sourcePath);
            var audioStream = mediaInfo.PrimaryAudioStream ??
                throw new InvalidOperationException(
                    $"No audio stream found in file: {sourcePath}");
            var channelCount = audioStream.Channels == 1 ? 1 : 2;

            using var outputStream = new MemoryStream();
            FFMpegArguments
                .FromFileInput(sourcePath)
                .OutputToPipe(new StreamPipeSink(outputStream), options => options
                    .WithAudioCodec("pcm_s16le")
                    .WithAudioSamplingRate(44100)
                    .WithCustomArgument($"-ac {channelCount}")
                    .ForceFormat("s16le"))
                .ProcessSynchronously();
            cancellationToken.ThrowIfCancellationRequested();

            if (outputStream.Length == 0)
                throw new InvalidOperationException("FFMpeg produced empty output stream.");

            return new PreparedAudioArtifact(
                sampleRate: 44100,
                channelCount,
                outputStream.ToArray());
        }
    }
}