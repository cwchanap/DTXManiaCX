using System;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Stage.Performance;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace DTXMania.Test.Stage.Performance;

// HPA-796 Task 3: TabHudDisplay.AcquireDrawResources reports a captured font
// acquisition failure once at Error through the optional stage-supplied
// logger, then keeps the existing Lanes fallback contract (IsReady stays
// false, display disposed). Direct font creation never goes through
// ResourceManager, so this error is not reported by the resource manager.
[Trait("Category", "Unit")]
public class TabHudDisplayFontFailureLoggingTests
{
    [Fact]
    public void FontAcquisitionFailure_ShouldLogErrorOnce_WithOriginalException_AndKeepLanesFallback()
    {
        var logger = new Mock<ILogger<TabHudDisplay>>();
        var failure = new InvalidOperationException("font unavailable");
        using var display = new TabHudDisplay(null, "t", "a", null, null, null, logger.Object,
            (_, _) => throw failure, (_, _, _, _) => { }, (_, _, _, _, _) => { });
        Assert.False(display.IsReady);
        logger.Verify(
            l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((s, _) =>
                    s.ToString()!.Contains("failed to acquire fonts", StringComparison.Ordinal)),
                failure,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void FontAcquisitionFailure_WhenLoggerThrows_ShouldReleaseAcquiredFonts()
    {
        var font = new Mock<IFont>();
        var logger = new Mock<ILogger<TabHudDisplay>>();
        var loggingFailure = new InvalidOperationException("logger failed");
        logger.Setup(l => l.Log(
                It.IsAny<LogLevel>(), It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()))
            .Throws(loggingFailure);
        int count = 0;

        Assert.Same(loggingFailure, Assert.Throws<InvalidOperationException>(() =>
            new TabHudDisplay(null, "t", "a", null, null, null, logger.Object,
                (_, _) => ++count == 1 ? font.Object : throw new InvalidOperationException("font failed"),
                (_, _, _, _) => { }, (_, _, _, _, _) => { })));

        font.Verify(f => f.Dispose(), Times.Once);
    }
}
