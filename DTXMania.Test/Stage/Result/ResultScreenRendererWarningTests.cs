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
    [Fact]
    public void Load_MissingRankBadge_ShouldWarnOnceWithAssetPath()
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
            renderer.Load(model);
            Trace.Flush();
            Assert.Contains("Missing rank badge", output.ToString());
            Assert.Contains(TexturePath.ResultRankF, output.ToString());
            Assert.Equal(1, output.ToString().Split("Missing rank badge").Length - 1);
        }
        finally
        {
            Trace.Listeners.Remove(listener);
        }
    }
}
