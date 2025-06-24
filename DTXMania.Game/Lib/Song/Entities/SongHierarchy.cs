using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.Xna.Framework;

namespace DTXMania.Game.Lib.Song.Entities
{
    /// <summary>
    /// Song Hierarchy Entity - Folder structure
    /// </summary>
    public class SongHierarchy
    {
        public int Id { get; set; }
        
        public int? SongId { get; set; }
        public virtual Song Song { get; set; }
        
        public int? ParentId { get; set; }
        public virtual SongHierarchy Parent { get; set; }
        public virtual ICollection<SongHierarchy> Children { get; set; } = new List<SongHierarchy>();
        
        [Required]
        public ENodeType NodeType { get; set; }
        
        [MaxLength(200)]
        public string Title { get; set; } = "";
        
        [MaxLength(100)]
        public string Genre { get; set; }
        
        public int DisplayOrder { get; set; }
        
        // Visual Properties
        public int TextColorArgb { get; set; } = unchecked((int)0xFFFFFFFF); // White color as ARGB
        
        [MaxLength(500)]
        public string SkinPath { get; set; }
        
        // Navigation
        [MaxLength(1000)]
        public string BreadcrumbPath { get; set; } = "";
        
        // Random Selection
        public bool IncludeInRandom { get; set; } = true;
    }
}
