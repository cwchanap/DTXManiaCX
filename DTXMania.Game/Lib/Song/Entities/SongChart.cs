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
        /// Legacy BPM property (nullable double)
        /// Maps to Bpm for backward compatibility
        /// </summary>
        [NotMapped]
        public double? BPM
        {
            get => Bpm > 0 ? Bpm : null;
            set => Bpm = value ?? 0;
        }
        
        /// <summary>
        /// Legacy DrumLevel property (nullable)
        /// Maps to DrumLevel for backward compatibility
        /// </summary>
        [NotMapped]
        public int? DrumLevelNullable
        {
            get => DrumLevel > 0 ? DrumLevel : null;
            set => DrumLevel = value ?? 0;
        }
        
        /// <summary>
        /// Legacy GuitarLevel property (nullable)
        /// Maps to GuitarLevel for backward compatibility
        /// </summary>
        [NotMapped]
        public int? GuitarLevelNullable
        {
            get => GuitarLevel > 0 ? GuitarLevel : null;
            set => GuitarLevel = value ?? 0;
        }
        
        /// <summary>
        /// Legacy BassLevel property (nullable)
        /// Maps to BassLevel for backward compatibility
        /// </summary>
        [NotMapped]
        public int? BassLevelNullable
        {
            get => BassLevel > 0 ? BassLevel : null;
            set => BassLevel = value ?? 0;
        }
        
        /// <summary>
        /// Legacy DrumNoteCount property (nullable)
        /// Maps to DrumNoteCount for backward compatibility
        /// </summary>
        [NotMapped]
        public int? DrumNoteCountNullable
        {
            get => DrumNoteCount > 0 ? DrumNoteCount : null;
            set => DrumNoteCount = value ?? 0;
        }
        
        /// <summary>
        /// Legacy GuitarNoteCount property (nullable)
        /// Maps to GuitarNoteCount for backward compatibility
        /// </summary>
        [NotMapped]
        public int? GuitarNoteCountNullable
        {
            get => GuitarNoteCount > 0 ? GuitarNoteCount : null;
            set => GuitarNoteCount = value ?? 0;
        }
        
        /// <summary>
        /// Legacy BassNoteCount property (nullable)
        /// Maps to BassNoteCount for backward compatibility
        /// </summary>
        [NotMapped]
        public int? BassNoteCountNullable
        {
            get => BassNoteCount > 0 ? BassNoteCount : null;
            set => BassNoteCount = value ?? 0;
        }
        
        /// <summary>
        /// Total chip count across all instruments
        /// Legacy compatibility from SongMetadata
        /// </summary>
        [NotMapped]
        public int TotalNoteCount => DrumNoteCount + GuitarNoteCount + BassNoteCount;
        
        /// <summary>
        /// Difficulty labels for each instrument
        /// Legacy compatibility from SongMetadata
        /// </summary>
        [NotMapped]
        public Dictionary<string, string> DifficultyLabels { get; set; } = new();
        
        /// <summary>
        /// Gets formatted duration string (MM:SS)
        /// Legacy compatibility from SongMetadata
        /// </summary>
        [NotMapped]
        public string FormattedDuration
        {
            get
            {
                if (Duration <= 0)
                    return "--:--";
                
                var totalSeconds = (int)Duration;
                var minutes = totalSeconds / 60;
                var seconds = totalSeconds % 60;
                return $"{minutes:D2}:{seconds:D2}";
            }
        }
        
        /// <summary>
        /// Preview image file alias
        /// Legacy compatibility from SongMetadata
        /// </summary>
        [NotMapped]
        public string? BackgroundImage
        {
            get => !string.IsNullOrEmpty(BackgroundFile) ? BackgroundFile : null;
            set => BackgroundFile = value ?? "";
        }
        
        #endregion
        
        #region Legacy SongMetadata Methods
        
        /// <summary>
        /// Creates a copy of this chart metadata
        /// Legacy compatibility from SongMetadata
        /// </summary>
        public SongChart Clone()
        {
            return new SongChart
            {
                SongId = SongId,
                FilePath = FilePath,
                FileHash = FileHash,
                FileSize = FileSize,
                LastModified = LastModified,
                DifficultyLevel = DifficultyLevel,
                DifficultyLabel = DifficultyLabel,
                Bpm = Bpm,
                Duration = Duration,
                BGMAdjust = BGMAdjust,
                DrumLevel = DrumLevel,
                DrumLevelDec = DrumLevelDec,
                GuitarLevel = GuitarLevel,
                GuitarLevelDec = GuitarLevelDec,
                BassLevel = BassLevel,
                BassLevelDec = BassLevelDec,
                HasDrumChart = HasDrumChart,
                HasGuitarChart = HasGuitarChart,
                HasBassChart = HasBassChart,
                IsClassicDrums = IsClassicDrums,
                IsClassicGuitar = IsClassicGuitar,
                IsClassicBass = IsClassicBass,
                DrumNoteCount = DrumNoteCount,
                GuitarNoteCount = GuitarNoteCount,
                BassNoteCount = BassNoteCount,
                PreviewFile = PreviewFile,
                PreviewImage = PreviewImage,
                BackgroundFile = BackgroundFile,
                StageFile = StageFile,
                FileFormat = FileFormat,
                DifficultyLabels = new Dictionary<string, string>(DifficultyLabels)
            };
        }
        
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
        
        /// <summary>
        /// Sets difficulty level for specified instrument
        /// Legacy compatibility from SongMetadata
        /// </summary>
        public void SetDifficultyLevel(string instrument, int level)
        {
            switch (instrument.ToUpperInvariant())
            {
                case "DRUMS":
                    DrumLevel = level;
                    HasDrumChart = level > 0;
                    break;
                case "GUITAR":
                    GuitarLevel = level;
                    HasGuitarChart = level > 0;
                    break;
                case "BASS":
                    BassLevel = level;
                    HasBassChart = level > 0;
                    break;
            }
        }
        
        /// <summary>
        /// Gets note count for specified instrument
        /// Legacy compatibility from SongMetadata
        /// </summary>
        public int? GetNoteCount(string instrument)
        {
            if (string.IsNullOrEmpty(instrument))
                return null;
            
            return instrument.ToUpperInvariant() switch
            {
                "DRUMS" => DrumNoteCount > 0 ? DrumNoteCount : null,
                "GUITAR" => GuitarNoteCount > 0 ? GuitarNoteCount : null,
                "BASS" => BassNoteCount > 0 ? BassNoteCount : null,
                _ => null
            };
        }
        
        /// <summary>
        /// Sets note count for specified instrument
        /// Legacy compatibility from SongMetadata
        /// </summary>
        public void SetNoteCount(string instrument, int count)
        {
            switch (instrument.ToUpperInvariant())
            {
                case "DRUMS":
                    DrumNoteCount = count;
                    break;
                case "GUITAR":
                    GuitarNoteCount = count;
                    break;
                case "BASS":
                    BassNoteCount = count;
                    break;
            }
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
