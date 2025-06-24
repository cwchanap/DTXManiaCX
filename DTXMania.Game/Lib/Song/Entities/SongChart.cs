using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace DTXMania.Game.Lib.Song.Entities
{
    /// <summary>
    /// Song Chart Entity - Represents an individual DTX file (1:1 mapping)
    /// </summary>
    public class SongChart
    {
        public int Id { get; set; }
        
        public int SongId { get; set; }
        public virtual Song Song { get; set; } = null!;
        
        // File Information (DTX file specific)
        [Required, MaxLength(500)]
        public string FilePath { get; set; } = "";
        public string FileHash { get; set; } = ""; // MD5 hash for integrity
        public long FileSize { get; set; }
        public DateTime LastModified { get; set; }
        
        // Chart Properties
        public int DifficultyLevel { get; set; } // 0-4 (Basic to Master)
        [MaxLength(50)]
        public string DifficultyLabel { get; set; } = "";
        
        // Game Information (DTX file specific)
        public double Bpm { get; set; }
        public double Duration { get; set; } // In seconds
        public int BGMAdjust { get; set; }
        
        // Level Information
        public int DrumLevel { get; set; }
        public int DrumLevelDec { get; set; } // Decimal part (0-99)
        public int GuitarLevel { get; set; }
        public int GuitarLevelDec { get; set; }
        public int BassLevel { get; set; }
        public int BassLevelDec { get; set; }
        
        // Chart Information
        public bool HasDrumChart { get; set; }
        public bool HasGuitarChart { get; set; }
        public bool HasBassChart { get; set; }
        public bool IsClassicDrums { get; set; }
        public bool IsClassicGuitar { get; set; }
        public bool IsClassicBass { get; set; }
        
        // Note Counts
        public int DrumNoteCount { get; set; }
        public int GuitarNoteCount { get; set; }
        public int BassNoteCount { get; set; }
        
        // Resources (chart specific)
        [MaxLength(200)]
        public string PreviewFile { get; set; } = "";
        [MaxLength(200)]
        public string PreviewImage { get; set; } = "";
        [MaxLength(200)]
        public string BackgroundFile { get; set; } = "";
        
        // Navigation Properties
        public virtual ICollection<SongScore> Scores { get; set; } = new List<SongScore>();
    }
}
