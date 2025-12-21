using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.Json;
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
        if (clientId is null)
        {
            throw new ArgumentNullException(nameof(clientId));
        }

        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new ArgumentException("clientId cannot be empty or whitespace.", nameof(clientId));
        }

        if (gameState is null)
        {
            throw new ArgumentNullException(nameof(gameState));
        }

        lock (_lock)
        {
            _gameStates[clientId] = CloneGameState(gameState);
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
        if (clientId is null)
        {
            throw new ArgumentNullException(nameof(clientId));
        }

        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new ArgumentException("clientId cannot be empty or whitespace.", nameof(clientId));
        }

        lock (_lock)
        {
            return _gameStates.TryGetValue(clientId, out var state) ? CloneGameState(state) : null;
        }
    }
    
    /// <summary>
    /// Get all active game states
    /// </summary>
    /// <returns>Read-only dictionary of all game states. Values are snapshots; callers should not cache references.</returns>
    public IReadOnlyDictionary<string, GameState> GetAllGameStates()
    {
        lock (_lock)
        {
            // Return a new dictionary as IReadOnlyDictionary to prevent caller modifications
            // Note: GameState objects are still mutable; callers should treat them as snapshots
            var snapshot = new Dictionary<string, GameState>(_gameStates.Count);
            foreach (var (clientId, state) in _gameStates)
            {
                snapshot[clientId] = CloneGameState(state);
            }

            return snapshot;
        }
    }

    private static GameState CloneGameState(GameState source)
    {
        return new GameState
        {
            PlayerPositionX = source.PlayerPositionX,
            PlayerPositionY = source.PlayerPositionY,
            Score = source.Score,
            Level = source.Level,
            // Keep CurrentStage non-null for consumers that expect a string (intentional normalization)
            CurrentStage = source.CurrentStage ?? string.Empty,
            CustomData = source.CustomData is null
                ? new Dictionary<string, object>()
                : DeepCloneCustomData(source.CustomData),
            Timestamp = source.Timestamp,
        };
    }

    private static Dictionary<string, object> DeepCloneCustomData(Dictionary<string, object> source)
    {
        if (source.Count == 0)
            return new Dictionary<string, object>();

        var clone = new Dictionary<string, object>(source.Count);
        foreach (var kvp in source)
        {
            var clonedValue = CloneValue(kvp.Value);
            if (clonedValue.isSupported)
            {
                clone[kvp.Key] = clonedValue.value!;
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"GameStateManager: Skipping unsupported custom data value for key '{kvp.Key}' (type {kvp.Value?.GetType().FullName ?? "null"}).");
            }
        }

        return clone;
    }
    
    private static (bool isSupported, object? value) CloneValue(object? value)
    {
        if (value is null)
            return (true, null);

        switch (value)
        {
            // Primitives and common value types
            case string s:
                return (true, s);
            case bool b:
                return (true, b);
            case int i:
                return (true, i);
            case long l:
                return (true, l);
            case double d:
                return (true, d);
            case float f:
                return (true, f);
            case decimal dec:
                return (true, dec);
            case short sh:
                return (true, sh);
            case byte by:
                return (true, by);
            case sbyte sb:
                return (true, sb);
            case uint ui:
                return (true, ui);
            case ulong ul:
                return (true, ul);
            case ushort us:
                return (true, us);
            case char c:
                return (true, c);
            case DateTime dt:
                return (true, dt);
            case DateTimeOffset dto:
                return (true, dto);
            case Guid guid:
                return (true, guid);
            case Enum e:
                return (true, e);

            // Nested dictionary
            case Dictionary<string, object> dict:
                return (true, DeepCloneCustomData(dict));
            case IDictionary<string, object> idict:
                return (true, DeepCloneCustomData(new Dictionary<string, object>(idict)));

            // Lists/arrays
            case IList list:
            {
                var clonedList = new List<object?>(list.Count);
                foreach (var item in list)
                {
                    var clonedItem = CloneValue(item);
                    if (clonedItem.isSupported)
                    {
                        clonedList.Add(clonedItem.value);
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"GameStateManager: Skipping unsupported list item (type {item?.GetType().FullName ?? "null"}).");
                    }
                }
                return (true, clonedList);
            }

            default:
                return (false, null);
        }
    }
    
    /// <summary>
    /// Process pending game state updates
    /// </summary>
    public Task ProcessUpdatesAsync(CancellationToken cancellationToken)
    {
        var states = GetAllGameStates();
        
        foreach (var (clientId, gameState) in states)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                _logger.LogDebug("Processing game state for client {ClientId}: Position=({X}, {Y}), Score={Score}",
                    clientId, gameState.PlayerPositionX, gameState.PlayerPositionY, gameState.Score);
                
                // TODO: Implement actual processing logic
                // This could include AI analysis, game recommendations, etc.
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing game state for client {ClientId}", clientId);
            }
        }

        return Task.CompletedTask;
    }
    
    /// <summary>
    /// Remove a client's game state
    /// </summary>
    /// <param name="clientId">Unique identifier for the client</param>
    public void RemoveClient(string clientId)
    {
        if (clientId is null)
        {
            throw new ArgumentNullException(nameof(clientId));
        }

        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new ArgumentException("clientId cannot be empty or whitespace.", nameof(clientId));
        }

        lock (_lock)
        {
            if (_gameStates.Remove(clientId))
            {
                _logger.LogInformation("Removed client {ClientId}", clientId);
            }
        }
    }
}
