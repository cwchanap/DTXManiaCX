using System;
using System.Collections.Generic;

namespace DTXManiaCX.MCP.Server;

/// <summary>
/// Represents the current state of the game.
/// </summary>
/// <remarks>
/// This type mirrors DTXMania.Game.Lib.GameState (defined in DTXMania.Game/Lib/GameApi.cs) to enable JSON-RPC communication
/// between the MCP server and the game. The MCP project is intentionally kept
/// independent of DTXMania.Game to allow standalone distribution.
/// 
/// When modifying this class, ensure the properties match DTXMania.Game.Lib.GameState
/// to maintain wire-compatibility.
/// </remarks>
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