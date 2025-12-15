using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DTXMania.Game.Lib.JsonRpc;

/// <summary>
/// JSON-RPC 2.0 server that exposes game API for MCP server to connect to
/// </summary>
public class JsonRpcServer : IDisposable, IAsyncDisposable
{
    private readonly IGameApi _gameApi;
    private readonly ILogger<JsonRpcServer>? _logger;
    private readonly int _port;
    private readonly string _apiKey;
    private IHost? _host;
    private bool _isRunning;
    private CancellationTokenSource? _cancellationTokenSource;
    private readonly JsonSerializerOptions _jsonOptions;

    public JsonRpcServer(IGameApi gameApi, int port = 8080, string apiKey = "", ILogger<JsonRpcServer>? logger = null)
    {
        _gameApi = gameApi ?? throw new ArgumentNullException(nameof(gameApi));

        if (port < 1 || port > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port), port, "Port must be between 1 and 65535.");
        }

        _port = port;
        _apiKey = apiKey;
        _logger = logger;
        
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
    }

    /// <summary>
    /// Start the JSON-RPC server
    /// </summary>
    /// <param name="cancellationToken">Optional cancellation token to stop server startup</param>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_isRunning)
            return;

        _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            _host = Host.CreateDefaultBuilder()
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseKestrel(options =>
                    {
                        options.Listen(IPAddress.Loopback, _port);
                    });
                    webBuilder.Configure(app =>
                    {
                        app.UseRouting();
                        app.UseEndpoints(endpoints =>
                        {
                            endpoints.MapPost("/jsonrpc", HandleJsonRpcRequest);
                            
                            endpoints.MapGet("/health", async context =>
                            {
                                var response = new
                                {
                                    status = "ok",
                                    protocol = "JSON-RPC 2.0",
                                    game_running = _gameApi.IsRunning,
                                    timestamp = DateTime.UtcNow
                                };
                                context.Response.ContentType = "application/json";
                                await context.Response.WriteAsync(JsonSerializer.Serialize(response, _jsonOptions));
                            });
                        });
                    });
                })
                .ConfigureServices(services =>
                {
                    services.AddRouting();
                })
                .Build();

            await _host.StartAsync(_cancellationTokenSource.Token);
            _isRunning = true;

            System.Diagnostics.Debug.WriteLine($"JSON-RPC server started on http://localhost:{_port}/jsonrpc");
            _logger?.LogInformation("JSON-RPC server started on port {Port}", _port);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to start JSON-RPC server: {ex.Message}");
            _logger?.LogError(ex, "Failed to start JSON-RPC server");
            throw;
        }
    }

    /// <summary>
    /// Handle JSON-RPC requests
    /// </summary>
    private async Task HandleJsonRpcRequest(HttpContext context)
    {
        JsonRpcResponse response;
        object? requestId = null;

        try
        {
            // Validate API key if configured
            if (!string.IsNullOrEmpty(_apiKey))
            {
                var providedKey = context.Request.Headers["X-Api-Key"].FirstOrDefault();
                if (providedKey != _apiKey)
                {
                    context.Response.StatusCode = 401;
                    response = CreateErrorResponse(null, JsonRpcErrorCodes.InvalidRequest, 
                        "Unauthorized: Invalid or missing API key");
                    await SendJsonRpcResponse(context, response);
                    return;
                }
            }

            // Check request size limit (prevent large payloads)
            if (context.Request.ContentLength > 1024) // 1KB limit
            {
                response = CreateErrorResponse(null, JsonRpcErrorCodes.InvalidRequest, 
                    "Request payload too large");
                await SendJsonRpcResponse(context, response);
                return;
            }

            // Read and parse request
            string requestBody;
            using (var reader = new StreamReader(context.Request.Body))
            {
                requestBody = await reader.ReadToEndAsync();
            }

            JsonRpcRequest? request;
            try
            {
                request = JsonSerializer.Deserialize<JsonRpcRequest>(requestBody, _jsonOptions);
            }
            catch (JsonException)
            {
                response = CreateErrorResponse(null, JsonRpcErrorCodes.ParseError, 
                    "Invalid JSON format");
                await SendJsonRpcResponse(context, response);
                return;
            }

            if (request == null)
            {
                response = CreateErrorResponse(null, JsonRpcErrorCodes.InvalidRequest, 
                    "Request is null");
                await SendJsonRpcResponse(context, response);
                return;
            }

            requestId = request.Id;

            // NOTE: This validation only checks JSON-RPC 2.0 request shape (jsonrpc/method), not caller authentication.
            // If an API key is configured, it must be validated separately before routing (otherwise any local process can invoke methods).
            // Validate JSON-RPC format
            if (request.JsonRpc != "2.0" || string.IsNullOrEmpty(request.Method))
            {
                response = CreateErrorResponse(requestId, JsonRpcErrorCodes.InvalidRequest, 
                    "Invalid JSON-RPC 2.0 request format");
                await SendJsonRpcResponse(context, response);
                return;
            }

            // Route the method call
            response = await RouteMethodCall(request);

            // Don't send response for notifications
            if (!request.IsNotification)
            {
                await SendJsonRpcResponse(context, response);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error handling JSON-RPC request");
            response = CreateErrorResponse(requestId, JsonRpcErrorCodes.InternalError, 
                "Internal server error");
            await SendJsonRpcResponse(context, response);
        }
    }

    /// <summary>
    /// Route method calls to appropriate handlers
    /// </summary>
    private async Task<JsonRpcResponse> RouteMethodCall(JsonRpcRequest request)
    {
        try
        {
            switch (request.Method)
            {
                case "getGameState":
                    return await HandleGetGameState(request);
                
                case "getWindowInfo":
                    return await HandleGetWindowInfo(request);
                
                case "sendInput":
                    return await HandleSendInput(request);
                
                case "ping":
                    return CreateSuccessResponse(request.Id, new { pong = true, timestamp = DateTime.UtcNow });
                
                default:
                    return CreateErrorResponse(request.Id, JsonRpcErrorCodes.MethodNotFound, 
                        $"Method '{request.Method}' not found");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error executing method {Method}", request.Method);
            return CreateErrorResponse(request.Id, JsonRpcErrorCodes.InternalError, 
                $"Error executing method: {ex.Message}");
        }
    }

    /// <summary>
    /// Handle getGameState method
    /// </summary>
    private async Task<JsonRpcResponse> HandleGetGameState(JsonRpcRequest request)
    {
        if (!_gameApi.IsRunning)
        {
            return CreateErrorResponse(request.Id, JsonRpcErrorCodes.GameNotRunning, 
                "Game is not running");
        }

        var gameState = await _gameApi.GetGameStateAsync();
        return CreateSuccessResponse(request.Id, gameState);
    }

    /// <summary>
    /// Handle getWindowInfo method
    /// </summary>
    private async Task<JsonRpcResponse> HandleGetWindowInfo(JsonRpcRequest request)
    {
        if (!_gameApi.IsRunning)
        {
            return CreateErrorResponse(request.Id, JsonRpcErrorCodes.GameNotRunning, 
                "Game is not running");
        }

        var windowInfo = await _gameApi.GetWindowInfoAsync();
        return CreateSuccessResponse(request.Id, windowInfo);
    }

    /// <summary>
    /// Handle sendInput method
    /// </summary>
    private async Task<JsonRpcResponse> HandleSendInput(JsonRpcRequest request)
    {
        if (!_gameApi.IsRunning)
        {
            return CreateErrorResponse(request.Id, JsonRpcErrorCodes.GameNotRunning, 
                "Game is not running");
        }

        if (request.Params == null)
        {
            return CreateErrorResponse(request.Id, JsonRpcErrorCodes.InvalidParams, 
                "Input parameters are required");
        }

        try
        {
            // Deserialize params as GameInput
            var jsonElement = (JsonElement)request.Params;
            var gameInput = JsonSerializer.Deserialize<GameInput>(jsonElement.GetRawText(), _jsonOptions);

            if (gameInput == null)
            {
                return CreateErrorResponse(request.Id, JsonRpcErrorCodes.InvalidParams, 
                    "Invalid input format");
            }

            // Validate input
            var (isValid, errorMessage) = ValidateGameInput(gameInput);
            if (!isValid)
            {
                return CreateErrorResponse(request.Id, JsonRpcErrorCodes.InvalidInput, 
                    errorMessage);
            }

            var success = await _gameApi.SendInputAsync(gameInput);
            return CreateSuccessResponse(request.Id, new { success });
        }
        catch (JsonException ex)
        {
            return CreateErrorResponse(request.Id, JsonRpcErrorCodes.InvalidParams, 
                $"Invalid input format: {ex.Message}");
        }
    }

    /// <summary>
    /// Validate game input for security and correctness
    /// </summary>
    private static (bool IsValid, string ErrorMessage) ValidateGameInput(GameInput input)
    {
        // Validate input type
        if (!Enum.IsDefined(typeof(InputType), input.Type))
            return (false, "Invalid input type");

        // Validate data based on input type
        switch (input.Type)
        {
            case InputType.MouseClick:
            case InputType.MouseMove:
                // For mouse input, data should contain position info
                if (input.Data is null || input.Data.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                    return (false, "Mouse input requires position data");

                if (input.Data.Value.ValueKind != JsonValueKind.Object)
                    return (false, "Mouse input data must be an object");
                break;

            case InputType.KeyPress:
            case InputType.KeyRelease:
                // For key input, data should contain key info
                if (input.Data is null || input.Data.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                    return (false, "Key input requires key data");

                if (input.Data.Value.ValueKind == JsonValueKind.String)
                {
                    var keyData = input.Data.Value.GetString();
                    if (string.IsNullOrEmpty(keyData) || keyData.Length > 50)
                        return (false, "Invalid key data format");
                }
                else if (input.Data.Value.ValueKind == JsonValueKind.Number)
                {
                    if (!input.Data.Value.TryGetInt32(out var keyCode) || keyCode < 0 || keyCode > 255)
                        return (false, "Invalid key data format");
                }
                else
                {
                    return (false, "Invalid key data format");
                }
                break;

            default:
                return (false, "Unsupported input type");
        }

        return (true, string.Empty);
    }

    /// <summary>
    /// Create success response
    /// </summary>
    private JsonRpcResponse CreateSuccessResponse(object? id, object result)
    {
        return new JsonRpcResponse
        {
            Id = id,
            Result = result
        };
    }

    /// <summary>
    /// Create error response
    /// </summary>
    private JsonRpcResponse CreateErrorResponse(object? id, int code, string message, object? data = null)
    {
        return new JsonRpcResponse
        {
            Id = id,
            Error = new JsonRpcError
            {
                Code = code,
                Message = message,
                Data = data
            }
        };
    }

    /// <summary>
    /// Send JSON-RPC response
    /// </summary>
    private async Task SendJsonRpcResponse(HttpContext context, JsonRpcResponse response)
    {
        context.Response.ContentType = "application/json";
        var json = JsonSerializer.Serialize(response, _jsonOptions);
        await context.Response.WriteAsync(json);
    }

    /// <summary>
    /// Stop the JSON-RPC server
    /// </summary>
    public async Task StopAsync()
    {
        if (!_isRunning)
            return;

        try
        {
            _cancellationTokenSource?.Cancel();

            if (_host != null)
            {
                await _host.StopAsync();
                _host.Dispose();
                _host = null;
            }

            _isRunning = false;
            System.Diagnostics.Debug.WriteLine("JSON-RPC server stopped");
            _logger?.LogInformation("JSON-RPC server stopped");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error stopping JSON-RPC server: {ex.Message}");
            _logger?.LogError(ex, "Error stopping JSON-RPC server");
        }
    }

    /// <summary>
    /// Get the server URL
    /// </summary>
    public string GetServerUrl()
    {
        return $"http://localhost:{_port}/jsonrpc";
    }

    /// <summary>
    /// Check if server is running
    /// </summary>
    public bool IsRunning => _isRunning;

    /// <summary>
    /// Dispose of resources asynchronously
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_isRunning)
        {
            await StopAsync();
        }

        _cancellationTokenSource?.Dispose();
        _host?.Dispose();
    }

    /// <summary>
    /// Dispose of resources synchronously
    /// </summary>
    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}