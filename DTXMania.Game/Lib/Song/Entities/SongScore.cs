using System;
using System.ComponentModel.DataAnnotations;
using DTXMania.Game.Lib.Song;

namespace DTXMania.Game.Lib.Song.Entities
{
    /// <summary>
    /// Song Score Entity - Performance records for a specific chart
    /// Merged from legacy SongScore and EF Core entity for comprehensive functionality
    /// Based on DTXManiaNX CScore patterns
    /// </summary>
    public class SongScore
    {
        #region EF Core Properties
        
        public int Id { get; set; }
        
        public int ChartId { get; set; }
        public virtual SongChart Chart { get; set; } = null!;
        
        public EInstrumentPart Instrument { get; set; }
        
        #endregion
        
        #region Legacy Compatibility Properties
        
        // Legacy Metadata property removed - use Chart relationship instead
        
        /// <summary>
        /// Difficulty level (0-100) - derived from chart or metadata
        /// </summary>
        [System.ComponentModel.DataAnnotations.Schema.NotMapped]
        public int DifficultyLevel 
        { 
            get 
            {
                if (Chart != null)
                {
                    return Instrument switch
                    {
                        EInstrumentPart.DRUMS => Chart.DrumLevel,
                        EInstrumentPart.GUITAR => Chart.GuitarLevel,
                        EInstrumentPart.BASS => Chart.BassLevel,
                        _ => 0
                    };
                }
                return _difficultyLevel;
            }
            set => _difficultyLevel = value;
        }
        private int _difficultyLevel;
        
        /// <summary>
        /// Difficulty label (e.g., "BASIC", "ADVANCED", "EXTREME")
        /// </summary>
        [MaxLength(50)]
        public string DifficultyLabel { get; set; } = "";
        
        #endregion
        
        #region Performance Data
        
        public int BestScore { get; set; }
        public int BestRank { get; set; } // Canonical rank bucket on the 0-100 scale; normalize legacy persisted ordinals before comparing.
        public double BestSkillPoint { get; set; }
        public double BestAchievementRate { get; set; }
        
        #endregion
        
        #region Statistics
        
        public bool FullCombo { get; set; }
        public bool Excellent { get; set; }
        public int PlayCount { get; set; }
        public int ClearCount { get; set; }
        public int MaxCombo { get; set; }
        
        #endregion
        
        #region Skill Calculation (Legacy)
        
        /// <summary>
        /// Highest skill value achieved
        /// </summary>
        public double HighSkill { get; set; }
        
        /// <summary>
        /// Current song skill value
        /// </summary>
        public double SongSkill { get; set; }
        
        #endregion
        
        #region Note Statistics (Legacy)
        
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
        
        #region Progress Tracking
        
        [MaxLength(50)]
        public string ProgressBar { get; set; } = ""; // Visual progress representation
        
        #endregion
        
        #region Last Play Information
        
        public DateTime? LastPlayedAt { get; set; }
        
        /// <summary>
        /// Last time this song was played (legacy compatibility)
        /// </summary>
        [System.ComponentModel.DataAnnotations.Schema.NotMapped]
        public DateTime? LastPlayed 
        { 
            get => LastPlayedAt; 
            set => LastPlayedAt = value; 
        }
        
        public int LastScore { get; set; }
        public double LastSkillPoint { get; set; }
        
        #endregion
        
        #region Input Method Tracking
        
        public bool UsedDrumPad { get; set; }
        public bool UsedKeyboard { get; set; }
        public bool UsedMidi { get; set; }
        public bool UsedJoypad { get; set; }
        public bool UsedMouse { get; set; }
        
        #endregion
        
        #region Calculated Properties (Legacy Compatibility)
        
        /// <summary>
        /// Gets the rank name for display
        /// </summary>
        [System.ComponentModel.DataAnnotations.Schema.NotMapped]
        public string RankName
        {
            get
            {
                return HasBeenPlayed ? RankString(NormalizeStoredBestRank(BestRank)) : "---";
            }
        }
        
        /// <summary>
        /// Gets the accuracy percentage
        /// </summary>
        [System.ComponentModel.DataAnnotations.Schema.NotMapped]
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
        [System.ComponentModel.DataAnnotations.Schema.NotMapped]
        public bool HasBeenPlayed => PlayCount > 0;
        
        /// <summary>
        /// Gets whether this is a new record
        /// </summary>
        [System.ComponentModel.DataAnnotations.Schema.NotMapped]
        public bool IsNewRecord { get; set; }
        
        #endregion
        
        #region Methods (Legacy Functionality)
        
        /// <summary>
        /// Updates the score with new performance data
        /// </summary>
        public bool UpdateScore(int score, int rank, bool fullCombo, 
            int perfect, int great, int good, int poor, int miss)
        {
            bool isNewBest = false;

            // Update play statistics
            bool hadPreviousPlays = PlayCount > 0; // capture before increment
            PlayCount++;
            LastPlayedAt = DateTime.UtcNow;
            int normalizedRank = NormalizeRankPercentage(rank);
            int currentBestRank = hadPreviousPlays ? NormalizeStoredBestRank(BestRank) : 0;
            
            // Normalize any legacy ordinal stored in BestRank
            if (hadPreviousPlays)
                BestRank = currentBestRank;

            // Check if this is a new best score
            if (score > BestScore)
            {
                BestScore = score;
                isNewBest = true;
            }

            // Check if this is a new best rank
            if (!hadPreviousPlays || normalizedRank > currentBestRank)
            {
                BestRank = normalizedRank;
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
            return RankMultiplier(NormalizeStoredBestRank(BestRank));
        }

        public static int NormalizeStoredBestRank(int rankValue)
        {
            if (IsLegacyOrdinal(rankValue))
            {
                return ConvertLegacyOrdinalToBucket(rankValue);
            }

            return NormalizeRankPercentage(rankValue);
        }

        public static int ComputeRankIndex(int rankValue)
        {
            int percentage = NormalizeRankPercentage(rankValue);
            return percentage switch
            {
                95 => 0,
                90 => 1,
                80 => 2,
                70 => 3,
                60 => 4,
                50 => 5,
                40 => 6,
                _ => 7
            };
        }

        public static string RankString(int rankValue)
        {
            return ComputeRankIndex(rankValue) switch
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

        public static double RankMultiplier(int rankValue)
        {
            return ComputeRankIndex(rankValue) switch
            {
                0 => 1.0,
                1 => 0.95,
                2 => 0.9,
                3 => 0.85,
                4 => 0.8,
                5 => 0.75,
                6 => 0.7,
                7 => 0.65,
                _ => 0.5
            };
        }

        private static bool IsLegacyOrdinal(int rankValue)
        {
            return rankValue >= 1 && rankValue <= 7;
        }

        private static int ConvertLegacyOrdinalToBucket(int rankValue)
        {
            // Legacy ordinals range: 1 (S) through 7 (F).
            // Note: ordinal 0 (SS in the old system) is excluded from IsLegacyOrdinal — a stored
            // value of 0 is treated as the F bucket on the new percentage scale. Legacy SS data
            // cannot be recovered without a separate migration flag.
            return rankValue switch
            {
                1 => 90,
                2 => 80,
                3 => 70,
                4 => 60,
                5 => 50,
                6 => 40,
                7 => 0,
                _ => throw new ArgumentOutOfRangeException(nameof(rankValue), "Legacy rank ordinals must be between 1 and 7.")
            };
        }

        private static int NormalizeRankPercentage(int rankValue)
        {
            int clampedRank = Math.Clamp(rankValue, 0, 100);
            return clampedRank switch
            {
                >= 95 => 95,
                >= 90 => 90,
                >= 80 => 80,
                >= 70 => 70,
                >= 60 => 60,
                >= 50 => 50,
                >= 40 => 40,
                _ => 0
            };
        }
        
        /// <summary>
        /// Creates a copy of this score (legacy compatibility)
        /// </summary>
        public SongScore Clone()
        {
            return new SongScore
            {
                // Legacy metadata no longer cloned - using EF Core relationships,
                ChartId = ChartId,
                Chart = Chart,
                Instrument = Instrument,
                DifficultyLevel = DifficultyLevel,
                DifficultyLabel = DifficultyLabel,
                BestScore = BestScore,
                BestRank = NormalizeStoredBestRank(BestRank),
                FullCombo = FullCombo,
                PlayCount = PlayCount,
                LastPlayedAt = LastPlayedAt,
                HighSkill = HighSkill,
                SongSkill = SongSkill,
                TotalNotes = TotalNotes,
                BestPerfect = BestPerfect,
                BestGreat = BestGreat,
                BestGood = BestGood,
                BestPoor = BestPoor,
                BestMiss = BestMiss,
                IsNewRecord = IsNewRecord,
                BestSkillPoint = BestSkillPoint,
                BestAchievementRate = BestAchievementRate,
                Excellent = Excellent,
                ClearCount = ClearCount,
                MaxCombo = MaxCombo,
                ProgressBar = ProgressBar,
                LastScore = LastScore,
                LastSkillPoint = LastSkillPoint,
                UsedDrumPad = UsedDrumPad,
                UsedKeyboard = UsedKeyboard,
                UsedMidi = UsedMidi,
                UsedJoypad = UsedJoypad,
                UsedMouse = UsedMouse
            };
        }
        
        #endregion
    }
}
