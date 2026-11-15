using System;
using System.Collections.Generic;
using System.IO;
using DTXMania.Game.Lib.Resources;
using Microsoft.Extensions.Logging;
using Microsoft.Xna.Framework.Graphics;
using Moq;
using Xunit;
using DTXMania.Test.Helpers;
using RD = DTXMania.Test.TestData.ReflectionHelpers;

namespace DTXMania.Test.Resources;

// HPA-796 Task 3: ResourceManager.OnResourceLoadFailed classifies load
// failures — expected optional-asset misses (FileNotFoundException) log at
// Debug (filtered in ordinary Release runs), unexpected decode/I/O failures
// warn once with the original exception and the path. The ResourceLoadFailed
// event stays raised so TryLoadTexture/TryLoadSound keep their contract.
[Trait("Category", "Unit")]
public class ResourceManagerLoadFailureLoggingTests
{
    private sealed class ThrowingDecodeResourceManager : ResourceManager
    {
        public ThrowingDecodeResourceManager(GraphicsDevice device, ILogger<ResourceManager>? logger)
            : base(device, logger)
        {
        }

        // Captured so tests can prove the ORIGINAL exception reaches the logger.
        public Exception? ThrownDecodeException { get; private set; }

        protected override ITexture CreateTextureCore(string resolvedPath, string originalPath, TextureCreationParams creationParams)
        {
            ThrownDecodeException = new InvalidOperationException("decode exploded");
            throw ThrownDecodeException;
        }

        protected override ISound CreateSoundCore(string resolvedPath, string originalPath)
        {
            ThrownDecodeException = new InvalidOperationException("decode exploded");
            throw ThrownDecodeException;
        }

        protected override ITexture CreateFallbackTexture(string originalPath)
            => new Mock<ITexture>().Object;

        protected override ISound CreateFallbackSound(string originalPath)
            => new Mock<ISound>().Object;
    }

    private static Mock<ILogger<ResourceManager>> CreateLoggerMock() => new();

    private static void VerifyLoggedOnce(
        Mock<ILogger<ResourceManager>> logger,
        LogLevel level,
        string messagePart,
        Exception exception)
    {
        // The messagePart and exception arguments are exact (non-matcher)
        // values, so they stay valid when captured by this shared expression.
        logger.Verify(
            l => l.Log(
                level,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) => state.ToString()!.Contains(messagePart, StringComparison.Ordinal)),
                exception,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    private static void VerifyNever(Mock<ILogger<ResourceManager>> logger, LogLevel level)
    {
        logger.Verify(
            l => l.Log(
                level,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    [Fact]
    public void LoadTexture_MissingOptionalFile_ShouldLogDebugOnly_AndStillRaiseEvent()
    {
        var logger = CreateLoggerMock();
        var failedPaths = new List<string>();
        var failedExceptions = new List<Exception?>();
        using var resourceManager = new ThrowingDecodeResourceManager(
            RD.CreateUninitialized<GraphicsDevice>(), logger.Object);
        resourceManager.ResourceLoadFailed += (_, e) =>
        {
            failedPaths.Add(e.Path);
            failedExceptions.Add(e.Exception);
        };

        var missingPath = $"hpa796-missing-{Guid.NewGuid():N}.png";
        var texture = resourceManager.LoadTexture(missingPath);

        // The 1x1-fallback contract stays: callers always receive a texture.
        Assert.NotNull(texture);
        Assert.Single(failedPaths);
        Assert.Equal(missingPath, failedPaths[0]);
        Assert.IsType<FileNotFoundException>(failedExceptions[0]);

        // Expected miss stays below Warning. Matchers must be built inline —
        // a matcher value passed through a helper parameter is captured as a
        // plain constant.
        logger.Verify(
            l => l.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) => state.ToString()!.Contains(missingPath, StringComparison.Ordinal)),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
        VerifyNever(logger, LogLevel.Warning);
    }

    [Fact]
    public void LoadTexture_UnexpectedDecodeFailure_ShouldLogWarningOnceWithOriginalException()
    {
        var logger = CreateLoggerMock();
        var failedPaths = new List<string>();
        using var resourceManager = new ThrowingDecodeResourceManager(
            RD.CreateUninitialized<GraphicsDevice>(), logger.Object);
        resourceManager.ResourceLoadFailed += (_, e) => failedPaths.Add(e.Path);

        var decodeFile = Path.Combine(Path.GetTempPath(), $"hpa796-decode-{Guid.NewGuid():N}.png");
        File.WriteAllText(decodeFile, "not a real png");
        try
        {
            var texture = resourceManager.LoadTexture(decodeFile);

            Assert.NotNull(texture);
            Assert.Single(failedPaths);

            VerifyLoggedOnce(logger, LogLevel.Warning, "Resource load failed:", resourceManager.ThrownDecodeException!);
            VerifyLoggedOnce(logger, LogLevel.Warning, decodeFile, resourceManager.ThrownDecodeException!);
            VerifyNever(logger, LogLevel.Debug);
        }
        finally
        {
            File.Delete(decodeFile);
        }
    }

    [Fact]
    public void LoadSound_MissingOptionalFile_ShouldLogDebugOnly()
    {
        var logger = CreateLoggerMock();
        var missingExceptions = new List<Exception?>();
        using var resourceManager = new ThrowingDecodeResourceManager(
            RD.CreateUninitialized<GraphicsDevice>(), logger.Object);
        resourceManager.ResourceLoadFailed += (_, e) => missingExceptions.Add(e.Exception);

        var missingPath = $"hpa796-missing-{Guid.NewGuid():N}.wav";
        var sound = resourceManager.LoadSound(missingPath);

        Assert.NotNull(sound);
        Assert.IsType<FileNotFoundException>(missingExceptions[0]);
        logger.Verify(
            l => l.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) => state.ToString()!.Contains(missingPath, StringComparison.Ordinal)),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
        VerifyNever(logger, LogLevel.Warning);
    }

    [Fact]
    public void LoadSound_UnexpectedDecodeFailure_ShouldLogWarningOnce()
    {
        var logger = CreateLoggerMock();
        using var resourceManager = new ThrowingDecodeResourceManager(
            RD.CreateUninitialized<GraphicsDevice>(), logger.Object);

        var decodeFile = Path.Combine(Path.GetTempPath(), $"hpa796-decode-{Guid.NewGuid():N}.wav");
        File.WriteAllText(decodeFile, "not a real wav");
        try
        {
            var sound = resourceManager.LoadSound(decodeFile);

            Assert.NotNull(sound);
            VerifyLoggedOnce(logger, LogLevel.Warning, "Resource load failed:", resourceManager.ThrownDecodeException!);
            VerifyLoggedOnce(logger, LogLevel.Warning, decodeFile, resourceManager.ThrownDecodeException!);
            VerifyNever(logger, LogLevel.Debug);
        }
        finally
        {
            File.Delete(decodeFile);
        }
    }

    [GraphicsFact]
    public void CreateResourceManager_ShouldPassLoggerThroughToClassification()
    {
        using var graphicsDeviceService = new TestGraphicsDeviceService();
        var logger = CreateLoggerMock();
        using var resourceManager = ResourceManagerFactory.CreateResourceManager(
            graphicsDeviceService.RequiredDevice, logger.Object);

        var missingPath = $"hpa796-factory-{Guid.NewGuid():N}.png";
        var texture = resourceManager.LoadTexture(missingPath);

        Assert.NotNull(texture);
        logger.Verify(
            l => l.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) => state.ToString()!.Contains(missingPath, StringComparison.Ordinal)),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
        VerifyNever(logger, LogLevel.Warning);
    }
}
