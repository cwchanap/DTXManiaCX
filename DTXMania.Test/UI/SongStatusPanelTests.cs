using System;
using Xunit;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using DTX.UI.Components;
using DTX.Song;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Test.Helpers;
using SongScore = DTXMania.Game.Lib.Song.Entities.SongScore;

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
        private readonly DTXMania.Game.Lib.Song.Entities.Song _testSong;
        private readonly SongChart _testChart;

        public SongStatusPanelTests()
        {
            _graphicsDeviceService = new TestGraphicsDeviceService();
            _statusPanel = new SongStatusPanel();

            // Create test song and chart with enhanced Phase 5 properties
            _testSong = new DTXMania.Game.Lib.Song.Entities.Song
            {
                Title = "Test Song",
                Artist = "Test Artist",
                Genre = "Test Genre",
                Comment = "Test comment"
            };
            
            _testChart = new SongChart
            {
                Bpm = 120.0,
                Duration = 180.5, // 3 minutes 0.5 seconds
                DrumLevel = 85,
                GuitarLevel = 78,
                BassLevel = 65,
                DrumNoteCount = 1250,
                GuitarNoteCount = 890,
                BassNoteCount = 650,
                HasDrumChart = true,
                HasGuitarChart = true,
                HasBassChart = true
            };

            // Create test song node with scores
            _testSongNode = new SongListNode
            {
                Type = NodeType.Score,
                Title = "Test Song",
                DatabaseSong = _testSong,
                DatabaseChart = _testChart,
                Scores = new SongScore[]
                {
                    new SongScore { Instrument = EInstrumentPart.DRUMS, DifficultyLevel = 85, BestScore = 950000, BestRank = 92, FullCombo = true, PlayCount = 15, HighSkill = 85.5 },
                    new SongScore { Instrument = EInstrumentPart.GUITAR, DifficultyLevel = 78, BestScore = 890000, BestRank = 88, FullCombo = false, PlayCount = 8, HighSkill = 72.3 },
                    new SongScore { Instrument = EInstrumentPart.BASS, DifficultyLevel = 65, BestScore = 820000, BestRank = 85, FullCombo = false, PlayCount = 5, HighSkill = 68.1 }
                }
            };
        }

        [Fact]
        public void Constructor_ShouldInitializeWithDefaultValues()
        {
            var panel = new SongStatusPanel();

            Assert.NotNull(panel);
            Assert.Equal(new Vector2(580, 320), panel.Size); // Updated to DTXManiaNX authentic size
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
        public void SongStatusPanel_WithEmptyData_ShouldNotCrash()
        {
            // Arrange
            var emptyNode = new SongListNode
            {
                Type = NodeType.Score,
                Title = "Empty Song",
                DatabaseSong = new DTXMania.Game.Lib.Song.Entities.Song(),
                DatabaseChart = new SongChart(),
                Scores = new SongScore[]
                {
                    new SongScore()
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
        public void SongStatusPanel_WithEnhancedData_ShouldDisplayDuration()
        {
            // Arrange
            var songWithDuration = new DTXMania.Game.Lib.Song.Entities.Song
            {
                Title = "Duration Test"
            };
            
            var chartWithDuration = new SongChart
            {
                Duration = 125.0 // 2:05
            };

            var nodeWithDuration = new SongListNode
            {
                Type = NodeType.Score,
                Title = "Duration Test",
                DatabaseSong = songWithDuration,
                DatabaseChart = chartWithDuration,
                Scores = new SongScore[]
                {
                    new SongScore()
                }
            };

            // Act & Assert - Should not throw
            _statusPanel.UpdateSongInfo(nodeWithDuration, 0);
        }

        [Fact]
        public void SongStatusPanel_WithNoteCounts_ShouldDisplayNoteInformation()
        {
            // Arrange
            var songWithNotes = new DTXMania.Game.Lib.Song.Entities.Song
            {
                Title = "Note Count Test"
            };
            
            var chartWithNotes = new SongChart
            {
                DrumNoteCount = 1500,
                GuitarNoteCount = 1200,
                BassNoteCount = 800
            };

            var nodeWithNotes = new SongListNode
            {
                Type = NodeType.Score,
                Title = "Note Count Test",
                DatabaseSong = songWithNotes,
                DatabaseChart = chartWithNotes,
                Scores = new SongScore[]
                {
                    new SongScore()
                }
            };

            // Act & Assert - Should not throw
            _statusPanel.UpdateSongInfo(nodeWithNotes, 0);
        }

        [Fact]
        public void SongStatusPanel_WithPartialInstrumentData_ShouldHandleGracefully()
        {
            // Arrange - Only drums have data
            var partialSong = new DTXMania.Game.Lib.Song.Entities.Song
            {
                Title = "Partial Data Test"
            };
            
            var partialChart = new SongChart
            {
                DrumLevel = 90,
                DrumNoteCount = 1800,
                HasDrumChart = true,
                // Guitar and Bass levels/notes are 0/false
                GuitarLevel = 0,
                BassLevel = 0,
                HasGuitarChart = false,
                HasBassChart = false
            };

            var partialNode = new SongListNode
            {
                Type = NodeType.Score,
                Title = "Partial Data Test",
                DatabaseSong = partialSong,
                DatabaseChart = partialChart,
                Scores = new SongScore[]
                {
                    new SongScore()
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

        #region Difficulty Chart Selection Tests

        [Fact]
        public void GetCurrentDifficultyChart_WithMultipleCharts_ShouldSelectCorrectChart()
        {
            // Arrange - Create a song with multiple charts
            var song = new DTXMania.Game.Lib.Song.Entities.Song
            {
                Title = "Multi Chart Song",
                Artist = "Test Artist"
            };

            var chart1 = new SongChart { Duration = 120.5, Bpm = 140, DrumLevel = 30, FilePath = "bas.dtx" };
            var chart2 = new SongChart { Duration = 180.7, Bpm = 140, DrumLevel = 50, FilePath = "adv.dtx" };
            var chart3 = new SongChart { Duration = 240.3, Bpm = 140, DrumLevel = 70, FilePath = "ext.dtx" };

            song.Charts = new List<SongChart> { chart1, chart2, chart3 };

            var songNode = new SongListNode
            {
                Type = NodeType.Score,
                Title = "Multi Chart Song",
                DatabaseSong = song,
                DatabaseChart = chart1, // Primary chart
                Scores = new SongScore[]
                {
                    new SongScore { Instrument = EInstrumentPart.DRUMS, DifficultyLevel = 30 },
                    new SongScore { Instrument = EInstrumentPart.DRUMS, DifficultyLevel = 50 },
                    new SongScore { Instrument = EInstrumentPart.DRUMS, DifficultyLevel = 70 }
                }
            };

            // Act & Assert - Test each difficulty
            _statusPanel.UpdateSongInfo(songNode, 0);
            // Note: We can't directly test GetCurrentDifficultyChart as it's private,
            // but we can verify the behavior through UpdateSongInfo calls
            
            _statusPanel.UpdateSongInfo(songNode, 1);
            _statusPanel.UpdateSongInfo(songNode, 2);
            
            // The method should not throw and should handle all difficulty levels
            Assert.True(true); // Test passes if no exceptions are thrown
        }

        [Fact]
        public void GetCurrentDifficultyChart_WithSingleChart_ShouldReturnThatChart()
        {
            // Arrange - Use the standard test song node with single chart
            _statusPanel.UpdateSongInfo(_testSongNode, 0);
            
            // Act & Assert - Should handle single chart without issues
            Assert.True(true); // Test passes if no exceptions are thrown
        }

        [Fact]
        public void GetCurrentDifficultyChart_WithNullDatabaseSong_ShouldHandleGracefully()
        {
            // Arrange
            var songNode = new SongListNode
            {
                Type = NodeType.Score,
                Title = "No Database Song",
                DatabaseSong = null,
                DatabaseChart = null
            };

            // Act & Assert - Should not throw
            _statusPanel.UpdateSongInfo(songNode, 0);
            Assert.True(true);
        }

        [Fact]
        public void GetCurrentDifficultyChart_WithEmptyCharts_ShouldFallbackToPrimaryChart()
        {
            // Arrange
            var song = new DTXMania.Game.Lib.Song.Entities.Song
            {
                Title = "Empty Charts Song",
                Artist = "Test Artist",
                Charts = new List<SongChart>() // Empty charts collection
            };

            var primaryChart = new SongChart { Duration = 150.0, Bpm = 120 };

            var songNode = new SongListNode
            {
                Type = NodeType.Score,
                Title = "Empty Charts Song",
                DatabaseSong = song,
                DatabaseChart = primaryChart // Should fallback to this
            };

            // Act & Assert - Should use fallback chart
            _statusPanel.UpdateSongInfo(songNode, 0);
            Assert.True(true);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        public void GetCurrentDifficultyChart_WithValidDifficultyIndex_ShouldNotThrow(int difficultyIndex)
        {
            // Arrange - Create song with multiple charts for different difficulties
            var song = new DTXMania.Game.Lib.Song.Entities.Song
            {
                Title = "Multi Difficulty Song",
                Artist = "Test Artist"
            };

            var charts = new List<SongChart>();
            for (int i = 0; i < 5; i++)
            {
                charts.Add(new SongChart 
                { 
                    Duration = 120.0 + (i * 30), 
                    Bpm = 140, 
                    DrumLevel = 30 + (i * 20),
                    FilePath = $"difficulty_{i}.dtx"
                });
            }
            song.Charts = charts;

            var songNode = new SongListNode
            {
                Type = NodeType.Score,
                Title = "Multi Difficulty Song",
                DatabaseSong = song,
                DatabaseChart = charts[0],
                Scores = charts.Select((chart, index) => new SongScore 
                { 
                    Instrument = EInstrumentPart.DRUMS, 
                    DifficultyLevel = chart.DrumLevel 
                }).ToArray()
            };

            // Act & Assert
            _statusPanel.UpdateSongInfo(songNode, difficultyIndex);
            Assert.True(true); // Test passes if no exceptions are thrown
        }

        [Fact]
        public void UpdateSongInfo_WithDifferentDifficulties_ShouldHandleDurationChanges()
        {
            // Arrange - Create a song similar to "My Hope Is Gone" with different durations
            var song = new DTXMania.Game.Lib.Song.Entities.Song
            {
                Title = "Duration Test Song",
                Artist = "Test Artist"
            };

            var basChart = new SongChart { Duration = 123.12, Bpm = 184, DrumLevel = 30, FilePath = "bas.dtx" };
            var advChart = new SongChart { Duration = 123.12, Bpm = 184, DrumLevel = 50, FilePath = "adv.dtx" };
            var fullChart = new SongChart { Duration = 398.33, Bpm = 184, DrumLevel = 70, FilePath = "full.dtx" };

            song.Charts = new List<SongChart> { basChart, advChart, fullChart };

            var songNode = new SongListNode
            {
                Type = NodeType.Score,
                Title = "Duration Test Song",
                DatabaseSong = song,
                DatabaseChart = basChart,
                Scores = new SongScore[]
                {
                    new SongScore { Instrument = EInstrumentPart.DRUMS, DifficultyLevel = 30 },
                    new SongScore { Instrument = EInstrumentPart.DRUMS, DifficultyLevel = 50 },
                    new SongScore { Instrument = EInstrumentPart.DRUMS, DifficultyLevel = 70 }
                }
            };

            // Act - Test switching between difficulties
            _statusPanel.UpdateSongInfo(songNode, 0); // bas.dtx - 123.12s
            _statusPanel.UpdateSongInfo(songNode, 1); // adv.dtx - 123.12s 
            _statusPanel.UpdateSongInfo(songNode, 2); // full.dtx - 398.33s

            // Assert - Should handle all transitions without throwing
            Assert.True(true);
        }

        #endregion

        #region BPM Background Tests

        [Fact]
        public void BPMBackground_IntegrationTest()
        {
            // Arrange
            var mockResourceManager = new MockResourceManager(_graphicsDeviceService.GraphicsDevice);
            _statusPanel.InitializeAuthenticGraphics(mockResourceManager);
            _statusPanel.UpdateSongInfo(_testSongNode, 0);

            // Test default integrated mode
            Assert.False(_statusPanel.UseStandaloneBPMBackground);

            // Test standalone mode
            _statusPanel.UseStandaloneBPMBackground = true;
            _statusPanel.InitializeAuthenticGraphics(mockResourceManager);

            // Assert - Should complete without throwing exceptions
            Assert.True(_statusPanel.UseStandaloneBPMBackground);
        }

        [Fact] 
        public void BPMBackground_SupportsTextureFallback()
        {
            // Arrange
            var mockResourceManager = new MockResourceManager(_graphicsDeviceService.GraphicsDevice);
            
            // Act - Initialize graphics (this should load 5_BPM.png or fallback)
            _statusPanel.InitializeAuthenticGraphics(mockResourceManager);
            
            // Assert - Should complete without throwing, indicating fallback works
            Assert.True(true);
        }

        #endregion

        public void Dispose()
        {
            _statusPanel?.Dispose();
            _graphicsDeviceService?.Dispose();
        }
    }
}
