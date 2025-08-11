using System.Linq;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Entities;
using Microsoft.Xna.Framework;
using Xunit;
using SongScore = DTXMania.Game.Lib.Song.Entities.SongScore;

namespace DTXMania.Test.Song
{
    /// <summary>
    /// Unit tests for SongListNode class
    /// Tests hierarchical song organization and node operations
    /// </summary>
    public class SongListNodeTests
    {
        #region Constructor Tests



        #endregion

        #region Factory Method Tests

        [Fact]
        public void CreateSongNode_ShouldCreateCorrectNode()
        {
            // Arrange
            var song = new DTXMania.Game.Lib.Song.Entities.Song
            {
                Title = "Test Song",
                Artist = "Test Artist",
                Genre = "Rock"
            };
            
            var chart = new SongChart
            {
                DrumLevel = 85,
                GuitarLevel = 78,
                BassLevel = 65,
                HasDrumChart = true,
                HasGuitarChart = true,
                HasBassChart = true
            };

            // Act
            var node = SongListNode.CreateSongNode(song, chart);

            // Assert
            Assert.Equal(NodeType.Score, node.Type);
            Assert.Equal("Test Song", node.Title);
            Assert.Equal("Rock", node.Genre);
            Assert.Same(song, node.DatabaseSong);
            Assert.Same(chart, node.DatabaseChart);
            Assert.True(node.HasScores);
            Assert.Equal(3, node.AvailableDifficulties);
        }

        [Fact]
        public void CreateBoxNode_ShouldCreateCorrectNode()
        {
            // Arrange
            var title = "Test Folder";
            var path = @"C:\Songs\TestFolder";

            // Act
            var node = SongListNode.CreateBoxNode(title, path);

            // Assert
            Assert.Equal(NodeType.Box, node.Type);
            Assert.Equal(title, node.Title);
            Assert.Equal(path, node.DirectoryPath);
            Assert.Equal(title, node.BreadcrumbPath);
            Assert.True(node.IsFolder);
            Assert.False(node.IsPlayable);
        }

        [Fact]
        public void CreateBoxNode_WithParent_ShouldSetCorrectBreadcrumb()
        {
            // Arrange
            var parent = SongListNode.CreateBoxNode("Parent", @"C:\Songs");
            var title = "Child";
            var path = @"C:\Songs\Child";

            // Act
            var node = SongListNode.CreateBoxNode(title, path, parent);

            // Assert
            Assert.Same(parent, node.Parent);
            Assert.Equal("Parent > Child", node.BreadcrumbPath);
        }

        [Fact]
        public void CreateBackNode_ShouldCreateCorrectNode()
        {
            // Arrange
            var parent = SongListNode.CreateBoxNode("Parent", @"C:\Songs");

            // Act
            var node = SongListNode.CreateBackNode(parent);

            // Assert
            Assert.Equal(NodeType.BackBox, node.Type);
            Assert.Equal(".. (Back)", node.Title);
            Assert.Same(parent, node.Parent);
            Assert.True(node.IsBackNavigation);
        }

        [Fact]
        public void CreateRandomNode_ShouldCreateCorrectNode()
        {
            // Act
            var node = SongListNode.CreateRandomNode();

            // Assert
            Assert.Equal(NodeType.Random, node.Type);
            Assert.Equal("Random Select", node.Title);
            Assert.Equal(Color.Yellow, node.TextColor);
        }

        #endregion

        #region Calculated Properties Tests

        [Theory]
        [InlineData(NodeType.Score, true, false, false)]
        [InlineData(NodeType.Box, false, false, true)]
        [InlineData(NodeType.BackBox, false, true, false)]
        [InlineData(NodeType.Random, false, false, false)]
        public void NodeTypeProperties_ShouldReturnCorrectValues(NodeType type, bool isPlayable, bool isBackNav, bool isFolder)
        {
            // Arrange
            var node = new SongListNode { Type = type };
            if (type == NodeType.Score)
            {
                // Add a score to make it playable
                node.Scores[0] = new SongScore { DifficultyLevel = 50 };
            }

            // Act & Assert
            Assert.Equal(isPlayable, node.IsPlayable);
            Assert.Equal(isBackNav, node.IsBackNavigation);
            Assert.Equal(isFolder, node.IsFolder);
        }



        [Theory]
        [InlineData(85, 78, 65, 85)]
        [InlineData(0, 78, 65, 78)]
        [InlineData(0, 0, 0, 0)]
        public void MaxDifficultyLevel_ShouldReturnHighestValue(int level1, int level2, int level3, int expected)
        {
            // Arrange
            var node = new SongListNode();
            if (level1 > 0) node.Scores[0] = new SongScore { DifficultyLevel = level1 };
            if (level2 > 0) node.Scores[1] = new SongScore { DifficultyLevel = level2 };
            if (level3 > 0) node.Scores[2] = new SongScore { DifficultyLevel = level3 };

            // Act
            var result = node.MaxDifficultyLevel;

            // Assert
            Assert.Equal(expected, result);
        }

        #endregion

        #region Score Management Tests

        [Fact]
        public void GetScore_WithValidIndex_ShouldReturnScore()
        {
            // Arrange
            var node = new SongListNode();
            var score = new SongScore { DifficultyLevel = 85 };
            node.Scores[1] = score;

            // Act
            var result = node.GetScore(1);

            // Assert
            Assert.Same(score, result);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(5)]
        [InlineData(10)]
        public void GetScore_WithInvalidIndex_ShouldReturnNull(int index)
        {
            // Arrange
            var node = new SongListNode();

            // Act
            var result = node.GetScore(index);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void SetScore_WithValidIndex_ShouldSetScore()
        {
            // Arrange
            var node = new SongListNode();
            var score = new SongScore { DifficultyLevel = 85 };

            // Act
            node.SetScore(1, score);

            // Assert
            Assert.Same(score, node.Scores[1]);
        }

        [Theory]
        [InlineData(2, new[] { 2, 3, 4, 1, 0 })] // Start from index 2
        [InlineData(0, new[] { 0, 1, 2, 3, 4 })] // Start from index 0
        [InlineData(4, new[] { 4, 3, 2, 1, 0 })] // Start from index 4
        public void GetClosestDifficultyIndex_ShouldReturnCorrectOrder(int anchorIndex, int[] searchOrder)
        {
            // Arrange
            var node = new SongListNode();
            node.Scores[2] = new SongScore { DifficultyLevel = 50 }; // Only index 2 has a score

            // Act
            var result = node.GetClosestDifficultyIndex(anchorIndex);

            // Assert
            Assert.Equal(2, result); // Should find the score at index 2
        }

        #endregion

        #region Child Management Tests

        [Fact]
        public void AddChild_ShouldSetParentAndUpdateBreadcrumb()
        {
            // Arrange
            var parent = SongListNode.CreateBoxNode("Parent", @"C:\Songs");
            var child = SongListNode.CreateBoxNode("Child", @"C:\Songs\Child");

            // Act
            parent.AddChild(child);

            // Assert
            Assert.Contains(child, parent.Children);
            Assert.Same(parent, child.Parent);
            Assert.Equal("Parent > Child", child.BreadcrumbPath);
        }

        [Fact]
        public void RemoveChild_ShouldRemoveAndClearParent()
        {
            // Arrange
            var parent = SongListNode.CreateBoxNode("Parent", @"C:\Songs");
            var child = SongListNode.CreateBoxNode("Child", @"C:\Songs\Child");
            parent.AddChild(child);

            // Act
            var result = parent.RemoveChild(child);

            // Assert
            Assert.True(result);
            Assert.DoesNotContain(child, parent.Children);
            Assert.Null(child.Parent);
        }

        [Fact]
        public void RemoveChild_WithNonExistentChild_ShouldReturnFalse()
        {
            // Arrange
            var parent = SongListNode.CreateBoxNode("Parent", @"C:\Songs");
            var child = SongListNode.CreateBoxNode("Child", @"C:\Songs\Child");

            // Act
            var result = parent.RemoveChild(child);

            // Assert
            Assert.False(result);
        }

        #endregion

        #region Sorting Tests

        [Fact]
        public void SortChildren_ByTitle_ShouldSortCorrectly()
        {
            // Arrange
            var parent = SongListNode.CreateBoxNode("Parent", @"C:\Songs");
            var song1 = new DTXMania.Game.Lib.Song.Entities.Song { Title = "Z Song" };
            var chart1 = new SongChart();
            var child1 = SongListNode.CreateSongNode(song1, chart1);
            
            var song2 = new DTXMania.Game.Lib.Song.Entities.Song { Title = "A Song" };
            var chart2 = new SongChart();
            var child2 = SongListNode.CreateSongNode(song2, chart2);
            
            var child3 = SongListNode.CreateBoxNode("M Folder", @"C:\Songs\M");
            
            parent.AddChild(child1);
            parent.AddChild(child2);
            parent.AddChild(child3);

            // Act
            parent.SortChildren(SongSortCriteria.Title);

            // Assert
            Assert.Equal(child3, parent.Children[0]); // BOX nodes first
            Assert.Equal(child2, parent.Children[1]); // A Song
            Assert.Equal(child1, parent.Children[2]); // Z Song
        }

        [Fact]
        public void SortChildren_ByLevel_ShouldSortByDifficultyDescending()
        {
            // Arrange
            var parent = SongListNode.CreateBoxNode("Parent", @"C:\Songs");
            
            var song1 = new DTXMania.Game.Lib.Song.Entities.Song { Title = "Easy Song" };
            var chart1 = new SongChart { DrumLevel = 30, HasDrumChart = true };
            var child1 = SongListNode.CreateSongNode(song1, chart1);
            
            var song2 = new DTXMania.Game.Lib.Song.Entities.Song { Title = "Hard Song" };
            var chart2 = new SongChart { DrumLevel = 90, HasDrumChart = true };
            var child2 = SongListNode.CreateSongNode(song2, chart2);
            
            parent.AddChild(child1);
            parent.AddChild(child2);

            // Verify scores are populated correctly
            Assert.True(child1.HasScores);
            Assert.True(child2.HasScores);
            Assert.Equal(30, child1.MaxDifficultyLevel);
            Assert.Equal(90, child2.MaxDifficultyLevel);

            // Act
            parent.SortChildren(SongSortCriteria.Level);

            // Assert
            Assert.Equal(child2, parent.Children[0]); // Hard Song (90) first
            Assert.Equal(child1, parent.Children[1]); // Easy Song (30) second
        }

        #endregion
    }
}
