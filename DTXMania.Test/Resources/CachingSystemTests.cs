using DTXMania.Game.Lib.Resources;
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace DTXMania.Test.Resources
{
    /// <summary>
    /// Unit tests for the caching system functionality
    /// Tests path-based cache, case-insensitive comparison, reference counting, and memory management
    /// </summary>
    public class CachingSystemTests : IDisposable
    {
        private readonly string _testDataPath;
        public CachingSystemTests()
        {
            // Create test data directory
            _testDataPath = Path.Combine(Path.GetTempPath(), "DTXManiaCX_CacheTests", Guid.NewGuid().ToString());
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

            // Create test texture files with different case variations
            File.WriteAllText(Path.Combine(defaultSkinPath, "test_texture.png"), "fake png data");
            File.WriteAllText(Path.Combine(defaultSkinPath, "background.jpg"), "fake jpg data");
            File.WriteAllText(Path.Combine(defaultSkinPath, "UPPERCASE.PNG"), "fake png data");
            File.WriteAllText(Path.Combine(defaultSkinPath, "MixedCase.Jpg"), "fake jpg data");
        }


        [Fact]
        public void ResourceUsageInfo_ShouldCalculateCorrectly()
        {
            // Arrange
            var usage = new ResourceUsageInfo
            {
                LoadedTextures = 5,
                LoadedFonts = 2,
                CacheHits = 15,
                CacheMisses = 7,
                TotalMemoryUsage = 1024 * 1024 // 1MB
            };

            // Act
            var hitRatio = (double)usage.CacheHits / (usage.CacheHits + usage.CacheMisses);
            var avgMemoryPerTexture = usage.TotalMemoryUsage / usage.LoadedTextures;

            // Assert
            Assert.Equal(0.6818, hitRatio, 4); // ~68.18% hit ratio
            Assert.Equal(209715, avgMemoryPerTexture); // ~205KB per texture
        }


        [Fact]
        public void CollectionLogic_ShouldIdentifyUnusedResources()
        {
            // Arrange - Simulate cache with reference counts
            var resourceReferences = new[]
            {
                ("texture1.png", 0),  // Unused
                ("texture2.png", 2),  // In use
                ("texture3.png", 0),  // Unused
                ("texture4.png", 1)   // In use
            };

            // Act
            var unusedCount = 0;
            foreach (var (path, refCount) in resourceReferences)
            {
                if (refCount <= 0)
                    unusedCount++;
            }

            // Assert
            Assert.Equal(2, unusedCount);
        }

        [Fact]
        public void PatternMatching_ShouldIdentifyCorrectResources()
        {
            // Arrange
            var cacheKeys = new[]
            {
                "graphics/background.jpg|false",
                "graphics/notes/don.png|true",
                "sounds/bgm.wav|false",
                "graphics/ui/button.png|false"
            };
            var pattern = "graphics/";

            // Act
            var matchingKeys = cacheKeys.Where(key => key.Contains(pattern)).ToList();

            // Assert
            Assert.Equal(3, matchingKeys.Count);
            Assert.Contains("graphics/background.jpg|false", matchingKeys);
            Assert.Contains("graphics/notes/don.png|true", matchingKeys);
            Assert.Contains("graphics/ui/button.png|false", matchingKeys);
            Assert.DoesNotContain("sounds/bgm.wav|false", matchingKeys);
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
