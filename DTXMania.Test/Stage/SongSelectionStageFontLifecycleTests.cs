using DTXMania.Game.Lib.Stage;
using DTXMania.Game;
using DTXMania.Game.Lib.Resources;
using Moq;
using System;
using System.Runtime.Serialization;
using Xunit;
using DTXMania.Test.TestData;

namespace DTXMania.Test.Stage
{
    [Trait("Category", "Unit")]
    public class SongSelectionStageFontLifecycleTests
    {
        [Fact]
        public void Deactivate_WhenFontLoaded_ShouldReleaseFontReference()
        {
            var stage = CreateUninitializedStage();
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
        }

        private static SongSelectionStage CreateUninitializedStage()
        {
#pragma warning disable SYSLIB0050
            return (SongSelectionStage)FormatterServices.GetUninitializedObject(typeof(SongSelectionStage));
#pragma warning restore SYSLIB0050
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
    }
}
