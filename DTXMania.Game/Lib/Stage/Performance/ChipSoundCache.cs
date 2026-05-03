#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Resources;

namespace DTXMania.Game.Lib.Stage.Performance
{
    /// <summary>
    /// Loads and plays per-WAV-id drum chip sounds for a single chart.
    /// Owned by PerformanceStage and disposed on stage cleanup.
    /// Mirrors the existing _bgmSounds pattern but for on-demand chip playback.
    /// </summary>
    public class ChipSoundCache : IDisposable
    {
        private readonly Func<string, ISound> _soundFactory;
        private readonly Dictionary<string, ISound> _sounds = new Dictionary<string, ISound>();
        private bool _disposed;

        /// <summary>
        /// Creates a ChipSoundCache. The factory is overridable for unit tests;
        /// production callers omit it and get the default ManagedSound loader.
        /// </summary>
        public ChipSoundCache(Func<string, ISound>? soundFactory = null)
        {
            _soundFactory = soundFactory ?? (path => new ManagedSound(path));
        }

        public int Count => _sounds.Count;

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
        /// Plays the sound for the given WAV id. Unknown ids are silent — not
        /// exceptional, since charts often reference WAVs that aren't loaded.
        /// </summary>
        public void Play(string wavId)
        {
            if (_disposed || string.IsNullOrEmpty(wavId)) return;
            if (!_sounds.TryGetValue(wavId, out var sound)) return;

            try
            {
                sound.Play();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[ChipSoundCache] Play failed for '{wavId}': {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

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
