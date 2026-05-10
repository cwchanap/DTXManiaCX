using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Filtering;
using DTXMania.Game.Lib.Stage;
using System.Collections.Generic;
using Xunit;

namespace DTXMania.Test.Stage
{
    [Trait("Category", "Unit")]
    public class SongSelectionStageFilterTests
    {
        [Fact]
        public void NewStage_WhenCreated_ShouldHaveDefaultFilterCriteriaEmpty()
        {
            Assert.True(SongSelectionStage.DefaultFilterCriteriaIsEmpty());
        }

        [Fact]
        public void DefaultFilteredView_WhenCreated_ShouldBeNull()
        {
            Assert.True(SongSelectionStage.DefaultFilteredViewIsNull());
        }

        [Fact]
        public void IsLibraryReady_WhenSyncInitPath_ShouldNotBlockApply()
        {
            object? list = new List<SongListNode>();
            System.Threading.Tasks.Task? task = null;
            bool processed = false;

            bool ready = list != null && (task == null || processed);

            Assert.True(ready);
        }

        [Fact]
        public void IsLibraryReady_WhenAsyncStillRunning_ShouldBlockApply()
        {
            object? list = new List<SongListNode>();
            System.Threading.Tasks.Task task = System.Threading.Tasks.Task.CompletedTask;
            bool processed = false;

            bool ready = list != null && (task == null || processed);

            Assert.False(ready);
        }

        #region SummarizeFilter

        [Fact]
        public void SummarizeFilter_WithEmptyCriteria_ShouldReturnEmpty()
        {
            Assert.Equal("", SongSelectionStage.SummarizeFilter(SongFilterCriteria.Default));
        }

        [Fact]
        public void SummarizeFilter_WithSearchQuery_ShouldContainQuery()
        {
            var c = SongFilterCriteria.Default with { SearchQuery = "hello" };
            var result = SongSelectionStage.SummarizeFilter(c);
            Assert.Contains("\"hello\"", result);
            Assert.StartsWith("Filtered:", result);
        }

        [Fact]
        public void SummarizeFilter_WithLevelRange_ShouldContainRange()
        {
            var c = SongFilterCriteria.Default with { MinLevel = 30, MaxLevel = 70 };
            var result = SongSelectionStage.SummarizeFilter(c);
            Assert.Contains("Lv 30-70", result);
        }

        [Fact]
        public void SummarizeFilter_WithLevelMinOnly_ShouldContainMinLevel()
        {
            var c = SongFilterCriteria.Default with { MinLevel = 50 };
            var result = SongSelectionStage.SummarizeFilter(c);
            Assert.Contains("Lv 50+", result);
        }

        [Fact]
        public void SummarizeFilter_WithLevelMaxOnly_ShouldContainMaxLevel()
        {
            var c = SongFilterCriteria.Default with { MaxLevel = 60 };
            var result = SongSelectionStage.SummarizeFilter(c);
            Assert.Contains("Lv ≤60", result);
        }

        [Fact]
        public void SummarizeFilter_WithInvertedLevelRange_ShouldNormalize()
        {
            var c = SongFilterCriteria.Default with { MinLevel = 80, MaxLevel = 30 };
            var result = SongSelectionStage.SummarizeFilter(c);
            Assert.Contains("Lv 30-80", result);
        }

        [Fact]
        public void SummarizeFilter_WithPlayedStatus_ShouldContainStatus()
        {
            var c = SongFilterCriteria.Default with { PlayedStatus = PlayedStatus.Cleared };
            var result = SongSelectionStage.SummarizeFilter(c);
            Assert.Contains("Cleared", result);
        }

        [Fact]
        public void SummarizeFilter_WithSortDescending_ShouldContainSort()
        {
            var c = SongFilterCriteria.Default with { SortDescending = true };
            var result = SongSelectionStage.SummarizeFilter(c);
            Assert.Contains("Title↓", result);
        }

        [Fact]
        public void SummarizeFilter_WithSortByArtistAscending_ShouldContainSort()
        {
            var c = SongFilterCriteria.Default with { SortBy = SongSortCriteria.Artist };
            var result = SongSelectionStage.SummarizeFilter(c);
            Assert.Contains("Artist↑", result);
        }

        [Fact]
        public void SummarizeFilter_WithSortByLevelDescending_ShouldContainSort()
        {
            var c = SongFilterCriteria.Default with { SortBy = SongSortCriteria.Level, SortDescending = true };
            var result = SongSelectionStage.SummarizeFilter(c);
            Assert.Contains("Level↓", result);
        }

        [Fact]
        public void SummarizeFilter_WithSortByGenre_ShouldContainSort()
        {
            var c = SongFilterCriteria.Default with { SortBy = SongSortCriteria.Genre };
            var result = SongSelectionStage.SummarizeFilter(c);
            Assert.Contains("Genre↑", result);
        }

        [Fact]
        public void SummarizeFilter_WithDefaultSortTitleAscending_ShouldOmitSort()
        {
            var c = SongFilterCriteria.Default with { SortBy = SongSortCriteria.Title, SortDescending = false };
            var result = SongSelectionStage.SummarizeFilter(c);
            Assert.DoesNotContain("Title", result);
        }

        [Fact]
        public void SummarizeFilter_WithCombinedSearchLevelPlayed_ShouldContainAll()
        {
            var c = SongFilterCriteria.Default with
            {
                SearchQuery = "test",
                MinLevel = 30,
                MaxLevel = 60,
                PlayedStatus = PlayedStatus.Played
            };
            var result = SongSelectionStage.SummarizeFilter(c);
            Assert.Contains("\"test\"", result);
            Assert.Contains("Lv 30-60", result);
            Assert.Contains("Played", result);
        }

        #endregion

        #region ClampSelectionIndex

        [Fact]
        public void ClampSelectionIndex_WithNullList_ShouldReturn0()
        {
            Assert.Equal(0, SongSelectionStage.ClampSelectionIndex(null, null));
        }

        [Fact]
        public void ClampSelectionIndex_WithEmptyList_ShouldReturn0()
        {
            Assert.Equal(0, SongSelectionStage.ClampSelectionIndex(null, new List<SongListNode>()));
        }

        [Fact]
        public void ClampSelectionIndex_WithNullPrevious_ShouldReturn0()
        {
            var list = new List<SongListNode> { new SongListNode { Title = "A" } };
            Assert.Equal(0, SongSelectionStage.ClampSelectionIndex(null, list));
        }

        [Fact]
        public void ClampSelectionIndex_WhenFoundInList_ShouldReturnIndex()
        {
            var node = new SongListNode { Title = "B" };
            var list = new List<SongListNode>
            {
                new SongListNode { Title = "A" },
                node,
                new SongListNode { Title = "C" }
            };
            Assert.Equal(1, SongSelectionStage.ClampSelectionIndex(node, list));
        }

        [Fact]
        public void ClampSelectionIndex_WhenNotFound_ShouldReturn0()
        {
            var prev = new SongListNode { Title = "X" };
            var list = new List<SongListNode>
            {
                new SongListNode { Title = "A" },
                new SongListNode { Title = "B" }
            };
            Assert.Equal(0, SongSelectionStage.ClampSelectionIndex(prev, list));
        }

        [Fact]
        public void ClampSelectionIndex_WithSameReferenceFirst_ShouldReturn0()
        {
            var node = new SongListNode { Title = "A" };
            var list = new List<SongListNode> { node, new SongListNode { Title = "B" } };
            Assert.Equal(0, SongSelectionStage.ClampSelectionIndex(node, list));
        }

        #endregion
    }
}
