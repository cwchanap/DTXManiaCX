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
                OriginalAudioPcmDecoder.DecodeAsync,
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
            Func<string, CancellationToken, Task<PreparedAudioArtifact>> directDecoder,
            Func<PreparedAudioArtifact, string, ISound> preparedSoundFactory,
            long decodedPcmBudgetBytes = DefaultDecodedPcmBudgetBytes)
        {
            ArgumentNullException.ThrowIfNull(scheduledBgmSourcePaths);
            ArgumentNullException.ThrowIfNull(chipSourcePathsByWavId);
            ArgumentNullException.ThrowIfNull(directDecoder);
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
            var artifacts = new Dictionary<string, PreparedAudioArtifact>(pathComparer);
            var cacheHits = 0;
            var completed = 0;
            long decodedBytes = 0;

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

                PreparedAudioArtifact artifact;
                if (modifiers.IsDefault)
                {
                    artifact = await directDecoder(
                        sourcePath,
                        cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    var key = await AudioVariantKey.CreateAsync(
                        sourcePath,
                        modifiers,
                        cancellationToken).ConfigureAwait(false);
                    artifact = await cache!.TryGetAsync(
                        key,
                        cancellationToken).ConfigureAwait(false);
                    if (artifact != null)
                    {
                        cacheHits++;
                    }
                    else
                    {
                        artifact = await cache.GetOrCreateAsync(
                            key,
                            (_, token) => processor!.PrepareAsync(
                                sourcePath,
                                modifiers,
                                token),
                            cancellationToken).ConfigureAwait(false);
                    }

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

            // The budget is checked before this point so no SoundEffect is allocated
            // for an over-budget profile.
            var soundsByPath = new Dictionary<string, ISound>(pathComparer);
            try
            {
                foreach (var sourcePath in allPaths)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    soundsByPath[sourcePath] =
                        preparedSoundFactory(artifacts[sourcePath], sourcePath);
                }

                var scheduled = scheduledPaths.ToDictionary(
                    path => path,
                    path => soundsByPath[path],
                    pathComparer);
                var chips = chipPaths.ToDictionary(
                    pair => pair.Key,
                    pair => soundsByPath[pair.Path],
                    StringComparer.OrdinalIgnoreCase);
                return new PreparedGameplayAudioSet(
                    mainPath != null ? soundsByPath[mainPath] : null,
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

        private static ISound CreatePreparedSound(
            PreparedAudioArtifact artifact,
            string sourcePath)
        {
            var channels = artifact.ChannelCount == 1
                ? AudioChannels.Mono
                : AudioChannels.Stereo;
            var effect = new SoundEffect(
                artifact.PcmData.ToArray(),
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