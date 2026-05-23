using System.Collections.Generic;
using System.Runtime.Serialization;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Stage;
using DTXMania.Game.Lib.Stage.Performance;
using DTXMania.Test.TestData;
using Moq;
using Xunit;

namespace DTXMania.Test.Stage.Performance
{
    [Trait("Category", "Unit")]
    public class PerformanceStageFontLifecycleTests
    {
        [Fact]
        public void OnDeactivate_ShouldReleaseReadyFontAndScrollSpeedFont()
        {
            var stage = CreateStage();
            var mockReadyFont = new Mock<IFont>();
            var mockScrollSpeedFont = new Mock<IFont>();
            ReflectionHelpers.SetPrivateField(stage, "_readyFont", mockReadyFont.Object);
            ReflectionHelpers.SetPrivateField(stage, "_scrollSpeedFont", mockScrollSpeedFont.Object);
            ReflectionHelpers.SetPrivateField(stage, "_bgmSounds", new Dictionary<string, ISound>());
            ReflectionHelpers.SetPrivateField(stage, "_scheduledBGMEvents", new List<BGMEvent>());

            ReflectionHelpers.InvokePrivateMethod(stage, "OnDeactivate");

            mockReadyFont.Verify(x => x.RemoveReference(), Times.Once);
            mockScrollSpeedFont.Verify(x => x.RemoveReference(), Times.Once);
            Assert.Null(ReflectionHelpers.GetPrivateField<IFont>(stage, "_readyFont"));
            Assert.Null(ReflectionHelpers.GetPrivateField<IFont>(stage, "_scrollSpeedFont"));
        }

        [Fact]
        public void InitializeReadyFont_WhenSucceeds_ShouldSetFontsAndCreateIndicator()
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

            var stage = CreateStage();
            ReflectionHelpers.SetPrivateField(stage, "_resourceManager", mockResourceManager.Object);

            ReflectionHelpers.InvokePrivateMethod(stage, "InitializeReadyFont");

            Assert.Same(mockReadyFont.Object, ReflectionHelpers.GetPrivateField<IFont>(stage, "_readyFont"));
            Assert.Same(mockScrollSpeedFont.Object, ReflectionHelpers.GetPrivateField<IFont>(stage, "_scrollSpeedFont"));
            var indicator = ReflectionHelpers.GetPrivateField<ScrollSpeedIndicator>(stage, "_scrollSpeedIndicator");
            Assert.NotNull(indicator);
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

            var stage = CreateStage();
            ReflectionHelpers.SetPrivateField(stage, "_resourceManager", mockResourceManager.Object);

            ReflectionHelpers.InvokePrivateMethod(stage, "InitializeReadyFont");

            mockReadyFont.Verify(x => x.RemoveReference(), Times.Once);
            Assert.Null(ReflectionHelpers.GetPrivateField<IFont>(stage, "_readyFont"));
            Assert.Null(ReflectionHelpers.GetPrivateField<IFont>(stage, "_scrollSpeedFont"));
            var indicator = ReflectionHelpers.GetPrivateField<ScrollSpeedIndicator>(stage, "_scrollSpeedIndicator");
            Assert.NotNull(indicator);
        }

        private static PerformanceStage CreateStage()
        {
#pragma warning disable SYSLIB0050
            var stage = (PerformanceStage)FormatterServices.GetUninitializedObject(typeof(PerformanceStage));
#pragma warning restore SYSLIB0050
            ReflectionHelpers.SetPrivateField(stage, "_game", ReflectionHelpers.CreateGame());
            return stage;
        }
    }
}
