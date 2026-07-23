using System;
using System.Buffers.Binary;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace DTXMania.Game.Lib.Resources
{
    /// <summary>
    /// Versioned MonoGame-compatible signed 16-bit little-endian PCM artifact.
    /// </summary>
    public sealed class PreparedAudioArtifact
    {
        private static readonly byte[] Magic = "DTXCXPCM"u8.ToArray();
        private readonly byte[] _pcmData;

        public const int CurrentVersion = 1;
        public const string FileExtension = ".dtxpcm";
        public const int MagicLength = 8;
        public const int VersionOffset = MagicLength;
        public const int SampleRateOffset = VersionOffset + sizeof(int);
        public const int ChannelCountOffset = SampleRateOffset + sizeof(int);
        public const int PcmLengthOffset = ChannelCountOffset + sizeof(int);
        public const int HeaderLength = PcmLengthOffset + sizeof(long);
        public const int MinimumSampleRate = 8000;
        public const int MaximumSampleRate = 48000;

        public PreparedAudioArtifact(
            int sampleRate,
            int channelCount,
            ReadOnlyMemory<byte> pcmData)
            : this(sampleRate, channelCount, pcmData.ToArray(), takeOwnership: true)
        {
        }

        private PreparedAudioArtifact(
            int sampleRate,
            int channelCount,
            byte[] pcmData,
            bool takeOwnership)
        {
            Validate(sampleRate, channelCount, pcmData.LongLength);

            SampleRate = sampleRate;
            ChannelCount = channelCount;
            _pcmData = takeOwnership ? pcmData : (byte[])pcmData.Clone();
        }

        public int SampleRate { get; }

        public int ChannelCount { get; }

        public ReadOnlyMemory<byte> PcmData => _pcmData;

        public long PcmByteLength => _pcmData.LongLength;

        public async Task WriteAsync(
            string filePath,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("Artifact path cannot be null or blank.", nameof(filePath));

            cancellationToken.ThrowIfCancellationRequested();
            var fullPath = Path.GetFullPath(filePath);
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var temporaryPath = fullPath + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                var header = CreateHeader();
                await using (var stream = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 81920,
                    FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    await stream.WriteAsync(header, cancellationToken);
                    await stream.WriteAsync(PcmData, cancellationToken);
                    await stream.FlushAsync(cancellationToken);
                }

                cancellationToken.ThrowIfCancellationRequested();
                File.Move(temporaryPath, fullPath, overwrite: true);
            }
            catch
            {
                TryDelete(temporaryPath);
                throw;
            }
        }

        public static async Task<PreparedAudioArtifact> ReadAsync(
            string filePath,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("Artifact path cannot be null or blank.", nameof(filePath));

            await using var stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (stream.Length < HeaderLength)
                throw new InvalidDataException("Prepared audio artifact header is truncated.");

            var header = new byte[HeaderLength];
            await ReadExactlyAsync(stream, header, cancellationToken);

            if (!header.AsSpan(0, MagicLength).SequenceEqual(Magic))
                throw new InvalidDataException("Prepared audio artifact magic is invalid.");

            var version = BinaryPrimitives.ReadInt32LittleEndian(
                header.AsSpan(VersionOffset, sizeof(int)));
            if (version != CurrentVersion)
            {
                throw new InvalidDataException(
                    $"Prepared audio artifact version {version} is unsupported.");
            }

            var sampleRate = BinaryPrimitives.ReadInt32LittleEndian(
                header.AsSpan(SampleRateOffset, sizeof(int)));
            var channelCount = BinaryPrimitives.ReadInt32LittleEndian(
                header.AsSpan(ChannelCountOffset, sizeof(int)));
            var declaredLength = BinaryPrimitives.ReadInt64LittleEndian(
                header.AsSpan(PcmLengthOffset, sizeof(long)));

            Validate(sampleRate, channelCount, declaredLength);
            if (declaredLength > int.MaxValue)
                throw new InvalidDataException("Prepared PCM payload is too large.");

            var expectedLength = checked((long)HeaderLength + declaredLength);
            if (stream.Length != expectedLength)
            {
                throw new InvalidDataException(
                    $"Prepared PCM length mismatch: declared {declaredLength}, " +
                    $"file contains {stream.Length - HeaderLength} payload bytes.");
            }

            var pcmData = new byte[(int)declaredLength];
            await ReadExactlyAsync(stream, pcmData, cancellationToken);
            return new PreparedAudioArtifact(
                sampleRate,
                channelCount,
                pcmData,
                takeOwnership: true);
        }

        private byte[] CreateHeader()
        {
            var header = new byte[HeaderLength];
            Magic.CopyTo(header, 0);
            BinaryPrimitives.WriteInt32LittleEndian(
                header.AsSpan(VersionOffset, sizeof(int)),
                CurrentVersion);
            BinaryPrimitives.WriteInt32LittleEndian(
                header.AsSpan(SampleRateOffset, sizeof(int)),
                SampleRate);
            BinaryPrimitives.WriteInt32LittleEndian(
                header.AsSpan(ChannelCountOffset, sizeof(int)),
                ChannelCount);
            BinaryPrimitives.WriteInt64LittleEndian(
                header.AsSpan(PcmLengthOffset, sizeof(long)),
                PcmByteLength);
            return header;
        }

        private static void Validate(
            int sampleRate,
            int channelCount,
            long pcmByteLength)
        {
            if (sampleRate < MinimumSampleRate || sampleRate > MaximumSampleRate)
            {
                throw new InvalidDataException(
                    $"Sample rate {sampleRate} is outside the supported " +
                    $"{MinimumSampleRate}..{MaximumSampleRate} Hz range.");
            }

            if (channelCount is not (1 or 2))
                throw new InvalidDataException("Only mono and stereo PCM are supported.");
            if (pcmByteLength <= 0)
                throw new InvalidDataException("Prepared PCM payload cannot be empty.");
            if ((pcmByteLength & 1) != 0)
                throw new InvalidDataException("Prepared PCM payload must contain 16-bit samples.");

            var frameSize = sizeof(short) * channelCount;
            if (pcmByteLength % frameSize != 0)
            {
                throw new InvalidDataException(
                    "Prepared PCM payload is not aligned to a complete audio frame.");
            }
        }

        private static async Task ReadExactlyAsync(
            Stream stream,
            Memory<byte> buffer,
            CancellationToken cancellationToken)
        {
            var offset = 0;
            while (offset < buffer.Length)
            {
                var read = await stream.ReadAsync(buffer[offset..], cancellationToken);
                if (read == 0)
                    throw new InvalidDataException("Prepared audio artifact payload is truncated.");
                offset += read;
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // Best effort: the final artifact was never published.
            }
        }
    }
}