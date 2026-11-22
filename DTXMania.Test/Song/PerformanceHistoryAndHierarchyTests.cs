using System;
using System.Collections.Generic;
using DTXMania.Game.Lib.Song.Entities;
using Xunit;

namespace DTXMania.Test.Song
{
    /// <summary>
    /// Tests for PerformanceHistory and SongHierarchy entities
    /// </summary>
    [Trait("Category", "Unit")]
    public class PerformanceHistoryTests
    {
        [Fact]
        public void PerformanceHistory_DefaultValues_ShouldBeCorrect()
        {
            var history = new PerformanceHistory();

            Assert.Equal(0, history.Id);
            Assert.Equal(0, history.SongId);
            Assert.Equal("", history.HistoryLine);
            Assert.Equal(0, history.DisplayOrder);
            Assert.Equal(default(DateTime), history.PerformedAt);
            Assert.Null(history.Song);
        }

        [Fact]
        public void PerformanceHistory_SetProperties_ShouldRetainValues()
        {
            var performedAt = new DateTime(2024, 1, 15, 12, 0, 0);
            var history = new PerformanceHistory
            {
                Id = 1,
                SongId = 42,
                PerformedAt = performedAt,
                HistoryLine = "Score: 950000 - SS Rank",
                DisplayOrder = 3
            };

            Assert.Equal(1, history.Id);
            Assert.Equal(42, history.SongId);
            Assert.Equal(performedAt, history.PerformedAt);
            Assert.Equal("Score: 950000 - SS Rank", history.HistoryLine);
            Assert.Equal(3, history.DisplayOrder);
        }

        [Fact]
        public void PerformanceHistory_DisplayOrderRange_ShouldAcceptOneToFive()
        {
            for (int i = 1; i <= 5; i++)
            {
                var history = new PerformanceHistory { DisplayOrder = i };
                Assert.Equal(i, history.DisplayOrder);
            }
        }

        [Fact]
        public void PerformanceHistory_ScoreScopeProperties_ShouldRetainValues()
        {
            var score = new SongScore { Id = 42, ChartId = 7, Instrument = EInstrumentPart.DRUMS };
            var history = new PerformanceHistory
            {
                SongId = 9,
                SongScoreId = 42,
                SongScore = score,
                DisplayOrder = 3,
                HistoryLine = "3.26/6/13 Cleared (S: 91.23)"
            };

            Assert.Equal(42, history.SongScoreId);
            Assert.Same(score, history.SongScore);
            Assert.Equal("3.26/6/13 Cleared (S: 91.23)", history.HistoryLine);
        }

        [Fact]
        public void SongScore_PlayHistoryLines_ShouldDefaultToEmptyList()
        {
            var score = new SongScore();

            Assert.NotNull(score.PlayHistoryLines);
            Assert.Empty(score.PlayHistoryLines);
        }
    }

    /// <summary>
    /// Tests for SongHierarchy entity
    /// </summary>
    public class SongHierarchyTests
    {
        [Fact]
        public void SongHierarchy_DefaultValues_ShouldBeCorrect()
        {
            var hierarchy = new SongHierarchy();

            Assert.Equal(0, hierarchy.Id);
            Assert.Null(hierarchy.SongId);
            Assert.Null(hierarchy.ParentId);
            Assert.Equal("", hierarchy.Title);
            Assert.Equal("", hierarchy.BreadcrumbPath);
            Assert.True(hierarchy.IncludeInRandom);
            Assert.NotNull(hierarchy.Children);
            Assert.Empty(hierarchy.Children);
        }

        [Fact]
        public void SongHierarchy_SetProperties_ShouldRetainValues()
        {
            var hierarchy = new SongHierarchy
            {
                Id = 10,
                SongId = 5,
                ParentId = 3,
                NodeType = ENodeType.Box,
                Title = "My Box",
                Genre = "Rock",
                DisplayOrder = 2,
                SkinPath = "/skins/myskin",
                BreadcrumbPath = "Root/Rock/My Box",
                IncludeInRandom = false
            };

            Assert.Equal(10, hierarchy.Id);
            Assert.Equal(5, hierarchy.SongId);
            Assert.Equal(3, hierarchy.ParentId);
            Assert.Equal(ENodeType.Box, hierarchy.NodeType);
            Assert.Equal("My Box", hierarchy.Title);
            Assert.Equal("Rock", hierarchy.Genre);
            Assert.Equal(2, hierarchy.DisplayOrder);
            Assert.Equal("/skins/myskin", hierarchy.SkinPath);
            Assert.Equal("Root/Rock/My Box", hierarchy.BreadcrumbPath);
            Assert.False(hierarchy.IncludeInRandom);
        }

        [Fact]
        public void SongHierarchy_TextColorArgb_DefaultShouldBeWhite()
        {
            var hierarchy = new SongHierarchy();
            // Default white = 0xFFFFFFFF as signed int
            Assert.Equal(unchecked((int)0xFFFFFFFF), hierarchy.TextColorArgb);
        }

        [Fact]
        public void SongHierarchy_NodeTypes_ShouldAllBeValid()
        {
            var songNode = new SongHierarchy { NodeType = ENodeType.Song };
            var boxNode = new SongHierarchy { NodeType = ENodeType.Box };
            var backBoxNode = new SongHierarchy { NodeType = ENodeType.BackBox };
            var randomNode = new SongHierarchy { NodeType = ENodeType.Random };

            Assert.Equal(ENodeType.Song, songNode.NodeType);
            Assert.Equal(ENodeType.Box, boxNode.NodeType);
            Assert.Equal(ENodeType.BackBox, backBoxNode.NodeType);
            Assert.Equal(ENodeType.Random, randomNode.NodeType);
        }

        [Fact]
        public void SongHierarchy_Children_ShouldSupportAddingChildren()
        {
            var parent = new SongHierarchy { Id = 1, Title = "Parent" };
            var child1 = new SongHierarchy { Id = 2, Title = "Child 1", ParentId = 1, Parent = parent };
            var child2 = new SongHierarchy { Id = 3, Title = "Child 2", ParentId = 1, Parent = parent };

            parent.Children.Add(child1);
            parent.Children.Add(child2);

            Assert.Equal(2, parent.Children.Count);
        }
    }

}
