#nullable enable

using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
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
            errorWriter,
            storeFactory: () => throw new UnauthorizedAccessException("denied"));

        var logger = runtime.GameDiagnostics.LoggerFactory.CreateLogger("probe");
        var exception = Record.Exception(() => logger.LogInformation("console survives"));

        Assert.Null(exception);
        Assert.False(runtime.IsCaptureEnabled);
        Assert.Contains("crash_reporting_disabled", errorWriter.ToString());
    }

    [Fact]
    public void CreateBestEffort_ShouldRemoveStaleTemporaryFilesAtStartup()
    {
        var reportRoot = CreateReportRoot();
        Directory.CreateDirectory(reportRoot);

        try
        {
            // Simulate a .tmp file left behind by an interrupted previous run (e.g. process
            // killed mid-write before the finally block could clean it up). Mark it older than
            // the 24-hour stale threshold so startup cleanup removes it without another crash.
            var staleTempPath = Path.Combine(reportRoot, ".crash-stale.tmp");
            File.WriteAllText(staleTempPath, "interrupted");
            File.SetLastWriteTimeUtc(staleTempPath, DateTime.UtcNow.Subtract(TimeSpan.FromHours(48)));

            using var runtime = CrashReportRuntime.CreateBestEffort(
                TextWriter.Null,
                storeFactory: () => CreateStore(reportRoot));

            Assert.True(runtime.IsCaptureEnabled);
            Assert.False(File.Exists(staleTempPath));
            Assert.Empty(Directory.EnumerateFiles(reportRoot, "*.tmp"));
        }
        finally
        {
            DeleteReportRoot(reportRoot);
        }
    }

    [Fact]
    public void CreateBestEffort_WhenSetupSucceeds_ShouldEnableCrashBufferAndPreserveLogging()
    {
        var reportRoot = CreateReportRoot();

        try
        {
            using var runtime = CrashReportRuntime.CreateBestEffort(
                TextWriter.Null,
                storeFactory: () => CreateStore(reportRoot));
            var logger = runtime.GameDiagnostics.LoggerFactory.CreateLogger("probe");

            logger.LogCrashEvent(
                LogLevel.Information,
                CrashLogEvents.StageTransitionCompleted,
                StageType.Startup,
                StageType.Title);

            var provider = Assert.IsType<CrashLogBufferProvider>(runtime.CrashLogBufferProvider);
            var record = Assert.Single(provider.Snapshot());
            Assert.True(runtime.IsCaptureEnabled);
            Assert.Equal(StageType.Title, record.Properties["TargetStage"]);
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
    public void CaptureFatal_ShouldPersistCachedProcessAndApplicationContext()
    {
        var reportRoot = CreateReportRoot();

        try
        {
            using var runtime = CrashReportRuntime.CreateBestEffort(
                TextWriter.Null,
                storeFactory: () => CreateStore(reportRoot));

            runtime.CaptureFatal(new InvalidOperationException("fatal game failure"));

            var reportPath = Assert.Single(
                Directory.EnumerateFiles(reportRoot, "*" + CrashReportStore.ReportExtension));
            var allText = File.ReadAllText(reportPath);

            Assert.Contains("Process [Available]", allText, StringComparison.Ordinal);
            Assert.Contains("  RuntimeFramework: ", allText, StringComparison.Ordinal);
            Assert.Contains("  OperatingSystem: ", allText, StringComparison.Ordinal);
            Assert.Contains("  ProcessArchitecture: ", allText, StringComparison.Ordinal);
            Assert.Contains("  ProcessStartUtc: ", allText, StringComparison.Ordinal);
            Assert.Contains("Application [Available]", allText, StringComparison.Ordinal);
            Assert.Contains("  ApplicationVersion: ", allText, StringComparison.Ordinal);
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
            TextWriter.Null,
            storeFactory: () => throw new OutOfMemoryException("fatal allocation failure")));
    }

    [Fact]
    public void CaptureFatal_WhenCaptureDisabled_ShouldReturnWithoutThrowing()
    {
        using var errorWriter = new StringWriter();
        var runtime = new CrashReportRuntime(
            Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance,
            crashLogBufferProvider: null,
            crashBreadcrumbBuffer: null,
            crashContextSnapshotStore: null,
            crashReportStore: null,
            errorWriter);

        var exception = Record.Exception(
            () => runtime.CaptureFatal(new InvalidOperationException("fatal")));

        Assert.Null(exception);
        Assert.False(runtime.IsCaptureEnabled);
        Assert.DoesNotContain("crash_reporting_capture_failed", errorWriter.ToString());
    }

    [Fact]
    public void CaptureFatal_WithNullException_ShouldThrow()
    {
        using var runtime = CrashReportRuntime.CreateBestEffort(
            TextWriter.Null,
            storeFactory: () => throw new UnauthorizedAccessException("denied"));

        Assert.Throws<ArgumentNullException>(() => runtime.CaptureFatal(null!));
    }

    [Fact]
    public void RecordSecondaryFailure_WithKnownCode_ShouldWriteNormalizedCode()
    {
        using var errorWriter = new StringWriter();
        var runtime = new CrashReportRuntime(
            Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance,
            crashLogBufferProvider: null,
            crashBreadcrumbBuffer: null,
            crashContextSnapshotStore: null,
            crashReportStore: null,
            errorWriter);

        runtime.RecordSecondaryFailure("crash_capture_failed");
        runtime.RecordSecondaryFailure("game_dispose_failed");
        runtime.RecordSecondaryFailure("runtime_dispose_failed");
        runtime.RecordSecondaryFailure("unknown_code");

        var output = errorWriter.ToString();
        Assert.Contains("code=crash_capture_failed", output);
        Assert.Contains("code=game_dispose_failed", output);
        Assert.Contains("code=runtime_dispose_failed", output);
        Assert.Contains("code=secondary_failure", output);
    }

    [Fact]
    public void GameDiagnostics_ShouldExposeBreadcrumbsContextsAndLogging()
    {
        using var runtime = CrashReportRuntime.CreateBestEffort(
            TextWriter.Null,
            storeFactory: () => throw new UnauthorizedAccessException("denied"));

        Assert.NotNull(runtime.GameDiagnostics.Breadcrumbs);
        Assert.NotNull(runtime.GameDiagnostics.Contexts);
        Assert.NotNull(runtime.GameDiagnostics.SensitiveData);
        Assert.NotNull(runtime.GameDiagnostics.LoggerFactory);
    }

    [Fact]
    public void GameDiagnostics_WhenCaptureEnabled_ShouldExposeProductionCrashReportInbox()
    {
        // An enabled runtime composes the production inbox over the runtime-owned store and
        // exposes it through the single IGameCrashDiagnostics.CrashReportInbox seam. The store,
        // launcher, parser, and root path must stay unexposed: only the inbox contract is.
        var reportRoot = CreateReportRoot();

        try
        {
            using var runtime = CrashReportRuntime.CreateBestEffort(
                TextWriter.Null,
                storeFactory: () => CreateStore(reportRoot));

            Assert.True(runtime.IsCaptureEnabled);
            var inbox = runtime.GameDiagnostics.CrashReportInbox;
            Assert.NotSame(EmptyCrashReportInbox.Instance, inbox);
            Assert.IsType<CrashReportInbox>(inbox);
        }
        finally
        {
            DeleteReportRoot(reportRoot);
        }
    }

    [Fact]
    public void CrashReportInbox_WhenCaptureEnabled_ShouldReflectReportsCapturedThroughTheRuntime()
    {
        // The composed inbox must reuse the runtime-owned CrashReportStore: a report captured via
        // the runtime appears in the inbox without any re-wiring, proving the two share one store
        // (and that store.RootPath drives the folder handoff rather than a divergent path).
        var reportRoot = CreateReportRoot();

        try
        {
            using var runtime = CrashReportRuntime.CreateBestEffort(
                TextWriter.Null,
                storeFactory: () => CreateStore(reportRoot));

            runtime.CaptureFatal(new InvalidOperationException("title-stage crash"));

            var reports = runtime.GameDiagnostics.CrashReportInbox.GetReports();
            var item = Assert.Single(reports);
            Assert.StartsWith("crash-", item.Summary.ReportId, StringComparison.Ordinal);
            Assert.False(item.IsAcknowledged);
        }
        finally
        {
            DeleteReportRoot(reportRoot);
        }
    }

    [Fact]
    public void GameDiagnostics_WhenBootstrapFails_ShouldExposeEmptyCrashReportInboxFacade()
    {
        // A bootstrap-degraded runtime has no store and must fall back to the null-object inbox
        // facade so the title stage never observes a null inbox or a half-built production one.
        using var runtime = CrashReportRuntime.CreateBestEffort(
            new StringWriter(),
            storeFactory: () => throw new UnauthorizedAccessException("denied"));

        Assert.False(runtime.IsCaptureEnabled);
        Assert.Same(EmptyCrashReportInbox.Instance, runtime.GameDiagnostics.CrashReportInbox);
    }

    [Fact]
    public void GameDiagnostics_WhenConstructedDirectlyWithoutStore_ShouldExposeEmptyCrashReportInboxFacade()
    {
        // The internal constructor path used by tests/disabled construction also exposes the
        // facade whenever no store is wired, matching the bootstrap-degraded behavior.
        using var runtime = new CrashReportRuntime(
            Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance,
            crashLogBufferProvider: null,
            crashBreadcrumbBuffer: null,
            crashContextSnapshotStore: null,
            crashReportStore: null,
            new StringWriter());

        Assert.False(runtime.IsCaptureEnabled);
        Assert.Same(EmptyCrashReportInbox.Instance, runtime.GameDiagnostics.CrashReportInbox);
    }

    [Fact]
    public void Constructor_WithNullLoggerFactory_ShouldThrow()
    {
        Assert.Throws<ArgumentNullException>(() => new CrashReportRuntime(
            null!,
            crashLogBufferProvider: null,
            crashBreadcrumbBuffer: null,
            crashContextSnapshotStore: null,
            crashReportStore: null,
            TextWriter.Null));
    }

    [Fact]
    public void Constructor_WithNullErrorWriter_ShouldThrow()
    {
        Assert.Throws<ArgumentNullException>(() => new CrashReportRuntime(
            Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance,
            crashLogBufferProvider: null,
            crashBreadcrumbBuffer: null,
            crashContextSnapshotStore: null,
            crashReportStore: null,
            errorWriter: null!));
    }

    private static CrashReportStore CreateStore(
        string reportRoot,
        ICrashReportArtifactWriter? writer = null)
    {
        return new CrashReportStore(
            reportRoot,
            writer ?? new CrashReportTextWriter(),
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
        public void Write(Stream destination, CrashReportDocument document)
        {
            throw new IOException("serialization failed");
        }
    }
}
