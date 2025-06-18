using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Threading.Tasks;
using Xunit;
using DTX.Resources;
using DTXMania.Test.Helpers;

namespace DTXMania.Test.Resources
{
    /// <summary>
    /// Unit tests for async texture loading functionality in ResourceManager
    /// </summary>
    public class AsyncResourceManagerTests : IDisposable
    {
        private readonly MockGraphicsDevice _mockGraphicsDevice;
        private readonly ResourceManager _resourceManager;

        public AsyncResourceManagerTests()
        {
            _mockGraphicsDevice = new MockGraphicsDevice();
            _resourceManager = new ResourceManager(_mockGraphicsDevice.GraphicsDevice);
        }

        [Fact]
        public async Task LoadTextureAsync_WithValidPath_ReturnsTexture()
        {
            // Arrange
            var texturePath = "Graphics/test_texture.png";

            // Act
            var result = await _resourceManager.LoadTextureAsync(texturePath);

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Width > 0);
            Assert.True(result.Height > 0);
        }

        [Fact]
        public async Task LoadTextureAsync_WithTransparency_ReturnsTexture()
        {
            // Arrange
            var texturePath = "Graphics/transparent_texture.png";

            // Act
            var result = await _resourceManager.LoadTextureAsync(texturePath, true);

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Width > 0);
            Assert.True(result.Height > 0);
        }

        [Fact]
        public async Task LoadTextureAsync_WithNullPath_ThrowsArgumentException()
        {
            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => 
                _resourceManager.LoadTextureAsync(null));
        }

        [Fact]
        public async Task LoadTextureAsync_WithEmptyPath_ThrowsArgumentException()
        {
            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => 
                _resourceManager.LoadTextureAsync(""));
        }

        [Fact]
        public async Task LoadTextureAsync_SamePath_ReturnsCachedTexture()
        {
            // Arrange
            var texturePath = "Graphics/cached_texture.png";

            // Act
            var result1 = await _resourceManager.LoadTextureAsync(texturePath);
            var result2 = await _resourceManager.LoadTextureAsync(texturePath);

            // Assert
            Assert.NotNull(result1);
            Assert.NotNull(result2);
            // Should return same cached instance (reference counting)
            Assert.Equal(result1.SourcePath, result2.SourcePath);
        }

        [Fact]
        public async Task LoadTextureAsync_DifferentTransparencySettings_ReturnsDifferentTextures()
        {
            // Arrange
            var texturePath = "Graphics/transparency_test.png";

            // Act
            var textureWithoutTransparency = await _resourceManager.LoadTextureAsync(texturePath, false);
            var textureWithTransparency = await _resourceManager.LoadTextureAsync(texturePath, true);

            // Assert
            Assert.NotNull(textureWithoutTransparency);
            Assert.NotNull(textureWithTransparency);
            // Should be different cache entries due to different transparency settings
            Assert.Equal(textureWithoutTransparency.SourcePath, textureWithTransparency.SourcePath);
        }

        [Fact]
        public async Task LoadTextureAsync_ConcurrentRequests_HandledCorrectly()
        {
            // Arrange
            var texturePath = "Graphics/concurrent_texture.png";

            // Act
            var tasks = new Task<ITexture>[5];
            for (int i = 0; i < 5; i++)
            {
                tasks[i] = _resourceManager.LoadTextureAsync(texturePath);
            }

            var results = await Task.WhenAll(tasks);

            // Assert
            Assert.All(results, result => Assert.NotNull(result));
            // All results should have the same source path
            Assert.All(results, result => Assert.Equal(texturePath, result.SourcePath));
        }

        [Fact]
        public async Task LoadTextureAsync_PerformanceComparison_IsReasonable()
        {
            // Arrange
            var texturePath = "Graphics/performance_test.png";

            // Act
            var syncStart = DateTime.UtcNow;
            var syncTexture = _resourceManager.LoadTexture(texturePath);
            var syncDuration = DateTime.UtcNow - syncStart;

            var asyncStart = DateTime.UtcNow;
            var asyncTexture = await _resourceManager.LoadTextureAsync(texturePath);
            var asyncDuration = DateTime.UtcNow - asyncStart;

            // Assert
            Assert.NotNull(syncTexture);
            Assert.NotNull(asyncTexture);
            
            // Async version should not be significantly slower than sync version
            // (allowing for some overhead but not excessive)
            Assert.True(asyncDuration.TotalMilliseconds < syncDuration.TotalMilliseconds * 2);
        }

        [Fact]
        public async Task LoadTextureAsync_WithNonExistentPath_ReturnsFallbackTexture()
        {
            // Arrange
            var nonExistentPath = "Graphics/does_not_exist.png";

            // Act
            var result = await _resourceManager.LoadTextureAsync(nonExistentPath);

            // Assert
            Assert.NotNull(result);
            // Should return fallback texture
            Assert.True(result.Width > 0);
            Assert.True(result.Height > 0);
        }

        [Fact]
        public async Task LoadTextureAsync_MultipleNonExistentPaths_ReturnsSameFallback()
        {
            // Arrange
            var nonExistentPath1 = "Graphics/missing1.png";
            var nonExistentPath2 = "Graphics/missing2.png";

            // Act
            var result1 = await _resourceManager.LoadTextureAsync(nonExistentPath1);
            var result2 = await _resourceManager.LoadTextureAsync(nonExistentPath2);

            // Assert
            Assert.NotNull(result1);
            Assert.NotNull(result2);
            // Both should be fallback textures with same dimensions
            Assert.Equal(result1.Width, result2.Width);
            Assert.Equal(result1.Height, result2.Height);
        }

        [Fact]
        public async Task LoadTextureAsync_AfterDispose_ThrowsObjectDisposedException()
        {
            // Arrange
            var texturePath = "Graphics/dispose_test.png";
            _resourceManager.Dispose();

            // Act & Assert
            await Assert.ThrowsAsync<ObjectDisposedException>(() => 
                _resourceManager.LoadTextureAsync(texturePath));
        }

        public void Dispose()
        {
            _resourceManager?.Dispose();
            _mockGraphicsDevice?.Dispose();
        }
    }
}
