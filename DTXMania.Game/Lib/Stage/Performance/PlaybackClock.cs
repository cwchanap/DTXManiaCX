#nullable enable

using System;
using Microsoft.Xna.Framework;

namespace DTXMania.Game.Lib.Stage.Performance
{
    /// <summary>
    /// Converts monotonic real game time into rate-adjusted logical chart time.
    /// </summary>
    public sealed class PlaybackClock
    {
        private readonly double _speedFactor;
        private TimeSpan _realAnchorTime;
        private double _logicalAnchorMs;
        private double _lastLogicalTimeMs;
        private bool _hasStarted;

        public PlaybackClock(int playSpeedPercent)
        {
            if (playSpeedPercent <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(playSpeedPercent),
                    playSpeedPercent,
                    "Play speed must be greater than zero.");
            }

            PlaySpeedPercent = playSpeedPercent;
            _speedFactor = playSpeedPercent / 100.0;
        }

        public int PlaySpeedPercent { get; }

        public bool IsRunning { get; private set; }

        public bool IsPaused { get; private set; }

        public double GetLogicalTimeMs(GameTime gameTime)
        {
            if (!_hasStarted)
                return 0.0;

            if (!IsRunning)
                return _logicalAnchorMs;

            var realElapsedMs =
                (gameTime.TotalGameTime - _realAnchorTime).TotalMilliseconds;
            var logicalTimeMs =
                _logicalAnchorMs + Math.Max(0.0, realElapsedMs) * _speedFactor;

            _lastLogicalTimeMs = Math.Max(_lastLogicalTimeMs, logicalTimeMs);
            return _lastLogicalTimeMs;
        }

        public void Start(GameTime gameTime, double logicalPositionMs = 0)
        {
            var normalizedPosition = NormalizePosition(logicalPositionMs);

            _realAnchorTime = gameTime.TotalGameTime;
            _logicalAnchorMs = normalizedPosition;
            _lastLogicalTimeMs = normalizedPosition;
            _hasStarted = true;
            IsRunning = true;
            IsPaused = false;
        }

        public void Pause(GameTime gameTime)
        {
            if (!IsRunning)
                return;

            _logicalAnchorMs = GetLogicalTimeMs(gameTime);
            _lastLogicalTimeMs = _logicalAnchorMs;
            IsRunning = false;
            IsPaused = true;
        }

        public void Resume(GameTime gameTime)
        {
            if (!IsPaused)
                return;

            _realAnchorTime = gameTime.TotalGameTime;
            _logicalAnchorMs = _lastLogicalTimeMs;
            IsRunning = true;
            IsPaused = false;
        }

        public void Stop()
        {
            _realAnchorTime = TimeSpan.Zero;
            _logicalAnchorMs = 0.0;
            _lastLogicalTimeMs = 0.0;
            _hasStarted = false;
            IsRunning = false;
            IsPaused = false;
        }

        public void SetLogicalPosition(double logicalPositionMs, GameTime gameTime)
        {
            if (!_hasStarted)
                return;

            var normalizedPosition = NormalizePosition(logicalPositionMs);
            _realAnchorTime = gameTime.TotalGameTime;
            _logicalAnchorMs = normalizedPosition;
            _lastLogicalTimeMs = normalizedPosition;
        }

        private static double NormalizePosition(double logicalPositionMs)
        {
            if (double.IsNaN(logicalPositionMs) ||
                double.IsInfinity(logicalPositionMs))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(logicalPositionMs),
                    logicalPositionMs,
                    "Logical position must be a finite number.");
            }

            return Math.Max(0.0, logicalPositionMs);
        }
    }
}