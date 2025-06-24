using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace DTXMania.Game.Lib.Song.Entities
{
    /// <summary>
    /// Song Entity - High-level song metadata (shared across all charts of the same song)
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
    }
}
