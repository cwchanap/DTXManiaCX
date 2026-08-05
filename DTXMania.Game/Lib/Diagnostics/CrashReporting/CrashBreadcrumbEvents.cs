#nullable enable

using System;
using System.Collections.Generic;

namespace DTXMania.Game.Lib.Diagnostics.CrashReporting;

/// <summary>
/// The single definition of every breadcrumb event name that may be persisted in a crash report.
///
/// Call sites pass one of these constants to <see cref="ICrashBreadcrumbSink.Record"/>, so the name
/// at the call site and the name on the allowlist are the same value and cannot drift apart.
/// <see cref="CrashBreadcrumbBuffer"/> rewrites anything else to <see cref="Unknown"/> at ingest.
///
/// Add an entry only when a call site actually records it; an unused entry widens what a crash
/// report may contain while proving nothing.
/// </summary>
internal static class CrashBreadcrumbEvents
{
    internal const string ProcessStarted = "process_started";
    internal const string InitializationMilestoneReached = "initialization_milestone_reached";
    internal const string StageTransitionRequested = "stage_transition_requested";
    internal const string StageTransitionStarted = "stage_transition_started";
    internal const string StageTransitionCompleted = "stage_transition_completed";
    internal const string StageTransitionRejected = "stage_transition_rejected";
    internal const string GraphicsSettingsChanged = "graphics_settings_changed";
    internal const string GraphicsDeviceLost = "graphics_device_lost";
    internal const string GraphicsDeviceReset = "graphics_device_reset";
    internal const string MidiDeviceCountChanged = "midi_device_count_changed";

    /// <summary>
    /// Substituted for any name that is not on the allowlist, so an unrecognized breadcrumb still
    /// marks its position in the timeline without persisting an arbitrary string.
    /// </summary>
    internal const string Unknown = "unknown_event";

    private static readonly HashSet<string> StableEventNames = new(StringComparer.Ordinal)
    {
        ProcessStarted,
        InitializationMilestoneReached,
        StageTransitionRequested,
        StageTransitionStarted,
        StageTransitionCompleted,
        StageTransitionRejected,
        GraphicsSettingsChanged,
        GraphicsDeviceLost,
        GraphicsDeviceReset,
        MidiDeviceCountChanged
    };

    internal static bool IsStableEvent(string eventName) =>
        StableEventNames.Contains(eventName);
}
