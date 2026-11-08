#nullable enable

namespace DTXMania.Game.Lib.Diagnostics.CrashReporting;

/// <summary>
/// The startup lifecycle milestones retained as crash evidence. This enum deliberately contains
/// only the values the crash-report pipeline may publish: it exists so that a report captured
/// during startup can attribute the furthest point reached without exposing the removed startup
/// profiler's milestone graph.
/// </summary>
internal enum StartupMilestone
{
    /// <summary>The startup stage finished activating.</summary>
    StartupActivation,

    /// <summary>The first startup frame finished drawing.</summary>
    StartupFirstDrawEnd,

    /// <summary>The startup stage finished its work and requested the title transition.</summary>
    SummaryRequest
}
