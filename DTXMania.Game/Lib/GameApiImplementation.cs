using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using System.Text.Json;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Stage;
using DTXMania.Game.Lib.Input;

namespace DTXMania.Game.Lib;

public interface IGameContext
{
    IStageManager? StageManager { get; }
    IConfigManager? ConfigManager { get; }
    IInputManagerCompat? InputManager { get; }
}

/// <summary>
/// Implementation of IGameApi for the DTXMania game
/// </summary>
public class GameApiImplementation : IGameApi
{
    private readonly IGameContext _game;
    private readonly object _lock = new object();
    private readonly ILogger<GameApiImplementation>? _logger;

    public GameApiImplementation(IGameContext game, ILogger<GameApiImplementation>? logger = null)
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

    private static string? ParseButtonId(JsonElement? data)
    {
        if (!data.HasValue)
        {
            return null;
        }

        var element = data.Value;
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                var str = element.GetString();
                if (string.IsNullOrWhiteSpace(str))
                    return null;

                // Normalize casing via Keys enum so it matches KeyBindings (case-sensitive).
                var trimmed = str.StartsWith("Key.", StringComparison.OrdinalIgnoreCase) ? str["Key.".Length..] : str;
                if (Enum.TryParse<Keys>(trimmed, true, out var parsedKey))
                {
                    return $"Key.{parsedKey}";
                }
                return null;

            case JsonValueKind.Number:
                if (element.TryGetInt32(out var keyCode))
                {
                    var keyName = ((Keys)keyCode).ToString();
                    return $"Key.{keyName}";
                }
                return null;

            default:
                return null;
        }
    }

    /// <summary>
    /// Sends input to the game.
    /// </summary>
    /// <remarks>
    /// TODO: Integrate with ModularInputManager/InputRouter to route MCP-driven input
    /// through the same path as player input for consistent handling.
    /// </remarks>
    /// <param name="input">The input to send to the game.</param>
    public async Task<bool> SendInputAsync(GameInput input)
    {
        try
        {
            if (input == null)
            {
                return false;
            }

            var inputManager = _game.InputManager;
            var modularInput = inputManager?.ModularInputManager;

            if (modularInput == null)
            {
                System.Diagnostics.Debug.WriteLine("Game API: InputManager/ModularInputManager unavailable - cannot route MCP input");
                return false;
            }

            switch (input.Type)
            {
                case InputType.KeyPress:
                case InputType.KeyRelease:
                {
                    var buttonId = ParseButtonId(input.Data);
                    if (string.IsNullOrWhiteSpace(buttonId))
                    {
                        System.Diagnostics.Debug.WriteLine("Game API: Missing or invalid key data for MCP input");
                        return false;
                    }

                    var pressed = input.Type == InputType.KeyPress;
                    return modularInput.InjectButton(buttonId, pressed);
                }

                case InputType.MouseClick:
                case InputType.MouseMove:
                    // TODO: When mouse routing is added, translate to UI input. For now, report unsupported.
                    System.Diagnostics.Debug.WriteLine($"Game API: Mouse input not yet supported ({input.Type})");
                    return false;

                default:
                    System.Diagnostics.Debug.WriteLine($"Game API: Unknown input type: {input.Type}");
                    return false;
            }
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