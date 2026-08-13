using DTXMania.Automation.Process;
using DTXMania.Automation.Telemetry;

namespace DTXMania.VideoRecorder.Workflow;

/// <summary>
/// The narrow game-control surface owned by the recorder workflow. The
/// Automation process and JSON-RPC primitives stay behind this seam so the
/// journey can be tested without launching CX.
/// </summary>
internal interface IGameRecordingControl : IAsyncDisposable
{
    string StandardOutput { get; }

    string StandardError { get; }

    void Start(GameProcessStartOptions options);

    Task WaitForStartupAsync(TimeSpan timeout, CancellationToken token);

    Task<GameStateSnapshot> GetGameStateAsync(CancellationToken token);

    Task SendKeyAsync(string key, TimeSpan hold, CancellationToken token);

    Task PrepareVideoChartAsync(string chartPath, CancellationToken token);

    Task<string?> TakeScreenshotBase64Async(CancellationToken token);

    Task StartPreparedPreviewAsync(CancellationToken token);

    Task ActivatePreparedChartAsync(CancellationToken token);
}
