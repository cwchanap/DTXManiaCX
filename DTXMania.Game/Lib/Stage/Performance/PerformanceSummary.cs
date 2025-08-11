using System;
using DTXMania.Game.Lib.Song.Entities;

namespace DTXMania.Game.Lib.Stage.Performance
{
    /// <summary>
    /// Data Transfer Object containing performance results for passing to ResultStage.
    /// Contains score, combo, accuracy counts, and clear flag information.
    /// </summary>
    public class PerformanceSummary
    {
        #region Properties

        /// <summary>
        /// Final score achieved
        /// </summary>
        public int Score { get; set; }

        /// <summary>
        /// Maximum combo achieved during the performance
        /// </summary>
        public int MaxCombo { get; set; }

        /// <summary>
        /// Whether the player cleared the song (didn't fail)
        /// </summary>
        public bool ClearFlag { get; set; }

        /// <summary>
        /// Count of Just judgements
        /// </summary>
        public int JustCount { get; set; }

        /// <summary>
        /// Count of Great judgements
        /// </summary>
        public int GreatCount { get; set; }

        /// <summary>
        /// Count of Good judgements
        /// </summary>
        public int GoodCount { get; set; }

        /// <summary>
        /// Count of Poor judgements
        /// </summary>
        public int PoorCount { get; set; }

        /// <summary>
        /// Count of Miss judgements
        /// </summary>
        public int MissCount { get; set; }

        /// <summary>
        /// Total number of notes in the chart
        /// </summary>
        public int TotalNotes { get; set; }

        /// <summary>
        /// Final life gauge value (0.0 to 100.0)
        /// </summary>
        public float FinalLife { get; set; }

        /// <summary>
        /// Performance completion reason
        /// </summary>
        public CompletionReason CompletionReason { get; set; }

        #endregion

        #region Calculated Properties

        /// <summary>
        /// Total number of judgements made
        /// </summary>
        public int TotalJudgements => JustCount + GreatCount + GoodCount + PoorCount + MissCount;

        /// <summary>
        /// Accuracy percentage (0.0 to 100.0)
        /// Based on weighted scoring of judgement types
        /// </summary>
        public double Accuracy
        {
            get
            {
                if (TotalNotes <= 0) return 0.0;

                // Weight judgements similar to scoring system
                double weightedHits = (JustCount * 1.0) + (GreatCount * 0.9) + (GoodCount * 0.5);
                double maxPossibleWeight = TotalNotes * 1.0;

                return maxPossibleWeight > 0 ? (weightedHits / maxPossibleWeight) * 100.0 : 0.0;
            }
        }

        /// <summary>
        /// Hit rate percentage (successful hits vs total notes)
        /// </summary>
        public double HitRate
        {
            get
            {
                if (TotalNotes <= 0) return 0.0;
                int successfulHits = JustCount + GreatCount + GoodCount;
                return ((double)successfulHits / TotalNotes) * 100.0;
            }
        }

        #endregion

        #region Constructor

        /// <summary>
        /// Creates a new PerformanceSummary with default values
        /// </summary>
        public PerformanceSummary()
        {
            Score = 0;
            MaxCombo = 0;
            ClearFlag = false;
            JustCount = 0;
            GreatCount = 0;
            GoodCount = 0;
            PoorCount = 0;
            MissCount = 0;
            TotalNotes = 0;
            FinalLife = 0.0f;
            CompletionReason = CompletionReason.Unknown;
        }

        #endregion

        #region Methods

        /// <summary>
        /// Increments the count for a specific judgement type
        /// </summary>
        /// <param name="judgementType">Type of judgement to increment</param>
        public void IncrementJudgement(JudgementType judgementType)
        {
            switch (judgementType)
            {
                case JudgementType.Just:
                    JustCount++;
                    break;
                case JudgementType.Great:
                    GreatCount++;
                    break;
                case JudgementType.Good:
                    GoodCount++;
                    break;
                case JudgementType.Poor:
                    PoorCount++;
                    break;
                case JudgementType.Miss:
                    MissCount++;
                    break;
            }
        }

        /// <summary>
        /// Gets the count for a specific judgement type
        /// </summary>
        /// <param name="judgementType">Type of judgement to get count for</param>
        /// <returns>Count of the specified judgement type</returns>
        public int GetJudgementCount(JudgementType judgementType)
        {
            return judgementType switch
            {
                JudgementType.Just => JustCount,
                JudgementType.Great => GreatCount,
                JudgementType.Good => GoodCount,
                JudgementType.Poor => PoorCount,
                JudgementType.Miss => MissCount,
                _ => 0
            };
        }

        /// <summary>
        /// Returns a string representation of the performance summary
        /// </summary>
        public override string ToString()
        {
            return $"Score: {Score:N0}, Max Combo: {MaxCombo}, " +
                   $"Accuracy: {Accuracy:F1}%, Clear: {ClearFlag}, " +
                   $"J/G/G/P/M: {JustCount}/{GreatCount}/{GoodCount}/{PoorCount}/{MissCount}";
        }

        #endregion
    }

    /// <summary>
    /// Enum representing the reason for performance completion
    /// </summary>
    public enum CompletionReason
    {
        /// <summary>
        /// Unknown or unspecified reason
        /// </summary>
        Unknown,

        /// <summary>
        /// Song completed successfully (reached end)
        /// </summary>
        SongComplete,

        /// <summary>
        /// Player failed (life gauge reached failure threshold)
        /// </summary>
        PlayerFailed,

        /// <summary>
        /// Player manually quit/escaped
        /// </summary>
        PlayerQuit
    }
}
