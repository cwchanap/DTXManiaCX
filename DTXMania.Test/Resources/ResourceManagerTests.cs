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
    [Collection("AppPaths")]
    public class ResourceManagerTests : IDisposable
    {
        private readonly string _testDataPath;
        // Saved so Dispose can restore the process CWD before deleting the
        // temp dir. Without this, parallel test classes that call
        // Path.GetFullPath (which reads the CWD) fail with FileNotFoundException
        // when this class's Dispose deletes the dir it set as CWD.
        private readonly string _previousCurrentDirectory;

        public ResourceManagerTests()
        {
            // Create test data directory
            _testDataPath = Path.Combine(Path.GetTempPath(), "DTXManiaCX_Tests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(_testDataPath);

            // Set up test directory structure
            SetupTestDirectories();

            // Set working directory for path resolution tests
            _previousCurrentDirectory = Environment.CurrentDirectory;
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
            if (graphicsDeviceService.GraphicsDevice == null)
                return;

            // Create a validating skin at a relative path so SetBoxDefSkinPath's
            // validation accepts it. Cleanup removes it after the test.
            var relativePath = "songs/mysong/skin";
            Directory.CreateDirectory(Path.Combine(relativePath, "Graphics"));
            File.WriteAllText(Path.Combine(relativePath, "Graphics", "1_background.jpg"), "bg");
            try
            {
                using var resourceManager = new ResourceManager(graphicsDeviceService.GraphicsDevice);

                // Act
                resourceManager.SetBoxDefSkinPath(relativePath);

                // Assert - Should preserve relative path (normalized separators but still relative)
                var effectivePath = resourceManager.GetCurrentEffectiveSkinPath();
                Assert.False(Path.IsPathRooted(effectivePath),
                    "Relative box.def skin paths should be preserved, not rewritten against app data root");
                Assert.Contains("songs", effectivePath.Replace('\\', '/'));
            }
            finally
            {
                if (Directory.Exists("songs"))
                    Directory.Delete("songs", recursive: true);
            }
        }

        [Fact]
        public void SetBoxDefSkinPath_WithAbsolutePath_ShouldNormalizeCorrectly()
        {
            // Arrange
            using var graphicsDeviceService = new TestGraphicsDeviceService();
            if (graphicsDeviceService.GraphicsDevice == null)
                return;

            using var resourceManager = new ResourceManager(graphicsDeviceService.GraphicsDevice);
            var absolutePath = Path.GetFullPath(Path.Combine(_testDataPath, "CustomSkin"));
            Directory.CreateDirectory(Path.Combine(absolutePath, "Graphics"));
            File.WriteAllText(Path.Combine(absolutePath, "Graphics", "1_background.jpg"), "bg");

            // Act
            resourceManager.SetBoxDefSkinPath(absolutePath);

            // Assert - Should be normalized and rooted
            var effectivePath = resourceManager.GetCurrentEffectiveSkinPath();
            Assert.True(Path.IsPathRooted(effectivePath), "Absolute paths should remain rooted after normalization");
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        public void SetBoxDefSkinPath_WithEmptyOrNullPath_ShouldClearPath(string path)
        {
            // Arrange
            using var graphicsDeviceService = new TestGraphicsDeviceService();
            if (graphicsDeviceService.GraphicsDevice == null)
                return;

            using var resourceManager = new ResourceManager(graphicsDeviceService.GraphicsDevice);

            var defaultEffectivePath = resourceManager.GetCurrentEffectiveSkinPath();
            // Use a path that doesn't exist — validation rejects it, so the
            // override is already cleared. Then clearing with empty/null
            // should keep the default effective path.
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
            if (graphicsDeviceService.GraphicsDevice == null)
                return;

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
            // Restore the process CWD before deleting the temp dir so parallel
            // test classes that call Path.GetFullPath don't fail with
            // FileNotFoundException when the dir they (implicitly) read as CWD
            // disappears.
            try { Environment.CurrentDirectory = _previousCurrentDirectory; }
            catch { /* CWD already gone — nothing to restore to */ }

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
