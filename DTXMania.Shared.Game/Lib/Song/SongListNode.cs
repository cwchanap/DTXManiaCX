using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

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

        /// <summary>
        /// Primary song metadata (for Score nodes)
        /// </summary>
        public SongMetadata? Metadata { get; set; }

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

                if (Metadata != null)
                    return Metadata.DisplayTitle;

                return "Unknown";
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

        /// <summary>
        /// Creates a song node from metadata
        /// </summary>
        public static SongListNode CreateSongNode(SongMetadata metadata)
        {
            var node = new SongListNode
            {
                Type = NodeType.Score,
                Title = metadata.DisplayTitle,
                Genre = metadata.DisplayGenre,
                Metadata = metadata
            };

            // Create scores for available instruments
            var instruments = metadata.AvailableInstruments;
            for (int i = 0; i < Math.Min(instruments.Count, 5); i++)
            {
                var instrument = instruments[i];
                var difficultyLevel = metadata.GetDifficultyLevel(instrument);
                
                if (difficultyLevel.HasValue)
                {
                    node.Scores[i] = new SongScore
                    {
                        Metadata = metadata,
                        Instrument = instrument,
                        DifficultyLevel = difficultyLevel.Value,
                        DifficultyLabel = node.DifficultyLabels[i]
                    };
                }
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
                score.Metadata = Metadata ?? score.Metadata;
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
                SongSortCriteria.Artist => string.Compare(a.Metadata?.DisplayArtist ?? "", b.Metadata?.DisplayArtist ?? "", StringComparison.OrdinalIgnoreCase),
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
