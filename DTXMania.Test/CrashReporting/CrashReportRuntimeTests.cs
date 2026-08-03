#nullable enable

using System;
using System.IO;
using System.Text.Json;
using DTXMania.Game.Lib.Diagnostics.CrashReporting;
using DTXMania.Game.Lib.Stage;
using Microsoft.Extensions.Logging;
using Moq;

namespace DTXMania.Test.CrashReporting;

[Trait("Category", "Unit")]
public sealed class CrashReportRuntimeTests
{
    [Fact]
    public void CreateBestEffort_WhenStoreFactoryFails_ShouldPreserveConsoleLogger()
    {
        using var errorWriter = new StringWriter();
        using var runtime = CrashReportRuntime.CreateBestEffort(
            StartupTimingTrace.Disabled,
            errorWriter,
            storeFactory: () => throw new UnauthorizedAccessException("denied"));

        var logger = runtime.GameDiagnostics.LoggerFactory.CreateLogger("probe");
        var exception = Record.Exception(() => logger.LogInformation("console survives"));

        Assert.Null(exception);
        Assert.False(runtime.IsCaptureEnabled);
        Assert.Same(EmptyCrashReportInbox.Instance, runtime.GameDiagnostics.Inbox);
        Assert.Contains("crash_reporting_disabled", errorWriter.ToString());
    }

    [Fact]
    public void CreateBestEffort_WhenSetupSucceeds_ShouldEnableCrashBufferAndPreserveLogging()
    {
        var reportRoot = CreateReportRoot();

        try
        {
            using var runtime = CrashReportRuntime.CreateBestEffort(
                StartupTimingTrace.Disabled,
                TextWriter.Null,
                storeFactory: () => CreateStore(reportRoot));
            var logger = runtime.GameDiagnostics.LoggerFactory.CreateLogger("probe");

            logger.LogInformation(
                new EventId(5100, "crash_safe_stage"),
                "Crash-safe stage changed to {Stage}",
                StageType.Title);

            var provider = Assert.IsType<CrashLogBufferProvider>(runtime.CrashLogBufferProvider);
            var record = Assert.Single(provider.Snapshot());
            Assert.True(runtime.IsCaptureEnabled);
            Assert.Equal(StageType.Title, record.Properties["Stage"]);
            Assert.False(Directory.Exists(reportRoot));
        }
        finally
        {
            DeleteReportRoot(reportRoot);
        }
    }

    [Fact]
    public void CaptureFatal_WhenStoreSerializationFails_ShouldNotThrow()
    {
        var reportRoot = CreateReportRoot();
        using var errorWriter = new StringWriter();

        try
        {
            using var runtime = CrashReportRuntime.CreateBestEffort(
                StartupTimingTrace.Disabled,
                errorWriter,
                storeFactory: () => CreateStore(reportRoot, new SerializationFailingArtifactWriter()));

            var exception = Record.Exception(() =>
                runtime.CaptureFatal(new InvalidOperationException("fatal game failure")));

            Assert.Null(exception);
            Assert.Contains("crash_reporting_capture_failed", errorWriter.ToString());
        }
        finally
        {
            DeleteReportRoot(reportRoot);
        }
    }

    [Fact]
    public void Dispose_ShouldOwnLoggerFactoryAndDisposeItExactlyOnce()
    {
        var loggerFactory = new Mock<ILoggerFactory>();
        var runtime = new CrashReportRuntime(
            loggerFactory.Object,
            crashLogBufferProvider: null,
            crashBreadcrumbBuffer: null,
            crashContextSnapshotStore: null,
            crashReportStore: null,
            errorWriter: TextWriter.Null);

        runtime.Dispose();
        runtime.Dispose();

        loggerFactory.Verify(factory => factory.Dispose(), Times.Once);
    }

    [Fact]
    public void CreateBestEffort_WhenSetupFails_ShouldNotAllocateReportFiles()
    {
        var reportRoot = CreateReportRoot();
        using var errorWriter = new StringWriter();

        try
        {
            using var runtime = CrashReportRuntime.CreateBestEffort(
                StartupTimingTrace.Disabled,
                errorWriter,
                storeFactory: () =>
                {
                    _ = CreateStore(reportRoot);
                    throw new UnauthorizedAccessException("store setup failed");
                });

            Assert.False(runtime.IsCaptureEnabled);
            Assert.False(Directory.Exists(reportRoot));
            Assert.Contains("crash_reporting_disabled", errorWriter.ToString());
        }
        finally
        {
            DeleteReportRoot(reportRoot);
        }
    }

    [Fact]
    public void CreateBestEffort_WhenStoreFactoryThrowsOutOfMemory_ShouldNotDegrade()
    {
        Assert.Throws<OutOfMemoryException>(() => CrashReportRuntime.CreateBestEffort(
            StartupTimingTrace.Disabled,
            TextWriter.Null,
            storeFactory: () => throw new OutOfMemoryException("fatal allocation failure")));
    }

    private static CrashReportStore CreateStore(
        string reportRoot,
        ICrashReportArtifactWriter? writer = null)
    {
        return new CrashReportStore(
            reportRoot,
            writer ?? new CrashReportArchiveWriter(),
            TimeProvider.System,
            TextWriter.Null);
    }

    private static string CreateReportRoot()
    {
        return Path.Combine(Path.GetTempPath(), "dtx-crash-runtime-" + Guid.NewGuid().ToString("N"));
    }

    private static void DeleteReportRoot(string reportRoot)
    {
        if (Directory.Exists(reportRoot))
        {
            Directory.Delete(reportRoot, recursive: true);
        }
    }

    private sealed class SerializationFailingArtifactWriter : ICrashReportArtifactWriter
    {
        public void WriteZip(Stream destination, CrashReportDocument document)
        {
            throw new JsonException("serialization failed");
        }

        public void WriteEmergencyText(Stream destination, CrashReportDocument document)
        {
            throw new JsonException("serialization failed");
        }
    }
}
