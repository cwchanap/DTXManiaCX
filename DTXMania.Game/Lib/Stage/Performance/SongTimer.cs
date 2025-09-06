using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;

namespace DTXMania.Game.Lib.Stage.Performance
{
    /// <summary>
    /// High-precision song timer for synchronizing gameplay with audio
    /// Wraps SoundEffectInstance with precise timing tracking
    /// </summary>
    public class SongTimer : IDisposable
    {
        #region Private Fields

        private readonly SoundEffectInstance _soundInstance;
        private TimeSpan _startTime;
        private DateTime _systemStartTime;
        private bool _isPlaying = false;
        private bool _disposed = false;
        private int _debugCallCount = 0;

        #endregion

        #region Properties

        /// <summary>
        /// Whether the song is currently playing
        /// Note: When volume is 0 (muted for BGM events), we only check our internal flag
        /// </summary>
        public bool IsPlaying 
        { 
            get 
            {
                var internalPlaying = _isPlaying;
                var soundState = _soundInstance?.State;
                var volume = _soundInstance?.Volume ?? 0f;
                var result = internalPlaying && (Math.Abs(volume) < 0.001f || soundState == SoundState.Playing);
                
                // Debug only when state changes unexpectedly or for first few calls
                if ((internalPlaying && !result) || (_debugCallCount++ < 3))
                {
                    System.Console.WriteLine($"🔍 CLAUDE-DEBUG: IsPlaying check - internal={internalPlaying}, state={soundState}, volume={volume}, result={result}");
                }
                
                return result;
            }
        }

        /// <summary>
        /// Whether the song has finished playing
        /// </summary>
        public bool IsFinished => _soundInstance?.State == SoundState.Stopped && _isPlaying;

        /// <summary>
        /// Volume of the audio (0.0 to 1.0)
        /// </summary>
        public float Volume
        {
            get => _soundInstance?.Volume ?? 0f;
            set
            {
            if (_soundInstance != null)
            {
                _soundInstance.Volume = MathHelper.Clamp(value, 0f, 1f);
            }
            }
        }

        /// <summary>
        /// Whether the audio is looped
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

        #endregion

        #region Constructors

        /// <summary>
        /// Creates a new SongTimer with the specified sound instance
        /// </summary>
        /// <param name="soundInstance">The sound instance to wrap</param>
        public SongTimer(SoundEffectInstance soundInstance)
        {
        _soundInstance = soundInstance ?? throw new ArgumentNullException(nameof(soundInstance));
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Starts playing the song and begins timing
        /// </summary>
        /// <param name="gameTime">Current game time for precise timing</param>
        public void Play(GameTime gameTime)
        {
        if (_disposed || _soundInstance == null)
        {
            System.Console.WriteLine("🔍 CLAUDE-DEBUG: SongTimer.Play failed - disposed or null instance");
            return;
        }

        _startTime = gameTime.TotalGameTime;
        _systemStartTime = DateTime.UtcNow;
        
        try
        {
            _soundInstance.Play();
            _isPlaying = true;
            System.Console.WriteLine($"🔍 CLAUDE-DEBUG: SoundInstance.Play() called - State={_soundInstance.State}, Volume={_soundInstance.Volume}");
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"🔍 CLAUDE-DEBUG: SoundInstance.Play() failed: {ex.Message}");
            _isPlaying = false;
        }
        }

        /// <summary>
        /// Pauses the song
        /// </summary>
        public void Pause()
        {
            System.Console.WriteLine("🔍 CLAUDE-DEBUG: SongTimer.Pause() called");
            
            if (_disposed || _soundInstance == null)
                return;

            _soundInstance.Pause();
            _isPlaying = false;
        }

        /// <summary>
        /// Resumes the song
        /// </summary>
        /// <param name="gameTime">Current game time for timing adjustment</param>
        public void Resume(GameTime gameTime)
        {
            if (_disposed || _soundInstance == null)
                return;

            // Adjust start time to account for pause duration
            // Adjust start time to account for pause duration
            var pauseDuration = gameTime.TotalGameTime - _startTime - TimeSpan.FromMilliseconds(GetCurrentMs());
            _startTime += pauseDuration;

            _soundInstance.Resume();
            _isPlaying = true;
            _systemStartTime = DateTime.UtcNow;
        }

        /// <summary>
        /// Stops the song
        /// </summary>
        public void Stop()
        {
            var stackTrace = new System.Diagnostics.StackTrace(true);
            var callingMethod = stackTrace.GetFrame(1)?.GetMethod();
            var callingClass = callingMethod?.DeclaringType?.Name;
            var callingMethodName = callingMethod?.Name;
            var fileName = stackTrace.GetFrame(1)?.GetFileName();
            var lineNumber = stackTrace.GetFrame(1)?.GetFileLineNumber();
            
            System.Console.WriteLine($"🔍 CLAUDE-DEBUG: SongTimer.Stop() called by {callingClass}::{callingMethodName} at {fileName}:{lineNumber}");
            
        if (_disposed || _soundInstance == null)
            return;

        _soundInstance.Stop();
        _isPlaying = false;
        }

        /// <summary>
        /// Gets the current playback time in milliseconds
        /// </summary>
        /// <param name="gameTime">Current game time</param>
        /// <returns>Current song time in milliseconds</returns>
        public double GetCurrentMs(GameTime gameTime)
        {
            if (_disposed || !_isPlaying)
                return 0.0;

            var elapsed = gameTime.TotalGameTime - _startTime;
            return elapsed.TotalMilliseconds;
        }

        /// <summary>
        /// Gets the current playback time in milliseconds (without GameTime parameter)
        /// Note: This is less precise than the GameTime version and should only be used
        /// when GameTime is not available
        /// </summary>
        /// <returns>Current song time in milliseconds</returns>
        public double GetCurrentMs()
        {
            if (_disposed || !_isPlaying)
                return 0.0;

            // This is less precise but can be used when GameTime is not available
            // In practice, the GameTime version should be preferred
            return (DateTime.UtcNow - _systemStartTime).TotalMilliseconds;
        }

        /// <summary>
        /// Sets the playback position (if supported by the audio format)
        /// </summary>
        /// <param name="positionMs">Position in milliseconds</param>
        /// <param name="gameTime">Current game time for timing adjustment</param>
        public void SetPosition(double positionMs, GameTime gameTime)
        {
        if (_disposed || _soundInstance == null)
            return;

        // Note: XNA/MonoGame SoundEffectInstance doesn't support seeking
        // This method is provided for future compatibility
        // For now, we adjust the start time to simulate the position
        _startTime = gameTime.TotalGameTime - TimeSpan.FromMilliseconds(positionMs);
        }

        /// <summary>
        /// Updates the timer (call this every frame)
        /// </summary>
        /// <param name="gameTime">Current game time</param>
        public void Update(GameTime gameTime)
        {
            if (_disposed)
                return;

            // Check if the sound has finished playing
            if (_isPlaying && _soundInstance?.State == SoundState.Stopped)
            {
                _isPlaying = false;
            }
        }

        #endregion

        #region IDisposable Implementation

        /// <summary>
        /// Disposes the timer and releases resources
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Protected dispose method
        /// </summary>
        /// <param name="disposing">Whether disposing from Dispose() call</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                Stop();
                _soundInstance?.Dispose();
                _disposed = true;
            }
        }

        #endregion
    }
}
