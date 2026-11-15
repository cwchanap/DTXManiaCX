using System;
using System.IO;
using System.Reflection;
using DTXMania.Game.Lib.Song;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace DTXMania.Test.Song;

// HPA-796 Task 3 fix round 1: the SET.def encoding retry loop must keep
// retrying every encoding, but report a persistent read failure exactly once
// — after all attempts fail — with the SET.def path and the retained
// exception, instead of one Warning per encoding attempt.
[Trait("Category", "Unit")]
[Collection("SongManager")] // serializes singleton ConfigureLogger use
public class SongManagerSetDefReadLoggingTests
{
    private static string[]? InvokeReadSetDefLines(string setDefPath)
    {
        var method = typeof(SongManager).GetMethod(
            "ReadSetDefLines",
            BindingFlags.NonPublic | BindingFlags.Static,
            null,
            new[] { typeof(string) },
            null);
        Assert.NotNull(method);
        return method!.Invoke(null, new object[] { setDefPath }) as string[];
    }

    [Fact]
    public void ReadSetDefLines_PersistentReadFailure_ShouldWarnOnceWithPathAndRetainedException()
    {
        var logger = new Mock<ILogger<SongManager>>();
        // A directory path exists but cannot be read as a file, so every
        // encoding attempt fails with UnauthorizedAccessException/IOException.
        var unreadableDir = Directory.CreateTempSubdirectory("setdef-");
        string unreadablePath = unreadableDir.FullName;
        try
        {
            SongManager.Instance.ConfigureLogger(logger.Object);

            var lines = InvokeReadSetDefLines(unreadablePath);

            Assert.Null(lines);
            logger.Verify(
                l => l.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((state, _) =>
                        state.ToString()!.Contains(unreadablePath, StringComparison.Ordinal)),
                    It.Is<Exception?>(exception => exception != null),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
            logger.Verify(
                l => l.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.IsAny<It.IsAnyType>(),
                    It.IsAny<Exception?>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }
        finally
        {
            // Do not leak the mock into other tests touching the singleton.
            SongManager.Instance.ConfigureLogger(null);
            unreadableDir.Delete(recursive: true);
        }
    }

    [Fact]
    public void ReadSetDefLines_ReadableFile_ShouldStaySilent()
    {
        var logger = new Mock<ILogger<SongManager>>();
        var setDefDir = Directory.CreateTempSubdirectory("setdef-");
        string setDefPath = Path.Combine(setDefDir.FullName, "SET.def");
        try
        {
            File.WriteAllLines(setDefPath, new[] { "#TITLE Test" });
            SongManager.Instance.ConfigureLogger(logger.Object);

            var lines = InvokeReadSetDefLines(setDefPath);

            Assert.NotNull(lines);
            logger.Verify(
                l => l.Log(
                    It.IsAny<LogLevel>(),
                    It.IsAny<EventId>(),
                    It.IsAny<It.IsAnyType>(),
                    It.IsAny<Exception?>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Never);
        }
        finally
        {
            SongManager.Instance.ConfigureLogger(null);
            setDefDir.Delete(recursive: true);
        }
    }
}
