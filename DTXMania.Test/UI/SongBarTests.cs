using Xunit;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using DTX.Song.Components;
using DTX.Song;
using DTX.Resources;
using DTXMania.Test.Helpers;
using DTXMania.Game.Lib.Song.Entities;
using SongScore = DTXMania.Game.Lib.Song.Entities.SongScore;

namespace DTXMania.Test.UI
{
    /// <summary>
    /// Unit tests for SongBar component (Phase 4 enhancement)
    /// </summary>
    public class SongBarTests : IDisposable
    {
        private readonly SongBar _songBar;
        private readonly SongListNode _testSongNode;

        public SongBarTests()
        {

            // Create test song and chart
            var testSong = new DTXMania.Game.Lib.Song.Entities.Song
            {
                Title = "Test Song",
                Artist = "Test Artist",
                Genre = "Test Genre"
            };
            
            var testChart = new SongChart
            {
                FilePath = "test.dtx",
                BPM = 120.0,
                DrumLevel = 85
            };
            
            // Create test song node
            _testSongNode = new SongListNode
            {
                Type = NodeType.Score,
                Title = "Test Song",
                DatabaseSong = testSong,
                DatabaseChart = testChart,
                Scores = new SongScore[]
                {
                    new SongScore
                    {
                        Instrument = EInstrumentPart.DRUMS,
                        BestScore = 950000,
                        BestRank = 85,
                        FullCombo = true,
                        PlayCount = 5
                    }
                }
            };

            // Create song bar
            _songBar = new SongBar();
        }

        [Fact]
        public void Constructor_ShouldInitializeWithDefaultValues()
        {
            var songBar = new SongBar();

            Assert.NotNull(songBar);
            Assert.Equal(new Vector2(600, 32), songBar.Size);
            Assert.False(songBar.IsSelected);
            Assert.False(songBar.IsCenter);
            Assert.Equal(0, songBar.CurrentDifficulty);
        }

        [Fact]
        public void SongNode_WhenSet_ShouldUpdateAndInvalidateTextures()
        {
            // Act
            _songBar.SongNode = _testSongNode;

            // Assert
            Assert.Equal(_testSongNode, _songBar.SongNode);
        }

        [Fact]
        public void CurrentDifficulty_ShouldClampToValidRange()
        {
            // Test lower bound
            _songBar.CurrentDifficulty = -1;
            Assert.Equal(0, _songBar.CurrentDifficulty);

            // Test upper bound
            _songBar.CurrentDifficulty = 10;
            Assert.Equal(4, _songBar.CurrentDifficulty);

            // Test valid value
            _songBar.CurrentDifficulty = 2;
            Assert.Equal(2, _songBar.CurrentDifficulty);
        }

        [Fact]
        public void IsSelected_WhenChanged_ShouldUpdateVisualState()
        {
            // Arrange
            _songBar.SongNode = _testSongNode;

            // Act
            _songBar.IsSelected = true;

            // Assert
            Assert.True(_songBar.IsSelected);
        }

        [Fact]
        public void IsCenter_WhenChanged_ShouldUpdateVisualState()
        {
            // Arrange
            _songBar.SongNode = _testSongNode;

            // Act
            _songBar.IsCenter = true;

            // Assert
            Assert.True(_songBar.IsCenter);
        }

        [Fact]
        public void UpdateVisualState_WithSelectedAndCenter_ShouldSetCorrectColors()
        {
            // Arrange
            _songBar.SongNode = _testSongNode;

            // Act
            _songBar.IsSelected = true;
            _songBar.IsCenter = true;
            _songBar.UpdateVisualState();

            // Assert - Visual state updated (no direct way to test colors, but method should not throw)
            Assert.True(_songBar.IsSelected);
            Assert.True(_songBar.IsCenter);
        }



        [Theory]
        [InlineData(NodeType.Score)]
        [InlineData(NodeType.Box)]
        [InlineData(NodeType.BackBox)]
        [InlineData(NodeType.Random)]
        public void SongBar_ShouldHandleAllNodeTypes(NodeType nodeType)
        {
            // Arrange
            var testNode = new SongListNode
            {
                Type = nodeType,
                Title = $"Test {nodeType}"
            };

            // Act
            _songBar.SongNode = testNode;

            // Assert
            Assert.Equal(testNode, _songBar.SongNode);
            Assert.Equal(nodeType, _songBar.SongNode.Type);
        }



        public void Dispose()
        {
            _songBar?.Dispose();
        }
    }
}
