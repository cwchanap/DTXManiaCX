using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Stage.Performance;

namespace DTXMania.Game.Lib.Resources
{
    /// <summary>
    /// Content-addressed identity for one prepared playback-audio variant.
    /// </summary>
    public sealed record AudioVariantKey(
        string SourceContentSha256,
        string DecoderIdentity,
        int PlaySpeedPercent,
        int PitchSemitones,
        int PipelineVersion)
    {
        public const int CurrentPipelineVersion = 1;

        public static async Task<AudioVariantKey> CreateAsync(
            string sourcePath,
            PlaybackModifiers modifiers,
            CancellationToken cancellationToken = default,
            int pipelineVersion = CurrentPipelineVersion)
        {
            if (string.IsNullOrWhiteSpace(sourcePath))
                throw new ArgumentException("Source path cannot be null or blank.", nameof(sourcePath));
            if (pipelineVersion <= 0)
                throw new ArgumentOutOfRangeException(nameof(pipelineVersion));

            await using var stream = new FileStream(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var fingerprint = await SHA256.HashDataAsync(stream, cancellationToken);

            return new AudioVariantKey(
                Convert.ToHexString(fingerprint).ToLowerInvariant(),
                GetDecoderIdentity(sourcePath),
                PlaySpeedRange.SnapAndClamp(modifiers.PlaySpeedPercent),
                PitchRange.SnapAndClamp(modifiers.PitchSemitones),
                pipelineVersion);
        }

        public string ToCacheFileName()
        {
            var canonical = string.Join(
                "|",
                SourceContentSha256,
                DecoderIdentity,
                PlaySpeedPercent.ToString(CultureInfo.InvariantCulture),
                PitchSemitones.ToString(CultureInfo.InvariantCulture),
                PipelineVersion.ToString(CultureInfo.InvariantCulture));
            var digest = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
            return $"v{PipelineVersion}-{Convert.ToHexString(digest).ToLowerInvariant()}" +
                PreparedAudioArtifact.FileExtension;
        }

        private static string GetDecoderIdentity(string sourcePath)
        {
            var extension = Path.GetExtension(sourcePath)
                .TrimStart('.')
                .ToLowerInvariant();
            return extension switch
            {
                "xa" => "cx-xa-decoder+ffmpeg:s16le",
                "wav" or "mp3" or "ogg" => "ffmpeg:" + extension,
                "" => "ffmpeg:no-extension",
                _ => "ffmpeg:" + extension,
            };
        }
    }
}