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
}
