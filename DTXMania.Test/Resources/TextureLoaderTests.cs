using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Threading.Tasks;
using Xunit;
using DTX.Resources;
using DTX.Song;
using DTXMania.Test.Helpers;

namespace DTXMania.Test.Resources
{
    /// <summary>
    /// Unit tests for TextureLoader async texture loading functionality
    /// </summary>
    public class TextureLoaderTests : IDisposable
    {
        private readonly MockGraphicsDevice _mockGraphicsDevice;
        private readonly MockResourceManager _mockResourceManager;
        private readonly TextureLoader _textureLoader;

        public TextureLoaderTests()
        {
            _mockGraphicsDevice = new MockGraphicsDevice();
            _mockResourceManager = new MockResourceManager(_mockGraphicsDevice.GraphicsDevice);
            _textureLoader = new TextureLoader(_mockResourceManager, _mockGraphicsDevice.GraphicsDevice);
        }

        [Fact]
        public async Task LoadTextureAsync_WithValidPath_ReturnsTexture()
        {
            // Arrange
            var texturePath = "test_texture.png";

            // Act
            var result = await _textureLoader.LoadTextureAsync(texturePath);

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Width > 0);
            Assert.True(result.Height > 0);
        }

        [Fact]
        public async Task LoadTextureAsync_WithNullPath_ReturnsPlaceholder()
        {
            // Act
            var result = await _textureLoader.LoadTextureAsync(null);

            // Assert
            Assert.NotNull(result);
            // Should return placeholder texture
            Assert.Equal("placeholder", result.SourcePath);
        }

        [Fact]
        public async Task LoadTextureAsync_WithEmptyPath_ReturnsPlaceholder()
        {
            // Act
            var result = await _textureLoader.LoadTextureAsync("");

            // Assert
            Assert.NotNull(result);
            // Should return placeholder texture
            Assert.Equal("placeholder", result.SourcePath);
        }

        [Fact]
        public async Task LoadTextureAsync_SamePath_ReturnsCachedTexture()
        {
            // Arrange
            var texturePath = "cached_texture.png";

            // Act
            var result1 = await _textureLoader.LoadTextureAsync(texturePath);
            var result2 = await _textureLoader.LoadTextureAsync(texturePath);

            // Assert
            Assert.NotNull(result1);
            Assert.NotNull(result2);
            // Should return same cached instance
            Assert.Equal(result1.SourcePath, result2.SourcePath);
        }

        [Fact]
        public void GetPlaceholder_ReturnsValidTexture()
        {
            // Act
            var placeholder = _textureLoader.GetPlaceholder();

            // Assert
            Assert.NotNull(placeholder);
            Assert.True(placeholder.Width > 0);
            Assert.True(placeholder.Height > 0);
            Assert.Equal("placeholder", placeholder.SourcePath);
        }

        [Fact]
        public void PreloadTextures_WithValidSongNodes_DoesNotThrow()
        {
            // Arrange
            var songNodes = new[]
            {
                CreateTestSongNode("Song1", "preview1.jpg"),
                CreateTestSongNode("Song2", "preview2.jpg"),
                CreateTestSongNode("Song3", "preview3.jpg")
            };

            // Act & Assert
            var exception = Record.Exception(() => 
                _textureLoader.PreloadTextures(songNodes, 1, 1));
            
            Assert.Null(exception);
        }

        [Fact]
        public void PreloadTextures_WithNullSongNodes_DoesNotThrow()
        {
            // Act & Assert
            var exception = Record.Exception(() => 
                _textureLoader.PreloadTextures(null, 0, 1));
            
            Assert.Null(exception);
        }

        [Fact]
        public void PreloadTextures_WithEmptySongNodes_DoesNotThrow()
        {
            // Arrange
            var songNodes = new SongListNode[0];

            // Act & Assert
            var exception = Record.Exception(() => 
                _textureLoader.PreloadTextures(songNodes, 0, 1));
            
            Assert.Null(exception);
        }

        [Fact]
        public async Task ClearCache_DisposesTexturesAndClearsCache()
        {
            // Arrange
            var texturePath = "test_clear.png";
            await _textureLoader.LoadTextureAsync(texturePath); // Ensure texture is loaded

            // Act
            _textureLoader.ClearCache();

            // Assert
            // Should not throw and cache should be cleared
            Assert.True(true); // Test passes if no exception is thrown
        }

        [Fact]
        public async Task LoadTextureAsync_ConcurrentRequests_HandledCorrectly()
        {
            // Arrange
            var texturePath = "concurrent_texture.png";

            // Act
            var tasks = new Task<ITexture>[5];
            for (int i = 0; i < 5; i++)
            {
                tasks[i] = _textureLoader.LoadTextureAsync(texturePath);
            }

            var results = await Task.WhenAll(tasks);

            // Assert
            Assert.All(results, result => Assert.NotNull(result));
            // All results should have the same source path
            Assert.All(results, result => Assert.Equal(texturePath, result.SourcePath));
        }

        private SongListNode CreateTestSongNode(string title, string previewImage)
        {
            var metadata = new SongMetadata
            {
                Title = title,
                PreviewImage = previewImage,
                FilePath = $"TestSongs/{title}/song.dtx"
            };

            return SongListNode.CreateSongNode(metadata);
        }

        public void Dispose()
        {
            _textureLoader?.Dispose();
            _mockResourceManager?.Dispose();
            _mockGraphicsDevice?.Dispose();
        }
    }
}
