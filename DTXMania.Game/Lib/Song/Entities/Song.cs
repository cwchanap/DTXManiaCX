using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;

namespace DTXMania.Game.Lib.Song.Entities
{
    /// <summary>
    /// Song Entity - High-level song metadata (shared across all charts of the same song)
    /// Merged from legacy SongMetadata for DTXMania compatibility
    /// </summary>
    public class Song
    {
        public int Id { get; set; }
        
        // Metadata (shared across all charts)
        [MaxLength(200)]
        public string Title { get; set; } = "";
        [MaxLength(200)]
        public string Artist { get; set; } = "";
        [MaxLength(100)]
        public string Genre { get; set; } = "";
        [MaxLength(1000)]
        public string Comment { get; set; } = "";
        
        // Timestamps
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        
        // Navigation Properties
        public virtual ICollection<SongChart> Charts { get; set; } = new List<SongChart>();
        
        #region Legacy SongMetadata Compatibility Properties
        
        /// <summary>
        /// Gets display title (falls back to filename if title is empty)
        /// </summary>
        [NotMapped]
        public string DisplayTitle
        {
            get
            {
                if (!string.IsNullOrEmpty(Title))
                    return Title;
                
                // Try to get filename from first chart
                var firstChart = Charts?.FirstOrDefault();
                if (firstChart != null && !string.IsNullOrEmpty(firstChart.FilePath))
                    return System.IO.Path.GetFileNameWithoutExtension(firstChart.FilePath);
                
                return "Unknown Song";
            }
        }
        
        /// <summary>
        /// Gets display artist (falls back to "Unknown" if empty)
        /// </summary>
        [NotMapped]
        public string DisplayArtist => string.IsNullOrEmpty(Artist) ? "Unknown Artist" : Artist;
        
        /// <summary>
        /// Gets display genre (falls back to "Unknown" if empty)
        /// </summary>
        [NotMapped]
        public string DisplayGenre => string.IsNullOrEmpty(Genre) ? "Unknown Genre" : Genre;
        
        /// <summary>
        /// Gets the highest difficulty level across all charts
        /// </summary>
        [NotMapped]
        public int MaxDifficultyLevel
        {
            get
            {
                if (Charts == null || !Charts.Any())
                    return 0;
                
                var maxLevel = 0;
                foreach (var chart in Charts)
                {
                    var levels = new[] { chart.DrumLevel, chart.GuitarLevel, chart.BassLevel };
                    foreach (var level in levels)
                    {
                        if (level > maxLevel)
                            maxLevel = level;
                    }
                }
                return maxLevel;
            }
        }
        
        /// <summary>
        /// Gets available instruments based on charts
        /// </summary>
        [NotMapped]
        public List<string> AvailableInstruments
        {
            get
            {
                var instruments = new HashSet<string>();
                if (Charts != null)
                {
                    foreach (var chart in Charts)
                    {
                        if (chart.HasDrumChart && chart.DrumLevel > 0) instruments.Add("DRUMS");
                        if (chart.HasGuitarChart && chart.GuitarLevel > 0) instruments.Add("GUITAR");
                        if (chart.HasBassChart && chart.BassLevel > 0) instruments.Add("BASS");
                    }
                }
                return instruments.ToList();
            }
        }
        
        #endregion
        
        #region Legacy SongMetadata Methods
        
        /// <summary>
        /// Creates a copy of this song metadata
        /// </summary>
        public Song Clone()
        {
            return new Song
            {
                Title = Title,
                Artist = Artist,
                Genre = Genre,
                Comment = Comment,
                CreatedAt = CreatedAt,
                UpdatedAt = UpdatedAt
                // Note: Charts are not cloned to avoid deep copy complexity
            };
        }
        
        #endregion
    }
}
