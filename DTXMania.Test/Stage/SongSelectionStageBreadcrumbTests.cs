using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Filtering;
using DTXMania.Game.Lib.Stage;
using Xunit;

namespace DTXMania.Test.Stage
{
    [Trait("Category", "Unit")]
    public class SongSelectionStageBreadcrumbTests
    {
        [Fact]
        public void DefaultCriteria_ShouldReturnEmpty()
        {
            var s = SongSelectionStage.SummarizeFilter(SongFilterCriteria.Default);
            Assert.Equal("", s);
        }

        [Fact]
        public void OnlySearchQuery_ShouldShowQuoted()
        {
            var c = SongFilterCriteria.Default with { SearchQuery = "beatles" };
            Assert.Equal("Filtered: \"beatles\"", SongSelectionStage.SummarizeFilter(c));
        }

        [Fact]
        public void LevelRange_ShouldShowLevels()
        {
            var c = SongFilterCriteria.Default with { MinLevel = 50, MaxLevel = 85 };
            Assert.Equal("Filtered: Lv 50-85", SongSelectionStage.SummarizeFilter(c));
        }

        [Fact]
        public void LevelRangeInverted_ShouldSwapToAscending()
        {
            var c = SongFilterCriteria.Default with { MinLevel = 80, MaxLevel = 30 };
            Assert.Equal("Filtered: Lv 30-80", SongSelectionStage.SummarizeFilter(c));
        }

        [Fact]
        public void LevelMinOnly_ShouldShowMinLevel()
        {
            var c = SongFilterCriteria.Default with { MinLevel = 50, MaxLevel = null };
            Assert.Equal("Filtered: Lv 50+", SongSelectionStage.SummarizeFilter(c));
        }

        [Fact]
        public void LevelMaxOnly_ShouldShowMaxLevel()
        {
            var c = SongFilterCriteria.Default with { MinLevel = null, MaxLevel = 85 };
            Assert.Equal("Filtered: Lv <=85", SongSelectionStage.SummarizeFilter(c));
        }

        [Fact]
        public void PlayedStatus_ShouldShowStatus()
        {
            var c = SongFilterCriteria.Default with { PlayedStatus = PlayedStatus.Unplayed };
            Assert.Equal("Filtered: Unplayed", SongSelectionStage.SummarizeFilter(c));
        }

        [Fact]
        public void SortNonDefault_ShouldBeAppendedWithArrow()
        {
            var c = SongFilterCriteria.Default with
            {
                SortBy = SongSortCriteria.Artist,
                SortDescending = false
            };
            Assert.Equal("Filtered: Artist^", SongSelectionStage.SummarizeFilter(c));
        }

        [Fact]
        public void SortDescending_ShouldShowDownArrow()
        {
            var c = SongFilterCriteria.Default with
            {
                SortBy = SongSortCriteria.Title,
                SortDescending = true
            };
            Assert.Equal("Filtered: Titlev", SongSelectionStage.SummarizeFilter(c));
        }

        [Fact]
        public void AllFacets_ShouldBeJoinedWithPipe()
        {
            var c = new SongFilterCriteria(
                SearchQuery: "beatles",
                MinLevel: 50, MaxLevel: 85,
                PlayedStatus: PlayedStatus.Unplayed,
                SortBy: SongSortCriteria.Title,
                SortDescending: false);
            Assert.Equal("Filtered: \"beatles\" | Lv 50-85 | Unplayed",
                SongSelectionStage.SummarizeFilter(c));
        }
    }
}
