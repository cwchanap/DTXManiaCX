#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace DTXMania.Game.Lib.Diagnostics.CrashReporting;

public enum CrashContextKind
{
    Process,
    Application,
    Startup,
    Configuration,
    Stage,
    Graphics,
    Audio,
    Input
}

public enum CrashContextStatus
{
    Available,
    Unavailable
}

public sealed record CrashReportSummary(
    string ReportId,
    DateTimeOffset CapturedAtUtc,
    string BuildId,
    string OperatingSystem,
    string ProcessArchitecture,
    string StageOrMilestone,
    string ExceptionType,
    string FileName);

public sealed record CrashReportInboxItem(
    CrashReportSummary Summary,
    bool IsAcknowledged);

public readonly record struct CrashReportActionResult(
    bool Succeeded,
    string? ErrorCode = null);

public interface ICrashReportInbox
{
    IReadOnlyList<CrashReportInboxItem> GetReports();

    CrashReportActionResult OpenGitHubIssue(string reportId);

    CrashReportActionResult OpenReportFolder(string reportId);

    CrashReportActionResult Dismiss(string reportId);

    CrashReportActionResult Delete(string reportId);
}

public sealed class EmptyCrashReportInbox : ICrashReportInbox
{
    public static EmptyCrashReportInbox Instance { get; } = new();

    private EmptyCrashReportInbox()
    {
    }

    public IReadOnlyList<CrashReportInboxItem> GetReports() => Array.Empty<CrashReportInboxItem>();

    public CrashReportActionResult OpenGitHubIssue(string reportId) => new(Succeeded: true);

    public CrashReportActionResult OpenReportFolder(string reportId) => new(Succeeded: true);

    public CrashReportActionResult Dismiss(string reportId) => new(Succeeded: true);

    public CrashReportActionResult Delete(string reportId) => new(Succeeded: true);
}

public sealed record CrashContextSnapshot(
    CrashContextKind Kind,
    CrashContextStatus Status,
    IReadOnlyDictionary<string, object?> Fields,
    string? FailureCode = null);

public sealed record CrashBreadcrumb(
    DateTimeOffset TimestampUtc,
    string EventName,
    IReadOnlyDictionary<string, object?> Properties);

public interface ICrashBreadcrumbSink
{
    void Record(string eventName, IReadOnlyDictionary<string, object?>? properties = null);
}

public interface ICrashContextSink
{
    void SetSnapshot(CrashContextSnapshot snapshot);
}

public interface ICrashSensitiveDataSink
{
    void RegisterPath(string? path);

    void RegisterSecret(string? secret);
}

public interface IGameCrashDiagnostics
{
    ILoggerFactory LoggerFactory { get; }
    ICrashBreadcrumbSink Breadcrumbs { get; }
    ICrashContextSink Contexts { get; }
    ICrashSensitiveDataSink SensitiveData { get; }
}

public sealed class EmptyCrashBreadcrumbSink : ICrashBreadcrumbSink
{
    public static EmptyCrashBreadcrumbSink Instance { get; } = new();

    private EmptyCrashBreadcrumbSink()
    {
    }

    public void Record(string eventName, IReadOnlyDictionary<string, object?>? properties = null)
    {
    }
}

public sealed class EmptyCrashContextSink : ICrashContextSink
{
    public static EmptyCrashContextSink Instance { get; } = new();

    private EmptyCrashContextSink()
    {
    }

    public void SetSnapshot(CrashContextSnapshot snapshot)
    {
    }
}

public sealed class EmptyCrashSensitiveDataSink : ICrashSensitiveDataSink
{
    public static EmptyCrashSensitiveDataSink Instance { get; } = new();

    private EmptyCrashSensitiveDataSink()
    {
    }

    public void RegisterPath(string? path)
    {
    }

    public void RegisterSecret(string? secret)
    {
    }
}
