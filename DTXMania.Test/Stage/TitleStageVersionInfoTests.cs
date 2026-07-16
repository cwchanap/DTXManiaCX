using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Stage;
using DTXMania.Test.TestData;
using Moq;
using Xunit;

namespace DTXMania.Test.Stage
{
    /// <summary>
    /// The title screen's version info is real font-rendered text; the old
    /// implementation drew an unlabeled white rectangle placeholder. The font
    /// loads through the resource manager and is optional — when it can't load,
    /// nothing is drawn (no white box).
    /// </summary>
    [Trait("Category", "Unit")]
    public class TitleStageVersionInfoTests
    {
        private static TitleStage CreateStage()
        {
            return new TitleStage(ReflectionHelpers.CreateGame());
        }

        [Fact]
        public void LoadVersionFont_WhenResourceLoadSucceeds_ShouldAssignVersionFont()
        {
            var stage = CreateStage();
            var resourceManager = new Mock<IResourceManager>();
            var font = new Mock<IFont>();
            resourceManager
                .Setup(x => x.LoadFont(It.IsAny<string>(), It.IsAny<int>()))
                .Returns(font.Object);
            ReflectionHelpers.SetPrivateField(stage, "_resourceManager", resourceManager.Object);

            ReflectionHelpers.InvokePrivateMethod(stage, "LoadVersionFont");

            Assert.Same(font.Object, ReflectionHelpers.GetPrivateField<IFont>(stage, "_versionFont"));
        }

        [Fact]
        public void LoadVersionFont_WhenResourceLoadFails_ShouldLeaveVersionFontUnset()
        {
            var stage = CreateStage();
            var resourceManager = new Mock<IResourceManager>();
            resourceManager
                .Setup(x => x.LoadFont(It.IsAny<string>(), It.IsAny<int>()))
                .Throws(new System.InvalidOperationException("missing font"));
            ReflectionHelpers.SetPrivateField(stage, "_resourceManager", resourceManager.Object);

            var exception = Record.Exception(() => ReflectionHelpers.InvokePrivateMethod(stage, "LoadVersionFont"));

            Assert.Null(exception);
            Assert.Null(ReflectionHelpers.GetPrivateField<IFont>(stage, "_versionFont"));
        }
    }
}
