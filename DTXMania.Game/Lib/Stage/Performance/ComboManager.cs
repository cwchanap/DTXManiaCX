using System;
using DTXMania.Game.Lib.Song.Entities;

namespace DTXMania.Game.Lib.Stage.Performance
{
    /// <summary>
    /// Manages combo tracking during gameplay.
    /// Increments combo on successful hits (Just/Great/Good), resets on Poor/Miss judgements.
    /// Tracks both current combo count and maximum combo achieved.
    /// </summary>
    public class ComboManager : IDisposable
    {
        #region Private Fields

        private int _currentCombo;
        private int _maxCombo;
        private bool _disposed = false;

        #endregion

        #region Events

        /// <summary>
        /// Raised when the combo changes
        /// </summary>
        public event EventHandler<ComboChangedEventArgs>? ComboChanged;

        /// <summary>
        /// Raised when a new max combo is achieved
        /// </summary>
        public event EventHandler<MaxComboChangedEventArgs>? MaxComboChanged;

        #endregion

        #region Properties

        /// <summary>
        /// Current combo count
        /// </summary>
        public int CurrentCombo => _currentCombo;

        /// <summary>
        /// Maximum combo achieved
        /// </summary>
        public int MaxCombo => _maxCombo;

        /// <summary>
        /// Whether the player currently has a combo going
        /// </summary>
        public bool HasCombo => _currentCombo > 0;

        #endregion

        #region Constructor

        /// <summary>
        /// Creates a new ComboManager
        /// </summary>
        public ComboManager()
        {
            _currentCombo = 0;
            _maxCombo = 0;
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Processes a judgement event and updates combo state
        /// </summary>
        /// <param name="judgementEvent">Judgement event to process</param>
        public void ProcessJudgement(JudgementEvent judgementEvent)
        {
            if (_disposed || judgementEvent == null)
                return;

            var previousCombo = _currentCombo;
            var previousMaxCombo = _maxCombo;

            switch (judgementEvent.Type)
            {
                case JudgementType.Just:
                case JudgementType.Great:
                case JudgementType.Good:
                    // Increment combo on successful hits
                    _currentCombo++;
                    
                    // Update max combo if needed
                    if (_currentCombo > _maxCombo)
                    {
                        _maxCombo = _currentCombo;
                        
                        // Raise max combo changed event
                        MaxComboChanged?.Invoke(this, new MaxComboChangedEventArgs
                        {
                            PreviousMaxCombo = previousMaxCombo,
                            NewMaxCombo = _maxCombo
                        });
                    }
                    break;

                case JudgementType.Poor:
                case JudgementType.Miss:
                    // Reset combo on Poor/Miss
                    _currentCombo = 0;
                    break;
            }

            // Raise combo changed event if combo actually changed
            if (_currentCombo != previousCombo)
            {
                ComboChanged?.Invoke(this, new ComboChangedEventArgs
                {
                    PreviousCombo = previousCombo,
                    CurrentCombo = _currentCombo,
                    JudgementType = judgementEvent.Type,
                    WasReset = _currentCombo == 0 && previousCombo > 0
                });

                System.Diagnostics.Debug.WriteLine($"Combo updated: {previousCombo} → {_currentCombo} ({judgementEvent.Type})");
            }
        }

        /// <summary>
        /// Resets both current and max combo to zero
        /// </summary>
        public void Reset()
        {
            if (_disposed)
                return;

            var previousCombo = _currentCombo;
            var previousMaxCombo = _maxCombo;
            
            _currentCombo = 0;
            _maxCombo = 0;

            // Raise events if values changed
            if (previousCombo > 0)
            {
                ComboChanged?.Invoke(this, new ComboChangedEventArgs
                {
                    PreviousCombo = previousCombo,
                    CurrentCombo = 0,
                    JudgementType = null,
                    WasReset = true
                });
            }

            if (previousMaxCombo > 0)
            {
                MaxComboChanged?.Invoke(this, new MaxComboChangedEventArgs
                {
                    PreviousMaxCombo = previousMaxCombo,
                    NewMaxCombo = 0
                });
            }
        }

        /// <summary>
        /// Gets combo statistics
        /// </summary>
        /// <returns>Combo statistics</returns>
        public ComboStatistics GetStatistics()
        {
            return new ComboStatistics
            {
                CurrentCombo = _currentCombo,
                MaxCombo = _maxCombo,
                HasCombo = HasCombo
            };
        }

        #endregion

        #region IDisposable Implementation

        /// <summary>
        /// Disposes the combo manager
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
                // Clear all event subscribers to prevent memory leaks
                ComboChanged = null;
                MaxComboChanged = null;
                
                _disposed = true;
            }
        }

        #endregion
    }

    #region Supporting Classes

    /// <summary>
    /// Event arguments for combo change events
    /// </summary>
    public class ComboChangedEventArgs : EventArgs
    {
        /// <summary>
        /// Previous combo value
        /// </summary>
        public int PreviousCombo { get; set; }

        /// <summary>
        /// Current combo value
        /// </summary>
        public int CurrentCombo { get; set; }

        /// <summary>
        /// Judgement type that caused this combo change
        /// </summary>
        public JudgementType? JudgementType { get; set; }

        /// <summary>
        /// Whether this was a combo reset (dropped to 0)
        /// </summary>
        public bool WasReset { get; set; }
    }

    /// <summary>
    /// Event arguments for max combo change events
    /// </summary>
    public class MaxComboChangedEventArgs : EventArgs
    {
        /// <summary>
        /// Previous max combo value
        /// </summary>
        public int PreviousMaxCombo { get; set; }

        /// <summary>
        /// New max combo value
        /// </summary>
        public int NewMaxCombo { get; set; }
    }

    /// <summary>
    /// Statistics about combo performance
    /// </summary>
    public class ComboStatistics
    {
        /// <summary>
        /// Current combo count
        /// </summary>
        public int CurrentCombo { get; set; }

        /// <summary>
        /// Maximum combo achieved
        /// </summary>
        public int MaxCombo { get; set; }

        /// <summary>
        /// Whether the player currently has a combo
        /// </summary>
        public bool HasCombo { get; set; }

        /// <summary>
        /// Returns a string representation of the statistics
        /// </summary>
        public override string ToString()
        {
            return $"Combo: {CurrentCombo} (Max: {MaxCombo})";
        }
    }

    #endregion
}
