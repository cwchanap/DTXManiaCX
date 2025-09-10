using System;
using System.Collections.Generic;

namespace DTXManiaCX.MCP.Server;

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