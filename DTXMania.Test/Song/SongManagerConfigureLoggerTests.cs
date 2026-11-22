using System;
using System.IO;
using System.Reflection;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Entities;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace DTXMania.Test.Song;

// HPA-801: SongLibrary takes its logger as a constructor argument (null falls
// back to NullLogger); the singleton ConfigureLogger seam is gone. Verified
// against the subscriber-failure guard path, which must warn once with the
// subscriber's original exception. The per-event
// SongDiscovered/EnumerationCompleted events were removed with HPA-801
// Decision 0, so the guard is exercised against the remaining
// SongLibraryPublished event (the only SafeInvoke caller).
[Trait("Category", "Unit")]
public class SongManagerConfigureLoggerTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(), $"configure_log_{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        try { File.Delete(_databasePath); } catch { /* best effort */ }
    }

    private static void InvokeSafeInvokeWithThrowingHandler(
        SongLibrary library,
        EventHandler<SongLibraryPublishedEventArgs> throwingHandler,
        string eventName)
    {
        var method = typeof(SongLibrary).GetMethods(
                BindingFlags.NonPublic | BindingFlags.Instance)
            .Single(candidate =>
                candidate.Name == "SafeInvoke" && candidate.IsGenericMethodDefinition)
            .MakeGenericMethod(typeof(SongLibraryPublishedEventArgs));
        method.Invoke(
            library,
            new object[] { throwingHandler, library, new SongLibraryPublishedEventArgs(new SongLibrarySnapshot(0, [], [], 0, 0)), eventName });
    }

    [Fact]
    public void ConstructorLogger_ShouldRouteSubscriberFailureWarningWithOriginalException()
    {
        var logger = new Mock<ILogger<SongLibrary>>();
        var thrown = new InvalidOperationException("subscriber blew up");
        using var library = new SongLibrary(new SongDatabaseService(_databasePath), logger.Object);

        InvokeSafeInvokeWithThrowingHandler(library, (_, _) => throw thrown, "SongLibraryPublished");

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

    [Fact]
    public void NullLogger_ShouldKeepFailurePathSilentAndNonThrowing()
    {
        using var library = new SongLibrary(new SongDatabaseService(_databasePath));

        var exception = Record.Exception(() =>
            InvokeSafeInvokeWithThrowingHandler(library, (_, _) => throw new InvalidOperationException("boom"), "SongLibraryPublished"));

        Assert.Null(exception);
    }
}
