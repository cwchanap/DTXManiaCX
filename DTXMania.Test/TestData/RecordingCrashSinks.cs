#nullable enable

using System;
using System.Collections.Generic;
using DTXMania.Game.Lib.Diagnostics.CrashReporting;
using Microsoft.Extensions.Logging;

namespace DTXMania.Test.TestData;

/// <summary>
/// Shared recording breadcrumb sink for crash-reporting tests. Captures
/// <see cref="CrashBreadcrumb"/> records so assertions can inspect both the event
/// name and the persisted properties.
/// </summary>
internal sealed class RecordingBreadcrumbSink : ICrashBreadcrumbSink
{
    private readonly List<CrashBreadcrumb> _events = new();

    public IReadOnlyList<CrashBreadcrumb> Events => _events;

    public void Record(string eventName, IReadOnlyDictionary<string, object?>? properties = null)
    {
        _events.Add(new CrashBreadcrumb(
            DateTimeOffset.UtcNow,
            eventName,
            properties ?? new Dictionary<string, object?>()));
    }
}

/// <summary>
/// Shared recording context sink for crash-reporting tests. Captures every
/// <see cref="CrashContextSnapshot"/> published via <c>SetSnapshot</c>.
/// </summary>
internal sealed class RecordingContextSink : ICrashContextSink
{
    private readonly List<CrashContextSnapshot> _snapshots = new();

    public IReadOnlyList<CrashContextSnapshot> Snapshots => _snapshots;

    public void SetSnapshot(CrashContextSnapshot snapshot)
    {
        _snapshots.Add(snapshot);
    }
}

/// <summary>
/// Shared recording sensitive-data sink for crash-reporting tests. Captures
/// every path and secret registered for redaction so assertions can verify the
/// configured roots and secrets were registered.
/// </summary>
internal sealed class RecordingSensitiveDataSink : ICrashSensitiveDataSink
{
    private readonly List<string?> _paths = new();
    private readonly List<string?> _secrets = new();

    public IReadOnlyList<string?> Paths => _paths;

    public IReadOnlyList<string?> Secrets => _secrets;

    public void RegisterPath(string? path)
    {
        _paths.Add(path);
    }

    public void RegisterSecret(string? secret)
    {
        _secrets.Add(secret);
    }
}

/// <summary>
/// Shared recording <see cref="IGameCrashDiagnostics"/> facade for
/// crash-reporting tests. Wires the recording breadcrumb, context, and
/// sensitive-data sinks together with a no-op logger factory.
/// </summary>
internal sealed class RecordingGameCrashDiagnostics : IGameCrashDiagnostics
{
    public RecordingBreadcrumbSink Breadcrumbs { get; } = new();

    public RecordingContextSink Contexts { get; } = new();

    public RecordingSensitiveDataSink SensitiveData { get; } = new();

    public ILoggerFactory LoggerFactory =>
        Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance;

    ICrashBreadcrumbSink IGameCrashDiagnostics.Breadcrumbs => Breadcrumbs;

    ICrashContextSink IGameCrashDiagnostics.Contexts => Contexts;

    ICrashSensitiveDataSink IGameCrashDiagnostics.SensitiveData => SensitiveData;
}
