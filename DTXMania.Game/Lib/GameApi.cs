using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

namespace DTXMania.Game.Lib;

/// <summary>
/// Interface for game API that MCP server can connect to
/// </summary>
public interface IGameApi
{
    /// <summary>
    /// Get the current game state
    /// </summary>
    Task<GameState> GetGameStateAsync();

    /// <summary>
    /// Send input to the game
    /// </summary>
    Task<bool> SendInputAsync(GameInput input);

    /// <summary>
    /// Get game window information
    /// </summary>
    Task<GameWindowInfo> GetWindowInfoAsync();

    /// <summary>
    /// Capture a screenshot of the current frame as a PNG byte array.
    /// This is fulfilled on the game's main thread during the next Draw() call.
    /// </summary>
    Task<byte[]?> TakeScreenshotAsync();

    /// <summary>
    /// Request a stage transition to the named stage.
    /// The actual transition is queued and executed on the game's main Update() thread.
    /// </summary>
    /// <param name="stageName">The stage name (e.g. "Title", "SongSelect"). Case-insensitive.</param>
    Task<bool> ChangeStageAsync(string stageName);

    /// <summary>
    /// Check if game is running
    /// </summary>
    bool IsRunning { get; }
}

/// <summary>
/// Represents the current state of the game
/// </summary>
public class GameState
{
    public float PlayerPositionX { get; set; }
    public float PlayerPositionY { get; set; }
    public int Score { get; set; }
    public int Level { get; set; }
    public string CurrentStage { get; set; } = string.Empty;
    public Dictionary<string, object> CustomData { get; set; } = new();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Represents input to send to the game
/// </summary>
public class GameInput
{
    public InputType Type { get; set; }
    public JsonElement? Data { get; set; }
}

/// <summary>
/// Types of input that can be sent to the game
/// </summary>
public enum InputType
{
    MouseClick,
    MouseMove,
    KeyPress,
    KeyRelease
}

/// <summary>
/// Game window information
/// </summary>
public class GameWindowInfo
{
    public int Width { get; set; }
    public int Height { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public string Title { get; set; } = string.Empty;
    public bool IsVisible { get; set; }
}