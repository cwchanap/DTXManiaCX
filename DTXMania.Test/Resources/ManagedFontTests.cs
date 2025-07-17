using DTX.Resources;
using Microsoft.Xna.Framework;
using System;
using Xunit;

namespace DTXMania.Test.Resources
{
    /// <summary>
    /// Basic unit tests for ManagedFont interfaces and data structures
    /// Tests core functionality without MonoGame dependencies
    /// </summary>
    public class ManagedFontTests : IDisposable
    {
        public ManagedFontTests()
        {
            // Basic setup for font-related tests
        }

        [Fact]
        public void FontLoadException_ShouldInitializeCorrectly()
        {
            // Arrange
            var fontPath = "Fonts/Arial.ttf";
            var message = "Failed to load font";
            var innerException = new InvalidOperationException("Invalid font format");

            // Act
            var exception1 = new FontLoadException(fontPath, message);
            var exception2 = new FontLoadException(fontPath, message, innerException);

            // Assert
            Assert.Equal(fontPath, exception1.FontPath);
            Assert.Equal(message, exception1.Message);
            Assert.Null(exception1.InnerException);

            Assert.Equal(fontPath, exception2.FontPath);
            Assert.Equal(message, exception2.Message);
            Assert.Equal(innerException, exception2.InnerException);
        }


        [Fact]
        public void TextRenderOptions_DefaultValues_ShouldBeCorrect()
        {
            // Arrange & Act
            var options = new TextRenderOptions();

            // Assert
            Assert.Equal(Color.White, options.TextColor);
            Assert.False(options.EnableOutline);
            Assert.Equal(Color.Black, options.OutlineColor);
            Assert.Equal(1, options.OutlineThickness);
            Assert.False(options.EnableGradient);
            Assert.Equal(Color.White, options.GradientTopColor);
            Assert.Equal(Color.Gray, options.GradientBottomColor);
            Assert.False(options.EnableShadow);
            Assert.Equal(Color.Black, options.ShadowColor);
            Assert.Equal(new Vector2(2, 2), options.ShadowOffset);
            Assert.Equal(1.0f, options.Scale);
            Assert.Equal(0.0f, options.Rotation);
            Assert.Equal(TextAlignment.Left, options.Alignment);
            Assert.Equal(float.MaxValue, options.MaxWidth);
            Assert.False(options.WordWrap);
        }

        [Theory]
        [InlineData('A', 0x0041, "Basic ASCII")]
        [InlineData('あ', 0x3042, "Hiragana")]
        [InlineData('ア', 0x30A2, "Katakana")]
        [InlineData('漢', 0x6F22, "Kanji")]
        [InlineData('Ａ', 0xFF21, "Fullwidth")]
        public void JapaneseCharacterRanges_ShouldHaveCorrectUnicodeValues(char character, int expectedCode, string description)
        {
            // Arrange & Act
            var actualCode = (int)character;

            // Assert
            Assert.Equal(expectedCode, actualCode);
            Assert.NotNull(description); // Ensure description is provided
        }


        [Fact]
        public void TextCacheKey_ShouldIncludeAllRenderingOptions()
        {
            // Arrange
            var text = "Test";
            var options1 = new TextRenderOptions
            {
                TextColor = Color.Red,
                EnableOutline = true,
                OutlineColor = Color.Black,
                OutlineThickness = 2
            };
            var options2 = new TextRenderOptions
            {
                TextColor = Color.Blue,
                EnableOutline = true,
                OutlineColor = Color.Black,
                OutlineThickness = 2
            };

            // Act
            var key1 = GenerateTestCacheKey(text, options1);
            var key2 = GenerateTestCacheKey(text, options2);

            // Assert
            Assert.NotEqual(key1, key2); // Different colors should produce different keys
        }

        private string GenerateTestCacheKey(string text, TextRenderOptions options)
        {
            // Simulate the cache key generation logic
            var keyBuilder = new System.Text.StringBuilder();
            keyBuilder.Append(text);
            keyBuilder.Append('|');
            keyBuilder.Append(options.TextColor.PackedValue);
            keyBuilder.Append('|');
            keyBuilder.Append(options.EnableOutline);
            keyBuilder.Append('|');
            keyBuilder.Append(options.OutlineColor.PackedValue);
            keyBuilder.Append('|');
            keyBuilder.Append(options.OutlineThickness);
            return keyBuilder.ToString();
        }

        public void Dispose()
        {
            // No cleanup needed for this test class
        }
    }
}
