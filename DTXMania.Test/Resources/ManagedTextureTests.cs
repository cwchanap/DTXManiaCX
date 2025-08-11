using DTXMania.Game.Lib.Resources;
using Microsoft.Xna.Framework;
using System;
using System.IO;
using Xunit;

namespace DTXMania.Test.Resources
{
    /// <summary>
    /// Basic unit tests for ManagedTexture interfaces and data structures
    /// Tests core functionality without MonoGame dependencies
    /// </summary>
    public class ManagedTextureTests : IDisposable
    {
        private readonly string _testImagePath;

        public ManagedTextureTests()
        {
            // Create a temporary test image file
            _testImagePath = Path.GetTempFileName();
            File.WriteAllBytes(_testImagePath, new byte[] { 0x89, 0x50, 0x4E, 0x47 }); // PNG header
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
            Assert.True(params1.PremultiplyAlpha);
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
                PremultiplyAlpha = false
            };

            // Assert
            Assert.True(params1.EnableTransparency);
            Assert.Equal(Color.Magenta, params1.TransparencyColor);
            Assert.True(params1.GenerateMipmaps);
            Assert.False(params1.PremultiplyAlpha);
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


        public void Dispose()
        {
            // Cleanup test file
            try
            {
                if (File.Exists(_testImagePath))
                {
                    File.Delete(_testImagePath);
                }
            }
            catch
            {
                // Ignore cleanup errors in tests
            }
        }
    }
}
