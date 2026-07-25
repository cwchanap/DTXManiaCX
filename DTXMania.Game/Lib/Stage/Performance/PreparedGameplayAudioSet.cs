#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Resources;
using Microsoft.Xna.Framework.Audio;

namespace DTXMania.Game.Lib.Stage.Performance
{
    /// <summary>
    /// Sole owner of every sound used by one performance activation.
    /// Playback components borrow the exposed views and own only their instances.
    /// </summary>
    public sealed class PreparedGameplayAudioSet : IDisposable
    {
        public const long DefaultDecodedPcmBudgetBytes = 512L * 1024L * 1024L;

        private readonly IReadOnlyList<ISound> _ownedSounds;
        private bool _disposed;

        private PreparedGameplayAudioSet(
            ISound? mainBackground,
            IReadOnlyDictionary<string, ISound> scheduledBgmBySourcePath,
            IReadOnlyDictionary<string, ISound> chipSoundsByWavId,
            IReadOnlyList<ISound> ownedSounds,
            float runtimePitch,
            long decodedPcmBytes)
        {
            MainBackground = mainBackground;
            ScheduledBgmBySourcePath = scheduledBgmBySourcePath;
            ChipSoundsByWavId = chipSoundsByWavId;
            _ownedSounds = ownedSounds;
            RuntimePitch = runtimePitch;
            DecodedPcmBytes = decodedPcmBytes;
        }

        public ISound? MainBackground { get; }

        public IReadOnlyDictionary<string, ISound> ScheduledBgmBySourcePath { get; }

        public IReadOnlyDictionary<string, ISound> ChipSoundsByWavId { get; }

        public float RuntimePitch { get; }

        public long DecodedPcmBytes { get; }

        public static Task<PreparedGameplayAudioSet> PrepareAsync(
            string? mainBackgroundPath,
            IEnumerable<string> scheduledBgmSourcePaths,
            IReadOnlyDictionary<string, string> chipSourcePathsByWavId,
            PlaybackModifiers modifiers,
            IAudioVariantProcessor? processor,
            PlaybackAudioVariantCache? cache,
            IProgress<AudioPreparationProgress>? progress,
            CancellationToken cancellationToken,
            long decodedPcmBudgetBytes = DefaultDecodedPcmBudgetBytes)
        {
            return PrepareAsync(
                mainBackgroundPath,
                scheduledBgmSourcePaths,
                chipSourcePathsByWavId,
                modifiers,
                processor,
                cache,
                progress,
                cancellationToken,
                LoadDefaultSound,
                CreatePreparedSound,
                decodedPcmBudgetBytes);
        }

        internal static async Task<PreparedGameplayAudioSet> PrepareAsync(
            string? mainBackgroundPath,
            IEnumerable<string> scheduledBgmSourcePaths,
            IReadOnlyDictionary<string, string> chipSourcePathsByWavId,
            PlaybackModifiers modifiers,
            IAudioVariantProcessor? processor,
            PlaybackAudioVariantCache? cache,
            IProgress<AudioPreparationProgress>? progress,
            CancellationToken cancellationToken,
            Func<string, ISound> defaultSoundLoader,
            Func<PreparedAudioArtifact, string, ISound> preparedSoundFactory,
            long decodedPcmBudgetBytes = DefaultDecodedPcmBudgetBytes)
        {
            ArgumentNullException.ThrowIfNull(scheduledBgmSourcePaths);
            ArgumentNullException.ThrowIfNull(chipSourcePathsByWavId);
            ArgumentNullException.ThrowIfNull(defaultSoundLoader);
            ArgumentNullException.ThrowIfNull(preparedSoundFactory);
            if (decodedPcmBudgetBytes <= 0)
                throw new ArgumentOutOfRangeException(nameof(decodedPcmBudgetBytes));
            if (!modifiers.IsDefault)
            {
                ArgumentNullException.ThrowIfNull(processor);
                ArgumentNullException.ThrowIfNull(cache);
            }

            var pathComparer = OperatingSystem.IsWindows()
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;
            var mainPath = NormalizeExistingPath(mainBackgroundPath);
            var scheduledPaths = scheduledBgmSourcePaths
                .Select(NormalizeExistingPath)
                .Where(path => path != null)
                .Cast<string>()
                .Distinct(pathComparer)
                .ToArray();
            var chipPaths = chipSourcePathsByWavId
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
                .Select(pair => (pair.Key, Path: NormalizeExistingPath(pair.Value)))
                .Where(pair => pair.Path != null)
                .Select(pair => (pair.Key, Path: pair.Path!))
                .ToArray();

            var allPaths = new HashSet<string>(pathComparer);
            if (mainPath != null)
                allPaths.Add(mainPath);
            foreach (var path in scheduledPaths)
                allPaths.Add(path);
            foreach (var pair in chipPaths)
                allPaths.Add(pair.Path);

            var stopwatch = Stopwatch.StartNew();
            var cacheHits = 0;
            var completed = 0;
            long decodedBytes = 0;
            var soundsByPath = new Dictionary<string, ISound>(pathComparer);

            try
            {
                if (modifiers.IsDefault)
                {
                    // The default profile bypasses variant preparation and loads each
                    // source through the original loaders (SoundEffect.FromStream for
                    // WAV — which handles MS-ADPCM/IMA-ADPCM natively — and FFMpeg-
                    // normalized MP3). The PCM decode path used for variant profiles
                    // cannot handle ADPCM WAVs or MP3s whose source rate exceeds
                    // MonoGame's 48 kHz SoundEffect ceiling, so routing the default
                    // profile through it regresses existing song compatibility.
                    // Per-sound failures — including the main background track — are
                    // skipped to mirror the legacy per-sound loaders, which left a
                    // missing BGM/chip unplayed rather than failing the whole
                    // performance (Invariant #2: silent-clock fallback). A corrupt
                    // main BGM therefore yields a null MainBackground and the
                    // performance stage falls back to the silent GameTime clock,
                    // matching DTXMania's original behavior.
                    //
                    // Each load is dispatched to the thread pool via Task.Run so the
                    // synchronous ManagedSound decode (OGG/MP3/ADPCM-WAV) does not
                    // run on the MonoGame update thread. PrepareAsync is awaited from
                    // PerformanceStage.InitializeGameplayCoreAsync, which is launched
                    // from OnActivate on the game thread; when a parsed chart is
                    // supplied via shared data there is no prior await, so without
                    // this dispatch the entire default-profile audio set would load
                    // synchronously on the update thread and freeze render/input
                    // until preparation completes.
                    foreach (var sourcePath in allPaths)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var currentRole = ResolveRole(
                            sourcePath,
                            mainPath,
                            scheduledPaths,
                            pathComparer);
                        progress?.Report(new AudioPreparationProgress(
                            completed,
                            allPaths.Count,
                            currentRole,
                            cacheHits,
                            stopwatch.Elapsed,
                            decodedBytes));

                        try
                        {
                            soundsByPath[sourcePath] = await Task.Run(
                                () => defaultSoundLoader(sourcePath),
                                cancellationToken).ConfigureAwait(false);
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            Debug.WriteLine(
                                $"[PreparedGameplayAudioSet] Skipping failed default-profile sound '{sourcePath}': {ex.Message}");
                        }

                        completed++;
                        progress?.Report(new AudioPreparationProgress(
                            completed,
                            allPaths.Count,
                            currentRole,
                            cacheHits,
                            stopwatch.Elapsed,
                            decodedBytes));
                    }
                }
                else
                {
                    var artifacts = new Dictionary<string, PreparedAudioArtifact>(pathComparer);
                    foreach (var sourcePath in allPaths)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var currentRole = ResolveRole(
                            sourcePath,
                            mainPath,
                            scheduledPaths,
                            pathComparer);
                        progress?.Report(new AudioPreparationProgress(
                            completed,
                            allPaths.Count,
                            currentRole,
                            cacheHits,
                            stopwatch.Elapsed,
                            decodedBytes));

                        var key = await AudioVariantKey.CreateAsync(
                            sourcePath,
                            modifiers,
                            cancellationToken).ConfigureAwait(false);
                        // A single get-or-create call replaces the prior
                        // TryGetAsync + GetOrCreateAsync pair, which performed
                        // a redundant disk read on every cache miss and failed
                        // to count race-resolved reads (a miss that another
                        // waiter published before this caller's factory ran)
                        // as cache hits. GetOrCreateWithStatusAsync reports
                        // hit/miss for both the direct disk hit and the
                        // in-flight race-resolved hit.
                        var lookup = await cache!.GetOrCreateWithStatusAsync(
                            key,
                            (_, token) => processor!.PrepareAsync(
                                sourcePath,
                                modifiers,
                                token),
                            cancellationToken).ConfigureAwait(false);
                        var artifact = lookup.Artifact;
                        if (lookup.CacheHit)
                        {
                            cacheHits++;
                        }

                        artifacts[sourcePath] = artifact;
                        decodedBytes = checked(decodedBytes + artifact.PcmByteLength);
                        completed++;
                        progress?.Report(new AudioPreparationProgress(
                            completed,
                            allPaths.Count,
                            currentRole,
                            cacheHits,
                            stopwatch.Elapsed,
                            decodedBytes));

                        if (decodedBytes > decodedPcmBudgetBytes)
                        {
                            throw new AudioPreparationBudgetExceededException(
                                decodedBytes,
                                decodedPcmBudgetBytes);
                        }
                    }

                    // The budget is checked before this point so no SoundEffect is
                    // allocated for an over-budget profile.
                    //
                    // Each artifact is removed from the dictionary immediately after
                    // its SoundEffect is constructed. SoundEffect copies the supplied
                    // buffer internally (see CreatePreparedSound), so retaining the
                    // artifact entries past this point keeps every decoded PCM array
                    // alive simultaneously alongside the SoundEffect copies. Near the
                    // 512 MiB session budget that roughly doubles peak memory
                    // (artifact arrays + SoundEffect-owned copies) and can OOM an
                    // otherwise valid profile despite passing the budget check.
                    // Removing the entry drops the only in-memory reference (the
                    // cache is disk-based and does not retain artifacts in memory),
                    // so the GC can reclaim each buffer before the next artifact is
                    // converted.
                    foreach (var sourcePath in allPaths)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var artifact = artifacts[sourcePath];
                        soundsByPath[sourcePath] =
                            preparedSoundFactory(artifact, sourcePath);
                        artifacts.Remove(sourcePath);
                    }
                }

                var scheduled = scheduledPaths
                    .Where(path => soundsByPath.ContainsKey(path))
                    .ToDictionary(
                        path => path,
                        path => soundsByPath[path],
                        pathComparer);
                var chips = chipPaths
                    .Where(pair => soundsByPath.ContainsKey(pair.Path))
                    .ToDictionary(
                        pair => pair.Key,
                        pair => soundsByPath[pair.Path],
                        StringComparer.OrdinalIgnoreCase);
                return new PreparedGameplayAudioSet(
                    mainPath != null && soundsByPath.TryGetValue(mainPath, out var mainSound)
                        ? mainSound
                        : null,
                    scheduled,
                    chips,
                    soundsByPath.Values
                        .Distinct<ISound>(ReferenceEqualityComparer.Instance)
                        .ToArray(),
                    modifiers.MonoGamePitch,
                    decodedBytes);
            }
            catch
            {
                foreach (var sound in soundsByPath.Values
                    .Distinct<ISound>(ReferenceEqualityComparer.Instance))
                {
                    try { sound.Dispose(); }
                    catch { }
                }
                throw;
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            foreach (var sound in _ownedSounds)
            {
                try { sound.Dispose(); }
                catch { }
            }
        }

        private static ISound LoadDefaultSound(string sourcePath)
        {
            return new ManagedSound(sourcePath, sourcePath);
        }

        private static ISound CreatePreparedSound(
            PreparedAudioArtifact artifact,
            string sourcePath)
        {
            var channels = artifact.ChannelCount == 1
                ? AudioChannels.Mono
                : AudioChannels.Stereo;
            // Pass the artifact's backing buffer directly to SoundEffect
            // instead of cloning via PcmData.ToArray(). SoundEffect copies
            // the buffer internally, so the intermediate clone was a wasted
            // hundreds-of-megabytes allocation for payloads near the 512 MiB
            // per-artifact ceiling. PcmDataBuffer is the artifact's own
            // immutable storage; SoundEffect does not mutate it.
            var effect = new SoundEffect(
                artifact.PcmDataBuffer,
                artifact.SampleRate,
                channels);
            return new ManagedSound(effect, sourcePath);
        }

        private static string? NormalizeExistingPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;
            var fullPath = Path.GetFullPath(path);
            if (File.Exists(fullPath))
                return fullPath;

            Debug.WriteLine(
                $"[PreparedGameplayAudioSet] Referenced audio source was not found: {fullPath}");
            return null;
        }

        private static string ResolveRole(
            string sourcePath,
            string? mainPath,
            IReadOnlyCollection<string> scheduledPaths,
            StringComparer comparer)
        {
            if (mainPath != null && comparer.Equals(sourcePath, mainPath))
                return "background";
            if (scheduledPaths.Contains(sourcePath, comparer))
                return "scheduled BGM";
            return "chip";
        }
    }

    public sealed class AudioPreparationBudgetExceededException : Exception
    {
        public AudioPreparationBudgetExceededException(long decodedBytes, long budgetBytes)
            : base($"Prepared audio requires {decodedBytes} bytes; the session budget is {budgetBytes} bytes.")
        {
            DecodedBytes = decodedBytes;
            BudgetBytes = budgetBytes;
        }

        public long DecodedBytes { get; }

        public long BudgetBytes { get; }
    }
}