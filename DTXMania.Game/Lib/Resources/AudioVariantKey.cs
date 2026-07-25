using System;
using System.Collections.Concurrent;
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

        /// <summary>
        /// Memoized content fingerprints keyed by canonical path. Each entry
        /// records the file length and last-write time so a stale entry is
        /// invalidated when the source changes. This avoids re-hashing the
        /// entire file on every warm-cache lookup, which for large BGMs
        /// (50–100 MB+) would blow the &lt;2 s warm-prep budget on every song
        /// entry at non-default speed.
        /// </summary>
        /// <remarks>
        /// Trade-off: a file replaced with same-size, same-mtime content would
        /// return a stale fingerprint. This is low-probability for game audio
        /// assets and recoverable by clearing the variant cache directory.
        /// The cache is bounded by <see cref="MaxFingerprintCacheEntries"/>;
        /// when exceeded it is cleared wholesale so fingerprints are recomputed
        /// on next access, preventing unbounded growth across a long session.
        /// </remarks>
        private static readonly ConcurrentDictionary<string, FingerprintEntry> _fingerprintCache =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Maximum number of source paths whose SHA-256 fingerprints are
        /// memoized before the cache is cleared. 512 is generous for typical
        /// play sessions; clearing is crude but rare and prevents unbounded
        /// memory growth.
        /// </summary>
        private const int MaxFingerprintCacheEntries = 512;

        private sealed record FingerprintEntry(long Length, DateTime LastWriteTimeUtc, string Sha256Hex);

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

            var fullPath = Path.GetFullPath(sourcePath);
            var info = new FileInfo(fullPath);
            var length = info.Length;
            var lastWrite = info.LastWriteTimeUtc;

            var sha256Hex = await GetOrComputeFingerprintAsync(
                fullPath, length, lastWrite, cancellationToken).ConfigureAwait(false);

            return new AudioVariantKey(
                sha256Hex,
                GetDecoderIdentity(sourcePath),
                PlaySpeedRange.SnapAndClamp(modifiers.PlaySpeedPercent),
                PitchRange.SnapAndClamp(modifiers.PitchSemitones),
                pipelineVersion);
        }

        /// <summary>
        /// Returns a memoized SHA-256 fingerprint for <paramref name="fullPath"/>
        /// when the cached entry still matches the file's length and mtime;
        /// otherwise reads and hashes the file and updates the cache.
        /// </summary>
        private static async Task<string> GetOrComputeFingerprintAsync(
            string fullPath,
            long length,
            DateTime lastWriteUtc,
            CancellationToken cancellationToken)
        {
            if (_fingerprintCache.TryGetValue(fullPath, out var existing) &&
                existing.Length == length &&
                existing.LastWriteTimeUtc == lastWriteUtc)
            {
                return existing.Sha256Hex;
            }

            await using var stream = new FileStream(
                fullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var fingerprint = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
            var hex = Convert.ToHexString(fingerprint).ToLowerInvariant();

            // Bound the static cache: clear wholesale when the entry count
            // exceeds the threshold so the cache cannot grow unbounded across
            // a long session with many unique source paths. Fingerprints are
            // recomputed on next access. The race between Count and Clear is
            // benign — at worst two concurrent callers both clear.
            if (_fingerprintCache.Count >= MaxFingerprintCacheEntries)
            {
                _fingerprintCache.Clear();
            }

            _fingerprintCache[fullPath] = new FingerprintEntry(length, lastWriteUtc, hex);
            return hex;
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