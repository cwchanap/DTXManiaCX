using Xunit;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Resources;
using DTXMania.Test.Helpers;
using System;
using System.Collections.Generic;
using System.IO;
using DTXMania.Game.Lib.Song.Entities;

namespace DTXMania.Test.UI
{
    /// <summary>
    /// Unit tests for SongBarRenderer component (Phase 4 enhancement)
    /// </summary>
    [Collection("GraphicsTests")]
    public class SongBarRendererTests : IDisposable
    {
        private static readonly byte[] OnePixelPng = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

        private readonly MockResourceManager? _resourceManager;
        private readonly TestGraphicsDeviceService _graphicsService;
        private readonly SongBarRenderer? _renderer;
        private readonly List<Texture2D> _fontTextures = new();
        private RenderTarget2D? _sharedRenderTarget;
        private readonly SongListNode _testSongNode;

        public SongBarRendererTests()
        {
            _graphicsService = new TestGraphicsDeviceService();
            try
            {
                if (_graphicsService.GraphicsDevice is { } graphicsDevice)
                {
                    _resourceManager = new MockResourceManager(graphicsDevice);
                    // CreateTitleTexture reads a 1020x76 region, so the shared target must be at
                    // least that large (production passes the 1024x1024 stage target).
                    _sharedRenderTarget = new RenderTarget2D(graphicsDevice, 1024, 1024);
                    _renderer = new SongBarRenderer(graphicsDevice, _resourceManager, _sharedRenderTarget);
                    _renderer.SetFont(CreateTestFont());
                }
            }
            catch
            {
                Dispose();
                throw;
            }

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
                Bpm = 120.0,
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

        }
        [GraphicsFact]
        public void Constructor_WithNullGraphicsDevice_ShouldThrow()
        {
            // Arrange
            var graphicsDevice = _graphicsService.RequiredDevice;

            using var titleRT = new RenderTarget2D(graphicsDevice, 400, 24);

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new SongBarRenderer(null, _resourceManager!, titleRT));
        }
        [GraphicsFact]
        public void Constructor_WithNullResourceManager_ShouldThrow()
        {
            // Arrange
            var graphicsDevice = _graphicsService.RequiredDevice;

            using var sharedRT = new RenderTarget2D(graphicsDevice, 512, 512);

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new SongBarRenderer(graphicsDevice, null, sharedRT));
        }

        [GraphicsFact]
        public void Constructor_WithNullRenderTarget_ShouldThrow()
        {
            // Arrange
            var graphicsDevice = _graphicsService.RequiredDevice;

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new SongBarRenderer(graphicsDevice, _resourceManager!, null));
        }

        [GraphicsFact]
        public void GenerateTitleTexture_WithNullSongNode_ShouldReturnNull()
        {
            // Act
            var texture = Renderer.GenerateTitleTexture(null);

            // Assert
            Assert.Null(texture);
        }

        [GraphicsFact]
        public void GenerateTitleTexture_WithoutFont_ShouldReturnNull()
        {
            // Arrange - the fixture renderer has a font, so build one that does not.
            var graphicsDevice = _graphicsService.RequiredDevice;
            using var renderer = new SongBarRenderer(graphicsDevice, _resourceManager!, _sharedRenderTarget!);

            // Act
            var texture = renderer.GenerateTitleTexture(_testSongNode);

            // Assert
            Assert.Null(texture);
        }

        [GraphicsFact]
        public void GenerateTitleTexture_CalledTwice_ShouldReturnCachedTexture()
        {
            // Arrange - the fixture renderer already carries a usable font.

            // Act
            var texture1 = Renderer.GenerateTitleTexture(_testSongNode);
            var texture2 = Renderer.GenerateTitleTexture(_testSongNode);

            // Assert
            Assert.NotNull(texture1);
            Assert.NotNull(texture2);
            Assert.Same(texture1, texture2); // Should be cached
        }

        [GraphicsFact]
        public void GeneratePreviewImageTexture_WithValidNode_ShouldAttemptLoad()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), $"SongBarRendererPreview_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);
            try
            {
                var previewPath = Path.Combine(tempDir, "preview.png");
                File.WriteAllBytes(previewPath, OnePixelPng);

                var node = new SongListNode
                {
                    Type = NodeType.Score,
                    Title = _testSongNode.Title,
                    DatabaseSong = _testSongNode.DatabaseSong,
                    DatabaseChart = new SongChart
                    {
                        FilePath = Path.Combine(tempDir, "test.dtx"),
                        Bpm = 120.0,
                        DrumLevel = 85,
                        PreviewImage = "preview.png"
                    }
                };

                var texture1 = Renderer.GeneratePreviewImageTexture(node);
                var texture2 = Renderer.GeneratePreviewImageTexture(node);

                Assert.NotNull(texture1);
                Assert.Same(texture1, texture2);
                texture1.RemoveReference();
                texture2.RemoveReference();
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, recursive: true);
            }
        }

        [GraphicsFact]
        public void GeneratePreviewImageTexture_WithMissingPreviewFileInSongDirectory_ShouldReturnNull()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), $"SongBarRendererMissingPreview_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);
            try
            {
                var node = new SongListNode
                {
                    Type = NodeType.Score,
                    Title = _testSongNode.Title,
                    DatabaseSong = _testSongNode.DatabaseSong,
                    DatabaseChart = new SongChart
                    {
                        FilePath = Path.Combine(tempDir, "test.dtx"),
                        Bpm = 120.0,
                        DrumLevel = 85,
                        PreviewImage = "missing-preview.png"
                    }
                };

                var texture = Renderer.GeneratePreviewImageTexture(node);

                Assert.Null(texture);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, recursive: true);
            }
        }

        [GraphicsFact]
        public void GeneratePreviewImageTexture_WithNullNode_ShouldReturnNull()
        {
            // Act
            var texture = Renderer.GeneratePreviewImageTexture(null);

            // Assert
            Assert.Null(texture);
        }

        [GraphicsFact]
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
            var texture = Renderer.GeneratePreviewImageTexture(nodeWithoutPreview);

            // Assert
            Assert.Null(texture);
        }

        [GraphicsFact]
        public void GenerateClearLampTexture_WithScoreNode_ShouldReturnTexture()
        {
            // Act
            var texture = Renderer.GenerateClearLampTexture(_testSongNode, 0);

            // Assert
            Assert.NotNull(texture);
        }

        [GraphicsFact]
        public void GenerateClearLampTexture_WithNonScoreNode_ShouldReturnNull()
        {
            // Arrange
            var boxNode = new SongListNode { Type = NodeType.Box, Title = "Test Box" };

            // Act
            var texture = Renderer.GenerateClearLampTexture(boxNode, 0);

            // Assert
            Assert.Null(texture);
        }

        [GraphicsFact]
        public void GenerateClearLampTexture_WithNullNode_ShouldReturnNull()
        {
            // Act
            var texture = Renderer.GenerateClearLampTexture(null, 0);

            // Assert
            Assert.Null(texture);
        }

        [GraphicsTheory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        public void GenerateClearLampTexture_WithDifferentDifficulties_ShouldReturnTexture(int difficulty)
        {
            // Act
            var texture = Renderer.GenerateClearLampTexture(_testSongNode, difficulty);

            // Assert
            Assert.NotNull(texture);
        }



        [GraphicsFact]
        public void SetFont_ShouldClearTitleCache()
        {
            // Arrange - two distinct real fonts.
            var font1 = CreateTestFont(lineSpacing: 30);
            var font2 = CreateTestFont(lineSpacing: 40);

            Renderer.SetFont(font1);
            var texture1 = Renderer.GenerateTitleTexture(_testSongNode);

            // Act
            Renderer.SetFont(font2);
            var texture2 = Renderer.GenerateTitleTexture(_testSongNode);

            // Assert
            Assert.NotNull(texture1);
            Assert.NotNull(texture2);
            Assert.NotSame(texture1, texture2);
            // Textures should be different since font changed and cache was cleared
        }
        [GraphicsTheory]
        [InlineData(NodeType.Score)]
        [InlineData(NodeType.Box)]
        [InlineData(NodeType.BackBox)]
        [InlineData(NodeType.Random)]
        public void GenerateTitleTexture_WithDifferentNodeTypes_ShouldGenerateAppropriateText(NodeType nodeType)
        {
            // Arrange - the fixture renderer already carries a usable font.
            var node = new SongListNode
            {
                Type = nodeType,
                Title = nodeType == NodeType.Box ? "Test Box" : "Test Song"
            };

            // Act
            var texture = Renderer.GenerateTitleTexture(node);

            // Assert
            Assert.NotNull(texture);

            // This test primarily verifies that different node types don't crash the renderer
        }




        private SongBarRenderer Renderer =>
            _renderer ?? throw _graphicsService.CreateMissingDeviceException();

        private SpriteFont CreateTestFont(int lineSpacing = 38)
        {
            var font = TestFontFactory.Create(_graphicsService.RequiredDevice, out var texture, lineSpacing);
            _fontTextures.Add(texture);
            return font;
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
            _sharedRenderTarget?.Dispose();
            _sharedRenderTarget = null;
            foreach (var texture in _fontTextures)
                texture.Dispose();
            _fontTextures.Clear();
            _resourceManager?.Dispose();
            _graphicsService?.Dispose();
        }
    }
}
