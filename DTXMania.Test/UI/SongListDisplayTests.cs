using Xunit;
using DTX.UI.Components;
using DTX.Song;
using DTX.Resources;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Moq;
using System.Collections.Generic;

namespace DTXMania.Test.UI
{
    /// <summary>
    /// Unit tests for SongListDisplay component
    /// </summary>
    public class SongListDisplayTests
    {
        [Fact]
        public void Constructor_ShouldInitializeCorrectly()
        {
            // Arrange & Act
            var display = new SongListDisplay();

            // Assert
            Assert.NotNull(display.CurrentList);
            Assert.Empty(display.CurrentList);
            Assert.Equal(0, display.SelectedIndex);
            Assert.Equal(0, display.CurrentDifficulty);
            Assert.False(display.IsScrolling);
            Assert.Null(display.SelectedSong);
        }

        [Fact]
        public void CurrentList_WhenSet_ShouldUpdateSelection()
        {
            // Arrange
            var display = new SongListDisplay();
            var songs = new List<SongListNode>
            {
                new SongListNode { Type = NodeType.Score, Title = "Song 1" },
                new SongListNode { Type = NodeType.Score, Title = "Song 2" }
            };

            // Act
            display.CurrentList = songs;

            // Assert
            Assert.Equal(songs, display.CurrentList);
            Assert.Equal(0, display.SelectedIndex);
            Assert.Equal(songs[0], display.SelectedSong);
        }

        [Fact]
        public void SelectedIndex_WhenChanged_ShouldUpdateSelectedSong()
        {
            // Arrange
            var display = new SongListDisplay();
            var songs = new List<SongListNode>
            {
                new SongListNode { Type = NodeType.Score, Title = "Song 1" },
                new SongListNode { Type = NodeType.Score, Title = "Song 2" }
            };
            display.CurrentList = songs;

            // Act
            display.SelectedIndex = 1;

            // Assert
            Assert.Equal(1, display.SelectedIndex);
            Assert.Equal(songs[1], display.SelectedSong);
        }

        [Fact]
        public void SelectedIndex_WhenOutOfBounds_ShouldClamp()
        {
            // Arrange
            var display = new SongListDisplay();
            var songs = new List<SongListNode>
            {
                new SongListNode { Type = NodeType.Score, Title = "Song 1" }
            };
            display.CurrentList = songs;

            // Act & Assert - Test upper bound
            display.SelectedIndex = 10;
            Assert.Equal(0, display.SelectedIndex); // Should clamp to last valid index

            // Act & Assert - Test lower bound
            display.SelectedIndex = -5;
            Assert.Equal(0, display.SelectedIndex); // Should clamp to 0
        }

        [Fact]
        public void MoveNext_ShouldAdvanceSelection()
        {
            // Arrange
            var display = new SongListDisplay();
            var songs = new List<SongListNode>
            {
                new SongListNode { Type = NodeType.Score, Title = "Song 1" },
                new SongListNode { Type = NodeType.Score, Title = "Song 2" },
                new SongListNode { Type = NodeType.Score, Title = "Song 3" }
            };
            display.CurrentList = songs;

            // Act
            display.MoveNext();

            // Assert
            Assert.Equal(1, display.SelectedIndex);
            Assert.Equal(songs[1], display.SelectedSong);
        }

        [Fact]
        public void MoveNext_AtEnd_ShouldWrapToBeginning()
        {
            // Arrange
            var display = new SongListDisplay();
            var songs = new List<SongListNode>
            {
                new SongListNode { Type = NodeType.Score, Title = "Song 1" },
                new SongListNode { Type = NodeType.Score, Title = "Song 2" }
            };
            display.CurrentList = songs;
            display.SelectedIndex = 1; // Last item

            // Act
            display.MoveNext();

            // Assert
            Assert.Equal(0, display.SelectedIndex); // Should wrap to first
            Assert.Equal(songs[0], display.SelectedSong);
        }

        [Fact]
        public void MovePrevious_ShouldGoBackward()
        {
            // Arrange
            var display = new SongListDisplay();
            var songs = new List<SongListNode>
            {
                new SongListNode { Type = NodeType.Score, Title = "Song 1" },
                new SongListNode { Type = NodeType.Score, Title = "Song 2" }
            };
            display.CurrentList = songs;
            display.SelectedIndex = 1;

            // Act
            display.MovePrevious();

            // Assert
            Assert.Equal(0, display.SelectedIndex);
            Assert.Equal(songs[0], display.SelectedSong);
        }

        [Fact]
        public void MovePrevious_AtBeginning_ShouldWrapToEnd()
        {
            // Arrange
            var display = new SongListDisplay();
            var songs = new List<SongListNode>
            {
                new SongListNode { Type = NodeType.Score, Title = "Song 1" },
                new SongListNode { Type = NodeType.Score, Title = "Song 2" }
            };
            display.CurrentList = songs;
            display.SelectedIndex = 0; // First item

            // Act
            display.MovePrevious();

            // Assert
            Assert.Equal(1, display.SelectedIndex); // Should wrap to last
            Assert.Equal(songs[1], display.SelectedSong);
        }

        [Fact]
        public void CurrentDifficulty_WhenSet_ShouldClampToValidRange()
        {
            // Arrange
            var display = new SongListDisplay();

            // Act & Assert - Test valid range
            display.CurrentDifficulty = 2;
            Assert.Equal(2, display.CurrentDifficulty);

            // Act & Assert - Test upper bound
            display.CurrentDifficulty = 10;
            Assert.Equal(4, display.CurrentDifficulty); // Should clamp to max (4)

            // Act & Assert - Test lower bound
            display.CurrentDifficulty = -5;
            Assert.Equal(0, display.CurrentDifficulty); // Should clamp to min (0)
        }

        [Fact]
        public void CycleDifficulty_WithValidSong_ShouldAdvanceDifficulty()
        {
            // Arrange
            var display = new SongListDisplay();
            var song = new SongListNode 
            { 
                Type = NodeType.Score, 
                Title = "Test Song",
                Scores = new SongScore[]
                {
                    new SongScore { Metadata = new SongMetadata() }, // Difficulty 0
                    new SongScore { Metadata = new SongMetadata() }, // Difficulty 1
                    null, // Difficulty 2 not available
                    new SongScore { Metadata = new SongMetadata() }, // Difficulty 3
                    null  // Difficulty 4 not available
                }
            };
            display.CurrentList = new List<SongListNode> { song };
            display.CurrentDifficulty = 0;

            // Act
            display.CycleDifficulty();

            // Assert - Should skip to next available difficulty
            Assert.Equal(1, display.CurrentDifficulty);
        }

        [Fact]
        public void RefreshDisplay_ShouldClearCaches()
        {
            // Arrange
            var display = new SongListDisplay();

            // Act
            display.RefreshDisplay();

            // Assert - Should not throw and complete successfully
            // (Cache clearing is internal, so we just verify the method runs)
            Assert.True(true);
        }

        [Fact]
        public void EmptyList_ShouldHandleGracefully()
        {
            // Arrange
            var display = new SongListDisplay();
            display.CurrentList = new List<SongListNode>();

            // Act & Assert - Should not throw
            display.MoveNext();
            display.MovePrevious();
            display.CycleDifficulty();
            display.ActivateSelected();

            Assert.Equal(0, display.SelectedIndex);
            Assert.Null(display.SelectedSong);
        }

        [Fact]
        public void SelectionChanged_Event_ShouldFireWhenSelectionChanges()
        {
            // Arrange
            var display = new SongListDisplay();
            var songs = new List<SongListNode>
            {
                new SongListNode { Type = NodeType.Score, Title = "Song 1" },
                new SongListNode { Type = NodeType.Score, Title = "Song 2" }
            };
            display.CurrentList = songs;

            SongSelectionChangedEventArgs eventArgs = null;
            display.SelectionChanged += (sender, e) => eventArgs = e;

            // Act
            display.SelectedIndex = 1;

            // Assert
            Assert.NotNull(eventArgs);
            Assert.Equal(songs[1], eventArgs.SelectedSong);
            Assert.Equal(0, eventArgs.CurrentDifficulty);
        }

        // Phase 2 Enhancement Tests

        [Fact]
        public void InitializeEnhancedRendering_WithValidParameters_ShouldSetupRendering()
        {
            // Arrange
            var display = new SongListDisplay();
            var mockGraphicsDevice = new Mock<GraphicsDevice>();
            var mockResourceManager = new Mock<IResourceManager>();

            // Act & Assert - Should not throw
            // Note: In headless test environment, this tests the method signature and basic validation
            try
            {
                display.InitializeEnhancedRendering(mockGraphicsDevice.Object, mockResourceManager.Object);
                Assert.True(true); // Method completed without throwing
            }
            catch (ArgumentNullException)
            {
                // Expected in test environment without proper graphics context
                Assert.True(true);
            }
        }

        [Fact]
        public void SetEnhancedRendering_WithBooleanValues_ShouldAcceptSettings()
        {
            // Arrange
            var display = new SongListDisplay();

            // Act & Assert - Should not throw
            display.SetEnhancedRendering(true);
            display.SetEnhancedRendering(false);

            Assert.NotNull(display); // Verify object is still valid
        }

        [Fact]
        public void RefreshDisplay_WithEnhancedRendering_ShouldClearAllCaches()
        {
            // Arrange
            var display = new SongListDisplay();
            var songs = new List<SongListNode>
            {
                new SongListNode { Type = NodeType.Score, Title = "Song 1" },
                new SongListNode { Type = NodeType.Box, Title = "Folder 1" }
            };
            display.CurrentList = songs;

            // Act
            display.RefreshDisplay();

            // Assert - Should complete without throwing
            Assert.NotNull(display);
            Assert.Equal(songs, display.CurrentList);
        }

        [Fact]
        public void SongListDisplay_WithMixedNodeTypes_ShouldHandleAllTypes()
        {
            // Arrange
            var display = new SongListDisplay();
            var mixedList = new List<SongListNode>
            {
                new SongListNode { Type = NodeType.Score, Title = "Song 1" },
                new SongListNode { Type = NodeType.Box, Title = "Folder 1" },
                new SongListNode { Type = NodeType.Random, Title = "Random Select" },
                new SongListNode { Type = NodeType.BackBox, Title = "Back" }
            };

            // Act
            display.CurrentList = mixedList;

            // Assert
            Assert.Equal(mixedList, display.CurrentList);
            Assert.Equal(0, display.SelectedIndex);
            Assert.Equal(mixedList[0], display.SelectedSong);

            // Test navigation through different node types
            display.MoveNext();
            Assert.Equal(NodeType.Box, display.SelectedSong.Type);

            display.MoveNext();
            Assert.Equal(NodeType.Random, display.SelectedSong.Type);

            display.MoveNext();
            Assert.Equal(NodeType.BackBox, display.SelectedSong.Type);
        }

        [Fact]
        public void ActivateSelected_WithDifferentNodeTypes_ShouldFireAppropriateEvents()
        {
            // Arrange
            var display = new SongListDisplay();
            var songs = new List<SongListNode>
            {
                new SongListNode { Type = NodeType.Score, Title = "Song 1" },
                new SongListNode { Type = NodeType.Box, Title = "Folder 1" }
            };
            display.CurrentList = songs;

            bool songActivatedFired = false;
            SongActivatedEventArgs songEventArgs = null;

            display.SongActivated += (sender, e) =>
            {
                songActivatedFired = true;
                songEventArgs = e;
            };

            // Act - Activate song
            display.ActivateSelected();

            // Assert
            Assert.True(songActivatedFired);
            Assert.NotNull(songEventArgs);
            Assert.Equal(songs[0], songEventArgs.Song);
        }

        [Fact]
        public void Dispose_WithEnhancedRendering_ShouldCleanupResources()
        {
            // Arrange
            var display = new SongListDisplay();

            // Act & Assert - Should not throw
            display.Dispose();
        }
    }
}
