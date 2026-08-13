using DTXMania.Automation.JsonRpc;
using DTXMania.Automation.Process;
using DTXMania.Automation.Telemetry;

namespace DTXMania.VideoRecorder.Workflow;

/// <summary>
/// Adapts the shared Automation process/JSON-RPC primitives to the recorder's
/// deliberately small game-control seam.
/// </summary>
internal sealed class AutomationGameRecordingControl : IGameRecordingControl
{
    private static readonly TimeSpan StartupPollInterval = TimeSpan.FromMilliseconds(250);

    private readonly GameProcessDriver _process;
    private readonly HttpClient _httpClient;
    private readonly JsonRpcGameClient _client;
    private bool _disposed;

    public AutomationGameRecordingControl(int apiPort, string apiKey)
    {
        if (apiPort is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(apiPort));
        ArgumentNullException.ThrowIfNull(apiKey);

        _process = new GameProcessDriver();
        _httpClient = new HttpClient(new SocketsHttpHandler { UseCookies = false })
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        _client = new JsonRpcGameClient(
            _httpClient,
            new GameApiConnectionOptions(
                new Uri($"http://127.0.0.1:{apiPort}/", UriKind.Absolute),
                apiKey));
    }

    public string StandardOutput => _process.StandardOutput;

    public string StandardError => _process.StandardError;

    public void Start(GameProcessStartOptions options)
    {
        ThrowIfDisposed();
        _process.Start(options);
    }

    public Task WaitForStartupAsync(TimeSpan timeout, CancellationToken token)
    {
        ThrowIfDisposed();
        return _process.WaitForStartupAsync(
            _client.GetHealthAsync,
            timeout,
            StartupPollInterval,
            token);
    }

    public Task<GameStateSnapshot> GetGameStateAsync(CancellationToken token)
    {
        ThrowIfDisposed();
        return _client.GetGameStateAsync(token);
    }

    public Task SendKeyAsync(string key, TimeSpan hold, CancellationToken token)
    {
        ThrowIfDisposed();
        return _client.SendKeyAsync(key, hold, token);
    }

    public Task PrepareVideoChartAsync(string chartPath, CancellationToken token)
    {
        ThrowIfDisposed();
        return _client.PrepareVideoChartAsync(chartPath, token);
    }

    public Task<string?> TakeScreenshotBase64Async(CancellationToken token)
    {
        ThrowIfDisposed();
        return _client.TakeScreenshotBase64Async(token);
    }

    public Task StartPreparedPreviewAsync(CancellationToken token)
    {
        ThrowIfDisposed();
        return _client.StartPreparedPreviewAsync(token);
    }

    public Task ActivatePreparedChartAsync(CancellationToken token)
    {
        ThrowIfDisposed();
        return _client.ActivatePreparedChartAsync(token);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        try
        {
            await _process.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            _httpClient.Dispose();
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
