#nullable enable

using System;
using DTXMania.Game.Lib.Config;
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
        private readonly GaugeDamageLevel _damageLevel;
        private readonly int _initialRiskyLimit;
        private int _remainingRisky;
        private readonly bool _failureEnabled;

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
        /// Raised when the player fails according to the configured failure policy
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
        /// <param name="damageLevel">Miss damage level</param>
        /// <param name="riskyLimit">Number of Poor/Miss judgements allowed before failure</param>
        /// <param name="failureEnabled">Whether gameplay failure is enabled</param>
        public GaugeManager(
            float startingLife = StartingLife,
            GaugeDamageLevel damageLevel = GaugeDamageLevel.Normal,
            int riskyLimit = RiskyRange.Default,
            bool failureEnabled = true)
        {
            _currentLife = Math.Clamp(startingLife, MinLife, MaxLife);
            _damageLevel = damageLevel;
            _initialRiskyLimit = RiskyRange.Clamp(riskyLimit);
            _remainingRisky = _initialRiskyLimit;
            _failureEnabled = failureEnabled;
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

            var riskyActive = _initialRiskyLimit > RiskyRange.Default;
            if (riskyActive && (judgementEvent.Type == JudgementType.Poor || judgementEvent.Type == JudgementType.Miss))
            {
                _remainingRisky--;
            }

            // Check for failure
            var justFailed = false;
            var shouldFail = !_failureEnabled
                ? false
                : riskyActive
                    ? _remainingRisky <= 0
                    : _currentLife < FailureThreshold;

            if (shouldFail && !_hasFailed)
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
        }

        /// <summary>
        /// Gets the life adjustment for a specific judgement type
        /// </summary>
        /// <param name="judgementType">Judgement type</param>
        /// <returns>Life adjustment in percentage points</returns>
        public float GetLifeAdjustment(JudgementType judgementType)
        {
            return judgementType switch
            {
                JudgementType.Perfect => +2.0f,   // Perfect: +2%
                JudgementType.Great => +1.5f,  // Great: +1.5%
                JudgementType.Good => +1.0f,   // Good: +1%
                JudgementType.Poor => -1.5f,   // Poor: -1.5%
                JudgementType.Miss => -3.0f * GetMissDamageMultiplier(_damageLevel),
                _ => 0.0f // Default for unknown types
            };
        }

        private static float GetMissDamageMultiplier(GaugeDamageLevel damageLevel)
        {
            return damageLevel switch
            {
                GaugeDamageLevel.Low => 0.5f,
                GaugeDamageLevel.Normal => 1.0f,
                GaugeDamageLevel.High => 1.5f,
                _ => 1.0f
            };
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
            _remainingRisky = _initialRiskyLimit;

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
