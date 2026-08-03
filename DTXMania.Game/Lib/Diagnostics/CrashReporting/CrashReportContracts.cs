#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace DTXMania.Game.Lib.Diagnostics.CrashReporting;

public enum CrashReportFormat
{
    ZipBundle,
    EmergencyText
}

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
    NotInitialized,
    Unavailable,
    CollectionFailed
}

public sealed record CrashReportSummary(
    string ReportId,
    DateTimeOffset CapturedAtUtc,
    string BuildId,
    string OperatingSystem,
    string ProcessArchitecture,
    string StageOrMilestone,
    string ExceptionType,
    CrashReportFormat Format,
    string FileName);

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
}

public interface ICrashReportInbox
{
    IReadOnlyList<CrashReportSummary> GetReports();
    bool TryAcknowledge(string reportId, out string? errorCode);
    bool TryDelete(string reportId, out string? errorCode);
}

public interface IGameCrashDiagnostics
{
    ILoggerFactory LoggerFactory { get; }
    ICrashBreadcrumbSink Breadcrumbs { get; }
    ICrashContextSink Contexts { get; }
    ICrashSensitiveDataSink SensitiveData { get; }
    ICrashReportInbox Inbox { get; }
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
}

public sealed class EmptyCrashReportInbox : ICrashReportInbox
{
    private static readonly IReadOnlyList<CrashReportSummary> EmptyReports = Array.Empty<CrashReportSummary>();

    public static EmptyCrashReportInbox Instance { get; } = new();

    private EmptyCrashReportInbox()
    {
    }

    public IReadOnlyList<CrashReportSummary> GetReports()
    {
        return EmptyReports;
    }

    public bool TryAcknowledge(string reportId, out string? errorCode)
    {
        errorCode = "report_not_found";
        return false;
    }

    public bool TryDelete(string reportId, out string? errorCode)
    {
        errorCode = "report_not_found";
        return false;
    }
}
