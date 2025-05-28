using System;
using System.IO;
using System.Linq;
using Xunit;
using DTX.Resources;

namespace DTXMania.Test.Resources
{
    public class SkinDiscoveryServiceTests : IDisposable
    {
        private readonly string _testSkinRoot;
        private readonly SkinDiscoveryService _discoveryService;

        public SkinDiscoveryServiceTests()
        {
            _testSkinRoot = Path.Combine(Path.GetTempPath(), "DTXManiaCX_Test_SkinDiscovery", Guid.NewGuid().ToString());
            _discoveryService = new SkinDiscoveryService(_testSkinRoot);
        }

        public void Dispose()
        {
            // Clean up test directories
            if (Directory.Exists(_testSkinRoot))
            {
                try
                {
                    Directory.Delete(_testSkinRoot, true);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        }

        [Fact]
        public void DiscoverSkins_WithNoSkinRoot_ReturnsEmptyList()
        {
            // Arrange
            var nonExistentRoot = Path.Combine(Path.GetTempPath(), "NonExistent");
            var service = new SkinDiscoveryService(nonExistentRoot);

            // Act
            var result = service.DiscoverSkins();

            // Assert
            Assert.Empty(result);
        }

        [Fact]
        public void DiscoverSkins_WithValidSkins_ReturnsCorrectCount()
        {
            // Arrange
            CreateTestSkinWithMetadata("DefaultSkin", "Default Skin", "Test Author", "1.0");
            CreateTestSkin("CustomSkin");
            CreateInvalidTestSkin("InvalidSkin");

            // Act
            var result = _discoveryService.DiscoverSkins();

            // Assert
            Assert.Equal(2, result.Count); // Only valid skins should be returned
            Assert.Contains(result, s => s.Name == "DefaultSkin");
            Assert.Contains(result, s => s.Name == "CustomSkin");
        }

        [Fact]
        public void AnalyzeSkin_WithValidSkin_ReturnsCorrectInfo()
        {
            // Arrange
            var skinPath = CreateTestSkinWithMetadata("TestSkin", "Test Skin Display", "Test Author", "2.0");

            // Act
            var result = _discoveryService.AnalyzeSkin(skinPath);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("TestSkin", result.Name);
            Assert.True(result.IsValid);
            Assert.Empty(result.MissingFiles);
            Assert.True(result.SizeBytes > 0);
        }

        [Fact]
        public void AnalyzeSkin_WithInvalidSkin_ReturnsInvalidInfo()
        {
            // Arrange
            var skinPath = CreateInvalidTestSkin("InvalidSkin");

            // Act
            var result = _discoveryService.AnalyzeSkin(skinPath);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("InvalidSkin", result.Name);
            Assert.False(result.IsValid);
            Assert.NotEmpty(result.MissingFiles);
        }

        [Fact]
        public void AnalyzeSkin_WithNonExistentPath_ReturnsNull()
        {
            // Act
            var result = _discoveryService.AnalyzeSkin("NonExistentPath");

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void ValidateSkin_WithValidSkin_ReturnsTrue()
        {
            // Arrange
            var skinPath = CreateTestSkin("ValidSkin");

            // Act
            var result = _discoveryService.ValidateSkin(skinPath);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void ValidateSkin_WithInvalidSkin_ReturnsFalse()
        {
            // Arrange
            var skinPath = CreateInvalidTestSkin("InvalidSkin");

            // Act
            var result = _discoveryService.ValidateSkin(skinPath);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void GetSkinCompleteness_WithCompleteSkin_Returns100Percent()
        {
            // Arrange
            var skinPath = CreateCompleteSkin("CompleteSkin");

            // Act
            var result = _discoveryService.GetSkinCompleteness(skinPath);

            // Assert
            Assert.Equal(100, result);
        }

        [Fact]
        public void GetSkinCompleteness_WithPartialSkin_ReturnsCorrectPercentage()
        {
            // Arrange
            var skinPath = CreateTestSkin("PartialSkin"); // Only has required files

            // Act
            var result = _discoveryService.GetSkinCompleteness(skinPath);

            // Assert
            Assert.True(result > 0 && result < 100);
        }

        [Fact]
        public void GetSkinCompleteness_WithNonExistentSkin_ReturnsZero()
        {
            // Act
            var result = _discoveryService.GetSkinCompleteness("NonExistentPath");

            // Assert
            Assert.Equal(0, result);
        }

        [Fact]
        public void AnalyzeSkin_WithDefaultSkin_MarksAsDefault()
        {
            // Arrange
            var skinPath = CreateTestSkin("Default");

            // Act
            var result = _discoveryService.AnalyzeSkin(skinPath);

            // Assert
            Assert.NotNull(result);
            Assert.True(result.IsDefault);
        }

        [Fact]
        public void AnalyzeSkin_WithNonDefaultSkin_DoesNotMarkAsDefault()
        {
            // Arrange
            var skinPath = CreateTestSkin("CustomSkin");

            // Act
            var result = _discoveryService.AnalyzeSkin(skinPath);

            // Assert
            Assert.NotNull(result);
            Assert.False(result.IsDefault);
        }

        #region Helper Methods

        private string CreateTestSkin(string skinName)
        {
            var skinPath = Path.Combine(_testSkinRoot, skinName);
            var graphicsPath = Path.Combine(skinPath, "Graphics");
            
            Directory.CreateDirectory(graphicsPath);
            
            // Create required validation files
            File.WriteAllText(Path.Combine(graphicsPath, "1_background.jpg"), "test content");
            File.WriteAllText(Path.Combine(graphicsPath, "2_background.jpg"), "test content");
            
            return skinPath;
        }

        private string CreateTestSkinWithMetadata(string skinName, string displayName, string author, string version)
        {
            var skinPath = CreateTestSkin(skinName);
            
            // Create SkinConfig.ini with metadata
            var configContent = $@"; Skin Configuration
Name={displayName}
Author={author}
Version={version}
Description=Test skin for unit testing
";
            File.WriteAllText(Path.Combine(skinPath, "SkinConfig.ini"), configContent);
            
            return skinPath;
        }

        private string CreateCompleteSkin(string skinName)
        {
            var skinPath = CreateTestSkin(skinName);
            var graphicsPath = Path.Combine(skinPath, "Graphics");
            var soundsPath = Path.Combine(skinPath, "Sounds");
            
            Directory.CreateDirectory(soundsPath);
            
            // Create additional common files
            File.WriteAllText(Path.Combine(graphicsPath, "7_background.jpg"), "test");
            File.WriteAllText(Path.Combine(graphicsPath, "5_background.jpg"), "test");
            File.WriteAllText(Path.Combine(soundsPath, "Decide.ogg"), "test");
            File.WriteAllText(Path.Combine(soundsPath, "Cancel.ogg"), "test");
            File.WriteAllText(Path.Combine(soundsPath, "Move.ogg"), "test");
            
            return skinPath;
        }

        private string CreateInvalidTestSkin(string skinName)
        {
            var skinPath = Path.Combine(_testSkinRoot, skinName);
            Directory.CreateDirectory(skinPath);
            
            // Don't create required files - this makes it invalid
            return skinPath;
        }

        #endregion
    }
}
