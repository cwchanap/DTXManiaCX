using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Filtering;
using Xunit;

namespace DTXMania.Test.Song.Filtering
{
    public class SongFilterCriteriaTests
    {
        [Fact]
        public void Default_ShouldHaveExpectedValues()
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
        public void Default_IsEmpty_ShouldBeTrue()
        {
            Assert.True(SongFilterCriteria.Default.IsEmpty);
        }

        [Fact]
        public void WhenSearchQuerySet_IsEmpty_ShouldBeFalse()
        {
            var c = SongFilterCriteria.Default with { SearchQuery = "abc" };
            Assert.False(c.IsEmpty);
        }

        [Fact]
        public void WhenMinLevelSet_IsEmpty_ShouldBeFalse()
        {
            var c = SongFilterCriteria.Default with { MinLevel = 50 };
            Assert.False(c.IsEmpty);
        }

        [Fact]
        public void WhenPlayedStatusNotAll_IsEmpty_ShouldBeFalse()
        {
            var c = SongFilterCriteria.Default with { PlayedStatus = PlayedStatus.Unplayed };
            Assert.False(c.IsEmpty);
        }

        [Fact]
        public void WhenSortDescending_IsEmpty_ShouldBeFalse()
        {
            var c = SongFilterCriteria.Default with { SortDescending = true };
            Assert.False(c.IsEmpty);
        }

        [Fact]
        public void WhenSortByNotTitle_IsEmpty_ShouldBeFalse()
        {
            var c = SongFilterCriteria.Default with { SortBy = SongSortCriteria.Artist };
            Assert.False(c.IsEmpty);
        }

        [Fact]
        public void Equals_RecordValueSemantics_ShouldBeTrue()
        {
            var a = SongFilterCriteria.Default with { SearchQuery = "x" };
            var b = SongFilterCriteria.Default with { SearchQuery = "x" };
            Assert.Equal(a, b);
        }
    }
}
