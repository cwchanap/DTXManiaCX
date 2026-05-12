using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Filtering;
using DTXMania.Game.Lib.Stage;
using System.Collections.Generic;
using Xunit;

namespace DTXMania.Test.Stage
{
    [Trait("Category", "Unit")]
    public class SongSelectionStageInputTests
    {
        #region ClampSelectionIndex

        [Fact]
        public void ClampSelectionIndex_PreviousAtEndOfMultiItemList_ShouldReturnLastIndex()
        {
            var node = new SongListNode { Title = "C" };
            var list = new List<SongListNode>
            {
                new SongListNode { Title = "A" },
                new SongListNode { Title = "B" },
                node
            };
            Assert.Equal(2, SongSelectionStage.ClampSelectionIndex(node, list));
        }

        [Fact]
        public void ClampSelectionIndex_SingleItemListWithMatch_ShouldReturn0()
        {
            var node = new SongListNode { Title = "Only" };
            var list = new List<SongListNode> { node };
            Assert.Equal(0, SongSelectionStage.ClampSelectionIndex(node, list));
        }

        [Fact]
        public void ClampSelectionIndex_SingleItemListWithoutMatch_ShouldReturn0()
        {
            var prev = new SongListNode { Title = "X" };
            var list = new List<SongListNode> { new SongListNode { Title = "Y" } };
            Assert.Equal(0, SongSelectionStage.ClampSelectionIndex(prev, list));
        }

        [Fact]
        public void ClampSelectionIndex_PreviousFoundAtMiddlePosition_ShouldReturnCorrectIndex()
        {
            var node = new SongListNode { Title = "Mid" };
            var list = new List<SongListNode>
            {
                new SongListNode { Title = "A" },
                new SongListNode { Title = "B" },
                node,
                new SongListNode { Title = "D" },
                new SongListNode { Title = "E" }
            };
            Assert.Equal(2, SongSelectionStage.ClampSelectionIndex(node, list));
        }

        [Fact]
        public void ClampSelectionIndex_DuplicateReferences_ShouldReturnFirstOccurrence()
        {
            var node = new SongListNode { Title = "Dup" };
            var list = new List<SongListNode>
            {
                node,
                new SongListNode { Title = "Other" },
                node
            };
            Assert.Equal(0, SongSelectionStage.ClampSelectionIndex(node, list));
        }

        #endregion

        #region SummarizeFilter

        [Fact]
        public void SummarizeFilter_WithPlayedStatusUnplayed_ShouldContainUnplayed()
        {
            var c = SongFilterCriteria.Default with { PlayedStatus = PlayedStatus.Unplayed };
            var result = SongSelectionStage.SummarizeFilter(c);
            Assert.Contains("Unplayed", result);
        }

        [Fact]
        public void SummarizeFilter_WithPlayedStatusPlayed_ShouldContainPlayed()
        {
            var c = SongFilterCriteria.Default with { PlayedStatus = PlayedStatus.Played };
            var result = SongSelectionStage.SummarizeFilter(c);
            Assert.Contains("Played", result);
        }

        [Fact]
        public void SummarizeFilter_WithPlayedStatusOnly_ShouldStartWithFiltered()
        {
            var c = SongFilterCriteria.Default with { PlayedStatus = PlayedStatus.Cleared };
            var result = SongSelectionStage.SummarizeFilter(c);
            Assert.StartsWith("Filtered:", result);
            Assert.Contains("Cleared", result);
        }

        [Fact]
        public void SummarizeFilter_WithSortDescendingOnly_ShouldStartWithFiltered()
        {
            var c = SongFilterCriteria.Default with { SortDescending = true };
            var result = SongSelectionStage.SummarizeFilter(c);
            Assert.StartsWith("Filtered:", result);
            Assert.Contains("Titlev", result);
        }

        [Fact]
        public void SummarizeFilter_WithSortByArtistDescending_ShouldContainArtistv()
        {
            var c = SongFilterCriteria.Default with { SortBy = SongSortCriteria.Artist, SortDescending = true };
            var result = SongSelectionStage.SummarizeFilter(c);
            Assert.Contains("Artistv", result);
        }

        [Fact]
        public void SummarizeFilter_WithSortByGenreDescending_ShouldContainGenrev()
        {
            var c = SongFilterCriteria.Default with { SortBy = SongSortCriteria.Genre, SortDescending = true };
            var result = SongSelectionStage.SummarizeFilter(c);
            Assert.Contains("Genrev", result);
        }

        [Fact]
        public void SummarizeFilter_WithSortByLevelAscending_ShouldContainLevelUpArrow()
        {
            var c = SongFilterCriteria.Default with { SortBy = SongSortCriteria.Level, SortDescending = false };
            var result = SongSelectionStage.SummarizeFilter(c);
            Assert.Contains("Level^", result);
        }

        [Fact]
        public void SummarizeFilter_WithCombinedAllFields_ShouldContainAllParts()
        {
            var c = SongFilterCriteria.Default with
            {
                SearchQuery = "mega",
                MinLevel = 40,
                MaxLevel = 80,
                PlayedStatus = PlayedStatus.Unplayed,
                SortBy = SongSortCriteria.Artist,
                SortDescending = true
            };
            var result = SongSelectionStage.SummarizeFilter(c);
            Assert.StartsWith("Filtered:", result);
            Assert.Contains("\"mega\"", result);
            Assert.Contains("Lv 40-80", result);
            Assert.Contains("Unplayed", result);
            Assert.Contains("Artistv", result);
        }

        [Fact]
        public void SummarizeFilter_WithSearchAndSortOnly_ShouldContainBothParts()
        {
            var c = SongFilterCriteria.Default with
            {
                SearchQuery = "test",
                SortBy = SongSortCriteria.Genre
            };
            var result = SongSelectionStage.SummarizeFilter(c);
            Assert.Contains("\"test\"", result);
            Assert.Contains("Genre^", result);
            Assert.DoesNotContain("Lv", result);
        }

        [Fact]
        public void SummarizeFilter_WithLevelAndSortOnly_ShouldContainLevelAndSort()
        {
            var c = SongFilterCriteria.Default with
            {
                MaxLevel = 50,
                SortBy = SongSortCriteria.Level,
                SortDescending = true
            };
            var result = SongSelectionStage.SummarizeFilter(c);
            Assert.Contains("Lv <=50", result);
            Assert.Contains("Levelv", result);
            Assert.DoesNotContain("\"", result);
        }

        [Fact]
        public void SummarizeFilter_WithPartsJoinedByPipe_ShouldUsePipeSeparator()
        {
            var c = SongFilterCriteria.Default with
            {
                SearchQuery = "x",
                MinLevel = 10
            };
            var result = SongSelectionStage.SummarizeFilter(c);
            Assert.Contains(" | ", result);
        }

        [Fact]
        public void SummarizeFilter_WithSortByTitleAscending_ShouldOmitSortFromResult()
        {
            var c = SongFilterCriteria.Default with
            {
                SearchQuery = "y",
                SortBy = SongSortCriteria.Title,
                SortDescending = false
            };
            var result = SongSelectionStage.SummarizeFilter(c);
            Assert.DoesNotContain("Title", result);
        }

        #endregion
    }
}
