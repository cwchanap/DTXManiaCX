using DTX.Resources;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.IO;
using Xunit;

namespace DTXMania.Test.Resources
{
    /// <summary>
    /// Unit tests for resource interfaces, enums, and exception classes
    /// Tests the contracts and data structures of the resource management system
    /// </summary>
    public class ResourceInterfaceTests
    {
        [Fact]
        public void FontStyle_Enumeration_ShouldSupportBitwiseOperations()
        {
            // Arrange & Act
            var regular = FontStyle.Regular;
            var bold = FontStyle.Bold;
            var italic = FontStyle.Italic;
            var underline = FontStyle.Underline;
            var strikeout = FontStyle.Strikeout;

            var boldItalic = FontStyle.Bold | FontStyle.Italic;
            var allStyles = FontStyle.Bold | FontStyle.Italic | FontStyle.Underline | FontStyle.Strikeout;

            // Assert
            Assert.Equal(0, (int)regular);
            Assert.Equal(1, (int)bold);
            Assert.Equal(2, (int)italic);
            Assert.Equal(4, (int)underline);
            Assert.Equal(8, (int)strikeout);

            Assert.True(boldItalic.HasFlag(FontStyle.Bold));
            Assert.True(boldItalic.HasFlag(FontStyle.Italic));
            Assert.False(boldItalic.HasFlag(FontStyle.Underline));

            Assert.True(allStyles.HasFlag(FontStyle.Bold));
            Assert.True(allStyles.HasFlag(FontStyle.Italic));
            Assert.True(allStyles.HasFlag(FontStyle.Underline));
            Assert.True(allStyles.HasFlag(FontStyle.Strikeout));
        }

        [Fact]
        public void TextAlignment_Enumeration_ShouldHaveCorrectValues()
        {
            // Arrange & Act & Assert
            Assert.Equal(0, (int)TextAlignment.Left);
            Assert.Equal(1, (int)TextAlignment.Center);
            Assert.Equal(2, (int)TextAlignment.Right);
            Assert.Equal(3, (int)TextAlignment.Justify);
        }

        [Fact]
        public void ResourceUsageInfo_ShouldInitializeWithDefaults()
        {
            // Arrange & Act
            var usage = new ResourceUsageInfo();

            // Assert
            Assert.Equal(0, usage.LoadedTextures);
            Assert.Equal(0, usage.LoadedFonts);
            Assert.Equal(0, usage.TotalMemoryUsage);
            Assert.Equal(0, usage.CacheHits);
            Assert.Equal(0, usage.CacheMisses);
            Assert.Equal(TimeSpan.Zero, usage.TotalLoadTime);
        }

        [Fact]
        public void ResourceUsageInfo_ShouldAllowPropertySetting()
        {
            // Arrange
            var usage = new ResourceUsageInfo();
            var expectedTime = TimeSpan.FromSeconds(5);

            // Act
            usage.LoadedTextures = 10;
            usage.LoadedFonts = 5;
            usage.TotalMemoryUsage = 1024 * 1024; // 1MB
            usage.CacheHits = 50;
            usage.CacheMisses = 10;
            usage.TotalLoadTime = expectedTime;

            // Assert
            Assert.Equal(10, usage.LoadedTextures);
            Assert.Equal(5, usage.LoadedFonts);
            Assert.Equal(1024 * 1024, usage.TotalMemoryUsage);
            Assert.Equal(50, usage.CacheHits);
            Assert.Equal(10, usage.CacheMisses);
            Assert.Equal(expectedTime, usage.TotalLoadTime);
        }

        [Fact]
        public void ResourceLoadFailedEventArgs_ShouldInitializeCorrectly()
        {
            // Arrange
            var path = "Graphics/test.png";
            var exception = new Exception("Test exception");
            var errorMessage = "Custom error message";

            // Act
            var eventArgs1 = new ResourceLoadFailedEventArgs(path, exception);
            var eventArgs2 = new ResourceLoadFailedEventArgs(path, exception, errorMessage);

            // Assert
            Assert.Equal(path, eventArgs1.Path);
            Assert.Equal(exception, eventArgs1.Exception);
            Assert.Equal("Test exception", eventArgs1.ErrorMessage);

            Assert.Equal(path, eventArgs2.Path);
            Assert.Equal(exception, eventArgs2.Exception);
            Assert.Equal(errorMessage, eventArgs2.ErrorMessage);
        }

        [Fact]
        public void ResourceLoadFailedEventArgs_WithNullException_ShouldHandleGracefully()
        {
            // Arrange
            var path = "Graphics/test.png";
            var errorMessage = "Custom error";

            // Act
            var eventArgs = new ResourceLoadFailedEventArgs(path, null, errorMessage);

            // Assert
            Assert.Equal(path, eventArgs.Path);
            Assert.Null(eventArgs.Exception);
            Assert.Equal(errorMessage, eventArgs.ErrorMessage);
        }

        [Fact]
        public void SkinChangedEventArgs_ShouldInitializeCorrectly()
        {
            // Arrange
            var oldSkin = "System/Default/";
            var newSkin = "System/Custom/";

            // Act
            var eventArgs = new SkinChangedEventArgs(oldSkin, newSkin);

            // Assert
            Assert.Equal(oldSkin, eventArgs.OldSkinPath);
            Assert.Equal(newSkin, eventArgs.NewSkinPath);
        }

        [Fact]
        public void TextureCreationParams_ShouldHaveCorrectDefaults()
        {
            // Arrange & Act
            var params1 = new TextureCreationParams();

            // Assert
            Assert.False(params1.EnableTransparency);
            Assert.Equal(Color.Black, params1.TransparencyColor);
            Assert.False(params1.GenerateMipmaps);
            Assert.Equal(SurfaceFormat.Color, params1.Format);
            Assert.True(params1.PremultiplyAlpha);
            Assert.Equal(TextureFilter.Linear, params1.Filter);
        }

        [Fact]
        public void TextureCreationParams_ShouldAllowCustomization()
        {
            // Arrange & Act
            var params1 = new TextureCreationParams
            {
                EnableTransparency = true,
                TransparencyColor = Color.Magenta,
                GenerateMipmaps = true,
                Format = SurfaceFormat.Bgra32,
                PremultiplyAlpha = false,
                Filter = TextureFilter.Point
            };

            // Assert
            Assert.True(params1.EnableTransparency);
            Assert.Equal(Color.Magenta, params1.TransparencyColor);
            Assert.True(params1.GenerateMipmaps);
            Assert.Equal(SurfaceFormat.Bgra32, params1.Format);
            Assert.False(params1.PremultiplyAlpha);
            Assert.Equal(TextureFilter.Point, params1.Filter);
        }

        [Fact]
        public void TextRenderOptions_ShouldHaveCorrectDefaults()
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

        [Fact]
        public void TextRenderOptions_ShouldAllowFullCustomization()
        {
            // Arrange & Act
            var options = new TextRenderOptions
            {
                TextColor = Color.Red,
                EnableOutline = true,
                OutlineColor = Color.Blue,
                OutlineThickness = 3,
                EnableGradient = true,
                GradientTopColor = Color.Yellow,
                GradientBottomColor = Color.Orange,
                EnableShadow = true,
                ShadowColor = Color.Gray,
                ShadowOffset = new Vector2(5, 5),
                Scale = 1.5f,
                Rotation = MathHelper.ToRadians(45),
                Alignment = TextAlignment.Center,
                MaxWidth = 200f,
                WordWrap = true
            };

            // Assert
            Assert.Equal(Color.Red, options.TextColor);
            Assert.True(options.EnableOutline);
            Assert.Equal(Color.Blue, options.OutlineColor);
            Assert.Equal(3, options.OutlineThickness);
            Assert.True(options.EnableGradient);
            Assert.Equal(Color.Yellow, options.GradientTopColor);
            Assert.Equal(Color.Orange, options.GradientBottomColor);
            Assert.True(options.EnableShadow);
            Assert.Equal(Color.Gray, options.ShadowColor);
            Assert.Equal(new Vector2(5, 5), options.ShadowOffset);
            Assert.Equal(1.5f, options.Scale);
            Assert.Equal(MathHelper.ToRadians(45), options.Rotation);
            Assert.Equal(TextAlignment.Center, options.Alignment);
            Assert.Equal(200f, options.MaxWidth);
            Assert.True(options.WordWrap);
        }

        [Fact]
        public void TextureLoadException_ShouldInitializeCorrectly()
        {
            // Arrange
            var texturePath = "Graphics/test.png";
            var message = "Failed to load texture";
            var innerException = new FileNotFoundException("File not found");

            // Act
            var exception1 = new TextureLoadException(texturePath, message);
            var exception2 = new TextureLoadException(texturePath, message, innerException);

            // Assert
            Assert.Equal(texturePath, exception1.TexturePath);
            Assert.Equal(message, exception1.Message);
            Assert.Null(exception1.InnerException);

            Assert.Equal(texturePath, exception2.TexturePath);
            Assert.Equal(message, exception2.Message);
            Assert.Equal(innerException, exception2.InnerException);
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
        public void ExceptionInheritance_ShouldBeCorrect()
        {
            // Arrange & Act
            var textureException = new TextureLoadException("test.png", "message");
            var fontException = new FontLoadException("test.ttf", "message");

            // Assert
            Assert.IsAssignableFrom<Exception>(textureException);
            Assert.IsAssignableFrom<Exception>(fontException);
        }

        [Theory]
        [InlineData(FontStyle.Regular)]
        [InlineData(FontStyle.Bold)]
        [InlineData(FontStyle.Italic)]
        [InlineData(FontStyle.Bold | FontStyle.Italic)]
        [InlineData(FontStyle.Underline | FontStyle.Strikeout)]
        public void FontStyle_AllCombinations_ShouldBeValid(FontStyle style)
        {
            // Arrange & Act & Assert
            // All combinations should be valid enum values
            Assert.True(Enum.IsDefined(typeof(FontStyle), (int)style) ||
                       style.ToString().Contains(",") || // Combined flags
                       ((int)style & ((int)style - 1)) != 0); // Power of 2 check for flags
        }

        [Theory]
        [InlineData(TextAlignment.Left)]
        [InlineData(TextAlignment.Center)]
        [InlineData(TextAlignment.Right)]
        [InlineData(TextAlignment.Justify)]
        public void TextAlignment_AllValues_ShouldBeValid(TextAlignment alignment)
        {
            // Arrange & Act & Assert
            Assert.True(Enum.IsDefined(typeof(TextAlignment), alignment));
        }
    }
}
