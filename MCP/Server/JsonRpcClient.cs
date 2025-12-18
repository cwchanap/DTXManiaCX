using Microsoft.Extensions.Logging;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace DTXManiaCX.MCP.Server.Services;

/// <summary>
/// JSON-RPC 2.0 client for communicating with game
/// </summary>
public class JsonRpcClient : IDisposable
{
    private readonly ILogger<JsonRpcClient>? _logger;
    private readonly HttpClient _httpClient;
    private readonly string _serverUrl;
    private readonly string? _apiKey;
    private readonly JsonSerializerOptions _jsonOptions;
    private int _requestId;

    public JsonRpcClient(string serverUrl, string? apiKey = null, ILogger<JsonRpcClient>? logger = null)
    {
        _logger = logger;
        _apiKey = apiKey;

        var handler = new SocketsHttpHandler
        {
            UseCookies = false
        };

        _httpClient = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = TimeSpan.FromSeconds(5)
        };
        _serverUrl = serverUrl.TrimEnd('/'); // Remove trailing slash if present
        
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
        
        _requestId = 0;
    }

    /// <summary>
    /// Send a JSON-RPC request and expect a response
    /// </summary>
    public async Task<JsonRpcResponse> SendRequestAsync(string method, object? parameters = null)
    {
        var requestId = Interlocked.Increment(ref _requestId);
        
        var request = new JsonRpcRequest
        {
            Id = requestId,
            Method = method,
            Params = parameters
        };

        return await SendRequestAsync(request, CancellationToken.None);
    }

    /// <summary>
    /// Send a JSON-RPC request and expect a response
    /// </summary>
    public async Task<JsonRpcResponse> SendRequestAsync(string method, object? parameters, CancellationToken cancellationToken)
    {
        var requestId = Interlocked.Increment(ref _requestId);

        var request = new JsonRpcRequest
        {
            Id = requestId,
            Method = method,
            Params = parameters
        };

        return await SendRequestAsync(request, cancellationToken);
    }

    /// <summary>
    /// Send a JSON-RPC notification (no response expected)
    /// </summary>
    public async Task<bool> SendNotificationAsync(string method, object? parameters = null)
    {
        var request = new JsonRpcRequest
        {
            Id = null, // Notifications have no ID
            Method = method,
            Params = parameters
        };

        try
        {
            await SendRequestAsync(request, CancellationToken.None);
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "SendNotificationAsync failed for method {Method}", method);
            return false;
        }
    }

    /// <summary>
    /// Send a JSON-RPC request object
    /// </summary>
    private async Task<JsonRpcResponse> SendRequestAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var json = JsonSerializer.Serialize(request, _jsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            // Add API key header if configured
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _serverUrl)
            {
                Content = content
            };
            if (!string.IsNullOrEmpty(_apiKey))
            {
                httpRequest.Headers.Add("X-Api-Key", _apiKey);
            }

            _logger?.LogDebug("Sending JSON-RPC request: {Method} with ID: {Id}", request.Method, request.Id);

            // IMPORTANT: If the game server is configured to require an API key, the X-Api-Key header must be set on this request
            // (see the header assignment above). Otherwise the server will return HTTP 401 before the JSON-RPC payload is processed.
            var httpResponse = await _httpClient.SendAsync(httpRequest, cancellationToken);
            
            if (!httpResponse.IsSuccessStatusCode)
            {
                var errorContent = await httpResponse.Content.ReadAsStringAsync();
                throw new JsonRpcException($"HTTP {httpResponse.StatusCode}: {errorContent}");
            }

            var responseJson = await httpResponse.Content.ReadAsStringAsync();
            
            // For notifications, there might be no response body
            if (request.IsNotification && string.IsNullOrEmpty(responseJson))
            {
                return new JsonRpcResponse { Id = null };
            }

            var response = JsonSerializer.Deserialize<JsonRpcResponse>(responseJson, _jsonOptions);
            
            if (response == null)
            {
                throw new JsonRpcException("Invalid JSON-RPC response format");
            }

            if (response.IsError)
            {
                throw new JsonRpcException($"JSON-RPC Error {response.Error!.Code}: {response.Error.Message}", 
                    response.Error.Code, response.Error.Data);
            }

            _logger?.LogDebug("Received JSON-RPC response for ID: {Id}", response.Id);
            return response;
        }
        catch (JsonException ex)
        {
            _logger?.LogError(ex, "JSON serialization error");
            throw new JsonRpcException($"JSON serialization error: {ex.Message}", ex);
        }
        catch (HttpRequestException ex)
        {
            _logger?.LogError(ex, "HTTP request error");
            throw new JsonRpcException($"HTTP request error: {ex.Message}", ex);
        }
        catch (TaskCanceledException ex)
        {
            _logger?.LogError(ex, "Request timeout");
            throw new JsonRpcException($"Request timeout: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Ping the server
    /// </summary>
    public async Task<bool> PingAsync()
    {
        try
        {
            var response = await SendRequestAsync("ping");
            return response.Result != null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Dispose of resources
    /// </summary>
    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}

/// <summary>
/// JSON-RPC 2.0 message classes for client.
/// </summary>
/// <remarks>
/// These types mirror DTXMania.Game.Lib.JsonRpc.JsonRpcMessage types to enable
/// JSON-RPC communication. The MCP project is intentionally kept independent
/// of DTXMania.Game to allow standalone distribution.
/// 
/// When modifying these classes, ensure the properties match those in
/// DTXMania.Game/Lib/JsonRpc/JsonRpcMessage.cs to maintain wire-compatibility.
/// This duplication is intentional, but it can drift over time; if that becomes a maintenance burden,
/// consider extracting the shared JSON-RPC message types into a common assembly referenced by both projects.
/// </remarks>
public class JsonRpcRequest
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; set; } = "2.0";

    [JsonPropertyName("method")]
    public string Method { get; set; } = string.Empty;

    [JsonPropertyName("params")]
    public object? Params { get; set; }

    [JsonPropertyName("id")]
    public object? Id { get; set; }

    [JsonIgnore]
    public bool IsNotification => Id == null;
}

public class JsonRpcResponse
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; set; } = "2.0";

    [JsonPropertyName("id")]
    public object? Id { get; set; }

    [JsonPropertyName("result")]
    public object? Result { get; set; }

    [JsonPropertyName("error")]
    public JsonRpcError? Error { get; set; }

    [JsonIgnore]
    public bool IsError => Error != null;
}

public class JsonRpcError
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("data")]
    public object? Data { get; set; }
}

/// <summary>
/// JSON-RPC specific exception
/// </summary>
public class JsonRpcException : Exception
{
    public int? ErrorCode { get; }
    public object? ErrorData { get; }

    public JsonRpcException(string message) : base(message)
    {
    }

    public JsonRpcException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public JsonRpcException(string message, int errorCode, object? errorData = null) : base(message)
    {
        ErrorCode = errorCode;
        ErrorData = errorData;
    }
}
