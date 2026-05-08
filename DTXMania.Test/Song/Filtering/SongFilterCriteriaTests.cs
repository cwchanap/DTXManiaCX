using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Filtering;
using Xunit;

namespace DTXMania.Test.Song.Filtering
{
    public class SongFilterCriteriaTests
    {
        [Fact]
        public void Default_HasExpectedValues()
        {
            var c = SongFilterCriteria.Default;

            Assert.Equal("", c.SearchQuery);
            Assert.Null(c.MinLevel);
            Assert.Null(c.MaxLevel);
            Assert.Equal(PlayedStatus.All, c.PlayedStatus);
            Assert.Equal(SongSortCriteria.Title, c.SortBy);
            Assert.False(c.SortDescending);
        }

        [Fact]
        public void IsEmpty_TrueForDefault()
        {
            Assert.True(SongFilterCriteria.Default.IsEmpty);
        }

        [Fact]
        public void IsEmpty_FalseWhenSearchQuerySet()
        {
            var c = SongFilterCriteria.Default with { SearchQuery = "abc" };
            Assert.False(c.IsEmpty);
        }

        [Fact]
        public void IsEmpty_FalseWhenLevelSet()
        {
            var c = SongFilterCriteria.Default with { MinLevel = 50 };
            Assert.False(c.IsEmpty);
        }

        [Fact]
        public void IsEmpty_FalseWhenPlayedStatusNotAll()
        {
            var c = SongFilterCriteria.Default with { PlayedStatus = PlayedStatus.Unplayed };
            Assert.False(c.IsEmpty);
        }

        [Fact]
        public void IsEmpty_FalseWhenSortDescending()
        {
            var c = SongFilterCriteria.Default with { SortDescending = true };
            Assert.False(c.IsEmpty);
        }

        [Fact]
        public void IsEmpty_FalseWhenSortByNotTitle()
        {
            var c = SongFilterCriteria.Default with { SortBy = SongSortCriteria.Artist };
            Assert.False(c.IsEmpty);
        }

        [Fact]
        public void Equality_RecordValueSemantics()
        {
            var a = SongFilterCriteria.Default with { SearchQuery = "x" };
            var b = SongFilterCriteria.Default with { SearchQuery = "x" };
            Assert.Equal(a, b);
        }
    }
}
