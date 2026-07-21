using System;
using System.IO;
using DTXMania.Game.Lib.Resources;
using Moq;
using Xunit;

namespace DTXMania.Test.Resources
{
    [Trait("Category", "Unit")]
    public class SkinManagerPatchCoverageTests : IDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(),
            "DTXManiaCX_SkinManagerPatchCoverage",
            Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void SwitchToSkinPath_WithMissingPath_ShouldRejectWithoutMutatingResources(string? path)
        {
            var resourceManager = new Mock<IResourceManager>();
            using var manager = new SkinManager(resourceManager.Object, _root);

            Assert.False(manager.SwitchToSkinPath(path!));
            resourceManager.Verify(value => value.SetBoxDefSkinPath(It.IsAny<string>()), Times.Never);
            resourceManager.Verify(value => value.SetSkinPath(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public void SwitchToSkinPath_WithRelativePath_ShouldRejectWithoutValidationSideEffects()
        {
            var resourceManager = new Mock<IResourceManager>();
            using var manager = new SkinManager(resourceManager.Object, _root);

            Assert.False(manager.SwitchToSkinPath(Path.Combine("relative", "skin")));
            resourceManager.Verify(value => value.SetBoxDefSkinPath(It.IsAny<string>()), Times.Never);
            resourceManager.Verify(value => value.SetSkinPath(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public void SwitchToSkinPath_WithInvalidAbsolutePath_ShouldRejectWithoutMutatingResources()
        {
            var invalidPath = Path.Combine(_root, "InvalidSkin");
            Directory.CreateDirectory(invalidPath);
            var resourceManager = new Mock<IResourceManager>();
            using var manager = new SkinManager(resourceManager.Object, _root);

            Assert.False(manager.SwitchToSkinPath(invalidPath));
            resourceManager.Verify(value => value.SetBoxDefSkinPath(It.IsAny<string>()), Times.Never);
            resourceManager.Verify(value => value.SetSkinPath(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public void SwitchToSkinPath_WithValidAbsolutePath_ShouldClearOverrideAndSwitch()
        {
            var validPath = CreateValidSkin("ExternalSkin");
            var resourceManager = new Mock<IResourceManager>();
            using var manager = new SkinManager(resourceManager.Object, _root);

            Assert.True(manager.SwitchToSkinPath(validPath));
            resourceManager.Verify(value => value.SetBoxDefSkinPath(""), Times.Once);
            resourceManager.Verify(value => value.SetSkinPath(validPath), Times.Once);
        }

        [Fact]
        public void SwitchToSkinPath_WhenResourceManagerThrows_ShouldReturnFalse()
        {
            var validPath = CreateValidSkin("ThrowingExternalSkin");
            var resourceManager = new Mock<IResourceManager>();
            resourceManager.Setup(value => value.SetSkinPath(validPath))
                .Throws(new InvalidOperationException("switch failed"));
            using var manager = new SkinManager(resourceManager.Object, _root);

            Assert.False(manager.SwitchToSkinPath(validPath));
            resourceManager.Verify(value => value.SetBoxDefSkinPath(""), Times.Once);
            resourceManager.Verify(value => value.SetSkinPath(validPath), Times.Once);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void SwitchToSystemSkin_WithMissingName_ShouldRejectWithoutMutatingResources(string? name)
        {
            var resourceManager = new Mock<IResourceManager>();
            using var manager = new SkinManager(resourceManager.Object, _root);

            Assert.False(manager.SwitchToSystemSkin(name!));
            resourceManager.Verify(value => value.SetBoxDefSkinPath(It.IsAny<string>()), Times.Never);
            resourceManager.Verify(value => value.SetSkinPath(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public void SwitchToSystemSkin_WhenResourceManagerThrows_ShouldReturnFalse()
        {
            CreateValidSkin("DiscoveredSkin");
            var resourceManager = new Mock<IResourceManager>();
            resourceManager.Setup(value => value.SetSkinPath(It.IsAny<string>()))
                .Throws(new InvalidOperationException("switch failed"));
            using var manager = new SkinManager(resourceManager.Object, _root);
            manager.RefreshAvailableSkins();

            Assert.False(manager.SwitchToSystemSkin("DiscoveredSkin"));
            resourceManager.Verify(value => value.SetBoxDefSkinPath(""), Times.Once);
            resourceManager.Verify(value => value.SetSkinPath(It.IsAny<string>()), Times.Once);
        }

        private string CreateValidSkin(string name)
        {
            var path = Path.Combine(_root, name);
            var graphics = Path.Combine(path, "Graphics");
            Directory.CreateDirectory(graphics);
            File.WriteAllText(Path.Combine(graphics, "1_background.jpg"), "test");
            return path;
        }
    }
}
