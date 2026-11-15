using System;
using System.Reflection;
using DTXMania.Game.Lib.Song;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace DTXMania.Test.Song;

// HPA-796 Task 3: SongManager has no constructor injection seam, so the
// singleton takes its logger via internal ConfigureLogger (null falls back to
// NullLogger). Verified against the subscriber-failure guard path, which must
// warn once with the subscriber's original exception.
[Trait("Category", "Unit")]
public class SongManagerConfigureLoggerTests
{
    private static void InvokeSafeInvokeWithThrowingHandler(EventHandler throwingHandler, string eventName)
    {
        var method = typeof(SongManager).GetMethod(
            "SafeInvoke",
            BindingFlags.NonPublic | BindingFlags.Static,
            null,
            new[] { typeof(EventHandler), typeof(object), typeof(EventArgs), typeof(string) },
            null);
        Assert.NotNull(method);
        method!.Invoke(null, new object[] { throwingHandler, SongManager.Instance, EventArgs.Empty, eventName });
    }

    [Fact]
    public void ConfigureLogger_ShouldRouteSubscriberFailureWarningWithOriginalException()
    {
        var logger = new Mock<ILogger<SongManager>>();
        var thrown = new InvalidOperationException("subscriber blew up");
        try
        {
            SongManager.Instance.ConfigureLogger(logger.Object);

            InvokeSafeInvokeWithThrowingHandler((_, _) => throw thrown, "EnumerationCompleted");

            logger.Verify(
                l => l.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((state, _) =>
                        state.ToString()!.Contains("EnumerationCompleted", StringComparison.Ordinal)),
                    thrown,
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }
        finally
        {
            // Do not leak the mock into other tests touching the singleton.
            SongManager.Instance.ConfigureLogger(null);
        }
    }

    [Fact]
    public void ConfigureLogger_WithNull_ShouldKeepFailurePathSilentAndNonThrowing()
    {
        SongManager.Instance.ConfigureLogger(null);

        var exception = Record.Exception(() =>
            InvokeSafeInvokeWithThrowingHandler((_, _) => throw new InvalidOperationException("boom"), "EnumerationCompleted"));

        Assert.Null(exception);
    }
}
