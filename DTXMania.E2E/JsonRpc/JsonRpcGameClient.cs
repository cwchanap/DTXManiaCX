using System.Net.Http.Json;
using System.Text.Json;
using DTXMania.E2E.Telemetry;

namespace DTXMania.E2E.JsonRpc;

public sealed class JsonRpcGameClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private int _nextId;

    public JsonRpcGameClient(HttpClient httpClient, string apiKey)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _apiKey = apiKey;
    }

    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "health");
            AddApiKey(request);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    public async Task<E2EGameState> GetGameStateAsync(CancellationToken cancellationToken)
    {
        var result = await SendAsync("getGameState", null, cancellationToken);
        return result.Deserialize<E2EGameState>(JsonOptions)
            ?? throw new InvalidOperationException("getGameState returned an empty result.");
    }

    public async Task SendKeyAsync(string key, TimeSpan holdDuration, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        await SendInputAsync(2, key, cancellationToken);
        if (holdDuration > TimeSpan.Zero)
            await Task.Delay(holdDuration, cancellationToken);
        await SendInputAsync(3, key, cancellationToken);
    }

    public async Task<string?> TakeScreenshotBase64Async(CancellationToken cancellationToken)
    {
        var result = await SendAsync("takeScreenshot", null, cancellationToken);
        return result.TryGetProperty("imageData", out var imageData) && imageData.ValueKind == JsonValueKind.String
            ? imageData.GetString()
            : null;
    }

    private Task<JsonElement> SendInputAsync(int type, string key, CancellationToken cancellationToken)
    {
        return SendAsync("sendInput", new { type, data = key }, cancellationToken);
    }

    private async Task<JsonElement> SendAsync(string method, object? parameters, CancellationToken cancellationToken)
    {
        var request = new
        {
            jsonrpc = "2.0",
            id = Interlocked.Increment(ref _nextId),
            method,
            @params = parameters
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "jsonrpc")
        {
            Content = JsonContent.Create(request)
        };
        AddApiKey(httpRequest);

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        if (root.TryGetProperty("error", out var error) && error.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
            throw new InvalidOperationException($"JSON-RPC {method} failed: {error}");

        if (!root.TryGetProperty("result", out var result))
            throw new InvalidOperationException($"JSON-RPC {method} did not include a result.");

        return result.Clone();
    }

    private void AddApiKey(HttpRequestMessage request)
    {
        if (!string.IsNullOrWhiteSpace(_apiKey))
            request.Headers.Add("X-Api-Key", _apiKey);
    }
}
