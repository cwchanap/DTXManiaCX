using System.Net.Http.Json;
using System.Text.Json;
using DTXMania.Automation.Process;
using DTXMania.Automation.Telemetry;

namespace DTXMania.Automation.JsonRpc;

public sealed class JsonRpcGameClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(5);

    private readonly HttpClient _httpClient;
    private readonly GameApiConnectionOptions _connection;
    private int _nextId;

    public JsonRpcGameClient(HttpClient httpClient, GameApiConnectionOptions connection)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
    }

    public async Task<GameHealthSnapshot?> GetHealthAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, Resolve("health"));
            AddApiKey(request);

            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return null;

            var body = await ReadBodyAsync(response, cancellationToken).ConfigureAwait(false);
            return ParseHealth(body);
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // HttpClient reports its per-request timeout as TaskCanceledException.
            return null;
        }
    }

    public async Task<GameStateSnapshot> GetGameStateAsync(CancellationToken cancellationToken)
    {
        var result = await SendAsync("getGameState", null, cancellationToken).ConfigureAwait(false);
        return result.Deserialize<GameStateSnapshot>(JsonOptions)
            ?? throw new InvalidOperationException("getGameState returned an empty result.");
    }

    public async Task SendKeyAsync(
        string key,
        TimeSpan holdDuration,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        await SendInputAsync(GameApiInputType.KeyPress, key, cancellationToken).ConfigureAwait(false);

        // Once the press has reached the game, guarantee one bounded
        // cancellation-independent release. The Game API translates press/release
        // into persistent injected button state, so a press without a matching
        // release leaves the input stuck. Cancellation can land in the window
        // after Task.Delay completes (or when holdDuration == 0) but before the
        // normal release request completes; in that case the normal release uses
        // the already-canceled caller token and may never reach the game. The
        // try/finally below ensures a cleanup release runs unless the normal
        // release has already completed.
        var released = false;
        try
        {
            if (holdDuration > TimeSpan.Zero)
            {
                await Task.Delay(holdDuration, cancellationToken).ConfigureAwait(false);
            }

            await SendInputAsync(GameApiInputType.KeyRelease, key, cancellationToken).ConfigureAwait(false);
            released = true;
        }
        finally
        {
            if (!released)
            {
                await SendReleaseOnCleanupPath(GameApiInputType.KeyRelease, key).ConfigureAwait(false);
            }
        }
    }

    public async Task SendMidiNoteAsync(
        int noteNumber,
        int velocity,
        TimeSpan holdDuration,
        CancellationToken cancellationToken)
    {
        if (noteNumber < 0 || noteNumber > 127)
            throw new ArgumentOutOfRangeException(nameof(noteNumber));

        if (velocity < 0 || velocity > 127)
            throw new ArgumentOutOfRangeException(nameof(velocity));

        var data = new { noteNumber, velocity };
        await SendInputAsync(GameApiInputType.MidiNoteOn, data, cancellationToken).ConfigureAwait(false);

        // See SendKeyAsync: once the note-on reaches the game, guarantee one
        // bounded cancellation-independent note-off unless the normal note-off
        // has already completed.
        var released = false;
        try
        {
            if (holdDuration > TimeSpan.Zero)
            {
                await Task.Delay(holdDuration, cancellationToken).ConfigureAwait(false);
            }

            await SendInputAsync(GameApiInputType.MidiNoteOff, data, cancellationToken).ConfigureAwait(false);
            released = true;
        }
        finally
        {
            if (!released)
            {
                await SendReleaseOnCleanupPath(GameApiInputType.MidiNoteOff, data).ConfigureAwait(false);
            }
        }
    }

    public async Task ChangeStageAsync(string stageName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stageName);
        await SendAsync("changeStage", new { stageName }, cancellationToken).ConfigureAwait(false);
    }

    public async Task PrepareVideoChartAsync(string chartPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chartPath);
        var result = await SendAsync(
            "prepareVideoChart",
            new { chartPath },
            cancellationToken).ConfigureAwait(false);
        EnsurePreparedChartCommandSucceeded("prepareVideoChart", result);
    }

    public async Task StartPreparedPreviewAsync(CancellationToken cancellationToken = default)
    {
        var result = await SendAsync("startPreparedPreview", null, cancellationToken).ConfigureAwait(false);
        EnsurePreparedChartCommandSucceeded("startPreparedPreview", result);
    }

    public async Task ActivatePreparedChartAsync(CancellationToken cancellationToken = default)
    {
        var result = await SendAsync("activatePreparedChart", null, cancellationToken).ConfigureAwait(false);
        EnsurePreparedChartCommandSucceeded("activatePreparedChart", result);
    }

    public async Task CancelPreparedChartAsync(CancellationToken cancellationToken = default)
    {
        var result = await SendAsync("cancelPreparedChart", null, cancellationToken).ConfigureAwait(false);
        EnsurePreparedChartCommandSucceeded("cancelPreparedChart", result);
    }

    public async Task<string?> TakeScreenshotBase64Async(CancellationToken cancellationToken)
    {
        var result = await SendAsync("takeScreenshot", null, cancellationToken).ConfigureAwait(false);
        return result.TryGetProperty("imageData", out var imageData)
            && imageData.ValueKind == JsonValueKind.String
            ? imageData.GetString()
            : null;
    }

    private async Task SendInputAsync(
        GameApiInputType type,
        object data,
        CancellationToken cancellationToken)
    {
        var result = await SendAsync(
            "sendInput",
            new { type = (int)type, data },
            cancellationToken).ConfigureAwait(false);

        if (!result.TryGetProperty("success", out var success)
            || success.ValueKind is not JsonValueKind.True)
        {
            throw new InvalidOperationException(
                $"sendInput type {(int)type} was not accepted by the game.");
        }
    }

    private static void EnsurePreparedChartCommandSucceeded(string method, JsonElement result)
    {
        if (result.TryGetProperty("success", out var success)
            && success.ValueKind == JsonValueKind.True)
        {
            return;
        }

        var error = result.TryGetProperty("error", out var errorElement)
            && errorElement.ValueKind == JsonValueKind.String
            ? errorElement.GetString()
            : null;

        throw new InvalidOperationException(
            error ?? $"JSON-RPC {method} command failed.");
    }

    private async Task SendReleaseOnCleanupPath(GameApiInputType type, object data)
    {
        using var cleanupCancellation = new CancellationTokenSource(CleanupTimeout);
        try
        {
            await SendInputAsync(type, data, cleanupCancellation.Token).ConfigureAwait(false);
        }
        catch
        {
            // The caller already observed cancellation; the cleanup release is best-effort
            // so the original cancellation exception remains the actionable error.
        }
    }

    private async Task<JsonElement> SendAsync(
        string method,
        object? parameters,
        CancellationToken cancellationToken)
    {
        var requestId = Interlocked.Increment(ref _nextId);
        var request = new
        {
            jsonrpc = "2.0",
            id = requestId,
            method,
            @params = parameters
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, Resolve("jsonrpc"))
        {
            Content = JsonContent.Create(request)
        };
        AddApiKey(httpRequest);

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        var body = await ReadBodyAsync(response, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"JSON-RPC {method} returned HTTP {(int)response.StatusCode} "
                + $"({response.ReasonPhrase}): {body}");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"JSON-RPC {method} returned malformed JSON: {body}",
                exception);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException(
                    $"JSON-RPC {method} returned a non-object response: {body}");
            }

            if (!root.TryGetProperty("id", out var responseIdElement))
            {
                throw new InvalidOperationException(
                    $"JSON-RPC {method} response did not include an id: {body}");
            }

            if (responseIdElement.ValueKind != JsonValueKind.Number
                || !responseIdElement.TryGetInt32(out var responseId)
                || responseId != requestId)
            {
                throw new InvalidOperationException(
                    $"JSON-RPC {method} response id does not match request id {requestId}: {body}");
            }

            if (root.TryGetProperty("error", out var error)
                && error.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
            {
                throw new InvalidOperationException(
                    $"JSON-RPC {method} failed: {body}");
            }

            if (!root.TryGetProperty("result", out var result))
            {
                throw new InvalidOperationException(
                    $"JSON-RPC {method} did not include a result: {body}");
            }

            return result.Clone();
        }
    }

    private Uri Resolve(string relativePath) => new(_connection.BaseUri, relativePath);

    private void AddApiKey(HttpRequestMessage request)
    {
        if (!string.IsNullOrWhiteSpace(_connection.ApiKey))
            request.Headers.Add("X-Api-Key", _connection.ApiKey);
    }

    private static async Task<string> ReadBodyAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        return response.Content is null
            ? string.Empty
            : await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    private static GameHealthSnapshot? ParseHealth(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return null;

            int? processId = null;
            if (root.TryGetProperty("processId", out var processIdElement))
            {
                if (processIdElement.ValueKind == JsonValueKind.Number
                    && processIdElement.TryGetInt32(out var numericProcessId))
                {
                    processId = numericProcessId;
                }
                else if (processIdElement.ValueKind == JsonValueKind.String
                    && int.TryParse(processIdElement.GetString(), out var stringProcessId))
                {
                    processId = stringProcessId;
                }
            }

            string? launchToken = null;
            if (root.TryGetProperty("launchToken", out var launchTokenElement)
                && launchTokenElement.ValueKind == JsonValueKind.String)
            {
                launchToken = launchTokenElement.GetString();
            }

            return new GameHealthSnapshot(processId, launchToken);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
