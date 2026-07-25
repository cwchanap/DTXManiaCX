#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Stage.Performance;
using DTXMania.Game.Lib.Utilities;

namespace DTXMania.Game.Lib.Resources
{
    /// <summary>
    /// Content-addressed disk cache for prepared playback-audio artifacts.
    /// </summary>
    public sealed class PlaybackAudioVariantCache
    {
        public const long DefaultMaxCacheBytes = 1024L * 1024L * 1024L;

        private readonly long _maxCacheBytes;
        private readonly ConcurrentDictionary<
            AudioVariantKey,
            WaiterSharedOperation<(PreparedAudioArtifact Artifact, bool CacheHit)>> _inFlight = new();
        private long _bytesWrittenSincePrune;

        /// <summary>
        /// Result of a get-or-create lookup, distinguishing a disk-cache hit
        /// from a freshly prepared artifact. Used by callers that track
        /// cache-hit statistics so race-resolved hits (a miss that another
        /// waiter published before this caller's factory ran) are counted as
        /// hits rather than misses.
        /// </summary>
        public readonly record struct CacheLookupResult(
            PreparedAudioArtifact Artifact,
            bool CacheHit);

        public PlaybackAudioVariantCache(
            string? cacheRoot = null,
            long maxCacheBytes = DefaultMaxCacheBytes)
        {
            if (maxCacheBytes <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxCacheBytes));

            CacheRoot = Path.GetFullPath(
                cacheRoot ?? AppPaths.GetPlaybackAudioCacheRoot());
            _maxCacheBytes = maxCacheBytes;

            Directory.CreateDirectory(CacheRoot);
            CleanupTemporaryFilesBestEffort();
            PruneBestEffort();
        }

        public string CacheRoot { get; }

        public async Task<PreparedAudioArtifact> GetOrCreateAsync(
            string sourcePath,
            PlaybackModifiers modifiers,
            Func<
                AudioVariantKey,
                CancellationToken,
                Task<PreparedAudioArtifact>> factory,
            CancellationToken cancellationToken)
        {
            var key = await AudioVariantKey.CreateAsync(
                sourcePath,
                modifiers,
                cancellationToken);
            return await GetOrCreateAsync(key, factory, cancellationToken);
        }

        public async Task<PreparedAudioArtifact> GetOrCreateAsync(
            AudioVariantKey key,
            Func<
                AudioVariantKey,
                CancellationToken,
                Task<PreparedAudioArtifact>> factory,
            CancellationToken cancellationToken)
        {
            return (await GetOrCreateWithStatusAsync(
                key, factory, cancellationToken).ConfigureAwait(false)).Artifact;
        }

        /// <summary>
        /// Gets a cached artifact or creates and stores a new one, reporting
        /// whether the result came from the disk cache (CacheHit=true) or was
        /// freshly prepared by the factory (CacheHit=false). A miss that
        /// another concurrent waiter published before this caller's factory
        /// ran is reported as a hit, since no factory work was performed.
        /// </summary>
        public async Task<CacheLookupResult> GetOrCreateWithStatusAsync(
            AudioVariantKey key,
            Func<
                AudioVariantKey,
                CancellationToken,
                Task<PreparedAudioArtifact>> factory,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(key);
            ArgumentNullException.ThrowIfNull(factory);

            var cached = await TryReadAsync(key, cancellationToken);
            if (cached != null)
                return new CacheLookupResult(cached, CacheHit: true);

            WaiterSharedOperation<(PreparedAudioArtifact Artifact, bool CacheHit)> operation;
            while (true)
            {
                operation = _inFlight.GetOrAdd(
                    key,
                    currentKey => new WaiterSharedOperation<(PreparedAudioArtifact, bool)>(
                        operationToken => CreateAndStoreAsync(
                            currentKey,
                            factory,
                            operationToken)));
                if (operation.TryAddWaiter())
                    break;

                RemoveInFlightIfCurrent(key, operation);
            }

            try
            {
                var result = await operation
                    .GetTask()
                    .WaitAsync(cancellationToken)
                    .ConfigureAwait(false);
                return new CacheLookupResult(result.Artifact, result.CacheHit);
            }
            finally
            {
                if (operation.ReleaseWaiter())
                    RemoveInFlightIfCurrent(key, operation);
            }
        }

        public string GetArtifactPath(AudioVariantKey key)
        {
            ArgumentNullException.ThrowIfNull(key);
            return Path.Combine(CacheRoot, key.ToCacheFileName());
        }

        public Task<PreparedAudioArtifact?> TryGetAsync(
            AudioVariantKey key,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(key);
            return TryReadAsync(key, cancellationToken);
        }

        public void Prune()
        {
            PruneBestEffort();
        }

        private async Task<PreparedAudioArtifact?> TryReadAsync(
            AudioVariantKey key,
            CancellationToken cancellationToken)
        {
            var path = GetArtifactPath(key);
            if (!File.Exists(path))
                return null;

            try
            {
                var artifact = await PreparedAudioArtifact.ReadAsync(
                    path,
                    cancellationToken);
                TouchBestEffort(path);
                return artifact;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                DeleteBestEffort(path);
                return null;
            }
        }

        private async Task<(PreparedAudioArtifact Artifact, bool CacheHit)> CreateAndStoreAsync(
            AudioVariantKey key,
            Func<
                AudioVariantKey,
                CancellationToken,
                Task<PreparedAudioArtifact>> factory,
            CancellationToken cancellationToken)
        {
            // Close the miss-to-in-flight race: another request may have published
            // the artifact after this caller's first cache lookup but before its
            // lazy factory won the dictionary slot. Report that as a cache hit so
            // callers tracking hit/miss statistics count race-resolved reads.
            var cached = await TryReadAsync(key, cancellationToken);
            if (cached != null)
                return (cached, CacheHit: true);

            var artifact = await factory(key, cancellationToken)
                ?? throw new InvalidDataException(
                    "Audio variant factory returned no prepared artifact.");
            var path = GetArtifactPath(key);
            await artifact.WriteAsync(path, cancellationToken);
            TouchBestEffort(path);
            // Gate pruning on a byte threshold instead of running on every
            // write. PruneBestEffort enumerates all cache files, which is
            // wasteful when the cache is well under budget. Only prune when
            // the bytes written since the last prune exceed 10% of the budget.
            var bytesSince = Interlocked.Add(
                ref _bytesWrittenSincePrune,
                artifact.PcmByteLength);
            if (bytesSince >= _maxCacheBytes / 10)
            {
                Interlocked.Exchange(ref _bytesWrittenSincePrune, 0);
                PruneBestEffort();
            }
            return (artifact, CacheHit: false);
        }

        private void PruneBestEffort()
        {
            try
            {
                var files = Directory
                    .EnumerateFiles(
                        CacheRoot,
                        "*" + PreparedAudioArtifact.FileExtension,
                        SearchOption.TopDirectoryOnly)
                    .Select(path => new FileInfo(path))
                    .Where(file => file.Exists)
                    .OrderBy(file => file.LastAccessTimeUtc)
                    .ThenBy(file => file.Name, StringComparer.Ordinal)
                    .ToList();
                var totalBytes = files.Sum(file => file.Length);

                foreach (var file in files)
                {
                    if (totalBytes <= _maxCacheBytes)
                        break;

                    var length = file.Length;
                    try
                    {
                        file.Delete();
                        totalBytes -= length;
                    }
                    catch
                    {
                        // Cleanup is best effort; cache correctness does not rely on it.
                    }
                }
            }
            catch
            {
                // Cleanup is best effort; cache correctness does not rely on it.
            }
        }

        private void CleanupTemporaryFilesBestEffort()
        {
            try
            {
                foreach (var path in Directory.EnumerateFiles(
                    CacheRoot,
                    "*.tmp-*",
                    SearchOption.TopDirectoryOnly))
                {
                    DeleteBestEffort(path);
                }
            }
            catch
            {
                // Cleanup is best effort.
            }
        }

        private static void TouchBestEffort(string path)
        {
            try
            {
                File.SetLastAccessTimeUtc(path, DateTime.UtcNow);
            }
            catch
            {
                // Last-access updates only improve pruning quality.
            }
        }

        private static void DeleteBestEffort(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // Cache cleanup is best effort.
            }
        }

        private void RemoveInFlightIfCurrent(
            AudioVariantKey key,
            WaiterSharedOperation<(PreparedAudioArtifact Artifact, bool CacheHit)> operation)
        {
            ((ICollection<KeyValuePair<
                AudioVariantKey,
                WaiterSharedOperation<(PreparedAudioArtifact, bool)>>>)(_inFlight))
                .Remove(new KeyValuePair<
                    AudioVariantKey,
                    WaiterSharedOperation<(PreparedAudioArtifact, bool)>>(key, operation));
        }
    }
}