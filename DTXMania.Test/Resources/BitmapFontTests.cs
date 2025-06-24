using Xunit;
using DTX.Resources;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Moq;
using System;

namespace DTXMania.Test.Resources
{
    /// <summary>
    /// Unit tests for BitmapFont class
    /// Tests DTXMania-style bitmap font rendering functionality
    /// </summary>
    public class BitmapFontTests
    {
        #region Test Helpers

        /// <summary>
        /// Creates a test BitmapFont instance without requiring a real GraphicsDevice
        /// Uses the internal testing constructor that allows null GraphicsDevice
        /// </summary>
        private BitmapFont CreateTestBitmapFont(IResourceManager? resourceManager = null)
        {
            var mockResourceManager = resourceManager ?? new Mock<IResourceManager>().Object;
            // Use internal testing constructor that allows null GraphicsDevice
            return new BitmapFont(mockResourceManager, true);
        }

        #endregion

        #region Constructor Tests

        [Fact]
        public void BitmapFont_Constructor_RequiresResourceManager()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new BitmapFont(null, null));
        }

        #endregion

        #region Properties Tests

        [Fact]
        public void BitmapFont_CharacterDimensions_ShouldMatchDTXManiaStandard()
        {
            // Arrange & Act
            using var font = CreateTestBitmapFont();

            // Assert
            Assert.Equal(8, font.CharacterWidth);
            Assert.Equal(16, font.CharacterHeight);
        }

        [Fact]
        public void BitmapFont_IsLoaded_ShouldBeFalseWhenTexturesNotLoaded()
        {
            // Arrange
            var mockResourceManager = new Mock<IResourceManager>();

            // Setup resource manager to return null (texture not found)
            mockResourceManager.Setup(rm => rm.LoadTexture(It.IsAny<string>()))
                              .Returns((ITexture?)null);

            // Act
            using var font = CreateTestBitmapFont(mockResourceManager.Object);

            // Assert
            Assert.False(font.IsLoaded);
        }

        #endregion

        #region Text Measurement Tests

        [Fact]
        public void MeasureText_EmptyString_ShouldReturnZero()
        {
            // Arrange
            using var font = CreateTestBitmapFont();

            // Act
            var result = font.MeasureText("");

            // Assert
            Assert.Equal(Vector2.Zero, result);
        }

        [Fact]
        public void MeasureText_NullString_ShouldReturnZero()
        {
            // Arrange
            using var font = CreateTestBitmapFont();

            // Act
            var result = font.MeasureText(null);

            // Assert
            Assert.Equal(Vector2.Zero, result);
        }

        [Fact]
        public void MeasureText_SingleLine_ShouldCalculateCorrectly()
        {
            // Arrange
            using var font = CreateTestBitmapFont();
            var text = "Hello";

            // Act
            var result = font.MeasureText(text);

            // Assert
            Assert.Equal(new Vector2(40, 16), result); // 5 chars * 8 width, 1 line * 16 height
        }

        [Fact]
        public void MeasureText_MultiLine_ShouldCalculateCorrectly()
        {
            // Arrange
            using var font = CreateTestBitmapFont();
            var text = "Hello\nWorld";

            // Act
            var result = font.MeasureText(text);

            // Assert
            Assert.Equal(new Vector2(40, 32), result); // Max 5 chars * 8 width, 2 lines * 16 height
        }

        #endregion

        #region FontType Enum Tests

        [Fact]
        public void FontType_ShouldHaveExpectedValues()
        {
            // Act & Assert
            Assert.Equal(0, (int)BitmapFont.FontType.Normal);
            Assert.Equal(1, (int)BitmapFont.FontType.Thin);
            Assert.Equal(2, (int)BitmapFont.FontType.WhiteThin);
        }

        [Fact]
        public void FontType_AllValuesAreDefined()
        {
            // Act & Assert
            Assert.True(Enum.IsDefined(typeof(BitmapFont.FontType), BitmapFont.FontType.Normal));
            Assert.True(Enum.IsDefined(typeof(BitmapFont.FontType), BitmapFont.FontType.Thin));
            Assert.True(Enum.IsDefined(typeof(BitmapFont.FontType), BitmapFont.FontType.WhiteThin));
        }

        #endregion

        #region Disposal Tests

        [Fact]
        public void BitmapFont_Dispose_ShouldNotThrow()
        {
            // Arrange
            var font = CreateTestBitmapFont();

            // Act & Assert
            font.Dispose(); // Should not throw
        }

        [Fact]
        public void BitmapFont_DisposeMultipleTimes_ShouldNotThrow()
        {
            // Arrange
            var font = CreateTestBitmapFont();

            // Act & Assert
            font.Dispose(); // First dispose
            font.Dispose(); // Second dispose should not throw
        }

        #endregion

        #region Resource Loading Tests

        [Fact]
        public void BitmapFont_Constructor_ShouldAttemptToLoadFontTextures()
        {
            // Arrange
            var mockResourceManager = new Mock<IResourceManager>();

            // Act
            using var font = CreateTestBitmapFont(mockResourceManager.Object);

            // Assert
            mockResourceManager.Verify(rm => rm.LoadTexture("Graphics/Console font 8x16.png"), Times.Once);
            mockResourceManager.Verify(rm => rm.LoadTexture("Graphics/Console font 2 8x16.png"), Times.Once);
        }

        #endregion
    }
}
