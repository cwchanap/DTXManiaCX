using System;
using System.IO;
using Xunit;
using Moq;
using DTX.Resources;

namespace DTXMania.Test.Resources
{
    public class SkinManagerTests : IDisposable
    {
        private readonly Mock<IResourceManager> _mockResourceManager;
        private readonly SkinManager _skinManager;
        private readonly string _testSkinRoot;

        public SkinManagerTests()
        {
            _mockResourceManager = new Mock<IResourceManager>();
            _testSkinRoot = Path.Combine(Path.GetTempPath(), "DTXManiaCX_Test_Skins", Guid.NewGuid().ToString());
            _skinManager = new SkinManager(_mockResourceManager.Object, _testSkinRoot);
        }

        public void Dispose()
        {
            _skinManager?.Dispose();

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
        public void Constructor_WithNullResourceManager_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new SkinManager(null!));
        }

        [Fact]
        public void Constructor_WithValidParameters_InitializesCorrectly()
        {
            // Arrange & Act
            using var skinManager = new SkinManager(_mockResourceManager.Object, "TestSkins/");

            // Assert
            Assert.NotNull(skinManager);
            Assert.Empty(skinManager.AvailableSystemSkins);
        }

        [Fact]
        public void GetSkinName_WithValidPath_ReturnsCorrectName()
        {
            // Arrange
            var skinPath = @"C:\Games\DTXMania\System\MyCustomSkin\";

            // Act
            var result = SkinManager.GetSkinName(skinPath);

            // Assert
            Assert.Equal("MyCustomSkin", result);
        }

        [Fact]
        public void GetSkinName_WithNullOrEmpty_ReturnsNull()
        {
            // Act & Assert
            Assert.Null(SkinManager.GetSkinName(null));
            Assert.Null(SkinManager.GetSkinName(""));
        }

        [Fact]
        public void ValidateSkinPath_WithValidSkin_ReturnsTrue()
        {
            // Arrange
            var skinPath = CreateTestSkin("ValidSkin");

            // Act
            var result = SkinManager.ValidateSkinPath(skinPath);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void ValidateSkinPath_WithInvalidSkin_ReturnsFalse()
        {
            // Arrange
            var skinPath = Path.Combine(_testSkinRoot, "InvalidSkin");
            Directory.CreateDirectory(skinPath);

            // Act
            var result = SkinManager.ValidateSkinPath(skinPath);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void SwitchToSystemSkin_WithValidSkin_ReturnsTrue()
        {
            // Arrange
            CreateTestSkin("TestSkin");
            _skinManager.RefreshAvailableSkins(); // Refresh to find the new skin
            _mockResourceManager.Setup(x => x.SetSkinPath(It.IsAny<string>()));
            _mockResourceManager.Setup(x => x.SetBoxDefSkinPath(""));

            // Act
            var result = _skinManager.SwitchToSystemSkin("TestSkin");

            // Assert
            Assert.True(result);
            _mockResourceManager.Verify(x => x.SetBoxDefSkinPath(""), Times.Once);
            _mockResourceManager.Verify(x => x.SetSkinPath(It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public void SwitchToSystemSkin_WithInvalidSkin_ReturnsFalse()
        {
            // Act
            var result = _skinManager.SwitchToSystemSkin("NonExistentSkin");

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void SetBoxDefSkin_WithValidPath_ReturnsTrue()
        {
            // Arrange
            var boxDefSkinPath = CreateTestSkin("BoxDefSkin");
            _mockResourceManager.Setup(x => x.SetBoxDefSkinPath(It.IsAny<string>()));

            // Act
            var result = _skinManager.SetBoxDefSkin(boxDefSkinPath);

            // Assert
            Assert.True(result);
            _mockResourceManager.Verify(x => x.SetBoxDefSkinPath(boxDefSkinPath), Times.Once);
        }

        [Fact]
        public void SetBoxDefSkin_WithEmptyPath_ClearsBoxDefSkin()
        {
            // Arrange
            _mockResourceManager.Setup(x => x.SetBoxDefSkinPath(""));

            // Act
            var result = _skinManager.SetBoxDefSkin("");

            // Assert
            Assert.True(result);
            _mockResourceManager.Verify(x => x.SetBoxDefSkinPath(""), Times.Once);
        }

        [Fact]
        public void SetBoxDefSkin_WithInvalidPath_ReturnsFalse()
        {
            // Arrange
            var invalidPath = Path.Combine(_testSkinRoot, "InvalidBoxDefSkin");
            Directory.CreateDirectory(invalidPath);

            // Act
            var result = _skinManager.SetBoxDefSkin(invalidPath);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void RefreshAvailableSkins_WithTestSkins_FindsValidSkins()
        {
            // Arrange
            CreateTestSkin("Skin1");
            CreateTestSkin("Skin2");
            CreateInvalidTestSkin("InvalidSkin");

            // Act
            _skinManager.RefreshAvailableSkins();

            // Assert
            Assert.Equal(2, _skinManager.AvailableSystemSkins.Count);
        }

        [Fact]
        public void CurrentSkinPath_ReturnsResourceManagerPath()
        {
            // Arrange
            var expectedPath = "System/CustomSkin/";
            _mockResourceManager.Setup(x => x.GetCurrentEffectiveSkinPath()).Returns(expectedPath);

            // Act
            var result = _skinManager.CurrentSkinPath;

            // Assert
            Assert.Equal(expectedPath, result);
        }

        #region Helper Methods

        private string CreateTestSkin(string skinName)
        {
            var skinPath = Path.Combine(_testSkinRoot, skinName);
            var graphicsPath = Path.Combine(skinPath, "Graphics");

            Directory.CreateDirectory(graphicsPath);

            // Create required validation files
            File.WriteAllText(Path.Combine(graphicsPath, "1_background.jpg"), "test");
            File.WriteAllText(Path.Combine(graphicsPath, "2_background.jpg"), "test");

            return skinPath + Path.DirectorySeparatorChar;
        }

        private string CreateInvalidTestSkin(string skinName)
        {
            var skinPath = Path.Combine(_testSkinRoot, skinName);
            Directory.CreateDirectory(skinPath);

            // Don't create required files - this makes it invalid
            return skinPath + Path.DirectorySeparatorChar;
        }

        #endregion
    }
}
