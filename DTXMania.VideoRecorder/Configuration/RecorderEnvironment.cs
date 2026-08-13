namespace DTXMania.VideoRecorder.Configuration;

internal sealed record RecorderEnvironment(
    Uri ObsUrl,
    string ObsPassword,
    string ObsOutputDirectory,
    string SourceAppDataRoot);
