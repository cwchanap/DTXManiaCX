using System;
using System.IO;
using DTXMania.Game.Lib.Resources;
using DTXMania.Test.Utilities;
using Xunit;

namespace DTXMania.Test.Resources
{
    public class XaDecoderTests : IDisposable
    {
        private readonly string _tempDir;

        public XaDecoderTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"XaDecoderTests_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, true);
        }

        private static byte[] BuildXaHeader(uint dataLength, uint samples, ushort sampleRate, byte bits, byte channels, uint magic = 0x3144574B)
        {
            var header = new byte[32];
            BitConverter.TryWriteBytes(header.AsSpan(0, 4), magic);
            BitConverter.TryWriteBytes(header.AsSpan(4, 4), dataLength);
            BitConverter.TryWriteBytes(header.AsSpan(8, 4), samples);
            BitConverter.TryWriteBytes(header.AsSpan(12, 2), sampleRate);
            header[14] = bits;
            header[15] = channels;
            return header;
        }

        [Fact]
        public void Decode_NullData_ShouldThrowArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => XaDecoder.Decode(null));
        }

        [Fact]
        public void Decode_TooShortData_ShouldThrowInvalidDataException()
        {
            var data = new byte[16];
            Assert.Throws<InvalidDataException>(() => XaDecoder.Decode(data));
        }

        [Fact]
        public void Decode_InvalidMagic_ShouldThrowInvalidDataException()
        {
            var data = BuildXaHeader(100, 32, 44100, 8, 1, magic: 0xDEADBEEF);
            Assert.Throws<InvalidDataException>(() => XaDecoder.Decode(data));
        }

        [Fact]
        public void Decode_ZeroDataLength_ShouldThrowInvalidDataException()
        {
            var data = BuildXaHeader(0, 32, 44100, 8, 1);
            Assert.Throws<InvalidDataException>(() => XaDecoder.Decode(data));
        }

        [Fact]
        public void Decode_ZeroSamples_ShouldThrowInvalidDataException()
        {
            var data = BuildXaHeader(33, 0, 44100, 8, 1);
            Assert.Throws<InvalidDataException>(() => XaDecoder.Decode(data));
        }

        [Fact]
        public void Decode_ZeroSampleRate_ShouldThrowInvalidDataException()
        {
            var data = BuildXaHeader(33, 32, 0, 8, 1);
            Assert.Throws<InvalidDataException>(() => XaDecoder.Decode(data));
        }

        [Fact]
        public void Decode_InvalidChannelCount_ShouldThrowInvalidDataException()
        {
            var data = BuildXaHeader(33, 32, 44100, 8, 3);
            Assert.Throws<InvalidDataException>(() => XaDecoder.Decode(data));
        }

        [Fact]
        public void Decode_InvalidBitDepth_ShouldThrowInvalidDataException()
        {
            var data = BuildXaHeader(33, 32, 44100, 2, 1);
            Assert.Throws<InvalidDataException>(() => XaDecoder.Decode(data));
        }

        [Fact]
        public void Decode_BlockMisaligned_ShouldThrowInvalidDataException()
        {
            var header = BuildXaHeader(7, 32, 44100, 8, 1);
            var data = new byte[32 + 7];
            Array.Copy(header, data, 32);
            Assert.Throws<InvalidDataException>(() => XaDecoder.Decode(data));
        }

        [Fact]
        public void Decode_TruncatedData_ShouldThrowInvalidDataException()
        {
            var header = BuildXaHeader(33, 32, 44100, 8, 1);
            var data = new byte[32 + 10];
            Array.Copy(header, data, 32);
            Assert.Throws<InvalidDataException>(() => XaDecoder.Decode(data));
        }

        [Fact]
        public void Decode_ValidMono8Bit_ShouldReturnDecodedSound()
        {
            var path = AudioTestUtils.CreateTestXaFile(Path.Combine(_tempDir, "test8.xa"), bits: 8, blocks: 2);
            var data = File.ReadAllBytes(path);
            var result = XaDecoder.Decode(data);
            Assert.True(result.PcmData.Length > 0);
            Assert.Equal(44100, result.SampleRate);
            Assert.Equal(1, result.Channels);
        }

        [Fact]
        public void Decode_ValidMono4Bit_ShouldReturnDecodedSound()
        {
            var path = AudioTestUtils.CreateTestXaFile(Path.Combine(_tempDir, "test4.xa"), bits: 4, blocks: 2);
            var data = File.ReadAllBytes(path);
            var result = XaDecoder.Decode(data);
            Assert.True(result.PcmData.Length > 0);
            Assert.Equal(44100, result.SampleRate);
            Assert.Equal(1, result.Channels);
        }

        [Fact]
        public void Decode_ValidMono6Bit_ShouldReturnDecodedSound()
        {
            var path = AudioTestUtils.CreateTestXaFile(Path.Combine(_tempDir, "test6.xa"), bits: 6, blocks: 2);
            var data = File.ReadAllBytes(path);
            var result = XaDecoder.Decode(data);
            Assert.True(result.PcmData.Length > 0);
            Assert.Equal(44100, result.SampleRate);
            Assert.Equal(1, result.Channels);
        }
    }
}
