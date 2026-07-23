#nullable enable

using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Song.Entities;

// Type aliases for EF Core entities
using SongScore = DTXMania.Game.Lib.Song.Entities.SongScore;
using SongEntity = DTXMania.Game.Lib.Song.Entities.Song;
using SongChart = DTXMania.Game.Lib.Song.Entities.SongChart;

namespace DTXMania.Game.Lib.Song
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
        private readonly object _scoreVariantLock = new();
        private IReadOnlyDictionary<ScoreVariantKey, SongScore> _scoreVariants =
            CreateReadOnlyVariantMap(new Dictionary<ScoreVariantKey, SongScore>());

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
        /// Atomically published score variants keyed by difficulty and exact play speed.
        /// The dictionary and its score values are detached snapshots and must be treated
        /// as immutable by readers.
        /// </summary>
        public IReadOnlyDictionary<ScoreVariantKey, SongScore> ScoreVariants =>
            Volatile.Read(ref _scoreVariants);

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

        /// <summary>
        /// Actual speed of the latest persisted play when this node was materialized
        /// for the Recent Plays list. Null for normal browse-list nodes.
        /// </summary>
        public int? RecentPlaySpeedPercent { get; internal set; }

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
                        SetScore(i, new SongScore
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
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SongListNode: Error populating scores from database: {ex.Message}");
            }
        }

        /// <summary>
        /// Copies play history (PlayCount, BestRank, BestScore, etc.) from persisted
        /// SongScore entities attached to each chart into the corresponding entries of
        /// the <see cref="Scores"/> array.
        /// Charts are matched to score slots by <see cref="SongScore.ChartId"/> when
        /// both sides carry a non-zero ChartId; otherwise falls back to matching by
        /// Instrument + DifficultyLevel.
        /// </summary>
        public void PopulatePlayHistoryFromCharts(SongChart[]? charts)
        {
            if (charts == null) return;

            for (int difficultyIndex = 0; difficultyIndex < Scores.Length; difficultyIndex++)
            {
                var score = Scores[difficultyIndex];
                if (score == null) continue;

                SongScore? persisted;

                // Prefer ChartId matching when both sides carry a non-zero ChartId.
                // This disambiguates multi-chart songs where two charts share the
                // same instrument and numeric difficulty level.
                if (score.ChartId != 0)
                {
                    persisted = charts
                        .SelectMany(c => c.Scores ?? Enumerable.Empty<SongScore>())
                        .FirstOrDefault(ps => ps.ChartId == score.ChartId
                                              && ps.Instrument == score.Instrument
                                              && ps.PlaySpeedPercent == 100);
                }
                else
                {
                    persisted = charts
                        .SelectMany(c => c.Scores ?? Enumerable.Empty<SongScore>())
                        .FirstOrDefault(ps => ps.Instrument == score.Instrument
                                              && ps.DifficultyLevel == score.DifficultyLevel
                                              && ps.PlaySpeedPercent == 100);
                }

                if (persisted == null) continue;

                score.PlayCount       = persisted.PlayCount;
                score.BestRank        = persisted.BestRank;
                score.BestScore       = persisted.BestScore;
                score.BestSkillPoint  = persisted.BestSkillPoint;
                score.BestAchievementRate = persisted.BestAchievementRate;
                score.FullCombo       = persisted.FullCombo;
                score.Excellent       = persisted.Excellent;
                score.ClearCount      = persisted.ClearCount;
                score.MaxCombo        = persisted.MaxCombo;
                score.HighSkill       = persisted.HighSkill;
                score.SongSkill       = persisted.SongSkill;
                score.LastPlayedAt    = persisted.LastPlayedAt;
                score.LastScore       = persisted.LastScore;
                score.LastSkillPoint  = persisted.LastSkillPoint;
                // The PerformanceHistory collection is NOT assumed to be purely scoped to
                // this SongScore. Legacy/song-wide rows (SongScoreId == null) and rows
                // belonging to other scores can appear in the collection (e.g. when the
                // graph is built outside EF Core's FK-scoped load path). The explicit
                // SongScoreId filter is what keeps the displayed top-5 correct.
                score.PlayHistoryLines = (persisted.PerformanceHistory ?? Enumerable.Empty<PerformanceHistory>())
                    .Where(h => h.SongScoreId == persisted.Id)
                    .OrderBy(h => h.DisplayOrder)
                    .Select(h => h.HistoryLine)
                    .Where(line => !string.IsNullOrWhiteSpace(line))
                    .Take(GameConstants.PlayHistory.MaxRecentPlays)
                    .ToList();

                SetScoreVariant(difficultyIndex, 100, score);
                // Preserve the direct factory/helper caller's legacy score object.
                // The published map retains its detached clone.
                Scores[difficultyIndex] = score;
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
                return Scores
                    .Where(score => score != null)
                    .Select(score => score!.DifficultyLevel)
                    .DefaultIfEmpty(0)
                    .Max();
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
        public static SongListNode CreateSongNode(
            SongEntity song,
            SongChart chart,
            bool hydratePersistedScores = true)
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
                node.SetScore(scoreIndex, new SongScore
                {
                    ChartId = chart.Id,
                    Instrument = DTXMania.Game.Lib.Song.Entities.EInstrumentPart.DRUMS,
                    DifficultyLevel = chart.DrumLevel,
                    DifficultyLabel = "DRUMS"
                });
                scoreIndex++;
            }

            if (chart.HasGuitarChart && chart.GuitarLevel > 0)
            {
                node.DifficultyLabels[scoreIndex] = $"GUITAR Lv.{chart.GuitarLevel}";
                node.SetScore(scoreIndex, new SongScore
                {
                    ChartId = chart.Id,
                    Instrument = DTXMania.Game.Lib.Song.Entities.EInstrumentPart.GUITAR,
                    DifficultyLevel = chart.GuitarLevel,
                    DifficultyLabel = "GUITAR"
                });
                scoreIndex++;
            }

            if (chart.HasBassChart && chart.BassLevel > 0)
            {
                node.DifficultyLabels[scoreIndex] = $"BASS Lv.{chart.BassLevel}";
                node.SetScore(scoreIndex, new SongScore
                {
                    ChartId = chart.Id,
                    Instrument = DTXMania.Game.Lib.Song.Entities.EInstrumentPart.BASS,
                    DifficultyLevel = chart.BassLevel,
                    DifficultyLabel = "BASS"
                });
            }

            if (hydratePersistedScores)
            {
                // Populate default-speed play history for direct factory callers.
                node.PopulatePlayHistoryFromCharts(new[] { chart });
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
            node.BreadcrumbPath = parent != null && !string.IsNullOrEmpty(parent.BreadcrumbPath)
                ? $"{parent.BreadcrumbPath} > {title}"
                : title;

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
        /// Gets the 1.00x compatibility score for the specified difficulty index.
        /// </summary>
        public SongScore? GetScore(int difficultyIndex)
        {
            return GetDefaultSpeedScore(difficultyIndex);
        }

        /// <summary>
        /// Gets the explicitly named 1.00x compatibility score.
        /// The fixed <see cref="Scores"/> array remains a fallback for legacy callers
        /// that still populate it directly.
        /// </summary>
        public SongScore? GetDefaultSpeedScore(int difficultyIndex)
        {
            var published = GetScore(difficultyIndex, 100);
            if (published != null)
                return published;

            if (IsValidDifficultyIndex(difficultyIndex))
                return Scores[difficultyIndex];

            return null;
        }

        /// <summary>
        /// Gets the score for an exact difficulty and play-speed variant.
        /// Non-default speeds never fall back to another variant.
        /// </summary>
        public SongScore? GetScore(int difficultyIndex, int playSpeedPercent)
        {
            if (!IsValidDifficultyIndex(difficultyIndex))
                return null;

            var snapshot = ScoreVariants;
            return snapshot.TryGetValue(
                new ScoreVariantKey(difficultyIndex, playSpeedPercent),
                out var score)
                ? score
                : null;
        }

        /// <summary>
        /// Sets the legacy 1.00x score for the specified difficulty index.
        /// </summary>
        public void SetScore(int difficultyIndex, SongScore score)
        {
            if (!IsValidDifficultyIndex(difficultyIndex))
                return;

            SetScoreVariant(difficultyIndex, 100, score);
            Scores[difficultyIndex] = score;
        }

        /// <summary>
        /// Publishes one exact speed variant using copy-on-write replacement.
        /// Passing null removes only the requested variant.
        /// </summary>
        public void SetScoreVariant(
            int difficultyIndex,
            int playSpeedPercent,
            SongScore? score)
        {
            if (!IsValidDifficultyIndex(difficultyIndex))
                return;
            ValidatePlaySpeedPercent(playSpeedPercent);

            var key = new ScoreVariantKey(difficultyIndex, playSpeedPercent);

            lock (_scoreVariantLock)
            {
                var next = CloneVariantMap(_scoreVariants);

                if (score == null)
                {
                    next.Remove(key);
                    if (playSpeedPercent == PlaySpeedRange.Default)
                        Scores[difficultyIndex] = null!;
                }
                else
                {
                    var publishedScore = score.Clone();
                    publishedScore.PlaySpeedPercent = playSpeedPercent;
                    next[key] = publishedScore;
                    if (playSpeedPercent == PlaySpeedRange.Default)
                        Scores[difficultyIndex] = publishedScore.Clone();
                }

                Volatile.Write(ref _scoreVariants, CreateReadOnlyVariantMap(next));
            }
        }

        /// <summary>
        /// Atomically replaces all published variants with detached score snapshots.
        /// Invalid difficulty keys are ignored; non-canonical speeds are rejected.
        /// </summary>
        public void PublishScoreVariants(
            IEnumerable<KeyValuePair<ScoreVariantKey, SongScore>> scoreVariants)
        {
            if (scoreVariants == null)
                throw new ArgumentNullException(nameof(scoreVariants));

            var next = new Dictionary<ScoreVariantKey, SongScore>();
            foreach (var pair in scoreVariants)
            {
                if (!IsValidDifficultyIndex(pair.Key.DifficultyIndex) ||
                    pair.Value == null)
                {
                    continue;
                }
                ValidatePlaySpeedPercent(pair.Key.PlaySpeedPercent);

                var publishedScore = pair.Value.Clone();
                publishedScore.PlaySpeedPercent = pair.Key.PlaySpeedPercent;
                next[pair.Key] = publishedScore;
            }

            lock (_scoreVariantLock)
            {
                foreach (var pair in next)
                {
                    if (pair.Key.PlaySpeedPercent == PlaySpeedRange.Default)
                        Scores[pair.Key.DifficultyIndex] = pair.Value.Clone();
                }

                Volatile.Write(ref _scoreVariants, CreateReadOnlyVariantMap(next));
            }
        }

        private bool IsValidDifficultyIndex(int difficultyIndex) =>
            difficultyIndex >= 0 && difficultyIndex < Scores.Length;

        private static void ValidatePlaySpeedPercent(int playSpeedPercent)
        {
            if (playSpeedPercent < PlaySpeedRange.Min ||
                playSpeedPercent > PlaySpeedRange.Max ||
                playSpeedPercent % PlaySpeedRange.Step != 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(playSpeedPercent),
                    playSpeedPercent,
                    "Play speed must be between 50 and 150 in steps of 5.");
            }
        }

        private static Dictionary<ScoreVariantKey, SongScore> CloneVariantMap(
            IReadOnlyDictionary<ScoreVariantKey, SongScore> source)
        {
            return source.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.Clone());
        }

        private static IReadOnlyDictionary<ScoreVariantKey, SongScore>
            CreateReadOnlyVariantMap(
                Dictionary<ScoreVariantKey, SongScore> variants)
        {
            return new ReadOnlyDictionary<ScoreVariantKey, SongScore>(variants);
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
