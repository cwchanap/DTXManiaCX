#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using DTXMania.Game.Lib.Diagnostics.CrashReporting;
using DTXMania.Game.Lib.Stage;
using DTXMania.Test.TestData;
using Microsoft.Extensions.Logging;

namespace DTXMania.Test.CrashReporting;

[Trait("Category", "Unit")]
public sealed class CrashReportStoreTests
{
    [Fact]
    public void Capture_ShouldWriteASingleTextReportAndReturnItsSummary()
    {
        using var fixture = CrashStoreFixture.Create();
        var store = fixture.CreateStore();

        var result = store.Capture(fixture.CreateCapture(0));

        Assert.Null(result.FailureCode);
        Assert.NotNull(result.Report);
        var report = result.Report!;
        var file = Assert.Single(Directory.EnumerateFiles(fixture.RootPath));
        Assert.Equal(report.FileName, Path.GetFileName(file));
        Assert.EndsWith(CrashReportStore.ReportExtension, report.FileName, StringComparison.Ordinal);
        Assert.StartsWith("crash-20260802-120000Z-", report.ReportId, StringComparison.Ordinal);
    }

    [Fact]
    public void Capture_ShouldLeaveNoTemporaryFileBehind()
    {
        using var fixture = CrashStoreFixture.Create();

        fixture.CreateStore().Capture(fixture.CreateCapture(0));

        Assert.Empty(Directory.EnumerateFiles(fixture.RootPath, "*.tmp"));
        Assert.Equal(
            Path.Combine(fixture.RootPath, "." + fixture.ArtifactWriter.LastFileName + ".tmp"),
            fixture.ArtifactWriter.LastDestinationPath);
    }

    [Fact]
    public void Capture_ShouldRecordBuildAndPlatformMetadata()
    {
        using var fixture = CrashStoreFixture.Create();

        var report = fixture.CreateStore().Capture(fixture.CreateCapture(0)).Report;

        Assert.NotNull(report);
        Assert.False(string.IsNullOrWhiteSpace(report!.BuildId));
        Assert.False(string.IsNullOrWhiteSpace(report.OperatingSystem));
        Assert.False(string.IsNullOrWhiteSpace(report.ProcessArchitecture));
        Assert.Equal(typeof(InvalidOperationException).FullName, report.ExceptionType);
    }

    [Fact]
    public void Capture_WhenStartupMilestonePrecedesActiveStage_ShouldPreferActiveStage()
    {
        using var fixture = CrashStoreFixture.Create();
        var capture = fixture.CreateCapture(0) with
        {
            Context =
            [
                new CrashContextSnapshot(
                    CrashContextKind.Startup,
                    CrashContextStatus.Available,
                    new Dictionary<string, object?>
                    {
                        ["Milestone"] = StartupCriticalPathMilestone.StartupActivation
                    }),
                new CrashContextSnapshot(
                    CrashContextKind.Stage,
                    CrashContextStatus.Available,
                    new Dictionary<string, object?> { ["Stage"] = StageType.SongSelect })
            ]
        };

        var report = fixture.CreateStore().Capture(capture).Report;

        Assert.Equal(nameof(StageType.SongSelect), report!.StageOrMilestone);
    }

    [Fact]
    public void Capture_WithOnlyAStartupMilestone_ShouldReportTheMilestone()
    {
        using var fixture = CrashStoreFixture.Create();
        var capture = fixture.CreateCapture(0) with
        {
            Context =
            [
                new CrashContextSnapshot(
                    CrashContextKind.Startup,
                    CrashContextStatus.Available,
                    new Dictionary<string, object?>
                    {
                        ["Milestone"] = StartupCriticalPathMilestone.StartupActivation
                    })
            ]
        };

        var report = fixture.CreateStore().Capture(capture).Report;

        Assert.Equal(nameof(StartupCriticalPathMilestone.StartupActivation), report!.StageOrMilestone);
    }

    [Fact]
    public void Capture_WithNoUsableContext_ShouldReportUnknown()
    {
        using var fixture = CrashStoreFixture.Create();
        var capture = fixture.CreateCapture(0) with { Context = [] };

        var report = fixture.CreateStore().Capture(capture).Report;

        Assert.Equal("Unknown", report!.StageOrMilestone);
    }

    [Fact]
    public void Capture_ShouldIgnoreStageContextThatIsNotAvailable()
    {
        using var fixture = CrashStoreFixture.Create();
        var capture = fixture.CreateCapture(0) with
        {
            Context =
            [
                new CrashContextSnapshot(
                    CrashContextKind.Stage,
                    CrashContextStatus.Unavailable,
                    new Dictionary<string, object?> { ["Stage"] = StageType.SongSelect })
            ]
        };

        var report = fixture.CreateStore().Capture(capture).Report;

        Assert.Equal("Unknown", report!.StageOrMilestone);
    }

    [Fact]
    public void Capture_SixReports_ShouldRetainTheNewestFive()
    {
        using var fixture = CrashStoreFixture.Create();
        var store = fixture.CreateStore();

        var reportIds = new List<string>();
        for (var index = 0; index < 6; index++)
        {
            reportIds.Add(store.Capture(fixture.CreateCapture(index)).Report!.ReportId);
            fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        }

        var remaining = Directory.EnumerateFiles(fixture.RootPath)
            .Select(Path.GetFileNameWithoutExtension)
            .ToArray();

        Assert.Equal(5, remaining.Length);
        Assert.DoesNotContain(reportIds[0], remaining);
        Assert.Contains(reportIds[5], remaining);
    }

    [Fact]
    public void Cleanup_ShouldDeleteOnlyTemporaryFilesOlderThanTwentyFourHours()
    {
        using var fixture = CrashStoreFixture.Create();
        var stalePath = Path.Combine(fixture.RootPath, ".stale.tmp");
        var freshPath = Path.Combine(fixture.RootPath, ".fresh.tmp");
        File.WriteAllText(stalePath, "stale");
        File.WriteAllText(freshPath, "fresh");
        File.SetLastWriteTimeUtc(stalePath, fixture.Clock.GetUtcNow().UtcDateTime.AddHours(-25));
        File.SetLastWriteTimeUtc(freshPath, fixture.Clock.GetUtcNow().UtcDateTime.AddHours(-1));

        fixture.CreateStore().Cleanup();

        Assert.False(File.Exists(stalePath));
        Assert.True(File.Exists(freshPath));
    }

    [Fact]
    public void Cleanup_ShouldIgnoreFilesThatAreNotCrashReports()
    {
        using var fixture = CrashStoreFixture.Create();
        var unrelatedPath = Path.Combine(fixture.RootPath, "notes.txt");
        File.WriteAllText(unrelatedPath, "keep me");
        var store = fixture.CreateStore();

        for (var index = 0; index < 6; index++)
        {
            store.Capture(fixture.CreateCapture(index));
            fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        }

        Assert.True(File.Exists(unrelatedPath));
    }

    [Fact]
    public void Cleanup_WhenRootDirectoryDoesNotExist_ShouldNotThrow()
    {
        using var fixture = CrashStoreFixture.Create();
        Directory.Delete(fixture.RootPath, recursive: true);

        fixture.CreateStore().Cleanup();
    }

    [Fact]
    public void Capture_WhenRootCannotBeCreated_ShouldReturnSafeFailureWithoutThrowing()
    {
        using var fixture = CrashStoreFixture.Create();
        var blockedRoot = Path.Combine(fixture.RootPath, "blocked");
        File.WriteAllText(blockedRoot, "not a directory");
        var errors = new StringWriter(CultureInfo.InvariantCulture);
        var store = new CrashReportStore(
            blockedRoot,
            fixture.ArtifactWriter,
            fixture.Clock,
            errors);

        var result = store.Capture(fixture.CreateCapture(0));

        Assert.Null(result.Report);
        Assert.NotNull(result.FailureCode);
        Assert.Contains("crash_report_capture_failed", errors.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Capture_WhenWriterThrows_ShouldReturnFailureWithoutThrowing()
    {
        using var fixture = CrashStoreFixture.Create();
        fixture.ArtifactWriter.Fail = true;
        var store = fixture.CreateStore();

        var result = store.Capture(fixture.CreateCapture(0));

        Assert.Null(result.Report);
        Assert.Equal("capture_io_failure", result.FailureCode);
        Assert.Empty(Directory.EnumerateFiles(fixture.RootPath));
    }

    [Fact]
    public void Capture_WhenWriterThrowsNonFileSystemException_ShouldLeaveNoTemporaryFile()
    {
        using var fixture = CrashStoreFixture.Create();
        fixture.ArtifactWriter.ThrowInstead = new InvalidOperationException("writer bug");
        var store = fixture.CreateStore();

        // A non-filesystem failure from the writer is not absorbed by the store; it
        // propagates so the runtime can record a generic capture failure. The temp file
        // must still be cleaned up so it cannot linger on disk indefinitely.
        Assert.Throws<InvalidOperationException>(() => store.Capture(fixture.CreateCapture(0)));

        Assert.Empty(Directory.EnumerateFiles(fixture.RootPath, "*.tmp"));
        Assert.Empty(Directory.EnumerateFiles(fixture.RootPath, "*.txt"));
    }

    [Fact]
    public void Capture_WithNullData_ShouldThrow()
    {
        using var fixture = CrashStoreFixture.Create();

        Assert.Throws<ArgumentNullException>(() => fixture.CreateStore().Capture(null!));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WithBlankRootPath_ShouldThrow(string rootPath)
    {
        Assert.Throws<ArgumentException>(() => new CrashReportStore(
            rootPath,
            new CrashReportTextWriter(),
            TimeProvider.System,
            TextWriter.Null));
    }

    [Fact]
    public void Constructor_WithNullDependencies_ShouldThrow()
    {
        Assert.Throws<ArgumentNullException>(() => new CrashReportStore(
            "root", null!, TimeProvider.System, TextWriter.Null));
        Assert.Throws<ArgumentNullException>(() => new CrashReportStore(
            "root", new CrashReportTextWriter(), null!, TextWriter.Null));
        Assert.Throws<ArgumentNullException>(() => new CrashReportStore(
            "root", new CrashReportTextWriter(), TimeProvider.System, null!));
    }

    [Theory]
    [InlineData("crash-20260806-123456Z-a1b2c3.txt", "crash-20260806-123456Z-a1b2c3")]
    [InlineData("crash-20260806-123456Z-a1b2c3.ack.txt", "crash-20260806-123456Z-a1b2c3")]
    [InlineData("CRASH-20260806-123456Z-A1B2C3.ACK.TXT", "CRASH-20260806-123456Z-A1B2C3")]
    public void GetLogicalReportId_ShouldStripAckAndTxtSuffixes(string fileName, string expected)
    {
        Assert.Equal(expected, CrashReportSummaryReader.GetLogicalReportId(fileName));
    }

    [Theory]
    [InlineData("crash-20260806-123456Z-a1b2c3.txt", true)]
    [InlineData("crash-20260806-123456Z-a1b2c3.ack.txt", true)]
    [InlineData("crash-20260806-123456Z-A1B2C3.ACK.TXT", true)]
    [InlineData("crash-20260806-123456Z-a1b2c3.tmp", false)]
    [InlineData(".crash-20260806-123456Z-a1b2c3.txt.tmp", false)]
    [InlineData("crash-20260806-123456Z-a1b2c3.txt.bak", false)]
    [InlineData("crash-2026086-123456Z-a1b2c3.txt", false)]
    [InlineData("notes.txt", false)]
    public void IsRetainedReport_ShouldAcceptPendingAndAckVariantsOnly(string fileName, bool expected)
    {
        Assert.Equal(expected, CrashReportStore.IsRetainedReport(fileName));
    }

    [Fact]
    public void RootPath_ShouldExposeTheConfiguredRootForComposition()
    {
        using var fixture = CrashStoreFixture.Create();

        Assert.Equal(fixture.RootPath, fixture.CreateStore().RootPath);
    }

    [Fact]
    public void DiscoverReports_WhenRootIsEmpty_ShouldReturnAnEmptyList()
    {
        using var fixture = CrashStoreFixture.Create();

        Assert.Empty(fixture.CreateStore().DiscoverReports());
    }

    [Fact]
    public void DiscoverReports_ShouldReturnNewestFirstByLogicalId()
    {
        using var fixture = CrashStoreFixture.Create();
        var store = fixture.CreateStore();

        var ids = new List<string>();
        for (var index = 0; index < 3; index++)
        {
            ids.Add(store.Capture(fixture.CreateCapture(index)).Report!.ReportId);
            fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        }

        var discovered = store.DiscoverReports();

        Assert.Equal(3, discovered.Count);
        Assert.Equal(ids[2], discovered[0].Summary.ReportId);
        Assert.Equal(ids[1], discovered[1].Summary.ReportId);
        Assert.Equal(ids[0], discovered[2].Summary.ReportId);
        Assert.False(discovered[0].IsAcknowledged);
    }

    [Fact]
    public void DiscoverReports_WithBothVariants_ShouldReturnOneLogicalItemWithPendingWinning()
    {
        using var fixture = CrashStoreFixture.Create();
        var store = fixture.CreateStore();
        var captured = store.Capture(fixture.CreateCapture(0)).Report!;
        var reportId = captured.ReportId;
        var pendingPath = Path.Combine(fixture.RootPath, captured.FileName);
        var ackPath = AckPath(fixture.RootPath, reportId);
        File.Copy(pendingPath, ackPath);

        var items = store.DiscoverReports();

        var item = Assert.Single(items);
        Assert.False(item.IsAcknowledged);
        Assert.Equal(captured.FileName, item.Summary.FileName);
        Assert.Equal(reportId, item.Summary.ReportId);
    }

    [Fact]
    public void DiscoverReports_WithOnlyAckVariant_ShouldReportAcknowledged()
    {
        using var fixture = CrashStoreFixture.Create();
        var store = fixture.CreateStore();
        var captured = store.Capture(fixture.CreateCapture(0)).Report!;
        var reportId = captured.ReportId;
        File.Move(Path.Combine(fixture.RootPath, captured.FileName), AckPath(fixture.RootPath, reportId));

        var items = store.DiscoverReports();

        var item = Assert.Single(items);
        Assert.True(item.IsAcknowledged);
        Assert.Equal(reportId + ".ack" + CrashReportStore.ReportExtension, item.Summary.FileName);
    }

    [Fact]
    public void Acknowledge_WithStaleAckTwin_ShouldOverwriteAndLeaveOneAcknowledgedArtifact()
    {
        using var fixture = CrashStoreFixture.Create();
        var store = fixture.CreateStore();
        var captured = store.Capture(fixture.CreateCapture(0)).Report!;
        var reportId = captured.ReportId;
        var pendingPath = Path.Combine(fixture.RootPath, captured.FileName);
        var ackPath = AckPath(fixture.RootPath, reportId);
        File.Copy(pendingPath, ackPath);

        var result = store.Acknowledge(reportId);

        Assert.True(result.Succeeded);
        Assert.Null(result.ErrorCode);
        Assert.False(File.Exists(pendingPath));
        Assert.True(File.Exists(ackPath));
        var item = Assert.Single(store.DiscoverReports());
        Assert.True(item.IsAcknowledged);
    }

    [Fact]
    public void Acknowledge_WhenAlreadyAcknowledged_ShouldBeIdempotentSuccess()
    {
        using var fixture = CrashStoreFixture.Create();
        var store = fixture.CreateStore();
        var reportId = store.Capture(fixture.CreateCapture(0)).Report!.ReportId;

        var first = store.Acknowledge(reportId);
        var second = store.Acknowledge(reportId);

        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded);
        Assert.True(File.Exists(AckPath(fixture.RootPath, reportId)));
    }

    [Fact]
    public void Acknowledge_WhenNoVariantExists_ShouldBeIdempotentSuccess()
    {
        using var fixture = CrashStoreFixture.Create();

        var result = fixture.CreateStore().Acknowledge("crash-20260806-123456Z-deadbe");

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Delete_WithBothVariants_ShouldRemoveBothArtifacts()
    {
        using var fixture = CrashStoreFixture.Create();
        var store = fixture.CreateStore();
        var captured = store.Capture(fixture.CreateCapture(0)).Report!;
        var reportId = captured.ReportId;
        var pendingPath = Path.Combine(fixture.RootPath, captured.FileName);
        var ackPath = AckPath(fixture.RootPath, reportId);
        File.Copy(pendingPath, ackPath);

        var result = store.DeleteReport(reportId);

        Assert.True(result.Succeeded);
        Assert.False(File.Exists(pendingPath));
        Assert.False(File.Exists(ackPath));
        Assert.Empty(store.DiscoverReports());
    }

    [Fact]
    public void Cleanup_WithPendingAckPair_ShouldCountThePairAsOneLogicalReport()
    {
        using var fixture = CrashStoreFixture.Create();
        var store = fixture.CreateStore();

        var ids = new List<string>();
        for (var index = 0; index < 6; index++)
        {
            ids.Add(store.Capture(fixture.CreateCapture(index)).Report!.ReportId);
            fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        }

        // The newest report receives an ack twin -> 7 physical files, 6 logical reports.
        var newestPending = Path.Combine(fixture.RootPath, ids[5] + CrashReportStore.ReportExtension);
        var newestAck = AckPath(fixture.RootPath, ids[5]);
        File.Copy(newestPending, newestAck);

        store.Cleanup();

        var discovered = store.DiscoverReports();
        Assert.Equal(5, discovered.Count);
        // Physical retention (the old policy) would have deleted ids[1] because the ack twin
        // sorts before its pending sibling. Logical retention keeps it.
        Assert.False(discovered.Any(item => item.Summary.ReportId == ids[0]));
        Assert.True(discovered.Any(item => item.Summary.ReportId == ids[1]));
        Assert.True(discovered.Any(item => item.Summary.ReportId == ids[5]));
        Assert.True(File.Exists(newestPending));
        Assert.True(File.Exists(newestAck));
    }

    private static string AckPath(string rootPath, string reportId)
    {
        return Path.Combine(rootPath, reportId + ".ack" + CrashReportStore.ReportExtension);
    }

    private sealed class CrashStoreFixture : IDisposable
    {
        private CrashStoreFixture()
        {
            RootPath = Path.Combine(Path.GetTempPath(), "dtx-crash-store-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(RootPath);
            Clock = new ManualTimeProvider(new DateTimeOffset(2026, 8, 2, 12, 0, 0, TimeSpan.Zero));
            ArtifactWriter = new FakeArtifactWriter();
        }

        internal string RootPath { get; }

        internal ManualTimeProvider Clock { get; }

        internal FakeArtifactWriter ArtifactWriter { get; }

        internal static CrashStoreFixture Create() => new();

        internal CrashReportStore CreateStore()
        {
            return new CrashReportStore(
                RootPath,
                ArtifactWriter,
                Clock,
                new StringWriter(CultureInfo.InvariantCulture));
        }

        internal CrashCaptureData CreateCapture(int index)
        {
            return new CrashCaptureData(
                new InvalidOperationException($"failure {index}"),
                [
                    new CrashLogRecord(
                        Clock.GetUtcNow(),
                        LogLevel.Information,
                        CrashLogEvents.StageTransitionCompleted.EventId,
                        CrashLogEvents.StageTransitionCompleted.MessageTemplate,
                        new Dictionary<string, object?> { ["TargetStage"] = StageType.Title },
                        ExceptionType: null)
                ],
                [
                    new CrashBreadcrumb(
                        Clock.GetUtcNow(),
                        "stage_transition_completed",
                        new Dictionary<string, object?> { ["TargetStage"] = StageType.Title })
                ],
                [
                    new CrashContextSnapshot(
                        CrashContextKind.Stage,
                        CrashContextStatus.Available,
                        new Dictionary<string, object?> { ["Stage"] = StageType.Title })
                ],
                [Path.Combine(RootPath, "songs")],
                []);
        }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }

    private sealed class FakeArtifactWriter : ICrashReportArtifactWriter
    {
        private readonly CrashReportTextWriter _inner = new();

        internal bool Fail { get; set; }

        internal Exception? ThrowInstead { get; set; }

        internal string? LastDestinationPath { get; private set; }

        internal string? LastFileName { get; private set; }

        public void Write(Stream destination, CrashReportDocument document)
        {
            LastDestinationPath = (destination as FileStream)?.Name;
            LastFileName = document.Summary.FileName;

            if (ThrowInstead is not null)
            {
                throw ThrowInstead;
            }

            if (Fail)
            {
                throw new IOException("Simulated writer failure.");
            }

            _inner.Write(destination, document);
        }
    }
}
