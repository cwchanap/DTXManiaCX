#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace DTXMania.Game.Lib.Diagnostics.CrashReporting;

internal sealed class CrashBreadcrumbBuffer : ICrashBreadcrumbSink
{
    private static readonly HashSet<string> StableEventNames = new(StringComparer.Ordinal)
    {
        "process_started",
        "initialization_milestone_reached",
        "stage_transition_requested",
        "stage_transition_started",
        "stage_transition_completed",
        "configuration_opened",
        "configuration_closed",
        "graphics_device_lost",
        "graphics_device_reset",
        "audio_device_attached",
        "audio_device_detached",
        "audio_device_selected",
        "midi_device_attached",
        "midi_device_detached",
        "midi_device_selected",
        "song_selection_entered",
        "gameplay_entered",
        "exit_requested"
    };

    private readonly object _gate = new();
    private readonly TimeProvider _timeProvider;
    private readonly int _capacity;
    private readonly Queue<CrashBreadcrumb> _breadcrumbs = new();

    internal CrashBreadcrumbBuffer(TimeProvider timeProvider, int capacity)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        _capacity = capacity;
    }

    public void Record(string eventName, IReadOnlyDictionary<string, object?>? properties = null)
    {
        var breadcrumb = StableEventNames.Contains(eventName)
            ? new CrashBreadcrumb(_timeProvider.GetUtcNow(), eventName, NormalizeProperties(properties))
            : new CrashBreadcrumb(_timeProvider.GetUtcNow(), "unknown_event", EmptyProperties.Instance);

        lock (_gate)
        {
            if (_breadcrumbs.Count == _capacity)
            {
                _breadcrumbs.Dequeue();
            }

            _breadcrumbs.Enqueue(breadcrumb);
        }
    }

    internal IReadOnlyList<CrashBreadcrumb> Snapshot()
    {
        lock (_gate)
        {
            return _breadcrumbs.ToArray();
        }
    }

    private static IReadOnlyDictionary<string, object?> NormalizeProperties(IReadOnlyDictionary<string, object?>? properties)
    {
        if (properties is null || properties.Count == 0)
        {
            return EmptyProperties.Instance;
        }

        var normalizedProperties = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in properties)
        {
            if (CrashLogFieldPolicy.Default.TryNormalizeProperty(property.Key, property.Value, out var normalizedValue))
            {
                normalizedProperties[property.Key] = normalizedValue;
            }
        }

        return normalizedProperties.Count == 0
            ? EmptyProperties.Instance
            : new ReadOnlyDictionary<string, object?>(normalizedProperties);
    }

    private static class EmptyProperties
    {
        internal static IReadOnlyDictionary<string, object?> Instance { get; } =
            new ReadOnlyDictionary<string, object?>(new Dictionary<string, object?>());
    }
}
