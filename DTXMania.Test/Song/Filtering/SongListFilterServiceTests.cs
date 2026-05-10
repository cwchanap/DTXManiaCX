using System.Collections.Generic;
using System.Linq;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Filtering;
using Xunit;

namespace DTXMania.Test.Song.Filtering
{
    [Trait("Category", "Unit")]
    public class SongListFilterServiceTests
    {
        private readonly SongListFilterService _svc = new();

        private static SongListNode Score(string title, string artist = "", int level = 50)
        {
            var node = new SongListNode { Type = NodeType.Score, Title = title };
            node.Scores[0] = new DTXMania.Game.Lib.Song.Entities.SongScore
            {
                DifficultyLevel = level,
                DifficultyLabel = "BASIC"
            };
            // Artist lives on DatabaseSong — wire a minimal entity
            if (!string.IsNullOrEmpty(artist))
            {
                node.DatabaseSong = new DTXMania.Game.Lib.Song.Entities.Song
                {
                    Title = title,
                    Artist = artist
                };
            }
            return node;
        }

        private static SongListNode Box(string title, params SongListNode[] children)
        {
            var box = SongListNode.CreateBoxNode(title, $"/path/{title}");
            foreach (var c in children)
                box.AddChild(c);
            return box;
        }

        [Fact]
        public void FlatteningRootScoreNodes_WithNoFilter_ShouldReturnAllRootScores()
        {
            var roots = new List<SongListNode>
            {
                Score("Song A"),
                Score("Song B")
            };

            var result = _svc.Apply(roots, SongFilterCriteria.Default);

            Assert.Equal(2, result.Count);
            Assert.Equal(new[] { "Song A", "Song B" }, result.Select(r => r.Node.DisplayTitle));
        }

        [Fact]
        public void FlatteningNestedScoreNodes_WithNoFilter_ShouldReturnAllScores()
        {
            var roots = new List<SongListNode>
            {
                Box("J-POP",
                    Score("Pop1"),
                    Box("80s", Score("Pop80a"), Score("Pop80b"))),
                Score("RootSong")
            };

            var result = _svc.Apply(roots, SongFilterCriteria.Default);

            // Sorted by Title (default): Pop1, Pop80a, Pop80b, RootSong
            Assert.Equal(4, result.Count);
            Assert.Equal(new[] { "Pop1", "Pop80a", "Pop80b", "RootSong" },
                result.Select(r => r.Node.DisplayTitle));
        }

        [Fact]
        public void FolderPath_WhenNestedInBoxes_ShouldPopulateFromParentBreadcrumb()
        {
            var roots = new List<SongListNode>
            {
                Box("J-POP",
                    Box("80s", Score("Hit")))
            };

            var result = _svc.Apply(roots, SongFilterCriteria.Default);

            var hit = result.Single();
            Assert.Equal("J-POP / 80s", hit.FolderPath);
        }

        [Fact]
        public void FolderPath_WhenRootScore_ShouldBeEmpty()
        {
            var roots = new List<SongListNode> { Score("RootOnly") };

            var result = _svc.Apply(roots, SongFilterCriteria.Default);

            Assert.Equal("", result.Single().FolderPath);
        }

        [Fact]
        public void Apply_WithBackAndRandomNodes_ShouldExcludeThem()
        {
            var box = Box("Folder", Score("InsideSong"));
            box.AddChild(SongListNode.CreateBackNode(box));
            box.AddChild(SongListNode.CreateRandomNode());

            var result = _svc.Apply(new[] { box }, SongFilterCriteria.Default);

            Assert.Equal(new[] { "InsideSong" }, result.Select(r => r.Node.DisplayTitle));
        }

        [Fact]
        public void SearchByTitle_WithCaseInsensitiveSubstring_ShouldMatch()
        {
            var roots = new List<SongListNode>
            {
                Score("Yesterday", "The Beatles"),
                Score("Hey Jude", "The Beatles"),
                Score("Smoke On The Water", "Deep Purple")
            };
            var criteria = SongFilterCriteria.Default with { SearchQuery = "yesterDAY" };

            var result = _svc.Apply(roots, criteria);

            Assert.Equal(new[] { "Yesterday" }, result.Select(r => r.Node.DisplayTitle));
        }

        [Fact]
        public void SearchByArtist_WithCaseInsensitiveSubstring_ShouldMatch()
        {
            var roots = new List<SongListNode>
            {
                Score("Yesterday", "The Beatles"),
                Score("Smoke On The Water", "Deep Purple")
            };
            var criteria = SongFilterCriteria.Default with { SearchQuery = "beatles" };

            var result = _svc.Apply(roots, criteria);

            Assert.Equal(new[] { "Yesterday" }, result.Select(r => r.Node.DisplayTitle));
        }

        [Fact]
        public void Search_WhenEmpty_ShouldReturnAll()
        {
            var roots = new List<SongListNode>
            {
                Score("A"), Score("B")
            };
            var criteria = SongFilterCriteria.Default with { SearchQuery = "" };

            var result = _svc.Apply(roots, criteria);

            Assert.Equal(2, result.Count);
        }

        [Fact]
        public void Search_WhenNoMatch_ShouldReturnEmpty()
        {
            var roots = new List<SongListNode>
            {
                Score("A", "Artist1"),
                Score("B", "Artist2")
            };
            var criteria = SongFilterCriteria.Default with { SearchQuery = "zzz" };

            var result = _svc.Apply(roots, criteria);

            Assert.Empty(result);
        }

        [Fact]
        public void LevelRange_WithBothBounds_ShouldFilterByMaxDifficulty()
        {
            var roots = new List<SongListNode>
            {
                Score("Easy", level: 30),
                Score("Mid",  level: 60),
                Score("Hard", level: 90)
            };
            var criteria = SongFilterCriteria.Default with { MinLevel = 50, MaxLevel = 80 };

            var result = _svc.Apply(roots, criteria);

            Assert.Equal(new[] { "Mid" }, result.Select(r => r.Node.DisplayTitle));
        }

        [Fact]
        public void LevelMinOnly_WithNoMaxBound_ShouldReturnAboveMin()
        {
            var roots = new List<SongListNode>
            {
                Score("Easy", level: 30),
                Score("Hard", level: 90)
            };
            var criteria = SongFilterCriteria.Default with { MinLevel = 50, MaxLevel = null };

            var result = _svc.Apply(roots, criteria);

            Assert.Equal(new[] { "Hard" }, result.Select(r => r.Node.DisplayTitle));
        }

        [Fact]
        public void LevelMaxOnly_WithNoMinBound_ShouldReturnBelowMax()
        {
            var roots = new List<SongListNode>
            {
                Score("Easy", level: 30),
                Score("Hard", level: 90)
            };
            var criteria = SongFilterCriteria.Default with { MinLevel = null, MaxLevel = 50 };

            var result = _svc.Apply(roots, criteria);

            Assert.Equal(new[] { "Easy" }, result.Select(r => r.Node.DisplayTitle));
        }

        [Fact]
        public void LevelRange_WhenMinGreaterThanMax_ShouldReturnEmpty()
        {
            var roots = new List<SongListNode>
            {
                Score("Low",  level: 20),
                Score("Mid",  level: 50),
                Score("High", level: 90)
            };
            // Min > Max is now the caller's responsibility to normalize
            // before passing to the service. The service treats the range as-is.
            var criteria = SongFilterCriteria.Default with { MinLevel = 80, MaxLevel = 30 };

            var result = _svc.Apply(roots, criteria);

            // With lo=80, hi=30, no level satisfies level >= 80 AND level <= 30
            Assert.Empty(result);
        }

        [Fact]
        public void LevelRange_WhenNormalizedByCaller_ShouldFilterCorrectly()
        {
            var roots = new List<SongListNode>
            {
                Score("Low",  level: 20),
                Score("Mid",  level: 50),
                Score("High", level: 90)
            };
            // Caller normalizes MinLevel/MaxLevel before creating criteria
            var criteria = SongFilterCriteria.Default with { MinLevel = 30, MaxLevel = 80 };

            var result = _svc.Apply(roots, criteria);

            Assert.Equal(new[] { "Mid" }, result.Select(r => r.Node.DisplayTitle));
        }

        private static SongListNode ScoreWith(string title, int playCount, int bestRank, int clearCount = 0)
        {
            var node = new SongListNode { Type = NodeType.Score, Title = title };
            node.Scores[0] = new DTXMania.Game.Lib.Song.Entities.SongScore
            {
                DifficultyLevel = 50,
                PlayCount = playCount,
                BestRank = bestRank,
                ClearCount = clearCount
            };
            return node;
        }

        [Fact]
        public void PlayedStatusUnplayed_WhenFiltering_ShouldKeepOnlyZeroPlays()
        {
            var roots = new List<SongListNode>
            {
                ScoreWith("Untouched", playCount: 0, bestRank: 0),
                ScoreWith("Tried",     playCount: 3, bestRank: 80)
            };
            var criteria = SongFilterCriteria.Default with { PlayedStatus = PlayedStatus.Unplayed };

            var result = _svc.Apply(roots, criteria);

            Assert.Equal(new[] { "Untouched" }, result.Select(r => r.Node.DisplayTitle));
        }

        [Fact]
        public void PlayedStatusPlayed_WhenFiltering_ShouldKeepAnyPlayedSong()
        {
            var roots = new List<SongListNode>
            {
                ScoreWith("Untouched", playCount: 0, bestRank: 0),
                ScoreWith("Tried",     playCount: 3, bestRank: 0)  // played but failed
            };
            var criteria = SongFilterCriteria.Default with { PlayedStatus = PlayedStatus.Played };

            var result = _svc.Apply(roots, criteria);

            Assert.Equal(new[] { "Tried" }, result.Select(r => r.Node.DisplayTitle));
        }

        [Fact]
        public void PlayedStatusCleared_WhenFiltering_ShouldRequirePlayCountAndNonFRank()
        {
            var roots = new List<SongListNode>
            {
                ScoreWith("Untouched", playCount: 0, bestRank: 0),
                ScoreWith("Failed",    playCount: 5, bestRank: 0),    // F bucket
                ScoreWith("Cleared",   playCount: 1, bestRank: 80)    // A bucket
            };
            var criteria = SongFilterCriteria.Default with { PlayedStatus = PlayedStatus.Cleared };

            var result = _svc.Apply(roots, criteria);

            Assert.Equal(new[] { "Cleared" }, result.Select(r => r.Node.DisplayTitle));
        }

        [Fact]
        public void PlayedStatusCleared_WithClearCountAndFRank_ShouldCountAsCleared()
        {
            // Songs persisted via SongDatabaseService.UpdateScoreAsync have ClearCount > 0
            // but BestRank stays at 0 (F). These should still be considered cleared.
            var roots = new List<SongListNode>
            {
                ScoreWith("PersistedClear", playCount: 3, bestRank: 0, clearCount: 2),
                ScoreWith("PlayedButFailed", playCount: 2, bestRank: 0, clearCount: 0),
                ScoreWith("Untouched",       playCount: 0, bestRank: 0, clearCount: 0)
            };
            var criteria = SongFilterCriteria.Default with { PlayedStatus = PlayedStatus.Cleared };

            var result = _svc.Apply(roots, criteria);

            Assert.Equal(new[] { "PersistedClear" }, result.Select(r => r.Node.DisplayTitle));
        }

        [Fact]
        public void PlayedStatusCleared_WithRankBasedClearAndNoClearCount_ShouldStillWork()
        {
            // In-memory update path sets BestRank but not ClearCount
            var roots = new List<SongListNode>
            {
                ScoreWith("RankClear",    playCount: 1, bestRank: 70, clearCount: 0),
                ScoreWith("RankClearA",   playCount: 1, bestRank: 80, clearCount: 0),
                ScoreWith("Failed",       playCount: 5, bestRank: 0,  clearCount: 0)
            };
            var criteria = SongFilterCriteria.Default with { PlayedStatus = PlayedStatus.Cleared };

            var result = _svc.Apply(roots, criteria);

            Assert.Equal(new[] { "RankClear", "RankClearA" }, result.Select(r => r.Node.DisplayTitle));
        }

        [Fact]
        public void PlayedStatusCleared_WithLegacyOrdinalBestRank_ShouldNormalize()
        {
            // Legacy ordinal 2 = A rank. Without normalization this maps to F via ComputeRankIndex.
            var roots = new List<SongListNode>
            {
                ScoreWith("LegacyA", playCount: 1, bestRank: 2),   // legacy ordinal A
                ScoreWith("ModernA", playCount: 1, bestRank: 80),  // canonical A bucket
                ScoreWith("Failed",  playCount: 3, bestRank: 0)    // F bucket
            };
            var criteria = SongFilterCriteria.Default with { PlayedStatus = PlayedStatus.Cleared };

            var result = _svc.Apply(roots, criteria);

            Assert.Equal(new[] { "LegacyA", "ModernA" }, result.Select(r => r.Node.DisplayTitle));
        }

        [Fact]
        public void PlayedStatusAll_WhenFiltering_ShouldReturnAll()
        {
            var roots = new List<SongListNode>
            {
                ScoreWith("A", playCount: 0, bestRank: 0),
                ScoreWith("B", playCount: 1, bestRank: 80)
            };
            var criteria = SongFilterCriteria.Default with { PlayedStatus = PlayedStatus.All };

            var result = _svc.Apply(roots, criteria);

            Assert.Equal(2, result.Count);
        }

        [Fact]
        public void PlayedStatusUnplayed_WithMissingScores_ShouldTreatAsUnplayed()
        {
            var node = new SongListNode { Type = NodeType.Score, Title = "NoScores" };
            // node.Scores is all-null
            var roots = new List<SongListNode> { node };
            var criteria = SongFilterCriteria.Default with { PlayedStatus = PlayedStatus.Unplayed };

            var result = _svc.Apply(roots, criteria);

            Assert.Single(result);
        }

        [Fact]
        public void SortByTitleDescending_WhenApplied_ShouldSortDescending()
        {
            var roots = new List<SongListNode>
            {
                Score("Apple"), Score("Banana"), Score("Cherry")
            };
            var criteria = SongFilterCriteria.Default with
            {
                SortBy = SongSortCriteria.Title,
                SortDescending = true
            };

            var result = _svc.Apply(roots, criteria);

            Assert.Equal(new[] { "Cherry", "Banana", "Apple" },
                result.Select(r => r.Node.DisplayTitle));
        }

        [Fact]
        public void SortByLevelAscending_WhenApplied_ShouldSortByLevel()
        {
            var roots = new List<SongListNode>
            {
                Score("High", level: 90),
                Score("Low",  level: 20),
                Score("Mid",  level: 50)
            };
            var criteria = SongFilterCriteria.Default with
            {
                SortBy = SongSortCriteria.Level,
                SortDescending = false
            };

            var result = _svc.Apply(roots, criteria);

            Assert.Equal(new[] { "Low", "Mid", "High" },
                result.Select(r => r.Node.DisplayTitle));
        }

        [Fact]
        public void SortByArtist_WhenApplied_ShouldSortByArtist()
        {
            var roots = new List<SongListNode>
            {
                Score("X", "Zenith"),
                Score("Y", "Apex")
            };
            var criteria = SongFilterCriteria.Default with { SortBy = SongSortCriteria.Artist };

            var result = _svc.Apply(roots, criteria);

            Assert.Equal(new[] { "Y", "X" }, result.Select(r => r.Node.DisplayTitle));
        }

        [Fact]
        public void CombinedSearchLevelPlayedSort_WhenApplied_ShouldFilterCorrectly()
        {
            var roots = new List<SongListNode>
            {
                ScoreWith("Beatles - Yesterday", playCount: 1, bestRank: 80),
                ScoreWith("Beatles - Hard Rock", playCount: 0, bestRank: 0),
                ScoreWith("Other Artist Song",   playCount: 1, bestRank: 80)
            };
            // Wire artist on first and second
            roots[0].DatabaseSong = new DTXMania.Game.Lib.Song.Entities.Song
            { Title = "Beatles - Yesterday", Artist = "The Beatles" };
            roots[1].DatabaseSong = new DTXMania.Game.Lib.Song.Entities.Song
            { Title = "Beatles - Hard Rock", Artist = "The Beatles" };

            var criteria = SongFilterCriteria.Default with
            {
                SearchQuery = "beatles",
                PlayedStatus = PlayedStatus.Played,
                SortBy = SongSortCriteria.Title,
                SortDescending = true
            };

            var result = _svc.Apply(roots, criteria);

            Assert.Equal(new[] { "Beatles - Yesterday" },
                result.Select(r => r.Node.DisplayTitle));
        }

        [Fact]
        public void Apply_WhenNullRoots_ShouldThrow()
        {
            Assert.Throws<ArgumentNullException>(() => _svc.Apply(null!, SongFilterCriteria.Default));
        }

        [Fact]
        public void Apply_WhenNullCriteria_ShouldThrow()
        {
            var roots = new List<SongListNode> { Score("A") };
            Assert.Throws<ArgumentNullException>(() => _svc.Apply(roots, null!));
        }

        [Fact]
        public void Apply_WhenEmptyRoots_ShouldReturnEmpty()
        {
            var result = _svc.Apply(new List<SongListNode>(), SongFilterCriteria.Default);
            Assert.Empty(result);
        }

        [Fact]
        public void SortByGenre_WhenApplied_ShouldSortByGenre()
        {
            var a = Score("SongA");
            a.Genre = "Rock";
            var b = Score("SongB");
            b.Genre = "Pop";
            var c = Score("SongC");
            c.Genre = "Rock";
            var roots = new List<SongListNode> { a, b, c };
            var criteria = SongFilterCriteria.Default with { SortBy = SongSortCriteria.Genre };

            var result = _svc.Apply(roots, criteria);

            Assert.Equal(new[] { "SongB", "SongA", "SongC" },
                result.Select(r => r.Node.DisplayTitle));
        }

        [Fact]
        public void SortByGenreDescending_WhenApplied_ShouldSortDescending()
        {
            var a = Score("SongA");
            a.Genre = "Rock";
            var b = Score("SongB");
            b.Genre = "Pop";
            var roots = new List<SongListNode> { a, b };
            var criteria = SongFilterCriteria.Default with
            {
                SortBy = SongSortCriteria.Genre,
                SortDescending = true
            };

            var result = _svc.Apply(roots, criteria);

            Assert.Equal(new[] { "SongA", "SongB" },
                result.Select(r => r.Node.DisplayTitle));
        }

        [Fact]
        public void Search_WithNullArtist_ShouldNotThrow()
        {
            var node = Score("TestSong");
            node.DatabaseSong = new DTXMania.Game.Lib.Song.Entities.Song
            { Title = "TestSong", Artist = null };
            var roots = new List<SongListNode> { node };
            var criteria = SongFilterCriteria.Default with { SearchQuery = "test" };

            var result = _svc.Apply(roots, criteria);

            Assert.Single(result);
        }

        [Fact]
        public void SortByLevelDescending_WhenApplied_ShouldSortDescending()
        {
            var roots = new List<SongListNode>
            {
                Score("Low", level: 20),
                Score("High", level: 90),
                Score("Mid", level: 50)
            };
            var criteria = SongFilterCriteria.Default with
            {
                SortBy = SongSortCriteria.Level,
                SortDescending = true
            };

            var result = _svc.Apply(roots, criteria);

            Assert.Equal(new[] { "High", "Mid", "Low" },
                result.Select(r => r.Node.DisplayTitle));
        }

        [Fact]
        public void Apply_WithNullNodeInRoots_ShouldSkipNull()
        {
            var roots = new List<SongListNode> { null!, Score("Valid") };
            var result = _svc.Apply(roots, SongFilterCriteria.Default);

            Assert.Single(result);
            Assert.Equal("Valid", result[0].Node.DisplayTitle);
        }

        [Fact]
        public void Apply_WithBoxWithNoChildren_ShouldIgnoreBox()
        {
            var box = SongListNode.CreateBoxNode("Empty", "/empty");
            var roots = new List<SongListNode> { box };

            var result = _svc.Apply(roots, SongFilterCriteria.Default);

            Assert.Empty(result);
        }

        [Fact]
        public void Apply_WithBackBoxNode_ShouldBeIgnored()
        {
            var box = Box("Folder", Score("Inside"));
            var back = SongListNode.CreateBackNode(box);
            var roots = new List<SongListNode> { box, back };

            var result = _svc.Apply(roots, SongFilterCriteria.Default);

            Assert.Equal(new[] { "Inside" }, result.Select(r => r.Node.DisplayTitle));
        }

        [Fact]
        public void LevelRange_WithBothNull_ShouldReturnAll()
        {
            var roots = new List<SongListNode>
            {
                Score("A", level: 20),
                Score("B", level: 90)
            };
            var criteria = SongFilterCriteria.Default with { MinLevel = null, MaxLevel = null };

            var result = _svc.Apply(roots, criteria);

            Assert.Equal(2, result.Count);
        }

        [Fact]
        public void BoxNodeAtPathRoot_WhenNested_ShouldPopulateFolderCorrectly()
        {
            var innerScore = Score("Inner");
            var childBox = SongListNode.CreateBoxNode("SubFolder", "/sub");
            childBox.AddChild(innerScore);
            var rootBox = SongListNode.CreateBoxNode("RootFolder", "/root");
            rootBox.AddChild(childBox);

            var result = _svc.Apply(new[] { rootBox }, SongFilterCriteria.Default);

            Assert.Single(result);
            Assert.Equal("RootFolder / SubFolder", result[0].FolderPath);
        }
    }
}
