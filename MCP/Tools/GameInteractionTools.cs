using Microsoft.Extensions.Logging;
using DTXManiaCX.MCP.Server.Services;
using System.Text.Json;

namespace DTXManiaCX.MCP.Server.Tools;

/// <summary>
/// MCP tools for interacting with .NET game applications
/// Provides functionality for clicking, dragging, and getting game state information
/// </summary>
public class GameInteractionTools
{
    private readonly ILogger<GameInteractionTools> _logger;
    private readonly GameInteractionService _gameInteractionService;
    private readonly List<ToolDefinition> _toolDefinitions = new();
    private bool _initialized;
    
    public GameInteractionTools(ILogger<GameInteractionTools> logger, GameInteractionService gameInteractionService)
    {
        _logger = logger;
        _gameInteractionService = gameInteractionService;
    }
    
    /// <summary>
    /// Initialize and register all game interaction capabilities
    /// This method sets up the available tool definitions for MCP clients
    /// </summary>
    public void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        _logger.LogInformation("Initializing game interaction tools");
        
        // Define available tools for MCP clients
        _toolDefinitions.Clear();
        _toolDefinitions.AddRange(new[]
        {
            new ToolDefinition
            {
                Name = "game_click",
                Description = "Click at a specific position in a .NET game application window",
                Parameters = new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object>
                    {
                        ["client_id"] = new Dictionary<string, object>
                        {
                            ["type"] = "string",
                            ["description"] = "Game client identifier"
                        },
                        ["x"] = new Dictionary<string, object>
                        {
                            ["type"] = "integer",
                            ["description"] = "X coordinate relative to game window"
                        },
                        ["y"] = new Dictionary<string, object>
                        {
                            ["type"] = "integer",
                            ["description"] = "Y coordinate relative to game window"
                        },
                        ["button"] = new Dictionary<string, object>
                        {
                            ["type"] = "string",
                            ["description"] = "Mouse button to click",
                            ["enum"] = new[] { "left", "right", "middle" },
                            ["default"] = "left"
                        }
                    },
                    ["required"] = new[] { "client_id", "x", "y" }
                }
            },
            
            new ToolDefinition
            {
                Name = "game_drag",
                Description = "Drag from one position to another in a .NET game application window",
                Parameters = new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object>
                    {
                        ["client_id"] = new Dictionary<string, object>
                        {
                            ["type"] = "string",
                            ["description"] = "Game client identifier"
                        },
                        ["start_x"] = new Dictionary<string, object>
                        {
                            ["type"] = "integer",
                            ["description"] = "Starting X coordinate"
                        },
                        ["start_y"] = new Dictionary<string, object>
                        {
                            ["type"] = "integer",
                            ["description"] = "Starting Y coordinate"
                        },
                        ["end_x"] = new Dictionary<string, object>
                        {
                            ["type"] = "integer",
                            ["description"] = "Ending X coordinate"
                        },
                        ["end_y"] = new Dictionary<string, object>
                        {
                            ["type"] = "integer",
                            ["description"] = "Ending Y coordinate"
                        },
                        ["duration_ms"] = new Dictionary<string, object>
                        {
                            ["type"] = "integer",
                            ["description"] = "Duration of the drag in milliseconds",
                            ["default"] = 500
                        }
                    },
                    ["required"] = new[] { "client_id", "start_x", "start_y", "end_x", "end_y" }
                }
            },
            
            new ToolDefinition
            {
                Name = "game_get_state",
                Description = "Get the current game state for a client",
                Parameters = new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object>
                    {
                        ["client_id"] = new Dictionary<string, object>
                        {
                            ["type"] = "string",
                            ["description"] = "Game client identifier"
                        }
                    },
                    ["required"] = new[] { "client_id" }
                }
            },
            
            new ToolDefinition
            {
                Name = "game_get_window_info",
                Description = "Get window dimensions and position for a game client",
                Parameters = new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object>
                    {
                        ["client_id"] = new Dictionary<string, object>
                        {
                            ["type"] = "string",
                            ["description"] = "Game client identifier"
                        }
                    },
                    ["required"] = new[] { "client_id" }
                }
            },
            
            new ToolDefinition
            {
                Name = "game_list_clients",
                Description = "List all active game clients and their states",
                Parameters = new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object>()
                }
            },
            
            new ToolDefinition
            {
                Name = "game_send_key",
                Description = "Send keyboard input to a game window",
                Parameters = new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object>
                    {
                        ["client_id"] = new Dictionary<string, object>
                        {
                            ["type"] = "string",
                            ["description"] = "Game client identifier"
                        },
                        ["key"] = new Dictionary<string, object>
                        {
                            ["type"] = "string",
                            ["description"] = "Key to press (e.g., 'W', 'Space', 'Enter')"
                        },
                        ["hold_duration_ms"] = new Dictionary<string, object>
                        {
                            ["type"] = "integer",
                            ["description"] = "How long to hold the key in milliseconds",
                            ["default"] = 50
                        }
                    },
                    ["required"] = new[] { "client_id", "key" }
                }
            }
        });
        
        _initialized = true;
        _logger.LogInformation("Initialized {Count} game interaction tools", _toolDefinitions.Count);
    }
    
    /// <summary>
    /// Get all available tool definitions
    /// </summary>
    /// <returns>List of tool definitions</returns>
    public IReadOnlyList<ToolDefinition> GetToolDefinitions() => _toolDefinitions;
    
    /// <summary>
    /// Execute a tool call with the given name and arguments
    /// </summary>
    /// <param name="toolName">Name of the tool to execute</param>
    /// <param name="arguments">JSON arguments for the tool</param>
    /// <returns>Tool execution result</returns>
    public async Task<ToolExecutionResult> ExecuteToolAsync(string toolName, JsonElement arguments)
    {
        try
        {
            return toolName switch
            {
                "game_click" => await HandleGameClickAsync(arguments),
                "game_drag" => await HandleGameDragAsync(arguments),
                "game_get_state" => await HandleGetGameStateAsync(arguments),
                "game_get_window_info" => await HandleGetWindowInfoAsync(arguments),
                "game_list_clients" => await HandleListActiveClientsAsync(arguments),
                "game_send_key" => await HandleSendKeyAsync(arguments),
                _ => new ToolExecutionResult
                {
                    IsError = true,
                    Content = $"Unknown tool: {toolName}"
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing tool {ToolName}", toolName);
            return new ToolExecutionResult
            {
                IsError = true,
                Content = $"Error executing tool: {ex.Message}"
            };
        }
    }
    
    private async Task<ToolExecutionResult> HandleGameClickAsync(JsonElement arguments)
    {
        try
        {
            var clientId = arguments.GetProperty("client_id").GetString()
                ?? throw new ArgumentException("client_id cannot be null");
            var x = arguments.GetProperty("x").GetInt32();
            var y = arguments.GetProperty("y").GetInt32();
            var button = arguments.TryGetProperty("button", out var buttonProp) ? 
                buttonProp.GetString() ?? "left" : "left";            
            var (success, message) = await _gameInteractionService.ClickAsync(clientId, x, y, button);
            
            return new ToolExecutionResult
            {
                IsError = !success,
                Content = JsonSerializer.Serialize(new
                {
                    success,
                    message,
                    action = "click",
                    client_id = clientId,
                    coordinates = new { x, y },
                    button
                })
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling game click tool");
            return new ToolExecutionResult
            {
                IsError = true,
                Content = $"Error: {ex.Message}"
            };
        }
    }
    
    private async Task<ToolExecutionResult> HandleGameDragAsync(JsonElement arguments)
    {
        try
        {
            var clientId = arguments.GetProperty("client_id").GetString()
                ?? throw new ArgumentException("client_id cannot be null");
            var startX = arguments.GetProperty("start_x").GetInt32();
            var startY = arguments.GetProperty("start_y").GetInt32();
            var endX = arguments.GetProperty("end_x").GetInt32();
            var endY = arguments.GetProperty("end_y").GetInt32();
            var durationMs = arguments.TryGetProperty("duration_ms", out var durationProp) ? 
                durationProp.GetInt32() : 500;
            
            var (success, message) = await _gameInteractionService.DragAsync(clientId, startX, startY, endX, endY, durationMs);
            
            return new ToolExecutionResult
            {
                IsError = !success,
                Content = JsonSerializer.Serialize(new
                {
                    success,
                    message,
                    action = "drag",
                    client_id = clientId,
                    start_coordinates = new { x = startX, y = startY },
                    end_coordinates = new { x = endX, y = endY },
                    duration_ms = durationMs
                })
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling game drag tool");
            return new ToolExecutionResult
            {
                IsError = true,
                Content = $"Error: {ex.Message}"
            };
        }
    }
    
    private async Task<ToolExecutionResult> HandleGetGameStateAsync(JsonElement arguments)
    {
        try
        {
            var clientId = arguments.GetProperty("client_id").GetString()
                ?? throw new ArgumentException("client_id cannot be null");
            var (success, message, gameState) = await _gameInteractionService.GetGameStateAsync(clientId);
            
            return new ToolExecutionResult
            {
                IsError = !success,
                Content = JsonSerializer.Serialize(new
                {
                    success,
                    message,
                    client_id = clientId,
                    game_state = gameState != null ? new
                    {
                        player_position = new { x = gameState.PlayerPositionX, y = gameState.PlayerPositionY },
                        score = gameState.Score,
                        level = gameState.Level,
                        custom_data = gameState.CustomData
                    } : null
                })
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling get game state tool");
            return new ToolExecutionResult
            {
                IsError = true,
                Content = $"Error: {ex.Message}"
            };
        }
    }
    
    private async Task<ToolExecutionResult> HandleGetWindowInfoAsync(JsonElement arguments)
    {
        try
        {
            var clientId = arguments.GetProperty("client_id").GetString()
                ?? throw new ArgumentException("client_id cannot be null");
            var (success, message, windowRect) = await _gameInteractionService.GetWindowInfoAsync(clientId);
            
            return new ToolExecutionResult
            {
                IsError = !success,
                Content = JsonSerializer.Serialize(new
                {
                    success,
                    message,
                    client_id = clientId,
                    window_info = windowRect != null ? new
                    {
                        left = windowRect.Left,
                        top = windowRect.Top,
                        right = windowRect.Right,
                        bottom = windowRect.Bottom,
                        width = windowRect.Width,
                        height = windowRect.Height
                    } : null
                })
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling get window info tool");
            return new ToolExecutionResult
            {
                IsError = true,
                Content = $"Error: {ex.Message}"
            };
        }
    }
    
    private async Task<ToolExecutionResult> HandleListActiveClientsAsync(JsonElement arguments)
    {
        try
        {
            // Note: This currently returns a single default client's state.
            // Multi-client support would require game API enhancements.
            var (success, message, gameStates) = await _gameInteractionService.GetDefaultClientStateAsync();
            
            return new ToolExecutionResult
            {
                IsError = !success,
                Content = JsonSerializer.Serialize(new
                {
                    success,
                    message,
                    client_count = gameStates?.Count ?? 0,
                    clients = gameStates?.ToDictionary(
                        kvp => kvp.Key,
                        kvp => new
                        {
                            player_position = new { x = kvp.Value.PlayerPositionX, y = kvp.Value.PlayerPositionY },
                            score = kvp.Value.Score,
                            level = kvp.Value.Level,
                            custom_data = kvp.Value.CustomData
                        }
                    )
                })
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling list active clients tool");
            return new ToolExecutionResult
            {
                IsError = true,
                Content = $"Error: {ex.Message}"
            };
        }
    }
    
    private async Task<ToolExecutionResult> HandleSendKeyAsync(JsonElement arguments)
    {
        try
        {
            var clientId = arguments.GetProperty("client_id").GetString()
                ?? throw new ArgumentException("client_id cannot be null");
            var key = arguments.GetProperty("key").GetString()
                ?? throw new ArgumentException("key cannot be null");
            var holdDurationMs = arguments.TryGetProperty("hold_duration_ms", out var durationProp) ? 
                durationProp.GetInt32() : 50;
            
            var (success, message) = await _gameInteractionService.SendKeyAsync(clientId, key, holdDurationMs);
            
            return new ToolExecutionResult
            {
                IsError = !success,
                Content = JsonSerializer.Serialize(new
                {
                    success,
                    message,
                    action = "send_key",
                    client_id = clientId,
                    key,
                    hold_duration_ms = holdDurationMs
                })
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling send key tool");
            return new ToolExecutionResult
            {
                IsError = true,
                Content = $"Error: {ex.Message}"
            };
        }
    }
}

/// <summary>
/// Represents a tool definition for MCP clients
/// </summary>
public class ToolDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public Dictionary<string, object> Parameters { get; set; } = new();
}

/// <summary>
/// Represents the result of executing a tool
/// </summary>
public class ToolExecutionResult
{
    public bool IsError { get; set; }
    public string Content { get; set; } = string.Empty;
}
