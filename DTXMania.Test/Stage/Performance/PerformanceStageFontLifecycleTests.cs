using System;
using DTXMania.Game;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Stage;
using DTXMania.Game.Lib.Stage.Performance;
using Microsoft.Xna.Framework.Graphics;
using Moq;
using Xunit;

namespace DTXMania.Test.Stage.Performance
{
    [Trait("Category", "Unit")]
    public class PerformanceStageFontLifecycleTests
    {
        [Fact]
        public void InitializeReadyFont_WhenSucceeds_ShouldCreateIndicatorAndReleaseBothFontsOnDeactivate()
        {
            var mockResourceManager = new Mock<IResourceManager>();
            var mockReadyFont = new Mock<IFont>();
            var mockScrollSpeedFont = new Mock<IFont>();
            mockResourceManager
                .Setup(x => x.LoadFont("NotoSerifJP", 24))
                .Returns(mockReadyFont.Object);
            mockResourceManager
                .Setup(x => x.LoadFont("NotoSerifJP", 14))
                .Returns(mockScrollSpeedFont.Object);

            using var stage = CreateStage(mockResourceManager.Object);
            stage.Activate();
            var indicator = stage.InitializeFonts();
            Assert.NotNull(indicator);
            stage.Deactivate();
            mockReadyFont.Verify(x => x.RemoveReference(), Times.Once);
            mockScrollSpeedFont.Verify(x => x.RemoveReference(), Times.Once);
        }

        [Fact]
        public void InitializeReadyFont_WhenScrollSpeedFontFails_ShouldReleaseReadyFontAndUseFallbackIndicator()
        {
            var mockResourceManager = new Mock<IResourceManager>();
            var mockReadyFont = new Mock<IFont>();
            mockResourceManager
                .Setup(x => x.LoadFont("NotoSerifJP", 24))
                .Returns(mockReadyFont.Object);
            mockResourceManager
                .Setup(x => x.LoadFont("NotoSerifJP", 14))
                .Throws(new Exception("Font load failed"));

            using var stage = CreateStage(mockResourceManager.Object);
            stage.Activate();
            var indicator = stage.InitializeFonts();
            mockReadyFont.Verify(x => x.RemoveReference(), Times.Once);
            Assert.NotNull(indicator);
            stage.Deactivate();
            mockReadyFont.Verify(x => x.RemoveReference(), Times.Once);
        }

        private static HeadlessStage CreateStage(IResourceManager resources)
        {
            var config = new Mock<IConfigManager>();
            config.SetupGet(x => x.Config).Returns(new ConfigData());
            var game = new Mock<IStageGame>();
            game.SetupGet(x => x.ConfigManager).Returns(config.Object);
            game.SetupGet(x => x.ResourceManager).Returns(resources);
            return new HeadlessStage(game.Object);
        }

        private sealed class HeadlessStage : PerformanceStage
        {
            public HeadlessStage(IStageGame game) : base(game) { }
            protected override SpriteBatch? CreateSpriteBatch(GraphicsDevice graphicsDevice) => null;
            protected override Func<PlaybackAudioVariantCache> CreateAudioVariantCacheFactory() =>
                () => throw new InvalidOperationException("Font lifecycle tests do not prepare audio.");
            public ScrollSpeedIndicator InitializeFonts() => InitializeReadyFont();
        }
    }
}
