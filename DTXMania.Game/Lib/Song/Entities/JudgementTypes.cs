using System;

namespace DTXMania.Game.Lib.Song.Entities
{
    /// <summary>
    /// Enumeration that represents the judgement types in DTXMania gameplay.
    /// Based on DTXManiaNX judgement system patterns.
    /// </summary>
    public enum JudgementType
    {
        /// <summary>
        /// Perfect timing hit (highest accuracy)
        /// </summary>
        Perfect,
        
        /// <summary>
        /// Excellent timing hit (very good accuracy)
        /// </summary>
        Great,
        
        /// <summary>
        /// Good timing hit (acceptable accuracy)
        /// </summary>
        Good,
        
        /// <summary>
        /// Poor timing hit (low accuracy, still counts as hit)
        /// </summary>
        Poor,
        
        /// <summary>
        /// Missed note (no hit registered)
        /// </summary>
        Miss
    }

    /// <summary>
    /// Event payload for a judgement made during gameplay.
    /// Contains all necessary information for scoring and feedback systems.
    /// </summary>
    public class JudgementEvent
    {
        #region Properties
        
        /// <summary>
        /// Reference to the note that was judged (for tracking purposes)
        /// </summary>
        public int NoteRef { get; set; }
        
        /// <summary>
        /// The lane where the judgement occurred (0-9 for the 10 lanes)
        /// </summary>
        public int Lane { get; set; }
        
        /// <summary>
        /// Timing delta in milliseconds (positive = late, negative = early)
        /// </summary>
        public double DeltaMs { get; set; }
        
        /// <summary>
        /// Type of the judgement (Perfect/Great/Good/Poor/Miss)
        /// </summary>
        public JudgementType Type { get; set; }
        
        /// <summary>
        /// Event timestamp when the judgement occurred
        /// </summary>
        public DateTime Timestamp { get; set; }
        
        #endregion
        
        #region Constructors
        
        /// <summary>
        /// Default constructor for JudgementEvent
        /// </summary>
        public JudgementEvent()
        {
            Type = JudgementType.Miss;
            Timestamp = DateTime.UtcNow;
        }
        
        /// <summary>
        /// Creates a new JudgementEvent with specified parameters
        /// </summary>
        /// <param name="noteRef">Reference to the note</param>
        /// <param name="lane">Lane where judgement occurred</param>
        /// <param name="deltaMs">Timing delta in milliseconds</param>
        /// <param name="type">Type of judgement</param>
        public JudgementEvent(int noteRef, int lane, double deltaMs, JudgementType type)
        {
            NoteRef = noteRef;
            Lane = lane;
            DeltaMs = deltaMs;
            Type = type;
            Timestamp = DateTime.UtcNow;
        }
        
        /// <summary>
        /// Creates a new JudgementEvent with automatic judgement type calculation
        /// </summary>
        /// <param name="noteRef">Reference to the note</param>
        /// <param name="lane">Lane where judgement occurred</param>
        /// <param name="deltaMs">Timing delta in milliseconds</param>
        public JudgementEvent(int noteRef, int lane, double deltaMs) 
            : this(noteRef, lane, deltaMs, TimingConstants.GetJudgementType(deltaMs))
        {
        }
        
        #endregion
        
        #region Methods
        
        /// <summary>
        /// Gets the score value for this judgement event
        /// </summary>
        /// <returns>Score points for this judgement</returns>
        public int GetScoreValue()
        {
            return TimingConstants.GetScoreValue(Type);
        }
        
        /// <summary>
        /// Determines if this judgement represents a hit (not a miss)
        /// </summary>
        /// <returns>True if the note was hit, false if missed</returns>
        public bool IsHit()
        {
            return Type != JudgementType.Miss;
        }
        
        /// <summary>
        /// Determines if this judgement was early (negative delta)
        /// </summary>
        /// <returns>True if the hit was early</returns>
        public bool IsEarly()
        {
            return DeltaMs < 0;
        }
        
        /// <summary>
        /// Determines if this judgement was late (positive delta)
        /// </summary>
        /// <returns>True if the hit was late</returns>
        public bool IsLate()
        {
            return DeltaMs > 0;
        }
        
        /// <summary>
        /// Gets the absolute timing deviation
        /// </summary>
        /// <returns>Absolute value of timing delta</returns>
        public double GetAbsoluteDelta()
        {
            return Math.Abs(DeltaMs);
        }
        
        /// <summary>
        /// Returns a string representation of this judgement event
        /// </summary>
        /// <returns>Formatted string with event details</returns>
        public override string ToString()
        {
            var timingText = IsEarly() ? "early" : (IsLate() ? "late" : "perfect");
            
            return $"JudgementEvent[{Type}] Lane {Lane} ({DeltaMs:+0.0;-0.0;0.0}ms {timingText}) - {GetScoreValue()} pts";
        }
        
        #endregion
    }

    /// <summary>
    /// Static class containing timing constants for judgement windows.
    /// Based on DTXManiaNX timing specifications for accurate rhythm game scoring.
    /// All values are in milliseconds and represent the acceptable timing deviation from perfect hit timing.
    /// </summary>
    public static class TimingConstants
    {
        #region Judgement Windows (in milliseconds)
        
        /// <summary>
        /// Perfect timing window for "Perfect" judgement (±25ms)
        /// </summary>
        public const double PerfectWindowMs = 25.0;
        
        /// <summary>
        /// Great timing window for "Great" judgement (±50ms)
        /// </summary>
        public const double GreatWindowMs = 50.0;
        
        /// <summary>
        /// Good timing window for "Good" judgement (±100ms)
        /// </summary>
        public const double GoodWindowMs = 100.0;
        
        /// <summary>
        /// Poor timing window for "Poor" judgement (±150ms)
        /// </summary>
        public const double PoorWindowMs = 150.0;
        
        /// <summary>
        /// Miss threshold - notes beyond this timing are considered missed (±200ms)
        /// </summary>
        public const double MissThresholdMs = 200.0;
        
        #endregion
        
        #region Score Values
        
        /// <summary>
        /// Score points awarded for a "Perfect" hit
        /// </summary>
        public const int PerfectScore = 1000;
        
        /// <summary>
        /// Score points awarded for a "Great" hit
        /// </summary>
        public const int GreatScore = 700;
        
        /// <summary>
        /// Score points awarded for a "Good" hit
        /// </summary>
        public const int GoodScore = 400;
        
        /// <summary>
        /// Score points awarded for a "Poor" hit
        /// </summary>
        public const int PoorScore = 100;
        
        /// <summary>
        /// Score points awarded for a "Miss" (typically 0)
        /// </summary>
        public const int MissScore = 0;
        
        #endregion
        
        #region Helper Methods
        
        /// <summary>
        /// Determines the judgement type based on timing delta
        /// </summary>
        /// <param name="deltaMs">Timing delta in milliseconds (absolute value)</param>
        /// <returns>The appropriate judgement type</returns>
        public static JudgementType GetJudgementType(double deltaMs)
        {
            double absDelta = Math.Abs(deltaMs);
            
            if (absDelta <= PerfectWindowMs) return JudgementType.Perfect;
            if (absDelta <= GreatWindowMs) return JudgementType.Great;
            if (absDelta <= GoodWindowMs) return JudgementType.Good;
            if (absDelta <= PoorWindowMs) return JudgementType.Poor;
            
            return JudgementType.Miss;
        }
        
        /// <summary>
        /// Gets the score value for a specific judgement type
        /// </summary>
        /// <param name="judgementType">The judgement type</param>
        /// <returns>Score value for the judgement</returns>
        public static int GetScoreValue(JudgementType judgementType)
        {
            return judgementType switch
            {
                JudgementType.Perfect => PerfectScore,
                JudgementType.Great => GreatScore,
                JudgementType.Good => GoodScore,
                JudgementType.Poor => PoorScore,
                JudgementType.Miss => MissScore,
                _ => 0
            };
        }
        
        #endregion
    }

}
