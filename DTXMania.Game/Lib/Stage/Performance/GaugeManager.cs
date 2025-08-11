using System;
using DTXMania.Game.Lib.Song.Entities;

namespace DTXMania.Game.Lib.Stage.Performance
{
    /// <summary>
    /// Manages the life gauge during gameplay.
    /// Adjusts life based on judgement events and tracks failure state.
    /// Utilizes predefined life adjustment values for each judgement type.
    /// </summary>
    public class GaugeManager : IDisposable
    {
        #region Private Fields

        private float _currentLife;
        private bool _hasFailed;
        private bool _disposed = false;

        // Life adjustment values for each judgement type (as percentages)
        private readonly float[] _lifeAdjustments = new float[]
        {
            +2.0f,  // Just: +2%
            +1.5f,  // Great: +1.5%
            +1.0f,  // Good: +1%
            -1.5f,  // Poor: -1.5%
            -3.0f   // Miss: -3%
        };

        #endregion

        #region Constants

        /// <summary>
        /// Maximum life value (100%)
        /// </summary>
        public const float MaxLife = 100.0f;

        /// <summary>
        /// Minimum life value (0%)
        /// </summary>
        public const float MinLife = 0.0f;

        /// <summary>
        /// Failure threshold (2%)
        /// </summary>
        public const float FailureThreshold = 2.0f;

        /// <summary>
        /// Starting life value (50%)
        /// </summary>
        public const float StartingLife = 50.0f;

        #endregion

        #region Events

        /// <summary>
        /// Raised when the life gauge changes
        /// </summary>
        public event EventHandler<GaugeChangedEventArgs>? GaugeChanged;

        /// <summary>
        /// Raised when the player fails (life falls below threshold)
        /// </summary>
        public event EventHandler<FailureEventArgs>? Failed;

        #endregion

        #region Properties

        /// <summary>
        /// Current life value (0.0 to 100.0)
        /// </summary>
        public float CurrentLife => _currentLife;

        /// <summary>
        /// Whether the player has failed
        /// </summary>
        public bool HasFailed => _hasFailed;

        /// <summary>
        /// Life as a percentage (0.0 to 1.0)
        /// </summary>
        public float LifePercentage => _currentLife / MaxLife;

        /// <summary>
        /// Whether the gauge is in danger zone (below 20%)
        /// </summary>
        public bool IsInDanger => _currentLife < 20.0f && !_hasFailed;

        #endregion

        #region Constructor

        /// <summary>
        /// Creates a new GaugeManager
        /// </summary>
        /// <param name="startingLife">Starting life value (default: 50%)</param>
        public GaugeManager(float startingLife = StartingLife)
        {
            _currentLife = Math.Clamp(startingLife, MinLife, MaxLife);
            _hasFailed = false;
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Processes a judgement event and updates the life gauge
        /// </summary>
        /// <param name="judgementEvent">Judgement event to process</param>
        public void ProcessJudgement(JudgementEvent judgementEvent)
        {
            if (_disposed || judgementEvent == null || _hasFailed)
                return;

            var previousLife = _currentLife;
            var adjustment = GetLifeAdjustment(judgementEvent.Type);
            
            // Apply life adjustment
            _currentLife += adjustment;
            
            // Clamp to valid range
            _currentLife = Math.Clamp(_currentLife, MinLife, MaxLife);

            // Check for failure
            var justFailed = false;
            if (_currentLife < FailureThreshold && !_hasFailed)
            {
                _hasFailed = true;
                justFailed = true;
                
                // Raise failure event
                Failed?.Invoke(this, new FailureEventArgs
                {
                    FinalLife = _currentLife,
                    JudgementType = judgementEvent.Type
                });

                System.Diagnostics.Debug.WriteLine($"Player failed! Life: {_currentLife:F1}% (Trigger: {judgementEvent.Type})");
            }

            // Raise gauge changed event
            GaugeChanged?.Invoke(this, new GaugeChangedEventArgs
            {
                PreviousLife = previousLife,
                CurrentLife = _currentLife,
                LifeChange = adjustment,
                JudgementType = judgementEvent.Type,
                JustFailed = justFailed
            });

            System.Diagnostics.Debug.WriteLine($"Life gauge: {previousLife:F1}% → {_currentLife:F1}% ({adjustment:+0.0;-0.0}% from {judgementEvent.Type})");
        }

        /// <summary>
        /// Gets the life adjustment for a specific judgement type
        /// </summary>
        /// <param name="judgementType">Judgement type</param>
        /// <returns>Life adjustment in percentage points</returns>
        public float GetLifeAdjustment(JudgementType judgementType)
        {
            int index = (int)judgementType;
            if (index >= 0 && index < _lifeAdjustments.Length)
                return _lifeAdjustments[index];
            
            return 0.0f; // Default for unknown types
        }

        /// <summary>
        /// Resets the gauge to starting conditions
        /// </summary>
        /// <param name="startingLife">Starting life value (default: 50%)</param>
        public void Reset(float startingLife = StartingLife)
        {
            if (_disposed)
                return;

            var previousLife = _currentLife;
            var previousFailed = _hasFailed;
            
            _currentLife = Math.Clamp(startingLife, MinLife, MaxLife);
            _hasFailed = false;

            // Raise gauge changed event
            GaugeChanged?.Invoke(this, new GaugeChangedEventArgs
            {
                PreviousLife = previousLife,
                CurrentLife = _currentLife,
                LifeChange = _currentLife - previousLife,
                JudgementType = null,
                JustFailed = false
            });
        }

        /// <summary>
        /// Gets gauge statistics
        /// </summary>
        /// <returns>Gauge statistics</returns>
        public GaugeStatistics GetStatistics()
        {
            return new GaugeStatistics
            {
                CurrentLife = _currentLife,
                LifePercentage = LifePercentage,
                HasFailed = _hasFailed,
                IsInDanger = IsInDanger
            };
        }

        #endregion

        #region IDisposable Implementation

        /// <summary>
        /// Disposes the gauge manager
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
                _disposed = true;
            }
        }

        #endregion
    }

    #region Supporting Classes

    /// <summary>
    /// Event arguments for gauge change events
    /// </summary>
    public class GaugeChangedEventArgs : EventArgs
    {
        /// <summary>
        /// Previous life value
        /// </summary>
        public float PreviousLife { get; set; }

        /// <summary>
        /// Current life value
        /// </summary>
        public float CurrentLife { get; set; }

        /// <summary>
        /// Life change amount (can be negative)
        /// </summary>
        public float LifeChange { get; set; }

        /// <summary>
        /// Judgement type that caused this change (null for resets)
        /// </summary>
        public JudgementType? JudgementType { get; set; }

        /// <summary>
        /// Whether the player just failed on this event
        /// </summary>
        public bool JustFailed { get; set; }
    }

    /// <summary>
    /// Event arguments for failure events
    /// </summary>
    public class FailureEventArgs : EventArgs
    {
        /// <summary>
        /// Final life value when failure occurred
        /// </summary>
        public float FinalLife { get; set; }

        /// <summary>
        /// Judgement type that triggered the failure
        /// </summary>
        public JudgementType JudgementType { get; set; }
    }

    /// <summary>
    /// Statistics about gauge performance
    /// </summary>
    public class GaugeStatistics
    {
        /// <summary>
        /// Current life value
        /// </summary>
        public float CurrentLife { get; set; }

        /// <summary>
        /// Life as percentage (0.0 to 1.0)
        /// </summary>
        public float LifePercentage { get; set; }

        /// <summary>
        /// Whether the player has failed
        /// </summary>
        public bool HasFailed { get; set; }

        /// <summary>
        /// Whether the gauge is in danger zone
        /// </summary>
        public bool IsInDanger { get; set; }

        /// <summary>
        /// Returns a string representation of the statistics
        /// </summary>
        public override string ToString()
        {
            var status = HasFailed ? "FAILED" : (IsInDanger ? "DANGER" : "OK");
            return $"Life: {CurrentLife:F1}% ({status})";
        }
    }

    #endregion
}
