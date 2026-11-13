using DTXMania.Game.Lib.Stage;
using DTXMania.Game;
using DTXMania.Game.Lib.Resources;
using Microsoft.Xna.Framework.Graphics;
using Moq;
using System;
using Xunit;
using DTXMania.Test.TestData;

namespace DTXMania.Test.Stage
{
    [Trait("Category", "Unit")]
    [Collection("SongManager")]
    public class SongSelectionStageFontLifecycleTests
    {
        [Fact]
        public void Deactivate_WhenFontLoaded_ShouldReleaseFontReference()
        {
            var stage = SongSelectionStageTestFactory.CreateStage();
            var mockFont = new Mock<IFont>();
            ReflectionHelpers.SetPrivateField(stage, "_font", mockFont.Object);

            stage.Deactivate();

            mockFont.Verify(x => x.RemoveReference(), Times.Once);
            Assert.Null(ReflectionHelpers.GetPrivateField<IFont>(stage, "_font"));
        }

        [Fact]
        public void Activate_WhenRenderTargetInitFails_ShouldReleaseFontAndThrow()
        {
            var mockResourceManager = new Mock<IResourceManager>();
            var mockFont = new Mock<IFont>();
            mockResourceManager
                .Setup(x => x.LoadFont(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<FontStyle>()))
                .Returns(mockFont.Object);

            var game = ReflectionHelpers.CreateGame(mockResourceManager.Object);
            var stage = new TestableSongSelectionStage(game, new InvalidOperationException("RT init failed"));
            ReflectionHelpers.SetPrivateField(stage, "_resourceManager", mockResourceManager.Object);

            var ex = Assert.Throws<InvalidOperationException>(() => stage.Activate());

            Assert.Equal("RT init failed", ex.Message);
            mockFont.Verify(x => x.RemoveReference(), Times.Once);
            Assert.Null(ReflectionHelpers.GetPrivateField<IFont>(stage, "_font"));
            // Graphics resources should be null after cleanup in catch block
            Assert.Null(ReflectionHelpers.GetPrivateField<SpriteBatch>(stage, "_spriteBatch"));
            Assert.Null(ReflectionHelpers.GetPrivateField<Texture2D>(stage, "_whitePixel"));
        }

        private class TestableSongSelectionStage : SongSelectionStage
        {
            private readonly Exception _renderTargetException;

            public TestableSongSelectionStage(BaseGame game, Exception renderTargetException) : base(game)
            {
                _renderTargetException = renderTargetException;
            }

            protected override void InitializeGraphicsResources()
            {
                // Skip graphics-dependent initialization for unit testing
            }

            protected override void InitializeStageRenderTargets()
            {
                throw _renderTargetException;
            }
        }

        [Fact]
        public void InitializeUI_WithThemedHistoryFont_ShouldLoadAndRetainConfiguredFace()
        {
            var resourceManager = new Mock<IResourceManager>();
            var historyFont = new Mock<IFont>();
            var uiFont = new Mock<IFont>();
            resourceManager.SetupGet(value => value.CurrentTheme).Returns(SkinTheme.Parse(new[]
            {
                "SongSelect.HistoryFontFamily=ShareTechMono",
                "SongSelect.HistoryFontSize=18",
                "SongSelect.HistoryTextScale=1.0"
            }));
            resourceManager.Setup(value => value.LoadFont("ShareTechMono", 18))
                .Returns(historyFont.Object);
            var stage = CreateStage(resourceManager);

            ReflectionHelpers.InvokePrivateMethod(stage, "InitializeUI", uiFont.Object);

            Assert.Same(historyFont.Object,
                ReflectionHelpers.GetPrivateField<IFont>(stage, "_historyFont"));
            resourceManager.Verify(value => value.LoadFont("ShareTechMono", 18), Times.Once);
        }

        [Fact]
        public void InitializeUI_WhenThemedHistoryFontFails_ShouldContinueWithSharedFont()
        {
            var resourceManager = new Mock<IResourceManager>();
            var uiFont = new Mock<IFont>();
            resourceManager.SetupGet(value => value.CurrentTheme).Returns(SkinTheme.Parse(new[]
            {
                "SongSelect.HistoryFontFamily=BrokenHistory",
                "SongSelect.HistoryFontSize=21"
            }));
            resourceManager.Setup(value => value.LoadFont("BrokenHistory", 21))
                .Throws(new System.InvalidOperationException("history font unavailable"));
            var stage = CreateStage(resourceManager);

            var exception = Record.Exception(() =>
                ReflectionHelpers.InvokePrivateMethod(stage, "InitializeUI", uiFont.Object));

            Assert.Null(exception);
            Assert.Null(ReflectionHelpers.GetPrivateField<IFont>(stage, "_historyFont"));
            resourceManager.Verify(value => value.LoadFont("BrokenHistory", 21), Times.Once);
        }

        private static SongSelectionStage CreateStage(Mock<IResourceManager> resourceManager)
        {
            var game = new Mock<IStageGame>();
            game.SetupGet(value => value.GraphicsDevice)
                .Returns(ReflectionHelpers.CreateUninitialized<GraphicsDevice>());
            game.SetupGet(value => value.ResourceManager).Returns(resourceManager.Object);

            var stage = ReflectionHelpers.CreateUninitialized<SongSelectionStage>();
            ReflectionHelpers.SetPrivateField(stage, "_game", game.Object);
            ReflectionHelpers.SetPrivateField(stage, "_resourceManager", resourceManager.Object);
            return stage;
        }
    }
}
