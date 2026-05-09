using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Filtering;
using DTXMania.Game.Lib.Stage;
using Xunit;

namespace DTXMania.Test.Stage
{
    public class SongSelectionStageBreadcrumbTests
    {
        [Fact]
        public void Summarize_DefaultCriteria_ReturnsEmpty()
        {
            var s = SongSelectionStage.SummarizeFilter(SongFilterCriteria.Default);
            Assert.Equal("", s);
        }

        [Fact]
        public void Summarize_OnlySearchQuery_ShowsQuoted()
        {
            var c = SongFilterCriteria.Default with { SearchQuery = "beatles" };
            Assert.Equal("Filtered: \"beatles\"", SongSelectionStage.SummarizeFilter(c));
        }

        [Fact]
        public void Summarize_LevelRange_ShowsLevels()
        {
            var c = SongFilterCriteria.Default with { MinLevel = 50, MaxLevel = 85 };
            Assert.Equal("Filtered: Lv 50-85", SongSelectionStage.SummarizeFilter(c));
        }

        [Fact]
        public void Summarize_LevelRangeInverted_SwapsToAscending()
        {
            var c = SongFilterCriteria.Default with { MinLevel = 80, MaxLevel = 30 };
            Assert.Equal("Filtered: Lv 30-80", SongSelectionStage.SummarizeFilter(c));
        }

        [Fact]
        public void Summarize_LevelMinOnly()
        {
            var c = SongFilterCriteria.Default with { MinLevel = 50, MaxLevel = null };
            Assert.Equal("Filtered: Lv 50+", SongSelectionStage.SummarizeFilter(c));
        }

        [Fact]
        public void Summarize_LevelMaxOnly()
        {
            var c = SongFilterCriteria.Default with { MinLevel = null, MaxLevel = 85 };
            Assert.Equal("Filtered: Lv ≤85", SongSelectionStage.SummarizeFilter(c));
        }

        [Fact]
        public void Summarize_PlayedStatus()
        {
            var c = SongFilterCriteria.Default with { PlayedStatus = PlayedStatus.Unplayed };
            Assert.Equal("Filtered: Unplayed", SongSelectionStage.SummarizeFilter(c));
        }

        [Fact]
        public void Summarize_SortNonDefault_AppendedWithArrow()
        {
            var c = SongFilterCriteria.Default with
            {
                SortBy = SongSortCriteria.Artist,
                SortDescending = false
            };
            Assert.Equal("Filtered: Artist↑", SongSelectionStage.SummarizeFilter(c));
        }

        [Fact]
        public void Summarize_SortDescending_DownArrow()
        {
            var c = SongFilterCriteria.Default with
            {
                SortBy = SongSortCriteria.Title,
                SortDescending = true
            };
            Assert.Equal("Filtered: Title↓", SongSelectionStage.SummarizeFilter(c));
        }

        [Fact]
        public void Summarize_AllFacets_JoinedWithDot()
        {
            var c = new SongFilterCriteria(
                SearchQuery: "beatles",
                MinLevel: 50, MaxLevel: 85,
                PlayedStatus: PlayedStatus.Unplayed,
                SortBy: SongSortCriteria.Title,
                SortDescending: false);
            Assert.Equal("Filtered: \"beatles\" · Lv 50-85 · Unplayed",
                SongSelectionStage.SummarizeFilter(c));
        }
    }
}
