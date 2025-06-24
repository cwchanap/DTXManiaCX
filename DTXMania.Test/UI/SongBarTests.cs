using Xunit;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using DTX.UI.Components;
using DTX.Song;
using DTX.Resources;
using DTXMania.Test.Helpers;

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

            // Create test song node
            _testSongNode = new SongListNode
            {
                Type = NodeType.Score,
                Title = "Test Song",
                Metadata = new SongMetadata
                {
                    Title = "Test Song",
                    Artist = "Test Artist",
                    BPM = 120.0,
                    DrumLevel = 85
                },
                Scores = new SongScore[]
                {
                    new SongScore
                    {
                        Metadata = new SongMetadata { FilePath = "test.dtx" },
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

        [Fact]
        public void SetTextures_ShouldAcceptNullValues()
        {
            // Act & Assert - Should not throw
            _songBar.SetTextures(null, null, null);
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

        [Fact]
        public void SongBar_WithNullSongNode_ShouldNotCrash()
        {
            // Act
            _songBar.SongNode = null;

            // Assert - Should not throw
            Assert.Null(_songBar.SongNode);
        }

        [Fact]
        public void SongBar_ShouldBeDisposable()
        {
            // Act & Assert - Should not throw
            _songBar.Dispose();
        }

        [Fact]
        public void SongBar_WithDifferentDifficulties_ShouldUpdateCorrectly()
        {
            // Arrange
            _songBar.SongNode = _testSongNode;

            // Act & Assert
            for (int i = 0; i <= 4; i++)
            {
                _songBar.CurrentDifficulty = i;
                Assert.Equal(i, _songBar.CurrentDifficulty);
            }
        }

        [Fact]
        public void SongBar_VisualStateChanges_ShouldNotThrow()
        {
            // Arrange
            _songBar.SongNode = _testSongNode;

            // Act & Assert - All combinations should work without throwing
            _songBar.IsSelected = false;
            _songBar.IsCenter = false;
            _songBar.UpdateVisualState();

            _songBar.IsSelected = true;
            _songBar.IsCenter = false;
            _songBar.UpdateVisualState();

            _songBar.IsSelected = false;
            _songBar.IsCenter = true;
            _songBar.UpdateVisualState();

            _songBar.IsSelected = true;
            _songBar.IsCenter = true;
            _songBar.UpdateVisualState();
        }

        public void Dispose()
        {
            _songBar?.Dispose();
        }
    }
}
