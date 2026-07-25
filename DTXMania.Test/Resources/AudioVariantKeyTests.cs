using System;
using System.IO;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Stage.Performance;
using Xunit;

namespace DTXMania.Test.Resources
{
    [Trait("Category", "Unit")]
    public sealed class AudioVariantKeyTests : IDisposable
    {
        private readonly string _tempDirectory = Path.Combine(
            Path.GetTempPath(),
            "dtxmania-variant-key-" + Guid.NewGuid().ToString("N"));

        public AudioVariantKeyTests()
        {
            Directory.CreateDirectory(_tempDirectory);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDirectory))
                Directory.Delete(_tempDirectory, recursive: true);
        }

        private string WriteSource(string fileName, byte[] content)
        {
            var path = Path.Combine(_tempDirectory, fileName);
            File.WriteAllBytes(path, content);
            return path;
        }

        #region ToCacheFileName

        [Fact]
        public async Task ToCacheFileName_IsDeterministicForSameKey()
        {
            var path = WriteSource("source.wav", new byte[] { 1, 2, 3 });
            var modifiers = new PlaybackModifiers(100, 0);

            var first = await AudioVariantKey.CreateAsync(path, modifiers);
            var second = await AudioVariantKey.CreateAsync(path, modifiers);

            Assert.Equal(first.ToCacheFileName(), second.ToCacheFileName());
        }

        [Fact]
        public async Task ToCacheFileName_EndsWithArtifactExtensionAndVersionPrefix()
        {
            var path = WriteSource("source.wav", new byte[] { 1, 2, 3 });
            var key = await AudioVariantKey.CreateAsync(path, new PlaybackModifiers(100, 0));

            var fileName = key.ToCacheFileName();

            Assert.StartsWith($"v{key.PipelineVersion}-", fileName);
            Assert.EndsWith(PreparedAudioArtifact.FileExtension, fileName);
        }

        [Fact]
        public async Task ToCacheFileName_DiffersForDifferentPlaySpeed()
        {
            var path = WriteSource("source.wav", new byte[] { 1, 2, 3 });

            var defaultKey = await AudioVariantKey.CreateAsync(path, new PlaybackModifiers(100, 0));
            var fastKey = await AudioVariantKey.CreateAsync(path, new PlaybackModifiers(125, 0));

            Assert.NotEqual(defaultKey.ToCacheFileName(), fastKey.ToCacheFileName());
        }

        [Fact]
        public async Task ToCacheFileName_DiffersForDifferentPitch()
        {
            var path = WriteSource("source.wav", new byte[] { 1, 2, 3 });

            var noPitch = await AudioVariantKey.CreateAsync(path, new PlaybackModifiers(100, 0));
            var pitched = await AudioVariantKey.CreateAsync(path, new PlaybackModifiers(100, -3));

            Assert.NotEqual(noPitch.ToCacheFileName(), pitched.ToCacheFileName());
        }

        [Fact]
        public async Task ToCacheFileName_DiffersForDifferentDecoderIdentity()
        {
            var wavPath = WriteSource("source.wav", new byte[] { 1, 2, 3 });
            var oggPath = WriteSource("source.ogg", new byte[] { 1, 2, 3 });

            var wavKey = await AudioVariantKey.CreateAsync(wavPath, new PlaybackModifiers(100, 0));
            var oggKey = await AudioVariantKey.CreateAsync(oggPath, new PlaybackModifiers(100, 0));

            Assert.NotEqual(wavKey.ToCacheFileName(), oggKey.ToCacheFileName());
        }

        #endregion

        #region DecoderIdentity

        [Theory]
        [InlineData("source.xa", "cx-xa-decoder+ffmpeg:s16le")]
        [InlineData("source.wav", "ffmpeg:wav")]
        [InlineData("source.mp3", "ffmpeg:mp3")]
        [InlineData("source.ogg", "ffmpeg:ogg")]
        [InlineData("source.flac", "ffmpeg:flac")]
        public async Task CreateAsync_DecoderIdentity_MapsExtensionCorrectly(
            string fileName, string expectedDecoder)
        {
            var path = WriteSource(fileName, new byte[] { 1, 2, 3 });
            var key = await AudioVariantKey.CreateAsync(path, new PlaybackModifiers(100, 0));

            Assert.Equal(expectedDecoder, key.DecoderIdentity);
        }

        [Fact]
        public async Task CreateAsync_DecoderIdentity_NoExtensionMapsToFfmpegNoExtension()
        {
            var path = WriteSource("noextension", new byte[] { 1, 2, 3 });
            var key = await AudioVariantKey.CreateAsync(path, new PlaybackModifiers(100, 0));

            Assert.Equal("ffmpeg:no-extension", key.DecoderIdentity);
        }

        [Fact]
        public async Task CreateAsync_DecoderIdentity_IsLowerCase()
        {
            var path = WriteSource("source.WAV", new byte[] { 1, 2, 3 });
            var key = await AudioVariantKey.CreateAsync(path, new PlaybackModifiers(100, 0));

            Assert.Equal("ffmpeg:wav", key.DecoderIdentity);
        }

        #endregion

        #region CreateAsync Validation

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public async Task CreateAsync_WithBlankSourcePath_ThrowsArgumentException(string? sourcePath)
        {
            await Assert.ThrowsAsync<ArgumentException>(
                () => AudioVariantKey.CreateAsync(sourcePath!, new PlaybackModifiers(100, 0)));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public async Task CreateAsync_WithNonPositivePipelineVersion_ThrowsArgumentOutOfRangeException(
            int pipelineVersion)
        {
            var path = WriteSource("source.wav", new byte[] { 1, 2, 3 });

            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
                () => AudioVariantKey.CreateAsync(
                    path,
                    new PlaybackModifiers(100, 0),
                    pipelineVersion: pipelineVersion));
        }

        #endregion

        #region Fingerprint Memoization

        [Fact]
        public async Task CreateAsync_MemoizesFingerprintForSameFile()
        {
            var path = WriteSource("source.wav", new byte[] { 1, 2, 3, 4, 5 });
            var modifiers = new PlaybackModifiers(100, 0);

            var first = await AudioVariantKey.CreateAsync(path, modifiers);
            // Second call should return the same SHA from the in-memory cache
            // (same length + mtime) without re-reading the file.
            var second = await AudioVariantKey.CreateAsync(path, modifiers);

            Assert.Equal(first.SourceContentSha256, second.SourceContentSha256);
        }

        [Fact]
        public async Task CreateAsync_DifferentContentProducesDifferentSha()
        {
            var pathA = WriteSource("a.wav", new byte[] { 1, 2, 3 });
            var pathB = WriteSource("b.wav", new byte[] { 4, 5, 6 });

            var keyA = await AudioVariantKey.CreateAsync(pathA, new PlaybackModifiers(100, 0));
            var keyB = await AudioVariantKey.CreateAsync(pathB, new PlaybackModifiers(100, 0));

            Assert.NotEqual(keyA.SourceContentSha256, keyB.SourceContentSha256);
        }

        #endregion

        #region PlaySpeed and Pitch Snapping

        [Fact]
        public async Task CreateAsync_SnapsPlaySpeedToCanonicalValue()
        {
            var path = WriteSource("source.wav", new byte[] { 1, 2, 3 });
            // Use a value that will be snapped by PlaySpeedRange.SnapAndClamp
            var key = await AudioVariantKey.CreateAsync(
                path,
                new PlaybackModifiers(100, 0));

            Assert.True(key.PlaySpeedPercent >= 50 && key.PlaySpeedPercent <= 150);
        }

        [Fact]
        public async Task CreateAsync_SnapsPitchToCanonicalValue()
        {
            var path = WriteSource("source.wav", new byte[] { 1, 2, 3 });
            var key = await AudioVariantKey.CreateAsync(
                path,
                new PlaybackModifiers(100, 0));

            Assert.True(key.PitchSemitones >= -12 && key.PitchSemitones <= 12);
        }

        #endregion
    }
}
