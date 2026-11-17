using System;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Stage.Performance;
using DTXMania.Game.Lib.Stage.Result;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace DTXMania.Test.Stage.Result;

public class ResultScreenRendererWarningTests
{
    // HPA-796 Task 3: a missing rank badge is an expected optional-asset miss.
    // The renderer pre-checks existence, so ResourceManager never sees the miss;
    // the renderer itself reports it at Debug (below Warning in ordinary runs).
    [Fact]
    public void Load_MissingRankBadge_ShouldReportAtDebugOnlyAndLoadWithFallback()
    {
        var resources = new Mock<IResourceManager>();
        resources.Setup(r => r.ResourceExists(It.IsAny<string>()))
            .Returns<string>(path => path != TexturePath.ResultRankF);
        resources.Setup(r => r.LoadTexture(It.IsAny<string>())).Returns(new Mock<ITexture>().Object);
        var logger = new Mock<ILogger>();
        var model = ResultScreenModel.Create(new PerformanceSummary { PlayingSkill = 30 }, null, 0, null, null);
        using var renderer = new ResultScreenRenderer(
            resources.Object, null, null, null, logger: logger.Object);

        var exception = Record.Exception(() => renderer.Load(model));

        Assert.Null(exception);
        logger.Verify(
            l => l.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) =>
                    state.ToString()!.Contains(TexturePath.ResultRankF, StringComparison.Ordinal)),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
        logger.Verify(
            l => l.Log(
                It.Is<LogLevel>(level => level >= LogLevel.Warning),
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }
}
