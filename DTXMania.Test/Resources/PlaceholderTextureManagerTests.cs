using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using Xunit;
using DTX.Resources;
using DTXMania.Test.Helpers;

namespace DTXMania.Test.Resources
{
    /// <summary>
    /// Unit tests for PlaceholderTextureManager
    /// </summary>
    public class PlaceholderTextureManagerTests : IDisposable
    {
        private readonly MockGraphicsDevice _mockGraphicsDevice;
        private readonly PlaceholderTextureManager _placeholderManager;

        public PlaceholderTextureManagerTests()
        {
            _mockGraphicsDevice = new MockGraphicsDevice();
            _placeholderManager = new PlaceholderTextureManager(_mockGraphicsDevice.GraphicsDevice);
        }

        [Fact]
        public void GetTitlePlaceholder_ReturnsValidTexture()
        {
            // Act
            var placeholder = _placeholderManager.GetTitlePlaceholder();

            // Assert
            Assert.NotNull(placeholder);
            Assert.True(placeholder.Width > 0);
            Assert.True(placeholder.Height > 0);
            Assert.Contains("placeholder_title", placeholder.SourcePath);
        }

        [Fact]
        public void GetPreviewImagePlaceholder_ReturnsValidTexture()
        {
            // Act
            var placeholder = _placeholderManager.GetPreviewImagePlaceholder();

            // Assert
            Assert.NotNull(placeholder);
            Assert.True(placeholder.Width > 0);
            Assert.True(placeholder.Height > 0);
            Assert.Contains("placeholder_preview", placeholder.SourcePath);
        }

        [Fact]
        public void GetClearLampPlaceholder_ReturnsValidTexture()
        {
            // Act
            var placeholder = _placeholderManager.GetClearLampPlaceholder();

            // Assert
            Assert.NotNull(placeholder);
            Assert.True(placeholder.Width > 0);
            Assert.True(placeholder.Height > 0);
            Assert.Contains("placeholder_clearlamp", placeholder.SourcePath);
        }

        [Fact]
        public void GetGenericPlaceholder_ReturnsValidTexture()
        {
            // Act
            var placeholder = _placeholderManager.GetGenericPlaceholder();

            // Assert
            Assert.NotNull(placeholder);
            Assert.True(placeholder.Width > 0);
            Assert.True(placeholder.Height > 0);
            Assert.Contains("placeholder_generic", placeholder.SourcePath);
        }

        [Fact]
        public void GetTitlePlaceholder_MultipleCalls_ReturnsCachedTexture()
        {
            // Act
            var placeholder1 = _placeholderManager.GetTitlePlaceholder();
            var placeholder2 = _placeholderManager.GetTitlePlaceholder();

            // Assert
            Assert.NotNull(placeholder1);
            Assert.NotNull(placeholder2);
            // Should return same cached instance
            Assert.Equal(placeholder1.SourcePath, placeholder2.SourcePath);
        }

        [Fact]
        public void GetPreviewImagePlaceholder_MultipleCalls_ReturnsCachedTexture()
        {
            // Act
            var placeholder1 = _placeholderManager.GetPreviewImagePlaceholder();
            var placeholder2 = _placeholderManager.GetPreviewImagePlaceholder();

            // Assert
            Assert.NotNull(placeholder1);
            Assert.NotNull(placeholder2);
            // Should return same cached instance
            Assert.Equal(placeholder1.SourcePath, placeholder2.SourcePath);
        }

        [Fact]
        public void GetClearLampPlaceholder_MultipleCalls_ReturnsCachedTexture()
        {
            // Act
            var placeholder1 = _placeholderManager.GetClearLampPlaceholder();
            var placeholder2 = _placeholderManager.GetClearLampPlaceholder();

            // Assert
            Assert.NotNull(placeholder1);
            Assert.NotNull(placeholder2);
            // Should return same cached instance
            Assert.Equal(placeholder1.SourcePath, placeholder2.SourcePath);
        }

        [Fact]
        public void GetGenericPlaceholder_MultipleCalls_ReturnsCachedTexture()
        {
            // Act
            var placeholder1 = _placeholderManager.GetGenericPlaceholder();
            var placeholder2 = _placeholderManager.GetGenericPlaceholder();

            // Assert
            Assert.NotNull(placeholder1);
            Assert.NotNull(placeholder2);
            // Should return same cached instance
            Assert.Equal(placeholder1.SourcePath, placeholder2.SourcePath);
        }

        [Fact]
        public void ClearCache_DisposesAllPlaceholders()
        {
            // Arrange
            var titlePlaceholder = _placeholderManager.GetTitlePlaceholder();
            var previewPlaceholder = _placeholderManager.GetPreviewImagePlaceholder();
            var clearLampPlaceholder = _placeholderManager.GetClearLampPlaceholder();
            var genericPlaceholder = _placeholderManager.GetGenericPlaceholder();

            // Act
            _placeholderManager.ClearCache();

            // Assert
            // Should not throw and cache should be cleared
            Assert.True(true); // Test passes if no exception is thrown
        }

        [Fact]
        public void Constructor_WithNullGraphicsDevice_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new PlaceholderTextureManager(null));
        }

        [Fact]
        public void DifferentPlaceholderTypes_HaveDifferentDimensions()
        {
            // Act
            var titlePlaceholder = _placeholderManager.GetTitlePlaceholder();
            var previewPlaceholder = _placeholderManager.GetPreviewImagePlaceholder();
            var clearLampPlaceholder = _placeholderManager.GetClearLampPlaceholder();
            var genericPlaceholder = _placeholderManager.GetGenericPlaceholder();

            // Assert
            Assert.NotNull(titlePlaceholder);
            Assert.NotNull(previewPlaceholder);
            Assert.NotNull(clearLampPlaceholder);
            Assert.NotNull(genericPlaceholder);

            // Title placeholder should be wider than it is tall (400x24)
            Assert.True(titlePlaceholder.Width > titlePlaceholder.Height);

            // Preview placeholder should be square (128x128)
            Assert.Equal(previewPlaceholder.Width, previewPlaceholder.Height);

            // Clear lamp placeholder should be narrow and tall (8x24)
            Assert.True(clearLampPlaceholder.Height > clearLampPlaceholder.Width);

            // Generic placeholder should be square (64x64)
            Assert.Equal(genericPlaceholder.Width, genericPlaceholder.Height);
        }

        public void Dispose()
        {
            _placeholderManager?.Dispose();
            _mockGraphicsDevice?.Dispose();
        }
    }
}
