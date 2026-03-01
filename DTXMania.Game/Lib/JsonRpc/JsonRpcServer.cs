#nullable enable

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
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
using DTXMania.Game.Lib.Config;

namespace DTXMania.Game.Lib.JsonRpc;

/// <summary>
/// JSON-RPC 2.0 server that exposes game API for MCP server to connect to.
/// </summary>
/// <remarks>
/// Uses structured logging via ILogger for server lifecycle events.
/// Request body size limit is configured via GameConstants.JsonRpc.MaxRequestBodyBytes.
/// Supports API key authentication and provides health check endpoint.
/// </remarks>
public class JsonRpcServer : IDisposable, IAsyncDisposable
{
    private readonly IGameApi _gameApi;
    private readonly ILogger<JsonRpcServer>? _logger;
    private readonly int _port;
    private readonly string _apiKey;

    private IHost? _host;
    private bool _isRunning;
    private bool _disposed;
    private CancellationTokenSource? _cancellationTokenSource;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly SemaphoreSlim _lifecycleSemaphore = new SemaphoreSlim(1, 1);
    private int _lifecycleSemaphoreDisposed;

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
        ThrowIfDisposed();
        await _lifecycleSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (_isRunning)
                return;

            _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            try
            {
                // Use HostBuilder directly (instead of Host.CreateDefaultBuilder) to avoid
                // calling Directory.GetCurrentDirectory(), which can fail when the process
                // working directory is deleted (e.g. in parallel test runs).
                _host = new HostBuilder()
                    .UseContentRoot(AppContext.BaseDirectory)
                    .ConfigureWebHostDefaults(webBuilder =>
                    {
                        webBuilder.UseKestrel(options =>
                        {
                            options.Listen(IPAddress.Loopback, _port);
                            options.Limits.MaxRequestBodySize = GameConstants.JsonRpc.MaxRequestBodyBytes;
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
                                        gameRunning = _gameApi.IsRunning,
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

                await _host.StartAsync(_cancellationTokenSource.Token).ConfigureAwait(false);
                _isRunning = true;

                _logger?.LogInformation("JSON-RPC server started on port {Port}", _port);
            }
            catch (Exception ex)
            {
                _isRunning = false;

                try
                {
                    _cancellationTokenSource?.Cancel();
                }
                catch (Exception cancelEx)
                {
                    _logger?.LogWarning(cancelEx, "Failed to cancel JSON-RPC server token during startup failure");
                }

                try
                {
                    if (_host != null)
                    {
                        await _host.StopAsync().ConfigureAwait(false);
                        _host.Dispose();
                        _host = null;
                    }
                }
                catch (Exception stopEx)
                {
                    _logger?.LogWarning(stopEx, "Failed to stop JSON-RPC server host during startup failure");
                }

                _logger?.LogError(ex, "Failed to start JSON-RPC server");

                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;

                throw;
            }
        }
        finally
        {
            _lifecycleSemaphore.Release();
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
            var maxRequestBodySizeFeature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (maxRequestBodySizeFeature != null && !maxRequestBodySizeFeature.IsReadOnly)
            {
                maxRequestBodySizeFeature.MaxRequestBodySize = GameConstants.JsonRpc.MaxRequestBodyBytes;
            }

            // Validate API key if configured
            if (!string.IsNullOrEmpty(_apiKey))
            {
                var providedKey = context.Request.Headers["X-Api-Key"].FirstOrDefault();
                if (string.IsNullOrEmpty(providedKey) || !ConstantTimeEquals(providedKey, _apiKey))
                {
                    context.Response.StatusCode = 401;
                    response = CreateErrorResponse(null, JsonRpcErrorCodes.InvalidRequest, 
                        "Unauthorized: Invalid or missing API key");
                    await SendJsonRpcResponse(context, response);
                    return;
                }
            }

            // Check request size limit (prevent large payloads)
            if (context.Request.ContentLength.HasValue && context.Request.ContentLength.Value > GameConstants.JsonRpc.MaxRequestBodyBytes)
            {
                context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
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

            // NOTE: This validation only checks JSON-RPC 2.0 request shape (jsonrpc/method); authentication was handled above (lines 179-190).

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
        catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            response = CreateErrorResponse(requestId, JsonRpcErrorCodes.InvalidRequest,
                "Request payload too large");
            await SendJsonRpcResponse(context, response);
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
                
                case "takeScreenshot":
                    return await HandleTakeScreenshot(request);

                case "changeStage":
                    return await HandleChangeStage(request);

                case "ping":
                    return CreateSuccessResponse(request.Id, new { pong = true, timestamp = DateTime.UtcNow });

                default:
                    return CreateErrorResponse(request.Id, JsonRpcErrorCodes.MethodNotFound,
                        $"Method '{request.Method}' not found");
            }
        }
        catch (Exception ex)
        {
            var correlationId = Guid.NewGuid().ToString();
            _logger?.LogError(ex, "Error executing method {Method}. CorrelationId: {CorrelationId}", request.Method, correlationId);
            return CreateErrorResponse(
                request.Id,
                JsonRpcErrorCodes.InternalError,
                $"Internal server error. CorrelationId: {correlationId}");
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
            if (request.Params is not JsonElement jsonElement)
            {
                return CreateErrorResponse(request.Id, JsonRpcErrorCodes.InvalidParams,
                    "Invalid input format");
            }

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
    /// Handle takeScreenshot method
    /// </summary>
    private async Task<JsonRpcResponse> HandleTakeScreenshot(JsonRpcRequest request)
    {
        if (!_gameApi.IsRunning)
        {
            return CreateErrorResponse(request.Id, JsonRpcErrorCodes.GameNotRunning,
                "Game is not running");
        }

        var pngBytes = await _gameApi.TakeScreenshotAsync();
        if (pngBytes == null)
        {
            return CreateErrorResponse(request.Id, JsonRpcErrorCodes.InternalError,
                "Screenshot capture failed (render target unavailable or already pending)");
        }

        var base64 = Convert.ToBase64String(pngBytes);
        return CreateSuccessResponse(request.Id, new
        {
            imageData = base64,
            mimeType = "image/png"
        });
    }

    /// <summary>
    /// Handle changeStage method
    /// </summary>
    private async Task<JsonRpcResponse> HandleChangeStage(JsonRpcRequest request)
    {
        if (!_gameApi.IsRunning)
        {
            return CreateErrorResponse(request.Id, JsonRpcErrorCodes.GameNotRunning,
                "Game is not running");
        }

        if (request.Params == null)
        {
            return CreateErrorResponse(request.Id, JsonRpcErrorCodes.InvalidParams,
                "stageName parameter is required");
        }

        string? stageName = null;
        try
        {
            if (request.Params is JsonElement paramsElement)
            {
                if (paramsElement.TryGetProperty("stageName", out var stageNameProp))
                {
                    if (stageNameProp.ValueKind != JsonValueKind.String)
                    {
                        return CreateErrorResponse(request.Id, JsonRpcErrorCodes.InvalidParams,
                            "stageName must be a string");
                    }
                    stageName = stageNameProp.GetString();
                }
            }
        }
        catch (JsonException ex)
        {
            return CreateErrorResponse(request.Id, JsonRpcErrorCodes.InvalidParams,
                $"Invalid params format: {ex.Message}");
        }

        if (string.IsNullOrWhiteSpace(stageName))
        {
            return CreateErrorResponse(request.Id, JsonRpcErrorCodes.InvalidParams,
                "stageName must be a non-empty string");
        }

        var success = await _gameApi.ChangeStageAsync(stageName);
        if (!success)
        {
            var validNames = string.Join(", ", Enum.GetNames(typeof(DTXMania.Game.Lib.Stage.StageType)));
            return CreateErrorResponse(request.Id, JsonRpcErrorCodes.InvalidParams,
                $"Unknown stage name '{stageName}'. Valid values: {validNames}");
        }

        return CreateSuccessResponse(request.Id, new { success = true, stageName });
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

    private static bool ConstantTimeEquals(string a, string b)
    {
        if (a.Length != b.Length)
            return false;

        var diff = 0;
        for (var i = 0; i < a.Length; i++)
        {
            diff |= a[i] ^ b[i];
        }

        return diff == 0;
    }

    /// <summary>
    /// Stop the JSON-RPC server
    /// </summary>
    public async Task StopAsync()
    {
        ThrowIfDisposed();
        await _lifecycleSemaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (!_isRunning)
                return;

            try
            {
                try
                {
                    _cancellationTokenSource?.Cancel();
                }
                catch (Exception cancelEx)
                {
                    _logger?.LogWarning(cancelEx, "Error cancelling JSON-RPC server");
                }

                if (_host != null)
                {
                    try
                    {
                        await _host.StopAsync().ConfigureAwait(false);
                    }
                    finally
                    {
                        try
                        {
                            _host.Dispose();
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogError(ex, "Error disposing JSON-RPC server host");
                        }
                        finally
                        {
                            _host = null;
                        }
                    }
                }

                _logger?.LogInformation("JSON-RPC server stopped");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error stopping JSON-RPC server");
                throw;
            }
            finally
            {
                if (_cancellationTokenSource != null)
                {
                    try
                    {
                        _cancellationTokenSource.Dispose();
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "Error disposing JSON-RPC server cancellation token source");
                    }
                    finally
                    {
                        _cancellationTokenSource = null;
                    }
                }
                _isRunning = false;
            }
        }
        finally
        {
            _lifecycleSemaphore.Release();
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(JsonRpcServer));
    }

    private static void CancelNoThrow(CancellationTokenSource? cancellationTokenSource)
    {
        if (cancellationTokenSource == null)
            return;

        try
        {
            cancellationTokenSource.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Expected when token source is already disposed
        }
        catch (AggregateException)
        {
            // Cancellation callback threw - suppress during dispose
        }
    }

    private void DisposeLifecycleSemaphoreOnce()
    {
        if (Interlocked.Exchange(ref _lifecycleSemaphoreDisposed, 1) != 0)
            return;

        try
        {
            _lifecycleSemaphore.Dispose();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void DisposeManagedResourcesSynchronously_NoLock()
    {
        CancelNoThrow(_cancellationTokenSource);

        _host?.Dispose();
        _host = null;

        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = null;

        _isRunning = false;
    }

    private async Task StopAndDisposeHostAsync_NoLock()
    {
        CancelNoThrow(_cancellationTokenSource);

        if (_host != null)
        {
            try
            {
                await _host.StopAsync().ConfigureAwait(false);
            }
            finally
            {
                _host.Dispose();
                _host = null;
            }
        }

        _isRunning = false;
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
        if (_disposed)
            return;

        await _lifecycleSemaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed)
                return;

            _disposed = true;
            await StopAndDisposeHostAsync_NoLock().ConfigureAwait(false);
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }
        finally
        {
            _lifecycleSemaphore.Release();
        }

        DisposeLifecycleSemaphoreOnce();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Dispose of resources synchronously
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposing)
            return;

        if (_disposed)
            return;

        _lifecycleSemaphore.Wait();
        try
        {
            if (_disposed)
                return;

            _disposed = true;
            DisposeManagedResourcesSynchronously_NoLock();
        }
        finally
        {
            _lifecycleSemaphore.Release();
        }

        DisposeLifecycleSemaphoreOnce();
    }
}