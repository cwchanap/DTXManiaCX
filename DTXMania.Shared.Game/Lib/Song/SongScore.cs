using System;

namespace DTX.Song
{
    /// <summary>
    /// Performance score data for a specific song and difficulty
    /// Based on DTXManiaNX CScore patterns
    /// </summary>
    public class SongScore
    {
        #region Basic Information

        /// <summary>
        /// Reference to the song metadata
        /// </summary>
        public SongMetadata Metadata { get; set; } = new();

        /// <summary>
        /// Instrument type (DRUMS, GUITAR, BASS)
        /// </summary>
        public string Instrument { get; set; } = "";

        /// <summary>
        /// Difficulty level (0-100)
        /// </summary>
        public int DifficultyLevel { get; set; }

        /// <summary>
        /// Difficulty label (e.g., "BASIC", "ADVANCED", "EXTREME")
        /// </summary>
        public string DifficultyLabel { get; set; } = "";

        #endregion

        #region Performance Statistics

        /// <summary>
        /// Best score achieved
        /// </summary>
        public int BestScore { get; set; }

        /// <summary>
        /// Best rank achieved (0-7: SS, S, A, B, C, D, E, F)
        /// </summary>
        public int BestRank { get; set; }

        /// <summary>
        /// Whether full combo has been achieved
        /// </summary>
        public bool FullCombo { get; set; }

        /// <summary>
        /// Number of times this song has been played
        /// </summary>
        public int PlayCount { get; set; }

        /// <summary>
        /// Last time this song was played
        /// </summary>
        public DateTime? LastPlayed { get; set; }

        #endregion

        #region Skill Calculation

        /// <summary>
        /// Highest skill value achieved
        /// </summary>
        public double HighSkill { get; set; }

        /// <summary>
        /// Current song skill value
        /// </summary>
        public double SongSkill { get; set; }

        #endregion

        #region Note Statistics

        /// <summary>
        /// Total number of notes in the song
        /// </summary>
        public int TotalNotes { get; set; }

        /// <summary>
        /// Best perfect hit count
        /// </summary>
        public int BestPerfect { get; set; }

        /// <summary>
        /// Best great hit count
        /// </summary>
        public int BestGreat { get; set; }

        /// <summary>
        /// Best good hit count
        /// </summary>
        public int BestGood { get; set; }

        /// <summary>
        /// Best poor hit count
        /// </summary>
        public int BestPoor { get; set; }

        /// <summary>
        /// Best miss count
        /// </summary>
        public int BestMiss { get; set; }

        #endregion

        #region Calculated Properties

        /// <summary>
        /// Gets the rank name for display
        /// </summary>
        public string RankName
        {
            get
            {
                return BestRank switch
                {
                    0 => "SS",
                    1 => "S",
                    2 => "A",
                    3 => "B",
                    4 => "C",
                    5 => "D",
                    6 => "E",
                    7 => "F",
                    _ => "---"
                };
            }
        }

        /// <summary>
        /// Gets the accuracy percentage
        /// </summary>
        public double Accuracy
        {
            get
            {
                if (TotalNotes == 0) return 0.0;
                var hitNotes = BestPerfect + BestGreat + BestGood + BestPoor;
                return (double)hitNotes / TotalNotes * 100.0;
            }
        }

        /// <summary>
        /// Gets whether this score has been played
        /// </summary>
        public bool HasBeenPlayed => PlayCount > 0;

        /// <summary>
        /// Gets whether this is a new record
        /// </summary>
        public bool IsNewRecord { get; set; }

        #endregion

        #region Methods

        /// <summary>
        /// Updates the score with new performance data
        /// </summary>
        public bool UpdateScore(int score, int rank, bool fullCombo, 
            int perfect, int great, int good, int poor, int miss)
        {
            bool isNewBest = false;

            // Update play statistics
            PlayCount++;
            LastPlayed = DateTime.Now;

            // Check if this is a new best score
            if (score > BestScore)
            {
                BestScore = score;
                isNewBest = true;
            }

            // Check if this is a new best rank
            if (rank < BestRank || BestRank == 0)
            {
                BestRank = rank;
                isNewBest = true;
            }

            // Update full combo status
            if (fullCombo && !FullCombo)
            {
                FullCombo = true;
                isNewBest = true;
            }

            // Update best note statistics if this is a better performance
            if (isNewBest || score == BestScore)
            {
                BestPerfect = perfect;
                BestGreat = great;
                BestGood = good;
                BestPoor = poor;
                BestMiss = miss;
            }

            IsNewRecord = isNewBest;
            return isNewBest;
        }

        /// <summary>
        /// Calculates skill value based on score and difficulty
        /// </summary>
        public void CalculateSkill()
        {
            if (BestScore == 0 || DifficultyLevel == 0)
            {
                SongSkill = 0;
                return;
            }

            // DTXMania skill calculation formula
            // Skill = (Score / 1000000) * DifficultyLevel * Multiplier
            var scoreRatio = (double)BestScore / 1000000.0;
            var difficultyMultiplier = DifficultyLevel / 100.0;
            var rankMultiplier = GetRankMultiplier();

            SongSkill = scoreRatio * difficultyMultiplier * rankMultiplier * 100.0;

            if (SongSkill > HighSkill)
                HighSkill = SongSkill;
        }

        /// <summary>
        /// Gets rank multiplier for skill calculation
        /// </summary>
        private double GetRankMultiplier()
        {
            return BestRank switch
            {
                0 => 1.0,  // SS
                1 => 0.95, // S
                2 => 0.9,  // A
                3 => 0.85, // B
                4 => 0.8,  // C
                5 => 0.75, // D
                6 => 0.7,  // E
                7 => 0.65, // F
                _ => 0.5
            };
        }

        /// <summary>
        /// Creates a copy of this score
        /// </summary>
        public SongScore Clone()
        {
            return new SongScore
            {
                Metadata = Metadata.Clone(),
                Instrument = Instrument,
                DifficultyLevel = DifficultyLevel,
                DifficultyLabel = DifficultyLabel,
                BestScore = BestScore,
                BestRank = BestRank,
                FullCombo = FullCombo,
                PlayCount = PlayCount,
                LastPlayed = LastPlayed,
                HighSkill = HighSkill,
                SongSkill = SongSkill,
                TotalNotes = TotalNotes,
                BestPerfect = BestPerfect,
                BestGreat = BestGreat,
                BestGood = BestGood,
                BestPoor = BestPoor,
                BestMiss = BestMiss,
                IsNewRecord = IsNewRecord
            };
        }

        #endregion
    }
}
