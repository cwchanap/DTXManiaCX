#nullable enable

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;

namespace DTXMania.Game.Lib.Song.Entities
{
    /// <summary>
    /// Song Chart Entity - Represents an individual DTX file (1:1 mapping)
    /// Merged from legacy SongMetadata for DTXMania compatibility
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
        
        // Additional media files from SongMetadata
        [MaxLength(200)]
        public string StageFile { get; set; } = "";
        [MaxLength(100)]
        public string FileFormat { get; set; } = "";
        
        // Navigation Properties
        public virtual ICollection<SongScore> Scores { get; set; } = new List<SongScore>();
        
        #region Legacy SongMetadata Compatibility Properties

        /// <summary>
        /// Total chip count across all instruments
        /// </summary>
        [NotMapped]
        public int TotalNoteCount => DrumNoteCount + GuitarNoteCount + BassNoteCount;

        /// <summary>
        /// Difficulty labels for each instrument
        /// Legacy compatibility from SongMetadata
        /// </summary>
        [NotMapped]
        public Dictionary<string, string> DifficultyLabels { get; set; } = new();

        #endregion

        #region Legacy SongMetadata Methods

        /// <summary>
        /// Gets difficulty level for specified instrument
        /// Legacy compatibility from SongMetadata
        /// </summary>
        public int? GetDifficultyLevel(string instrument)
        {
            if (string.IsNullOrEmpty(instrument))
                return null;

            return instrument.ToUpperInvariant() switch
            {
                "DRUMS" => DrumLevel > 0 ? DrumLevel : null,
                "GUITAR" => GuitarLevel > 0 ? GuitarLevel : null,
                "BASS" => BassLevel > 0 ? BassLevel : null,
                _ => null
            };
        }

        #endregion
        
        #region Note Count Calculation Methods
        
        /// <summary>
        /// Gets the total note count for a specific instrument
        /// Consolidates note count logic for UI components
        /// </summary>
        /// <param name="instrument">Instrument name (DRUMS, GUITAR, BASS)</param>
        /// <returns>Note count for the specified instrument, or 0 if not available</returns>
        public int GetInstrumentNoteCount(string instrument)
        {
            if (string.IsNullOrEmpty(instrument))
                return 0;
                
            return instrument.ToUpperInvariant() switch
            {
                "DRUMS" => DrumNoteCount,
                "GUITAR" => GuitarNoteCount,
                "BASS" => BassNoteCount,
                _ => 0
            };
        }
        
        /// <summary>
        /// Gets the total note count for a specific instrument part enum
        /// </summary>
        /// <param name="instrumentPart">Instrument part enum</param>
        /// <returns>Note count for the specified instrument part</returns>
        public int GetInstrumentNoteCount(DTXMania.Game.Lib.Song.Entities.EInstrumentPart instrumentPart)
        {
            return instrumentPart switch
            {
                DTXMania.Game.Lib.Song.Entities.EInstrumentPart.DRUMS => DrumNoteCount,
                DTXMania.Game.Lib.Song.Entities.EInstrumentPart.GUITAR => GuitarNoteCount,
                DTXMania.Game.Lib.Song.Entities.EInstrumentPart.BASS => BassNoteCount,
                _ => 0
            };
        }
        
        /// <summary>
        /// Gets note count statistics for display purposes
        /// </summary>
        /// <returns>Tuple containing (TotalNotes, DrumNotes, GuitarNotes, BassNotes)</returns>
        public (int Total, int Drums, int Guitar, int Bass) GetNoteCountStats()
        {
            return (TotalNoteCount, DrumNoteCount, GuitarNoteCount, BassNoteCount);
        }
        
        /// <summary>
        /// Checks if the chart has any notes for any instrument
        /// </summary>
        /// <returns>True if the chart has any notes, false otherwise</returns>
        public bool HasAnyNotes()
        {
            return TotalNoteCount > 0;
        }
        
        /// <summary>
        /// Gets formatted note count string for display
        /// </summary>
        /// <param name="includeBreakdown">If true, includes breakdown by instrument</param>
        /// <returns>Formatted note count string</returns>
        public string GetFormattedNoteCount(bool includeBreakdown = false)
        {
            if (!HasAnyNotes())
                return "No notes";
                
            if (!includeBreakdown)
                return $"{TotalNoteCount:N0} notes";
                
            var parts = new List<string>();
            if (DrumNoteCount > 0) parts.Add($"D:{DrumNoteCount:N0}");
            if (GuitarNoteCount > 0) parts.Add($"G:{GuitarNoteCount:N0}");
            if (BassNoteCount > 0) parts.Add($"B:{BassNoteCount:N0}");
            
            return parts.Count > 0 
                ? $"{TotalNoteCount:N0} total ({string.Join(", ", parts)})"
                : $"{TotalNoteCount:N0} notes";
        }
        
        #endregion
    }
}
