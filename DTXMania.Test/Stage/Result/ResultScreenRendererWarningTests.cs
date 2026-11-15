using System.Diagnostics;
using System.IO;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Stage.Performance;
using DTXMania.Game.Lib.Stage.Result;
using Moq;
using Xunit;

namespace DTXMania.Test.Stage.Result;

[CollectionDefinition("ResultRendererTrace", DisableParallelization = true)]
public sealed class ResultRendererTraceCollection { }

[Collection("ResultRendererTrace")]
public class ResultScreenRendererWarningTests
{
    // HPA-796 Task 3: a missing rank badge is an expected optional-asset miss.
    // The renderer no longer duplicates the failure at the leaf; the owner
    // (ResourceManager.OnResourceLoadFailed) reports it at Debug, which stays
    // below Warning in ordinary Release runs.
    [Fact]
    public void Load_MissingRankBadge_ShouldStayBelowWarningAndLoadWithFallback()
    {
        var resources = new Mock<IResourceManager>();
        resources.Setup(r => r.ResourceExists(It.IsAny<string>()))
            .Returns<string>(path => path != TexturePath.ResultRankF);
        resources.Setup(r => r.LoadTexture(It.IsAny<string>())).Returns(new Mock<ITexture>().Object);
        var model = ResultScreenModel.Create(new PerformanceSummary { PlayingSkill = 30 }, null, 0, null, null);
        using var renderer = new ResultScreenRenderer(resources.Object, null, null, null);
        using var output = new StringWriter();
        using var listener = new TextWriterTraceListener(output);
        Trace.Listeners.Add(listener);
        try
        {
            var exception = Record.Exception(() => renderer.Load(model));
            Trace.Flush();

            Assert.Null(exception);
            Assert.DoesNotContain("Missing rank badge", output.ToString());
            Assert.DoesNotContain(TexturePath.ResultRankF, output.ToString());
            Assert.Equal(string.Empty, output.ToString().Trim());
        }
        finally
        {
            Trace.Listeners.Remove(listener);
        }
    }
}
