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
            [Fact]
        public void LoadDisplayFonts_WhenTitleFaceFails_ShouldStillLoadArtistFaceAndReleasePreviousFonts()
        {
            var stage = new SongTransitionStage(ReflectionHelpers.CreateGame());
            var oldTitle = new Mock<IFont>();
            var oldArtist = new Mock<IFont>();
            var loadedArtist = new Mock<IFont>();
            var resourceManager = new Mock<IResourceManager>();
            resourceManager.SetupGet(value => value.CurrentTheme).Returns(SkinTheme.Parse(new[]
            {
                "Transition.TitleFontFamily=BrokenTitle",
                "Transition.TitleFontSize=31",
                "Transition.ArtistFontFamily=ArtistFace",
                "Transition.ArtistFontSize=19"
            }));
            resourceManager.Setup(value => value.LoadFont("BrokenTitle", 31))
                .Throws(new InvalidOperationException("title unavailable"));
            resourceManager.Setup(value => value.LoadFont("ArtistFace", 19))
                .Returns(loadedArtist.Object);

            ReflectionHelpers.SetPrivateField(stage, "_resourceManager", resourceManager.Object);
            ReflectionHelpers.SetPrivateField(stage, "_titleDisplayFont", oldTitle.Object);
            ReflectionHelpers.SetPrivateField(stage, "_artistDisplayFont", oldArtist.Object);

            ReflectionHelpers.InvokePrivateMethod(stage, "LoadDisplayFonts");

            oldTitle.Verify(value => value.RemoveReference(), Times.Once);
            oldArtist.Verify(value => value.RemoveReference(), Times.Once);
            Assert.Null(ReflectionHelpers.GetPrivateField<IFont>(stage, "_titleDisplayFont"));
            Assert.Same(loadedArtist.Object,
                ReflectionHelpers.GetPrivateField<IFont>(stage, "_artistDisplayFont"));
        }

        [Fact]
        public void LoadDisplayFonts_WhenArtistFaceFails_ShouldKeepLoadedTitleFace()
        {
            var stage = new SongTransitionStage(ReflectionHelpers.CreateGame());
            var loadedTitle = new Mock<IFont>();
            var resourceManager = new Mock<IResourceManager>();
            resourceManager.SetupGet(value => value.CurrentTheme).Returns(SkinTheme.Parse(new[]
            {
                "Transition.TitleFontFamily=TitleFace",
                "Transition.TitleFontSize=29",
                "Transition.ArtistFontFamily=BrokenArtist",
                "Transition.ArtistFontSize=18"
            }));
            resourceManager.Setup(value => value.LoadFont("TitleFace", 29))
                .Returns(loadedTitle.Object);
            resourceManager.Setup(value => value.LoadFont("BrokenArtist", 18))
                .Throws(new InvalidOperationException("artist unavailable"));

            ReflectionHelpers.SetPrivateField(stage, "_resourceManager", resourceManager.Object);

            ReflectionHelpers.InvokePrivateMethod(stage, "LoadDisplayFonts");

            Assert.Same(loadedTitle.Object,
                ReflectionHelpers.GetPrivateField<IFont>(stage, "_titleDisplayFont"));
            Assert.Null(ReflectionHelpers.GetPrivateField<IFont>(stage, "_artistDisplayFont"));
        }
}
}
