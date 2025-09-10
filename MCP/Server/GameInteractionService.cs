using Microsoft.Extensions.Logging;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;

namespace DTXManiaCX.MCP.Server.Services;

/// <summary>
/// Service for interacting with .NET game applications via HTTP API
/// Provides tools for sending input and retrieving game state through REST endpoints
/// </summary>
public class GameInteractionService : IDisposable
{
    private readonly ILogger<GameInteractionService> _logger;
    private readonly GameStateManager _gameStateManager;
    private readonly HttpClient _httpClient;
    private readonly string _gameApiUrl;

    public GameInteractionService(ILogger<GameInteractionService> logger, GameStateManager gameStateManager)
    {
        _logger = logger;
        _gameStateManager = gameStateManager;
        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(5); // Short timeout for game interactions
        _gameApiUrl = "http://localhost:8080"; // Default game API URL
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
    
    /// <summary>
    /// Click at a specific position in a game window via HTTP API
    /// </summary>
    /// <param name="clientId">Game client identifier</param>
    /// <param name="x">X coordinate (relative to game window)</param>
    /// <param name="y">Y coordinate (relative to game window)</param>
    /// <param name="button">Mouse button to click (left, right, middle)</param>
    /// <returns>Success status and any error message</returns>
    public async Task<(bool Success, string Message)> ClickAsync(string clientId, int x, int y, string button = "left")
    {
        try
        {
            _logger.LogInformation("Attempting to click at position ({X}, {Y}) for client {ClientId}", x, y, clientId);

            var input = new
            {
                Type = "MouseClick",
                Data = new
                {
                    x = x,
                    y = y,
                    button = button,
                    clientId = clientId
                }
            };

            var response = await _httpClient.PostAsJsonAsync($"{_gameApiUrl}/game/input", input);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<dynamic>();
                _logger.LogInformation("Successfully sent click input to game");
                return (true, $"Successfully clicked at ({x}, {y})");
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Failed to send click input: {StatusCode} - {Content}", response.StatusCode, errorContent);
                return (false, $"HTTP {response.StatusCode}: {errorContent}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending click input for client {ClientId}", clientId);
            return (false, $"Error: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Drag from one position to another in the game window via HTTP API
    /// </summary>
    /// <param name="clientId">Game client identifier</param>
    /// <param name="startX">Starting X coordinate</param>
    /// <param name="startY">Starting Y coordinate</param>
    /// <param name="endX">Ending X coordinate</param>
    /// <param name="endY">Ending Y coordinate</param>
    /// <param name="durationMs">Duration of the drag in milliseconds</param>
    /// <returns>Success status and any error message</returns>
    public async Task<(bool Success, string Message)> DragAsync(string clientId, int startX, int startY, int endX, int endY, int durationMs = 500)
    {
        try
        {
            _logger.LogInformation("Attempting to drag from ({StartX}, {StartY}) to ({EndX}, {EndY}) for client {ClientId}",
                startX, startY, endX, endY, clientId);

            var input = new
            {
                Type = "MouseDrag",
                Data = new
                {
                    startX = startX,
                    startY = startY,
                    endX = endX,
                    endY = endY,
                    durationMs = durationMs,
                    clientId = clientId
                }
            };

            var response = await _httpClient.PostAsJsonAsync($"{_gameApiUrl}/game/input", input);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<dynamic>();
                _logger.LogInformation("Successfully sent drag input to game");
                return (true, $"Successfully dragged from ({startX}, {startY}) to ({endX}, {endY})");
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Failed to send drag input: {StatusCode} - {Content}", response.StatusCode, errorContent);
                return (false, $"HTTP {response.StatusCode}: {errorContent}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending drag input for client {ClientId}", clientId);
            return (false, $"Error: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Get the current game state for a client via HTTP API
    /// </summary>
    /// <param name="clientId">Game client identifier</param>
    /// <returns>Game state information</returns>
    public async Task<(bool Success, string Message, GameState? GameState)> GetGameStateAsync(string clientId)
    {
        try
        {
            _logger.LogInformation("Retrieving game state for client {ClientId}", clientId);

            var response = await _httpClient.GetAsync($"{_gameApiUrl}/game/state");

            if (response.IsSuccessStatusCode)
            {
                var gameState = await response.Content.ReadFromJsonAsync<GameState>();
                _logger.LogInformation("Successfully retrieved game state for client {ClientId}", clientId);
                return (true, "Game state retrieved successfully", gameState);
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Failed to retrieve game state: {StatusCode} - {Content}", response.StatusCode, errorContent);
                return (false, $"HTTP {response.StatusCode}: {errorContent}", null);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving game state for client {ClientId}", clientId);
            return (false, $"Error: {ex.Message}", null);
        }
    }
    
    /// <summary>
    /// Get window information for a game client via HTTP API
    /// </summary>
    /// <param name="clientId">Game client identifier</param>
    /// <returns>Window dimensions and position</returns>
    public async Task<(bool Success, string Message, WindowInfo? WindowRect)> GetWindowInfoAsync(string clientId)
    {
        try
        {
            _logger.LogInformation("Retrieving window info for client {ClientId}", clientId);

            var response = await _httpClient.GetAsync($"{_gameApiUrl}/game/window");

            if (response.IsSuccessStatusCode)
            {
                var windowInfo = await response.Content.ReadFromJsonAsync<WindowInfo>();
                _logger.LogInformation("Successfully retrieved window info for client {ClientId}", clientId);
                return (true, "Window info retrieved successfully", windowInfo);
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Failed to retrieve window info: {StatusCode} - {Content}", response.StatusCode, errorContent);
                return (false, $"HTTP {response.StatusCode}: {errorContent}", null);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving window info for client {ClientId}", clientId);
            return (false, $"Error: {ex.Message}", null);
        }
    }
    
    /// <summary>
    /// List all active game clients via HTTP API
    /// </summary>
    /// <returns>List of active client IDs and their game states</returns>
    public async Task<(bool Success, string Message, Dictionary<string, GameState>? GameStates)> ListActiveClientsAsync()
    {
        try
        {
            _logger.LogInformation("Retrieving active clients list");

            var response = await _httpClient.GetAsync($"{_gameApiUrl}/game/state");

            if (response.IsSuccessStatusCode)
            {
                var gameState = await response.Content.ReadFromJsonAsync<GameState>();
                var gameStates = new Dictionary<string, GameState>
                {
                    ["default"] = gameState ?? new GameState()
                };

                _logger.LogInformation("Retrieved game client information");
                return (true, $"Found {gameStates.Count} active clients", gameStates);
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Failed to retrieve client list: {StatusCode} - {Content}", response.StatusCode, errorContent);
                return (false, $"HTTP {response.StatusCode}: {errorContent}", null);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing active clients");
            return (false, $"Error: {ex.Message}", null);
        }
    }
    
    /// <summary>
    /// Simulate keyboard input to the game via HTTP API
    /// </summary>
    /// <param name="clientId">Game client identifier</param>
    /// <param name="key">Key to press (e.g., "W", "Space", "Enter")</param>
    /// <param name="holdDurationMs">How long to hold the key in milliseconds</param>
    /// <returns>Success status and any error message</returns>
    public async Task<(bool Success, string Message)> SendKeyAsync(string clientId, string key, int holdDurationMs = 50)
    {
        try
        {
            _logger.LogInformation("Attempting to send key '{Key}' to client {ClientId}", key, clientId);

            var input = new
            {
                Type = "KeyPress",
                Data = new
                {
                    key = key,
                    holdDurationMs = holdDurationMs,
                    clientId = clientId
                }
            };

            var response = await _httpClient.PostAsJsonAsync($"{_gameApiUrl}/game/input", input);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<dynamic>();
                _logger.LogInformation("Successfully sent key input to game");
                return (true, $"Key '{key}' sent successfully");
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Failed to send key input: {StatusCode} - {Content}", response.StatusCode, errorContent);
                return (false, $"HTTP {response.StatusCode}: {errorContent}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending key to client {ClientId}", clientId);
            return (false, $"Error: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Dispose of HTTP client resources
    /// </summary>
    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}
