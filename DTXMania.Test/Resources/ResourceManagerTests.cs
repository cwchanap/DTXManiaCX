using DTXMania.Game.Lib.Resources;
using System;
using System.IO;
using System.Reflection;
using Xunit;
using Microsoft.Xna.Framework.Graphics;
using DTXMania.Test.Helpers;

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
        public void SetBoxDefSkinPath_WithRelativePath_ShouldPreserveRelativePath()
        {
            // Arrange - Create a mock graphics device
            using var graphicsDeviceService = new TestGraphicsDeviceService();
            Assert.NotNull(graphicsDeviceService.GraphicsDevice);

            using var resourceManager = new ResourceManager(graphicsDeviceService.GraphicsDevice);
            var relativePath = "songs/mysong/skin";

            // Act
            resourceManager.SetBoxDefSkinPath(relativePath);

            // Assert - Should preserve relative path (normalized separators but still relative)
            var effectivePath = resourceManager.GetCurrentEffectiveSkinPath();
            Assert.False(Path.IsPathRooted(effectivePath),
                "Relative box.def skin paths should be preserved, not rewritten against app data root");
            Assert.Contains("songs", effectivePath.Replace('\\', '/'));
        }

        [Fact]
        public void SetBoxDefSkinPath_WithAbsolutePath_ShouldNormalizeCorrectly()
        {
            // Arrange
            using var graphicsDeviceService = new TestGraphicsDeviceService();
            Assert.NotNull(graphicsDeviceService.GraphicsDevice);

            using var resourceManager = new ResourceManager(graphicsDeviceService.GraphicsDevice);
            var absolutePath = Path.GetFullPath(Path.Combine(_testDataPath, "CustomSkin"));
            Directory.CreateDirectory(absolutePath);

            // Act
            resourceManager.SetBoxDefSkinPath(absolutePath);

            // Assert - Should be normalized and rooted
            var effectivePath = resourceManager.GetCurrentEffectiveSkinPath();
            Assert.True(Path.IsPathRooted(effectivePath), "Absolute paths should remain rooted after normalization");
        }

        [Fact]
        public void SetBoxDefSkinPath_WithEmptyPath_ShouldClearPath()
        {
            // Arrange
            using var graphicsDeviceService = new TestGraphicsDeviceService();
            Assert.NotNull(graphicsDeviceService.GraphicsDevice);

            using var resourceManager = new ResourceManager(graphicsDeviceService.GraphicsDevice);

            var defaultEffectivePath = resourceManager.GetCurrentEffectiveSkinPath();
            resourceManager.SetBoxDefSkinPath("songs/test/skin");

            // Act
            resourceManager.SetBoxDefSkinPath("");

            // Assert
            var effectivePath = resourceManager.GetCurrentEffectiveSkinPath();
            Assert.Equal(defaultEffectivePath, effectivePath);
            Assert.DoesNotContain("songs/test/skin", effectivePath);
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        public void SetBoxDefSkinPath_WithEmptyOrNullPath_ShouldClearPath(string path)
        {
            // Arrange
            using var graphicsDeviceService = new TestGraphicsDeviceService();
            Assert.NotNull(graphicsDeviceService.GraphicsDevice);

            using var resourceManager = new ResourceManager(graphicsDeviceService.GraphicsDevice);

            var defaultEffectivePath = resourceManager.GetCurrentEffectiveSkinPath();
            resourceManager.SetBoxDefSkinPath("songs/test/skin");

            // Act
            resourceManager.SetBoxDefSkinPath(path);

            // Assert
            var effectivePath = resourceManager.GetCurrentEffectiveSkinPath();
            Assert.Equal(defaultEffectivePath, effectivePath);
            Assert.DoesNotContain("songs/test/skin", effectivePath);
        }

        [Fact]
        public void ResourceExists_WhenMissingInCurrentSkinButPresentInFallback_ShouldReturnTrue()
        {
            // Arrange
            using var graphicsDeviceService = new TestGraphicsDeviceService();
            Assert.NotNull(graphicsDeviceService.GraphicsDevice);

            using var resourceManager = new ResourceManager(graphicsDeviceService.GraphicsDevice);

            var currentSkinRoot = Path.Combine(_testDataPath, "System", "Custom");
            var fallbackSkinRoot = Path.Combine(_testDataPath, "System");
            var fallbackOnlyFile = Path.Combine(fallbackSkinRoot, "Graphics", "fallback_only.png");
            File.WriteAllText(fallbackOnlyFile, "fake png data");

            // Ensure file does not exist in current skin
            var currentSkinFile = Path.Combine(currentSkinRoot, "Graphics", "fallback_only.png");
            if (File.Exists(currentSkinFile))
                File.Delete(currentSkinFile);

            resourceManager.SetSkinPath(currentSkinRoot);
            SetPrivateField(resourceManager, "_fallbackSkinPath", EnsureTrailingSeparator(fallbackSkinRoot));

            // Act
            var exists = resourceManager.ResourceExists(Path.Combine("Graphics", "fallback_only.png"));

            // Assert
            Assert.True(exists);
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            field!.SetValue(target, value);
        }

        private static string EnsureTrailingSeparator(string path)
        {
            var fullPath = Path.GetFullPath(path);
            return fullPath.EndsWith(Path.DirectorySeparatorChar.ToString())
                ? fullPath
                : fullPath + Path.DirectorySeparatorChar;
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
