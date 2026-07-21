using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Stage;
using DTXMania.Test.TestData;
using Moq;
using Xunit;

namespace DTXMania.Test.Stage
{
    [Trait("Category", "Unit")]
    public class SongSelectionStageHistoryFontCoverageTests
    {
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
            var stage = new SongSelectionStage(ReflectionHelpers.CreateGame(resourceManager.Object));
            ReflectionHelpers.SetPrivateField(stage, "_resourceManager", resourceManager.Object);

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
            var stage = new SongSelectionStage(ReflectionHelpers.CreateGame(resourceManager.Object));
            ReflectionHelpers.SetPrivateField(stage, "_resourceManager", resourceManager.Object);

            var exception = Record.Exception(() =>
                ReflectionHelpers.InvokePrivateMethod(stage, "InitializeUI", uiFont.Object));

            Assert.Null(exception);
            Assert.Null(ReflectionHelpers.GetPrivateField<IFont>(stage, "_historyFont"));
            resourceManager.Verify(value => value.LoadFont("BrokenHistory", 21), Times.Once);
        }
    }
}
