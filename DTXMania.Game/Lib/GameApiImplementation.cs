using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Xna.Framework;

namespace DTXMania.Game.Lib;

/// <summary>
/// Implementation of IGameApi for the DTXMania game
/// </summary>
public class GameApiImplementation : IGameApi
{
    private readonly BaseGame _game;
    private readonly object _lock = new object();
    private readonly ILogger<GameApiImplementation>? _logger;

    public GameApiImplementation(BaseGame game, ILogger<GameApiImplementation>? logger = null)
    {
        _game = game ?? throw new ArgumentNullException(nameof(game));
        _logger = logger;
    }

    public bool IsRunning => true; // Game is running if this instance exists

    public async Task<GameState> GetGameStateAsync()
    {
        return await Task.FromResult(GetGameStateSafe());
    }

    private GameState GetGameStateSafe()
    {
        lock (_lock)
        {
            try
            {
                // This is a simplified implementation
                // In a real game, you'd get actual game state from the game logic
                var gameState = new GameState
                {
                    PlayerPositionX = 0, // Would get from actual player position
                    PlayerPositionY = 0, // Would get from actual player position
                    Score = 0, // Would get from actual score
                    Level = 1, // Would get from actual level
                    CurrentStage = _game.StageManager?.CurrentStage?.ToString() ?? "Unknown",
                    CustomData = new Dictionary<string, object>
                    {
                        ["game_name"] = "DTXManiaCX",
                        ["platform"] = Environment.OSVersion.Platform.ToString(),
                        ["config_screen_width"] = _game.ConfigManager?.Config?.ScreenWidth ?? 0,
                        ["config_screen_height"] = _game.ConfigManager?.Config?.ScreenHeight ?? 0
                    },
                    Timestamp = DateTime.UtcNow
                };

                return gameState;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error getting game state");
                // Return safe fallback state if game state access fails
                return new GameState
                {
                    PlayerPositionX = 0,
                    PlayerPositionY = 0,
                    Score = 0,
                    Level = 1,
                    CurrentStage = "Error",
                    CustomData = new Dictionary<string, object>
                    {
                        ["error"] = SanitizeExceptionMessage(ex),
                        ["game_name"] = "DTXManiaCX"
                    },
                    Timestamp = DateTime.UtcNow
                };
            }
        }
    }

    private static string SanitizeExceptionMessage(Exception ex)
    {
        return $"Internal error ({ex.GetType().Name})";
    }

    /// <summary>
    /// Sends input to the game.
    /// </summary>
    /// <remarks>
    /// STUB IMPLEMENTATION: This method currently only logs inputs and does not route them
    /// to the actual game input system. Returns false to indicate the input was not processed.
    /// 
    /// TODO: Integrate with ModularInputManager/InputRouter to route MCP-driven input
    /// through the same path as player input for consistent handling.
    /// </remarks>
    /// <param name="input">The input to send to the game.</param>
    /// <returns>False - input is not currently processed (stub implementation).</returns>
    public async Task<bool> SendInputAsync(GameInput input)
    {
        try
        {
            // STUB: This implementation only logs inputs - it does not route them to the game.
            // MCP clients should be aware that inputs are acknowledged but not acted upon.
            var dataText = input.Data?.GetRawText() ?? "null";
            switch (input.Type)
            {
                case InputType.MouseClick:
                    System.Diagnostics.Debug.WriteLine($"Game API [STUB]: Mouse click input received (not processed): {dataText}");
                    break;

                case InputType.MouseMove:
                    System.Diagnostics.Debug.WriteLine($"Game API [STUB]: Mouse move input received (not processed): {dataText}");
                    break;

                case InputType.KeyPress:
                    System.Diagnostics.Debug.WriteLine($"Game API [STUB]: Key press input received (not processed): {dataText}");
                    break;

                case InputType.KeyRelease:
                    System.Diagnostics.Debug.WriteLine($"Game API [STUB]: Key release input received (not processed): {dataText}");
                    break;

                default:
                    System.Diagnostics.Debug.WriteLine($"Game API [STUB]: Unknown input type: {input.Type}");
                    return false;
            }

            // Return false to indicate the input was not actually processed
            // This is intentional - MCP clients should know the input wasn't acted upon
            return false;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Game API: Error processing input: {ex.Message}");
            return false;
        }
    }

    public async Task<GameWindowInfo> GetWindowInfoAsync()
    {
        return await Task.FromResult(GetWindowInfoSafe());
    }

    private GameWindowInfo GetWindowInfoSafe()
    {
        lock (_lock)
        {
            try
            {
                // This is a simplified implementation
                // In a real game, you'd get actual window information
                var windowInfo = new GameWindowInfo
                {
                    Width = _game.ConfigManager?.Config?.ScreenWidth ?? 1920,
                    Height = _game.ConfigManager?.Config?.ScreenHeight ?? 1080,
                    X = 0, // Would get from actual window position
                    Y = 0, // Would get from actual window position
                    Title = "DTXManiaCX",
                    IsVisible = true
                };

                return windowInfo;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Game API: Error getting window info: {ex.Message}");
                // Return safe fallback window info if access fails
                return new GameWindowInfo
                {
                    Width = 1920,
                    Height = 1080,
                    X = 0,
                    Y = 0,
                    Title = "DTXManiaCX (Error)",
                    IsVisible = false
                };
            }
        }
    }
}