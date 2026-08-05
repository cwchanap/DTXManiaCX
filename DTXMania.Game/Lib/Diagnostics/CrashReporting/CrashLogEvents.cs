#nullable enable

using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace DTXMania.Game.Lib.Diagnostics.CrashReporting;

/// <summary>
/// A log event that may be persisted in a crash report, carrying its id, stable name, and
/// message template together.
/// </summary>
internal sealed record CrashLogEvent(int Id, string Name, string MessageTemplate)
{
    internal EventId EventId => new(Id, Name);
}

/// <summary>
/// The single definition of every crash-reportable log event.
///
/// Call sites log through <see cref="CrashLogEventExtensions.LogCrashEvent"/> with one of these
/// definitions, so an event's id, name, and template exist in exactly one place and cannot drift
/// apart. <see cref="CrashLogFieldPolicy"/> classifies incoming records against this same list —
/// any log call that did not come from here is buffered without its message or properties.
///
/// Add an entry only when a call site actually logs it; an unused entry widens what a crash
/// report may contain while proving nothing.
/// </summary>
internal static class CrashLogEvents
{
    internal static CrashLogEvent StageTransitionRequested { get; } = new(
        5103,
        "stage_transition_requested",
        "Stage transition requested: {PreviousStage} -> {TargetStage}");

    internal static CrashLogEvent StageTransitionStarted { get; } = new(
        5104,
        "stage_transition_started",
        "Stage transition started: {PreviousStage} -> {TargetStage}");

    internal static CrashLogEvent StageTransitionCompleted { get; } = new(
        5105,
        "stage_transition_completed",
        "Stage transition completed: {PreviousStage} -> {TargetStage}");

    internal static CrashLogEvent StageTransitionRejected { get; } = new(
        5111,
        "stage_transition_rejected",
        "Stage transition rejected: {Reason}");

    internal static CrashLogEvent GraphicsInitialized { get; } = new(
        5114,
        "graphics_initialized",
        "Graphics initialized: {Width}x{Height}, fullscreen={Fullscreen}, vsync={VSync}");

    internal static CrashLogEvent GraphicsSettingsChanged { get; } = new(
        5107,
        "graphics_settings_changed",
        "Graphics settings updated: {Width}x{Height}, fullscreen={Fullscreen}, vsync={VSync}");

    internal static CrashLogEvent GraphicsDeviceLost { get; } = new(
        5112,
        "graphics_device_lost",
        "Graphics device lost");

    internal static CrashLogEvent GraphicsDeviceReset { get; } = new(
        5113,
        "graphics_device_reset",
        "Graphics device reset");

    internal static CrashLogEvent MidiDeviceCountChanged { get; } = new(
        5108,
        "midi_device_count_changed",
        "MIDI device count: {MidiDeviceCount}");

    internal static IReadOnlyList<CrashLogEvent> All { get; } =
    [
        StageTransitionRequested,
        StageTransitionStarted,
        StageTransitionCompleted,
        StageTransitionRejected,
        GraphicsInitialized,
        GraphicsSettingsChanged,
        GraphicsDeviceLost,
        GraphicsDeviceReset,
        MidiDeviceCountChanged
    ];
}

internal static class CrashLogEventExtensions
{
    /// <summary>
    /// Logs a crash-reportable event. Using this instead of a hand-written event id and template
    /// is what keeps the call site and <see cref="CrashLogFieldPolicy"/> in agreement.
    /// </summary>
    internal static void LogCrashEvent(
        this ILogger logger,
        LogLevel logLevel,
        CrashLogEvent crashEvent,
        params object?[] arguments)
    {
        logger.Log(logLevel, crashEvent.EventId, crashEvent.MessageTemplate, arguments);
    }
}
