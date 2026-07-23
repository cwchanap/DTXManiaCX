using System;
using System.Buffers.Binary;
using System.IO;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Resources;
using Xunit;

namespace DTXMania.Test.Resources
{
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