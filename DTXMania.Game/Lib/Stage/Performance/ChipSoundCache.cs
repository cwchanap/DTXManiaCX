#nullable enable

using System;
using System.Collections.Generic;
using DTXMania.Game.Lib.Resources;
using Microsoft.Xna.Framework.Audio;

namespace DTXMania.Game.Lib.Stage.Performance
{
    /// <summary>
    /// Plays borrowed per-WAV-id drum chip sounds for a single chart.
    /// The prepared gameplay audio set owns the sounds; this cache owns only
    /// instances created from them.
    /// </summary>
    public class ChipSoundCache : IDisposable
    {
        private readonly Dictionary<string, ISound> _sounds = new Dictionary<string, ISound>();
        private readonly List<SoundEffectInstance> _activeInstances = new List<SoundEffectInstance>();
        private bool _disposed;

        /// <summary>
        /// Maximum number of concurrent active instances before forcing cleanup.
        /// Prevents unbounded growth during gameplay.
        /// </summary>
        internal const int MaxActiveInstances = 64;

        /// <summary>
        /// Creates a cache over sounds owned by PreparedGameplayAudioSet.
        /// </summary>
        public ChipSoundCache(IReadOnlyDictionary<string, ISound>? borrowedSoundsByWavId = null)
        {
            if (borrowedSoundsByWavId == null)
                return;

            foreach (var pair in borrowedSoundsByWavId)
            {
                if (!string.IsNullOrEmpty(pair.Key) && pair.Value != null)
                    _sounds[pair.Key] = pair.Value;
            }
        }

        public int Count => _sounds.Count;

        /// <summary>
        /// Number of currently active (playing or paused) sound instances.
        /// </summary>
        public int ActiveInstanceCount => _activeInstances.Count;

        public bool Contains(string wavId) =>
            !string.IsNullOrEmpty(wavId) && _sounds.ContainsKey(wavId);

        /// <summary>
        /// Plays the sound for the given WAV id at full volume, centered.
        /// </summary>
        public void Play(string wavId) => Play(wavId, 1.0f, 0.0f, 0.0f);

        public void Play(string wavId, float volume, float pan) =>
            Play(wavId, volume, 0.0f, pan);

        /// <summary>
        /// Plays the sound for the given WAV id with the supplied volume and pan,
        /// honoring the chart's #VOLUME/#PAN definitions. Unknown ids are silent —
        /// not exceptional, since charts often reference WAVs that aren't loaded.
        /// Tracks the returned SoundEffectInstance for lifecycle management.
        /// </summary>
        /// <param name="wavId">WAV id to play.</param>
        /// <param name="volume">Normalized volume, 0.0–1.0.</param>
        /// <param name="pan">Normalized stereo pan, -1.0 (left) to 1.0 (right).</param>
        public void Play(string wavId, float volume, float pitch, float pan)
        {
            if (_disposed || string.IsNullOrEmpty(wavId)) return;
            if (!_sounds.TryGetValue(wavId, out var sound)) return;

            try
            {
                // The fast path is valid only for the exact default profile.
                SoundEffectInstance? instance =
                    (volume >= 0.999f &&
                     Math.Abs(pitch) < 0.001f &&
                     Math.Abs(pan) < 0.001f)
                        ? sound.Play()
                        : sound.Play(volume, pitch, pan);

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

            // Borrowed sounds remain owned by PreparedGameplayAudioSet.
            _sounds.Clear();
        }
    }
}
