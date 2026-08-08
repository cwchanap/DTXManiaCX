#nullable enable

using System;
using System.Globalization;
using System.IO;
using System.Linq;
using DTXMania.Game.Lib.Diagnostics.CrashReporting;
using DTXMania.Game.Lib.Stage;
using DTXMania.Test.TestData;
using Microsoft.Extensions.Logging;

namespace DTXMania.Test.CrashReporting;

[Trait("Category", "Unit")]
public sealed class CrashReportInboxTests
{
    [Fact]
    public void OpenGitHubIssue_WhenLaunchSucceeds_ShouldAcknowledgeTheReport()
    {
        using var fixture = InboxFixture.Create();
        var reportId = fixture.CaptureReport();
        var launcher = new FakeExternalLauncher { UriResult = Success() };

        var result = new CrashReportInbox(fixture.Store, launcher).OpenGitHubIssue(reportId);

        Assert.True(result.Succeeded);
        Assert.Null(result.ErrorCode);
        Assert.True(launcher.LaunchedUri is not null);
        Assert.True(GitHubCrashIssueBuilder.IsTargetAllowed(launcher.LaunchedUri));
        var item = Assert.Single(fixture.Store.DiscoverReports());
        Assert.True(item.IsAcknowledged);
    }

    [Fact]
    public void OpenReportFolder_WhenLaunchSucceeds_ShouldAcknowledgeTheReport()
    {
        using var fixture = InboxFixture.Create();
        var reportId = fixture.CaptureReport();
        var launcher = new FakeExternalLauncher { FolderResult = Success() };

        var result = new CrashReportInbox(fixture.Store, launcher).OpenReportFolder(reportId);

        Assert.True(result.Succeeded);
        Assert.Equal(fixture.Store.RootPath, launcher.LaunchedFolder);
        var item = Assert.Single(fixture.Store.DiscoverReports());
        Assert.True(item.IsAcknowledged);
    }

    [Fact]
    public void OpenGitHubIssue_WhenLaunchFails_ShouldLeaveReportPending()
    {
        using var fixture = InboxFixture.Create();
        var reportId = fixture.CaptureReport();
        var launcher = new FakeExternalLauncher
        {
            UriResult = new CrashReportActionResult(Succeeded: false, ErrorCode: "launch_nonzero_exit")
        };

        var result = new CrashReportInbox(fixture.Store, launcher).OpenGitHubIssue(reportId);

        Assert.False(result.Succeeded);
        Assert.Equal("launch_nonzero_exit", result.ErrorCode);
        var item = Assert.Single(fixture.Store.DiscoverReports());
        Assert.False(item.IsAcknowledged);
    }

    [Fact]
    public void OpenGitHubIssue_WhenLaunchSucceedsButAcknowledgeFails_ShouldReturnRetryableAcknowledgeError()
    {
        // The read-only-root mechanism uses Unix file modes (chmod), which compile on Windows but
        // throw at runtime. Windows ACL-based denial is out of scope for this Unix-origin task.
        // Skip on Windows (and when running as root, which bypasses file permissions).
        if (OperatingSystem.IsWindows() || RunningAsRoot())
        {
            return;
        }

        using var fixture = InboxFixture.Create();
        var reportId = fixture.CaptureReport();
        var launcher = new FakeExternalLauncher { UriResult = Success() };
        fixture.MakeRootReadOnly();

        var result = new CrashReportInbox(fixture.Store, launcher).OpenGitHubIssue(reportId);

        Assert.False(result.Succeeded);
        Assert.Equal("acknowledge_io_failure", result.ErrorCode);
        Assert.True(launcher.LaunchedUri is not null);
        // The report stayed pending because the ack failed.
        fixture.RestoreRootWritable();
        var item = Assert.Single(fixture.Store.DiscoverReports());
        Assert.False(item.IsAcknowledged);
    }

    [Fact]
    public void Dismiss_ShouldAcknowledgeWithoutLaunchingOrDeleting()
    {
        using var fixture = InboxFixture.Create();
        var reportId = fixture.CaptureReport();
        var launcher = new FakeExternalLauncher();

        var result = new CrashReportInbox(fixture.Store, launcher).Dismiss(reportId);

        Assert.True(result.Succeeded);
        Assert.Null(launcher.LaunchedUri);
        Assert.Null(launcher.LaunchedFolder);
        var item = Assert.Single(fixture.Store.DiscoverReports());
        Assert.True(item.IsAcknowledged);
    }

    [Fact]
    public void Delete_ShouldRemoveTheLogicalReport()
    {
        using var fixture = InboxFixture.Create();
        var reportId = fixture.CaptureReport();
        var launcher = new FakeExternalLauncher();

        var result = new CrashReportInbox(fixture.Store, launcher).Delete(reportId);

        Assert.True(result.Succeeded);
        Assert.Null(launcher.LaunchedUri);
        Assert.Null(launcher.LaunchedFolder);
        Assert.Empty(fixture.Store.DiscoverReports());
    }

    [Fact]
    public void OpenGitHubIssue_WithUnknownReportId_ShouldReturnReportNotFound()
    {
        using var fixture = InboxFixture.Create();
        var launcher = new FakeExternalLauncher { UriResult = Success() };

        var result = new CrashReportInbox(fixture.Store, launcher)
            .OpenGitHubIssue("crash-20260807-010203Z-deadbe");

        Assert.False(result.Succeeded);
        Assert.Equal("report_not_found", result.ErrorCode);
        Assert.Null(launcher.LaunchedUri);
    }

    [Fact]
    public void OpenReportFolder_WithUnknownReportId_ShouldReturnReportNotFound()
    {
        using var fixture = InboxFixture.Create();
        var launcher = new FakeExternalLauncher { FolderResult = Success() };

        var result = new CrashReportInbox(fixture.Store, launcher).OpenReportFolder("missing-id");

        Assert.False(result.Succeeded);
        Assert.Equal("report_not_found", result.ErrorCode);
        Assert.Null(launcher.LaunchedFolder);
    }

    [Fact]
    public void Dismiss_WithUnknownReportId_ShouldReturnReportNotFound()
    {
        using var fixture = InboxFixture.Create();
        var launcher = new FakeExternalLauncher();

        var result = new CrashReportInbox(fixture.Store, launcher).Dismiss("missing-id");

        Assert.False(result.Succeeded);
        Assert.Equal("report_not_found", result.ErrorCode);
    }

    [Fact]
    public void Delete_WithUnknownReportId_ShouldReturnReportNotFound()
    {
        using var fixture = InboxFixture.Create();
        var launcher = new FakeExternalLauncher();

        var result = new CrashReportInbox(fixture.Store, launcher).Delete("missing-id");

        Assert.False(result.Succeeded);
        Assert.Equal("report_not_found", result.ErrorCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void OpenGitHubIssue_WithBlankReportId_ShouldThrow(string reportId)
    {
        using var fixture = InboxFixture.Create();
        var inbox = new CrashReportInbox(fixture.Store, new FakeExternalLauncher());

        Assert.Throws<ArgumentException>(() => inbox.OpenGitHubIssue(reportId));
    }

    [Fact]
    public void Constructor_WithNullDependencies_ShouldThrow()
    {
        using var fixture = InboxFixture.Create();
        Assert.Throws<ArgumentNullException>(() => new CrashReportInbox(null!, new FakeExternalLauncher()));
        Assert.Throws<ArgumentNullException>(() => new CrashReportInbox(fixture.Store, null!));
    }

    [Fact]
    public void GetReports_WhenStoreIsEmpty_ShouldReturnEmptyList()
    {
        using var fixture = InboxFixture.Create();
        var inbox = new CrashReportInbox(fixture.Store, new FakeExternalLauncher());

        Assert.Empty(inbox.GetReports());
    }

    private static CrashReportActionResult Success() => new(Succeeded: true);

    private static bool RunningAsRoot()
    {
        return Environment.UserName == "root"
            || (Environment.GetEnvironmentVariable("USER") == "root");
    }

    private sealed class InboxFixture : IDisposable
    {
        private readonly CrashReportStore _store;
        private readonly ManualTimeProvider _clock;
        private bool _rootReadOnly;

        private InboxFixture(string rootPath)
        {
            RootPath = rootPath;
            _clock = new ManualTimeProvider(new DateTimeOffset(2026, 8, 7, 1, 2, 3, TimeSpan.Zero));
            _store = new CrashReportStore(rootPath, new CrashReportTextWriter(), _clock, TextWriter.Null);
        }

        internal string RootPath { get; }

        internal CrashReportStore Store => _store;

        internal static InboxFixture Create()
        {
            var rootPath = Path.Combine(Path.GetTempPath(), "dtx-crash-inbox-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(rootPath);
            return new InboxFixture(rootPath);
        }

        internal string CaptureReport()
        {
            var result = _store.Capture(CreateCapture());
            if (result.Report is null || result.FailureCode is not null)
            {
                throw new InvalidOperationException(
                    "Fixture capture failed: " + (result.FailureCode ?? "unknown"));
            }

            _clock.Advance(TimeSpan.FromMinutes(1));
            return result.Report.ReportId;
        }

        internal void MakeRootReadOnly()
        {
            _rootReadOnly = true;
            SetRootPermissions(readOnly: true);
        }

        internal void RestoreRootWritable()
        {
            if (_rootReadOnly)
            {
                SetRootPermissions(readOnly: false);
                _rootReadOnly = false;
            }
        }

        private void SetRootPermissions(bool readOnly)
        {
            // 0500 = r-x (enumerate + chdir, no writes -> File.Move fails with EACCES).
            // 0700 = rwx (default).
            var mode = readOnly ? UnixFileMode.OtherRead | UnixFileMode.OtherExecute
                                | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                                | UnixFileMode.UserRead | UnixFileMode.UserExecute
                : UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                    | UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute
                    | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;

            File.SetUnixFileMode(RootPath, mode);
        }

        private CrashCaptureData CreateCapture()
        {
            return new CrashCaptureData(
                new InvalidOperationException("fixture failure"),
                [
                    new CrashLogRecord(
                        _clock.GetUtcNow(),
                        LogLevel.Information,
                        CrashLogEvents.StageTransitionCompleted.EventId,
                        CrashLogEvents.StageTransitionCompleted.MessageTemplate,
                        new Dictionary<string, object?> { ["TargetStage"] = StageType.Title },
                        ExceptionType: null)
                ],
                [
                    new CrashBreadcrumb(
                        _clock.GetUtcNow(),
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
            RestoreRootWritable();

            try
            {
                if (Directory.Exists(RootPath))
                {
                    Directory.Delete(RootPath, recursive: true);
                }
            }
            catch (IOException)
            {
                // Best-effort temp cleanup; never fail the test on teardown.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private sealed class FakeExternalLauncher : IExternalLauncher
    {
        internal Uri? LaunchedUri { get; private set; }

        internal string? LaunchedFolder { get; private set; }

        internal CrashReportActionResult UriResult { get; set; } = new(Succeeded: true);

        internal CrashReportActionResult FolderResult { get; set; } = new(Succeeded: true);

        public CrashReportActionResult LaunchUri(Uri target)
        {
            LaunchedUri = target;
            return UriResult;
        }

        public CrashReportActionResult LaunchFolder(string path)
        {
            LaunchedFolder = path;
            return FolderResult;
        }
    }
}
