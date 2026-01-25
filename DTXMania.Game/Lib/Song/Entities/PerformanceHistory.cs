using System;
using System.ComponentModel.DataAnnotations;

namespace DTXMania.Game.Lib.Song.Entities
{
    /// <summary>
    /// Performance History Entity
    /// </summary>
    public class PerformanceHistory
    {
        public int Id { get; set; }
        
        public int SongId { get; set; }
        public virtual Song Song { get; set; } = null!;
        
        public DateTime PerformedAt { get; set; }
        
        [MaxLength(500)]
        public string HistoryLine { get; set; } = "";
        
        public int DisplayOrder { get; set; } // 1-5 for 5 history lines
    }
}
