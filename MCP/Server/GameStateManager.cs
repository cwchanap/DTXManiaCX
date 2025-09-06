using Microsoft.Extensions.Logging;

namespace DTXManiaCX.MCP.Server.Services;

/// <summary>
/// Manages game state data received from MonoGame clients
/// </summary>
public class GameStateManager
{
    private readonly ILogger<GameStateManager> _logger;
    private readonly Dictionary<string, GameState> _gameStates = new();
    private readonly object _lock = new();
    
    public GameStateManager(ILogger<GameStateManager> logger)
    {
        _logger = logger;
    }
    
    /// <summary>
    /// Update game state for a specific client
    /// </summary>
    /// <param name="clientId">Unique identifier for the client</param>
    /// <param name="gameState">The current game state</param>
    public void UpdateGameState(string clientId, GameState gameState)
    {
        lock (_lock)
        {
            _gameStates[clientId] = gameState;
            _logger.LogDebug("Updated game state for client {ClientId}", clientId);
        }
    }
    
    /// <summary>
    /// Get game state for a specific client
    /// </summary>
    /// <param name="clientId">Unique identifier for the client</param>
    /// <returns>Game state or null if not found</returns>
    public GameState? GetGameState(string clientId)
    {
        lock (_lock)
        {
            return _gameStates.TryGetValue(clientId, out var state) ? state : null;
        }
    }
    
    /// <summary>
    /// Get all active game states
    /// </summary>
    /// <returns>Dictionary of all game states</returns>
    public Dictionary<string, GameState> GetAllGameStates()
    {
        lock (_lock)
        {
            return new Dictionary<string, GameState>(_gameStates);
        }
    }
    
    /// <summary>
    /// Process pending game state updates
    /// </summary>
    public async Task ProcessUpdatesAsync()
    {
        var states = GetAllGameStates();
        
        foreach (var (clientId, gameState) in states)
        {
            _logger.LogDebug("Processing game state for client {ClientId}: Position=({X}, {Y}), Score={Score}",
                clientId, gameState.PlayerPositionX, gameState.PlayerPositionY, gameState.Score);
            
            // TODO: Implement actual processing logic
            // This could include AI analysis, game recommendations, etc.
        }
        
        await Task.CompletedTask;
    }
    
    /// <summary>
    /// Remove a client's game state
    /// </summary>
    /// <param name="clientId">Unique identifier for the client</param>
    public void RemoveClient(string clientId)
    {
        lock (_lock)
        {
            if (_gameStates.Remove(clientId))
            {
                _logger.LogInformation("Removed client {ClientId}", clientId);
            }
        }
    }
}
