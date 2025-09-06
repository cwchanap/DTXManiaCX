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
    public Dictionary<string, object> CustomData { get; set; } = new();
}