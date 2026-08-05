#nullable enable

using System;
using System.Collections.Generic;
using DTXMania.Game.Lib.Diagnostics.CrashReporting;

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
