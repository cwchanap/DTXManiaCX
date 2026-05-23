using System.Runtime.Serialization;
using DTXMania.Game;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Stage;
using DTXMania.Test.TestData;
using Moq;
using Xunit;

namespace DTXMania.Test.Stage
{
    [Trait("Category", "Unit")]
    public class SongTransitionStageFontLifecycleTests
    {
        [Fact]
        public void LoadLevelNumberFont_WhenSucceeds_ShouldReplaceOldFont()
        {
            var stage = CreateUninitializedStage();
            var resourceManager = new Mock<IResourceManager>();
            var oldFont = new Mock<IFont>();
            var newFont = new Mock<IFont>();

            resourceManager
                .Setup(x => x.LoadFont("NotoSerifJP", 24))
                .Returns(newFont.Object);
            ReflectionHelpers.SetPrivateField(stage, "_resourceManager", resourceManager.Object);
            ReflectionHelpers.SetPrivateField(stage, "_levelNumberFont", oldFont.Object);

            ReflectionHelpers.InvokePrivateMethod(stage, "LoadLevelNumberFont");

            oldFont.Verify(x => x.RemoveReference(), Times.Once);
            Assert.Same(newFont.Object, ReflectionHelpers.GetPrivateField<IFont>(stage, "_levelNumberFont"));
        }

        [Fact]
        public void LoadLevelNumberFont_WhenFails_ShouldRestoreOldFont()
        {
            var stage = CreateUninitializedStage();
            var resourceManager = new Mock<IResourceManager>();
            var oldFont = new Mock<IFont>();

            resourceManager
                .Setup(x => x.LoadFont("NotoSerifJP", 24))
                .Throws(new InvalidOperationException("font missing"));
            ReflectionHelpers.SetPrivateField(stage, "_resourceManager", resourceManager.Object);
            ReflectionHelpers.SetPrivateField(stage, "_levelNumberFont", oldFont.Object);

            ReflectionHelpers.InvokePrivateMethod(stage, "LoadLevelNumberFont");

            oldFont.Verify(x => x.RemoveReference(), Times.Never);
            Assert.Same(oldFont.Object, ReflectionHelpers.GetPrivateField<IFont>(stage, "_levelNumberFont"));
        }

        [Fact]
        public void Deactivate_ShouldReleaseAllFonts()
        {
            var stage = CreateUninitializedStage();
            var titleFont = new Mock<IFont>();
            var artistFont = new Mock<IFont>();
            var levelNumberFont = new Mock<IFont>();

            ReflectionHelpers.SetPrivateField(stage, "_titleFont", titleFont.Object);
            ReflectionHelpers.SetPrivateField(stage, "_artistFont", artistFont.Object);
            ReflectionHelpers.SetPrivateField(stage, "_levelNumberFont", levelNumberFont.Object);
            ReflectionHelpers.SetPrivateField(stage, "_currentPhase", StagePhase.Normal);

            stage.Deactivate();

            titleFont.Verify(x => x.RemoveReference(), Times.Once);
            artistFont.Verify(x => x.RemoveReference(), Times.Once);
            levelNumberFont.Verify(x => x.RemoveReference(), Times.Once);
            Assert.Null(ReflectionHelpers.GetPrivateField<IFont>(stage, "_titleFont"));
            Assert.Null(ReflectionHelpers.GetPrivateField<IFont>(stage, "_artistFont"));
            Assert.Null(ReflectionHelpers.GetPrivateField<IFont>(stage, "_levelNumberFont"));
        }

        private static SongTransitionStage CreateUninitializedStage()
        {
#pragma warning disable SYSLIB0050
            return (SongTransitionStage)FormatterServices.GetUninitializedObject(typeof(SongTransitionStage));
#pragma warning restore SYSLIB0050
        }
    }
}
