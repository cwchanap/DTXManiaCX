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
        private DateTime _systemStartTime;
        private double _systemLogicalPositionMs;
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
                if (_soundInstance != null)
                    _soundInstance.Volume = MathHelper.Clamp(value, 0f, 1f);
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
                if (_soundInstance != null)
                    _soundInstance.Pan = MathHelper.Clamp(value, -1f, 1f);
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
                if (_soundInstance != null)
                    _soundInstance.Pitch = MathHelper.Clamp(value, -1f, 1f);
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
                if (_soundInstance != null)
                    _soundInstance.IsLooped = value;
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
        /// Starts audio transport and the logical gameplay clock.
        /// </summary>
        public bool Play(GameTime gameTime)
        {
            if (_disposed)
                return false;

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
                    return false;
                }
            }

            _playbackClock.Start(gameTime);
            _systemLogicalPositionMs = 0.0;
            _systemStartTime = DateTime.UtcNow;
            return true;
        }

        /// <summary>
        /// Pauses audio transport and freezes logical time at the supplied GameTime.
        /// </summary>
        public void Pause(GameTime gameTime)
        {
            if (_disposed || !_playbackClock.IsRunning)
                return;

            _playbackClock.Pause(gameTime);
            _systemLogicalPositionMs = _playbackClock.GetLogicalTimeMs(gameTime);
            _soundInstance?.Pause();
        }

        /// <summary>
        /// Resumes audio transport and logical time from the paused position.
        /// </summary>
        public void Resume(GameTime gameTime)
        {
            if (_disposed || !_playbackClock.IsPaused)
                return;

            _soundInstance?.Resume();
            _playbackClock.Resume(gameTime);
            _systemStartTime = DateTime.UtcNow;
        }

        /// <summary>
        /// Stops audio transport and resets logical time.
        /// </summary>
        public void Stop()
        {
            if (_disposed)
                return;

            _soundInstance?.Stop();
            _playbackClock.Stop();
            _systemLogicalPositionMs = 0.0;
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
        /// Compatibility wall-clock estimate for callers without GameTime.
        /// Gameplay code must use GetCurrentMs(GameTime).
        /// </summary>
        public double GetCurrentMs()
        {
            if (_disposed)
                return 0.0;

            if (_playbackClock.IsPaused)
                return _systemLogicalPositionMs;

            if (!_playbackClock.IsRunning)
                return 0.0;

            var systemElapsedMs =
                (DateTime.UtcNow - _systemStartTime).TotalMilliseconds;
            return _systemLogicalPositionMs +
                systemElapsedMs * _playbackClock.PlaySpeedPercent / 100.0;
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
            _systemLogicalPositionMs =
                _playbackClock.GetLogicalTimeMs(gameTime);
            _systemStartTime = DateTime.UtcNow;
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

            Stop();
            _soundInstance?.Dispose();
            _disposed = true;
        }
    }
}
