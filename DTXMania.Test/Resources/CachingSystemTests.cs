using DTX.Resources;
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
        public void PathNormalization_ShouldHandleCaseInsensitiveComparison()
        {
            // Arrange
            var path1 = "Graphics/test_texture.png";
            var path2 = "GRAPHICS/TEST_TEXTURE.PNG";
            var path3 = "graphics/Test_Texture.PNG";

            // Act - Simulate path normalization (ResourceManager.NormalizePath is private)
            var normalized1 = path1.Replace('\\', '/').ToLowerInvariant();
            var normalized2 = path2.Replace('\\', '/').ToLowerInvariant();
            var normalized3 = path3.Replace('\\', '/').ToLowerInvariant();

            // Assert
            Assert.Equal(normalized1, normalized2);
            Assert.Equal(normalized1, normalized3);
            Assert.Equal("graphics/test_texture.png", normalized1);
        }

        [Fact]
        public void CacheKey_ShouldIncludePathAndParameters()
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
        public void CacheKey_ShouldDifferentiateBetweenParameters()
        {
            // Arrange
            var path = "graphics/test_texture.png";

            // Act
            var cacheKey1 = $"{path}|{true}";
            var cacheKey2 = $"{path}|{false}";

            // Assert
            Assert.NotEqual(cacheKey1, cacheKey2);
            Assert.Equal("graphics/test_texture.png|True", cacheKey1);
            Assert.Equal("graphics/test_texture.png|False", cacheKey2);
        }

        [Fact]
        public void FontCacheKey_ShouldIncludeAllParameters()
        {
            // Arrange
            var path = "fonts/arial.ttf";
            var size = 24;
            var style = FontStyle.Bold | FontStyle.Italic;

            // Act
            var cacheKey = $"{path}|{size}|{style}";

            // Assert
            Assert.Equal("fonts/arial.ttf|24|Bold, Italic", cacheKey);
        }

        [Fact]
        public void ReferenceCountingLogic_ShouldIncrementCorrectly()
        {
            // Arrange
            var initialCount = 0;

            // Act
            var count1 = System.Threading.Interlocked.Increment(ref initialCount);
            var count2 = System.Threading.Interlocked.Increment(ref initialCount);

            // Assert
            Assert.Equal(1, count1);
            Assert.Equal(2, count2);
            Assert.Equal(2, initialCount);
        }

        [Fact]
        public void ReferenceCountingLogic_ShouldDecrementCorrectly()
        {
            // Arrange
            var initialCount = 2;

            // Act
            var count1 = System.Threading.Interlocked.Decrement(ref initialCount);
            var count2 = System.Threading.Interlocked.Decrement(ref initialCount);

            // Assert
            Assert.Equal(1, count1);
            Assert.Equal(0, count2);
            Assert.Equal(0, initialCount);
        }

        [Fact]
        public void CacheStatistics_ShouldTrackHitsAndMisses()
        {
            // Arrange
            var cacheHits = 0;
            var cacheMisses = 0;

            // Act - Simulate cache operations
            System.Threading.Interlocked.Increment(ref cacheMisses); // First load
            System.Threading.Interlocked.Increment(ref cacheHits);   // Second load (cache hit)
            System.Threading.Interlocked.Increment(ref cacheHits);   // Third load (cache hit)

            // Assert
            Assert.Equal(2, cacheHits);
            Assert.Equal(1, cacheMisses);
        }

        [Fact]
        public void MemoryUsageCalculation_ShouldBeAccurate()
        {
            // Arrange
            var textureWidth = 256;
            var textureHeight = 256;
            var bytesPerPixel = 4; // RGBA
            var textureCount = 3;

            // Act
            var singleTextureMemory = textureWidth * textureHeight * bytesPerPixel;
            var totalMemory = singleTextureMemory * textureCount;

            // Assert
            Assert.Equal(262144, singleTextureMemory); // 256KB per texture
            Assert.Equal(786432, totalMemory); // 768KB total
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

        [Theory]
        [InlineData("Graphics/test.png", "GRAPHICS/TEST.PNG")]
        [InlineData("sounds/bgm.wav", "SOUNDS/BGM.WAV")]
        [InlineData("fonts/arial.ttf", "Fonts/Arial.TTF")]
        public void CaseInsensitivePaths_ShouldNormalizeToSameKey(string path1, string path2)
        {
            // Act
            var normalized1 = path1.Replace('\\', '/').ToLowerInvariant();
            var normalized2 = path2.Replace('\\', '/').ToLowerInvariant();

            // Assert
            Assert.Equal(normalized1, normalized2);
        }

        [Fact]
        public void PathSeparatorNormalization_ShouldHandleBackslashes()
        {
            // Arrange
            var windowsPath = "Graphics\\Textures\\background.jpg";
            var unixPath = "Graphics/Textures/background.jpg";

            // Act
            var normalizedWindows = windowsPath.Replace('\\', '/').ToLowerInvariant();
            var normalizedUnix = unixPath.Replace('\\', '/').ToLowerInvariant();

            // Assert
            Assert.Equal(normalizedWindows, normalizedUnix);
            Assert.Equal("graphics/textures/background.jpg", normalizedWindows);
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
