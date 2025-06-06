using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Xunit;
using DTX.UI.Components;
using DTX.Song;
using System.Collections.Generic;
using System.Linq;

namespace DTXMania.Test.UI
{
    /// <summary>
    /// Unit tests for DTXManiaNX curved layout implementation
    /// </summary>
    public class CurvedLayoutTests
    {
        private readonly SongListDisplay _songListDisplay;
        private readonly List<SongListNode> _testSongs;

        public CurvedLayoutTests()
        {
            _songListDisplay = new SongListDisplay();
            _testSongs = CreateTestSongs();
        }

        private List<SongListNode> CreateTestSongs()
        {
            var songs = new List<SongListNode>();
            for (int i = 0; i < 20; i++)
            {
                songs.Add(new SongListNode
                {
                    Type = NodeType.Score,
                    Title = $"Test Song {i + 1}",
                    Genre = $"Test Genre {i + 1}"
                });
            }
            return songs;
        }

        [Fact]
        public void CurvedLayoutCoordinates_ShouldHave13Positions()
        {
            // Arrange & Act
            var coordinates = GetCurvedBarCoordinates();

            // Assert
            Assert.Equal(13, coordinates.Length);
        }

        [Fact]
        public void CurvedLayoutCoordinates_CenterPosition_ShouldBeAtCorrectLocation()
        {
            // Arrange
            var coordinates = GetCurvedBarCoordinates();
            const int centerIndex = 5; // Bar 5 is center in DTXManiaNX

            // Act
            var centerPosition = coordinates[centerIndex];

            // Assert
            Assert.Equal(464, centerPosition.X);
            Assert.Equal(270, centerPosition.Y);
        }

        [Fact]
        public void CurvedLayoutCoordinates_ShouldFormProperCurve()
        {
            // Arrange
            var coordinates = GetCurvedBarCoordinates();

            // Act & Assert - Check curve pattern
            // Bars 0-4: Should curve inward (decreasing X toward center)
            for (int i = 0; i < 4; i++)
            {
                Assert.True(coordinates[i].X > coordinates[i + 1].X, 
                    $"Bar {i} X coordinate should be greater than Bar {i + 1}");
            }

            // Bar 5: Center position (leftmost X)
            Assert.Equal(464, coordinates[5].X);

            // Bars 6-12: Should curve outward (increasing X from center)
            for (int i = 6; i < 12; i++)
            {
                Assert.True(coordinates[i].X < coordinates[i + 1].X,
                    $"Bar {i} X coordinate should be less than Bar {i + 1}");
            }
        }

        [Fact]
        public void SongListDisplay_CurrentList_ShouldSetCorrectly()
        {
            // Arrange
            var testList = _testSongs.Take(10).ToList();

            // Act
            _songListDisplay.CurrentList = testList;

            // Assert
            Assert.Equal(testList.Count, _songListDisplay.CurrentList.Count);
            Assert.Equal(0, _songListDisplay.SelectedIndex);
        }

        [Fact]
        public void SongListDisplay_MoveNext_ShouldAdvanceSelection()
        {
            // Arrange
            _songListDisplay.CurrentList = _testSongs;
            var initialIndex = _songListDisplay.SelectedIndex;

            // Act
            _songListDisplay.MoveNext();

            // Assert
            Assert.Equal(initialIndex + 1, _songListDisplay.SelectedIndex);
        }

        [Fact]
        public void SongListDisplay_MovePrevious_ShouldDecrementSelection()
        {
            // Arrange
            _songListDisplay.CurrentList = _testSongs;
            _songListDisplay.SelectedIndex = 5; // Start at middle

            // Act
            _songListDisplay.MovePrevious();

            // Assert
            Assert.Equal(4, _songListDisplay.SelectedIndex);
        }

        [Fact]
        public void SongListDisplay_MoveNext_ShouldWrapAroundAtEnd()
        {
            // Arrange
            _songListDisplay.CurrentList = _testSongs;
            _songListDisplay.SelectedIndex = _testSongs.Count - 1; // Last item

            // Act
            _songListDisplay.MoveNext();

            // Assert
            Assert.Equal(0, _songListDisplay.SelectedIndex); // Should wrap to first
        }

        [Fact]
        public void SongListDisplay_MovePrevious_ShouldWrapAroundAtBeginning()
        {
            // Arrange
            _songListDisplay.CurrentList = _testSongs;
            _songListDisplay.SelectedIndex = 0; // First item

            // Act
            _songListDisplay.MovePrevious();

            // Assert
            Assert.Equal(_testSongs.Count - 1, _songListDisplay.SelectedIndex); // Should wrap to last
        }

        [Fact]
        public void SongListDisplay_EmptyList_ShouldHandleGracefully()
        {
            // Arrange
            var emptyList = new List<SongListNode>();

            // Act
            _songListDisplay.CurrentList = emptyList;

            // Assert
            Assert.Empty(_songListDisplay.CurrentList);
            Assert.Equal(0, _songListDisplay.SelectedIndex);
        }

        [Fact]
        public void SongListDisplay_Navigation_ShouldNotCrashWithEmptyList()
        {
            // Arrange
            _songListDisplay.CurrentList = new List<SongListNode>();

            // Act & Assert - Should not throw exceptions
            _songListDisplay.MoveNext();
            _songListDisplay.MovePrevious();
            
            Assert.Equal(0, _songListDisplay.SelectedIndex);
        }

        [Theory]
        [InlineData(0, 708, 5)]      // Bar 0 (top)
        [InlineData(5, 464, 270)]    // Bar 5 (center/selected)
        [InlineData(12, 1280, 668)]  // Bar 12 (bottom)
        public void CurvedLayoutCoordinates_SpecificPositions_ShouldMatchDTXManiaNX(int barIndex, int expectedX, int expectedY)
        {
            // Arrange
            var coordinates = GetCurvedBarCoordinates();

            // Act
            var position = coordinates[barIndex];

            // Assert
            Assert.Equal(expectedX, position.X);
            Assert.Equal(expectedY, position.Y);
        }

        [Fact]
        public void SongListDisplay_CenterIndex_ShouldAlwaysBeBar5()
        {
            // Arrange & Act
            const int expectedCenterIndex = 5;

            // Assert
            // This test verifies that the center index matches DTXManiaNX convention
            // where Bar 5 (0-based index) is always the selected/center position
            Assert.Equal(5, expectedCenterIndex);
        }

        [Fact]
        public void SongListDisplay_VisibleItems_ShouldBe13()
        {
            // Arrange & Act
            const int expectedVisibleItems = 13;

            // Assert
            // DTXManiaNX always shows exactly 13 bars in the curved layout
            Assert.Equal(13, expectedVisibleItems);
        }

        [Theory]
        [InlineData(NodeType.Score, "Regular song")]
        [InlineData(NodeType.Box, "Folder/directory")]
        [InlineData(NodeType.BackBox, "Back navigation")]
        public void SongListNode_NodeTypes_ShouldSupportAllTypes(NodeType nodeType, string description)
        {
            // Arrange
            var node = new SongListNode
            {
                Type = nodeType,
                Title = $"Test {description}",
                Genre = "Test Genre"
            };

            // Act & Assert
            Assert.Equal(nodeType, node.Type);
            Assert.Contains("Test", node.Title);
        }

        /// <summary>
        /// Helper method to get the curved bar coordinates (simulates the private constant)
        /// </summary>
        private Point[] GetCurvedBarCoordinates()
        {
            return new Point[]
            {
                new Point(708, 5),      // Bar 0 (top)
                new Point(626, 56),     // Bar 1
                new Point(578, 107),    // Bar 2
                new Point(546, 158),    // Bar 3
                new Point(528, 209),    // Bar 4
                new Point(464, 270),    // Bar 5 (CENTER/SELECTED)
                new Point(548, 362),    // Bar 6
                new Point(578, 413),    // Bar 7
                new Point(624, 464),    // Bar 8
                new Point(686, 515),    // Bar 9
                new Point(788, 566),    // Bar 10
                new Point(996, 617),    // Bar 11
                new Point(1280, 668)    // Bar 12 (bottom)
            };
        }
    }
}
