#nullable enable

using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;

namespace DTXMania.Game.Lib.Stage.Performance
{
    /// <summary>
    /// Controls background-audio transport while exposing a rate-aware logical
    /// gameplay clock that is independent of the audio instance state.
    /// </summary>
    public class SongTimer : IDisposable
    {
        private readonly SoundEffectInstance? _soundInstance;
        private readonly PlaybackClock _playbackClock;
        private readonly Action<string>? _logger;
        private bool _disposed;

        /// <summary>
        /// Whether the logical gameplay clock is running.
        /// </summary>
        public bool IsPlaying => !_disposed && _playbackClock.IsRunning;

        /// <summary>
        /// Whether the logical gameplay clock has a resumable paused position.
        /// </summary>
        public bool IsPaused => !_disposed && _playbackClock.IsPaused;

        /// <summary>
        /// Volume of the audio (0.0 to 1.0).
        /// </summary>
        public float Volume
        {
            get => _soundInstance?.Volume ?? 0f;
            set
            {
                if (_disposed || _soundInstance == null)
                    return;

                try
                {
                    _soundInstance.Volume = MathHelper.Clamp(value, 0f, 1f);
                }
                catch (Exception ex)
                {
                    _logger?.Invoke(
                        $"SongTimer.Volume() failed: {ex.GetType().Name} - {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Stereo pan of the audio (-1.0 = full left … +1.0 = full right).
        /// SoundEffectInstance.Pan is well-defined for mono sources but may
        /// have limited effect on stereo background tracks.
        /// </summary>
        public float Pan
        {
            get => _soundInstance?.Pan ?? 0f;
            set
            {
                if (_disposed || _soundInstance == null)
                    return;

                try
                {
                    _soundInstance.Pan = MathHelper.Clamp(value, -1f, 1f);
                }
                catch (Exception ex)
                {
                    _logger?.Invoke(
                        $"SongTimer.Pan() failed: {ex.GetType().Name} - {ex.Message}");
                }
            }
        }

        /// <summary>
        /// MonoGame pitch adjustment (-1.0 = one octave down, +1.0 = one octave up).
        /// </summary>
        public float Pitch
        {
            get => _soundInstance?.Pitch ?? 0f;
            set
            {
                if (_disposed || _soundInstance == null)
                    return;

                try
                {
                    _soundInstance.Pitch = MathHelper.Clamp(value, -1f, 1f);
                }
                catch (Exception ex)
                {
                    _logger?.Invoke(
                        $"SongTimer.Pitch() failed: {ex.GetType().Name} - {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Whether the audio is looped.
        /// </summary>
        public bool IsLooped
        {
            get => _soundInstance?.IsLooped ?? false;
            set
            {
                if (_disposed || _soundInstance == null)
                    return;

                try
                {
                    _soundInstance.IsLooped = value;
                }
                catch (Exception ex)
                {
                    _logger?.Invoke(
                        $"SongTimer.IsLooped() failed: {ex.GetType().Name} - {ex.Message}");
                }
            }
        }

        public SongTimer(
            SoundEffectInstance soundInstance,
            Action<string>? logger = null)
            : this(soundInstance, 100, logger)
        {
        }

        public SongTimer(
            SoundEffectInstance soundInstance,
            int playSpeedPercent,
            Action<string>? logger = null)
        {
            _soundInstance =
                soundInstance ?? throw new ArgumentNullException(nameof(soundInstance));
            _playbackClock = new PlaybackClock(playSpeedPercent);
            _logger = logger;
        }

        public SongTimer(Action<string>? logger = null)
            : this(100, logger)
        {
        }

        public SongTimer(
            int playSpeedPercent,
            Action<string>? logger = null)
        {
            _playbackClock = new PlaybackClock(playSpeedPercent);
            _logger = logger;
        }

        /// <summary>
        /// Starts audio transport and the logical gameplay clock. When the
        /// audio transport fails to start, the logical clock still runs on its
        /// GameTime fallback so the song can advance and complete without
        /// audible background audio; the transport failure is reported via
        /// the return value (false) and the logger.
        /// </summary>
        public bool Play(GameTime gameTime)
        {
            if (_disposed)
                return false;

            bool transportStarted = true;
            if (_soundInstance != null)
            {
                try
                {
                    _soundInstance.Play();
                }
                catch (Exception ex)
                {
                    _logger?.Invoke(
                        $"SongTimer.Play() failed: {ex.GetType().Name} - {ex.Message}");
                    transportStarted = false;
                }
            }

            _playbackClock.Start(gameTime);
            return transportStarted;
        }

        /// <summary>
        /// Pauses audio transport and freezes logical time at the supplied GameTime.
        /// </summary>
        public void Pause(GameTime gameTime)
        {
            if (_disposed || !_playbackClock.IsRunning)
                return;

            _playbackClock.Pause(gameTime);
            try { _soundInstance?.Pause(); }
            catch (Exception ex)
            {
                _logger?.Invoke(
                    $"SongTimer.Pause() failed: {ex.GetType().Name} - {ex.Message}");
            }
        }

        /// <summary>
        /// Resumes audio transport and logical time from the paused position.
        /// </summary>
        public void Resume(GameTime gameTime)
        {
            if (_disposed || !_playbackClock.IsPaused)
                return;

            try { _soundInstance?.Resume(); }
            catch (Exception ex)
            {
                _logger?.Invoke(
                    $"SongTimer.Resume() failed: {ex.GetType().Name} - {ex.Message}");
            }
            _playbackClock.Resume(gameTime);
        }

        /// <summary>
        /// Stops audio transport and resets logical time.
        /// </summary>
        public void Stop()
        {
            if (_disposed)
                return;

            try { _soundInstance?.Stop(); }
            catch (Exception ex)
            {
                _logger?.Invoke(
                    $"SongTimer.Stop() failed: {ex.GetType().Name} - {ex.Message}");
            }
            _playbackClock.Stop();
        }

        /// <summary>
        /// Gets the rate-adjusted logical chart time in milliseconds.
        /// </summary>
        public double GetCurrentMs(GameTime gameTime)
        {
            if (_disposed)
                return 0.0;

            return _playbackClock.GetLogicalTimeMs(gameTime);
        }

        /// <summary>
        /// Sets the logical chart position. Audio seeking is not supported by
        /// SoundEffectInstance, so this changes chart time only.
        /// </summary>
        public void SetPosition(double positionMs, GameTime gameTime)
        {
            if (_disposed)
                return;

            _playbackClock.SetLogicalPosition(positionMs, gameTime);
        }

        /// <summary>
        /// Retained for the stage update lifecycle. Audio completion deliberately
        /// does not change logical clock state.
        /// </summary>
        public void Update(GameTime gameTime)
        {
            if (_disposed)
                return;

            _playbackClock.GetLogicalTimeMs(gameTime);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed || !disposing)
                return;

            try
            {
                Stop();
                _soundInstance?.Dispose();
            }
            finally
            {
                _disposed = true;
            }
        }
    }
}
