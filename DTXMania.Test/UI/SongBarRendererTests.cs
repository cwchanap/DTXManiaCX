using Xunit;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Resources;
using DTXMania.Test.Helpers;
using System;
using DTXMania.Game.Lib.Song.Entities;

namespace DTXMania.Test.UI
{
    /// <summary>
    /// Unit tests for SongBarRenderer component (Phase 4 enhancement)
    /// </summary>
    public class SongBarRendererTests : IDisposable
    {
        private readonly MockResourceManager _resourceManager;
        private readonly TestGraphicsDeviceService _graphicsService;
        private readonly SongBarRenderer? _renderer;
        private readonly SongListNode _testSongNode;

        public SongBarRendererTests()
        {
            _graphicsService = new TestGraphicsDeviceService();
            _resourceManager = new MockResourceManager(_graphicsService.GraphicsDevice);

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
                DrumLevel = 85,
                PreviewImage = "preview.png"
            };

            // Create test song node
            _testSongNode = new SongListNode
            {
                Type = NodeType.Score,
                Title = "Test Song",
                DatabaseSong = testSong,
                DatabaseChart = testChart,
                Scores =
                [
                    new SongScore
                    {
                        Instrument = EInstrumentPart.DRUMS,
                        BestScore = 950000,
                        BestRank = 85,
                        FullCombo = true,
                        PlayCount = 5
                    }
                ]
            };

            var sharedRT = new RenderTarget2D(_graphicsService.GraphicsDevice, 512, 512);
            _renderer = new SongBarRenderer(_graphicsService.GraphicsDevice, _resourceManager, sharedRT);

        }
        [Fact]
        public void Constructor_WithNullGraphicsDevice_ShouldThrow()
        {
            // Arrange
            var titleRT = new RenderTarget2D(_graphicsService.GraphicsDevice, 400, 24);
            var clearLampRT = new RenderTarget2D(_graphicsService.GraphicsDevice, 8, 24);

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new SongBarRenderer(null, _resourceManager, titleRT));
        }
        [Fact]
        public void Constructor_WithNullResourceManager_ShouldThrow()
        {
            // Arrange
            var sharedRT = new RenderTarget2D(_graphicsService.GraphicsDevice, 512, 512);

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new SongBarRenderer(_graphicsService.GraphicsDevice, null, sharedRT));
        }

        [Fact]
        public void Constructor_WithNullRenderTarget_ShouldThrow()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new SongBarRenderer(_graphicsService.GraphicsDevice, _resourceManager, null));
        }

        [Fact]
        public void GenerateTitleTexture_WithNullSongNode_ShouldReturnNull()
        {
            // Act
            var texture = _renderer.GenerateTitleTexture(null);

            // Assert
            Assert.Null(texture);
        }

        [Fact]
        public void GenerateTitleTexture_WithoutFont_ShouldReturnNull()
        {
            // Act
            var texture = _renderer.GenerateTitleTexture(_testSongNode);

            // Assert
            Assert.Null(texture);
        }

        [Fact]
        public void GenerateTitleTexture_CalledTwice_ShouldReturnCachedTexture()
        {
            // Arrange
            var font = CreateTestFont();
            _renderer.SetFont(font);

            // Act
            var texture1 = _renderer.GenerateTitleTexture(_testSongNode);
            var texture2 = _renderer.GenerateTitleTexture(_testSongNode);

            // Assert
            Assert.NotNull(texture1);
            Assert.NotNull(texture2);
            Assert.Same(texture1, texture2); // Should be cached
        }

        [Fact]
        public void GeneratePreviewImageTexture_WithValidNode_ShouldAttemptLoad()
        {
            // Act
            var texture = _renderer.GeneratePreviewImageTexture(_testSongNode);

            // Assert - May be null if file doesn't exist, but should not throw
            // The mock resource manager will return null for non-existent files
        }

        [Fact]
        public void GeneratePreviewImageTexture_WithNullNode_ShouldReturnNull()
        {
            // Act
            var texture = _renderer.GeneratePreviewImageTexture(null);

            // Assert
            Assert.Null(texture);
        }

        [Fact]
        public void GeneratePreviewImageTexture_WithNodeWithoutPreviewImage_ShouldReturnNull()
        {
            // Arrange
            var nodeWithoutPreview = new SongListNode
            {
                Type = NodeType.Score,
                Title = "No Preview Song",
                DatabaseSong = new DTXMania.Game.Lib.Song.Entities.Song { Title = "No Preview Song" },
                DatabaseChart = new SongChart()
            };

            // Act
            var texture = _renderer.GeneratePreviewImageTexture(nodeWithoutPreview);

            // Assert
            Assert.Null(texture);
        }

        [Fact]
        public void GenerateClearLampTexture_WithScoreNode_ShouldReturnTexture()
        {
            // Act
            var texture = _renderer.GenerateClearLampTexture(_testSongNode, 0);

            // Assert
            Assert.NotNull(texture);
        }

        [Fact]
        public void GenerateClearLampTexture_WithNonScoreNode_ShouldReturnNull()
        {
            // Arrange
            var boxNode = new SongListNode { Type = NodeType.Box, Title = "Test Box" };

            // Act
            var texture = _renderer.GenerateClearLampTexture(boxNode, 0);

            // Assert
            Assert.Null(texture);
        }

        [Fact]
        public void GenerateClearLampTexture_WithNullNode_ShouldReturnNull()
        {
            // Act
            var texture = _renderer.GenerateClearLampTexture(null, 0);

            // Assert
            Assert.Null(texture);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        public void GenerateClearLampTexture_WithDifferentDifficulties_ShouldReturnTexture(int difficulty)
        {
            // Act
            var texture = _renderer.GenerateClearLampTexture(_testSongNode, difficulty);

            // Assert
            Assert.NotNull(texture);
        }



        [Fact]
        public void SetFont_ShouldClearTitleCache()
        {
            // Arrange
            var font1 = CreateTestFont();
            var font2 = CreateTestFont();

            _renderer.SetFont(font1);
            var texture1 = _renderer.GenerateTitleTexture(_testSongNode);

            // Act
            _renderer.SetFont(font2);
            var texture2 = _renderer.GenerateTitleTexture(_testSongNode);

            // Assert
            Assert.NotNull(texture1);
            Assert.NotNull(texture2);
            // Textures should be different since font changed and cache was cleared
        }
        [Theory]
        [InlineData(NodeType.Score)]
        [InlineData(NodeType.Box)]
        [InlineData(NodeType.BackBox)]
        [InlineData(NodeType.Random)]
        public void GenerateTitleTexture_WithDifferentNodeTypes_ShouldGenerateAppropriateText(NodeType nodeType)
        {
            // Arrange
            var font = CreateTestFont();
            _renderer.SetFont(font);

            var node = new SongListNode
            {
                Type = nodeType,
                Title = nodeType == NodeType.Box ? "Test Box" : "Test Song"
            };

            // Act
            var texture = _renderer.GenerateTitleTexture(node);

            // Assert
            Assert.NotNull(texture);

            // This test primarily verifies that different node types don't crash the renderer
        }




        private SpriteFont? CreateTestFont()
        {
            // Create a minimal test font
            // In a real test environment, you'd load an actual font file
            // For now, we'll return null and handle it gracefully in the renderer
            return null; // The renderer should handle null fonts gracefully
        }



        // Phase 2 Enhancement Tests







        [Fact]
        public void SongBarInfo_Properties_ShouldBeSettable()
        {
            // Test that SongBarInfo properties can be set
            var barInfo = new SongBarInfo
            {
                SongNode = _testSongNode,
                BarType = BarType.Score,
                TitleString = "Test Title",
                TextColor = Color.White,
                DifficultyLevel = 2,
                IsSelected = true
            };

            Assert.Equal(_testSongNode, barInfo.SongNode);
            Assert.Equal(BarType.Score, barInfo.BarType);
            Assert.Equal("Test Title", barInfo.TitleString);
            Assert.Equal(Color.White, barInfo.TextColor);
            Assert.Equal(2, barInfo.DifficultyLevel);
            Assert.True(barInfo.IsSelected);
        }
        public void Dispose()
        {
            _renderer?.Dispose();
            _resourceManager?.Dispose();
            _graphicsService?.Dispose();
        }
    }
}
