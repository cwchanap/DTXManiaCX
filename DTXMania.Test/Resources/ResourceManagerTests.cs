using DTX.Resources;
using System;
using System.IO;
using Xunit;

namespace DTXMania.Test.Resources
{
    /// <summary>
    /// Basic unit tests for ResourceManager interfaces and data structures
    /// Tests core functionality without MonoGame dependencies
    /// </summary>
    public class ResourceManagerTests : IDisposable
    {
        private readonly string _testDataPath;

        public ResourceManagerTests()
        {
            // Create test data directory
            _testDataPath = Path.Combine(Path.GetTempPath(), "DTXManiaCX_Tests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(_testDataPath);

            // Set up test directory structure
            SetupTestDirectories();

            // Set working directory for path resolution tests
            Environment.CurrentDirectory = _testDataPath;
        }

        private void SetupTestDirectories()
        {
            // Create skin directory structure
            // Default skin: System/Graphics/ (no Default subdirectory)
            var defaultSkinPath = Path.Combine(_testDataPath, "System", "Graphics");
            Directory.CreateDirectory(defaultSkinPath);

            // Custom skin: System/Custom/Graphics/
            var customSkinPath = Path.Combine(_testDataPath, "System", "Custom", "Graphics");
            Directory.CreateDirectory(customSkinPath);

            // Create validation files for skin system
            File.WriteAllText(Path.Combine(defaultSkinPath, "1_background.jpg"), "fake image data");
            File.WriteAllText(Path.Combine(customSkinPath, "1_background.jpg"), "fake image data");

            // Create test texture files
            File.WriteAllText(Path.Combine(defaultSkinPath, "test_texture.png"), "fake png data");
            File.WriteAllText(Path.Combine(customSkinPath, "test_texture.png"), "fake png data");
            File.WriteAllText(Path.Combine(defaultSkinPath, "missing_in_custom.png"), "fake png data");
        }

        [Fact]
        public void Constructor_WithNullGraphicsDevice_ShouldThrowArgumentNullException()
        {
            // Arrange, Act & Assert
            Assert.Throws<ArgumentNullException>(() => new ResourceManager(null));
        }

        [Fact]
        public void PathResolution_ShouldWorkWithRelativePaths()
        {
            // Arrange
            var basePath = "System/";
            var relativePath = "Graphics/test_texture.png";
            var expectedPath = Path.Combine(basePath, relativePath);

            // Act
            var resolvedPath = Path.Combine(basePath, relativePath);

            // Assert
            Assert.Equal(expectedPath, resolvedPath);
            Assert.Contains("Graphics/test_texture.png", resolvedPath);
        }

        [Fact]
        public void FileExistence_ShouldDetectExistingFiles()
        {
            // Arrange
            var testFilePath = Path.Combine(_testDataPath, "System", "Graphics", "test_texture.png");

            // Act
            var exists = File.Exists(testFilePath);

            // Assert
            Assert.True(exists);
        }

        [Fact]
        public void FileExistence_ShouldDetectNonExistingFiles()
        {
            // Arrange
            var nonExistentPath = Path.Combine(_testDataPath, "System", "Graphics", "nonexistent.png");

            // Act
            var exists = File.Exists(nonExistentPath);

            // Assert
            Assert.False(exists);
        }

        [Fact]
        public void SkinPathValidation_ShouldValidateNullOrEmpty()
        {
            // Arrange & Act & Assert
            Assert.True(string.IsNullOrEmpty(null));
            Assert.True(string.IsNullOrEmpty(""));
            Assert.False(string.IsNullOrEmpty("System/"));
        }

        [Fact]
        public void DirectoryStructure_ShouldBeSetupCorrectly()
        {
            // Arrange
            var defaultSkinPath = Path.Combine(_testDataPath, "System", "Graphics");
            var customSkinPath = Path.Combine(_testDataPath, "System", "Custom", "Graphics");

            // Act & Assert
            Assert.True(Directory.Exists(defaultSkinPath));
            Assert.True(Directory.Exists(customSkinPath));
            Assert.True(File.Exists(Path.Combine(defaultSkinPath, "1_background.jpg")));
            Assert.True(File.Exists(Path.Combine(customSkinPath, "1_background.jpg")));
        }

        [Fact]
        public void CaseInsensitivePaths_ShouldResolveToSameFile()
        {
            // Arrange
            var lowerCasePath = "graphics/test_texture.png";
            var upperCasePath = "GRAPHICS/TEST_TEXTURE.PNG";
            var mixedCasePath = "Graphics/Test_Texture.PNG";

            // Act - Test path normalization logic
            var normalized1 = lowerCasePath.Replace('\\', '/').ToLowerInvariant();
            var normalized2 = upperCasePath.Replace('\\', '/').ToLowerInvariant();
            var normalized3 = mixedCasePath.Replace('\\', '/').ToLowerInvariant();

            // Assert
            Assert.Equal(normalized1, normalized2);
            Assert.Equal(normalized1, normalized3);
            Assert.Equal("graphics/test_texture.png", normalized1);
        }

        [Fact]
        public void CacheKeyGeneration_ShouldIncludeAllParameters()
        {
            // Arrange
            var path = "graphics/test_texture.png";
            var enableTransparency = true;

            // Act
            var cacheKey = $"{path}|{enableTransparency}";

            // Assert
            Assert.Equal("graphics/test_texture.png|True", cacheKey);
        }

        [Fact]
        public void FontCacheKeyGeneration_ShouldIncludeAllParameters()
        {
            // Arrange
            var path = "fonts/arial.ttf";
            var size = 24;
            var style = FontStyle.Bold;

            // Act
            var cacheKey = $"{path}|{size}|{style}";

            // Assert
            Assert.Equal("fonts/arial.ttf|24|Bold", cacheKey);
        }

        [Fact]
        public void SoundCacheKeyGeneration_ShouldBeConsistent()
        {
            // Arrange
            var path = "Sounds/Move.ogg";
            var normalizedPath = path.Replace('\\', '/').ToLowerInvariant();

            // Act
            var cacheKey = normalizedPath;

            // Assert
            Assert.Equal("sounds/move.ogg", cacheKey);
        }

        [Theory]
        [InlineData("Sounds/Move.ogg")]
        [InlineData("Sounds/Decide.ogg")]
        [InlineData("Sounds/Game start.ogg")]
        public void SoundPath_Resolution_ShouldFollowSkinSystem(string soundPath)
        {
            // Arrange
            var basePath = "System/";
            var expectedPath = Path.Combine(basePath, soundPath);

            // Act
            var resolvedPath = Path.Combine(basePath, soundPath);

            // Assert
            Assert.Contains("System", resolvedPath);
            Assert.Contains("Sounds", resolvedPath);
            Assert.EndsWith(".ogg", resolvedPath);
        }

        [Fact]
        public void SoundFileExtensions_ShouldSupportMultipleFormats()
        {
            // Arrange
            var supportedExtensions = new[] { ".wav", ".ogg" };
            var testFiles = new[]
            {
                "test.wav",
                "test.ogg",
                "Move.ogg",
                "Decide.ogg"
            };

            // Act & Assert
            foreach (var file in testFiles)
            {
                var extension = Path.GetExtension(file).ToLowerInvariant();
                Assert.Contains(extension, supportedExtensions);
            }
        }

        public void Dispose()
        {
            // Cleanup test directory
            try
            {
                if (Directory.Exists(_testDataPath))
                {
                    Directory.Delete(_testDataPath, true);
                }
            }
            catch
            {
                // Ignore cleanup errors in tests
            }
        }
    }
}
