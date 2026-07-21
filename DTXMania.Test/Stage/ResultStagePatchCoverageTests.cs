using System.Runtime.Serialization;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Stage;
using DTXMania.Test.TestData;
using Moq;
using Xunit;

namespace DTXMania.Test.Stage
{
    [Trait("Category", "Unit")]
    public class ResultStagePatchCoverageTests
    {
        [Fact]
        public void CleanupComponents_WithDisplayFontsLoaded_ShouldReleaseAndClearBothFonts()
        {
#pragma warning disable SYSLIB0050
            var stage = (ResultStage)FormatterServices.GetUninitializedObject(typeof(ResultStage));
#pragma warning restore SYSLIB0050
            var valueFont = new Mock<IFont>();
            var countFont = new Mock<IFont>();
            ReflectionHelpers.SetPrivateField(stage, "_valueResultFont", valueFont.Object);
            ReflectionHelpers.SetPrivateField(stage, "_countResultFont", countFont.Object);

            ReflectionHelpers.InvokePrivateMethod(stage, "CleanupComponents");

            valueFont.Verify(value => value.RemoveReference(), Times.Once);
            countFont.Verify(value => value.RemoveReference(), Times.Once);
            Assert.Null(ReflectionHelpers.GetPrivateField<IFont>(stage, "_valueResultFont"));
            Assert.Null(ReflectionHelpers.GetPrivateField<IFont>(stage, "_countResultFont"));
        }
    }
}
