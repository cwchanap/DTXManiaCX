using System;
using System.Buffers.Binary;
using System.IO;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Resources;
using Xunit;

namespace DTXMania.Test.Resources
{
    [Trait("Category", "Unit")]
    public sealed class PreparedAudioArtifactTests : IDisposable
    {
        private readonly string _tempDirectory = Path.Combine(
            Path.GetTempPath(),
            "dtxmania-prepared-artifact-" + Guid.NewGuid().ToString("N"));

        public PreparedAudioArtifactTests()
        {
            Directory.CreateDirectory(_tempDirectory);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDirectory))
                Directory.Delete(_tempDirectory, recursive: true);
        }

        [Fact]
        public async Task WriteAndReadAsync_RoundTripsHeaderAndRawPcm()
        {
            var path = Path.Combine(_tempDirectory, "tone.dtxpcm");
            var pcm = new byte[] { 0x01, 0x02, 0x03, 0x04 };
            var artifact = new PreparedAudioArtifact(44100, 2, pcm);

            await artifact.WriteAsync(path);
            var roundTrip = await PreparedAudioArtifact.ReadAsync(path);

            Assert.Equal(44100, roundTrip.SampleRate);
            Assert.Equal(2, roundTrip.ChannelCount);
            Assert.Equal(4, roundTrip.PcmByteLength);
            Assert.Equal(pcm, roundTrip.PcmData.ToArray());
        }

        [Theory]
        [InlineData(7999, 1)]
        [InlineData(48001, 1)]
        [InlineData(44100, 0)]
        [InlineData(44100, 3)]
        public void Constructor_RejectsUnsupportedMetadata(int sampleRate, int channelCount)
        {
            Assert.Throws<InvalidDataException>(
                () => new PreparedAudioArtifact(sampleRate, channelCount, new byte[] { 0, 0 }));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void Constructor_RejectsEmptyOddOrFrameMisalignedPcm(int byteLength)
        {
            Assert.Throws<InvalidDataException>(
                () => new PreparedAudioArtifact(44100, 2, new byte[byteLength]));
        }

        [Fact]
        public async Task ReadAsync_RejectsInvalidMagic()
        {
            var path = await WriteValidArtifactAsync();
            var bytes = await File.ReadAllBytesAsync(path);
            bytes[0] ^= 0xFF;
            await File.WriteAllBytesAsync(path, bytes);

            await Assert.ThrowsAsync<InvalidDataException>(
                () => PreparedAudioArtifact.ReadAsync(path));
        }

        [Fact]
        public async Task ReadAsync_RejectsUnsupportedVersion()
        {
            var path = await WriteValidArtifactAsync();
            var bytes = await File.ReadAllBytesAsync(path);
            BinaryPrimitives.WriteInt32LittleEndian(
                bytes.AsSpan(PreparedAudioArtifact.MagicLength, sizeof(int)),
                PreparedAudioArtifact.CurrentVersion + 1);
            await File.WriteAllBytesAsync(path, bytes);

            await Assert.ThrowsAsync<InvalidDataException>(
                () => PreparedAudioArtifact.ReadAsync(path));
        }

        [Fact]
        public async Task ReadAsync_RejectsDeclaredLengthMismatchAndTruncation()
        {
            var path = await WriteValidArtifactAsync();
            var bytes = await File.ReadAllBytesAsync(path);
            BinaryPrimitives.WriteInt64LittleEndian(
                bytes.AsSpan(PreparedAudioArtifact.PcmLengthOffset, sizeof(long)),
                100);
            await File.WriteAllBytesAsync(path, bytes);

            await Assert.ThrowsAsync<InvalidDataException>(
                () => PreparedAudioArtifact.ReadAsync(path));
        }

        [Fact]
        public async Task ReadAsync_RejectsTrailingPayloadBeyondDeclaredLength()
        {
            var path = await WriteValidArtifactAsync();
            await using (var stream = new FileStream(path, FileMode.Append, FileAccess.Write))
            {
                await stream.WriteAsync(new byte[] { 0, 0 });
            }

            await Assert.ThrowsAsync<InvalidDataException>(
                () => PreparedAudioArtifact.ReadAsync(path));
        }

        [Fact]
        public async Task WriteAsync_WhenCancelled_LeavesNoFinalOrTemporaryArtifact()
        {
            var path = Path.Combine(_tempDirectory, "cancelled.dtxpcm");
            var artifact = new PreparedAudioArtifact(44100, 1, new byte[] { 0, 0 });
            using var cancellation = new System.Threading.CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => artifact.WriteAsync(path, cancellation.Token));

            Assert.False(File.Exists(path));
            Assert.Empty(Directory.GetFiles(_tempDirectory, "*.tmp-*"));
        }

        /// <summary>
        /// Regression guard for the duplicate-allocation optimization. The
        /// FFmpeg variant processor reads a PCM payload via
        /// File.ReadAllBytesAsync and then adopts it via
        /// PreparedAudioArtifact.FromOwnedBytes. The public constructor would
        /// clone the buffer (ToArray), requiring two simultaneous
        /// hundreds-of-megabytes allocations for a payload near the 512 MiB
        /// per-artifact ceiling. FromOwnedBytes must take ownership without
        /// cloning — the artifact's backing buffer must be the same array
        /// reference the caller passed.
        /// </summary>
        [Fact]
        public void FromOwnedBytes_TakesOwnershipWithoutCloning()
        {
            var pcm = new byte[] { 0x01, 0x02, 0x03, 0x04 };
            var artifact = PreparedAudioArtifact.FromOwnedBytes(44100, 2, pcm);

            Assert.Equal(44100, artifact.SampleRate);
            Assert.Equal(2, artifact.ChannelCount);
            Assert.Equal(4, artifact.PcmByteLength);
            // The backing buffer must be the exact same array reference —
            // no defensive clone. This is the whole point of the internal
            // ownership-taking factory.
            Assert.True(
                ReferenceEquals(pcm, artifact.PcmDataBuffer),
                "FromOwnedBytes must adopt the caller's array without cloning.");
            // The public PcmData view must still reflect the same contents.
            Assert.Equal(pcm, artifact.PcmData.ToArray());
        }

        /// <summary>
        /// The internal PcmDataBuffer accessor exists so trusted same-assembly
        /// callers (the SoundEffect construction path in
        /// PreparedGameplayAudioSet) can pass the backing byte[] directly
        /// instead of cloning via PcmData.ToArray(). Verify it returns the
        /// artifact's own backing storage (reference-equal), not a copy.
        /// </summary>
        [Fact]
        public void PcmDataBuffer_ReturnsBackingStorageWithoutCloning()
        {
            // The public constructor clones the input; the artifact owns its
            // own buffer. PcmDataBuffer must expose that owned buffer directly.
            var artifact = new PreparedAudioArtifact(
                44100,
                2,
                new byte[] { 0x01, 0x02, 0x03, 0x04 });

            var buffer = artifact.PcmDataBuffer;
            Assert.Equal(4, buffer.Length);
            // PcmDataBuffer must be the same reference as the internal backing
            // storage (not a copy), so callers can pass it to SoundEffect
            // without an intermediate ToArray() clone.
            Assert.True(
                ReferenceEquals(buffer, artifact.PcmDataBuffer),
                "PcmDataBuffer must return the same reference on each call.");
        }

        [Fact]
        public void FromOwnedBytes_NullBuffer_ShouldThrow()
        {
            Assert.Throws<ArgumentNullException>(
                () => PreparedAudioArtifact.FromOwnedBytes(44100, 1, null!));
        }

        private async Task<string> WriteValidArtifactAsync()
        {
            var path = Path.Combine(_tempDirectory, "valid.dtxpcm");
            var artifact = new PreparedAudioArtifact(
                44100,
                2,
                new byte[] { 0x00, 0x01, 0x02, 0x03 });
            await artifact.WriteAsync(path);
            return path;
        }
    }
}