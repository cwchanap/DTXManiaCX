using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using DTXMania.Game.Lib.Song.Entities;

// Type aliases for EF Core entities
using SongScore = DTXMania.Game.Lib.Song.Entities.SongScore;
using SongEntity = DTXMania.Game.Lib.Song.Entities.Song;
using SongChart = DTXMania.Game.Lib.Song.Entities.SongChart;

namespace DTX.Song
{
    /// <summary>
    /// Node types for song list hierarchy
    /// Based on DTXManiaNX ENodeType
    /// </summary>
    public enum NodeType
    {
        /// <summary>
        /// Individual song file
        /// </summary>
        Score = 0,

        /// <summary>
        /// Folder container (BOX)
        /// </summary>
        Box = 1,

        /// <summary>
        /// Back/parent navigation item
        /// </summary>
        BackBox = 2,

        /// <summary>
        /// Random song selection placeholder
        /// </summary>
        Random = 3
    }

    /// <summary>
    /// Hierarchical song list node structure
    /// Based on DTXManiaNX CSongListNode patterns
    /// </summary>
    public class SongListNode
    {
        #region Core Properties

        /// <summary>
        /// Node type (Score, Box, BackBox, Random)
        /// </summary>
        public NodeType Type { get; set; } = NodeType.Score;

        /// <summary>
        /// Song scores for up to 5 difficulty levels
        /// Index corresponds to difficulty: 0=BASIC, 1=ADVANCED, 2=EXTREME, etc.
        /// </summary>
        public SongScore[] Scores { get; set; } = new SongScore[5];

        /// <summary>
        /// Difficulty labels for each level
        /// </summary>
        public string[] DifficultyLabels { get; set; } = new string[5];

        /// <summary>
        /// Child nodes for BOX navigation
        /// </summary>
        public List<SongListNode> Children { get; set; } = new();

        /// <summary>
        /// Parent node reference
        /// </summary>
        public SongListNode? Parent { get; set; }

        #endregion

        #region Display Properties

        /// <summary>
        /// Display title for the node
        /// </summary>
        public string Title { get; set; } = "";

        /// <summary>
        /// Genre classification
        /// </summary>
        public string Genre { get; set; } = "";

        /// <summary>
        /// Text color for display
        /// </summary>
        public Color TextColor { get; set; } = Color.White;

        /// <summary>
        /// Navigation breadcrumb path
        /// </summary>
        public string BreadcrumbPath { get; set; } = "";

        /// <summary>
        /// Custom skin path for BOX folders
        /// </summary>
        public string? SkinPath { get; set; }

        #endregion

        #region Metadata

        // Legacy Metadata property removed - use DatabaseSong and DatabaseChart instead
        
        /// <summary>
        /// EF Core Song entity for database integration
        /// </summary>
        public SongEntity? DatabaseSong { get; set; }
        
        /// <summary>
        /// EF Core SongChart entity for database integration
        /// </summary>
        public SongChart? DatabaseChart { get; set; }

        /// <summary>
        /// Directory path for BOX nodes
        /// </summary>
        public string? DirectoryPath { get; set; }

        /// <summary>
        /// Whether this node is expanded (for BOX nodes)
        /// </summary>
        public bool IsExpanded { get; set; }

        /// <summary>
        /// Sort order within parent
        /// </summary>
        public int SortOrder { get; set; }

        /// <summary>
        /// Database song ID for EF Core integration
        /// </summary>
        public int? DatabaseSongId { get; set; }

        #endregion

        #region EF Core Integration Methods

        /// <summary>
        /// Populates the legacy Scores array from EF Core data for UI compatibility
        /// </summary>
        public void PopulateScoresFromDatabase(DTXMania.Game.Lib.Song.Entities.SongDatabaseService databaseService)
        {
            if (DatabaseSongId == null) return;

            try
            {
                // Use DatabaseSong if available
                var availableInstruments = DatabaseSong?.AvailableInstruments ?? new List<string>();
                
                for (int i = 0; i < Math.Min(availableInstruments.Count, 5); i++)
                {
                    var instrument = availableInstruments[i];
                    var firstChart = DatabaseSong?.Charts?.FirstOrDefault();
                    var difficultyLevel = firstChart?.GetDifficultyLevel(instrument);
                    
                    if (difficultyLevel.HasValue)
                    {
                        Scores[i] = new SongScore
                        {
                            Instrument = instrument switch
                            {
                                "DRUMS" => DTXMania.Game.Lib.Song.Entities.EInstrumentPart.DRUMS,
                                "GUITAR" => DTXMania.Game.Lib.Song.Entities.EInstrumentPart.GUITAR,
                                "BASS" => DTXMania.Game.Lib.Song.Entities.EInstrumentPart.BASS,
                                _ => DTXMania.Game.Lib.Song.Entities.EInstrumentPart.DRUMS
                            },
                            DifficultyLevel = difficultyLevel.Value,
                            DifficultyLabel = DifficultyLabels[i]
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SongListNode: Error populating scores from database: {ex.Message}");
            }
        }

        #endregion

        #region Calculated Properties

        /// <summary>
        /// Gets whether this node has any scores
        /// </summary>
        public bool HasScores => Scores.Any(s => s != null);

        /// <summary>
        /// Gets the number of available difficulties
        /// </summary>
        public int AvailableDifficulties => Scores.Count(s => s != null);

        /// <summary>
        /// Gets whether this is a playable song
        /// </summary>
        public bool IsPlayable => Type == NodeType.Score && HasScores;

        /// <summary>
        /// Gets whether this is a folder
        /// </summary>
        public bool IsFolder => Type == NodeType.Box;

        /// <summary>
        /// Gets whether this is a back navigation item
        /// </summary>
        public bool IsBackNavigation => Type == NodeType.BackBox;

        /// <summary>
        /// Gets the display title with fallback
        /// </summary>
        public string DisplayTitle
        {
            get
            {
                if (!string.IsNullOrEmpty(Title))
                    return Title;

                if (Type == NodeType.BackBox)
                    return ".. (Back)";

                if (Type == NodeType.Random)
                    return "Random Select";

                if (DatabaseSong != null && !string.IsNullOrEmpty(DatabaseSong.Title))
                    return DatabaseSong.Title;

                // Fall back to filename from chart if available
                if (DatabaseChart != null && !string.IsNullOrEmpty(DatabaseChart.FilePath))
                    return System.IO.Path.GetFileNameWithoutExtension(DatabaseChart.FilePath);

                // If we have database entities, return "Unknown Song" (EF Core context)
                // Otherwise return "Unknown" (legacy direct creation)
                return (DatabaseSong != null || DatabaseChart != null) ? "Unknown Song" : "Unknown";
            }
        }

        /// <summary>
        /// Gets the highest difficulty level available
        /// </summary>
        public int MaxDifficultyLevel
        {
            get
            {
                var maxLevel = 0;
                foreach (var score in Scores)
                {
                    if (score != null && score.DifficultyLevel > maxLevel)
                        maxLevel = score.DifficultyLevel;
                }
                return maxLevel;
            }
        }

        #endregion

        #region Constructors

        /// <summary>
        /// Creates a new song list node
        /// </summary>
        public SongListNode()
        {
            // Initialize difficulty labels with defaults
            DifficultyLabels[0] = "BASIC";
            DifficultyLabels[1] = "ADVANCED";
            DifficultyLabels[2] = "EXTREME";
            DifficultyLabels[3] = "MASTER";
            DifficultyLabels[4] = "SPECIAL";
        }

        // Legacy CreateSongNode(SongMetadata) method removed - use CreateSongNode(Song, SongChart) instead

        /// <summary>
        /// Creates a song node from EF Core entities
        /// </summary>
        public static SongListNode CreateSongNode(SongEntity song, SongChart chart)
        {
            if (song == null)
                throw new ArgumentNullException(nameof(song));
            if (chart == null)
                throw new ArgumentNullException(nameof(chart));

            var node = new SongListNode
            {
                Type = NodeType.Score,
                Title = song.Title, // Use raw title, not DisplayTitle to allow fallback logic to work
                Genre = song.DisplayGenre,
                DatabaseSong = song,
                DatabaseChart = chart
            };

            // Set difficulty labels and scores based on chart data
            int scoreIndex = 0;
            
            if (chart.HasDrumChart && chart.DrumLevel > 0)
            {
                node.DifficultyLabels[scoreIndex] = $"DRUMS Lv.{chart.DrumLevel}";
                node.Scores[scoreIndex] = new SongScore
                {
                    Instrument = DTXMania.Game.Lib.Song.Entities.EInstrumentPart.DRUMS,
                    DifficultyLevel = chart.DrumLevel,
                    DifficultyLabel = "DRUMS"
                };
                scoreIndex++;
            }
            
            if (chart.HasGuitarChart && chart.GuitarLevel > 0)
            {
                node.DifficultyLabels[scoreIndex] = $"GUITAR Lv.{chart.GuitarLevel}";
                node.Scores[scoreIndex] = new SongScore
                {
                    Instrument = DTXMania.Game.Lib.Song.Entities.EInstrumentPart.GUITAR,
                    DifficultyLevel = chart.GuitarLevel,
                    DifficultyLabel = "GUITAR"
                };
                scoreIndex++;
            }
            
            if (chart.HasBassChart && chart.BassLevel > 0)
            {
                node.DifficultyLabels[scoreIndex] = $"BASS Lv.{chart.BassLevel}";
                node.Scores[scoreIndex] = new SongScore
                {
                    Instrument = DTXMania.Game.Lib.Song.Entities.EInstrumentPart.BASS,
                    DifficultyLevel = chart.BassLevel,
                    DifficultyLabel = "BASS"
                };
                scoreIndex++;
            }

            return node;
        }

        /// <summary>
        /// Creates a BOX folder node
        /// </summary>
        public static SongListNode CreateBoxNode(string title, string directoryPath, SongListNode? parent = null)
        {
            var node = new SongListNode
            {
                Type = NodeType.Box,
                Title = title,
                DirectoryPath = directoryPath,
                Parent = parent
            };

            // Update breadcrumb path
            if (parent != null)
            {
                node.BreadcrumbPath = string.IsNullOrEmpty(parent.BreadcrumbPath) 
                    ? title 
                    : $"{parent.BreadcrumbPath} > {title}";
            }
            else
            {
                node.BreadcrumbPath = title;
            }

            return node;
        }

        /// <summary>
        /// Creates a back navigation node
        /// </summary>
        public static SongListNode CreateBackNode(SongListNode parent)
        {
            return new SongListNode
            {
                Type = NodeType.BackBox,
                Title = ".. (Back)",
                Parent = parent,
                BreadcrumbPath = parent.BreadcrumbPath
            };
        }

        /// <summary>
        /// Creates a random selection node
        /// </summary>
        public static SongListNode CreateRandomNode()
        {
            return new SongListNode
            {
                Type = NodeType.Random,
                Title = "Random Select",
                TextColor = Color.Yellow
            };
        }

        #endregion

        #region Methods

        /// <summary>
        /// Adds a child node
        /// </summary>
        public void AddChild(SongListNode child)
        {
            child.Parent = this;
            Children.Add(child);
            
            // Update child's breadcrumb path
            child.BreadcrumbPath = string.IsNullOrEmpty(BreadcrumbPath) 
                ? child.Title 
                : $"{BreadcrumbPath} > {child.Title}";
        }

        /// <summary>
        /// Removes a child node
        /// </summary>
        public bool RemoveChild(SongListNode child)
        {
            if (Children.Remove(child))
            {
                child.Parent = null;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Gets score for specified difficulty index
        /// </summary>
        public SongScore? GetScore(int difficultyIndex)
        {
            if (difficultyIndex >= 0 && difficultyIndex < Scores.Length)
                return Scores[difficultyIndex];
            return null;
        }

        /// <summary>
        /// Sets score for specified difficulty index
        /// </summary>
        public void SetScore(int difficultyIndex, SongScore score)
        {
            if (difficultyIndex >= 0 && difficultyIndex < Scores.Length)
            {
                Scores[difficultyIndex] = score;
                // Score metadata no longer needed - using EF Core entities
            }
        }

        /// <summary>
        /// Gets the closest available difficulty to the specified anchor level
        /// </summary>
        public int GetClosestDifficultyIndex(int anchorIndex)
        {
            // Start from anchor and search upward
            for (int i = anchorIndex; i < Scores.Length; i++)
            {
                if (Scores[i] != null)
                    return i;
            }

            // Search downward from anchor
            for (int i = anchorIndex - 1; i >= 0; i--)
            {
                if (Scores[i] != null)
                    return i;
            }

            // Return first available difficulty
            for (int i = 0; i < Scores.Length; i++)
            {
                if (Scores[i] != null)
                    return i;
            }

            return 0; // Default to first slot
        }

        /// <summary>
        /// Sorts children by specified criteria
        /// </summary>
        public void SortChildren(SongSortCriteria criteria = SongSortCriteria.Title)
        {
            Children.Sort((a, b) => CompareByCriteria(a, b, criteria));
        }

        private static int CompareByCriteria(SongListNode a, SongListNode b, SongSortCriteria criteria)
        {
            // BOX nodes always come first, then songs
            if (a.Type != b.Type)
            {
                if (a.Type == NodeType.Box) return -1;
                if (b.Type == NodeType.Box) return 1;
            }

            return criteria switch
            {
                SongSortCriteria.Title => string.Compare(a.DisplayTitle, b.DisplayTitle, StringComparison.OrdinalIgnoreCase),
                SongSortCriteria.Artist => string.Compare(a.DatabaseSong?.DisplayArtist ?? "", b.DatabaseSong?.DisplayArtist ?? "", StringComparison.OrdinalIgnoreCase),
                SongSortCriteria.Genre => string.Compare(a.Genre, b.Genre, StringComparison.OrdinalIgnoreCase),
                SongSortCriteria.Level => b.MaxDifficultyLevel.CompareTo(a.MaxDifficultyLevel),
                _ => string.Compare(a.DisplayTitle, b.DisplayTitle, StringComparison.OrdinalIgnoreCase)
            };
        }

        #endregion
    }

    /// <summary>
    /// Sort criteria for song lists
    /// </summary>
    public enum SongSortCriteria
    {
        Title,
        Artist,
        Genre,
        Level
    }
}
