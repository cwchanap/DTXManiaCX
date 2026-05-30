using System;
using System.Buffers.Binary;
using System.IO;

namespace DTXMania.Game.Lib.Resources
{
    internal readonly struct XaDecodedSound
    {
        public XaDecodedSound(byte[] pcmData, int sampleRate, int channels)
        {
            PcmData = pcmData;
            SampleRate = sampleRate;
            Channels = channels;
        }

        public byte[] PcmData { get; }
        public int SampleRate { get; }
        public int Channels { get; }
    }

    internal static class XaDecoder
    {
        private const int HeaderSize = 32;
        private const uint HeaderMagic = 0x3144574B;
        private const int BlockSamples = 32;

        private static readonly short[,] GainFactor =
        {
            { 0, 0 },
            { 240, 0 },
            { 460, -208 },
            { 392, -220 },
            { 488, -240 },
        };

        public static XaDecodedSound Decode(byte[] xaData)
        {
            if (xaData == null)
                throw new ArgumentNullException(nameof(xaData));

            if (xaData.Length < HeaderSize)
                throw new InvalidDataException("XA data is too short to contain a KWD1 header.");

            var magic = BinaryPrimitives.ReadUInt32LittleEndian(xaData.AsSpan(0, 4));
            var dataLength = BinaryPrimitives.ReadUInt32LittleEndian(xaData.AsSpan(4, 4));
            var samples = BinaryPrimitives.ReadUInt32LittleEndian(xaData.AsSpan(8, 4));
            var sampleRate = BinaryPrimitives.ReadUInt16LittleEndian(xaData.AsSpan(12, 2));
            var bits = xaData[14];
            var channels = xaData[15];

            if (magic != HeaderMagic)
                throw new InvalidDataException("Invalid XA header magic.");

            if (dataLength == 0 || samples == 0 || sampleRate == 0)
                throw new InvalidDataException("Invalid XA header values.");

            if (channels != 1 && channels != 2)
                throw new InvalidDataException($"Unsupported XA channel count: {channels}.");

            if (bits != 4 && bits != 6 && bits != 8)
                throw new InvalidDataException($"Unsupported XA bit depth: {bits}.");

            var blockSizePerChannel = bits * 4 + 1;
            var compressedBlockSize = blockSizePerChannel * channels;

            if (dataLength % compressedBlockSize != 0)
                throw new InvalidDataException("XA data length is not block-aligned.");

            var availableSamples = (long)BlockSamples * dataLength / compressedBlockSize;
            if (samples > availableSamples || availableSamples - samples >= BlockSamples)
                throw new InvalidDataException("XA sample count does not match compressed data length.");

            if (xaData.Length < HeaderSize + dataLength)
                throw new InvalidDataException("XA data is shorter than the length declared by the header.");

            var totalPcmSamples = checked((int)(samples * channels));
            var pcmSamples = new short[totalPcmSamples];
            var blockPcm = new short[BlockSamples * channels];
            var channelStates = new[]
            {
                new ChannelState(
                    BinaryPrimitives.ReadInt16LittleEndian(xaData.AsSpan(20, 2)),
                    BinaryPrimitives.ReadInt16LittleEndian(xaData.AsSpan(22, 2))),
                new ChannelState(
                    BinaryPrimitives.ReadInt16LittleEndian(xaData.AsSpan(24, 2)),
                    BinaryPrimitives.ReadInt16LittleEndian(xaData.AsSpan(26, 2))),
            };

            var compressedOffset = HeaderSize;
            var framesWritten = 0;
            var blockCount = dataLength / compressedBlockSize;

            for (var block = 0; block < blockCount && framesWritten < samples; block++)
            {
                Array.Clear(blockPcm, 0, blockPcm.Length);

                for (var channel = 0; channel < channels; channel++)
                {
                    var blockData = xaData.AsSpan(compressedOffset, blockSizePerChannel);
                    var profile = InflateBlock(blockData, bits, blockPcm, channel, channels);
                    DecodeInflatedBlock(blockPcm, channel, channels, profile, channelStates[channel]);
                    compressedOffset += blockSizePerChannel;
                }

                var framesToCopy = Math.Min(BlockSamples, (int)samples - framesWritten);
                Array.Copy(blockPcm, 0, pcmSamples, framesWritten * channels, framesToCopy * channels);
                framesWritten += framesToCopy;
            }

            var pcmBytes = new byte[pcmSamples.Length * sizeof(short)];
            for (var i = 0; i < pcmSamples.Length; i++)
            {
                BinaryPrimitives.WriteInt16LittleEndian(pcmBytes.AsSpan(i * sizeof(short), sizeof(short)), pcmSamples[i]);
            }

            return new XaDecodedSound(pcmBytes, sampleRate, channels);
        }

        private static byte InflateBlock(ReadOnlySpan<byte> source, int bits, short[] destination, int channel, int channels)
        {
            var profile = source[0];
            var sourceOffset = 1;
            var destinationOffset = channel;

            switch (bits)
            {
                case 4:
                    for (var sample = 0; sample < BlockSamples; sample += 2)
                    {
                        var packed = source[sourceOffset++];
                        destination[destinationOffset] = (short)((packed & 0xF0) << 8);
                        destinationOffset += channels;
                        destination[destinationOffset] = (short)((packed & 0x0F) << 12);
                        destinationOffset += channels;
                    }
                    break;

                case 6:
                    for (var sample = 0; sample < BlockSamples; sample += 4)
                    {
                        var packed = (source[sourceOffset] << 16) |
                            (source[sourceOffset + 1] << 8) |
                            source[sourceOffset + 2];
                        sourceOffset += 3;

                        destination[destinationOffset] = (short)((packed & 0x00FC0000) >> 8);
                        destinationOffset += channels;
                        destination[destinationOffset] = (short)((packed & 0x0003F000) >> 2);
                        destinationOffset += channels;
                        destination[destinationOffset] = (short)((packed & 0x00000FC0) << 4);
                        destinationOffset += channels;
                        destination[destinationOffset] = (short)((packed & 0x0000003F) << 10);
                        destinationOffset += channels;
                    }
                    break;

                case 8:
                    for (var sample = 0; sample < BlockSamples; sample++)
                    {
                        destination[destinationOffset] = (short)(source[sourceOffset++] << 8);
                        destinationOffset += channels;
                    }
                    break;
            }

            return profile;
        }

        private static void DecodeInflatedBlock(short[] pcm, int channel, int channels, byte profile, ChannelState state)
        {
            var factor = profile >> 4;
            var range = profile & 0x0F;

            if (factor >= GainFactor.GetLength(0))
                throw new InvalidDataException($"Invalid XA gain factor: {factor}.");

            var gain0 = GainFactor[factor, 0];
            var gain1 = GainFactor[factor, 1];
            var offset = channel;

            for (var sampleIndex = 0; sampleIndex < BlockSamples; sampleIndex++)
            {
                var ranged = pcm[offset] >> range;
                var gain = (state.Prev0 * gain0) + (state.Prev1 * gain1);
                var sample = ranged + gain / 256;
                sample = Math.Clamp(sample, short.MinValue, short.MaxValue);

                pcm[offset] = (short)sample;
                state.Prev1 = state.Prev0;
                state.Prev0 = (short)sample;

                offset += channels;
            }
        }

        private sealed class ChannelState
        {
            public ChannelState(short prev0, short prev1)
            {
                Prev0 = prev0;
                Prev1 = prev1;
            }

            public short Prev0 { get; set; }
            public short Prev1 { get; set; }
        }
    }
}
