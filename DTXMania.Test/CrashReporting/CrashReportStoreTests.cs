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
