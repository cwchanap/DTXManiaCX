using System;
using System.Reflection;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Entities;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace DTXMania.Test.Song;

// HPA-796 Task 3: SongManager has no constructor injection seam, so the
// singleton takes its logger via internal ConfigureLogger (null falls back to
// NullLogger). Verified against the subscriber-failure guard path, which must
// warn once with the subscriber's original exception. The per-event
// SongDiscovered/EnumerationCompleted events were removed with HPA-801
// Decision 0, so the guard is exercised against the remaining
// SongLibraryPublished event (the only SafeInvoke caller).
[Trait("Category", "Unit")]
[Collection("SongManager")] // serializes singleton ConfigureLogger use
public class SongManagerConfigureLoggerTests
{
    private static void InvokeSafeInvokeWithThrowingHandler(
        EventHandler<SongLibraryPublishedEventArgs> throwingHandler,
        string eventName)
    {
        var method = typeof(SongManager).GetMethods(
                BindingFlags.NonPublic | BindingFlags.Instance)
            .Single(candidate =>
                candidate.Name == "SafeInvoke" && candidate.IsGenericMethodDefinition)
            .MakeGenericMethod(typeof(SongLibraryPublishedEventArgs));
        method.Invoke(
            SongManager.Instance,
            new object[] { throwingHandler, SongManager.Instance, new SongLibraryPublishedEventArgs(new SongLibrarySnapshot(0, [], [], 0, 0)), eventName });
    }

    [Fact]
    public void ConfigureLogger_ShouldRouteSubscriberFailureWarningWithOriginalException()
    {
        var logger = new Mock<ILogger<SongManager>>();
        var thrown = new InvalidOperationException("subscriber blew up");
        try
        {
            SongManager.Instance.ConfigureLogger(logger.Object);

            InvokeSafeInvokeWithThrowingHandler((_, _) => throw thrown, "SongLibraryPublished");

            logger.Verify(
                l => l.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((state, _) =>
                        state.ToString()!.Contains("SongLibraryPublished", StringComparison.Ordinal)),
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
            InvokeSafeInvokeWithThrowingHandler((_, _) => throw new InvalidOperationException("boom"), "SongLibraryPublished"));

        Assert.Null(exception);
    }
}
