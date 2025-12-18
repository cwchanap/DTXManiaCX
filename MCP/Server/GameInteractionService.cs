using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DTXManiaCX.MCP.Server.Services;

/// <summary>
/// Configuration options for GameInteractionService
/// </summary>
public class GameInteractionOptions
{
    /// <summary>
    /// The JSON-RPC endpoint URL for the game API
    /// </summary>
    public string GameApiUrl { get; set; } = "http://localhost:8080/jsonrpc";
    
    /// <summary>
    /// The API key for authenticating with the game API (optional, but required if game has EnableGameApi with a key set)
    /// </summary>
    public string? GameApiKey { get; set; }
}

/// <summary>
/// Service for interacting with .NET game applications via JSON-RPC 2.0
/// Provides tools for sending input and retrieving game state through JSON-RPC protocol
/// </summary>
public class GameInteractionService : IDisposable
{
    private readonly ILogger<GameInteractionService> _logger;
    private readonly JsonRpcClient _jsonRpcClient;
    private readonly string _gameApiUrl;
    private static readonly JsonSerializerOptions ResponseDeserializationOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public GameInteractionService(ILogger<GameInteractionService> logger, ILoggerFactory loggerFactory, GameInteractionOptions? options = null)
    {
        _logger = logger;
        
        options ??= new GameInteractionOptions();
        _gameApiUrl = options.GameApiUrl;
        _jsonRpcClient = new JsonRpcClient(_gameApiUrl, options.GameApiKey, loggerFactory.CreateLogger<JsonRpcClient>());
    }

    /// <summary>
    /// Simple window information class
    /// </summary>
    public class WindowInfo
    {
        public int Left { get; set; }
        public int Top { get; set; }
        public int Right { get; set; }
        public int Bottom { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
    }

    private Task<(bool Success, string Message)> SendInputAsync(string methodName, object inputParams, string clientId, string successMessage)
    {
        return SendInputAsync(methodName, inputParams, clientId, successMessage, CancellationToken.None);
    }

    private async Task<(bool Success, string Message)> SendInputAsync(string methodName, object inputParams, string clientId, string successMessage, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _jsonRpcClient.SendRequestAsync(methodName, inputParams, cancellationToken);

            if (response.Result is not JsonElement resultElement)
            {
                return (false, "No result returned from game");
            }

            if (!resultElement.TryGetProperty("success", out var successProperty))
            {
                return (false, "No result returned from game");
            }

            var success = successProperty.GetBoolean();
            if (success)
            {
                _logger.LogInformation("Successfully sent {Method} input to game", methodName);
                return (true, successMessage);
            }

            return (false, "Game rejected the input");
        }
        catch (JsonRpcException ex)
        {
            _logger.LogError(ex, "JSON-RPC error sending {Method} input for client {ClientId}", methodName, clientId);
            return (false, $"JSON-RPC Error {ex.ErrorCode}: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending {Method} input for client {ClientId}", methodName, clientId);
            return (false, $"Error: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Click at a specific position in a game window via JSON-RPC
    /// </summary>
    /// <param name="clientId">Game client identifier</param>
    /// <param name="x">X coordinate (relative to game window)</param>
    /// <param name="y">Y coordinate (relative to game window)</param>
    /// <param name="button">Mouse button to click (left, right, middle)</param>
    /// <returns>Success status and any error message</returns>
    public async Task<(bool Success, string Message)> ClickAsync(string clientId, int x, int y, string button = "left", CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            _logger.LogInformation("Attempting to click at position ({X}, {Y}) for client {ClientId}", x, y, clientId);

            var inputParams = new
            {
                Type = 0, // MouseClick
                Data = new
                {
                    x = x,
                    y = y,
                    button = button,
                    clientId = clientId
                }
            };

            var response = await _jsonRpcClient.SendRequestAsync("sendInput", inputParams, cancellationToken);
            
            if (response.Result != null)
            {
                var resultElement = (JsonElement)response.Result;
                var success = resultElement.GetProperty("success").GetBoolean();
                
                if (success)
                {
                    _logger.LogInformation("Successfully sent click input to game");
                    return (true, $"Successfully clicked at ({x}, {y})");
                }
                else
                {
                    return (false, "Game rejected the input");
                }
            }
            
            return (false, "No result returned from game");
        }
        catch (JsonRpcException ex)
        {
            _logger.LogError(ex, "JSON-RPC error sending click input for client {ClientId}", clientId);
            return (false, $"JSON-RPC Error {ex.ErrorCode}: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending click input for client {ClientId}", clientId);
            return (false, $"Error: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Drag from one position to another in the game window via JSON-RPC
    /// </summary>
    /// <param name="clientId">Game client identifier</param>
    /// <param name="startX">Starting X coordinate</param>
    /// <param name="startY">Starting Y coordinate</param>
    /// <param name="endX">Ending X coordinate</param>
    /// <param name="endY">Ending Y coordinate</param>
    /// <param name="durationMs">Duration of the drag in milliseconds</param>
    /// <returns>Success status and any error message</returns>
    public async Task<(bool Success, string Message)> DragAsync(string clientId, int startX, int startY, int endX, int endY, int durationMs = 500, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _logger.LogInformation("Attempting to drag from ({StartX}, {StartY}) to ({EndX}, {EndY}) for client {ClientId}",
            startX, startY, endX, endY, clientId);

        var inputParams = new
        {
            Type = 1, // MouseMove (we'll extend this to support drag)
            Data = new
            {
                startX = startX,
                startY = startY,
                endX = endX,
                endY = endY,
                durationMs = durationMs,
                clientId = clientId,
                isDrag = true
            }
        };

        return await SendInputAsync("sendInput", inputParams, clientId, $"Successfully dragged from ({startX}, {startY}) to ({endX}, {endY})", cancellationToken);
    }
    
    /// <summary>
    /// Get the current game state via JSON-RPC.
    /// </summary>
    /// <remarks>
    /// Note: The clientId parameter is used for logging context only. The current implementation
    /// only supports a single game instance. Multi-client support would require the game API
    /// to track and differentiate between multiple client sessions.
    /// </remarks>
    /// <param name="clientId">Client identifier used for logging context (not passed to game API).</param>
    /// <returns>Game state information.</returns>
    public async Task<(bool Success, string Message, GameState? GameState)> GetGameStateAsync(string clientId, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            _logger.LogInformation("Retrieving game state for client {ClientId}", clientId);

            var response = await _jsonRpcClient.SendRequestAsync("getGameState", null, cancellationToken);
            
            if (response.Result != null)
            {
                var gameState = DeserializeResult<GameState>(response.Result);
                _logger.LogInformation("Successfully retrieved game state for client {ClientId}", clientId);
                return (true, "Game state retrieved successfully", gameState);
            }
            
            return (false, "No game state returned", null);
        }
        catch (JsonRpcException ex)
        {
            _logger.LogError(ex, "JSON-RPC error retrieving game state for client {ClientId}", clientId);
            return (false, $"JSON-RPC Error {ex.ErrorCode}: {ex.Message}", null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving game state for client {ClientId}", clientId);
            return (false, $"Error: {ex.Message}", null);
        }
    }
    
    /// <summary>
    /// Get window information for a game client via JSON-RPC
    /// </summary>
    /// <param name="clientId">Game client identifier</param>
    /// <returns>Window dimensions and position</returns>
    public async Task<(bool Success, string Message, WindowInfo? WindowRect)> GetWindowInfoAsync(string clientId, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            _logger.LogInformation("Retrieving window info for client {ClientId}", clientId);

            var response = await _jsonRpcClient.SendRequestAsync("getWindowInfo", null, cancellationToken);
            
            if (response.Result != null)
            {
                var windowInfo = DeserializeResult<WindowInfo>(response.Result);
                _logger.LogInformation("Successfully retrieved window info for client {ClientId}", clientId);
                return (true, "Window info retrieved successfully", windowInfo);
            }
            
            return (false, "No window info returned", null);
        }
        catch (JsonRpcException ex)
        {
            _logger.LogError(ex, "JSON-RPC error retrieving window info for client {ClientId}", clientId);
            return (false, $"JSON-RPC Error {ex.ErrorCode}: {ex.Message}", null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving window info for client {ClientId}", clientId);
            return (false, $"Error: {ex.Message}", null);
        }
    }
    
    /// <summary>
    /// Get the default (single) client's game state via JSON-RPC.
    /// </summary>
    /// <remarks>
    /// Note: This implementation only supports a single game client. Multi-client support
    /// would require the game API to track and expose multiple client sessions.
    /// The returned dictionary always contains a single "default" entry.
    /// </remarks>
    /// <returns>Dictionary containing the default client's game state.</returns>
    public async Task<(bool Success, string Message, Dictionary<string, GameState>? GameStates)> GetDefaultClientStateAsync()
    {
        try
        {
            _logger.LogInformation("Retrieving active clients list");

            var response = await _jsonRpcClient.SendRequestAsync("getGameState");
            
            if (response.Result != null)
            {
                var gameState = DeserializeResult<GameState>(response.Result) ?? new GameState();
                var gameStates = new Dictionary<string, GameState>
                {
                    ["default"] = gameState
                };

                _logger.LogInformation("Retrieved game client information");
                return (true, $"Found {gameStates.Count} active clients", gameStates);
            }
            
            return (false, "No game state returned", null);
        }
        catch (JsonRpcException ex)
        {
            _logger.LogError(ex, "JSON-RPC error listing active clients");
            return (false, $"JSON-RPC Error {ex.ErrorCode}: {ex.Message}", null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing active clients");
            return (false, $"Error: {ex.Message}", null);
        }
    }
    
    private static T? DeserializeResult<T>(object? result)
    {
        if (result is null)
        {
            return default;
        }

        if (result is JsonElement element)
        {
            return element.Deserialize<T>(ResponseDeserializationOptions);
        }

        var json = JsonSerializer.Serialize(result);
        return JsonSerializer.Deserialize<T>(json, ResponseDeserializationOptions);
    }
    
    /// <summary>
    /// Simulate keyboard input to the game via JSON-RPC
    /// </summary>
    /// <param name="clientId">Game client identifier</param>
    /// <param name="key">Key to press (e.g., "W", "Space", "Enter")</param>
    /// <param name="holdDurationMs">How long to hold the key in milliseconds</param>
    /// <returns>Success status and any error message</returns>
    public async Task<(bool Success, string Message)> SendKeyAsync(string clientId, string key, int holdDurationMs = 50, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            _logger.LogInformation("Attempting to send key '{Key}' to client {ClientId}", key, clientId);

            var inputParams = new
            {
                Type = 2, // KeyPress
                Data = new
                {
                    key = key,
                    holdDurationMs = holdDurationMs,
                    clientId = clientId
                }
            };

            var response = await _jsonRpcClient.SendRequestAsync("sendInput", inputParams, cancellationToken);
            
            if (response.Result != null)
            {
                var resultElement = (JsonElement)response.Result;
                var success = resultElement.GetProperty("success").GetBoolean();
                
                if (success)
                {
                    _logger.LogInformation("Successfully sent key input to game");
                    return (true, $"Key '{key}' sent successfully");
                }
                else
                {
                    return (false, "Game rejected the input");
                }
            }
            
            return (false, "No result returned from game");
        }
        catch (JsonRpcException ex)
        {
            _logger.LogError(ex, "JSON-RPC error sending key to client {ClientId}", clientId);
            return (false, $"JSON-RPC Error {ex.ErrorCode}: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending key to client {ClientId}", clientId);
            return (false, $"Error: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Dispose of JSON-RPC client resources
    /// </summary>
    public void Dispose()
    {
        _jsonRpcClient?.Dispose();
    }
}
