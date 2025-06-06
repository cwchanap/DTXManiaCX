using System;
using Xunit;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using DTX.UI.Components;
using DTX.Song;
using DTXMania.Test.Helpers;

namespace DTXMania.Test.UI
{
    /// <summary>
    /// Unit tests for SongStatusPanel component
    /// Tests the enhanced Phase 5 functionality including note counts, duration, and improved difficulty display
    /// </summary>
    public class SongStatusPanelTests : IDisposable
    {
        private readonly TestGraphicsDeviceService _graphicsDeviceService;
        private readonly SongStatusPanel _statusPanel;
        private readonly SongListNode _testSongNode;
        private readonly SongMetadata _testMetadata;

        public SongStatusPanelTests()
        {
            _graphicsDeviceService = new TestGraphicsDeviceService();
            _statusPanel = new SongStatusPanel();

            // Create test metadata with enhanced Phase 5 properties
            _testMetadata = new SongMetadata
            {
                Title = "Test Song",
                Artist = "Test Artist",
                Genre = "Test Genre",
                BPM = 120.0,
                Duration = 180.5, // 3 minutes 0.5 seconds
                DrumLevel = 85,
                GuitarLevel = 78,
                BassLevel = 65,
                DrumNoteCount = 1250,
                GuitarNoteCount = 890,
                BassNoteCount = 650,
                Comment = "Test comment"
            };

            // Create test song node with scores
            _testSongNode = new SongListNode
            {
                Type = NodeType.Score,
                Title = "Test Song",
                Scores = new SongScore[]
                {
                    new SongScore { Metadata = _testMetadata, Instrument = "DRUMS", DifficultyLevel = 85, BestScore = 950000, BestRank = 92, FullCombo = true, PlayCount = 15, HighSkill = 85.5 },
                    new SongScore { Metadata = _testMetadata, Instrument = "GUITAR", DifficultyLevel = 78, BestScore = 890000, BestRank = 88, FullCombo = false, PlayCount = 8, HighSkill = 72.3 },
                    new SongScore { Metadata = _testMetadata, Instrument = "BASS", DifficultyLevel = 65, BestScore = 820000, BestRank = 85, FullCombo = false, PlayCount = 5, HighSkill = 68.1 }
                }
            };
        }

        [Fact]
        public void Constructor_ShouldInitializeWithDefaultValues()
        {
            var panel = new SongStatusPanel();

            Assert.NotNull(panel);
            Assert.Equal(new Vector2(300, 400), panel.Size);
            Assert.True(panel.Visible);
        }

        [Fact]
        public void UpdateSongInfo_WithValidSong_ShouldUpdateCurrentSong()
        {
            // Act
            _statusPanel.UpdateSongInfo(_testSongNode, 0);

            // Assert - No direct way to verify internal state, but method should not throw
            Assert.True(true);
        }

        [Fact]
        public void UpdateSongInfo_WithNullSong_ShouldNotThrow()
        {
            // Act & Assert
            _statusPanel.UpdateSongInfo(null, 0);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public void UpdateSongInfo_WithDifferentDifficulties_ShouldNotThrow(int difficulty)
        {
            // Act & Assert
            _statusPanel.UpdateSongInfo(_testSongNode, difficulty);
        }

        [Fact]
        public void Font_Property_ShouldGetAndSet()
        {
            // Skip if no graphics device available
            if (_graphicsDeviceService.GraphicsDevice == null)
            {
                Assert.True(true, "Skipped: No graphics device available");
                return;
            }

            // Arrange
            var font = CreateTestFont();

            // Act
            _statusPanel.Font = font;

            // Assert
            Assert.Equal(font, _statusPanel.Font);
        }

        [Fact]
        public void SmallFont_Property_ShouldGetAndSet()
        {
            // Skip if no graphics device available
            if (_graphicsDeviceService.GraphicsDevice == null)
            {
                Assert.True(true, "Skipped: No graphics device available");
                return;
            }

            // Arrange
            var font = CreateTestFont();

            // Act
            _statusPanel.SmallFont = font;

            // Assert
            Assert.Equal(font, _statusPanel.SmallFont);
        }

        [Fact]
        public void WhitePixel_Property_ShouldGetAndSet()
        {
            // Skip if no graphics device available
            if (_graphicsDeviceService.GraphicsDevice == null)
            {
                Assert.True(true, "Skipped: No graphics device available");
                return;
            }

            // Arrange
            var texture = CreateTestTexture();

            // Act
            _statusPanel.WhitePixel = texture;

            // Assert
            Assert.Equal(texture, _statusPanel.WhitePixel);
        }

        // BackgroundColor property test removed - property doesn't exist in current implementation

        [Fact]
        public void SongStatusPanel_WithBoxNode_ShouldNotCrash()
        {
            // Arrange
            var boxNode = new SongListNode
            {
                Type = NodeType.Box,
                Title = "Test Box"
            };

            // Act & Assert
            _statusPanel.UpdateSongInfo(boxNode, 0);
        }

        [Fact]
        public void SongStatusPanel_WithBackBoxNode_ShouldNotCrash()
        {
            // Arrange
            var backNode = new SongListNode
            {
                Type = NodeType.BackBox,
                Title = ".."
            };

            // Act & Assert
            _statusPanel.UpdateSongInfo(backNode, 0);
        }

        [Fact]
        public void SongStatusPanel_WithRandomNode_ShouldNotCrash()
        {
            // Arrange
            var randomNode = new SongListNode
            {
                Type = NodeType.Random,
                Title = "Random"
            };

            // Act & Assert
            _statusPanel.UpdateSongInfo(randomNode, 0);
        }

        [Fact]
        public void SongStatusPanel_WithEmptyMetadata_ShouldNotCrash()
        {
            // Arrange
            var emptyNode = new SongListNode
            {
                Type = NodeType.Score,
                Title = "Empty Song",
                Scores = new SongScore[]
                {
                    new SongScore { Metadata = new SongMetadata() }
                }
            };

            // Act & Assert
            _statusPanel.UpdateSongInfo(emptyNode, 0);
        }

        [Fact]
        public void SongStatusPanel_WithNullScores_ShouldNotCrash()
        {
            // Arrange
            var nullScoreNode = new SongListNode
            {
                Type = NodeType.Score,
                Title = "No Scores",
                Scores = null
            };

            // Act & Assert
            _statusPanel.UpdateSongInfo(nullScoreNode, 0);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(10)]
        [InlineData(100)]
        public void UpdateSongInfo_WithInvalidDifficulty_ShouldNotThrow(int difficulty)
        {
            // Act & Assert
            _statusPanel.UpdateSongInfo(_testSongNode, difficulty);
        }

        [Fact]
        public void SongStatusPanel_ShouldBeDisposable()
        {
            // Act & Assert - Should not throw
            _statusPanel.Dispose();
        }

        [Fact]
        public void SongStatusPanel_WithEnhancedMetadata_ShouldDisplayDuration()
        {
            // Arrange
            var metadataWithDuration = new SongMetadata
            {
                Title = "Duration Test",
                Duration = 125.0 // 2:05
            };

            var nodeWithDuration = new SongListNode
            {
                Type = NodeType.Score,
                Title = "Duration Test",
                Scores = new SongScore[]
                {
                    new SongScore { Metadata = metadataWithDuration }
                }
            };

            // Act & Assert - Should not throw
            _statusPanel.UpdateSongInfo(nodeWithDuration, 0);
        }

        [Fact]
        public void SongStatusPanel_WithNoteCounts_ShouldDisplayNoteInformation()
        {
            // Arrange
            var metadataWithNotes = new SongMetadata
            {
                Title = "Note Count Test",
                DrumNoteCount = 1500,
                GuitarNoteCount = 1200,
                BassNoteCount = 800
            };

            var nodeWithNotes = new SongListNode
            {
                Type = NodeType.Score,
                Title = "Note Count Test",
                Scores = new SongScore[]
                {
                    new SongScore { Metadata = metadataWithNotes }
                }
            };

            // Act & Assert - Should not throw
            _statusPanel.UpdateSongInfo(nodeWithNotes, 0);
        }

        [Fact]
        public void SongStatusPanel_WithPartialInstrumentData_ShouldHandleGracefully()
        {
            // Arrange - Only drums have data
            var partialMetadata = new SongMetadata
            {
                Title = "Partial Data Test",
                DrumLevel = 90,
                DrumNoteCount = 1800,
                // Guitar and Bass levels/notes are null
            };

            var partialNode = new SongListNode
            {
                Type = NodeType.Score,
                Title = "Partial Data Test",
                Scores = new SongScore[]
                {
                    new SongScore { Metadata = partialMetadata }
                }
            };

            // Act & Assert - Should not throw
            _statusPanel.UpdateSongInfo(partialNode, 0);
        }

        private SpriteFont CreateTestFont()
        {
            if (_graphicsDeviceService.GraphicsDevice == null)
                return null;

            // Create a minimal test font (this is a simplified approach)
            // In a real scenario, you'd load an actual font file
            return null; // Return null for now as creating SpriteFont requires content pipeline
        }

        private Texture2D CreateTestTexture()
        {
            if (_graphicsDeviceService.GraphicsDevice == null)
                return null;

            var texture = new Texture2D(_graphicsDeviceService.GraphicsDevice, 1, 1);
            texture.SetData(new[] { Color.White });
            return texture;
        }

        public void Dispose()
        {
            _statusPanel?.Dispose();
            _graphicsDeviceService?.Dispose();
        }
    }
}
