using System;
using System.IO;
using System.Reflection;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Entities;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace DTXMania.Test.Song;

// HPA-796 Task 3 fix round 1: the SET.def encoding retry loop must keep
// retrying every encoding, but report a persistent read failure exactly once
// — after all attempts fail — with the SET.def path and the retained
// exception, instead of one Warning per encoding attempt.
// HPA-801: the logger is a constructor argument now (no singleton
// ConfigureLogger seam), so each test builds its own library with a mock
// ILogger<SongLibrary>.
[Trait("Category", "Unit")]
public class SongManagerSetDefReadLoggingTests : IDisposable
{
    private readonly Mock<ILogger<SongLibrary>> _logger = new();
    private readonly SongLibrary _library;
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(), $"setdef_log_{Guid.NewGuid():N}.db");

    public SongManagerSetDefReadLoggingTests()
    {
        _library = new SongLibrary(new SongDatabase(_databasePath), _logger.Object);
    }

    public void Dispose()
    {
        _library.Dispose();
        try { File.Delete(_databasePath); } catch { /* best effort */ }
    }

    private string[]? InvokeReadSetDefLines(string setDefPath)
    {
        var method = typeof(SongLibrary).GetMethod(
            "ReadSetDefLines",
            BindingFlags.NonPublic | BindingFlags.Instance,
            null,
            new[] { typeof(string) },
            null);
        Assert.NotNull(method);
        return method!.Invoke(_library, new object[] { setDefPath }) as string[];
    }

    [Fact]
    public void ReadSetDefLines_PersistentReadFailure_ShouldWarnOnceWithPathAndRetainedException()
    {
        // A directory path exists but cannot be read as a file, so every
        // encoding attempt fails with UnauthorizedAccessException/IOException.
        var unreadableDir = Directory.CreateTempSubdirectory("setdef-");
        string unreadablePath = unreadableDir.FullName;
        try
        {
            var lines = InvokeReadSetDefLines(unreadablePath);

            Assert.Null(lines);
            _logger.Verify(
                l => l.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((state, _) =>
                        state.ToString()!.Contains(unreadablePath, StringComparison.Ordinal)),
                    It.Is<Exception?>(exception => exception != null),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
            _logger.Verify(
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
            unreadableDir.Delete(recursive: true);
        }
    }

    [Fact]
    public void ReadSetDefLines_RepeatedReadsOfSameUnreadableFile_ShouldWarnOncePerFile()
    {
        var unreadableDir = Directory.CreateTempSubdirectory("setdef-");
        try
        {
            // The DB hierarchy build and the freshness inventory both read the same file.
            Assert.Null(InvokeReadSetDefLines(unreadableDir.FullName));
            Assert.Null(InvokeReadSetDefLines(unreadableDir.FullName));

            _logger.Verify(
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
            unreadableDir.Delete(recursive: true);
        }
    }

    [Fact]
    public void ReadSetDefLines_ReadableFile_ShouldStaySilent()
    {
        var setDefDir = Directory.CreateTempSubdirectory("setdef-");
        string setDefPath = Path.Combine(setDefDir.FullName, "SET.def");
        try
        {
            File.WriteAllLines(setDefPath, new[] { "#TITLE Test" });

            var lines = InvokeReadSetDefLines(setDefPath);

            Assert.NotNull(lines);
            _logger.Verify(
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
            setDefDir.Delete(recursive: true);
        }
    }
}
