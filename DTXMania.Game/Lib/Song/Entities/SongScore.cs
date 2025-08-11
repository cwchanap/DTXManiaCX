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
        public int BestRank { get; set; } // 0-7: SS, S, A, B, C, D, E, F
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
            PlayCount++;
            LastPlayedAt = DateTime.Now;
            
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
                BestRank = BestRank,
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