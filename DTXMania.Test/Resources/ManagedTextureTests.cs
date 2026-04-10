using DTXMania.Game.Lib.Resources;
using DTXMania.Test.TestData;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.IO;
using System.Runtime.CompilerServices;
using Xunit;

namespace DTXMania.Test.Resources
{
    /// <summary>
    /// Basic unit tests for ManagedTexture interfaces and data structures
    /// Tests core functionality without MonoGame dependencies
    /// </summary>
    [Trait("Category", "Unit")]
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

        [Fact]
        public void FilePathConstructor_WhenGraphicsDeviceIsNull_ShouldThrowArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new ManagedTexture(null!, "texture.png", "source"));
        }

        [Fact]
        public void ExistingTextureConstructor_WhenSourcePathIsNull_ShouldFallbackToUnknown()
        {
            var texture = (Texture2D)RuntimeHelpers.GetUninitializedObject(typeof(Texture2D));

            var managedTexture = new ManagedTexture(null!, texture, null!);

            Assert.Same(texture, managedTexture.Texture);
            Assert.Equal("Unknown", managedTexture.SourcePath);
        }

        [Fact]
        public void AddReferenceAndRemoveReference_ShouldTrackReferenceCountWithoutDisposing()
        {
            var managedTexture = CreateManagedTexture();

            managedTexture.AddReference();
            managedTexture.AddReference();
            managedTexture.RemoveReference();

            Assert.Equal(1, managedTexture.ReferenceCount);
            Assert.False(managedTexture.IsDisposed);
        }

        [Fact]
        public void AddReference_WhenDisposed_ShouldThrowObjectDisposedException()
        {
            var managedTexture = CreateManagedTexture(disposed: true);

            Assert.Throws<ObjectDisposedException>(() => managedTexture.AddReference());
        }

        [Fact]
        public void Properties_WhenTextureIsMissing_ShouldUseSafeDefaults()
        {
            var managedTexture = CreateManagedTexture();

            managedTexture.ZAxisRotation = 1.25f;
            managedTexture.ScaleRatio = new Vector3(2f, 3f, 1f);
            managedTexture.AdditiveBlending = true;

            Assert.Equal(0L, managedTexture.MemoryUsage);
            Assert.Equal(1.25f, managedTexture.ZAxisRotation);
            Assert.Equal(new Vector3(2f, 3f, 1f), managedTexture.ScaleRatio);
            Assert.True(managedTexture.AdditiveBlending);
        }

        [Theory]
        [InlineData(999, 255)]
        [InlineData(-5, 0)]
        [InlineData(0, 0)]
        [InlineData(255, 255)]
        [InlineData(128, 128)]
        public void Transparency_ShouldClampToByteRange(int input, int expected)
        {
            var managedTexture = CreateManagedTexture();

            managedTexture.Transparency = input;

            Assert.Equal(expected, managedTexture.Transparency);
        }

        [Fact]
        public void DrawOverloads_WhenTextureIsMissing_ShouldReturnWithoutThrowing()
        {
            var managedTexture = CreateManagedTexture();

            var exception = Record.Exception(() =>
            {
                managedTexture.Draw(null!, Vector2.Zero);
                managedTexture.Draw(null!, Vector2.Zero, null);
                managedTexture.Draw(null!, new Rectangle(0, 0, 10, 10), null, Color.White, 0f, Vector2.Zero, SpriteEffects.None, 0f);
                managedTexture.Draw(null!, Vector2.One, Vector2.One, 0f, Vector2.Zero);
            });

            Assert.Null(exception);
        }

        [Fact]
        public void CloneAndColorOperations_WhenTextureIsMissing_ShouldThrowObjectDisposedException()
        {
            var managedTexture = CreateManagedTexture();
            var tempPath = Path.Combine(Path.GetTempPath(), "texture.png");

            Assert.Throws<ObjectDisposedException>(() => managedTexture.Clone());
            Assert.Throws<ObjectDisposedException>(() => managedTexture.GetColorData());
            Assert.Throws<ObjectDisposedException>(() => managedTexture.SetColorData(Array.Empty<Color>()));
            try
            {
                Assert.Throws<ObjectDisposedException>(() => managedTexture.SaveToFile(tempPath));
            }
            finally
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
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

        private static ManagedTexture CreateManagedTexture(Texture2D? texture = null, bool disposed = false)
        {
            var managedTexture = (ManagedTexture)RuntimeHelpers.GetUninitializedObject(typeof(ManagedTexture));

            ReflectionHelpers.SetPrivateField(managedTexture, "_texture", texture);
            ReflectionHelpers.SetPrivateField(managedTexture, "_sourcePath", "TestTexture");
            ReflectionHelpers.SetPrivateField(managedTexture, "_referenceCount", 0);
            ReflectionHelpers.SetPrivateField(managedTexture, "_disposed", disposed);
            ReflectionHelpers.SetPrivateField(managedTexture, "_lockObject", new object());
            ReflectionHelpers.SetPrivateField(managedTexture, "_transparency", 255);
            ReflectionHelpers.SetPrivateField(managedTexture, "_scaleRatio", Vector3.One);
            ReflectionHelpers.SetPrivateField(managedTexture, "_zAxisRotation", 0f);
            ReflectionHelpers.SetPrivateField(managedTexture, "_additiveBlending", false);

            return managedTexture;
        }
    }
}
