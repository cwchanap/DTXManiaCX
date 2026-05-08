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
    }
}
