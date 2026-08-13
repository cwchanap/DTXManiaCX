namespace DTXMania.VideoRecorder.Workflow;

/// <summary>
/// Stable names used by recorder diagnostics. The workflow deliberately keeps
/// the journey imperative; this enum only gives diagnostics a compact,
/// machine-readable vocabulary.
/// </summary>
internal enum RecordingStep
{
    Started,
    StartupReady,
    TitleReady,
    SongSelectReady,
    ChartPrepared,
    ScreenshotBeforeRecording,
    ObsConnected,
    ObsStatusChecked,
    ObsStarted,
    PreviewReady,
    SongTransition,
    PerformanceReady,
    ResultCompleted,
    ScreenshotAfterResult,
    ResultHold,
    ObsStopped,
    ArtifactVerified,
    Completed,
    Failed
}
