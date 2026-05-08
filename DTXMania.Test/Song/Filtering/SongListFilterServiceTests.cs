using System.Collections.Generic;
using System.Linq;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Filtering;
using Xunit;

namespace DTXMania.Test.Song.Filtering
{
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
        public void Apply_FlattensRootScoreNodes_NoFilter()
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
        public void Apply_FlattensNestedScoreNodes_NoFilter()
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
        public void Apply_PopulatesFolderPathFromParentBreadcrumb()
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
        public void Apply_RootScoreHasEmptyFolderPath()
        {
            var roots = new List<SongListNode> { Score("RootOnly") };

            var result = _svc.Apply(roots, SongFilterCriteria.Default);

            Assert.Equal("", result.Single().FolderPath);
        }

        [Fact]
        public void Apply_ExcludesBackBoxAndRandomNodes()
        {
            var box = Box("Folder", Score("InsideSong"));
            box.AddChild(SongListNode.CreateBackNode(box));
            box.AddChild(SongListNode.CreateRandomNode());

            var result = _svc.Apply(new[] { box }, SongFilterCriteria.Default);

            Assert.Equal(new[] { "InsideSong" }, result.Select(r => r.Node.DisplayTitle));
        }

        [Fact]
        public void Apply_SearchByTitle_CaseInsensitiveSubstring()
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
        public void Apply_SearchByArtist_CaseInsensitiveSubstring()
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
        public void Apply_SearchEmpty_ReturnsAll()
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
        public void Apply_SearchNoMatch_ReturnsEmpty()
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
        public void Apply_LevelRange_FiltersByMaxDifficulty()
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
        public void Apply_LevelMinOnly_NoMaxBound()
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
        public void Apply_LevelMaxOnly_NoMinBound()
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
        public void Apply_LevelMinGreaterThanMax_SwapsSilently()
        {
            var roots = new List<SongListNode>
            {
                Score("Mid", level: 50)
            };
            var criteria = SongFilterCriteria.Default with { MinLevel = 80, MaxLevel = 30 };

            var result = _svc.Apply(roots, criteria);

            Assert.Single(result);
        }

        private static SongListNode ScoreWith(string title, int playCount, int bestRank)
        {
            var node = new SongListNode { Type = NodeType.Score, Title = title };
            node.Scores[0] = new DTXMania.Game.Lib.Song.Entities.SongScore
            {
                DifficultyLevel = 50,
                PlayCount = playCount,
                BestRank = bestRank
            };
            return node;
        }

        [Fact]
        public void Apply_PlayedStatusUnplayed_KeepsOnlyZeroPlays()
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
        public void Apply_PlayedStatusPlayed_KeepsAnyPlayedSong()
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
        public void Apply_PlayedStatusCleared_RequiresPlayCountAndNonFRank()
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
        public void Apply_PlayedStatusAll_NoFilter()
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
        public void Apply_PlayedStatusUnplayed_TreatsMissingScoresAsUnplayed()
        {
            var node = new SongListNode { Type = NodeType.Score, Title = "NoScores" };
            // node.Scores is all-null
            var roots = new List<SongListNode> { node };
            var criteria = SongFilterCriteria.Default with { PlayedStatus = PlayedStatus.Unplayed };

            var result = _svc.Apply(roots, criteria);

            Assert.Single(result);
        }
    }
}
