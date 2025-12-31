#nullable enable

using System;
using DTXMania.Game.Lib.Song.Entities;

namespace DTXMania.Game.Lib.Stage.Performance
{
    /// <summary>
    /// Manages score calculation during gameplay based on judgement events.
    /// Implements DTXMania scoring system with 1,000,000 point maximum.
    /// Uses variable score multipliers based on judgement accuracy.
    /// </summary>
    public class ScoreManager : IDisposable
    {
        #region Private Fields

        private readonly int _totalNotes;
        private readonly int _baseScore;
        private int _currentScore;
        private bool _disposed = false;


        #endregion

        #region Constants

        /// <summary>
        /// Maximum possible score
        /// </summary>
        public const int MaxScore = 1000000;

        #endregion

        #region Events

        /// <summary>
        /// Raised when the score changes
        /// </summary>
        public event EventHandler<ScoreChangedEventArgs>? ScoreChanged;

        #endregion

        #region Properties

        /// <summary>
        /// Current score
        /// </summary>
        public int CurrentScore => _currentScore;

        /// <summary>
        /// Base score per note (MaxScore / TotalNotes)
        /// </summary>
        public int BaseScore => _baseScore;

        /// <summary>
        /// Total number of notes in the chart
        /// </summary>
        public int TotalNotes => _totalNotes;

        /// <summary>
        /// Maximum possible score for this chart
        /// </summary>
        public int TheoreticalMaxScore => _baseScore * _totalNotes;

        #endregion

        #region Constructor

        /// <summary>
        /// Creates a new ScoreManager
        /// </summary>
        /// <param name="totalNotes">Total number of notes in the chart</param>
        public ScoreManager(int totalNotes)
        {
            if (totalNotes <= 0)
                throw new ArgumentException("Total notes must be greater than 0", nameof(totalNotes));

            _totalNotes = totalNotes;
            _baseScore = MaxScore / totalNotes; // Integer division as specified
            _currentScore = 0;
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Processes a judgement event and updates the score
        /// </summary>
        /// <param name="judgementEvent">Judgement event to process</param>
        public void ProcessJudgement(JudgementEvent judgementEvent)
        {
            if (_disposed || judgementEvent == null)
                return;

            var multiplier = GetScoreMultiplier(judgementEvent.Type);
            var scoreToAdd = (int)Math.Floor(_baseScore * multiplier);

            var previousScore = _currentScore;
            _currentScore += scoreToAdd;

            // Ensure score never exceeds maximum
            _currentScore = Math.Min(_currentScore, MaxScore);

            // Raise score changed event
            var eventArgs = new ScoreChangedEventArgs
            {
                PreviousScore = previousScore,
                CurrentScore = _currentScore,
                ScoreAdded = scoreToAdd,
                JudgementType = judgementEvent.Type
            };

            ScoreChanged?.Invoke(this, eventArgs);
        }

        /// <summary>
        /// Gets the score multiplier for a specific judgement type
        /// </summary>
        /// <param name="judgementType">Judgement type</param>
        /// <returns>Score multiplier (0.0 to 1.0)</returns>
        public double GetScoreMultiplier(JudgementType judgementType)
        {
            return GetScoreMultiplierStatic(judgementType);
        }

        /// <summary>
        /// Gets the score multiplier for a specific judgement type (static version)
        /// </summary>
        /// <param name="judgementType">Judgement type</param>
        /// <returns>Score multiplier (0.0 to 1.0)</returns>
        public static double GetScoreMultiplierStatic(JudgementType judgementType)
        {
            return judgementType switch
            {
                JudgementType.Just => 1.0,
                JudgementType.Great => 0.9,
                JudgementType.Good => 0.5,
                JudgementType.Poor => 0.0,
                JudgementType.Miss => 0.0,
                _ => 0.0 // Default for unknown types
            };
        }

        /// <summary>
        /// Calculates the theoretical score for a specific judgement type
        /// </summary>
        /// <param name="judgementType">Judgement type</param>
        /// <returns>Score points that would be awarded</returns>
        public int CalculateScoreForJudgement(JudgementType judgementType)
        {
            var multiplier = GetScoreMultiplier(judgementType);
            return (int)Math.Floor(_baseScore * multiplier);
        }

        /// <summary>
        /// Gets scoring statistics
        /// </summary>
        /// <returns>Scoring statistics</returns>
        public ScoreStatistics GetStatistics()
        {
            return new ScoreStatistics
            {
                CurrentScore = _currentScore,
                BaseScore = _baseScore,
                TotalNotes = _totalNotes,
                TheoreticalMaxScore = TheoreticalMaxScore,
                ScorePercentage = TheoreticalMaxScore > 0 ? (double)_currentScore / TheoreticalMaxScore * 100.0 : 0.0
            };
        }

        /// <summary>
        /// Resets the score to zero
        /// </summary>
        public void Reset()
        {
            if (_disposed)
                return;

            var previousScore = _currentScore;
            _currentScore = 0;

            ScoreChanged?.Invoke(this, new ScoreChangedEventArgs
            {
                PreviousScore = previousScore,
                CurrentScore = 0,
                ScoreAdded = -previousScore,
                JudgementType = null
            });
        }

        #endregion

        #region IDisposable Implementation

        /// <summary>
        /// Disposes the score manager
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
    /// Event arguments for score change events
    /// </summary>
    public class ScoreChangedEventArgs : EventArgs
    {
        /// <summary>
        /// Previous score value
        /// </summary>
        public int PreviousScore { get; set; }

        /// <summary>
        /// Current score value
        /// </summary>
        public int CurrentScore { get; set; }

        /// <summary>
        /// Amount of score added (can be negative for resets)
        /// </summary>
        public int ScoreAdded { get; set; }

        /// <summary>
        /// Judgement type that caused this score change (null for resets)
        /// </summary>
        public JudgementType? JudgementType { get; set; }
    }

    /// <summary>
    /// Statistics about scoring
    /// </summary>
    public class ScoreStatistics
    {
        /// <summary>
        /// Current score
        /// </summary>
        public int CurrentScore { get; set; }

        /// <summary>
        /// Base score per note
        /// </summary>
        public int BaseScore { get; set; }

        /// <summary>
        /// Total number of notes
        /// </summary>
        public int TotalNotes { get; set; }

        /// <summary>
        /// Theoretical maximum score for this chart
        /// </summary>
        public int TheoreticalMaxScore { get; set; }

        /// <summary>
        /// Score as percentage of theoretical maximum
        /// </summary>
        public double ScorePercentage { get; set; }

        /// <summary>
        /// Returns a string representation of the statistics
        /// </summary>
        public override string ToString()
        {
            return $"Score: {CurrentScore:N0}/{TheoreticalMaxScore:N0} ({ScorePercentage:F1}%)";
        }
    }

    #endregion
}
