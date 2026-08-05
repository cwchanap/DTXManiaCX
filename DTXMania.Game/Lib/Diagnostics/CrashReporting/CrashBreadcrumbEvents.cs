#nullable enable

using System;
using System.Collections.Generic;

namespace DTXMania.Game.Lib.Diagnostics.CrashReporting;

/// <summary>
/// The single source of truth for the stable breadcrumb event names that may be persisted
/// in a crash report. Read by both <see cref="CrashBreadcrumbBuffer"/> (ingest) and
/// <see cref="CrashReportArchiveWriter"/> (serialization); unknown names are rewritten to
/// "unknown_event" at both boundaries.
/// </summary>
internal static class CrashBreadcrumbEvents
{
    private static readonly HashSet<string> StableEventNames = new(StringComparer.Ordinal)
    {
        "process_started",
        "initialization_milestone_reached",
        "stage_transition_requested",
        "stage_transition_started",
        "stage_transition_completed",
        "stage_transition_rejected",
        "configuration_opened",
        "configuration_closed",
        "graphics_settings_changed",
        "graphics_device_lost",
        "graphics_device_reset",
        "audio_device_attached",
        "audio_device_detached",
        "audio_device_selected",
        "midi_device_attached",
        "midi_device_detached",
        "midi_device_selected",
        "midi_device_count_changed",
        "song_selection_entered",
        "gameplay_entered",
        "exit_requested"
    };

    public static bool IsStableEvent(string eventName) =>
        StableEventNames.Contains(eventName);
}
