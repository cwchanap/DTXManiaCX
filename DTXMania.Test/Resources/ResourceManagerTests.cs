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
