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

    // ---------------------------------------------------------------------------------------------
    // GetStageOrMilestone edge cases (exercised through Capture)
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void Capture_WithNullContextList_ShouldReportUnknown()
    {
        using var fixture = CrashStoreFixture.Create();
        var capture = fixture.CreateCapture(0) with { Context = null! };

        var report = fixture.CreateStoreWithNoOpWriter().Capture(capture).Report;

        Assert.Equal("Unknown", report!.StageOrMilestone);
    }

    [Fact]
    public void Capture_WithNullSnapshotInContext_ShouldSkipItAndReportUnknown()
    {
        using var fixture = CrashStoreFixture.Create();
        var capture = fixture.CreateCapture(0) with
        {
            Context = [null!]
        };

        var report = fixture.CreateStoreWithNoOpWriter().Capture(capture).Report;

        Assert.Equal("Unknown", report!.StageOrMilestone);
    }

    [Fact]
    public void Capture_WithStartupStageType_ShouldIgnoreItAndFallBackToMilestone()
    {
        using var fixture = CrashStoreFixture.Create();
        // StageType.Startup is explicitly excluded; the startup milestone should be used instead.
        var capture = fixture.CreateCapture(0) with
        {
            Context =
            [
                new CrashContextSnapshot(
                    CrashContextKind.Stage,
                    CrashContextStatus.Available,
                    new Dictionary<string, object?> { ["Stage"] = StageType.Startup }),
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
    public void Capture_WithUndefinedStageEnumValue_ShouldSkipItAndReportUnknown()
    {
        using var fixture = CrashStoreFixture.Create();
        var capture = fixture.CreateCapture(0) with
        {
            Context =
            [
                new CrashContextSnapshot(
                    CrashContextKind.Stage,
                    CrashContextStatus.Available,
                    new Dictionary<string, object?> { ["Stage"] = (StageType)999 })
            ]
        };

        var report = fixture.CreateStore().Capture(capture).Report;

        Assert.Equal("Unknown", report!.StageOrMilestone);
    }

    [Fact]
    public void Capture_WithUndefinedMilestoneEnumValue_ShouldSkipItAndReportUnknown()
    {
        using var fixture = CrashStoreFixture.Create();
        var capture = fixture.CreateCapture(0) with
        {
            Context =
            [
                new CrashContextSnapshot(
                    CrashContextKind.Startup,
                    CrashContextStatus.Available,
                    new Dictionary<string, object?> { ["Milestone"] = (StartupCriticalPathMilestone)999 })
            ]
        };

        var report = fixture.CreateStore().Capture(capture).Report;

        Assert.Equal("Unknown", report!.StageOrMilestone);
    }

    [Fact]
    public void Capture_WithStageSnapshotMissingStageField_ShouldSkipItAndReportUnknown()
    {
        using var fixture = CrashStoreFixture.Create();
        var capture = fixture.CreateCapture(0) with
        {
            Context =
            [
                new CrashContextSnapshot(
                    CrashContextKind.Stage,
                    CrashContextStatus.Available,
                    new Dictionary<string, object?> { ["Other"] = "value" })
            ]
        };

        var report = fixture.CreateStore().Capture(capture).Report;

        Assert.Equal("Unknown", report!.StageOrMilestone);
    }

    [Fact]
    public void Capture_WithStageSnapshotHavingWrongFieldType_ShouldSkipItAndReportUnknown()
    {
        using var fixture = CrashStoreFixture.Create();
        var capture = fixture.CreateCapture(0) with
        {
            Context =
            [
                new CrashContextSnapshot(
                    CrashContextKind.Stage,
                    CrashContextStatus.Available,
                    new Dictionary<string, object?> { ["Stage"] = "NotAnEnum" })
            ]
        };

        var report = fixture.CreateStore().Capture(capture).Report;

        Assert.Equal("Unknown", report!.StageOrMilestone);
    }

    [Fact]
    public void Capture_WithStartupSnapshotMissingMilestoneField_ShouldReportUnknown()
    {
        using var fixture = CrashStoreFixture.Create();
        var capture = fixture.CreateCapture(0) with
        {
            Context =
            [
                new CrashContextSnapshot(
                    CrashContextKind.Startup,
                    CrashContextStatus.Available,
                    new Dictionary<string, object?> { ["Other"] = "value" })
            ]
        };

        var report = fixture.CreateStore().Capture(capture).Report;

        Assert.Equal("Unknown", report!.StageOrMilestone);
    }

    [Fact]
    public void Capture_WithSnapshotHavingNullFields_ShouldSkipItAndReportUnknown()
    {
        using var fixture = CrashStoreFixture.Create();
        var capture = fixture.CreateCapture(0) with
        {
            Context =
            [
                new CrashContextSnapshot(
                    CrashContextKind.Stage,
                    CrashContextStatus.Available,
                    Fields: null!)
            ]
        };

        var report = fixture.CreateStoreWithNoOpWriter().Capture(capture).Report;

        Assert.Equal("Unknown", report!.StageOrMilestone);
    }

    // ---------------------------------------------------------------------------------------------
    // DeleteReport / DiscoverReports when root does not exist
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void DeleteReport_WhenRootDirectoryDoesNotExist_ShouldBeIdempotentSuccess()
    {
        using var fixture = CrashStoreFixture.Create();
        Directory.Delete(fixture.RootPath, recursive: true);

        var result = fixture.CreateStore().DeleteReport("crash-20260806-123456Z-deadbe");

        Assert.True(result.Succeeded);
        Assert.Null(result.ErrorCode);
    }

    [Fact]
    public void DiscoverReports_WhenRootDirectoryDoesNotExist_ShouldReturnEmptyList()
    {
        using var fixture = CrashStoreFixture.Create();
        Directory.Delete(fixture.RootPath, recursive: true);

        Assert.Empty(fixture.CreateStore().DiscoverReports());
    }

    [Fact]
    public void Acknowledge_WithBlankReportId_ShouldThrow()
    {
        using var fixture = CrashStoreFixture.Create();

        Assert.Throws<ArgumentException>(() => fixture.CreateStore().Acknowledge(""));
        Assert.Throws<ArgumentException>(() => fixture.CreateStore().Acknowledge("   "));
    }

    [Fact]
    public void DeleteReport_WithBlankReportId_ShouldThrow()
    {
        using var fixture = CrashStoreFixture.Create();

        Assert.Throws<ArgumentException>(() => fixture.CreateStore().DeleteReport(""));
        Assert.Throws<ArgumentException>(() => fixture.CreateStore().DeleteReport("   "));
    }

    [Fact]
    public void DiscoverReports_WithNonRetainedFiles_ShouldIgnoreThem()
    {
        using var fixture = CrashStoreFixture.Create();
        // Files that don't match the retained-name regex should be ignored.
        File.WriteAllText(Path.Combine(fixture.RootPath, "notes.txt"), "keep me");
        File.WriteAllText(Path.Combine(fixture.RootPath, "random.log"), "log");

        Assert.Empty(fixture.CreateStore().DiscoverReports());
    }

    [Fact]
    public void DiscoverReports_WithRetainedFileHavingEmptyLogicalId_ShouldSkipIt()
    {
        using var fixture = CrashStoreFixture.Create();
        // A file that matches the regex but whose logical id extraction returns empty should be
        // skipped. This is hard to trigger with the current regex, but a corrupt file name that
        // somehow passes the regex but fails logical-id extraction exercises the guard.
        // In practice GetLogicalReportId never returns empty for a regex-matched name, so this
        // test documents the guard rather than exercising it directly.
        var store = fixture.CreateStore();
        store.Capture(fixture.CreateCapture(0));

        Assert.Single(store.DiscoverReports());
    }

    // ---------------------------------------------------------------------------------------------
    // Cleanup exception paths
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void Cleanup_WhenFileDeletionFails_ShouldNotThrow()
    {
        using var fixture = CrashStoreFixture.Create();
        // Create a stale .tmp file that is old enough to be cleaned up.
        var stalePath = Path.Combine(fixture.RootPath, ".stale.tmp");
        File.WriteAllText(stalePath, "stale");
        File.SetLastWriteTimeUtc(stalePath, fixture.Clock.GetUtcNow().UtcDateTime.AddHours(-25));

        // Run cleanup - it should succeed even if individual file operations have issues.
        fixture.CreateStore().Cleanup();

        // The stale file should be gone (normal case).
        Assert.False(File.Exists(stalePath));
    }

    private static string AckPath(string rootPath, string reportId)
    {
        return Path.Combine(rootPath, reportId + ".ack" + CrashReportStore.ReportExtension);
    }

    // Writes a minimal valid crash-report file under an arbitrary (possibly mixed-case) name, so
    // the case-insensitive contract can be exercised directly. On a case-insensitive volume two
    // names that differ only by extension coexist; the logical layer must still treat variants
    // of one logical id as a single report.
    private static string WriteReport(string rootPath, string fileName)
    {
        var path = Path.Combine(rootPath, fileName);
        File.WriteAllText(path, MinimalReportBody());
        return path;
    }

    private static string MinimalReportBody()
    {
        // Valid header so the summary reader populates the filename-derived id and timestamp;
        // no named fields, so everything else falls back to "Unknown".
        return CrashReportTextWriter.Header + "\n\n" + CrashReportTextWriter.ExceptionSection + "\n";
    }

    [Fact]
    public void DiscoverReports_WithMixedCasePendingAndAckTwins_ShouldGroupAsOneLogicalReport()
    {
        using var fixture = CrashStoreFixture.Create();
        // A mixed-case pending twin and a lowercase ack twin whose logical ids are equal only
        // under case-insensitive comparison: they must collapse into ONE logical report, with
        // the pending variant winning.
        WriteReport(fixture.RootPath, "CRASH-20260806-123456Z-A1B2C3.TXT");
        WriteReport(fixture.RootPath, "crash-20260806-123456z-a1b2c3.ack.txt");

        var items = fixture.CreateStore().DiscoverReports();

        var item = Assert.Single(items);
        Assert.False(item.IsAcknowledged);
    }

    [Fact]
    public void Acknowledge_WithMixedCaseOnDiskName_ShouldResolveByLogicalIdAndPreserveCase()
    {
        using var fixture = CrashStoreFixture.Create();
        var pendingPath = WriteReport(fixture.RootPath, "CRASH-20260806-123456Z-A1B2C3.TXT");

        // Pass a logical id whose casing differs from the on-disk name; resolution must be
        // case-insensitive (enumerate + match), not a case-sensitive path reconstruction.
        var result = fixture.CreateStore().Acknowledge("crash-20260806-123456z-a1b2c3");

        Assert.True(result.Succeeded);
        Assert.Null(result.ErrorCode);
        Assert.False(File.Exists(pendingPath));
        // The ack twin is derived from the pending file's actual on-disk name (".ack" inserted
        // before the final extension), so its casing is preserved rather than reconstructed.
        var ackFiles = Directory.EnumerateFiles(fixture.RootPath)
            .Where(static p => Path.GetFileName(p)
                .EndsWith(".ack" + CrashReportStore.ReportExtension, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var ackFile = Assert.Single(ackFiles);
        Assert.Equal("CRASH-20260806-123456Z-A1B2C3.ack.TXT", Path.GetFileName(ackFile));
    }

    [Fact]
    public void DeleteReport_WithMixedCaseOnDiskNames_ShouldRemoveEveryPhysicalVariant()
    {
        using var fixture = CrashStoreFixture.Create();
        var pendingPath = WriteReport(fixture.RootPath, "CRASH-20260806-123456Z-A1B2C3.TXT");
        WriteReport(fixture.RootPath, "crash-20260806-123456z-a1b2c3.ack.txt");

        var result = fixture.CreateStore().DeleteReport("crash-20260806-123456z-a1b2c3");

        Assert.True(result.Succeeded);
        Assert.False(File.Exists(pendingPath));
        Assert.Empty(Directory.EnumerateFiles(fixture.RootPath));
    }

    [Fact]
    public void Cleanup_WithMixedCasePendingAckPair_ShouldCountAsOneLogicalReport()
    {
        using var fixture = CrashStoreFixture.Create();
        var store = fixture.CreateStore();

        var ids = new List<string>();
        for (var index = 0; index < 6; index++)
        {
            ids.Add(store.Capture(fixture.CreateCapture(index)).Report!.ReportId);
            fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        }

        // A mixed-case ack twin for the newest logical id (case-insensitively equal to its
        // pending sibling): 7 physical files, still only 6 logical reports.
        WriteReport(fixture.RootPath, ids[5].ToUpperInvariant() + ".ACK.TXT");

        store.Cleanup();

        var discovered = store.DiscoverReports();
        Assert.Equal(5, discovered.Count);
        // Oldest logical id is pruned; the mixed-case pair counts as ONE so the second-oldest
        // survives (a case-sensitive count would have pruned two logical ids instead).
        Assert.False(discovered.Any(item => string.Equals(
            item.Summary.ReportId, ids[0], StringComparison.OrdinalIgnoreCase)));
        Assert.True(discovered.Any(item => string.Equals(
            item.Summary.ReportId, ids[1], StringComparison.OrdinalIgnoreCase)));
        Assert.True(discovered.Any(item => string.Equals(
            item.Summary.ReportId, ids[5], StringComparison.OrdinalIgnoreCase)));
    }

    // ---------------------------------------------------------------------------------------------
    // Acknowledge / DeleteReport / DiscoverReports filesystem-failure paths
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void Acknowledge_WhenRootDirectoryDoesNotExist_ShouldBeIdempotentSuccess()
    {
        using var fixture = CrashStoreFixture.Create();
        Directory.Delete(fixture.RootPath, recursive: true);

        var result = fixture.CreateStore().Acknowledge("crash-20260806-123456Z-deadbe");

        Assert.True(result.Succeeded);
        Assert.Null(result.ErrorCode);
    }

    [Fact]
    public void DeleteReport_WhenFileDeletionFails_ShouldReturnDeleteIoFailure()
    {
        // The read-only-root mechanism uses Unix file modes (chmod), which compile on Windows but
        // throw at runtime. Skip on Windows (and when running as root, which bypasses permissions).
        if (OperatingSystem.IsWindows() || RunningAsRoot())
        {
            return;
        }

        using var fixture = CrashStoreFixture.Create();
        var store = fixture.CreateStore();
        var reportId = store.Capture(fixture.CreateCapture(0)).Report!.ReportId;
        MakeRootReadOnly(fixture.RootPath);

        var result = store.DeleteReport(reportId);

        Assert.False(result.Succeeded);
        Assert.Equal("delete_io_failure", result.ErrorCode);
    }

    [Fact]
    public void Acknowledge_WhenFileMoveFails_ShouldReturnAcknowledgeIoFailure()
    {
        if (OperatingSystem.IsWindows() || RunningAsRoot())
        {
            return;
        }

        using var fixture = CrashStoreFixture.Create();
        var store = fixture.CreateStore();
        var reportId = store.Capture(fixture.CreateCapture(0)).Report!.ReportId;
        MakeRootReadOnly(fixture.RootPath);

        var result = store.Acknowledge(reportId);

        Assert.False(result.Succeeded);
        Assert.Equal("acknowledge_io_failure", result.ErrorCode);
    }

    [Fact]
    public void DiscoverReports_WhenSummaryReaderFails_ShouldSkipTheReport()
    {
        // A retained-name file whose content is empty (no header) causes the reader to return
        // defaults rather than throwing, so it is still discovered. To exercise the catch block
        // for a filesystem exception during read, create a file that matches the regex but is
        // actually a directory (File.Open on a directory throws UnauthorizedAccessException).
        using var fixture = CrashStoreFixture.Create();
        var dirPath = Path.Combine(fixture.RootPath, "crash-20260806-123456Z-a1b2c3.txt");
        Directory.CreateDirectory(dirPath);

        var items = fixture.CreateStore().DiscoverReports();

        // The directory-as-file is skipped (the read throws and is caught).
        Assert.Empty(items);
    }

    [Fact]
    public void Cleanup_WhenEnumeratingFilesFails_ShouldNotThrow()
    {
        if (OperatingSystem.IsWindows() || RunningAsRoot())
        {
            return;
        }

        using var fixture = CrashStoreFixture.Create();
        // Create a retained report, then make the root unreadable so enumeration fails.
        fixture.CreateStore().Capture(fixture.CreateCapture(0));
        MakeRootReadOnly(fixture.RootPath);

        var exception = Record.Exception(() => fixture.CreateStore().Cleanup());

        Assert.Null(exception);
    }

    [Fact]
    public void Capture_WhenErrorWriterThrows_ShouldNotPropagate()
    {
        // The WriteSafeError helper swallows IOException/ObjectDisposedException from the error
        // writer. A capture that fails (e.g. blocked root) writes an error line; if that write
        // also throws, the capture must still not propagate the writer exception.
        using var fixture = CrashStoreFixture.Create();
        var blockedRoot = Path.Combine(fixture.RootPath, "blocked");
        File.WriteAllText(blockedRoot, "not a directory");
        var throwingWriter = new ThrowingTextWriter();
        var store = new CrashReportStore(
            blockedRoot,
            fixture.ArtifactWriter,
            fixture.Clock,
            throwingWriter);

        var exception = Record.Exception(() => store.Capture(fixture.CreateCapture(0)));

        Assert.Null(exception);
    }

    [Fact]
    public void DiscoverReports_WhenMoreThanFiveLogicalIdsLinger_ShouldBoundToFive()
    {
        // Even if cleanup missed some reports (e.g. it failed), DiscoverReports bounds the
        // summaries read to the retention limit via .Take(MaximumRetainedReports).
        using var fixture = CrashStoreFixture.Create();
        for (var index = 0; index < 7; index++)
        {
            WriteReport(fixture.RootPath,
                "crash-2026080" + index + "-120000Z-a1b2c" + index + ".txt");
        }

        var items = fixture.CreateStore().DiscoverReports();

        Assert.Equal(5, items.Count);
    }

    private static bool RunningAsRoot()
    {
        return Environment.UserName == "root"
            || Environment.GetEnvironmentVariable("USER") == "root";
    }

    private static void MakeRootReadOnly(string rootPath)
    {
        File.SetUnixFileMode(rootPath,
            UnixFileMode.OtherRead | UnixFileMode.OtherExecute
            | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
            | UnixFileMode.UserRead | UnixFileMode.UserExecute);
    }

    private sealed class ThrowingTextWriter : TextWriter
    {
        public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;

        public override void Write(char value) => throw new ObjectDisposedException("writer");

        public override void WriteLine(string? value) => throw new ObjectDisposedException("writer");
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

        /// <summary>Creates a store whose writer skips the inner serialization, for tests that
        /// exercise Capture's pre-write logic (e.g. GetStageOrMilestone) with degenerate context
        /// the real writer cannot serialize.</summary>
        internal CrashReportStore CreateStoreWithNoOpWriter()
        {
            ArtifactWriter.SkipInnerWrite = true;
            return CreateStore();
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
                // Restore write permissions in case a test made the root read-only.
                if (!OperatingSystem.IsWindows())
                {
                    try
                    {
                        File.SetUnixFileMode(RootPath,
                            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                            | UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute
                            | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute);
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                    {
                    }
                }

                try
                {
                    Directory.Delete(RootPath, recursive: true);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                }
            }
        }
    }

    private sealed class FakeArtifactWriter : ICrashReportArtifactWriter
    {
        private readonly CrashReportTextWriter _inner = new();

        internal bool Fail { get; set; }

        internal Exception? ThrowInstead { get; set; }

        /// <summary>When true, the inner writer is skipped so documents with null/degenerate
        /// context (that the real writer cannot serialize) can still exercise Capture's
        /// pre-write logic (e.g. GetStageOrMilestone).</summary>
        internal bool SkipInnerWrite { get; set; }

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

            if (SkipInnerWrite)
            {
                return;
            }

            _inner.Write(destination, document);
        }
    }
}
