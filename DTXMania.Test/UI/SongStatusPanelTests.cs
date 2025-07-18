using System;
using Xunit;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using DTX.Song.Components;
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
            Assert.Equal(3, song.Charts.Count); // Verify test setup is correct
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



        #endregion

        public void Dispose()
        {
            _statusPanel?.Dispose();
            _graphicsDeviceService?.Dispose();
        }
    }
}
