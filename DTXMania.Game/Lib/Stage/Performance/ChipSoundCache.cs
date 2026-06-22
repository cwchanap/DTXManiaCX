#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Resources;
using Microsoft.Xna.Framework.Audio;

namespace DTXMania.Game.Lib.Stage.Performance
{
    /// <summary>
    /// Loads and plays per-WAV-id drum chip sounds for a single chart.
    /// Owned by PerformanceStage and disposed on stage cleanup.
    /// Mirrors the existing _bgmSounds pattern but for on-demand chip playback.
    /// Tracks active SoundEffectInstances to prevent native resource leaks.
    /// </summary>
    public class ChipSoundCache : IDisposable
    {
        private readonly Func<string, ISound> _soundFactory;
        private readonly Dictionary<string, ISound> _sounds = new Dictionary<string, ISound>();
        private readonly List<SoundEffectInstance> _activeInstances = new List<SoundEffectInstance>();
        private bool _disposed;

        /// <summary>
        /// Maximum number of concurrent active instances before forcing cleanup.
        /// Prevents unbounded growth during gameplay.
        /// </summary>
        internal const int MaxActiveInstances = 64;

        /// <summary>
        /// Creates a ChipSoundCache. The factory is overridable for unit tests;
        /// production callers omit it and get the default ManagedSound loader.
        /// </summary>
        public ChipSoundCache(Func<string, ISound>? soundFactory = null)
        {
            _soundFactory = soundFactory ?? (path => new ManagedSound(path));
        }

        public int Count => _sounds.Count;

        /// <summary>
        /// Number of currently active (playing or paused) sound instances.
        /// </summary>
        public int ActiveInstanceCount => _activeInstances.Count;

        public bool Contains(string wavId) =>
            !string.IsNullOrEmpty(wavId) && _sounds.ContainsKey(wavId);

        /// <summary>
        /// Loads each WAV definition into memory. Per-file failures are logged
        /// and skipped — one bad WAV does not abort preload.
        /// </summary>
        public Task PreloadAsync(IReadOnlyDictionary<string, string> wavDefs)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ChipSoundCache));
            if (wavDefs == null) return Task.CompletedTask;

            foreach (var kvp in wavDefs)
            {
                var wavId = kvp.Key;
                var path = kvp.Value;

                if (string.IsNullOrEmpty(path) || _sounds.ContainsKey(wavId))
                    continue;

                if (!File.Exists(path))
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[ChipSoundCache] Skipping WAV id '{wavId}': file not found at {path}");
                    continue;
                }

                try
                {
                    var sound = _soundFactory(path);
                    _sounds[wavId] = sound;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[ChipSoundCache] Failed to load WAV id '{wavId}' from {path}: {ex.Message}");
                }
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Plays the sound for the given WAV id at full volume, centered.
        /// </summary>
        public void Play(string wavId) => Play(wavId, 1.0f, 0.0f);

        /// <summary>
        /// Plays the sound for the given WAV id with the supplied volume and pan,
        /// honoring the chart's #VOLUME/#PAN definitions. Unknown ids are silent —
        /// not exceptional, since charts often reference WAVs that aren't loaded.
        /// Tracks the returned SoundEffectInstance for lifecycle management.
        /// </summary>
        /// <param name="wavId">WAV id to play.</param>
        /// <param name="volume">Normalized volume, 0.0–1.0.</param>
        /// <param name="pan">Normalized stereo pan, -1.0 (left) to 1.0 (right).</param>
        public void Play(string wavId, float volume, float pan)
        {
            if (_disposed || string.IsNullOrEmpty(wavId)) return;
            if (!_sounds.TryGetValue(wavId, out var sound)) return;

            try
            {
                // Keep the simple full-volume, centered path on Play() so the common
                // case (charts with no #VOLUME/#PAN) behaves exactly as before.
                SoundEffectInstance? instance =
                    (volume >= 0.999f && Math.Abs(pan) < 0.001f)
                        ? sound.Play()
                        : sound.Play(volume, 0.0f, pan);

                if (instance != null)
                {
                    _activeInstances.Add(instance);
                }

                // Periodically clean up stopped instances to prevent unbounded growth
                if (_activeInstances.Count > MaxActiveInstances)
                {
                    CleanupStoppedInstances();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[ChipSoundCache] Play failed for '{wavId}': {ex.Message}");
            }
        }

        /// <summary>
        /// Removes and disposes all instances that have finished playing.
        /// Called automatically when active instance count exceeds the threshold.
        /// </summary>
        public void CleanupStoppedInstances()
        {
            for (int i = _activeInstances.Count - 1; i >= 0; i--)
            {
                var instance = _activeInstances[i];
                if (instance.State == SoundState.Stopped)
                {
                    try { instance.Dispose(); }
                    catch { /* Best effort */ }
                    _activeInstances.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// Stops all active sound instances immediately. Used during stage transitions
        /// to silence chip sounds before disposal.
        /// </summary>
        public void StopAll()
        {
            foreach (var instance in _activeInstances)
            {
                try { instance.Stop(); }
                catch { /* Best effort */ }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // Stop and dispose all active instances
            foreach (var instance in _activeInstances)
            {
                try { instance.Stop(); instance.Dispose(); }
                catch { /* Best effort during cleanup */ }
            }
            _activeInstances.Clear();

            // Dispose cached sounds
            foreach (var kvp in _sounds)
            {
                try { kvp.Value?.Dispose(); }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[ChipSoundCache] Failed to dispose WAV id '{kvp.Key}': {ex.Message}");
                }
            }
            _sounds.Clear();
        }
    }
}
