using Xunit;
using DTX.Song.Components;
using DTX.Song;
using DTX.Resources;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Moq;
using System.Collections.Generic;
using DTXMania.Test.Helpers;
using DTXMania.Game.Lib.Song.Entities;
using SongScore = DTXMania.Game.Lib.Song.Entities.SongScore;

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
        public void SelectedIndex_WhenOutOfBounds_ShouldAllowInfiniteScrolling()
        {
            // Arrange
            var display = new SongListDisplay();
            var songs = new List<SongListNode>
            {
                new SongListNode { Type = NodeType.Score, Title = "Song 1" }
            };
            display.CurrentList = songs;

            // Act & Assert - Test infinite scrolling allows any index
            display.SelectedIndex = 10;
            Assert.Equal(10, display.SelectedIndex); // Index can be any value
            Assert.Equal(songs[0], display.SelectedSong); // But SelectedSong uses modulo

            // Act & Assert - Test negative indices
            display.SelectedIndex = -5;
            Assert.Equal(-5, display.SelectedIndex); // Index can be negative
            Assert.Equal(songs[0], display.SelectedSong); // But SelectedSong uses modulo
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
        public void MoveNext_AtEnd_ShouldContinueInfinitely()
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

            // Assert - Index continues infinitely, but SelectedSong wraps correctly
            Assert.Equal(2, display.SelectedIndex); // Index continues
            Assert.Equal(songs[0], display.SelectedSong); // Song wraps via modulo
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
        public void MovePrevious_AtBeginning_ShouldContinueInfinitely()
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

            // Assert - Index continues infinitely, but SelectedSong wraps correctly
            Assert.Equal(-1, display.SelectedIndex); // Index continues
            Assert.Equal(songs[1], display.SelectedSong); // Song wraps via modulo
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
                    new SongScore { Instrument = EInstrumentPart.DRUMS }, // Difficulty 0
                    new SongScore { Instrument = EInstrumentPart.GUITAR }, // Difficulty 1
                    null, // Difficulty 2 not available
                    new SongScore { Instrument = EInstrumentPart.BASS }, // Difficulty 3
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


    }
}
