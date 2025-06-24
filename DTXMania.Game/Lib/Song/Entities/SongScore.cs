using System;
using System.ComponentModel.DataAnnotations;

namespace DTXMania.Game.Lib.Song.Entities
{
    /// <summary>
    /// Song Score Entity - Performance records for a specific chart
    /// </summary>
    public class SongScore
    {
        public int Id { get; set; }
        
        public int ChartId { get; set; }
        public virtual SongChart Chart { get; set; } = null!;
        
        public EInstrumentPart Instrument { get; set; }
        
        // Performance Data
        public int BestScore { get; set; }
        public int BestRank { get; set; } // E-SS
        public double BestSkillPoint { get; set; }
        public double BestAchievementRate { get; set; }
        
        // Statistics
        public bool FullCombo { get; set; }
        public bool Excellent { get; set; }
        public int PlayCount { get; set; }
        public int ClearCount { get; set; }
        public int MaxCombo { get; set; }
        
        // Progress Tracking
        [MaxLength(50)]
        public string ProgressBar { get; set; } = ""; // Visual progress representation
        
        // Last Play Information
        public DateTime? LastPlayedAt { get; set; }
        public int LastScore { get; set; }
        public double LastSkillPoint { get; set; }
        
        // Input Method Tracking
        public bool UsedDrumPad { get; set; }
        public bool UsedKeyboard { get; set; }
        public bool UsedMidi { get; set; }
        public bool UsedJoypad { get; set; }
        public bool UsedMouse { get; set; }
    }
}
