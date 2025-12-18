using Microsoft.Extensions.Logging;
using DTXManiaCX.MCP.Server.Services;
using DTXManiaCX.MCP.Server.Tools;
using System.Text.Json;

namespace DTXManiaCX.MCP.Server.Console;

/// <summary>
/// Simple console application to test the game interaction tools
/// </summary>
public class GameInteractionTestConsole
{
    private readonly GameInteractionTools _gameTools;
    private readonly ILogger<GameInteractionTestConsole> _logger;
    
    public GameInteractionTestConsole(GameInteractionTools gameTools, ILogger<GameInteractionTestConsole> logger)
    {
        _gameTools = gameTools;
        _logger = logger;
    }
    
    public async Task RunTestsAsync()
    {
        _logger.LogInformation("Starting Game Interaction Tests");
        
        // Test 1: List active clients
        await TestListActiveClients();
        
        // Test 2: Click simulation (commented out for safety - would actually click!)
        // await TestClick();
        
        // Test 3: Get window info
        await TestGetWindowInfo();
        
        _logger.LogInformation("Game Interaction Tests completed");
    }
    
    private async Task TestListActiveClients()
    {
        _logger.LogInformation("Test: List Active Clients");
        
        using var emptyDoc = JsonDocument.Parse("{}");
        var emptyArgs = emptyDoc.RootElement;
        var result = await _gameTools.ExecuteToolAsync("game_list_clients", emptyArgs);
        
        System.Console.WriteLine($"Result: {result.Content}");
        System.Console.WriteLine($"IsError: {result.IsError}");
    }
    
    private async Task TestClick()
    {
        _logger.LogInformation("Test: Click Simulation");
        
        var clickArgs = JsonDocument.Parse(@"{
            ""client_id"": ""test-client"",
            ""x"": 100,
            ""y"": 100,
            ""button"": ""left""
        }").RootElement;
        
        var result = await _gameTools.ExecuteToolAsync("game_click", clickArgs);
        
        System.Console.WriteLine($"Click Result: {result.Content}");
        System.Console.WriteLine($"IsError: {result.IsError}");
    }
    
    private async Task TestGetWindowInfo()
    {
        _logger.LogInformation("Test: Get Window Info");
        
        var windowArgs = JsonDocument.Parse(@"{
            ""client_id"": ""test-client""
        }").RootElement;
        
        var result = await _gameTools.ExecuteToolAsync("game_get_window_info", windowArgs);
        
        System.Console.WriteLine($"Window Info Result: {result.Content}");
        System.Console.WriteLine($"IsError: {result.IsError}");
    }
}
